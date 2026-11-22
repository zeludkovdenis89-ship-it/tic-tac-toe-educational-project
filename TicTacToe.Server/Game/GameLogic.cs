using System;

namespace TicTacToe.Server.Game;

/// <summary>Единая серверная бизнес-логика правил крестиков-ноликов.</summary>
public static class GameLogic
{
    public static bool ValidateMove(GameSession session, string playerId, int x, int y)
        => ValidateMove(session, playerId, x, y, out _);

    public static bool ValidateMove(GameSession session, string playerId, int x, int y, out string reason)
    {
        if (session.Status != "Game")
        {
            reason = "Игра не активна";
            return false;
        }

        if (session.CurrentTurnPlayerId != playerId)
        {
            reason = "Не ваша очередь";
            return false;
        }

        if (x < 0 || x > 2 || y < 0 || y > 2)
        {
            reason = "Ход вне поля";
            return false;
        }

        if (!string.IsNullOrEmpty(session.Board[y, x]))
        {
            reason = "Занятая клетка";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>Возвращает "X", "O" или пустую строку, если победителя нет.</summary>
    public static string CheckWinner(string[,] board)
    {
        if (board.GetLength(0) != 3 || board.GetLength(1) != 3)
            throw new ArgumentException("Игровое поле должно иметь размер 3×3.", nameof(board));

        for (int i = 0; i < 3; i++)
        {
            if (!string.IsNullOrEmpty(board[i, 0]) &&
                board[i, 0] == board[i, 1] && board[i, 1] == board[i, 2])
                return board[i, 0];

            if (!string.IsNullOrEmpty(board[0, i]) &&
                board[0, i] == board[1, i] && board[1, i] == board[2, i])
                return board[0, i];
        }

        if (!string.IsNullOrEmpty(board[0, 0]) &&
            board[0, 0] == board[1, 1] && board[1, 1] == board[2, 2])
            return board[0, 0];

        if (!string.IsNullOrEmpty(board[0, 2]) &&
            board[0, 2] == board[1, 1] && board[1, 1] == board[2, 0])
            return board[0, 2];

        return "";
    }

    public static bool IsDraw(string[,] board)
    {
        if (board.GetLength(0) != 3 || board.GetLength(1) != 3)
            throw new ArgumentException("Игровое поле должно иметь размер 3×3.", nameof(board));

        if (!string.IsNullOrEmpty(CheckWinner(board)))
            return false;

        foreach (string? cell in board)
            if (string.IsNullOrEmpty(cell))
                return false;

        return true;
    }

    /// <summary>Возвращает индексы трёх клеток победной линии для клиента.</summary>
    public static int[] GetWinningLine(string[,] board)
    {
        for (int i = 0; i < 3; i++)
        {
            if (!string.IsNullOrEmpty(board[i, 0]) &&
                board[i, 0] == board[i, 1] && board[i, 1] == board[i, 2])
                return new[] { i * 3, i * 3 + 1, i * 3 + 2 };

            if (!string.IsNullOrEmpty(board[0, i]) &&
                board[0, i] == board[1, i] && board[1, i] == board[2, i])
                return new[] { i, i + 3, i + 6 };
        }

        if (!string.IsNullOrEmpty(board[0, 0]) &&
            board[0, 0] == board[1, 1] && board[1, 1] == board[2, 2])
            return new[] { 0, 4, 8 };

        if (!string.IsNullOrEmpty(board[0, 2]) &&
            board[0, 2] == board[1, 1] && board[1, 1] == board[2, 0])
            return new[] { 2, 4, 6 };

        return Array.Empty<int>();
    }
}
