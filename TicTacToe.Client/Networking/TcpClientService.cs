using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TicTacToe.Client.Networking;

public sealed class TcpClientService : IAsyncDisposable
{
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public event Action<NetMessage>? MessageReceived;
    public event Action<string>? ConnectionLost;

    public bool IsConnected => _tcp?.Connected == true;

    public async Task ConnectAsync(string host, int port)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port);
        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        _cts = new CancellationTokenSource();
        _ = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendAsync(NetMessage msg)
    {
        if (_writer is null || !IsConnected)
            throw new InvalidOperationException("Нет соединения с сервером.");
        await _writer.WriteLineAsync(msg.Serialize());
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (_cts is { IsCancellationRequested: false })
            {
                var line = await _reader!.ReadLineAsync(_cts.Token);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                NetMessage msg;
                try { msg = NetMessage.Deserialize(line); }
                catch { continue; }

                // Обновляем интерфейс WPF только из главного потока
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageReceived?.Invoke(msg);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ConnectionLost?.Invoke(ex.Message));
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() => ConnectionLost?.Invoke("Соединение закрыто сервером."));
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        if (_writer is not null) await _writer.DisposeAsync();
        _reader?.Dispose();
        _tcp?.Dispose();
    }
}