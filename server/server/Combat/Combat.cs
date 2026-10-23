namespace Ashen;

/// <summary>Round-wide settings a fight is simulated with.</summary>
public class FightRules
{
    /// <summary>Active round modifier id (modifiers.json), or null.</summary>
    public string? Modifier { get; set; }
    /// <summary>Arena of this fight (arenas.json), or null for a plain fight.</summary>
    public string? Arena { get; set; }
    /// <summary>Extra damage multiplier for everyone (Double Trouble mutator).</summary>
    public double DamageMult { get; set; } = 1;
    public double OvertimeStart { get; set; } = Combat.OvertimeStart;
    public double SuddenDeathStart { get; set; } = Combat.SuddenDeathStart;
    public bool Has(string id) => Modifier == id;
    public bool In(string arena) => Arena == arena;

    public FightRules WithArena(string? arena) => new()
    {
        Modifier = Modifier, Arena = arena, DamageMult = DamageMult,
        OvertimeStart = OvertimeStart, SuddenDeathStart = SuddenDeathStart
    };
}

/// <summary>
/// Tick based fight simulation. Fights run on frozen <see cref="Loadout"/>s, so nothing a fight
/// does (stacks, cooldowns, HP) leaks back into the players' builds.
/// </summary>
public static class Combat
{
    public static int TPS { get; set; } = 20;
    public static double SPT => 1.0 / TPS;

    public const double MaxDamageReduction = 0.65;
    public const double MaxDodgeChance = 0.45;
    /// <summary>Abilities (Brace, Smoke, ...) can push past the normal caps, up to these.</summary>
    public const double AbilityMaxDamageReduction = 0.85;
    public const double AbilityMaxDodgeChance = 0.75;
    public const double MinCooldown = 0.1;
    public const double OvertimeStart = 30;
    public const double OvertimeRampPerSecond = 0.1;
    public const double SuddenDeathStart = 60;
    /// <summary>When neither fighter has a weapon, overtime and sudden death come much sooner.</summary>
    public const double NoWeaponOvertimeStart = 5;
    public const double NoWeaponSuddenDeathStart = 10;
    /// <summary>The ultimate is charged after dealing + taking this share of your max HP in damage.</summary>
    public const double UltChargeFraction = 0.4;
    /// <summary>Safety net: a fight never runs longer than this (sudden death ends it long before).</summary>
    public const double HardLimit = 180;

    /// <summary>Simulates a whole fight at once. Fighters with a kit use the bot for their abilities.</summary>
    public static CombatResult CalculateCombatResults(Loadout l1, Loadout l2, int? seed = null, FightRules? rules = null)
    {
        var fight = new Fight(l1, l2, rules ?? new FightRules(), seed ?? Random.Shared.Next());
        fight.SetBot(0, true);
        fight.SetBot(1, true);
        while (!fight.Finished) fight.Step();
        return fight.Result!;
    }

    /// <summary>Stats shown in the shop's stat panel: static buffs applied, at full HP.</summary>
    public static Dictionary<string, double> PreviewStats(Loadout loadout, FightRules? rules = null)
    {
        var fight = new Fight(loadout, new Loadout(), rules ?? new FightRules(), 0);
        return fight.PreviewStats(0);
    }

    /// <summary>Weapon damage bonus for owning 2/3/4+ distinct items of the same special pack.</summary>
    public static double SynergyMult(Loadout loadout, out ItemPack? pack)
    {
        var best = loadout.AllItems().Where(i => i.Item.IsSpecial)
            .GroupBy(i => i.Item.Pack)
            .Select(g => (Pack: g.Key, Count: g.Select(i => i.Id).Distinct().Count()))
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();
        pack = best.Count >= 2 ? best.Pack : null;
        return best.Count switch
        {
            >= 4 => 1.15,
            3 => 1.10,
            2 => 1.05,
            _ => 1
        };
    }
}

/// <summary>One fight. Call <see cref="Step"/> once per tick; abilities queued with <see cref="Queue"/> fire on the next tick.</summary>
public sealed class Fight
{
    private const double tiny = 0.0001;

    private enum ModKind { DamageDealt, DamageTaken, WeaponCooldown, Dodge, DamageReduction, CritChance, Lifesteal, StunImmune }

    private sealed class TempMod
    {
        public string Source { get; init; } = "";
        public ModKind Kind { get; init; }
        public double Value { get; init; }
        public double ExpiresAt { get; init; }
        /// <summary>Put on this fighter by the enemy (removed by Cleanse).</summary>
        public bool Debuff { get; init; }
    }

    private sealed class Fighter
    {
        public int Index { get; }
        public Loadout Loadout { get; }
        public Random Rng { get; }
        public double MaxHealth { get; }
        public double Health { get; set; }
        public double StunLeft { get; set; }
        public double InvulnerableLeft { get; set; }
        public OwnedItem? Weapon => Loadout.Weapon;
        public double WeaponTimer { get; set; }
        public double LastWeaponCooldown { get; set; } = 1;
        public List<EffectState> Effects { get; } = new();
        public double Earnings { get; set; }
        public double DamageDealt { get; set; }
        public double DamageTaken { get; set; }

        // ----- kit -----
        public Kit? Kit { get; }
        public bool Bot { get; set; }
        public double[] SkillCooldowns { get; } = new double[3];
        public double UltCharge { get; set; }
        public Queue<int> Inputs { get; } = new();
        public List<TempMod> Mods { get; } = new();
        public double ParryUntil { get; set; } = -1;
        public double ParryReflect { get; set; }
        public int MissNextAttacks { get; set; }
        public bool NextHitCrit { get; set; }
        public double? NextHitMult { get; set; }
        public bool NextHitCoinFlip { get; set; }
        public int AllInHits { get; set; }
        public double Shield { get; set; }
        public double ShieldUntil { get; set; }
        public double BulwarkReflect { get; set; }
        public double? HeadshotAt { get; set; }
        public List<double> FlurryAttacks { get; } = new();
        public IReadOnlyDictionary<string, double> Aug => Loadout.AugmentMods;
        public bool SecondWindUsed { get; set; }
        public bool FirstStrikeUsed { get; set; }

        public bool Stunned => StunLeft > tiny;
        public bool Invulnerable => InvulnerableLeft > tiny;

