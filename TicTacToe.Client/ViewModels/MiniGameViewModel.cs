using System.Windows.Input;
using TicTacToe.Client.Networking;

namespace TicTacToe.Client.ViewModels;

public sealed class MiniGameViewModel : ViewModelBase
{
    private readonly TcpClientService _net;

    private bool _choiceMade;
    public bool ChoiceMade
    {
        get => _choiceMade;
        private set
        {
            if (Set(ref _choiceMade, value))
            {
                OnPropertyChanged(nameof(CanChooseHeads));
                OnPropertyChanged(nameof(CanChooseTails));
            }
        }
    }

    private string? _opponentChoice;
    public string? OpponentChoice
    {
        get => _opponentChoice;
        private set
        {
            if (Set(ref _opponentChoice, value))
            {
                OnPropertyChanged(nameof(CanChooseHeads));
                OnPropertyChanged(nameof(CanChooseTails));
            }
        }
    }

    public bool CanChooseHeads => !ChoiceMade && OpponentChoice != "Heads";
    public bool CanChooseTails => !ChoiceMade && OpponentChoice != "Tails";

    private string _status = "Сделайте выбор: орёл или решка";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _mySymbol = "";
    public string MySymbol { get => _mySymbol; private set => Set(ref _mySymbol, value); }

    private string _myTurnInfo = "";
    public string MyTurnInfo { get => _myTurnInfo; private set => Set(ref _myTurnInfo, value); }

    public ICommand ChooseHeadsCommand { get; }
    public ICommand ChooseTailsCommand { get; }
    public ICommand LeaveSessionCommand { get; }

    public event Action? Finished;

    public MiniGameViewModel(TcpClientService net)
    {
        _net = net;
        ChooseHeadsCommand = new RelayCommand(() => ChooseAsync("Heads"));
        ChooseTailsCommand = new RelayCommand(() => ChooseAsync("Tails"));
        LeaveSessionCommand = new RelayCommand(LeaveSessionAsync);
    }

    private async Task ChooseAsync(string choice)
    {
        if (ChoiceMade) return;
        ChoiceMade = true;
        Status = $"Вы выбрали «{Translate(choice)}». Ожидание соперника…";
        try { await _net.SendAsync(new NetMessage { Type = MsgType.MiniGameChoice, Choice = choice }); }
        catch (Exception ex) { Status = $"Ошибка: {ex.Message}"; }
    }

    public void ApplyOpponentChoice(NetMessage msg)
    {
        if (msg.Choice is not ("Heads" or "Tails")) return;

        OpponentChoice = msg.Choice;
        if (!ChoiceMade)
            Status = $"Соперник выбрал «{Translate(msg.Choice)}». Выберите другой вариант.";
    }

    public void SetError(string message) => Status = message;

    public void OnOpponentLeft()
    {
        ChoiceMade = true;
        OpponentChoice = null;
        Status = "Соперник вышел. Ожидание нового игрока…";
    }

    private async Task LeaveSessionAsync()
    {
        try
        {
            await _net.SendAsync(new NetMessage { Type = MsgType.LeaveSession });
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
    }

    public void ApplyResult(NetMessage msg, string playerId)
    {
        MySymbol = msg.Symbol == "X" ? "Крестики ✕" : "Нолики ◯";
        MyTurnInfo = msg.CurrentTurn == playerId ? "Вы ходите первым" : "Вы ходите вторым";
        Status = $"Монета: {Translate(msg.Choice ?? "")}. Ваша фигура: {MySymbol}";
        Task.Delay(1500).ContinueWith(_ => Finished?.Invoke());
    }

    public void Reset()
    {
        ChoiceMade = false;
        OpponentChoice = null;
        Status = "Сделайте выбор: орёл или решка";
        MySymbol = "";
        MyTurnInfo = "";
    }

    private static string Translate(string c) => c == "Heads" ? "орёл" : "решка";
}