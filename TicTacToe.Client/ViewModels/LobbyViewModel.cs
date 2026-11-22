using System.Collections.ObjectModel;
using System.Windows.Input;
using TicTacToe.Client.Networking;

namespace TicTacToe.Client.ViewModels;

public sealed class LobbyViewModel : ViewModelBase
{
    private readonly TcpClientService _net;

    public ObservableCollection<SessionInfo> Sessions { get; } = new();

    private string _host = "127.0.0.1";
    public string Host { get => _host; set => Set(ref _host, value); }

    private int _port = 5000;
    public int Port { get => _port; set => Set(ref _port, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set
        {
            Set(ref _error, value);
            OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrEmpty(Error);

    public ICommand CreateSessionCommand { get; }
    public ICommand RefreshSessionsCommand { get; }
    public ICommand JoinSessionCommand { get; }
    public ICommand ConnectCommand { get; }

    public event Action<string, string>? SessionJoined;
    public event Func<Task>? ConnectRequested;

    public LobbyViewModel(TcpClientService net)
    {
        _net = net;
        CreateSessionCommand = new RelayCommand(CreateSessionAsync);
        RefreshSessionsCommand = new RelayCommand(RefreshSessionsAsync);
        JoinSessionCommand = new RelayCommand<SessionInfo>(JoinSessionAsync);
        ConnectCommand = new RelayCommand(async () =>
        {
            if (ConnectRequested is not null) await ConnectRequested.Invoke();
        });
    }

    public async Task RefreshSessionsAsync()
    {
        IsBusy = true;
        ClearError();
        try { await _net.SendAsync(new NetMessage { Type = MsgType.ListSessions }); }
        catch (Exception ex) { SetError(ex.Message); }
        finally { IsBusy = false; }
    }

    public async Task CreateSessionAsync()
    {
        IsBusy = true;
        ClearError();
        try { await _net.SendAsync(new NetMessage { Type = MsgType.CreateSession }); }
        catch (Exception ex) { SetError(ex.Message); }
        finally { IsBusy = false; }
    }

    public async Task JoinSessionAsync(SessionInfo session)
    {
        IsBusy = true;
        ClearError();
        try
        {
            await _net.SendAsync(new NetMessage
            {
                Type = MsgType.JoinSession,
                SessionId = session.SessionId
            });
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { IsBusy = false; }
    }

    internal void OnSessionList(NetMessage msg)
    {
        Sessions.Clear();
        if (msg.Sessions is not null)
            foreach (var s in msg.Sessions) Sessions.Add(s);
        if (Sessions.Count == 0)
            SetError("Свободных сессий нет. Создайте новую.");
    }

    internal void OnSessionCreated(NetMessage msg)
        => SessionJoined?.Invoke(msg.SessionId ?? "", msg.PlayerId ?? "");

    internal void OnJoined(NetMessage msg)
        => SessionJoined?.Invoke(msg.SessionId ?? "", msg.PlayerId ?? "");

    public void SetError(string message) => Error = message;
    public void ClearError() => Error = null;
}