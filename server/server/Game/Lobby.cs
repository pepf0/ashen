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
    public string Kit { get; set; } = Ashen.Kit.Default;
    /// <summary>Best kit mastery title (e.g. "Duelist Adept"), from the database.</summary>
    public string? Mastery { get; set; }
    /// <summary>Bots have negative ids, no socket, and play through <see cref="BotIdentity"/>.</summary>
    public bool IsBot => Difficulty is not null;
    public BotDifficulty? Difficulty { get; set; }
    public Appearance? Appearance { get; set; }
    public Connection? BotIdentity { get; set; }
    /// <summary>Admin watching a simulation: in the lobby, but not a player.</summary>
    public bool Observer { get; set; }
}

public record ChatMessage(long UserId, string Name, string Text, long At);

/// <summary>
/// A lobby and the match running in it. Every public method locks <see cref="sync"/>; the match loop
/// only holds the lock while it changes state, never while it waits.
/// </summary>
public partial class Lobby
{
    public const int MaxPlayers = 8;
    public const int MinPlayers = 2;
    public const int FlawlessBonus = 5;
    public const double ResultsSeconds = 5;
    public const double CountdownSeconds = 3;
    public const int InterestPer = 10;
    public const int MaxInterest = 5;

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
    /// <summary>Events active this match (events with a chance are rolled at match start).</summary>
    private readonly HashSet<string> activeEvents = new();
    /// <summary>Names of players who took the Empty Hands offer, for the events list.</summary>
    private readonly List<string> weaponlessTakers = new();
    private readonly HashSet<string> lobbySpecials = new();
    private readonly List<ChatMessage> chat = new();
    private readonly Dictionary<long, DateTime> lastChat = new();
    private int round;
    private int startingLives;
    private DateTime phaseEndsAt;
    private DateTime matchStartedAt;
    private DateTime fightsStartAt;
    private readonly List<LiveFight> fights = new();
    private int nextFightId;
    private RoundModifier? modifier;
    private readonly List<Mutator> mutators = new();
    private readonly List<PendingReplay> replays = new();

    private bool Mut(string id) => mutators.Any(m => m.Id == id);
    private int MaxLevel => Mut("master_smith") ? 7 : OwnedItem.MaxLevel;
    private int KitSwapCost => Mut("free_kits") ? 0 : Kit.SwapCost;
    private int[] AugmentRounds => Mut("augment_feast") ? new[] { 3, 6, 9, 12 } : Augment.DefaultRounds;
    private readonly List<string> pendingAnnouncements = new();
    private object? lastGameOver;
    private BalanceLog? log;

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