        public Fighter(int index, Loadout loadout, Random rng, FightRules rules)
        {
            Index = index;
            Loadout = loadout;
            Rng = rng;
            MaxHealth = loadout.MaxHealth * (rules.Has("glass_cannon") ? 0.7 : 1) * loadout.AugmentMods.Get("max_hp_mult");
            Health = MaxHealth;
            Bot = loadout.Bot;
            Kit = loadout.Kit is not null && GameData.Kits.TryGetValue(loadout.Kit, out var kit) ? kit : null;
            foreach (var item in loadout.AllItems())
                foreach (var effect in item.Item.Effects)
                    Effects.Add(new EffectState(effect, item, item.EffectScale));
            foreach (var id in loadout.BonusEffects)
                if (GameData.Effects.TryGetValue(id, out var effect))
                    Effects.Add(new EffectState(effect, null, 1));
        }

        public double ModSum(ModKind kind) => Mods.Where(m => m.Kind == kind).Sum(m => m.Value);
        public double ModProduct(ModKind kind) => Mods.Where(m => m.Kind == kind).Aggregate(1.0, (a, m) => a * m.Value);
        public bool HasMod(ModKind kind) => Mods.Any(m => m.Kind == kind);
    }

    private sealed class EffectState
    {
        public Effect Effect { get; }
        public OwnedItem? Source { get; }
        public double Scale { get; }
        public double CurrentCooldown { get; set; }
        public int Stacks { get; set; }
        public double ExpiresAt { get; set; } = double.PositiveInfinity;
        public int TriggerCount { get; set; }
        public double Payout { get; set; }
        public bool? WasActive { get; set; }

        public EffectState(Effect effect, OwnedItem? source, double scale)
        {
            Effect = effect;
            Source = source;
            Scale = scale;
            Payout = (effect.StartMoney ?? 0) * scale;
        }

        public double? ScaledMult => Effect.Mult is double m ? 1 + (m - 1) * Scale : null;
        public double? ScaledAdd => Effect.Add * Scale;
    }

    private enum HitKind { Weapon, Effect, Ability }

    private readonly Fighter[] fighters;
    private readonly FightRules rules;
    private readonly List<CombatEvent> events = new();
    private int emitted;
    private int tick;
    private readonly double overtimeStart;
    private readonly double suddenDeathStart;
    private readonly FighterInfo[] infos;
    private double nextLightning = 5;
    private readonly string?[] killingBlow = new string?[2];

    public string? Arena => rules.Arena;
    public bool Finished { get; private set; }
    public CombatResult? Result { get; private set; }
    public double Time => tick * Combat.SPT;
    public IReadOnlyList<CombatEvent> Events => events;

    public Fight(Loadout a, Loadout b, FightRules rules, int seed)
    {
        this.rules = rules;
        var rng = new Random(seed);
        fighters = new[] { new Fighter(0, a, new Random(rng.Next()), rules), new Fighter(1, b, new Random(rng.Next()), rules) };
        bool noWeapons = a.Weapon is null && b.Weapon is null;
        overtimeStart = noWeapons ? Math.Min(Combat.NoWeaponOvertimeStart, rules.OvertimeStart) : rules.OvertimeStart;
        suddenDeathStart = noWeapons ? Math.Min(Combat.NoWeaponSuddenDeathStart, rules.SuddenDeathStart) : rules.SuddenDeathStart;
        foreach (var f in fighters)
        {
            f.WeaponTimer = f.Weapon is null ? double.PositiveInfinity : WeaponCooldown(f);
            f.LastWeaponCooldown = f.Weapon is null ? 1 : f.WeaponTimer;
            foreach (var es in f.Effects.Where(e => e.Effect.Trigger == TriggerKind.Cooldown))
                es.CurrentCooldown = EffectCooldown(f, es);
        }
        infos = fighters.Select(Describe).ToArray();
        foreach (var f in fighters)
        {
            if (f.Aug.Get("start_shield") is > 0 and var shield)
            {
                f.Shield = shield * f.MaxHealth;
                f.ShieldUntil = double.PositiveInfinity;
            }
        }
        foreach (var f in fighters)
            if (f.Aug.Get("opening_stun") is > 0 and var stun)
                Stun(f, Opponent(f), stun, new StunEvent { Source = f.Index, Target = 1 - f.Index, EffectSource = "augment.concussion" });
        foreach (var f in fighters) ApplyRoundStartEffects(f);
        foreach (var f in fighters) UpdateConditionalBuffs(f, report: false);
    }

    public FighterInfo Info(int index) => infos[index];

    public void SetBot(int index, bool bot) => fighters[index].Bot = bot;

    /// <summary>Queues an ability press (0-2 = Q/W/E, 3 = R ultimate). Ignored if it isn't usable on the next tick.</summary>
    public void Queue(int index, int slot)
    {
        if (Finished || slot is < 0 or > 3) return;
        var f = fighters[index];
        if (f.Inputs.Count < 4) f.Inputs.Enqueue(slot);
    }

    /// <summary>Events produced since the last call (for streaming to the players).</summary>
    public List<CombatEvent> TakeNewEvents()
    {
        var list = events.Skip(emitted).ToList();
        emitted = events.Count;
        return list;
    }

    private Fighter Opponent(Fighter f) => fighters[1 - f.Index];

    // ================= tick =================

    public void Step()
    {
        if (Finished) return;
        tick++;
        var hpBefore = fighters.Select(f => f.Health).ToArray();

        foreach (var f in fighters) TickTimers(f);
        var order = tick % 2 == 0 ? fighters : fighters.Reverse().ToArray();
        foreach (var f in order)
        {
            if (f.Bot) BotThink(f);
            while (f.Inputs.Count > 0) UseAbility(f, f.Inputs.Dequeue());
        }
        foreach (var f in order) TickScheduled(f);
        foreach (var f in order) TryWeaponAttack(f);
        foreach (var f in order) TryTriggerCooldownEffects(f);
        ApplyArenaAndRegen();
        if (Time >= suddenDeathStart - tiny) ApplySuddenDeath();
        foreach (var f in fighters) UpdateConditionalBuffs(f, report: true);

        bool dead0 = fighters[0].Health <= 0, dead1 = fighters[1].Health <= 0;
        int? winner = null;
        if (dead0 || dead1)
            winner = dead0 && dead1
                ? (hpBefore[0] > hpBefore[1] ? 0 : hpBefore[1] > hpBefore[0] ? 1 : -1)
                : dead0 ? 1 : 0;
        else if (Time >= Combat.HardLimit)
        {
            var p0 = fighters[0].Health / fighters[0].MaxHealth;
            var p1 = fighters[1].Health / fighters[1].MaxHealth;
            winner = p0 > p1 ? 0 : p1 > p0 ? 1 : -1;
        }
        if (winner is int w) Finish(w);
    }

