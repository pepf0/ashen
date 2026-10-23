using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

namespace Ashen;

public class Program
{
    public static async Task Main(string[] args)
    {
        var root = GameData.FindRoot();
        var dbPath = Environment.GetEnvironmentVariable("ASHEN_DB") ?? Path.Combine(root, "data", "ashen.db");
        GameData.OverrideDir = Environment.GetEnvironmentVariable("ASHEN_DATA_OVERRIDES")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? root, "json-overrides");
        GameData.Load(root);
        Admin.Usernames = (Environment.GetEnvironmentVariable("ASHEN_ADMINS") ?? "pepf")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
            builder.WebHost.UseUrls("http://0.0.0.0:5067");
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = Protocol.Json.PropertyNamingPolicy;
            foreach (var c in Protocol.Json.Converters) o.SerializerOptions.Converters.Add(c);
        });
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            // The game runs behind nginx. X-Forwarded-For is only trusted from the proxies listed in
            // ASHEN_TRUSTED_PROXIES (comma separated), so rate limits see real client IPs but can't be spoofed.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var trusted = Environment.GetEnvironmentVariable("ASHEN_TRUSTED_PROXIES") ?? "";
            foreach (var entry in trusted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (System.Net.IPAddress.TryParse(entry, out var ip)) o.KnownProxies.Add(ip);
        });

        var app = builder.Build();
        var database = new Database(dbPath);
        BalanceLog.Directory = Environment.GetEnvironmentVariable("ASHEN_LOGS")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? root, "logs");
        var lobbies = new LobbyManager(database);
        var limiter = new RateLimiter();
        _ = lobbies.SweepAsync(app.Lifetime.ApplicationStopping);
        _ = Admin.ApplyPendingReloadsAsync(lobbies, app.Lifetime.ApplicationStopping);

        app.UseForwardedHeaders();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        // Admin-edited data files (json-overrides) win over the repository's json/.
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (path.StartsWith("/json/") && GameData.Files.Contains(path[6..]) && GameData.OverrideDir is { } dir
                && File.Exists(Path.Combine(dir, path[6..])))
            {
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.CacheControl = "no-cache";
                await ctx.Response.SendFileAsync(Path.Combine(dir, path[6..]));
                return;
            }
            await next();
        });

        // Only the frontend folders are served, never the server source or the database.
        app.MapGet("/", () => Results.File(Path.Combine(root, "index.html"), "text/html; charset=utf-8"));
        foreach (var folder in new[] { "css", "js", "img", "json" })
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(root, folder)),
                RequestPath = "/" + folder,
                OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
            });
        }

        MapApi(app, database, lobbies, limiter);

        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await GameSocket.HandleAsync(socket, database, lobbies, context.RequestAborted);
        });

        Console.WriteLine($"Ashen: serving {root}, database {dbPath}");
        await app.RunAsync();
    }

    private record Credentials(string? Username, string? Password);
    private record DisplayNameBody(string? DisplayName);
    private record PasswordBody(string? OldPassword, string? NewPassword);
    private record FriendRequestBody(string? Username);
    private record FriendRespondBody(long UserId, bool Accept);

    private static User? Authenticate(HttpContext ctx, Database db)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ") ? db.UserForToken(header["Bearer ".Length..].Trim()) : null;
    }

    private static IResult Error(string message, int status = 400) =>
        Results.Json(new { error = message }, statusCode: status);

    private static void MapApi(WebApplication app, Database db, LobbyManager lobbies, RateLimiter limiter)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/register", (Credentials body, HttpContext ctx) =>
        {
            if (!limiter.Allow("register:" + ctx.Connection.RemoteIpAddress, 20, TimeSpan.FromHours(1)))
                return Error("Too many accounts created. Try again later.", 429);
            var username = body.Username?.Trim();
            if (Database.ValidateUsername(username) is { } ue) return Error(ue);
            if (Database.ValidatePassword(body.Password) is { } pe) return Error(pe);
            var user = db.Register(username!, body.Password!);
            if (user is null) return Error("That username is taken.");
            return Results.Json(new { token = db.CreateSession(user.Id), user });
        });

        api.MapPost("/auth/login", (Credentials body, HttpContext ctx) =>
        {
            var key = "login:" + ctx.Connection.RemoteIpAddress;
            if (!limiter.Check(key, 10, TimeSpan.FromMinutes(10)))
                return Error("Too many failed logins. Try again in a few minutes.", 429);
            var user = db.Login(body.Username?.Trim() ?? "", body.Password ?? "");
            if (user is null)
            {
                limiter.Hit(key);
                return Error("Wrong username or password.", 401);
            }
            return Results.Json(new { token = db.CreateSession(user.Id), user });
        });

        api.MapPost("/auth/logout", (HttpContext ctx) =>
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ")) db.DeleteSession(header["Bearer ".Length..].Trim());
            return Results.Json(new { ok = true });
        });

        api.MapGet("/me", (HttpContext ctx) =>
            Authenticate(ctx, db) is { } user
                ? Results.Json(new { user.Id, user.Username, user.DisplayName, isAdmin = Admin.IsAdmin(user) })
                : Error("Not logged in.", 401));

        Admin.Map(api, db, lobbies);

        api.MapPut("/me", (DisplayNameBody body, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            var name = Database.CleanDisplayName(body.DisplayName);
            if (name is null) return Error("Display names are 1-20 characters.");
            db.SetDisplayName(user.Id, name);
            lobbies.LobbyOf(user.Id)?.RenameMember(user.Id, name);
            GameSocket.Rename(user.Id, name);
            return Results.Json(user with { DisplayName = name });
        });

        api.MapPut("/me/password", (PasswordBody body, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            if (Database.ValidatePassword(body.NewPassword) is { } pe) return Error(pe);
            return db.ChangePassword(user.Id, body.OldPassword ?? "", body.NewPassword!)
                ? Results.Json(new { ok = true })
                : Error("Your current password is wrong.", 401);
        });

        api.MapGet("/me/keybinds", (HttpContext ctx) =>
            Authenticate(ctx, db) is { } user ? Results.Json(db.GetKeybinds(user.Id)) : Error("Not logged in.", 401));

        api.MapPut("/me/keybinds", (Dictionary<string, string> body, HttpContext ctx) =>
            Authenticate(ctx, db) is { } user ? Results.Json(db.SetKeybinds(user.Id, body)) : Error("Not logged in.", 401));

        api.MapGet("/me/locker", (HttpContext ctx) =>
            Authenticate(ctx, db) is { } user ? Results.Json(db.GetLocker(user.Id)) : Error("Not logged in.", 401));

        api.MapPut("/me/locker", (Dictionary<string, string?> body, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            db.SetEquipped(user.Id, body);
            lobbies.LobbyOf(user.Id)?.RefreshAppearance(user.Id);
            return Results.Json(db.GetLocker(user.Id));
        });

        api.MapGet("/profile/{id:long}", (long id) =>
            db.Profile(id) is { } profile ? Results.Json(profile) : Error("Player not found.", 404));

        api.MapGet("/achievements", () => Results.Json(new
        {
            achievements = GameData.Achievements,
            rarity = db.AchievementRarity()
        }));

        api.MapGet("/friends", (HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            return Results.Json(db.Friends(user.Id).Select(f =>
            {
                var lobby = lobbies.LobbyOf(f.UserId);
                bool online = GameSocket.IsOnline(f.UserId);
                string presence = !online ? "offline" : lobby is null ? "online" : lobby.InMatch ? "in match" : "in lobby";
                return new
                {
                    id = f.UserId,
                    name = f.Name,
                    status = f.Status,
                    presence = f.Status == "accepted" ? presence : null,
                    joinable = f.Status == "accepted" && lobby is not null && !lobby.InMatch ? lobby.Code : null
                };
            }));
        });

        api.MapPost("/friends/request", (FriendRequestBody body, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            if (db.UserIdByName(body.Username ?? "") is not long target) return Error("No player with that username.", 404);
            if (db.RequestFriend(user.Id, target) is { } err) return Error(err);
            GameSocket.Notify(target, new { type = "friends", message = $"{user.DisplayName} sent you a friend request." });
            return Results.Json(new { ok = true });
        });

        api.MapPost("/friends/respond", (FriendRespondBody body, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            db.RespondFriend(body.UserId, user.Id, body.Accept);
            if (body.Accept) GameSocket.Notify(body.UserId, new { type = "friends", message = $"{user.DisplayName} accepted your friend request." });
            return Results.Json(new { ok = true });
        });

        api.MapDelete("/friends/{id:long}", (long id, HttpContext ctx) =>
        {
            if (Authenticate(ctx, db) is not { } user) return Error("Not logged in.", 401);
            db.RemoveFriend(user.Id, id);
            return Results.Json(new { ok = true });
        });

        api.MapGet("/lobbies", () => Results.Json(lobbies.PublicLobbies()));
        api.MapGet("/leaderboard", () => Results.Json(db.Leaderboard()));
        api.MapGet("/matches", (int? limit, long? user) =>
            Results.Json(db.RecentMatches(Math.Clamp(limit ?? 20, 1, 100), user)));
        api.MapGet("/replays/{id:long}", (long id) =>
            db.Replay(id) is { } json ? Results.Text(json, "application/json") : Error("Replay not found.", 404));

        api.MapGet("/matches/{id:long}", (long id) =>
            db.MatchDetails(id) is { } m ? Results.Json(m) : Error("Match not found.", 404));
    }
}

