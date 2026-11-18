using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed class Peer(WebSocket socket, IPAddress address)
{
    readonly Channel<string> outbound = Channel.CreateBounded<string>(32);
    public IPAddress Address { get; } = address;
    public void Queue(string message) { if (!outbound.Writer.TryWrite(message)) Abort(); }
    public void Abort() => socket.Abort();
    public async Task Run(Stage stage, CancellationToken stopping)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var token = cts.Token;
        var writing = Task.Run(async () =>
        {
            try
            {
                await foreach (string message in outbound.Reader.ReadAllAsync(token))
                    await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, token);
            }
            finally { await cts.CancelAsync(); }
        }, token);
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var bytes = new MemoryStream();
                int limit = 8192;
                WebSocketReceiveResult fragment;
                do
                {
                    fragment = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (fragment.MessageType == WebSocketMessageType.Close) return;
                    if (bytes.Length == 0) lock (stage.Sync) limit = stage.PacketLimit(this);
                    if (fragment.MessageType != WebSocketMessageType.Text || bytes.Length + fragment.Count > limit)
                    { await socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "Unsupported packet", token); return; }
                    bytes.Write(buffer, 0, fragment.Count);
                } while (!fragment.EndOfMessage);
                var message = Proto.Parse(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
                lock (stage.Sync) stage.Message(this, message);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        finally
        {
            lock (stage.Sync) stage.Lost(this);
            outbound.Writer.TryComplete(); await cts.CancelAsync();
            try { await writing; } catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        }
    }
}
