namespace Ashen;

/// <summary>
/// Admin simulations: a bot-only match with tweakable settings that an admin watches as an observer,
/// at adjustable speed (or paused). Simulated matches are never saved to the history.
/// </summary>
public partial class Lobby
{
    public const double SimShopSeconds = 6;
    public static readonly double[] SimSpeeds = { 0, 0.5, 1, 2, 4, 8, 16 };

    public bool Simulation { get; private set; }
    /// <summary>Game speed of a simulation: 1 = real time, 0 = paused.</summary>
    public double SimSpeed { get; private set; } = 1;
    private bool SimMutators { get; set; } = true;
    /// <summary>Bot the observer follows into fights (null = the first fight).</summary>
    private long? simFollow;
    private double simFightClock;

    /// <summary>Phase lengths (countdown, results, shop viewing time) shrink with the simulation speed.</summary>
    private double SimSeconds(double seconds) => Simulation ? seconds / Math.Max(0.5, SimSpeed) : seconds;

    public string? StartSimulation(Connection conn, LobbySettings settings, int bots, BotDifficulty[] difficulties, bool mutatorsOn, double speed)
    {
        lock (sync)
        {
            var me = Members.FirstOrDefault(m => m.UserId == conn.User.Id);
            if (me is null) return "You are not in this lobby.";
            me.Observer = true;
            Simulation = true;
            SimMutators = mutatorsOn;
            SimSpeed = SimSpeeds.Contains(speed) ? speed : 1;
            Settings = settings.Sanitized();
            Settings.Public = false;
            bots = Math.Clamp(bots, MinPlayers, MaxPlayers);
            for (int i = 0; i < bots; i++)
                AddBotUnlocked(difficulties.Length == 0 ? BotDifficulty.Normal : difficulties[i % difficulties.Length]);
            AddSystemChat($"Simulation: {bots} bots, speed {SimSpeed}x.");
            BroadcastLobby();
            return Start(conn);
        }
    }

    public string? SetSimSpeed(Connection conn, double speed)
    {
        lock (sync)
        {
            if (!Simulation) return "This isn't a simulation.";
            if (conn.User.Id != HostId) return "Only the admin running the simulation can change its speed.";
            if (!SimSpeeds.Contains(speed)) return "Unknown speed.";
            SimSpeed = speed;
            BroadcastState();
            return null;
        }
    }

    /// <summary>Observer picks which bot to follow into the next fights (and jumps into its current fight).</summary>
    public string? SimFollow(Connection conn, long playerId)
    {
        lock (sync)
        {
            if (!Simulation) return "This isn't a simulation.";
            simFollow = playerId;
            if (State == LobbyState.Combat && FightOf(playerId) is { } lf && MemberOf(conn.User.Id) is { } m)
            {
                foreach (var f in fights) f.Spectators.Remove(m.UserId);
                lf.Spectators.Add(m.UserId);
                SendFightStart(m, lf, null);
            }
            BroadcastState();
            return null;
        }
    }

    /// <summary>Puts observers into the followed bot's fight (or the first one) when fights start.</summary>
    private void FollowFights()
    {
        if (!Simulation || fights.Count == 0) return;
        var lf = (simFollow is long id ? FightOf(id) : null) ?? fights[0];
        foreach (var m in Members.Where(m => m.Observer))
        {
            foreach (var f in fights) f.Spectators.Remove(m.UserId);
            lf.Spectators.Add(m.UserId);
            SendFightStart(m, lf, null);
        }
    }

    /// <summary>Everything about every bot, for the observer's overview.</summary>
    private object SimView() => new
    {
        speed = SimSpeed,
        speeds = SimSpeeds,
        follow = simFollow,
        mutatorsOn = SimMutators,
        bots = players.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            kit = p.Kit,
            difficulty = MemberOf(p.Id)?.Difficulty,
            money = p.Money,
            lives = p.Lives,
            eliminated = p.Eliminated,
            placement = p.Placement,
            wins = p.Wins,
            losses = p.Losses,
            maxHp = Math.Round(p.MaxHealth),
            hpUpgrades = p.HpUpgrades,
            specialChance = Math.Round(p.SpecialChanceWith(modifier?.Id, RollOptions(p).SpecialMult), 4),
            build = p.BuildView(),
            augments = p.Augments,
            stats = Combat.PreviewStats(p.ToLoadout(), Rules)
        })
    };
}