    public List<long> MemberIds()
    {
        lock (sync) return Members.Where(m => !m.IsBot).Select(m => m.UserId).ToList();
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
            var member = new LobbyMember
            {
                UserId = conn.User.Id, Name = conn.User.DisplayName,
                Mastery = database?.MasteryTitle(conn.User.Id), Appearance = database?.GetAppearance(conn.User.Id)
            };
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
            var own = FightOf(member.UserId);
            if (State == LobbyState.Combat && own is not null && !own.Ended)
                SendFightStart(member, own, own.IndexOf(member.UserId));
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
        if (!Members.Any(m => !m.IsBot))
        {
            // Only bots left: close the lobby.
            Members.Clear();
            manager.Remove(this);
            return;
        }
        if (member.UserId == HostId)
            HostId = (Members.FirstOrDefault(m => m.Connected) ?? Members.First(m => !m.IsBot)).UserId;
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

    public object AdminView()
    {
        lock (sync)
        {
            return new
            {
                code = Code,
                state = State,
                round,
                hostId = HostId,
                settings = Settings,
                mutators = mutators.Select(m => m.Id),
                members = Members.Select(m => new
                {
                    id = m.UserId, name = m.Name, connected = m.Connected, bot = m.Difficulty,
                    lives = FindPlayer(m.UserId)?.Lives, eliminated = FindPlayer(m.UserId)?.Eliminated
                })
            };
        }
    }

    /// <summary>Admin: removes a player (forfeiting if a match is running).</summary>
    public void AdminKick(long userId, string reason)
    {
        lock (sync)
        {
            var member = Members.FirstOrDefault(m => m.UserId == userId);
            if (member is null) return;
            if (InMatch && FindPlayer(userId) is { Eliminated: false } p) Forfeit(p);
            member.Connection?.Send(new { type = "error", message = reason });
            RemoveMember(member, $"{member.Name} was removed by an admin.");
        }
    }

    /// <summary>Admin: closes the lobby for everyone.</summary>
    public void AdminClose()
    {
        lock (sync)
        {
            foreach (var m in Members.ToList())
            {
                m.Connection?.Send(new { type = "error", message = "This lobby was closed by an admin." });
                m.Connection?.Send(new { type = "lobby", lobby = (object?)null });
                manager.Unbind(m.UserId, this);
            }
            Members.Clear();
        }
        manager.Remove(this);
    }

    /// <summary>Stops a running match (its loop notices the state change) when the lobby is deleted.</summary>
    public void Close()
    {
        lock (sync)
        {
            State = LobbyState.Waiting;
            players.Clear();
            log?.Dispose();
            log = null;
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
            foreach (var m in Members.Where(m => !m.Connected && !m.IsBot).ToList()) RemoveMember(m, $"{m.Name} left.");
            if (Members.Count(m => !m.Observer) < MinPlayers) return $"You need at least {MinPlayers} players (add bots for a solo game).";

            players.Clear();
            claimedEvents.Clear();
            weaponlessTakers.Clear();
            activeEvents.Clear();
            foreach (var ev in GameData.Events)
                if (ev.Chance is not { } chance || rng.NextDouble() < chance) activeEvents.Add(ev.Id);
            lobbySpecials.Clear();
            pendingAnnouncements.Clear();
            lastGameOver = null;
            round = 0;
            matchStartedAt = DateTime.UtcNow;
            mutators.Clear();
            replays.Clear();
            if (!Simulation || SimMutators) mutators.AddRange(Mutator.All.OrderBy(_ => rng.Next()).Take(Mutator.PerMatch));
            int lives = Settings.Lives + (Mut("long_haul") ? 2 : 0) - (Mut("hardcore") ? 2 : 0);
            if (Mut("hardcore")) lives = Math.Max(Math.Min(2, Settings.Lives), lives);
            foreach (var m in Members.Where(m => !m.Observer))
            {
                var player = new Player
                {
                    Id = m.UserId,
                    Name = m.Name,
                    Money = Settings.StartMoney + (Mut("rich_start") ? 50 : 0),
                    Lives = Math.Max(1, lives),
                    MaxHealth = Settings.StartHp * (Mut("fragile_start") ? 0.6 : 1),
                    Connected = m.Connected,
                    Kit = m.Kit,
                    Appearance = m.Appearance
                };
                player.Stats.MinLives = player.Lives;
                if (Mut("armed_start"))
                {
                    var weapons = GameData.Generics.Where(i => i.Type == ItemType.Weapon).ToList();
                    player.Weapon = new OwnedItem(weapons[rng.Next(weapons.Count)]);
                }
                players.Add(player);
            }
            startingLives = Math.Max(1, lives);
            log?.Dispose();
            log = BalanceLog.Open(Code);
            log.Write("match_start", new
            {
                lobby = Code,
                settings = Settings,
                mutators = mutators.Select(m => m.Id),
                events = activeEvents,
                players = players.Select(p => new { id = p.Id, name = p.Name, kit = p.Kit })
            });
            AddSystemChat("The match has started. Good luck!");
            AddSystemChat("Mutators this match: " + string.Join(", ", mutators.Select(m => $"{m.Name} ({m.Description})")));
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
                    || (!Simulation && (AlivePlayers().Any(p => p.Connected) || AlivePlayers().All(p => p.Id < 0))
                        && AlivePlayers().All(p => p.Ready || !p.Connected)));

                lock (sync)
                {
                    if (CheckGameOver()) return;
                    BeginCombat();
                }
                await WaitUntil(() => DateTime.UtcNow >= fightsStartAt);
                simFightClock = 0;

                // Real-time fights: every fight catches up to the ticks it should have done by now.
                using (var timer = new PeriodicTimer(TimeSpan.FromSeconds(Combat.SPT)))
                {
                    while (true)
                    {
                        lock (sync)
                        {
                            if (State == LobbyState.Waiting) throw new OperationCanceledException();
                            if (TickFights()) break;
                        }
                        await timer.WaitForNextTickAsync();
                    }
                }

                lock (sync)
                {
                    State = LobbyState.Results;
                    phaseEndsAt = DateTime.UtcNow.AddSeconds(SimSeconds(ResultsSeconds));
                    Broadcast(RoundRecap());
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
        var last = DateTime.UtcNow;
        while (true)
        {
            lock (sync)
            {
                if (State == LobbyState.Waiting) throw new OperationCanceledException();
                // A paused simulation pushes every deadline back by the time it was paused.
                var now = DateTime.UtcNow;
                if (Simulation && SimSpeed <= 0) { phaseEndsAt += now - last; fightsStartAt += now - last; }
                last = now;
                if (condition()) return;
            }
            await Task.Delay(Simulation ? 50 : 200);
        }
    }

    private IEnumerable<Player> AlivePlayers() => players.Where(p => !p.Eliminated);
    private Player? FindPlayer(long id) => players.FirstOrDefault(p => p.Id == id);

    private ISet<string> UnavailableSpecials(Player p) =>
        Settings.SpecialMode == SpecialMode.PerLobby ? lobbySpecials : p.ObtainedSpecials;

    private FightRules Rules => new()
    {
        Modifier = modifier?.Id,
        DamageMult = Mut("double_trouble") && round % 3 == 0 ? 2 : 1,
        OvertimeStart = Mut("quick_death") ? 20 : Combat.OvertimeStart,
        SuddenDeathStart = Mut("quick_death") ? 40 : Combat.SuddenDeathStart
    };

    private ShopRollOptions RollOptions(Player p) => new()
    {
        Modifier = modifier?.Id,
        Slots = Mut("big_shops") ? 8 : Shop.Slots,
        MaxLevel = MaxLevel,
        NoShirts = Mut("no_shirts"),
        SpecialMult = (Mut("special_frenzy") ? 2 : 1) * Settings.SpecialChanceMult,
        Discount = Math.Clamp(p.AugmentMods.Get("shop_discount"), 0, 0.9)
    };

    private string? RollArena() =>
        Mut("arena_masters") || rng.NextDouble() >= Arena.PlainChance ? Arena.All[rng.Next(Arena.All.Count)].Id : null;

    /// <summary>From round 2, 70% of rounds get a random modifier.</summary>
    private void RollModifier()
    {
        modifier = (Mut("chaos") || (round >= 2 && rng.NextDouble() >= RoundModifier.CalmChance)) && RoundModifier.All.Count > 0
            ? RoundModifier.All[rng.Next(RoundModifier.All.Count)]
            : null;
        if (modifier is not null) AddSystemChat($"Round {round}: {modifier.Name}. {modifier.Description}.");
    }

    /// <summary>Income, interest on saved money, loss-streak bonus and Tax, applied when a shop opens.</summary>
    private void PayIncome(Player p)
    {
        var income = new Dictionary<string, int>();
        if (round > 1)
        {
            var aug = p.AugmentMods;
            int cap = (Mut("big_interest") ? 10 : MaxInterest) + (int)aug.Get("interest_cap_add");
            income["interest"] = Math.Min(cap, p.Money / InterestPer);
            income["income"] = (Settings.Income + (int)aug.Get("income_add")) * (modifier?.Id == "gold_rush" ? 2 : 1);
            if (p.LossStreak >= 2) income["lossStreak"] = 10 + 5 * (p.LossStreak - 2);
        }
        p.Money += income.Values.Sum();
        if (modifier?.Id == "tax")
        {
            income["tax"] = -(int)Math.Floor(p.Money * 0.1);
            p.Money += income["tax"];
        }
        p.LastIncome = income.Where(kv => kv.Value != 0).ToDictionary();
    }

    private void BeginShop()
    {
        round++;
        State = LobbyState.Shop;
        phaseEndsAt = DateTime.UtcNow.AddSeconds(Simulation ? SimSeconds(SimShopSeconds) : Settings.ShopSeconds);
        fights.Clear();
        RollModifier();
        log?.Write("round", new { round, modifier = modifier?.Id, alive = AlivePlayers().Count() });
        foreach (var p in AlivePlayers())
        {
            PayIncome(p);
            p.Stats.MaxMoney = Math.Max(p.Stats.MaxMoney, p.Money);
            p.Ready = false;
            p.Shop.RerollsLeft = Shop.FreeRerolls + (int)p.AugmentMods.Get("rerolls_add");
            p.Shop.Locked = false;
            p.Shop.Items = Shop.Roll(p, UnavailableSpecials(p), rng, RollOptions(p));
            p.AugmentOffer = null;
            p.Mystery = null;
            if (AugmentRounds.Contains(round))
            {
                p.AugmentOffer = Augment.RollOffer(p.Augments, rng, Mut("augment_feast") ? 4 : 3);
                log?.Write("augment_offer", new { round, player = p.Id, augments = p.AugmentOffer });
            }
            else if (rng.NextDouble() < MysteryOffer.Chance)
            {
                p.Mystery = RollMystery(p);
                if (p.Mystery is not null) log?.Write("mystery_offer", new { round, player = p.Id, kind = p.Mystery.Kind, choices = p.Mystery.Choices });
            }
            p.Shop.Pity = round == Shop.PityRound && WeaponlessEventActive && p.EarlyWeaponlessFights >= 2 && p.Weapon is null
                ? Shop.PityOffer(UnavailableSpecials(p), rng)
                : new List<ShopSlot>();
            CheckEvents(p, null);
            log?.Write("snapshot", new
            {
                round,
                player = p.Id,
                name = p.Name,
                money = p.Money,
                income = p.LastIncome,
                kit = p.Kit,
                lossStreak = p.LossStreak,
                lives = p.Lives,
                maxHp = Math.Round(p.MaxHealth, 2),
                hpUpgrades = p.HpUpgrades,
                specialChance = p.SpecialChance,
                wins = p.Wins,
                losses = p.Losses,
                titles = p.Titles,
                build = BalanceLog.Build(p)
            });
            LogRoll(p, 0);
            if (p.Shop.Pity.Count > 0)
                log?.Write("pity_offer", new { round, player = p.Id, items = p.Shop.Pity });
        }
        AnnounceRoundEvents();
        BotsTakeShop();
        FlushAnnouncements();
        BroadcastState();
    }

    private void LogRoll(Player p, int reroll) => log?.Write("roll", new
    {
        round,
        player = p.Id,
        reroll,
        specialChance = p.SpecialChanceWith(modifier?.Id),
        modifier = modifier?.Id,
        items = p.Shop.Items.Select(i => i.ItemId)
    });

    private void LogFight(Player a, Player? b, CombatResult result)
    {
        log?.Write("fight", new
        {
            round,
            ghost = b is null,
            fighters = result.Fighters.Select((f, i) => new
            {
                player = f.PlayerId,
                name = f.Name,
                ghost = f.Ghost,
                maxHp = f.MaxHp,
                damageReduction = f.DamageReduction,
                dodgeChance = f.DodgeChance,
                items = f.Items,
                endHp = result.EndHp[i],
                damageDealt = result.DamageDealt[i],
                damageTaken = result.DamageTaken[i],
                earnings = result.Earnings[i],
                attacks = result.Events.OfType<DamageEvent>().Count(e => e.WeaponAttack && e.Source == i),
                crits = result.Events.OfType<DamageEvent>().Count(e => e.WeaponAttack && e.Source == i && e.CritMultiplier is not null),
                dodged = result.Events.OfType<DamageEvent>().Count(e => e.WeaponAttack && e.Target == i && e.Dodged),
                stunsLanded = result.Events.OfType<StunEvent>().Count(e => e.Source == i && !e.Blocked),
                kit = f.Kit,
                bot = b is null && i == 1,
                abilities = result.Events.OfType<AbilityEvent>().Where(e => e.Source == i).GroupBy(e => e.Ability)
                    .ToDictionary(g => g.Key, g => g.Count()),
                healed = Math.Round(result.Events.OfType<HealEvent>().Where(e => e.Source == i).Sum(e => e.Amount), 1),
                absorbed = Math.Round(result.Events.OfType<DamageEvent>().Where(e => e.Target == i).Sum(e => e.Absorbed), 1)
            }),
            modifier = modifier?.Id,
            winner = result.Winner,
            duration = result.Duration,
            suddenDeath = result.Duration >= Combat.SuddenDeathStart
        });
    }

    /// <summary>Pairs everyone and starts the live fights after a short countdown.</summary>
    private void BeginCombat()
    {
        State = LobbyState.Combat;
        fightsStartAt = DateTime.UtcNow.AddSeconds(SimSeconds(CountdownSeconds));
        foreach (var p in players)
        {
            p.AugmentOffer = null;
            p.Mystery = null;
        }
        var alive = AlivePlayers().ToList();
        var (pairs, ghostFor) = MakePairs(alive);
        var rules = Rules;
        fights.Clear();

        foreach (var (a, b) in pairs)
        {
            a.TimesFought[b.Id] = a.TimesFought.GetValueOrDefault(b.Id) + 1;
            b.TimesFought[a.Id] = b.TimesFought.GetValueOrDefault(a.Id) + 1;
            fights.Add(new LiveFight
            {
                Id = ++nextFightId,
                Fight = new Fight(BotLoadout(a), BotLoadout(b), rules.WithArena(RollArena()), rng.Next()),
                Players = new Player?[] { a, b }
            });
        }
        if (ghostFor is not null)
        {
            var others = alive.Where(p => p != ghostFor).ToList();
            var copy = others[rng.Next(others.Count)];
            ghostFor.GhostFights++;
            var fight = new Fight(BotLoadout(ghostFor), copy.ToLoadout(ghost: true), rules.WithArena(RollArena()), rng.Next());
            fight.SetBot(1, true);
            fights.Add(new LiveFight { Id = ++nextFightId, Fight = fight, Players = new Player?[] { ghostFor, null } });
        }
        if (round <= 2)
            foreach (var p in alive.Where(p => p.Weapon is null)) p.EarlyWeaponlessFights++;

        foreach (var fight in fights)
            for (int i = 0; i < 2; i++)
                if (fight.Players[i] is { } p && MemberOf(p.Id) is { } m) SendFightStart(m, fight, i);
        FollowFights();
        BroadcastState();
    }

    private Loadout BotLoadout(Player p)
    {
        var l = p.ToLoadout();
        l.BotSkill = BotSkill(p);
        return l;
    }

    private LobbyMember? MemberOf(long userId) => Members.FirstOrDefault(m => m.UserId == userId);

    private LiveFight? FightOf(long playerId) => fights.FirstOrDefault(f => f.IndexOf(playerId) is not null);

    /// <summary>Advances every running fight to the current time and streams the new ticks. Returns true when all ended.</summary>
    private bool TickFights()
    {
        if (Simulation) simFightClock += Combat.SPT * SimSpeed;
        int target = Simulation ? (int)(simFightClock * Combat.TPS) : (int)((DateTime.UtcNow - fightsStartAt).TotalSeconds * Combat.TPS);
        int maxSteps = Combat.TPS * Math.Max(1, (int)Math.Ceiling(Simulation ? SimSpeed : 1));
        foreach (var lf in fights.Where(f => !f.Ended))
        {
            for (int i = 0; i < 2; i++)
                if (lf.Players[i] is { } p) lf.Fight.SetBot(i, !p.Connected);

            int steps = 0;
            while (lf.TicksDone < target && !lf.Fight.Finished && steps++ < maxSteps)
            {
                lf.Fight.Step();
                lf.TicksDone++;
            }
            var snap = lf.Fight.Snapshot();
            if (lf.TicksDone % 2 == 0 || lf.Fight.Finished) lf.Snapshots.Add(snap);
            var tick = new { type = "fightTick", fightId = lf.Id, events = lf.Fight.TakeNewEvents(), snap };
            foreach (var m in Watchers(lf)) m.Connection?.Send(tick);

            if (lf.Fight.Finished)
            {
                lf.Ended = true;
                var result = lf.Fight.Result!;
                // Bounties are read before anyone's streak resets.
                var bounties = lf.Players.Select(p => p?.Bounty ?? 0).ToArray();
                for (int i = 0; i < 2; i++)
                    if (lf.Players[i] is { } p) ApplyResult(p, result, i, lf.Players[1 - i], bounties[1 - i]);
                for (int i = 0; i < 2; i++)
                    if (lf.Players[i] is { } p && result.Winner != i) p.WinStreak = 0;
                for (int i = 0; i < 2; i++)
                    if (lf.Players[i] is { } p) TrackFight(p, result, i);
                // Rivalries: who beat whom (fights against ghosts don't count).
                if (result.Winner >= 0 && lf.Players[1 - result.Winner] is { } loser && lf.Players[result.Winner] is { } winner)
                    loser.Stats.LostTo[winner.Id] = loser.Stats.LostTo.GetValueOrDefault(winner.Id) + 1;
                LogFight(lf.Players[0]!, lf.Players[1], result);
                StoreReplay(lf, result);
                var end = new { type = "fightEnd", fightId = lf.Id, result = ResultSummary(result) };
                foreach (var m in Watchers(lf)) m.Connection?.Send(end);
                BroadcastState();
            }
        }
        return fights.All(f => f.Ended);
    }

    /// <summary>Recap of every fight this round: who won, by how much, the biggest single hit and notable finishes.</summary>
    private object RoundRecap() => new
    {
        type = "roundRecap",
        round,
        fights = fights.Where(f => f.Fight.Result is not null).Select(f =>
        {
            var r = f.Fight.Result!;
            var hits = r.Events.OfType<DamageEvent>().Where(e => e.Damage > 0 && !e.SuddenDeath).ToList();
            var big = hits.OrderByDescending(e => e.Damage).FirstOrDefault();
            int w = r.Winner;
            double hpLeft = w >= 0 ? r.EndHp[w] / Math.Max(1, r.Fighters[w].MaxHp) : 0;
            return new
            {
                names = new[] { r.Fighters[0].Name, r.Fighters[1].Name },
                ids = new[] { f.Players[0]?.Id, f.Players[1]?.Id },
                ghost = new[] { r.Fighters[0].Ghost, r.Fighters[1].Ghost },
                winner = w,
                hpLeft = Math.Round(hpLeft, 3),
                duration = Math.Round(r.Duration, 1),
                biggestHit = big is null ? null : new { by = big.Source, amount = Math.Round(big.Damage, 1), crit = big.CritMultiplier is not null },
                flawless = w >= 0 && r.DamageTaken[w] <= 0,
                clutch = w >= 0 && hpLeft <= 0.1
            };
        })
    };

    /// <summary>Stat preview of the player's build with one more copy of an item (level-up, new weapon/shirt, or a free dynamic slot).</summary>
    private Dictionary<string, double>? PreviewWith(Player p, string itemId)
    {
        if (!GameData.Items.TryGetValue(itemId, out var item)) return null;
        var l = p.ToLoadout();
        var owned = l.AllItems().FirstOrDefault(o => o.Id == itemId);
        if (owned is not null) { if (owned.Level >= OwnedItem.MaxLevel) return null; owned.Level++; }
        else if (item.Type == ItemType.Weapon) l.Weapon = new OwnedItem(item);
        else if (item.Type == ItemType.Shirt) l.Shirt = new OwnedItem(item);
        else
        {
            int free = Array.IndexOf(l.Dynamics, null);
            if (free < 0) return null;
            l.Dynamics[free] = new OwnedItem(item);
        }
        return Combat.PreviewStats(l, Rules);
    }

    private void TrackFight(Player p, CombatResult result, int i)
    {
        var s = p.Stats;
        bool won = result.Winner == i;
        var mine = result.Events.Where(e => e.Source == i).ToList();
        s.MaxFightDamage = Math.Max(s.MaxFightDamage, result.DamageDealt[i]);
        s.TotalDamage += result.DamageDealt[i];
        s.TotalTaken += result.DamageTaken[i];
        var hits = mine.OfType<DamageEvent>().Where(e => e.Target != i && !e.SuddenDeath).ToList();
        if (hits.Count > 0) s.BiggestHit = Math.Max(s.BiggestHit, hits.Max(e => e.Damage));
        s.Crits += hits.Count(e => e.CritMultiplier is not null);
        s.Healed += mine.OfType<HealEvent>().Sum(e => e.Amount);
        s.Stuns += mine.OfType<StunEvent>().Count(e => !e.Blocked && e.Target != i);
        var abilities = mine.OfType<AbilityEvent>().Where(e => e.Ability != "second_wind").ToList();
        s.Abilities += abilities.Count;
        s.Ultimates += abilities.Count(e => e.Slot == 3);
        if (won)
        {
            s.FightsWon++;
            s.FightWinKits.Add(p.Kit);
            if (result.DamageTaken[i] <= 0) s.Flawless++;
            if (result.EndHp[i] / Math.Max(1, result.Fighters[i].MaxHp) <= 0.05) s.Clutch = true;
            if (mine.OfType<AbilityEvent>().Any(e => e.Ability == "second_wind")) s.SecondWindWin = true;
            if (result.KillingBlow[1 - i] == "headshot") s.HeadshotFinish = true;
            if (result.KillingBlow[1 - i] == "xxzweiradxx.staff.smash") s.SmashFinish = true;
        }
        s.MaxWinStreak = Math.Max(s.MaxWinStreak, p.WinStreak);
        s.MaxBounty = Math.Max(s.MaxBounty, p.Bounty);
        s.MinLives = Math.Min(s.MinLives, Math.Max(0, p.Lives));
        if (!won) s.LivesLost++;
        s.MaxMoney = Math.Max(s.MaxMoney, p.Money);
    }

    private void StoreReplay(LiveFight lf, CombatResult result)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                round,
                modifier,
                arena = GameData.Arenas.FirstOrDefault(a => a.Id == lf.Fight.Arena),
                fighters = result.Fighters,
                events = result.Events,
                snaps = lf.Snapshots,
                result = ResultSummary(result)
            }, Protocol.Json);
            replays.Add(new PendingReplay(round, result.Fighters[0].Name, result.Fighters[1].Name, result.Winner, result.Duration, Database.Compress(json)));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[lobby {Code}] could not record replay: {e.Message}");
        }
    }

    private IEnumerable<LobbyMember> Watchers(LiveFight lf) =>
        Members.Where(m => lf.IndexOf(m.UserId) is not null || lf.Spectators.Contains(m.UserId));

    /// <summary>Result without the event list (the clients already got the events live).</summary>
    private static object ResultSummary(CombatResult r) => new
    {
        winner = r.Winner,
        duration = r.Duration,
        endHp = r.EndHp,
        damageDealt = r.DamageDealt,
        damageTaken = r.DamageTaken,
        earnings = r.Earnings,
        fighters = r.Fighters.Select(f => new { name = f.Name, maxHp = f.MaxHp })
    };

    private void SendFightStart(LobbyMember member, LiveFight lf, int? you)
    {
        member.Connection?.Send(new
        {
            type = "fightStart",
            fightId = lf.Id,
            round,
            you,
            countdown = Math.Max(0, (fightsStartAt - DateTime.UtcNow).TotalSeconds),
            modifier,
            arena = GameData.Arenas.FirstOrDefault(x => x.Id == lf.Fight.Arena),
            fighters = new[] { lf.Fight.Info(0), lf.Fight.Info(1) },
            buffs = new[] { lf.Fight.ActiveBuffs(0), lf.Fight.ActiveBuffs(1) },
            snap = lf.Fight.Snapshot(),
            ended = lf.Ended,
            result = lf.Ended ? ResultSummary(lf.Fight.Result!) : null
        });
    }

    private void ApplyResult(Player p, CombatResult result, int index, Player? opponent, int opponentBounty)
    {
        bool won = result.Winner == index;
        p.Money += result.Earnings[index];
        if (won)
        {
            if (opponent is { } victim && opponentBounty > 0)
            {
                p.Money += opponentBounty;
                p.Stats.BountiesCollected++;
                log?.Write("bounty", new { round, player = p.Id, from = victim.Id, amount = opponentBounty });
                AddSystemChat($"{p.Name} collected the {opponentBounty}$ bounty on {victim.Name}!");
            }
            p.WinStreak++;
            if (p.WinStreak == Player.BountyStreak) AddSystemChat($"{p.Name} is on a {p.WinStreak} win streak. Bounty: {p.Bounty}$");
            p.Money += (int)p.AugmentMods.Get("win_bonus_add") + (result.Arena == "colosseum" ? 5 : 0);
        }
        p.LastFightHpPercent = result.EndHp[index] / Math.Max(1, result.Fighters[index].MaxHp);
        p.LastFightDamageDealt = result.DamageDealt[index];
        if (won || result.Winner == -1) p.LossStreak = 0;
        else p.LossStreak++;
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
            log?.Write("elimination", new { round, player = ranked[i].Id, placement = ranked[i].Placement, build = BalanceLog.Build(ranked[i]) });
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

    /// <summary>The opponent who beat this player most often (at least twice).</summary>
    private object? Nemesis(Player p)
    {
        var top = p.Stats.LostTo.OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (top.Value < 2 || players.FirstOrDefault(o => o.Id == top.Key) is not { } rival) return null;
        return new { id = rival.Id, name = rival.Name, times = top.Value };
    }

    /// <summary>End-of-match awards: the best player in a few categories (only awarded when someone has a value).</summary>
    private List<object> Awards()
    {
        var list = new List<object>();
        void Add(string id, string name, string description, Func<Player, double> score, Func<double, string> show)
        {
            var best = players.OrderByDescending(score).ThenByDescending(p => p.Stats.TotalDamage).FirstOrDefault();
            if (best is null || score(best) <= 0) return;
            list.Add(new { id, name, description, player = best.Name, playerId = best.Id, value = show(score(best)), appearance = best.Appearance, kit = best.Kit });
        }
        Add("mvp", "MVP", "Most fights won", p => p.Stats.FightsWon, v => $"{v} wins");
        Add("heavy_hitter", "Heavy Hitter", "Biggest single hit", p => p.Stats.BiggestHit, v => $"{Math.Round(v)} damage");
        Add("destroyer", "Destroyer", "Most damage dealt", p => p.Stats.TotalDamage, v => $"{Math.Round(v)} total");
        Add("tank", "Iron Wall", "Most damage taken", p => p.Stats.TotalTaken, v => $"{Math.Round(v)} taken");
        Add("crit_machine", "Crit Machine", "Most critical hits", p => p.Stats.Crits, v => $"{v} crits");
        Add("medic", "Lifeline", "Most healing", p => p.Stats.Healed, v => $"{Math.Round(v)} healed");
        return list;
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
                build = p.BuildView(),
                kit = p.Kit,
                augments = p.Augments,
                appearance = p.Appearance,
                nemesis = Nemesis(p)
            })
            .ToList();
        var events = claimedEvents.Select(kv => new { id = kv.Key, by = kv.Value }).ToList();
        lastGameOver = new { type = "gameOver", rounds = round, standings, events, awards = Awards() };
        log?.Write("match_end", new { rounds = round, standings, events });
        log?.Dispose();
        log = null;
        Broadcast(lastGameOver);

        try
        {
            // Bots are never stored; matches with fewer than 2 humans are kept in history but don't count.
            long? matchId = Simulation ? null : database?.SaveMatch(new MatchRecord
            {
                LobbyCode = Code,
                StartedAt = matchStartedAt,
                EndedAt = DateTime.UtcNow,
                Rounds = round,
                Settings = Settings,
                Counted = players.Count(p => p.Id > 0) >= 2,
                Players = players.Where(p => p.Id > 0).Select(p => new MatchPlayerRecord
                {
                    UserId = p.Id,
                    DisplayName = p.Name,
                    Placement = p.Placement ?? players.Count,
                    Wins = p.Wins,
                    Losses = p.Losses,
                    Build = p.BuildView(),
                    Titles = p.Titles.ToList(),
                    Kit = p.Kit,
                    Augments = p.Augments.ToList()
                }).ToList()
            });
            if (matchId is long id) database!.SaveReplays(id, replays);
            replays.Clear();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[lobby {Code}] could not save match: {e.Message}");
        }

        ApplyProgression();
        State = LobbyState.Waiting;
        players.Clear();
        foreach (var m in Members.Where(m => !m.IsBot))
        {
            m.Mastery = database?.MasteryTitle(m.UserId);
            m.Appearance = database?.GetAppearance(m.UserId);
        }
        foreach (var m in Members.Where(m => !m.Connected && !m.IsBot).ToList()) RemoveMember(m, $"{m.Name} left.");
        AddSystemChat("The match is over. The host can start a rematch.");
        BroadcastLobby();
    }

    /// <summary>XP, achievements and cosmetics for every human, sent to their game over screen.</summary>
    private void ApplyProgression()
    {
        if (database is null) return;
        int humans = players.Count(p => p.Id > 0);
        bool soloVsHard = humans == 1 && Members.Where(m => m.IsBot).All(m => m.Difficulty == BotDifficulty.Hard) && players.Count > 1;
        foreach (var p in players.Where(p => p.Id > 0))
        {
            try
            {
                p.Stats.MaxMoney = Math.Max(p.Stats.MaxMoney, p.Money);
                p.Stats.MaxHp = Math.Max(p.Stats.MaxHp, p.MaxHealth);
                var result = database.ApplyMatch(p.Id, p.Stats, p.Placement ?? players.Count, players.Count, humans, soloVsHard, p.Augments.Count);
                MemberOf(p.Id)?.Connection?.Send(new { type = "progress", progress = result });
                log?.Write("progress", new { player = p.Id, result.XpGained, result.Level, result.NewAchievements });
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[lobby {Code}] progression failed for {p.Id}: {e.Message}");
            }
        }
    }

    public void RefreshAppearance(long userId)
    {
        lock (sync)
        {
            if (MemberOf(userId) is not { } m) return;
            m.Appearance = database?.GetAppearance(userId);
            if (FindPlayer(userId) is { } p) p.Appearance = m.Appearance;
            BroadcastLobby();
        }
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
            p.Stats.Rerolls++;
            p.Shop.Items = Shop.Roll(p, UnavailableSpecials(p), rng, RollOptions(p));
            LogRoll(p, p.Shop.RerollsLeft);
            SendState(p.Id);
            return null;
        }
    }

    public string? Buy(Connection conn, int slot, int? replace) => BuyFrom(conn, slot, replace, pity: false);

    public string? BuyPity(Connection conn, int slot, int? replace) => BuyFrom(conn, slot, replace, pity: true);

    /// <summary>Buys from the shop roll (locks rerolls) or from the pity offer (a third of the price, no lock).</summary>
    private string? BuyFrom(Connection conn, int slot, int? replace, bool pity)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            var slots = pity ? p.Shop.Pity : p.Shop.Items;
            if (slot < 0 || slot >= slots.Count) return "Invalid shop slot.";
            var shopSlot = slots[slot];
            if (shopSlot.Bought) return "Already bought.";
            var item = GameData.Items[shopSlot.ItemId];
            int price = shopSlot.Price ?? item.Price;
            if (p.Money < price) return "Not enough money.";
            if (item.IsSpecial && p.ObtainedSpecials.Contains(item.Id))
                return "You already got this special item.";
            if (item.IsSpecial && UnavailableSpecials(p).Contains(item.Id))
            {
                shopSlot.Bought = true;
                SendState(p.Id);
                return "Someone else already got this special item.";
            }

            int moneyBefore = p.Money;
            string kind;
            string? replaced = null;
            var owned = p.FindOwned(item.Id);
            if (owned is not null)
            {
                if (owned.Level >= MaxLevel) return "That item is already at max level.";
                owned.Level++;
                kind = "level_up";
            }
            else if (item.Type == ItemType.Weapon)
            {
                replaced = p.Weapon?.Id;
                p.Weapon = new OwnedItem(item);
                kind = replaced is null ? "new" : "replace";
            }
            else if (item.Type == ItemType.Shirt)
            {
                replaced = p.Shirt?.Id;
                p.Shirt = new OwnedItem(item);
                kind = replaced is null ? "new" : "replace";
            }
            else
            {
                int free = Array.IndexOf(p.Dynamics, null);
                if (free < 0)
                {
                    if (replace is not int r || r < 0 || r >= p.Dynamics.Length) return "Choose an item to replace.";
                    free = r;
                    replaced = p.Dynamics[r]?.Id;
                }
                p.Dynamics[free] = new OwnedItem(item);
                kind = replaced is null ? "new" : "replace";
            }

            p.Money -= price;
            shopSlot.Bought = true;
            var st = p.Stats;
            st.Spent += price;
            if (item.Id == Shop.DragonId) st.OwnedDragon = true;
            if (item.Type == ItemType.Shirt) st.EverShirt = true;
            if (pity && shopSlot.Source == "weaponless")
            {
                st.WeaponlessTaken = true;
                if (!weaponlessTakers.Contains(p.Name)) weaponlessTakers.Add(p.Name);
            }
            if (p.FindOwned(item.Id) is { } nowOwned && nowOwned.Level >= MaxLevel) st.MaxLevelReached = true;
            if (p.AllItems().Where(x => x.Item.IsSpecial).GroupBy(x => x.Item.Pack).Any(g => g.Select(x => x.Id).Distinct().Count() >= 5))
                st.FullPack = true;
            if (!pity) p.Shop.Locked = true;
            if (item.IsSpecial)
            {
                p.ObtainedSpecials.Add(item.Id);
                lobbySpecials.Add(item.Id);
            }
            log?.Write("purchase", new
            {
                round,
                player = p.Id,
                item = item.Id,
                price,
                pity,
                kind,
                replaced,
                level = p.FindOwned(item.Id)?.Level ?? 1,
                moneyBefore,
                moneyAfter = p.Money,
                rerollsLeft = p.Shop.RerollsLeft,
                shopSecondsLeft = Math.Round(Math.Max(0, (phaseEndsAt - DateTime.UtcNow).TotalSeconds), 1)
            });
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
            p.Stats.MaxHp = Math.Max(p.Stats.MaxHp, p.MaxHealth);
            log?.Write("hp_upgrade", new { round, player = p.Id, cost, upgrades = p.HpUpgrades, maxHp = Math.Round(p.MaxHealth, 2) });
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

    // ================= augments & mystery events =================

    private MysteryOffer? RollMystery(Player p)
    {
        var kinds = MysteryOffer.Kinds.Where(k => k switch
        {
            "shrine" => p.Lives > 1 && GameData.Specials.Any(i => !UnavailableSpecials(p).Contains(i.Id)),
            "fountain" => p.Lives < startingLives,
            "blacksmith" => p.AllItems().Any(),
            _ => true
        }).ToList();
        if (kinds.Count == 0) return null;
        var offer = new MysteryOffer { Kind = kinds[rng.Next(kinds.Count)] };
        if (offer.Kind == "shrine")
            offer.Choices = GameData.Specials.Where(i => !UnavailableSpecials(p).Contains(i.Id))
                .OrderBy(_ => rng.Next()).Take(3).Select(i => i.Id).ToList();
        return offer;
    }

    public string? PickAugment(Connection conn, int index)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            if (p.AugmentOffer is not { } offer || index < 0 || index >= offer.Count) return "No augment to pick.";
            var id = offer[index];
            var aug = GameData.Augments.First(a => a.Id == id);
            p.Augments.Add(id);
            p.AugmentOffer = null;
            p.Money += (int)aug.Mods.Get("money_now");
            p.Lives += (int)aug.Mods.Get("life_now");
            int slots = (int)aug.Mods.Get("dynamic_slots_add");
            if (slots > 0) p.Dynamics = p.Dynamics.Concat(new OwnedItem?[slots]).ToArray();
            log?.Write("augment_pick", new { round, player = p.Id, augment = id, tier = aug.Tier });
            if (aug.Tier == AugmentTier.Prismatic) p.Stats.Prismatic = true;
            SendState(p.Id);
            return null;
        }
    }

    /// <summary>Uses this shop's mystery event. arg: Shrine choice index, Gamble bet, or Blacksmith slot ("weapon", "shirt", "dynamic2").</summary>
    public string? UseMystery(Connection conn, string? arg)
    {
        lock (sync)
        {
            var p = ShopPlayer(conn, out var error);
            if (p is null) return error;
            if (p.Mystery is not { Used: false } m) return "There is no mystery event.";
            switch (m.Kind)
            {
                case "chest":
                {
                    if (p.Money < MysteryOffer.ChestCost) return "Not enough money.";
                    p.Money -= MysteryOffer.ChestCost;
                    var specials = GameData.Specials.Where(i => !UnavailableSpecials(p).Contains(i.Id)).ToList();
                    Item item = specials.Count > 0 && rng.NextDouble() < MysteryOffer.ChestSpecialChance
                        ? Shop.WeightedPick(specials, rng)!
                        : GameData.Generics[rng.Next(GameData.Generics.Count)];
                    p.Shop.Pity.Add(new ShopSlot { ItemId = item.Id, Price = 0, Source = "chest" });
                    m.Result = $"The chest had {item.Name}. Take it from your bonus offers.";
                    break;
                }
                case "shrine":
                {
                    if (!int.TryParse(arg, out int i) || i < 0 || i >= m.Choices.Count) return "Pick a special item.";
                    if (p.Lives <= 1) return "You need more than 1 life.";
                    p.Lives--;
                    p.Stats.Shrine = true;
                    p.Stats.MinLives = Math.Min(p.Stats.MinLives, p.Lives);
                    p.Shop.Pity.Add(new ShopSlot { ItemId = m.Choices[i], Price = 0, Source = "shrine" });
                    m.Result = $"You sacrificed a life for {GameData.Items[m.Choices[i]].Name}. Take it from your bonus offers.";
                    break;
                }
                case "gamble":
                {
                    if (!int.TryParse(arg, out int bet) || bet <= 0) return "Choose a bet.";
                    if (bet > p.Money / 2) return "You can bet at most half your money.";
                    bool won = rng.NextDouble() < 0.5;
                    p.Money += won ? bet : -bet;
                    if (won) p.Stats.GambleBestWin = Math.Max(p.Stats.GambleBestWin, bet);
                    m.Result = won ? $"You won {bet}$!" : $"You lost {bet}$.";
                    break;
                }
                case "blacksmith":
                {
                    var item = arg switch
                    {
                        "weapon" => p.Weapon,
                        "shirt" => p.Shirt,
                        _ when arg?.StartsWith("dynamic") == true && int.TryParse(arg[7..], out int d) && d >= 0 && d < p.Dynamics.Length => p.Dynamics[d],
                        _ => null
                    };
                    if (item is null) return "Pick one of your items.";
                    int cap = item.Item.IsSpecial ? MysteryOffer.BlacksmithSpecialMaxLevel : MaxLevel;
                    if (item.Level >= cap) return "That item can't be upgraded further here.";
                    if (p.Money < MysteryOffer.BlacksmithCost) return "Not enough money.";
                    p.Money -= MysteryOffer.BlacksmithCost;
                    item.Level++;
                    m.Result = $"{item.Item.Name} is now level {item.Level}.";
                    break;
                }
                case "fountain":
                    if (p.Lives >= startingLives) return "You already have full lives.";
                    if (p.Money < MysteryOffer.FountainCost) return "Not enough money.";
                    p.Money -= MysteryOffer.FountainCost;
                    p.Lives++;
                    m.Result = "You feel refreshed: +1 life.";
                    break;
                case "library":
                    p.FreeKitSwap = true;
                    p.Money += MysteryOffer.LibraryMoney;
                    m.Result = $"+{MysteryOffer.LibraryMoney}$ and your next kit change is free.";
                    break;
            }
            m.Used = true;
            log?.Write("mystery_use", new { round, player = p.Id, kind = m.Kind, arg, result = m.Result });
            CheckEvents(p, null);
            FlushAnnouncements();
            SendState(p.Id);
            return null;
        }
    }

    /// <summary>Q/W/E/R pressed during your fight.</summary>
    public void UseAbility(Connection conn, int slot)
    {
        lock (sync)
        {
            if (State != LobbyState.Combat) return;
            var lf = FightOf(conn.User.Id);
            if (lf is null || lf.Ended || lf.IndexOf(conn.User.Id) is not int index) return;
            lf.Fight.Queue(index, slot);
        }
    }

    /// <summary>Watch another fight: allowed once you're eliminated or your own fight is over.</summary>
    public string? Spectate(Connection conn, int fightId)
    {
        lock (sync)
        {
            if (State is not (LobbyState.Combat or LobbyState.Results)) return "There are no fights right now.";
            var own = FightOf(conn.User.Id);
            if (own is { Ended: false }) return "Finish your own fight first.";
            var lf = fights.FirstOrDefault(f => f.Id == fightId);
            if (lf is null) return "That fight doesn't exist.";
            foreach (var f in fights) f.Spectators.Remove(conn.User.Id);
            if (lf.IndexOf(conn.User.Id) is null) lf.Spectators.Add(conn.User.Id);
            if (MemberOf(conn.User.Id) is { } m) SendFightStart(m, lf, lf.IndexOf(conn.User.Id));
            return null;
        }
    }

    /// <summary>Kit choice in the waiting room (free).</summary>
    public string? PickKit(Connection conn, string kit)
    {
        lock (sync)
        {
            if (!GameData.Kits.ContainsKey(kit)) return "Unknown kit.";
            if (InMatch) return ChangeKitInShop(conn, kit);
            if (MemberOf(conn.User.Id) is not { } m) return "You are not in this lobby.";
            m.Kit = kit;
            BroadcastLobby();
            return null;
        }
    }

    /// <summary>Changing kits during the match costs money and only works in the shop.</summary>
    private string? ChangeKitInShop(Connection conn, string kit)
    {
        var p = ShopPlayer(conn, out var error);
        if (p is null) return error;
        if (p.Kit == kit) return null;
        int cost = p.FreeKitSwap ? 0 : KitSwapCost;
        if (p.Money < cost) return "Not enough money.";
        p.Money -= cost;
        p.FreeKitSwap = false;
        log?.Write("kit_change", new { round, player = p.Id, from = p.Kit, to = kit, cost });
        p.Kit = kit;
        if (MemberOf(p.Id) is { } m) m.Kit = kit;
        SendState(p.Id);
        return null;
    }

    // ================= lobby events =================

    private record FightOutcome(bool Won, bool Flawless);

    private void CheckEvents(Player p, FightOutcome? fight)
    {
        foreach (var ev in GameData.Events)
        {
            if (claimedEvents.ContainsKey(ev.Id) || !activeEvents.Contains(ev.Id)) continue;
            if (ev.Trigger == EventTrigger.WeaponlessStart) continue;
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
            log?.Write("event_claimed", new { round, player = p.Id, eventId = ev.Id });
            p.Stats.EventsClaimed++;
            p.Money += ev.RewardMoney ?? 0;
            p.BonusEffects.AddRange(ev.RewardEffects ?? new List<string>());
            p.Titles.Add(ev.Name);
            pendingAnnouncements.Add(ev.Id + "\n" + p.Id);
        }
    }

    private bool WeaponlessEventActive =>
        GameData.Events.Any(e => e.Trigger == EventTrigger.WeaponlessStart && activeEvents.Contains(e.Id));

    /// <summary>Banners for active events that announce themselves this round (e.g. Empty Hands in rounds 1-2).</summary>
    private void AnnounceRoundEvents()
    {
        foreach (var ev in GameData.Events)
        {
            if (!activeEvents.Contains(ev.Id) || ev.AnnounceRounds?.Contains(round) != true) continue;
            string text = ev.AnnounceText ?? $"{ev.Name}: {ev.Description}";
            Broadcast(new { type = "announce", eventId = ev.Id, name = ev.Name, text });
            AddSystemChat("Event: " + text);
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
        members = Members.Select(m => new { id = m.UserId, name = m.Name, connected = m.Connected || m.IsBot, kit = m.Kit, mastery = m.Mastery, bot = m.Difficulty, appearance = m.Appearance })
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
                modifier,
                fights = State is LobbyState.Combat or LobbyState.Results ? fights.Select(f => f.Summary()) : null,
                kitSwapCost = p?.FreeKitSwap == true ? 0 : KitSwapCost,
                mutators,
                maxLevel = MaxLevel,
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
                    specialChance = p.SpecialChanceWith(modifier?.Id, RollOptions(p).SpecialMult),
                    kit = p.Kit,
                    income = p.LastIncome,
                    lossStreak = p.LossStreak,
                    winStreak = p.WinStreak,
                    augments = p.Augments,
                    augmentOffer = p.AugmentOffer,
                    mystery = p.Mystery,
                    startingLives,
                    canSpectate = State is LobbyState.Combat or LobbyState.Results && (p.Eliminated || FightOf(p.Id) is not { Ended: false }),
                    weapon = p.Weapon is { } w ? new ItemView(w.Id, w.Level, "weapon") : null,
                    shirt = p.Shirt is { } s ? new ItemView(s.Id, s.Level, "shirt") : null,
                    dynamics = p.Dynamics.Select((d, i) => d is null ? null : new ItemView(d.Id, d.Level, $"dynamic{i}")),
                    shop = new { items = p.Shop.Items, pity = p.Shop.Pity, rerollsLeft = p.Shop.RerollsLeft, locked = p.Shop.Locked },
                    stats = Combat.PreviewStats(p.ToLoadout(), Rules),
                    // Stats if each shop item were bought (null when it couldn't go in without a swap).
                    shopPreview = p.Shop.Items.Select(i => i.Bought ? null : PreviewWith(p, i.ItemId)),
                    titles = p.Titles,
                    wins = p.Wins,
                    losses = p.Losses
                },
                sim = member.Observer ? SimView() : null,
                players = players.Select(o => new
                {
                    id = o.Id,
                    name = o.Name,
                    ready = o.Ready,
                    alive = !o.Eliminated,
                    connected = o.Connected || o.Id < 0,
                    bot = o.Id < 0,
                    kit = o.Kit,
                    titles = o.Titles,
                    bounty = o.Bounty,
                    appearance = o.Appearance,
                    mastery = MemberOf(o.Id)?.Mastery
                }),
                events = GameData.Events.Where(e => activeEvents.Contains(e.Id)).Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    description = e.Description,
                    reward = e.Reward,
                    special = e.Chance is not null,
                    claimedBy = e.Trigger == EventTrigger.WeaponlessStart
                        ? (weaponlessTakers.Count > 0 ? string.Join(", ", weaponlessTakers) : null)
                        : claimedEvents.TryGetValue(e.Id, out var by) ? FindPlayer(by)?.Name : null
                })
            }
        });
    }

}
