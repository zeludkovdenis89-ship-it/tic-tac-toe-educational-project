using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TicTacToe.Server.Game;
using TicTacToe.Server.Networking;

namespace TicTacToe.Server.Services;

/// <summary>Единая точка отправки JSON-сообщений клиентам.</summary>
public sealed class PlayerService : IPlayerService
{
    public async Task SendAsync(PlayerConnection player, NetMessage message)
    {
        await player.SendGate.WaitAsync();
        try
        {
            await player.Writer.WriteLineAsync(message.Serialize());
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Обрыв соединения будет обработан DisconnectHandler в Program.
        }
        finally
        {
            player.SendGate.Release();
        }
    }

    public async Task BroadcastAsync(GameSession session, NetMessage message)
    {
        foreach (var player in session.Players.ToArray())
            await SendAsync(player, message);
    }
}
