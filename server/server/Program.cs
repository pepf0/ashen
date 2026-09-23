using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace ashen;

public class Program
{
    static string jsonsFolder = "../../json";
    public static Dictionary<string, Item> Item => ItemLoader.ItemsFromJson(File.ReadAllText(jsonsFolder + "/items.json"))
        .ToDictionary(item => item.Id);
    public static Dictionary<string, Effect> Effects => ItemLoader.EffectsFromJson(File.ReadAllText(jsonsFolder + "/effects.json"))
        .ToDictionary(effect => effect.Id);
    public static async Task Main(string[] args)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5067/");
        listener.Start();
        Console.WriteLine("Listening on ws://localhost:5067/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                _ = HandleConnectionAsync(wsContext.WebSocket);
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    private static async Task HandleConnectionAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"Received: {text}");

            var response = Encoding.UTF8.GetBytes($"Echo: {text}");
            await socket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}