    private void Finish(int winner)
    {
        Finished = true;
        var result = new CombatResult
        {
            Fighters = infos,
            Events = events,
            Winner = winner,
            Duration = Math.Round(Time, 2),
            Arena = rules.Arena,
            KillingBlow = killingBlow.ToArray()
        };
        for (int i = 0; i < 2; i++)
        {
            var f = fighters[i];
            result.EndHp[i] = Math.Max(0, Math.Round(f.Health, 1));
            result.DamageDealt[i] = Math.Round(f.DamageDealt, 1);
            result.DamageTaken[i] = Math.Round(f.DamageTaken, 1);
            var payouts = f.Effects.Where(e => e.Effect.Type == EffectType.Gamble).Sum(e => e.Payout);
            result.Earnings[i] = (int)Math.Round(f.Earnings + payouts);
        }
        Result = result;
    }

    /// <summary>Compact per-tick state for the live view: HP, timers, cooldowns, ult charge, active ability effects.</summary>
    public object Snapshot() => new
    {
        t = Math.Round(Time, 2),
        overtime = Time > overtimeStart ? Math.Round(OvertimeMultiplier, 2) : (double?)null,
        suddenDeath = Time >= suddenDeathStart,
        f = fighters.Select(f => new
        {
            hp = Math.Max(0, Math.Round(f.Health, 1)),
            maxHp = Math.Round(f.MaxHealth, 1),
            shield = Math.Round(f.Shield, 1),
            weapon = f.Weapon is null ? 0 : Math.Round(Math.Clamp(1 - f.WeaponTimer / Math.Max(0.05, f.LastWeaponCooldown), 0, 1), 3),
            stun = Math.Round(f.StunLeft, 2),
            invuln = Math.Round(f.InvulnerableLeft, 2),
            ult = Math.Round(f.UltCharge, 3),
            cds = f.SkillCooldowns.Select(c => Math.Round(Math.Max(0, c), 2)),
            windup = f.HeadshotAt is double h ? Math.Round(h - Time, 2) : (double?)null,
            mods = f.Mods.GroupBy(m => m.Source).Select(g => new { id = g.Key, left = Math.Round(g.Max(m => m.ExpiresAt) - Time, 1), debuff = g.First().Debuff }),
            parry = f.ParryUntil >= Time
        })
    };

    /// <summary>Item buffs active right now, so someone joining mid-fight sees the same list.</summary>
    public List<BuffEvent> ActiveBuffs(int index)
    {
        var f = fighters[index];
        var list = new List<BuffEvent>();
        foreach (var es in f.Effects.Where(e => e.Effect.Type == EffectType.Buff))
        {
            int n = ActiveStacks(f, es);
            var target = ResolveTargetName(f, es);
            if (n > 0 && target is not null) list.Add(MakeBuffEvent(f, es, n, target));
        }
        if (SynergyBuff(f) is { } syn) list.Add(syn);
        return list;
    }

    private FighterInfo Describe(Fighter f)
    {
        var info = new FighterInfo
        {
            Name = f.Loadout.Name,
            PlayerId = f.Loadout.PlayerId,
            Ghost = f.Loadout.Ghost,
            Kit = f.Kit?.Id,
            Augments = f.Loadout.AugmentIds.ToList(),
            Appearance = f.Loadout.Appearance,
            MaxHp = Math.Round(f.MaxHealth, 1),
            DamageReduction = Math.Round(DamageReduction(f), 4),
            DodgeChance = Math.Round(DodgeChance(f), 4)
        };
        if (f.Loadout.Weapon is { } w) info.Items.Add(new ItemView(w.Id, w.Level, "weapon"));
        if (f.Loadout.Shirt is { } s) info.Items.Add(new ItemView(s.Id, s.Level, "shirt"));
        foreach (var d in f.Loadout.Dynamics)
            if (d is not null) info.Items.Add(new ItemView(d.Id, d.Level, "dynamic"));
        foreach (var es in f.Effects.Where(e => e.Effect.Type == EffectType.Buff
                     && e.Effect.Trigger == TriggerKind.Static && e.Effect.Condition?.HPBelow is null))
        {
            var target = ResolveTargetName(f, es);
            if (target is null) continue;
            info.StartBuffs.Add(MakeBuffEvent(f, es, 1, target));
        }
        if (SynergyBuff(f) is { } syn) info.StartBuffs.Add(syn);
        return info;
    }

    private BuffEvent? SynergyBuff(Fighter f)
    {
        double mult = Combat.SynergyMult(f.Loadout, out var pack);
        if (pack is null || f.Weapon is null) return null;
        return new BuffEvent
        {
            Source = f.Index, Target = f.Index, EffectSource = $"synergy.{pack.ToString()!.ToLowerInvariant()}",
            Stat = StatNames.Damage, BuffTarget = f.Weapon.Id, Stacks = 1, Mult = mult,
            T = Math.Round(Time, 2), Tick = tick, Hp = Hp()
        };
    }

    public Dictionary<string, double> PreviewStats(int index)
    {
        var f = fighters[index];
        var stats = new Dictionary<string, double>
        {
            ["max_hp"] = f.MaxHealth,
            ["damage_reduction"] = DamageReduction(f),
            ["dodge_chance"] = DodgeChance(f)
        };
        if (f.Weapon is not null)
        {
            stats["damage"] = WeaponStat(f, StatNames.Damage);
            stats["cooldown"] = WeaponCooldown(f);
            stats["crit_chance"] = WeaponStat(f, StatNames.CritChance);
            stats["crit_damage"] = WeaponStat(f, StatNames.CritDamage);
        }
        return stats;
    }

    // ================= stats =================

    private int ActiveStacks(Fighter f, EffectState es)
    {
        if (es.Effect.Trigger != TriggerKind.Static) return es.Stacks;
        if (es.Effect.Condition?.HPBelow is double below)
            return f.Health / f.MaxHealth < below ? 1 : 0;
        return 1;
    }

