using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TicTacToe.Server.Game;
using TicTacToe.Server.Logging;
using TicTacToe.Server.Networking;
using TicTacToe.Server.Repositories;
using TicTacToe.Server.Services;

namespace TicTacToe.Server;

internal sealed class Program
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(30);
    private readonly IGameRepository _repository;
    private readonly IPlayerService _playerService;
    private readonly Logger _logger;

    private Program(IGameRepository repository, IPlayerService playerService, Logger logger)
    {
        _repository = repository;
        _playerService = playerService;
        _logger = logger;
    }

    // Composition root: зависимости создаются один раз и передаются серверу через конструктор.
    private static async Task Main()
    {
        IGameRepository repository = new InMemoryGameRepository();
        IPlayerService playerService = new PlayerService();
        var logger = new Logger();
        var server = new Program(repository, playerService, logger);
        await server.RunAsync();
    }

    private async Task RunAsync()
    {
        // Порт и способ запуска сервера оставлены без изменений.
        var listener = new TcpListener(IPAddress.Any, 5000);
        listener.Start();
        Console.WriteLine("Сервер запущен на порту 5000...");
        Console.WriteLine("Ожидание подключений...");
        _logger.LogEvent("Сервер запущен на порту 5000");

        while (true)
        {
            var tcpClient = await listener.AcceptTcpClientAsync();
            Console.WriteLine($"Подключился клиент: {tcpClient.Client.RemoteEndPoint}");
            _ = Task.Run(() => HandleClientAsync(tcpClient));
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient)
    {
        var stream = tcpClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        string? playerId = null;
        GameSession? session = null;

        try
        {
            while (tcpClient.Connected)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;

                NetMessage msg;
                try
                {
                    msg = NetMessage.Deserialize(line);
                    if (!Enum.IsDefined(typeof(MsgType), msg.Type))
                        throw new FormatException("Неизвестный тип сообщения.");
                }
                catch (Exception ex)
                {
                    _logger.LogEvent($"Некорректное JSON-сообщение: {ex.Message}");
                    await writer.WriteLineAsync(new NetMessage
                    {
                        Type = MsgType.Error,
                        Text = "Некорректное сообщение"
                    }.Serialize());
                    continue;
                }

                switch (msg.Type)
                {
                    case MsgType.CreateSession:
                    {
                        if (session != null)
                        {
                            await writer.WriteLineAsync(new NetMessage { Type = MsgType.Error, Text = "Вы уже состоите в сессии" }.Serialize());
                            break;
                        }

                        session = new GameSession();
                        var player = new PlayerConnection { Writer = writer, Client = tcpClient };
                        session.Players.Add(player);
                        playerId = player.PlayerId;

                        _repository.TryAdd(session);
                        _repository.AddPlayerIndex(playerId, session);
                        _logger.LogEvent($"Создана сессия {session.SessionId}; подключён игрок {playerId}");
                        await _playerService.SendAsync(player, new NetMessage
                        {
                            Type = MsgType.CreateSessionOk,
                            SessionId = session.SessionId,
                            PlayerId = player.PlayerId
                        });
                        Console.WriteLine($"Создана сессия: {session.SessionId[..8]}");
                        break;
                    }

                    case MsgType.ListSessions:
                    {
                        var list = new System.Collections.Generic.List<SessionInfo>();
                        foreach (var availableSession in _repository.GetAll())
                        {
                            await availableSession.Gate.WaitAsync();
                            try
                            {
                                if (!availableSession.IsTerminating &&
                                    availableSession.Players.Count < 2 &&
                                    availableSession.Status == "Waiting")
                                {
                                    list.Add(new SessionInfo
                                    {
                                        SessionId = availableSession.SessionId,
                                        PlayersCount = availableSession.Players.Count,
                                        Status = availableSession.Status
                                    });
                                }
                            }
                            finally { availableSession.Gate.Release(); }
                        }

                        await writer.WriteLineAsync(new NetMessage
                        {
                            Type = MsgType.SessionList,
                            Sessions = list
                        }.Serialize());
                        break;
                    }

                    case MsgType.JoinSession:
                    {
                        if (session != null || string.IsNullOrWhiteSpace(msg.SessionId) ||
                            !_repository.TryGet(msg.SessionId, out var found) || found == null)
                        {
                            await writer.WriteLineAsync(new NetMessage { Type = MsgType.Error, Text = "Сессия не найдена" }.Serialize());
                            break;
                        }

                        var joiningSession = found;
                        await joiningSession.Gate.WaitAsync();
                        try
                        {
                            if (joiningSession.IsTerminating || joiningSession.Status != "Waiting" || joiningSession.Players.Count >= 2)
                            {
                                await writer.WriteLineAsync(new NetMessage { Type = MsgType.Error, Text = "Сессия заполнена" }.Serialize());
                                break;
                            }

                            session = joiningSession;
                            var player = new PlayerConnection { Writer = writer, Client = tcpClient };
                            session.Players.Add(player);
                            playerId = player.PlayerId;
                            _repository.AddPlayerIndex(playerId, session);
                            _logger.LogEvent($"Игрок {playerId} подключился к сессии {session.SessionId}");

                            await _playerService.SendAsync(player, new NetMessage
                            {
                                Type = MsgType.JoinSessionOk,
                                SessionId = session.SessionId,
                                PlayerId = player.PlayerId
                            });

                            if (session.Players.Count == 2)
                            {
                                session.Status = "MiniGame";
                                await _playerService.BroadcastAsync(session, new NetMessage { Type = MsgType.MiniGameStart });
                                _logger.LogEvent($"Сессия {session.SessionId}: подключились оба игрока, начата мини-игра");
                                Console.WriteLine($"Сессия {session.SessionId[..8]} заполнена. Запуск мини-игры.");
                            }
                        }
                        finally { joiningSession.Gate.Release(); }
                        break;
                    }

                    case MsgType.MiniGameChoice:
                    {
                        if (session == null || playerId == null) break;
                        await session.Gate.WaitAsync();
                        try
                        {
                            var player = session.GetPlayer(playerId);
                            if (player == null || session.Status != "MiniGame") break;
                            if (msg.Choice is not ("Heads" or "Tails"))
                            {
                                await _playerService.SendAsync(player, new NetMessage { Type = MsgType.Error, Text = "Выберите орёл или решку" });
                                break;
                            }

                            if (!string.IsNullOrEmpty(player.MiniGameChoice))
                                break;

                            var opponent = session.Players.FirstOrDefault(p => p.PlayerId != playerId);
                            if (opponent == null) break;

                            if (opponent.MiniGameChoice == msg.Choice)
                            {
                                // Даже если второй игрок успел нажать до получения уведомления,
                                // сервер сообщает ему выбор соперника, чтобы заблокировать эту кнопку.
                                await _playerService.SendAsync(player, new NetMessage
                                {
                                    Type = MsgType.MiniGameChoiceUpdate,
                                    PlayerId = opponent.PlayerId,
                                    Choice = opponent.MiniGameChoice
                                });
                                break;
                            }

                            player.MiniGameChoice = msg.Choice;
                            await _playerService.SendAsync(opponent, new NetMessage
                            {
                                Type = MsgType.MiniGameChoiceUpdate,
                                PlayerId = playerId,
                                Choice = msg.Choice
                            });

                            if (session.Players.Count == 2 && session.Players.All(p => !string.IsNullOrEmpty(p.MiniGameChoice)))
                            {
                                await session.StartMainGameAsync(_playerService);
                                _logger.LogEvent($"Сессия {session.SessionId}: мини-игра завершена, первый ход игрока {session.CurrentTurnPlayerId}");
                                StartTurnTimeout(session);
                            }
                        }
                        finally { session.Gate.Release(); }
                        break;
                    }

                    case MsgType.Move:
                    {
                        if (session == null || playerId == null) break;
                        await session.Gate.WaitAsync();
                        try
                        {
                            var player = session.GetPlayer(playerId);
                            if (player == null || msg.X == null || msg.Y == null) break;

                            if (!GameLogic.ValidateMove(session, playerId, msg.X.Value, msg.Y.Value, out string reason))
                            {
                                await _playerService.SendAsync(player, new NetMessage { Type = MsgType.MoveRejected, Reason = reason });
                                _logger.LogEvent($"Отклонён ход игрока {playerId} в сессии {session.SessionId}: {reason}");
                                break;
                            }

                            int x = msg.X.Value;
                            int y = msg.Y.Value;
                            session.Board[y, x] = player.Symbol;
                            _logger.LogEvent($"Ход: сессия {session.SessionId}; игрок {playerId}; символ {player.Symbol}; клетка ({x},{y}); время {DateTime.Now:HH:mm:ss}");

                            var otherPlayer = session.Players.FirstOrDefault(p => p.PlayerId != playerId);
                            if (otherPlayer != null)
                                session.CurrentTurnPlayerId = otherPlayer.PlayerId;

                            await _playerService.BroadcastAsync(session, new NetMessage
                            {
                                Type = MsgType.StateUpdate,
                                Board = session.GetBoardArray(),
                                CurrentTurn = session.CurrentTurnPlayerId
                            });

                            string winner = GameLogic.CheckWinner(session.Board);
                            bool draw = winner.Length == 0 && GameLogic.IsDraw(session.Board);

                            if (winner.Length > 0 || draw)
                            {
                                session.CancelTurnTimeout();
                                session.Status = "Finished";
                                if (draw)
                                {
                                    _logger.LogEvent($"Результат игры: сессия {session.SessionId}; ничья");
                                }
                                else
                                {
                                    string winnerId = session.Players.FirstOrDefault(p => p.Symbol == winner)?.PlayerId ?? "";
                                    _logger.LogEvent($"Результат игры: сессия {session.SessionId}; победил игрок {winnerId} ({winner})");
                                }

                                // Небольшая пауза сохраняет прежний визуальный порядок: последний ход,
                                // затем сообщение GAME_OVER с индексами выигрышной линии.
                                await Task.Delay(350);
                                if (draw)
                                {
                                    await _playerService.BroadcastAsync(session, new NetMessage
                                    {
                                        Type = MsgType.GameOver,
                                        Result = "Draw"
                                    });
                                }
                                else
                                {
                                    var winnerPlayer = session.Players.FirstOrDefault(p => p.Symbol == winner);
                                    await _playerService.BroadcastAsync(session, new NetMessage
                                    {
                                        Type = MsgType.GameOver,
                                        Result = "Win",
                                        WinnerId = winnerPlayer?.PlayerId,
                                        Board = session.GetWinLine().Select(i => i.ToString()).ToArray()
                                    });
                                }
                            }
                            else
                            {
                                StartTurnTimeout(session);
                            }
                        }
                        finally { session.Gate.Release(); }
                        break;
                    }

                    case MsgType.LeaveSession:
                    {
                        if (session == null || playerId == null) break;

                        var leavingSession = session;
                        var leavingPlayerId = playerId;
                        await leavingSession.Gate.WaitAsync();
                        try
                        {
                            var leavingPlayer = leavingSession.GetPlayer(leavingPlayerId);
                            if (leavingPlayer == null) break;

                            _logger.LogEvent($"Игрок {leavingPlayerId} добровольно вышел из сессии {leavingSession.SessionId}");
                            leavingSession.Players.Remove(leavingPlayer);
                            _repository.RemovePlayerIndex(leavingPlayerId);
                            leavingSession.CancelTurnTimeout();
                            leavingSession.ClearBoard();
                            leavingSession.CurrentTurnPlayerId = "";
                            leavingSession.Status = "Waiting";
                            leavingSession.IsTerminating = false;

                            foreach (var remaining in leavingSession.Players)
                            {
                                remaining.Symbol = "";
                                remaining.MiniGameChoice = null;
                            }

                            if (leavingSession.Players.Count > 0)
                            {
                                await _playerService.BroadcastAsync(leavingSession, new NetMessage
                                {
                                    Type = MsgType.OpponentLeft,
                                    Text = "Соперник вышел из игры. Ожидаем нового игрока…"
                                });
                            }
                            else
                            {
                                _repository.TryRemove(leavingSession.SessionId, out _);
                            }

                            await _playerService.SendAsync(leavingPlayer, new NetMessage
                            {
                                Type = MsgType.LeaveSessionOk
                            });
                        }
                        finally { leavingSession.Gate.Release(); }

                        // TCP-соединение остаётся открытым: игрок возвращается в лобби на том же соединении.
                        session = null;
                        playerId = null;
                        break;
                    }

                    case MsgType.ResetSession:
                    {
                        if (session == null || playerId == null) break;
                        await session.Gate.WaitAsync();
                        try
                        {
                            if (session.IsTerminating || session.Players.Count != 2)
                                break;

                            // Второй запрос от соперника после уже выполненного сброса считается повторным
                            // и не должен прерывать новую мини-игру или показывать ложную ошибку.
                            if (session.Status == "MiniGame")
                                break;

                            if (session.Status != "Finished")
                            {
                                var player = session.GetPlayer(playerId);
                                if (player != null)
                                    await _playerService.SendAsync(player, new NetMessage { Type = MsgType.Error, Text = "Новая партия доступна после завершения текущей игры" });
                                break;
                            }

                            // RESET_SESSION полностью сбрасывает состояние и запускает мини-игру заново.
                            session.PrepareRematch();
                            await _playerService.BroadcastAsync(session, new NetMessage { Type = MsgType.MiniGameStart });
                            _logger.LogEvent($"Сессия {session.SessionId}: получен RESET_SESSION; поле очищено, запущена новая мини-игра");
                        }
                        finally { session.Gate.Release(); }
                        break;
                    }

                    case MsgType.ChatMessage:
                    {
                        if (session == null || playerId == null || string.IsNullOrWhiteSpace(msg.Text)) break;
                        if (msg.Text.Length > 200)
                        {
                            var player = session.GetPlayer(playerId);
                            if (player != null)
                                await _playerService.SendAsync(player, new NetMessage { Type = MsgType.Error, Text = "Ограничение 200 символов" });
                            break;
                        }

                        await _playerService.BroadcastAsync(session, new NetMessage
                        {
                            Type = MsgType.ChatMessage,
                            PlayerId = playerId,
                            Text = msg.Text
                        });
                        break;
                    }
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogEvent($"Обрыв связи с клиентом {playerId ?? "неизвестен"}: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Соединение закрыто сервером при таймауте или отключении второго игрока.
        }
        catch (Exception ex)
        {
            _logger.LogEvent($"Ошибка обработки клиента {playerId ?? "неизвестен"}: {ex}");
            Console.WriteLine($"Ошибка клиента: {ex.Message}");
        }
        finally
        {
            if (session != null && playerId != null)
                await DisconnectHandlerAsync(session, playerId, "Клиент отключился");

            tcpClient.Close();
        }
    }

    private void StartTurnTimeout(GameSession session)
    {
        session.CancelTurnTimeout();
        var timeoutCts = new CancellationTokenSource();
        session.TurnTimeoutCts = timeoutCts;
        string expectedPlayerId = session.CurrentTurnPlayerId;
        _ = WatchTurnTimeoutAsync(session, expectedPlayerId, timeoutCts);
    }

    private async Task WatchTurnTimeoutAsync(GameSession session, string expectedPlayerId, CancellationTokenSource timeoutCts)
    {
        try
        {
            await Task.Delay(TurnTimeout, timeoutCts.Token);
            await session.Gate.WaitAsync();
            try
            {
                if (session.IsTerminating ||
                    session.Status != "Game" ||
                    session.CurrentTurnPlayerId != expectedPlayerId ||
                    !ReferenceEquals(session.TurnTimeoutCts, timeoutCts))
                    return;

                _logger.LogEvent($"Таймаут хода: игрок {expectedPlayerId} не сделал ход за 30 секунд; сессия {session.SessionId} завершена");
                session.Status = "Finished";
                session.IsTerminating = true;
                session.CancelTurnTimeout();

                await _playerService.BroadcastAsync(session, new NetMessage
                {
                    Type = MsgType.Error,
                    Text = "Таймаут ожидания хода: игрок не сделал ход за 30 секунд. Сессия завершена."
                });

                foreach (var player in session.Players)
                {
                    _repository.RemovePlayerIndex(player.PlayerId);
                    try { player.Client?.Close(); } catch { }
                }
                // Сессию удалит DisconnectHandler после закрытия обоих клиентских соединений.
            }
            finally { session.Gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task DisconnectHandlerAsync(GameSession session, string playerId, string reason)
    {
        await session.Gate.WaitAsync();
        try
        {
            var disconnected = session.GetPlayer(playerId);
            if (disconnected == null) return;

            _logger.LogEvent($"Отключение игрока {playerId} от сессии {session.SessionId}: {reason}");
            session.Players.Remove(disconnected);
            _repository.RemovePlayerIndex(playerId);

            if (!session.IsTerminating)
            {
                session.IsTerminating = true;
                session.Status = "Finished";
                session.CancelTurnTimeout();
                _logger.LogEvent($"Сессия {session.SessionId} завершена из-за отключения игрока {playerId}");

                await _playerService.BroadcastAsync(session, new NetMessage
                {
                    Type = MsgType.Error,
                    Text = "Соперник вышел из игры."
                });

                // Второму участнику сообщаем причину и закрываем соединение с завершённой сессией.
                foreach (var remaining in session.Players)
                {
                    _repository.RemovePlayerIndex(remaining.PlayerId);
                    try { remaining.Client?.Close(); } catch { }
                }
            }

            if (session.Players.Count == 0)
            {
                _repository.TryRemove(session.SessionId, out _);
                _logger.LogEvent($"Сессия {session.SessionId} удалена из памяти: оба игрока отключены");
            }
        }
        finally { session.Gate.Release(); }
    }
}
