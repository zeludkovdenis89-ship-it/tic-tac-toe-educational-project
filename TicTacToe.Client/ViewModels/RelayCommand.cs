using System.Windows.Input;

namespace TicTacToe.Client.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _exec;
    private readonly Func<bool>? _can;

    public RelayCommand(Func<Task> exec, Func<bool>? can = null)
    {
        _exec = exec;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => _can?.Invoke() ?? true;

    public async void Execute(object? p)
    {
        try { await _exec(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _exec;
    private readonly Predicate<T>? _can;

    public RelayCommand(Func<T, Task> exec, Predicate<T>? can = null)
    {
        _exec = exec;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => p is T t && (_can?.Invoke(t) ?? true);

    public async void Execute(object? p)
    {
        if (p is T t)
        {
            try { await _exec(t); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}