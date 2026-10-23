namespace Ashen;

/// <summary>Admin panel endpoints (/api/admin/...). Admins are the usernames in ASHEN_ADMINS (default "pepf").</summary>
public static class Admin
{
    public static HashSet<string> Usernames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Data was edited while matches were running; reload once none are.</summary>
    private static volatile bool pendingReload;

    public static bool IsAdmin(User user) => Usernames.Contains(user.Username);

    /// <summary>Visual-only prank effects the admin troll panel can send (see js/troll.js).</summary>
    public static readonly HashSet<string> FxEffects = new()
    {
        "banner", "toast", "chat", "reconnect", "shake", "flip", "mirror", "glitch", "itemnames", "miss", "lag"
    };

    private record BanBody(string? Reason);
    private record KickBody(long UserId);

    public static void Map(RouteGroupBuilder api, Database db, LobbyManager lobbies)
    {
        var admin = api.MapGroup("/admin");
        admin.AddEndpointFilter(async (ctx, next) =>
        {
            var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
            var user = header.StartsWith("Bearer ") ? db.UserForToken(header["Bearer ".Length..].Trim()) : null;
            if (user is null || !IsAdmin(user)) return Results.Json(new { error = "Admins only." }, statusCode: 403);
            return await next(ctx);
        });

        admin.MapGet("/lobbies", () => Results.Json(lobbies.All.Select(l => l.AdminView())));
        admin.MapPost("/lobbies/{code}/close", (string code) =>
        {
            lobbies.Find(code)?.AdminClose();
            return Results.Json(new { ok = true });
        });
        admin.MapPost("/lobbies/{code}/kick", (string code, KickBody body) =>
        {
            lobbies.Find(code)?.AdminKick(body.UserId, "You were removed by an admin.");
            return Results.Json(new { ok = true });
        });

        admin.MapGet("/users", (string? q) => Results.Json(db.SearchUsers(q ?? "")));
        admin.MapPost("/users/{id:long}/ban", (long id, BanBody body) =>
        {
            db.SetBanned(id, true, body.Reason);
            lobbies.LobbyOf(id)?.AdminKick(id, "Your account was banned.");
            GameSocket.Kick(id);
            return Results.Json(new { ok = true });
        });
        admin.MapPost("/users/{id:long}/unban", (long id) =>
        {
            db.SetBanned(id, false, null);
            return Results.Json(new { ok = true });
        });

        admin.MapGet("/stats", () => Results.Json(new
        {
            online = GameSocket.OnlineCount,
            lobbies = lobbies.All.Count,
            inMatch = lobbies.All.Count(l => l.InMatch),
            pendingReload,
            data = db.AdminStats()
        }));

        admin.MapGet("/data", () => Results.Json(GameData.Files.Select(f => new
        {
            file = f,
            overridden = GameData.OverrideDir is { } d && File.Exists(Path.Combine(d, f))
        })));
        admin.MapGet("/data/{file}", (string file) =>
            GameData.Files.Contains(file) ? Results.Text(File.ReadAllText(GameData.PathOf(file)), "application/json") : Results.NotFound());

        // Saves an edited file after validating the whole data set with it. Applied right away if no match runs.
        admin.MapPut("/data/{file}", async (string file, HttpRequest req) =>
        {
            if (!GameData.Files.Contains(file) || GameData.OverrideDir is not { } dir) return Results.NotFound();
            using var reader = new StreamReader(req.Body);
            var text = await reader.ReadToEndAsync();
            try
            {
                GameData.Load(GameData.Root, new Dictionary<string, string> { [file] = text }, apply: false);
            }
            catch (Exception e)
            {
                return Results.Json(new { error = $"Invalid data: {e.Message}" }, statusCode: 400);
            }
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, file);
            if (File.Exists(target))
                File.Copy(target, Path.Combine(dir, $"{file}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak"), overwrite: true);
            await File.WriteAllTextAsync(target, text);
            return Results.Json(new { ok = true, applied = TryReload(lobbies) });
        });

        // Removes the override so the repository's version is used again.
        admin.MapDelete("/data/{file}", (string file) =>
        {
            if (!GameData.Files.Contains(file) || GameData.OverrideDir is not { } dir) return Results.NotFound();
            var target = Path.Combine(dir, file);
            if (File.Exists(target)) File.Move(target, Path.Combine(dir, $"{file}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak"), overwrite: true);
            return Results.Json(new { ok = true, applied = TryReload(lobbies) });
        });
    }

    /// <summary>Reloads data now if no match is running, otherwise marks it pending.</summary>
    private static bool TryReload(LobbyManager lobbies)
    {
        if (lobbies.AnyInMatch)
        {
            pendingReload = true;
            return false;
        }
        GameData.Load(GameData.Root);
        pendingReload = false;
        return true;
    }

    public static async Task ApplyPendingReloadsAsync(LobbyManager lobbies, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { return; }
            if (!pendingReload || lobbies.AnyInMatch) continue;
            try
            {
                GameData.Load(GameData.Root);
                pendingReload = false;
                Console.WriteLine("Reloaded game data after admin edits.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Pending data reload failed: {e.Message}");
                pendingReload = false;
            }
        }
    }
}
