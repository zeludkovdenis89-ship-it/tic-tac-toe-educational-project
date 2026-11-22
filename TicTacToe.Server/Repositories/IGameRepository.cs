using System.Collections.Generic;
using TicTacToe.Server.Game;

namespace TicTacToe.Server.Repositories;

public interface IGameRepository
{
    bool TryAdd(GameSession session);
    bool TryGet(string sessionId, out GameSession? session);
    bool TryRemove(string sessionId, out GameSession? session);
    IReadOnlyCollection<GameSession> GetAll();
    void AddPlayerIndex(string playerId, GameSession session);
    void RemovePlayerIndex(string playerId);
    bool TryGetByPlayer(string playerId, out GameSession? session);
}