    /// <summary>Applies every item buff on <paramref name="f"/> for <paramref name="stat"/> whose target matches.
    /// Stacks of the same effect add up, different effects multiply, flat adds always add.</summary>
    private double Modify(Fighter f, double baseValue, string stat, Func<string, bool> targets)
    {
        double add = 0, mult = 1;
        foreach (var es in f.Effects)
        {
            var e = es.Effect;
            if (e.Type != EffectType.Buff || e.Stat != stat || e.Target is null || !targets(e.Target)) continue;
            int n = ActiveStacks(f, es);
            if (n == 0) continue;
            if (es.ScaledAdd is double a) add += a * n;
            if (es.ScaledMult is double m) mult *= Math.Max(0, 1 + n * (m - 1));
        }
        return (baseValue + add) * mult;
    }

    private static bool TargetsItem(Fighter f, OwnedItem item, string target) =>
        target == item.Id || (target == "type.weapon" && ReferenceEquals(item, f.Weapon));

    private double ItemStat(Fighter f, OwnedItem item, string stat) =>
        Modify(f, item.Stat(stat), stat, t => TargetsItem(f, item, t));

    private double CooldownMult => (rules.Has("overclock") ? 0.8 : 1) * (rules.In("ice") ? 1.15 : 1);

    private double WeaponStat(Fighter f, string stat)
    {
        if (f.Weapon is null) return 0;
        var value = ItemStat(f, f.Weapon, stat);
        return stat switch
        {
            StatNames.Cooldown => Math.Max(Combat.MinCooldown, value),
            StatNames.CritChance => Math.Clamp(value + f.ModSum(ModKind.CritChance) + f.Aug.Get("crit_chance_add") + (rules.In("forge") ? 0.1 : 0), 0, 1),
            StatNames.CritDamage => Math.Max(1, value) * (rules.Has("blood_moon") ? 1.5 : 1) * (rules.In("forge") ? 1.2 : 1) * f.Aug.Get("crit_damage_mult"),
            StatNames.Damage => Math.Max(0, value) * Combat.SynergyMult(f.Loadout, out _) * (rules.Has("rust") ? 0.75 : 1) * f.Aug.Get("weapon_damage_mult"),
            _ => Math.Max(0, value)
        };
    }

    private double WeaponCooldown(Fighter f) =>
        Math.Max(Combat.MinCooldown, WeaponStat(f, StatNames.Cooldown) * CooldownMult * f.ModProduct(ModKind.WeaponCooldown) * f.Aug.Get("weapon_cooldown_mult"));

    private double EffectStat(Fighter f, EffectState es, string stat, double baseValue) =>
        Modify(f, baseValue, stat, t => t == es.Effect.Id);

    private double EffectCooldown(Fighter f, EffectState es) =>
        Math.Max(Combat.MinCooldown, EffectStat(f, es, StatNames.Cooldown, es.Effect.Cooldown ?? 0) * CooldownMult);

    private double DamageReduction(Fighter f)
    {
        double dr = Math.Clamp(f.Loadout.AllItems().Sum(i => ItemStat(f, i, StatNames.DamageReduction)) + f.Aug.Get("damage_reduction_add"), 0, Combat.MaxDamageReduction);
        if (rules.Has("fragile")) dr *= 0.5;
        return Math.Clamp(dr + f.ModSum(ModKind.DamageReduction), 0, Combat.AbilityMaxDamageReduction);
    }

    private double DodgeChance(Fighter f)
    {
        double dodge = f.Loadout.AllItems().Sum(i => ItemStat(f, i, StatNames.DodgeChance)) + (rules.Has("fog") ? 0.15 : 0)
                       + (rules.In("gale") ? 0.12 : 0) + f.Aug.Get("dodge_add");
        dodge = Math.Clamp(dodge, 0, Combat.MaxDodgeChance);
        return Math.Clamp(dodge + f.ModSum(ModKind.Dodge), 0, Combat.AbilityMaxDodgeChance);
    }

    private double OvertimeMultiplier =>
        Time > overtimeStart ? 1 + Combat.OvertimeRampPerSecond * (Time - overtimeStart) : 1;

    private double GlobalDamageMult => (rules.Has("glass_cannon") ? 1.5 : 1) * rules.DamageMult;

    // ================= timers =================

    private void TickTimers(Fighter f)
    {
        if (f.InvulnerableLeft > 0)
        {
            f.InvulnerableLeft = Math.Max(0, f.InvulnerableLeft - Combat.SPT);
            if (f.InvulnerableLeft <= 0) f.BulwarkReflect = 0;
        }
        for (int i = 0; i < 3; i++) f.SkillCooldowns[i] = Math.Max(0, f.SkillCooldowns[i] - Combat.SPT);
        f.Mods.RemoveAll(m => m.ExpiresAt <= Time + tiny);
        if (f.Shield > 0 && f.ShieldUntil <= Time + tiny) f.Shield = 0;

        foreach (var es in f.Effects)
        {
            if (es.Stacks > 0 && es.ExpiresAt <= Time + tiny)
            {
                es.Stacks = 0;
                es.ExpiresAt = double.PositiveInfinity;
                var target = ResolveTargetName(f, es);
                if (target is not null) Emit(MakeBuffEvent(f, es, 0, target));
            }
        }

        if (f.Stunned)
        {
            f.StunLeft = Math.Max(0, f.StunLeft - Combat.SPT);
            return;
        }
        f.WeaponTimer -= Combat.SPT;
        foreach (var es in f.Effects.Where(e => e.Effect.Trigger == TriggerKind.Cooldown))
            es.CurrentCooldown -= Combat.SPT;
    }

    /// <summary>Flurry attacks and the Headshot wind-up.</summary>
    private void TickScheduled(Fighter f)
    {
        while (f.FlurryAttacks.Count > 0 && f.FlurryAttacks[0] <= Time + tiny)
        {
            f.FlurryAttacks.RemoveAt(0);
            WeaponAttack(f, "flurry");
        }
        if (f.HeadshotAt is double at && at <= Time + tiny)
        {
            f.HeadshotAt = null;
            var target = Opponent(f);
            var ev = new DamageEvent { Source = f.Index, Target = target.Index, AbilitySource = "headshot" };
            double fraction = AbilityOf(f, "headshot")?.Fraction ?? 0.4;
            Hit(f, target, target.Health * fraction, HitKind.Ability, ev, applyReduction: false);
        }
    }

