using System;
using System.IO;

namespace TicTacToe.Server.Logging;

/// <summary>Потокобезопасная запись событий сервера в game_log.txt.</summary>
public sealed class Logger
{
    private static readonly object Sync = new();
    private readonly string _logPath;

    public Logger(string? logPath = null)
    {
        _logPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "game_log.txt");
    }

    public void LogEvent(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
        lock (Sync)
        {
            try
            {
                File.AppendAllText(_logPath, line);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Не удалось записать game_log.txt: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Нет доступа к game_log.txt: {ex.Message}");
            }
        }
    }
}
