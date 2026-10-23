namespace Ashen;

public enum LobbyState
{
    Waiting,
    Shop,
    Combat,
    Results
}

public class LobbyMember
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public Connection? Connection { get; set; }
    public bool Connected => Connection is not null;
}

public record ChatMessage(long UserId, string Name, string Text, long At);

/// <summary>
/// A lobby and the match running in it. Every public method locks <see cref="sync"/>; the match loop
/// only holds the lock while it changes state, never while it waits.
/// </summary>
public class Lobby
{
    public const int MaxPlayers = 8;
    public const int MinPlayers = 2;
    public const int FlawlessBonus = 5;
    public const double ResultsSeconds = 5;
    /// <summary>Extra time on top of the longest fight before the results screen starts anyway.</summary>
    public const double ReplayGraceSeconds = 15;

    private readonly object sync = new();
    private readonly LobbyManager manager;
    private readonly Database? database;
    private readonly Random rng = new();

    public string Code { get; }
    public long HostId { get; private set; }
    public LobbySettings Settings { get; private set; } = new();
    public LobbyState State { get; private set; } = LobbyState.Waiting;
    public List<LobbyMember> Members { get; } = new();
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    // ----- match state -----
    private readonly List<Player> players = new();
    private readonly Dictionary<string, long> claimedEvents = new();
    private readonly HashSet<string> lobbySpecials = new();
    private readonly List<ChatMessage> chat = new();
    private readonly Dictionary<long, DateTime> lastChat = new();
    private int round;
    private DateTime phaseEndsAt;
    private DateTime matchStartedAt;
    private DateTime combatStartedAt;
    private readonly Dictionary<long, (CombatResult Result, int Index)> roundFights = new();
    private readonly HashSet<long> replayDone = new();
    private readonly List<string> pendingAnnouncements = new();
    private object? lastGameOver;

    public Lobby(string code, LobbyManager manager, Database? database)
    {
        Code = code;
        this.manager = manager;
        this.database = database;
    }

    public bool InMatch => State != LobbyState.Waiting;

    public bool HasMember(long userId)
    {
        lock (sync) return Members.Any(m => m.UserId == userId);
    }

    /// <summary>Entry for the public lobby browser, or null if the lobby shouldn't be listed.</summary>
    public object? BrowseEntry()
    {
        lock (sync)
        {
            if (!Settings.Public || InMatch || Members.Count is 0 or >= MaxPlayers) return null;
            return new
            {
                code = Code,
                host = Members.FirstOrDefault(m => m.UserId == HostId)?.Name,
                players = Members.Count,
                maxPlayers = MaxPlayers
            };
        }
    }

    public bool IsEmpty
    {
        get { lock (sync) return Members.Count == 0; }
    }

    // ================= membership =================

    public string? TryJoin(Connection conn)
    {
        lock (sync)
        {
            Touch();
            var existing = Members.FirstOrDefault(m => m.UserId == conn.User.Id);
            if (existing is not null)
            {
                Attach(existing, conn);
                return null;
            }
            if (InMatch) return "This match is already running.";
            if (Members.Count >= MaxPlayers) return "This lobby is full.";
            var member = new LobbyMember { UserId = conn.User.Id, Name = conn.User.DisplayName };
            Members.Add(member);
            if (Members.Count == 1) HostId = member.UserId;
            Attach(member, conn);
            AddSystemChat($"{member.Name} joined.");
            BroadcastLobby();
            return null;
        }
    }

    private void Attach(LobbyMember member, Connection conn)
    {
        member.Connection = conn;
        member.Name = conn.User.DisplayName;
        var player = FindPlayer(member.UserId);
        if (player is not null)
        {
            player.Connected = true;
            player.Name = member.Name;
        }
        BroadcastLobby();
        conn.Send(new { type = "chat", messages = chat });
        if (InMatch)
        {
            SendState(member);
            if (State == LobbyState.Combat && roundFights.TryGetValue(member.UserId, out var fight) && !replayDone.Contains(member.UserId))
                SendCombat(member, fight.Result, fight.Index);
        }
    }