/// <summary>Fixed-window counters for rate limiting logins and sign-ups.</summary>
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime Start)> counters = new();

    /// <summary>Counts an attempt and says whether it is still within the limit.</summary>
    public bool Allow(string key, int max, TimeSpan window)
    {
        if (!Check(key, max, window)) return false;
        Hit(key);
        return true;
    }

    public bool Check(string key, int max, TimeSpan window)
    {
        if (counters.TryGetValue(key, out var c) && DateTime.UtcNow - c.Start > window)
            counters.TryRemove(key, out _);
        return !counters.TryGetValue(key, out c) || c.Count < max;
    }

    public void Hit(string key) =>
        counters.AddOrUpdate(key, _ => (1, DateTime.UtcNow), (_, c) => (c.Count + 1, c.Start));
}

/// <summary>The game's WebSocket protocol: the first message authenticates, the rest are game actions.</summary>
public static class GameSocket
{
    private const int MaxMessageBytes = 64 * 1024;
    private static readonly ConcurrentDictionary<long, Connection> online = new();

    public static bool IsOnline(long userId) => online.ContainsKey(userId);
    public static int OnlineCount => online.Count;

    /// <summary>Logs a user out of the game socket (bans).</summary>
    public static void Kick(long userId)
    {
        if (!online.TryGetValue(userId, out var conn)) return;
        conn.Send(new { type = "authError", message = "You were logged out." });
        conn.Complete();
    }