    private void TryWeaponAttack(Fighter attacker)
    {
        if (attacker.Stunned || attacker.Weapon is null || attacker.WeaponTimer > tiny) return;
        double cd = WeaponCooldown(attacker);
        attacker.LastWeaponCooldown = cd;
        attacker.WeaponTimer = Math.Max(0, attacker.WeaponTimer + cd);
        WeaponAttack(attacker, null);
        if (attacker.Aug.Get("double_strike") is > 0 and var chance && attacker.Rng.NextDouble() < chance)
            WeaponAttack(attacker, "double_strike");
    }

    /// <summary>One weapon swing: Feint/All In misses, dodge, crit, ability hit multipliers, then on-hit triggers.</summary>
    private void WeaponAttack(Fighter attacker, string? ability)
    {
        var weapon = attacker.Weapon;
        if (weapon is null) return;
        var defender = Opponent(attacker);
        var ev = new DamageEvent { ItemSource = weapon.Id, WeaponAttack = ability is null, AbilitySource = ability, Source = attacker.Index, Target = defender.Index };

        if (attacker.MissNextAttacks > 0)
        {
            attacker.MissNextAttacks--;
            ev.Missed = true;
            Emit(ev);
            return;
        }
        double hitMult = 1;
        if (attacker.AllInHits > 0)
        {
            attacker.AllInHits--;
            var allIn = AbilityOf(attacker, "all_in");
            if (attacker.Rng.NextDouble() >= (allIn?.Chance ?? 0.5))
            {
                ev.Missed = true;
                Emit(ev);
                return;
            }
            hitMult *= allIn?.Mult ?? 3;
        }
        if (defender.Invulnerable || (defender.ParryUntil >= Time))
        {
            // Resolved in Hit (blocked / parried); no dodge or crit roll.
        }
        else if (defender.Rng.NextDouble() < DodgeChance(defender))
        {
            ev.Dodged = true;
            Emit(ev);
            return;
        }

        bool crit = attacker.NextHitCrit || attacker.Rng.NextDouble() < WeaponStat(attacker, StatNames.CritChance);
        attacker.NextHitCrit = false;
        double critMult = crit ? WeaponStat(attacker, StatNames.CritDamage) : 1;
        if (attacker.NextHitMult is double m)
        {
            hitMult *= m;
            attacker.NextHitMult = null;
        }
        if (attacker.NextHitCoinFlip)
        {
            attacker.NextHitCoinFlip = false;
            var flip = AbilityOf(attacker, "coin_flip");
            hitMult *= attacker.Rng.NextDouble() < 0.5 ? flip?.WinMult ?? 2 : flip?.LoseMult ?? 0.5;
        }
        if (!attacker.FirstStrikeUsed)
        {
            attacker.FirstStrikeUsed = true;
            hitMult *= attacker.Aug.Get("first_strike") is > 0 and var fs ? fs : 1;
        }
        ev.CritMultiplier = crit ? Math.Round(critMult, 3) : null;
        bool landed = Hit(attacker, defender, WeaponStat(attacker, StatNames.Damage) * critMult * hitMult, HitKind.Weapon, ev);
        if (!landed) return;

        FireTriggers(attacker, TriggerKind.Hit, weapon.Id);
        if (crit) FireTriggers(attacker, TriggerKind.Crit, weapon.Id);
    }

    /// <summary>
    /// Applies damage from <paramref name="attacker"/> to <paramref name="defender"/>: invulnerability (Bulwark reflects),
    /// Parry, damage multipliers, damage reduction, overtime, Shield, lifesteal and ultimate charge.
    /// Returns whether the hit landed (for on-hit triggers).
    /// </summary>
    private bool Hit(Fighter attacker, Fighter defender, double raw, HitKind kind, DamageEvent ev, bool applyReduction = true)
    {
        if (defender.Invulnerable)
        {
            ev.Blocked = true;
            Emit(ev);
            if (defender.BulwarkReflect > 0) Reflect(defender, attacker, raw * defender.BulwarkReflect, "bulwark");
            return false;
        }
        if (defender.ParryUntil >= Time)
        {
            defender.ParryUntil = -1;
            ev.Parried = true;
            Emit(ev);
            if (defender.ParryReflect > 0) Reflect(defender, attacker, raw * defender.ParryReflect, "parry");
            return false;
        }

        double damage = raw * attacker.ModProduct(ModKind.DamageDealt) * defender.ModProduct(ModKind.DamageTaken) * GlobalDamageMult;
        if (defender.Health / defender.MaxHealth < 0.3) damage *= 1 + attacker.Aug.Get("execute");
        if (applyReduction) damage *= 1 - DamageReduction(defender);
        if (kind != HitKind.Ability) damage *= OvertimeMultiplier;
        damage = Math.Max(0, damage);

        if (defender.Shield > 0)
        {
            double absorbed = Math.Min(defender.Shield, damage);
            defender.Shield -= absorbed;
            damage -= absorbed;
            ev.Absorbed = Math.Round(absorbed, 2);
        }
        Deal(attacker, defender, damage, ev);
        if (kind == HitKind.Weapon && defender.Aug.Get("thorns") is > 0 and var thorns && damage > 0 && defender.Health > 0)
            Reflect(defender, attacker, damage * thorns, "thorns");
        return true;
    }

    private void Reflect(Fighter from, Fighter to, double amount, string ability)
    {
        var ev = new DamageEvent { Source = from.Index, Target = to.Index, Reflected = true, AbilitySource = ability };
        Deal(from, to, Math.Max(0, amount), ev);
    }

