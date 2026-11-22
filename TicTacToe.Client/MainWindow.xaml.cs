using System.Windows;
using TicTacToe.Client.ViewModels;

namespace TicTacToe.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                await vm.ConnectAsync(vm.Lobby.Host, vm.Lobby.Port);
            }
        };
    }
}