    public static void Notify(long userId, object message)
    {
        if (online.TryGetValue(userId, out var conn)) conn.Send(message);
    }

    public static void Rename(long userId, string name)
    {
        if (online.TryGetValue(userId, out var conn)) conn.User = conn.User with { DisplayName = name };
    }

    public static async Task HandleAsync(WebSocket socket, Database db, LobbyManager lobbies, CancellationToken ct)
    {
        var first = await ReceiveAsync(socket, ct);
        if (first is null) return;
        User? user = null;
        try
        {
            using var doc = JsonDocument.Parse(first);
            if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "auth"
                && doc.RootElement.TryGetProperty("token", out var tok))
                user = db.UserForToken(tok.GetString());
        }
        catch (JsonException) { }
        if (user is null)
        {
            await SendRawAsync(socket, new { type = "authError", message = "Please log in again." }, ct);
            await CloseAsync(socket);
            return;
        }

        var conn = new Connection(socket, user);
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = conn.PumpAsync(pumpCts.Token);

        if (online.TryGetValue(user.Id, out var old))
        {
            old.Send(new { type = "replaced" });
            old.Complete();
        }
        online[user.Id] = conn;
        conn.Send(new { type = "hello", user });

        if (lobbies.LobbyOf(user.Id) is { } existing && lobbies.Join(conn, existing) is null)
        {
            // Rejoined the lobby/match the user was already in.
        }
        else
        {
            conn.Send(new { type = "lobby", lobby = (object?)null });
        }

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var text = await ReceiveAsync(socket, ct);
                if (text is null) break;
                try
                {
                    Handle(conn, text, lobbies, db);
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    conn.Send(new { type = "error", message = "Invalid message." });
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // Client went away.
        }
        finally
        {
            online.TryRemove(new KeyValuePair<long, Connection>(user.Id, conn));
            lobbies.LobbyOf(user.Id)?.Disconnected(conn);
            conn.Complete();
            pumpCts.Cancel();
            try { await pump; } catch { }
            await CloseAsync(socket);
        }
    }

    private static void Handle(Connection conn, string text, LobbyManager lobbies, Database db)
    {
        using var doc = JsonDocument.Parse(text);
        var msg = doc.RootElement;
        var type = msg.GetProperty("type").GetString();
        var lobby = lobbies.LobbyOf(conn.User.Id);
        string? error = null;

        switch (type)
        {
            case "create":
            {
                var created = lobbies.Create();
                if (msg.TryGetProperty("settings", out var s))
                    created.UpdateSettingsUnchecked(s.Deserialize<LobbySettings>(Protocol.Json) ?? new LobbySettings());
                error = lobbies.Join(conn, created);
                break;
            }
            case "solo":
            {
                var created = lobbies.Create();
                error = lobbies.Join(conn, created);
                if (error is null)
                    error = created.StartSolo(conn, msg.GetProperty("bots").GetInt32(),
                        Enum.TryParse<BotDifficulty>(msg.GetProperty("difficulty").GetString(), true, out var d) ? d : BotDifficulty.Normal);
                break;
            }
            case "addBot":
                error = lobby?.AddBot(conn, msg.TryGetProperty("difficulty", out var bd) && Enum.TryParse<BotDifficulty>(bd.GetString(), true, out var diff) ? diff : BotDifficulty.Normal)
                        ?? (lobby is null ? "You are not in a lobby." : null);
                break;
            case "join":
            {
                var target = lobbies.Find(msg.GetProperty("code").GetString() ?? "");
                error = target is null ? "No lobby with that code." : lobbies.Join(conn, target);
                break;
            }
            case "leave":
                lobby?.Leave(conn);
                conn.Send(new { type = "lobby", lobby = (object?)null });
                break;
            case "settings":
                error = lobby?.UpdateSettings(conn, msg.GetProperty("settings").Deserialize<LobbySettings>(Protocol.Json) ?? new LobbySettings())
                        ?? (lobby is null ? "You are not in a lobby." : null);
                break;
            case "kick":
                error = lobby?.Kick(conn, msg.GetProperty("userId").GetInt64());
                break;
            case "start":
                error = lobby?.Start(conn);
                break;
            case "chat":
                error = lobby?.Chat(conn, msg.GetProperty("text").GetString() ?? "");
                break;
            case "reroll":
                error = lobby?.Reroll(conn);
                break;
            case "buy":
            {
                int? replace = msg.TryGetProperty("replace", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null;
                error = lobby?.Buy(conn, msg.GetProperty("slot").GetInt32(), replace);
                break;
            }
            case "buyPity":
            {
                int? replace = msg.TryGetProperty("replace", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null;
                error = lobby?.BuyPity(conn, msg.GetProperty("slot").GetInt32(), replace);
                break;
            }
            case "buyHp":
                error = lobby?.BuyHp(conn);
                break;
            case "ready":
                error = lobby?.SetReady(conn, msg.GetProperty("ready").GetBoolean());
                break;
            case "augment":
                error = lobby?.PickAugment(conn, msg.GetProperty("index").GetInt32());
                break;
            case "mystery":
                error = lobby?.UseMystery(conn, msg.TryGetProperty("arg", out var arg) ? arg.ToString() : null);
                break;
            case "ability":
                lobby?.UseAbility(conn, msg.GetProperty("slot").GetInt32());
                break;
            case "spectate":
                error = lobby?.Spectate(conn, msg.GetProperty("fightId").GetInt32());
                break;
            case "kit":
                error = lobby?.PickKit(conn, msg.GetProperty("kit").GetString() ?? "") ?? (lobby is null ? "You are not in a lobby." : null);
                break;
            case "invite":
            {
                long target = msg.GetProperty("userId").GetInt64();
                if (lobby is null) error = "Create or join a lobby first.";
                else if (!db.AreFriends(conn.User.Id, target)) error = "You can only invite friends.";
                else if (!IsOnline(target)) error = "That friend is offline.";
                else
                {
                    Notify(target, new { type = "invite", from = conn.User.DisplayName, fromId = conn.User.Id, code = lobby.Code });
                    conn.Send(new { type = "info", message = "Invite sent." });
                }
                break;
            }
            case "fx":
            {
                // Admin prank effects: purely visual, sent only to chosen players in the same lobby, never signed.
                if (!Admin.IsAdmin(conn.User) || lobby is null) { error = "Unknown message."; break; }
                var effect = msg.GetProperty("effect").GetString() ?? "";
                if (!Admin.FxEffects.Contains(effect)) { error = "Unknown effect."; break; }
                var fxText = msg.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
                if (fxText.Length > 120) fxText = fxText[..120];
                var targets = msg.GetProperty("targets").EnumerateArray().Select(x => x.GetInt64()).ToHashSet();
                foreach (var id in lobby.MemberIds().Where(targets.Contains))
                    Notify(id, new { type = "fx", effect, text = fxText });
                break;
            }
            case "ping":
                conn.Send(new { type = "pong" });
                break;
            default:
                error = "Unknown message.";
                break;
        }
        if (error is not null) conn.Send(new { type = "error", message = error });
    }

    private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxMessageBytes) return null;
            if (result.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static Task SendRawAsync(WebSocket socket, object message, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Protocol.Json)), WebSocketMessageType.Text, true, ct);

    private static async Task CloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (WebSocketException) { }
    }
}
