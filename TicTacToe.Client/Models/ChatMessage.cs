namespace TicTacToe.Client.Models;

public sealed class ChatMessage
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}