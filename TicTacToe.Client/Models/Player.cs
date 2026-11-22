namespace TicTacToe.Client.Models;

public sealed class Player
{
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";      // "X" | "O"
    public bool IsReady { get; set; }
}