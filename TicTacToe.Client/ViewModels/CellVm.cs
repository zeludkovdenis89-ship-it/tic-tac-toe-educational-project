using System.Windows.Input;

namespace TicTacToe.Client.ViewModels;

public sealed class CellVm : ViewModelBase
{
    public int X { get; init; }
    public int Y { get; init; }

    private string _symbol = "";
    public string Symbol
    {
        get => _symbol;
        set
        {
            Set(ref _symbol, value);
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool IsEmpty => string.IsNullOrEmpty(Symbol);
    public string Display => Symbol switch { "X" => "✕", "O" => "◯", _ => "" };

    public ICommand? ClickCommand { get; set; }
}