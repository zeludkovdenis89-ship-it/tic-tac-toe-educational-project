using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TicTacToe.Server.Networking;
using TicTacToe.Server.Services;

namespace TicTacToe.Server.Game;

public sealed class PlayerConnection
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = "";
    public StreamWriter Writer { get; set; } = null!;
    public TcpClient? Client { get; set; }
    public SemaphoreSlim SendGate { get; } = new(1, 1);
    public string? MiniGameChoice { get; set; }
}

public sealed class GameSession
{
    public string SessionId { get; } = Guid.NewGuid().ToString();
    public List<PlayerConnection> Players { get; } = new();
    public string[,] Board { get; } = new string[3, 3];
    public string CurrentTurnPlayerId { get; set; } = "";
    public string Status { get; set; } = "Waiting";
    public bool IsTerminating { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public CancellationTokenSource? TurnTimeoutCts { get; set; }

    public PlayerConnection? GetPlayer(string playerId)
        => Players.FirstOrDefault(p => p.PlayerId == playerId);

    public string[] GetBoardArray()
    {
        var arr = new string[9];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                arr[y * 3 + x] = Board[y, x] ?? "";
        return arr;
    }

    public int[] GetWinLine() => GameLogic.GetWinningLine(Board);

    public void ClearBoard()
    {
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Board[y, x] = "";
    }

    public void PrepareRematch()
    {
        CancelTurnTimeout();
        ClearBoard();
        CurrentTurnPlayerId = "";
        Status = "MiniGame";
        foreach (var player in Players)
        {
            player.Symbol = "";
            player.MiniGameChoice = null;
        }
    }

    public void CancelTurnTimeout()
    {
        var cts = TurnTimeoutCts;
        TurnTimeoutCts = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    public async Task StartMainGameAsync(IPlayerService playerService)
    {
        // Ищем того, кто сделал выбор первым
        var firstPlayer = Players.FirstOrDefault(p => !string.IsNullOrEmpty(p.MiniGameChoice));
        if (firstPlayer == null) return;

        var secondPlayer = Players.FirstOrDefault(p => p.PlayerId != firstPlayer.PlayerId);
        if (secondPlayer == null) return;

        // Игроки должны явно выбрать разные стороны; сервер дополнительно проверяет это.
        if (string.IsNullOrEmpty(secondPlayer.MiniGameChoice) ||
            firstPlayer.MiniGameChoice == secondPlayer.MiniGameChoice)
            return;

        // Подбрасываем монету (50/50)
        string coinResult = Random.Shared.Next(0, 2) == 0 ? "Heads" : "Tails";

        // Определяем победителя жеребьевки
        PlayerConnection winner;
        PlayerConnection loser;

        if (firstPlayer.MiniGameChoice == coinResult)
        {
            winner = firstPlayer;
            loser = secondPlayer;
        }
        else
        {
            winner = secondPlayer;
            loser = firstPlayer;
        }

        // Победитель получает нолики и ходит первым
        winner.Symbol = "O";
        loser.Symbol = "X";

        CurrentTurnPlayerId = winner.PlayerId;
        Status = "Game";
        ClearBoard();

        // Отправляем результат обоим игрокам
        foreach (var player in Players)
        {
            await playerService.SendAsync(player, new NetMessage
            {
                Type = MsgType.MiniGameResult,
                Symbol = player.Symbol,
                CurrentTurn = CurrentTurnPlayerId,
                Choice = coinResult, 
                Board = GetBoardArray()
            });
        }
    }
}
