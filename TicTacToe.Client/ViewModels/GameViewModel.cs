using System.Collections.ObjectModel;
using System.Windows.Input;
using TicTacToe.Client.Models;
using TicTacToe.Client.Networking;

namespace TicTacToe.Client.ViewModels;

public sealed class GameViewModel : ViewModelBase
{
    private readonly TcpClientService _net;
    private string _playerId = "";

    public ObservableCollection<CellVm> Board { get; } = new();
    public ObservableCollection<ChatMessage> Chat { get; } = new();

    private bool _isMyTurn;
    public bool IsMyTurn { get => _isMyTurn; private set => Set(ref _isMyTurn, value); }

    private bool _isInputLocked;
    public bool IsInputLocked
    {
        get => _isInputLocked;
        private set { Set(ref _isInputLocked, value); OnPropertyChanged(nameof(CanClick)); }
    }
    public bool CanClick => IsMyTurn && !IsInputLocked;

    private string _statusText = "Ожидание игры…";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private string _mySymbol = "";
    public string MySymbol { get => _mySymbol; private set => Set(ref _mySymbol, value); }

    private string _chatInput = "";
    public string ChatInput { get => _chatInput; set => Set(ref _chatInput, value); }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set { Set(ref _error, value); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(Error);

    // Свойства для отрисовки линии победы
    private bool _showWinLine;
    public bool ShowWinLine { get => _showWinLine; private set => Set(ref _showWinLine, value); }

    private double _lineX1, _lineY1, _lineX2, _lineY2;
    public double LineX1 { get => _lineX1; private set => Set(ref _lineX1, value); }
    public double LineY1 { get => _lineY1; private set => Set(ref _lineY1, value); }
    public double LineX2 { get => _lineX2; private set => Set(ref _lineX2, value); }
    public double LineY2 { get => _lineY2; private set => Set(ref _lineY2, value); }

    private bool _canRestart;
    public bool CanRestart { get => _canRestart; private set => Set(ref _canRestart, value); }

    public ICommand SendChatCommand { get; }
    public ICommand ResetSessionCommand { get; }
    public ICommand LeaveSessionCommand { get; }

    public GameViewModel(TcpClientService net)
    {
        _net = net;

        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                var cell = new CellVm { X = x, Y = y };
                cell.ClickCommand = new RelayCommand(() => MoveAsync(cell));
                Board.Add(cell);
            }

        SendChatCommand = new RelayCommand(SendChatAsync);
        ResetSessionCommand = new RelayCommand(RequestRematchAsync);
        LeaveSessionCommand = new RelayCommand(LeaveSessionAsync);
    }

    public void InitPlayer(string playerId) => _playerId = playerId;
    public void SetError(string message)
    {
        Error = message;
    }
    public void InitGameState(NetMessage msg)
    {
        if (msg.Board is { Length: 9 })
        {
            for (int i = 0; i < 9; i++)
                Board[i].Symbol = msg.Board[i] ?? "";
        }

        if (!string.IsNullOrEmpty(msg.Symbol)) MySymbol = msg.Symbol!;

        IsMyTurn = msg.CurrentTurn == _playerId;
        IsInputLocked = false;
        StatusText = IsMyTurn ? "Ваш ход" : "Ход соперника";
        ShowWinLine = false;
        CanRestart = false;
    }

    private async Task MoveAsync(CellVm cell)
    {
        if (!CanClick || !cell.IsEmpty) return;

        IsInputLocked = true;
        Error = null;
        try
        {
            await _net.SendAsync(new NetMessage
            {
                Type = MsgType.Move,
                PlayerId = _playerId,
                X = cell.X,
                Y = cell.Y
            });
        }
        catch (Exception ex) { Error = ex.Message; IsInputLocked = false; }
    }

