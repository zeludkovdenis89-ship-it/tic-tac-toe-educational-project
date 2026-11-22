using TicTacToe.Client.Networking;

namespace TicTacToe.Client.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly TcpClientService _net = new();

    public LobbyViewModel Lobby { get; }
    public MiniGameViewModel MiniGame { get; }
    public GameViewModel Game { get; }

    private ViewModelBase _current;
    public ViewModelBase Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string PlayerId { get; private set; } = "";
    public string SessionId { get; private set; } = "";

    public MainViewModel()
    {
        Lobby = new LobbyViewModel(_net);
        MiniGame = new MiniGameViewModel(_net);
        Game = new GameViewModel(_net);
        _current = Lobby;

        _net.MessageReceived += OnMessage;
        _net.ConnectionLost += OnConnectionLost;

        Lobby.SessionJoined += (sessionId, playerId) =>
        {
            SessionId = sessionId;
            PlayerId = playerId;
            Game.InitPlayer(playerId);
            Current = Game;
        };

        MiniGame.Finished += () => Current = Game;
    }

    public async Task ConnectAsync(string host, int port)
    {
        try
        {
            await _net.ConnectAsync(host, port);
            Lobby.ClearError();
            await Lobby.RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            Lobby.SetError($"Не удалось подключиться: {ex.Message}");
        }
    }

    private void OnMessage(NetMessage msg)
    {
        switch (msg.Type)
        {
            case MsgType.CreateSessionOk:
                SessionId = msg.SessionId ?? SessionId;
                PlayerId = msg.PlayerId ?? PlayerId;
                Game.InitPlayer(PlayerId);
                Lobby.OnSessionCreated(msg);
                break;

            case MsgType.SessionList:
                Lobby.OnSessionList(msg);
                break;

            case MsgType.JoinSessionOk:
                SessionId = msg.SessionId ?? SessionId;
                PlayerId = msg.PlayerId ?? PlayerId;
                Game.InitPlayer(PlayerId);
                Lobby.OnJoined(msg);
                break;

            case MsgType.MiniGameStart:
                Game.ResetBoard();
                MiniGame.Reset();
                Current = MiniGame;
                break;

            case MsgType.MiniGameResult:
                MiniGame.ApplyResult(msg, PlayerId);
                Game.InitGameState(msg);
                break;

            case MsgType.MiniGameChoiceUpdate:
                MiniGame.ApplyOpponentChoice(msg);
                break;

            case MsgType.LeaveSessionOk:
                SessionId = "";
                PlayerId = "";
                Game.ResetBoard();
                Current = Lobby;
                _ = Lobby.RefreshSessionsAsync();
                break;

            case MsgType.OpponentLeft:
                Lobby.SetError(msg.Text ?? "Соперник покинул игру.");
                Game.ResetBoard();
                Current = Lobby;
                _ = Lobby.RefreshSessionsAsync();
                break;

            case MsgType.MoveOk:
            case MsgType.MoveRejected:
            case MsgType.StateUpdate:
                Game.Apply(msg);
                break;

            case MsgType.GameOver:
                Game.ApplyGameOver(msg);
                break;

            case MsgType.ChatMessage:
                Game.AppendChat(msg);
                break;

            case MsgType.Error:
                string errorText = msg.Text ?? "Ошибка сервера";

                if (errorText.Contains("Таймаут") ||
                    errorText.Contains("Соперник") ||
                    errorText.Contains("Сессия завершена") ||
                    errorText.Contains("покинул игру"))
                {
                    Lobby.SetError(errorText);
                    Game.ResetBoard();
                    Current = Lobby;
                    _ = Lobby.RefreshSessionsAsync();
                }
                else
                {
                    if (Current == Game)
                        Game.SetServerError(errorText);
                    else if (Current == MiniGame)
                        MiniGame.SetError(errorText);
                    else
                        Lobby.SetError(errorText);
                }
                break;
        }
    }

    private void OnConnectionLost(string reason)
    {
        string message = $"Соединение потеряно: {reason}";
        if (Current == Game)
            Game.SetServerError(message);
        else
            Lobby.SetError(message);
    }
}