    /// <summary>The socket closed. In a match the seat stays reserved; in the waiting room the player leaves.</summary>
    public void Disconnected(Connection conn)
    {
        lock (sync)
        {
            var member = Members.FirstOrDefault(m => m.Connection == conn);
            if (member is null) return;
            member.Connection = null;
            if (InMatch)
            {
                if (FindPlayer(member.UserId) is { } p) p.Connected = false;
                BroadcastState();
                BroadcastLobby();
            }
            else
            {
                // Grace period so a page reload doesn't kick you out of the waiting room.
                BroadcastLobby();
                var userId = member.UserId;
                _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => RemoveIfStillGone(userId));
            }
        }
    }

    private void RemoveIfStillGone(long userId)
    {
        lock (sync)
        {
            if (InMatch) return;
            var member = Members.FirstOrDefault(m => m.UserId == userId);
            if (member is { Connected: false }) RemoveMember(member, $"{member.Name} left.");
        }
    }

    public void Leave(Connection conn)
    {
        lock (sync)
        {
            var member = Members.FirstOrDefault(m => m.UserId == conn.User.Id);
            if (member is null) return;
            if (InMatch && FindPlayer(member.UserId) is { Eliminated: false } p)
            {
                Forfeit(p);
            }
            RemoveMember(member, $"{member.Name} left.");
        }
    }

    public string? Kick(Connection conn, long userId)
    {
        lock (sync)
        {
            if (conn.User.Id != HostId) return "Only the host can kick players.";
            if (InMatch) return "You can only kick players before the match starts.";
            if (userId == HostId) return "You can't kick yourself.";
            var member = Members.FirstOrDefault(m => m.UserId == userId);
            if (member is null) return "Player not found.";
            member.Connection?.Send(new { type = "kicked" });
            RemoveMember(member, $"{member.Name} was kicked.");
            return null;
        }
    }

    private void RemoveMember(LobbyMember member, string message)
    {
        Members.Remove(member);
        manager.Unbind(member.UserId, this);
        member.Connection?.Send(new { type = "left" });
        if (Members.Count == 0)
        {
            manager.Remove(this);
            return;
        }
        if (member.UserId == HostId)
            HostId = (Members.FirstOrDefault(m => m.Connected) ?? Members[0]).UserId;
        AddSystemChat(message);
        BroadcastLobby();
        if (InMatch) BroadcastState();
    }

    public string? UpdateSettings(Connection conn, LobbySettings settings)
    {
        lock (sync)
        {
            if (conn.User.Id != HostId) return "Only the host can change settings.";
            if (InMatch) return "Settings can't be changed during a match.";
            Settings = settings.Sanitized();
            BroadcastLobby();
            return null;
        }
    }

    public void UpdateSettingsUnchecked(LobbySettings settings)
    {
        lock (sync) Settings = settings.Sanitized();
    }

    public void RenameMember(long userId, string name)
    {
        lock (sync)
        {
            if (Members.FirstOrDefault(m => m.UserId == userId) is { } m) m.Name = name;
            if (FindPlayer(userId) is { } p) p.Name = name;
            BroadcastLobby();
            if (InMatch) BroadcastState();
        }
    }

    public string? Chat(Connection conn, string text)
    {
        lock (sync)
        {
            text = text.Trim();
            if (text.Length == 0) return null;
            if (text.Length > 200) text = text[..200];
            var now = DateTime.UtcNow;
            if (lastChat.TryGetValue(conn.User.Id, out var last) && (now - last).TotalSeconds < 0.5)
                return "Slow down a little.";
            lastChat[conn.User.Id] = now;
            var member = Members.FirstOrDefault(m => m.UserId == conn.User.Id);
            if (member is null) return null;
            AddChat(new ChatMessage(member.UserId, member.Name, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            return null;
        }
    }

    private void AddSystemChat(string text) =>
        AddChat(new ChatMessage(0, "", text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    private void AddChat(ChatMessage message)
    {
        chat.Add(message);
        if (chat.Count > 50) chat.RemoveAt(0);
        Broadcast(new { type = "chat", messages = new[] { message } });
    }

    public bool IsAbandoned(TimeSpan after)
    {
        lock (sync) return Members.All(m => !m.Connected) && DateTime.UtcNow - LastActivity > after;
    }

    /// <summary>Stops a running match (its loop notices the state change) when the lobby is deleted.</summary>
    public void Close()
    {
        lock (sync)
        {
            State = LobbyState.Waiting;
            players.Clear();
        }
    }

    private void Touch() => LastActivity = DateTime.UtcNow;

    // ================= match =================

    public string? Start(Connection conn)
    {
        lock (sync)
        {
            if (conn.User.Id != HostId) return "Only the host can start the match.";
            if (InMatch) return "The match is already running.";
            var connected = Members.Where(m => m.Connected).ToList();
            if (connected.Count < MinPlayers) return $"You need at least {MinPlayers} players.";
            foreach (var m in Members.Where(m => !m.Connected).ToList()) RemoveMember(m, $"{m.Name} left.");

            players.Clear();
            claimedEvents.Clear();
            lobbySpecials.Clear();
            pendingAnnouncements.Clear();
            lastGameOver = null;
            round = 0;
            matchStartedAt = DateTime.UtcNow;
            foreach (var m in Members)
            {
                players.Add(new Player
                {
                    Id = m.UserId,
                    Name = m.Name,
                    Money = Settings.StartMoney,
                    Lives = Settings.Lives,
                    MaxHealth = Settings.StartHp,
                    Connected = m.Connected
                });
            }
            AddSystemChat("The match has started. Good luck!");
            State = LobbyState.Shop;
            _ = Task.Run(RunMatchAsync);
            return null;
        }
    }

    private async Task RunMatchAsync()
    {
        try
        {
            while (true)
            {
                lock (sync)
                {
                    if (CheckGameOver()) return;
                    BeginShop();
                }
                // Ends early once every alive player is ready (disconnected players count as ready), but never
                // while nobody is connected at all, so a server hiccup doesn't fast-forward the whole match.
                await WaitUntil(() => DateTime.UtcNow >= phaseEndsAt || AlivePlayers().Count() <= 1
                    || (AlivePlayers().Any(p => p.Connected) && AlivePlayers().All(p => p.Ready || !p.Connected)));

                double longest;
                lock (sync)
                {
                    if (CheckGameOver()) return;
                    longest = BeginCombat();
                }
                var replayDeadline = DateTime.UtcNow.AddSeconds(longest + ReplayGraceSeconds);
                await WaitUntil(() => DateTime.UtcNow >= replayDeadline || AlivePlayers().Count() <= 1
                    || roundFights.Keys.All(id => replayDone.Contains(id) || FindPlayer(id) is not { Connected: true }));

                lock (sync)
                {
                    State = LobbyState.Results;
                    phaseEndsAt = DateTime.UtcNow.AddSeconds(ResultsSeconds);
                    BroadcastState();
                }
                await WaitUntil(() => DateTime.UtcNow >= phaseEndsAt);

                lock (sync) FinishRound();
            }
        }
        catch (OperationCanceledException)
        {
            // The lobby was closed while the match was running.
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[lobby {Code}] match crashed: {e}");
            lock (sync)
            {
                State = LobbyState.Waiting;
                AddSystemChat("The match crashed and was stopped. Sorry!");
                BroadcastLobby();
            }
        }
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        while (true)
        {
            lock (sync)
            {
                if (State == LobbyState.Waiting) throw new OperationCanceledException();
                if (condition()) return;
            }
            await Task.Delay(200);
        }
    }

    private IEnumerable<Player> AlivePlayers() => players.Where(p => !p.Eliminated);
    private Player? FindPlayer(long id) => players.FirstOrDefault(p => p.Id == id);

    private ISet<string> UnavailableSpecials(Player p) =>
        Settings.SpecialMode == SpecialMode.PerLobby ? lobbySpecials : p.ObtainedSpecials;

    private void BeginShop()
    {
        round++;
        State = LobbyState.Shop;
        phaseEndsAt = DateTime.UtcNow.AddSeconds(Settings.ShopSeconds);
        roundFights.Clear();
        replayDone.Clear();
        foreach (var p in AlivePlayers())
        {
            if (round > 1) p.Money += Settings.Income;
            p.Ready = false;
            p.Shop.RerollsLeft = Shop.FreeRerolls;
            p.Shop.Locked = false;
            p.Shop.Items = Shop.Roll(p, UnavailableSpecials(p), rng);
            CheckEvents(p, null);
        }
        FlushAnnouncements();
        BroadcastState();
    }

    /// <summary>Pairs everyone, simulates all fights and applies the results. Returns the longest fight's duration.</summary>
    private double BeginCombat()
    {
        State = LobbyState.Combat;
        combatStartedAt = DateTime.UtcNow;
        var alive = AlivePlayers().ToList();
        var (pairs, ghostFor) = MakePairs(alive);
        double longest = 0;

        foreach (var (a, b) in pairs)
        {
            var result = Combat.CalculateCombatResults(a.ToLoadout(), b.ToLoadout());
            a.TimesFought[b.Id] = a.TimesFought.GetValueOrDefault(b.Id) + 1;
            b.TimesFought[a.Id] = b.TimesFought.GetValueOrDefault(a.Id) + 1;
            ApplyResult(a, result, 0);
            ApplyResult(b, result, 1);
            roundFights[a.Id] = (result, 0);
            roundFights[b.Id] = (result, 1);
            longest = Math.Max(longest, result.Duration);
        }
        if (ghostFor is not null)
        {
            var others = alive.Where(p => p != ghostFor).ToList();
            var copy = others[rng.Next(others.Count)];
            var result = Combat.CalculateCombatResults(ghostFor.ToLoadout(), copy.ToLoadout(ghost: true));
            ghostFor.GhostFights++;
            ApplyResult(ghostFor, result, 0);
            roundFights[ghostFor.Id] = (result, 0);
            longest = Math.Max(longest, result.Duration);
        }

        foreach (var m in Members)
        {
            if (roundFights.TryGetValue(m.UserId, out var fight)) SendCombat(m, fight.Result, fight.Index);
        }
        BroadcastState();
        return longest;
    }

    private void ApplyResult(Player p, CombatResult result, int index)
    {
        bool won = result.Winner == index;
        p.Money += result.Earnings[index];
        p.LastFightHpPercent = result.EndHp[index] / Math.Max(1, result.Fighters[index].MaxHp);
        p.LastFightDamageDealt = result.DamageDealt[index];
        if (won)
        {
            p.Wins++;
            p.Money += Settings.WinBonus;
            bool flawless = result.DamageTaken[index] <= 0;
            if (flawless && round > 1) p.Money += FlawlessBonus;
            CheckEvents(p, new FightOutcome(true, flawless));
        }
        else
        {
            p.Losses++;
            p.Lives--;
            if (p.Lives <= 0) p.EliminatedInRound = round;
            CheckEvents(p, new FightOutcome(false, false));
        }
    }

    /// <summary>
    /// Pairs players so everyone meets the opponents they faced least often (rotation). With an odd count the
    /// player with the fewest ghost fights so far fights a ghost.
    /// </summary>
    private (List<(Player, Player)> Pairs, Player? Ghost) MakePairs(List<Player> alive)
    {
        List<(Player, Player)> best = new();
        Player? bestGhost = null;
        long bestCost = long.MaxValue;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            var order = alive.OrderBy(_ => rng.Next()).ToList();
            Player? ghost = null;
            if (order.Count % 2 == 1)
            {
                ghost = order[^1];
                order.RemoveAt(order.Count - 1);
            }
            long cost = ghost is null ? 0 : ghost.GhostFights * 1000L;
            var pairs = new List<(Player, Player)>();
            for (int i = 0; i + 1 < order.Count; i += 2)
            {
                var times = order[i].TimesFought.GetValueOrDefault(order[i + 1].Id);
                cost += (long)times * times * 100;
                pairs.Add((order[i], order[i + 1]));
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                best = pairs;
                bestGhost = ghost;
                if (cost == 0) break;
            }
        }
        return (best, bestGhost);
    }

    private void FinishRound()
    {
        AssignPlacements(players.Where(p => p.Eliminated && p.Placement is null).ToList());
        FlushAnnouncements();
    }

    private void Forfeit(Player p)
    {
        p.Lives = 0;
        p.Ready = true;
        p.EliminatedInRound = round;
        AssignPlacements(new List<Player> { p });
        AddSystemChat($"{p.Name} forfeited.");
    }

    /// <summary>Ranks players knocked out together by fight HP%, then damage dealt; full ties share a placement.</summary>
    private void AssignPlacements(List<Player> knockedOut)
    {
        if (knockedOut.Count == 0) return;
        int aliveAfter = AlivePlayers().Count();
        var ranked = knockedOut
            .OrderByDescending(p => p.LastFightHpPercent)
            .ThenByDescending(p => p.LastFightDamageDealt)
            .ToList();
        for (int i = 0; i < ranked.Count; i++)
        {
            bool tiedWithPrevious = i > 0
                && ranked[i].LastFightHpPercent == ranked[i - 1].LastFightHpPercent
                && ranked[i].LastFightDamageDealt == ranked[i - 1].LastFightDamageDealt;
            ranked[i].Placement = tiedWithPrevious ? ranked[i - 1].Placement : aliveAfter + 1 + i;
        }
    }

    private bool CheckGameOver()
    {
        var alive = AlivePlayers().ToList();
        if (alive.Count > 1) return false;
        foreach (var p in alive) p.Placement = 1;
        EndMatch();
        return true;
    }

    private void EndMatch()
    {
        var standings = players
            .OrderBy(p => p.Placement ?? int.MaxValue)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                placement = p.Placement ?? players.Count,
                lives = Math.Max(0, p.Lives),
                wins = p.Wins,
                losses = p.Losses,
                money = p.Money,
                maxHp = Math.Round(p.MaxHealth),
                titles = p.Titles,
                build = p.BuildView()
            })
            .ToList();
        var events = claimedEvents.Select(kv => new { id = kv.Key, by = kv.Value }).ToList();
        lastGameOver = new { type = "gameOver", rounds = round, standings, events };
        Broadcast(lastGameOver);

        try
        {
            database?.SaveMatch(new MatchRecord
            {
                LobbyCode = Code,
                StartedAt = matchStartedAt,
                EndedAt = DateTime.UtcNow,
                Rounds = round,
                Settings = Settings,
                Players = players.Select(p => new MatchPlayerRecord
                {
                    UserId = p.Id,
                    DisplayName = p.Name,
                    Placement = p.Placement ?? players.Count,
                    Wins = p.Wins,
                    Losses = p.Losses,
                    Build = p.BuildView(),
                    Titles = p.Titles.ToList()
                }).ToList()
            });
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[lobby {Code}] could not save match: {e.Message}");
        }

        State = LobbyState.Waiting;
        players.Clear();
        foreach (var m in Members.Where(m => !m.Connected).ToList()) RemoveMember(m, $"{m.Name} left.");
        AddSystemChat("The match is over. The host can start a rematch.");
        BroadcastLobby();
    }

    // ================= shop actions =================

    private Player? ShopPlayer(Connection conn, out string? error)
    {
        error = null;
        var p = FindPlayer(conn.User.Id);
        if (p is null || !InMatch) error = "You are not in a match.";
        else if (p.Eliminated) error = "You have been eliminated.";
        else if (State != LobbyState.Shop) error = "The shop is closed.";
        return error is null ? p : null;
    }

    public string? Reroll(Connection conn)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            if (p.Shop.Locked) return "You already bought from this roll.";
            if (p.Shop.RerollsLeft <= 0) return "No rerolls left.";
            p.Shop.RerollsLeft--;
            p.Shop.Items = Shop.Roll(p, UnavailableSpecials(p), rng);
            SendState(p.Id);
            return null;
        }
    }

    public string? Buy(Connection conn, int slot, int? replace)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            if (slot < 0 || slot >= p.Shop.Items.Count) return "Invalid shop slot.";
            var shopSlot = p.Shop.Items[slot];
            if (shopSlot.Bought) return "Already bought.";
            var item = GameData.Items[shopSlot.ItemId];
            if (p.Money < item.Price) return "Not enough money.";
            if (item.IsSpecial && Settings.SpecialMode == SpecialMode.PerLobby && lobbySpecials.Contains(item.Id))
            {
                shopSlot.Bought = true;
                SendState(p.Id);
                return "Someone else already got this special item.";
            }

            var owned = p.FindOwned(item.Id);
            if (owned is not null)
            {
                if (owned.Level >= OwnedItem.MaxLevel) return "That item is already at max level.";
                owned.Level++;
            }
            else if (item.Type == ItemType.Weapon)
            {
                p.Weapon = new OwnedItem(item);
            }
            else if (item.Type == ItemType.Shirt)
            {
                p.Shirt = new OwnedItem(item);
            }
            else
            {
                int free = Array.IndexOf(p.Dynamics, null);
                if (free < 0)
                {
                    if (replace is not int r || r < 0 || r >= p.Dynamics.Length) return "Choose an item to replace.";
                    free = r;
                }
                p.Dynamics[free] = new OwnedItem(item);
            }

            p.Money -= item.Price;
            shopSlot.Bought = true;
            p.Shop.Locked = true;
            if (item.IsSpecial)
            {
                p.ObtainedSpecials.Add(item.Id);
                lobbySpecials.Add(item.Id);
            }
            CheckEvents(p, null);
            FlushAnnouncements();
            SendState(p.Id);
            return null;
        }
    }

    public string? BuyHp(Connection conn)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            int cost = HpUpgradeCost(p);
            if (p.Money < cost) return "Not enough money.";
            p.Money -= cost;
            p.HpUpgrades++;
            p.MaxHealth *= Settings.HpUpgradeMult;
            SendState(p.Id);
            return null;
        }
    }

    private int HpUpgradeCost(Player p) => Settings.HpUpgradeCost + Settings.HpUpgradeCostIncrease * p.HpUpgrades;

    public string? SetReady(Connection conn, bool ready)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            p.Ready = ready;
            BroadcastState();
            return null;
        }
    }

    public void ReplayFinished(Connection conn)
    {
        lock (sync)
        {
            if (State == LobbyState.Combat) replayDone.Add(conn.User.Id);
        }
    }

    // ================= lobby events =================

    private record FightOutcome(bool Won, bool Flawless);

    private void CheckEvents(Player p, FightOutcome? fight)
    {
        foreach (var ev in GameData.Events)
        {
            if (claimedEvents.ContainsKey(ev.Id)) continue;
            if (ev.NotFirstRound && round <= 1) continue;
            bool met = ev.Trigger switch
            {
                EventTrigger.WinFight => fight is { Won: true },
                EventTrigger.FlawlessWin => fight is { Won: true, Flawless: true },
                EventTrigger.OwnSpecial => p.ObtainedSpecials.Count > 0,
                EventTrigger.OwnItem => ev.Item is not null && p.FindOwned(ev.Item) is not null,
                EventTrigger.FullPack => p.AllItems().Where(i => i.Item.IsSpecial).GroupBy(i => i.Item.Pack)
                    .Any(g => g.Select(i => i.Id).Distinct().Count() >= GameData.Items.Values.Count(x => x.Pack == g.Key)),
                EventTrigger.MoneyAtLeast => p.Money >= (ev.Value ?? double.MaxValue),
                _ => false
            };
            if (!met) continue;

            claimedEvents[ev.Id] = p.Id;
            p.Money += ev.RewardMoney ?? 0;
            p.BonusEffects.AddRange(ev.RewardEffects ?? new List<string>());
            p.Titles.Add(ev.Name);
            pendingAnnouncements.Add(ev.Id + "\n" + p.Id);
        }
    }

    /// <summary>Announces claimed events. Events from fights wait until the results so they don't spoil replays.</summary>
    private void FlushAnnouncements()
    {
        if (State == LobbyState.Combat) return;
        foreach (var entry in pendingAnnouncements)
        {
            var parts = entry.Split('\n');
            var ev = GameData.Events.First(e => e.Id == parts[0]);
            var who = FindPlayer(long.Parse(parts[1]));
            Broadcast(new { type = "announce", eventId = ev.Id, name = ev.Name, reward = ev.Reward, player = who?.Name });
            AddSystemChat($"{who?.Name} claimed {ev.Name}!");
        }
        pendingAnnouncements.Clear();
        BroadcastState();
    }

    // ================= messages =================

    private void Broadcast(object message)
    {
        foreach (var m in Members) m.Connection?.Send(message);
    }

    public object LobbyView() => new
    {
        code = Code,
        hostId = HostId,
        state = State,
        inMatch = InMatch,
        settings = Settings,
        members = Members.Select(m => new { id = m.UserId, name = m.Name, connected = m.Connected })
    };

    private void BroadcastLobby()
    {
        Touch();
        Broadcast(new { type = "lobby", lobby = LobbyView() });
    }

    private void BroadcastState()
    {
        foreach (var m in Members) SendState(m);
    }

    private void SendState(long userId)
    {
        if (Members.FirstOrDefault(m => m.UserId == userId) is { } m) SendState(m);
    }

    private void SendState(LobbyMember member)
    {
        if (member.Connection is null || !InMatch) return;
        var p = FindPlayer(member.UserId);
        double? remaining = State is LobbyState.Shop or LobbyState.Results
            ? Math.Max(0, (phaseEndsAt - DateTime.UtcNow).TotalSeconds)
            : null;
        member.Connection.Send(new
        {
            type = "state",
            state = new
            {
                phase = State,
                round,
                phaseRemaining = remaining,
                settings = Settings,
                you = p is null ? null : new
                {
                    id = p.Id,
                    money = p.Money,
                    lives = p.Lives,
                    eliminated = p.Eliminated,
                    placement = p.Placement,
                    ready = p.Ready,
                    maxHp = Math.Round(p.MaxHealth, 1),
                    hpUpgradeCost = HpUpgradeCost(p),
                    specialChance = p.SpecialChance,
                    weapon = p.Weapon is { } w ? new ItemView(w.Id, w.Level, "weapon") : null,
                    shirt = p.Shirt is { } s ? new ItemView(s.Id, s.Level, "shirt") : null,
                    dynamics = p.Dynamics.Select((d, i) => d is null ? null : new ItemView(d.Id, d.Level, $"dynamic{i}")),
                    shop = new { items = p.Shop.Items, rerollsLeft = p.Shop.RerollsLeft, locked = p.Shop.Locked },
                    stats = Combat.PreviewStats(p.ToLoadout()),
                    titles = p.Titles,
                    wins = p.Wins,
                    losses = p.Losses
                },
                players = players.Select(o => new
                {
                    id = o.Id,
                    name = o.Name,
                    ready = o.Ready,
                    alive = !o.Eliminated,
                    connected = o.Connected,
                    titles = o.Titles
                }),
                events = GameData.Events.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    description = e.Description,
                    reward = e.Reward,
                    claimedBy = claimedEvents.TryGetValue(e.Id, out var by) ? FindPlayer(by)?.Name : null
                })
            }
        });
    }

    private void SendCombat(LobbyMember member, CombatResult result, int index)
    {
        member.Connection?.Send(new
        {
            type = "combat",
            round,
            you = index,
            elapsed = (DateTime.UtcNow - combatStartedAt).TotalSeconds,
            result
        });
    }
}