    internal void Apply(NetMessage msg)
    {
        if (msg.Type == MsgType.MoveRejected)
        {
            Error = msg.Reason ?? "Ход отклонён";
            IsInputLocked = false;
            return;
        }

        if (msg.Board is { Length: 9 })
        {
            for (int i = 0; i < 9; i++)
                Board[i].Symbol = msg.Board[i] ?? "";

            if (!string.IsNullOrEmpty(msg.Symbol)) MySymbol = msg.Symbol!;
            IsMyTurn = msg.CurrentTurn == _playerId;
            IsInputLocked = false;

            StatusText = IsMyTurn ? "Ваш ход" : "Ход соперника";
        }

        if (msg.Type == MsgType.MoveOk) IsInputLocked = false;
    }

    internal void ApplyGameOver(NetMessage msg)
    {
        IsInputLocked = true;
        IsMyTurn = false;
        CanRestart = true;

        if (msg.Result == "Win" && msg.Board != null && msg.Board.Length == 3)
        {
            // msg.Board хранит 3 индекса выигрышных клеток (например, "0", "1", "2")
            if (int.TryParse(msg.Board[0], out int i1) &&
                int.TryParse(msg.Board[1], out int i2) &&
                int.TryParse(msg.Board[2], out int i3))
            {
                // Вычисляем координаты для линии. Размер клетки 100 + отступы по 6 с каждой стороны = 112.
                // Координаты центров клеток: 56, 168, 280.
                // Верхний левый угол сетки: (6, 6).

                double[] centersX = { 56, 168, 280 };
                double[] centersY = { 56, 168, 280 };

                LineX1 = centersX[i1 % 3];
                LineY1 = centersY[i1 / 3];
                LineX2 = centersX[i3 % 3];
                LineY2 = centersY[i3 / 3];

                ShowWinLine = true;
            }
        }

        StatusText = msg.Result switch
        {
            "Win" => msg.WinnerId == _playerId ? "🏆 Вы победили!" : "Поражение.",
            "Draw" => "Ничья.",
            _ => "Партия завершена."
        };
    }

    internal void AppendChat(NetMessage msg)
    {
        Chat.Add(new ChatMessage
        {
            Author = msg.PlayerId ?? "?",
            Text = msg.Text ?? "",
            Timestamp = DateTime.Now
        });
    }

    private async Task SendChatAsync()
    {
        var text = ChatInput.Trim();
        if (text.Length == 0) return;
        if (text.Length > 200) { Error = "Ограничение 200 символов"; return; }

        try
        {
            await _net.SendAsync(new NetMessage
            {
                Type = MsgType.ChatMessage,
                Text = text,
                PlayerId = _playerId
            });
            ChatInput = "";
            Error = null;
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private async Task LeaveSessionAsync()
    {
        Error = null;
        try
        {
            await _net.SendAsync(new NetMessage
            {
                Type = MsgType.LeaveSession,
                PlayerId = _playerId
            });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public void OnOpponentLeft(string message)
    {
        foreach (var cell in Board) cell.Symbol = "";
        IsInputLocked = true;
        IsMyTurn = false;
        CanRestart = false;
        ShowWinLine = false;
        StatusText = string.IsNullOrWhiteSpace(message)
            ? "Соперник вышел. Ожидание нового игрока…"
            : message;
    }

    private async Task RequestRematchAsync()
    {
        if (!CanRestart) return;

        CanRestart = false;
        Error = null;
        StatusText = "Запуск новой партии…";
        try
        {
            await _net.SendAsync(new NetMessage { Type = MsgType.ResetSession, PlayerId = _playerId });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            CanRestart = true;
        }
    }

    public void SetServerError(string message)
    {
        Error = message;
        IsInputLocked = true;
        IsMyTurn = false;
    }

    public void ResetBoard()
    {
        foreach (var c in Board) c.Symbol = "";
        ShowWinLine = false;
        CanRestart = false;
        StatusText = "Ожидание новой партии…";
        IsInputLocked = false;
        IsMyTurn = false;
    }
}