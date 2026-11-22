using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TicTacToe.Server.Game;

namespace TicTacToe.Server.Repositories;

/// <summary>Хранилище сессий в памяти, соответствующее модели DAL проекта.</summary>
public sealed class InMemoryGameRepository : IGameRepository
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
    private readonly ConcurrentDictionary<string, GameSession> _playerSessions = new();

    public bool TryAdd(GameSession session) => _sessions.TryAdd(session.SessionId, session);
    public bool TryGet(string sessionId, out GameSession? session) => _sessions.TryGetValue(sessionId, out session);
    public bool TryRemove(string sessionId, out GameSession? session) => _sessions.TryRemove(sessionId, out session);
    public IReadOnlyCollection<GameSession> GetAll() => _sessions.Values.ToArray();
    public void AddPlayerIndex(string playerId, GameSession session) => _playerSessions[playerId] = session;
    public void RemovePlayerIndex(string playerId) => _playerSessions.TryRemove(playerId, out _);
    public bool TryGetByPlayer(string playerId, out GameSession? session) => _playerSessions.TryGetValue(playerId, out session);
}
