using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Ashen;

/// <summary>One browser's WebSocket. Sends are queued so game code never awaits the network.</summary>
public class Connection
{
    public WebSocket Socket { get; }
    public User User { get; set; }
    private readonly Channel<string> outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public Connection(WebSocket socket, User user)
    {
        Socket = socket;
        User = user;
    }

    public void Send(object message) => outbox.Writer.TryWrite(JsonSerializer.Serialize(message, Protocol.Json));

    public void Complete() => outbox.Writer.TryComplete();

    public async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var text in outbox.Reader.ReadAllAsync(ct))
            {
                if (Socket.State != WebSocketState.Open) break;
                await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException) { }
    }
}