    private void Deal(Fighter attacker, Fighter defender, double damage, DamageEvent ev, bool chargeUlt = true)
    {
        defender.Health -= damage;
        if (defender.Health <= 0 && !defender.SecondWindUsed && defender.Aug.Get("second_wind") > 0)
        {
            defender.SecondWindUsed = true;
            damage += defender.Health - 1;
            defender.Health = 1;
            events.Add(new AbilityEvent { Source = defender.Index, Target = defender.Index, Ability = "second_wind", Tick = tick, T = Math.Round(Time, 2), Hp = Hp() });
        }
        attacker.DamageDealt += damage;
        defender.DamageTaken += damage;
        if (defender.Health <= 0 && killingBlow[defender.Index] is null)
            killingBlow[defender.Index] = ev.AbilitySource ?? ev.EffectSource ?? (ev.SuddenDeath ? "sudden_death" : "weapon");
        ev.Damage = Math.Round(damage, 2);
        Emit(ev);
        if (chargeUlt && damage > 0)
        {
            ChargeUlt(attacker, damage);
            ChargeUlt(defender, damage);
            double steal = attacker.ModSum(ModKind.Lifesteal) + attacker.Aug.Get("lifesteal");
            if (steal > 0 && attacker.Health > 0) Heal(attacker, damage * steal);
        }
    }

    private void ChargeUlt(Fighter f, double damage)
    {
        if (f.Kit is null) return;
        double speed = f.Aug.Get("ult_charge_mult") * (rules.In("void") ? 2 : 1);
        f.UltCharge = Math.Min(1, f.UltCharge + damage * speed / (Combat.UltChargeFraction * f.MaxHealth));
    }

    private void Heal(Fighter f, double amount)
    {
        double healed = Math.Min(amount, f.MaxHealth - f.Health);
        if (healed <= 0) return;
        f.Health += healed;
        Emit(new HealEvent { Source = f.Index, Target = f.Index, Amount = Math.Round(healed, 2) });
    }

    private void FireTriggers(Fighter owner, TriggerKind kind, string itemId)
    {
        foreach (var es in owner.Effects.Where(e => e.Effect.Trigger == kind && e.Effect.TriggerItemId == itemId).ToList())
        {
            es.TriggerCount++;
            if (es.Effect.Condition?.EveryNHits is int n)
            {
                int every = Math.Max(1, (int)Math.Round(EffectStat(owner, es, StatNames.HitCondition, n)));
                if (es.TriggerCount % every != 0) continue;
            }
            if (es.Effect.Chance is double chance && owner.Rng.NextDouble() >= chance) continue;
            Execute(owner, es);
        }
    }

    private void TryTriggerCooldownEffects(Fighter owner)
    {
        if (owner.Stunned) return;
        foreach (var es in owner.Effects.Where(e => e.Effect.Trigger == TriggerKind.Cooldown && e.CurrentCooldown <= tiny))
        {
            es.CurrentCooldown = Math.Max(0, es.CurrentCooldown + EffectCooldown(owner, es));
            if (es.Effect.Chance is double chance && owner.Rng.NextDouble() >= chance) continue;
            Execute(owner, es);
        }
    }

    private void ApplyRoundStartEffects(Fighter f)
    {
        foreach (var es in f.Effects.Where(e => e.Effect.Trigger == TriggerKind.RoundStart))
            Execute(f, es);
    }

    /// <summary>Arena effects (Lava, Sanctuary, Storm) and Regeneration, once per tick.</summary>
    private void ApplyArenaAndRegen()
    {
        foreach (var f in fighters)
        {
            double heal = f.Aug.Get("regen") + (rules.In("sanctuary") ? 0.01 : 0);
            if (heal > 0 && f.Health > 0) f.Health = Math.Min(f.MaxHealth, f.Health + heal * f.MaxHealth * Combat.SPT);
            if (rules.In("lava") && f.Health > 0)
            {
                double burn = 0.01 * f.MaxHealth * Combat.SPT;
                f.Health -= burn;
                f.DamageTaken += burn;
            }
        }
        if (rules.In("storm") && Time >= nextLightning - tiny)
        {
            nextLightning += 5;
            var target = fighters[fighters[0].Rng.Next(2)];
            var ev = new DamageEvent { Source = target.Index, Target = target.Index, AbilitySource = "lightning" };
            if (target.Invulnerable)
            {
                ev.Blocked = true;
                Emit(ev);
            }
            else
            {
                double damage = 0.08 * target.MaxHealth;
                target.Health -= damage;
                target.DamageTaken += damage;
                ev.Damage = Math.Round(damage, 2);
                Emit(ev);
            }
        }
    }

    private void ApplySuddenDeath()
    {
        double damage = Math.Pow(2, Math.Floor(Time - suddenDeathStart + tiny));
        foreach (var f in fighters)
        {
            var other = Opponent(f);
            Deal(other, f, damage, new DamageEvent { Source = other.Index, Target = f.Index, SuddenDeath = true }, chargeUlt: false);
        }
    }

    /// <summary>Stuns <paramref name="target"/> unless invulnerable or stun immune. Stuns add up.</summary>
    private void Stun(Fighter source, Fighter target, double duration, StunEvent ev)
    {
        duration *= source.Aug.Get("stun_duration_mult");
        if (target.Invulnerable || target.HasMod(ModKind.StunImmune))
        {
            ev.Blocked = true;
            Emit(ev);
            return;
        }
        target.StunLeft += duration;
        ev.Duration = duration;
        Emit(ev);
    }

