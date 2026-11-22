using System.Threading.Tasks;
using TicTacToe.Server.Game;
using TicTacToe.Server.Networking;

namespace TicTacToe.Server.Services;

public interface IPlayerService
{
    Task SendAsync(PlayerConnection player, NetMessage message);
    Task BroadcastAsync(GameSession session, NetMessage message);
}