    private void Execute(Fighter owner, EffectState es)
    {
        var e = es.Effect;
        var opponent = Opponent(owner);
        switch (e.Type)
        {
            case EffectType.Buff:
            {
                var target = ResolveTargetName(owner, es);
                if (target is null || e.Stat is null) return;
                es.Stacks = Math.Min(es.Stacks + 1, e.StackLimit);
                if (e.Duration is double d) es.ExpiresAt = Time + d;
                Emit(MakeBuffEvent(owner, es, es.Stacks, target));
                break;
            }
            case EffectType.Attack:
            {
                var ev = new DamageEvent { Source = owner.Index, Target = opponent.Index, ItemSource = es.Source?.Id, EffectSource = e.Id };
                double damage = Math.Max(0, EffectStat(owner, es, StatNames.Damage, (e.Damage ?? 0) * es.Scale));
                Hit(owner, opponent, damage, HitKind.Effect, ev);
                break;
            }
            case EffectType.Stun:
            {
                var ev = new StunEvent { Source = owner.Index, Target = opponent.Index, ItemSource = es.Source?.Id, EffectSource = e.Id };
                double duration = e.Duration ?? 0;
                if (es.Source is not null && ReferenceEquals(es.Source, owner.Weapon)
                    && owner.Rng.NextDouble() < WeaponStat(owner, StatNames.CritChance))
                {
                    duration *= 2;
                    ev.Crit = true;
                }
                Stun(owner, opponent, duration, ev);
                break;
            }
            case EffectType.Invulnerability:
            {
                double duration = e.Duration ?? 0;
                owner.InvulnerableLeft = Math.Max(owner.InvulnerableLeft, duration);
                Emit(new InvulnerabilityEvent { Source = owner.Index, Target = owner.Index, ItemSource = es.Source?.Id, EffectSource = e.Id, Duration = duration });
                break;
            }
            case EffectType.Gamble:
            {
                bool won = owner.Rng.NextDouble() < 0.5;
                es.Payout *= won ? e.WinMult ?? 2 : e.LoseMult ?? 0.5;
                Emit(new MoneyEvent { Source = owner.Index, Target = owner.Index, ItemSource = es.Source?.Id, EffectSource = e.Id, Payout = Math.Round(es.Payout, 2), Won = won });
                break;
            }
            case EffectType.Money:
            {
                double gain = (e.Gain ?? 0) * es.Scale;
                owner.Earnings += gain;
                Emit(new MoneyEvent { Source = owner.Index, Target = owner.Index, ItemSource = es.Source?.Id, EffectSource = e.Id, Gain = Math.Round(gain, 2) });
                break;
            }
            case EffectType.Interest:
            {
                double gain = Math.Floor(owner.Loadout.Money * (e.Gain ?? 0) * es.Scale);
                owner.Earnings += gain;
                Emit(new MoneyEvent { Source = owner.Index, Target = owner.Index, ItemSource = es.Source?.Id, EffectSource = e.Id, Gain = gain });
                break;
            }
        }
    }

    // ================= abilities =================

    private static Ability? AbilityOf(Fighter f, string id) => f.Kit?.Abilities.FirstOrDefault(a => a.Id == id);

    /// <summary>Whether slot (0-2 skills, 3 ultimate) can be used right now.</summary>
    private bool CanUse(Fighter f, int slot)
    {
        if (f.Kit is null || Finished || f.Health <= 0) return false;
        var a = f.Kit.Abilities[slot];
        if (f.Stunned && !a.UsableWhileStunned) return false;
        if (a.NeedsWeapon && f.Weapon is null) return false;
        if (a.Id == "headshot" && f.HeadshotAt is not null) return false;
        return slot == 3 ? f.UltCharge >= 1 - tiny : f.SkillCooldowns[slot] <= tiny;
    }

    private void AddMod(Fighter f, string source, ModKind kind, double value, double duration, bool debuff = false) =>
        f.Mods.Add(new TempMod { Source = source, Kind = kind, Value = value, ExpiresAt = Time + duration, Debuff = debuff });

    private void UseAbility(Fighter f, int slot)
    {
        if (!CanUse(f, slot)) return;
        var a = f.Kit!.Abilities[slot];
        var enemy = Opponent(f);
        double duration = a.Duration ?? 0;
        var ev = new AbilityEvent { Source = f.Index, Target = f.Index, Ability = a.Id, Slot = slot };

        if (slot == 3) f.UltCharge = 0;
        else f.SkillCooldowns[slot] = (a.Cooldown ?? 0) * CooldownMult * f.Aug.Get("skill_cooldown_mult");

        switch (a.Id)
        {
            // Duelist
            case "parry":
                f.ParryUntil = Time + duration;
                f.ParryReflect = a.Reflect ?? 0.5;
                break;
            case "lunge":
                Emit(ev);
                WeaponAttack(f, "lunge");
                return;
            case "feint":
                enemy.MissNextAttacks++;
                ev.Target = enemy.Index;
                break;
            case "flurry":
            {
                int hits = Math.Max(1, a.Hits ?? 5);
                double gap = hits > 1 ? duration / (hits - 1) : 0;
                for (int i = 0; i < hits; i++) f.FlurryAttacks.Add(Time + i * gap);
                break;
            }
            // Berserker
            case "rage":
                AddMod(f, a.Id, ModKind.DamageDealt, a.Mult ?? 1.5, duration);
                AddMod(f, a.Id, ModKind.DamageTaken, a.Taken ?? 1.25, duration);
                break;
            case "bloodlust":
                AddMod(f, a.Id, ModKind.Lifesteal, a.Fraction ?? 0.3, duration);
                break;
            case "shout":
                Emit(ev);
                Stun(f, enemy, duration, new StunEvent { Source = f.Index, Target = enemy.Index, EffectSource = a.Id });
                return;
            case "rampage":
                AddMod(f, a.Id, ModKind.WeaponCooldown, a.Mult ?? 0.5, duration);
                AddMod(f, a.Id, ModKind.StunImmune, 1, duration);
                f.StunLeft = 0;
                f.WeaponTimer *= a.Mult ?? 0.5;
                break;
            // Guardian
            case "shield":
                f.Shield = f.MaxHealth * (a.Fraction ?? 0.15);
                f.ShieldUntil = Time + duration;
                break;
            case "brace":
                AddMod(f, a.Id, ModKind.DamageReduction, a.Add ?? 0.3, duration);
                break;
            case "cleanse":
                f.StunLeft = 0;
                f.Mods.RemoveAll(m => m.Debuff);
                f.MissNextAttacks = 0;
                break;
            case "bulwark":
                f.InvulnerableLeft = Math.Max(f.InvulnerableLeft, duration);
                f.BulwarkReflect = a.Reflect ?? 1;
                break;
            // Trickster
            case "smoke":
            case "retreat":
                AddMod(f, a.Id, ModKind.Dodge, a.Add ?? 0.4, duration);
                break;
            case "hex":
                AddMod(enemy, a.Id, ModKind.WeaponCooldown, a.Mult ?? 1.5, duration, debuff: true);
                if (double.IsFinite(enemy.WeaponTimer)) enemy.WeaponTimer *= a.Mult ?? 1.5;
                ev.Target = enemy.Index;
                break;
            case "blink":
                f.NextHitCrit = true;
                break;
            case "mirror":
                MirrorBuffs(f, enemy, duration);
                break;
            // Gambler
            case "coin_flip":
                f.NextHitCoinFlip = true;
                break;
            case "double_down":
                AddMod(f, a.Id, ModKind.CritChance, a.Add ?? 0.3, duration);
                break;
            case "jackpot":
                if (f.Rng.NextDouble() < (a.Chance ?? 0.1))
                {
                    ev.Outcome = "stun";
                    Emit(ev);
                    Stun(f, enemy, duration, new StunEvent { Source = f.Index, Target = enemy.Index, EffectSource = a.Id });
                    return;
                }
                ev.Outcome = "money";
                f.Earnings += a.Money ?? 2;
                Emit(ev);
                Emit(new MoneyEvent { Source = f.Index, Target = f.Index, EffectSource = a.Id, Gain = a.Money ?? 2 });
                return;
            case "all_in":
                f.AllInHits = Math.Max(1, a.Hits ?? 3);
                break;
            // Sniper
            case "aim":
                f.NextHitMult = a.Mult ?? 2;
                break;
            case "mark":
                AddMod(enemy, a.Id, ModKind.DamageTaken, a.Mult ?? 1.2, duration, debuff: true);
                ev.Target = enemy.Index;
                break;
            case "headshot":
                f.HeadshotAt = Time + (a.Windup ?? 2);
                break;
        }
        Emit(ev);
    }

    /// <summary>Mirror: copies the enemy's ability buffs and their weapon buff stacks (as multipliers) for a while.</summary>
    private void MirrorBuffs(Fighter f, Fighter enemy, double duration)
    {
        foreach (var m in enemy.Mods.Where(m => !m.Debuff).ToList())
            AddMod(f, "mirror", m.Kind, m.Value, duration);
        foreach (var es in enemy.Effects.Where(e => e.Effect.Type == EffectType.Buff
                     && (e.Effect.Target == "type.weapon" || e.Effect.Target == enemy.Weapon?.Id)))
        {
            int n = ActiveStacks(enemy, es);
            if (n == 0) continue;
            double mult = es.ScaledMult is double m ? Math.Max(0, 1 + n * (m - 1)) : 1;
            switch (es.Effect.Stat)
            {
                case StatNames.Damage when mult != 1: AddMod(f, "mirror", ModKind.DamageDealt, mult, duration); break;
                case StatNames.Cooldown when mult != 1: AddMod(f, "mirror", ModKind.WeaponCooldown, mult, duration); break;
                case StatNames.CritChance when es.ScaledAdd is double a: AddMod(f, "mirror", ModKind.CritChance, a * n, duration); break;
            }
        }
    }

    /// <summary>Simple bot for ghosts, disconnected players and simulations: defensive skills right before the enemy
    /// swings, Cleanse when stunned or debuffed, everything else as soon as it is ready.</summary>
    private void BotThink(Fighter f)
    {
        if (f.Kit is null) return;
        int skill = f.Loadout.BotSkill;
        if (skill <= 0)
        {
            // Easy: presses something random now and then.
            if (f.Rng.NextDouble() < 0.03)
            {
                int slot = f.Rng.Next(4);
                if (CanUse(f, slot)) f.Inputs.Enqueue(slot);
            }
            return;
        }
        if (skill == 1)
        {
            // Normal: uses everything as soon as it's ready, no timing.
            for (int slot = 0; slot < 4; slot++)
                if (CanUse(f, slot) && (f.Kit.Abilities[slot].Id != "cleanse" || f.Stunned)) f.Inputs.Enqueue(slot);
            return;
        }
        var enemy = Opponent(f);
        bool enemySwingSoon = enemy.Weapon is not null && !enemy.Stunned && enemy.WeaponTimer <= 0.25;
        bool threat = enemySwingSoon || (enemy.HeadshotAt is double h && h - Time <= 0.3) || enemy.FlurryAttacks.Count > 0;
        for (int slot = 0; slot < 4; slot++)
        {
            if (!CanUse(f, slot)) continue;
            var id = f.Kit.Abilities[slot].Id;
            bool use = id switch
            {
                "parry" or "feint" or "shield" or "brace" or "smoke" or "retreat" => threat,
                "cleanse" => f.Stunned || f.Mods.Any(m => m.Debuff),
                "bulwark" => threat || f.Health / f.MaxHealth < 0.5,
                "mirror" => enemy.Mods.Any(m => !m.Debuff) || enemy.Effects.Any(e => e.Stacks > 0),
                _ => true
            };
            if (use) f.Inputs.Enqueue(slot);
        }
    }

    // ================= buff reporting =================

    private void UpdateConditionalBuffs(Fighter f, bool report)
    {
        foreach (var es in f.Effects.Where(e => e.Effect.Type == EffectType.Buff
                     && e.Effect.Trigger == TriggerKind.Static && e.Effect.Condition?.HPBelow is not null))
        {
            bool active = ActiveStacks(f, es) > 0;
            if (es.WasActive == active) continue;
            es.WasActive = active;
            var target = ResolveTargetName(f, es);
            if (report && target is not null) Emit(MakeBuffEvent(f, es, active ? 1 : 0, target));
        }
    }

    private static string? ResolveTargetName(Fighter f, EffectState es)
    {
        var target = es.Effect.Target;
        if (target is null || es.Effect.Stat is null) return null;
        if (target == "type.weapon") return f.Weapon?.Id;
        if (f.Loadout.AllItems().Any(i => i.Id == target)) return target;
        if (f.Effects.Any(other => other.Effect.Id == target)) return target;
        return null;
    }

    private BuffEvent MakeBuffEvent(Fighter f, EffectState es, int stacks, string target)
    {
        var e = es.Effect;
        return new BuffEvent
        {
            Source = f.Index,
            Target = f.Index,
            ItemSource = es.Source?.Id,
            EffectSource = e.Id,
            Stat = e.Stat,
            BuffTarget = target,
            Stacks = stacks,
            Mult = es.ScaledMult is double m ? Math.Round(Math.Max(0, 1 + stacks * (m - 1)), 4) : null,
            Add = es.ScaledAdd is double a ? Math.Round(a * stacks, 4) : null,
            Duration = stacks > 0 ? e.Duration : null,
            Tick = tick,
            T = Math.Round(Time, 2),
            Hp = Hp()
        };
    }

    private double[] Hp() => fighters is null
        ? new double[2]
        : fighters.Select(x => Math.Max(0, Math.Round(x.Health, 1))).ToArray();

    private void Emit(CombatEvent ev)
    {
        ev.Tick = tick;
        ev.T = Math.Round(Time, 2);
        ev.Hp = Hp();
        events.Add(ev);
    }
}
