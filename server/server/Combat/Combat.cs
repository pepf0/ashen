namespace Ashen;

/// <summary>
/// Tick based fight simulation. Fights run on frozen <see cref="Loadout"/>s, so nothing a fight
/// does (stacks, cooldowns, HP) leaks back into the players' builds.
/// </summary>
public static class Combat
{
    public static int TPS { get; set; } = 20;
    public static double SPT => 1.0 / TPS;
    private const double tiny = 0.0001;

    public const double MaxDamageReduction = 0.8;
    public const double MaxDodgeChance = 0.6;
    public const double MinCooldown = 0.1;
    public const double OvertimeStart = 30;
    public const double OvertimeRampPerSecond = 0.1;
    public const double SuddenDeathStart = 60;
    /// <summary>Safety net: a fight never runs longer than this (sudden death ends it long before).</summary>
    public const double HardLimit = 180;

    public static CombatResult CalculateCombatResults(Loadout l1, Loadout l2, int? seed = null)
    {
        var rng = seed is int s ? new Random(s) : new Random();
        var fight = new Fight(new Fighter(0, l1, new Random(rng.Next())), new Fighter(1, l2, new Random(rng.Next())));
        return fight.Run();
    }

    /// <summary>Stats shown in the shop's stat panel: static buffs applied, at full HP.</summary>
    public static Dictionary<string, double> PreviewStats(Loadout loadout)
    {
        var fight = new Fight(new Fighter(0, loadout, new Random(0)), new Fighter(1, new Loadout(), new Random(0)));
        var f = fight.Fighters[0];
        var stats = new Dictionary<string, double>
        {
            ["max_hp"] = f.MaxHealth,
            ["damage_reduction"] = fight.DamageReduction(f),
            ["dodge_chance"] = fight.DodgeChance(f)
        };
        if (f.Weapon is not null)
        {
            stats["damage"] = fight.WeaponStat(f, StatNames.Damage);
            stats["cooldown"] = fight.WeaponStat(f, StatNames.Cooldown);
            stats["crit_chance"] = fight.WeaponStat(f, StatNames.CritChance);
            stats["crit_damage"] = fight.WeaponStat(f, StatNames.CritDamage);
        }
        return stats;
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
        public List<EffectState> Effects { get; } = new();
        public double Earnings { get; set; }
        public double DamageDealt { get; set; }
        public double DamageTaken { get; set; }

        public bool Stunned => StunLeft > tiny;
        public bool Invulnerable => InvulnerableLeft > tiny;

        public Fighter(int index, Loadout loadout, Random rng)
        {
            Index = index;
            Loadout = loadout;
            Rng = rng;
            MaxHealth = loadout.MaxHealth;
            Health = MaxHealth;
            foreach (var item in loadout.AllItems())
                foreach (var effect in item.Item.Effects)
                    Effects.Add(new EffectState(effect, item, item.EffectScale));
            foreach (var id in loadout.BonusEffects)
                if (GameData.Effects.TryGetValue(id, out var effect))
                    Effects.Add(new EffectState(effect, null, 1));
        }
    }

    private sealed class EffectState
    {
        public Effect Effect { get; }
        public OwnedItem? Source { get; }
        /// <summary>Level scaling of the effect's strength.</summary>
        public double Scale { get; }
        public double CurrentCooldown { get; set; }
        public int Stacks { get; set; }
        public double ExpiresAt { get; set; } = double.PositiveInfinity;
        public int TriggerCount { get; set; }
        public double Payout { get; set; }
        /// <summary>Last known on/off state of a conditional static buff, to report changes.</summary>
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

    private sealed class Fight
    {
        public Fighter[] Fighters { get; }
        private readonly List<CombatEvent> events = new();
        private int tick;
        private double Time => tick * SPT;

        public Fight(Fighter a, Fighter b)
        {
            Fighters = new[] { a, b };
            foreach (var f in Fighters)
            {
                f.WeaponTimer = f.Weapon is null ? double.PositiveInfinity : WeaponStat(f, StatNames.Cooldown);
                foreach (var es in f.Effects.Where(e => e.Effect.Trigger == TriggerKind.Cooldown))
                    es.CurrentCooldown = EffectCooldown(f, es);
            }
        }

        private Fighter Opponent(Fighter f) => Fighters[1 - f.Index];

        public CombatResult Run()
        {
            var result = new CombatResult();
            for (int i = 0; i < 2; i++) result.Fighters[i] = Describe(Fighters[i]);

            foreach (var f in Fighters) ApplyRoundStartEffects(f);
            foreach (var f in Fighters) UpdateConditionalBuffs(f, report: false);

            int winner;
            while (true)
            {
                tick++;
                var hpBefore = Fighters.Select(f => f.Health).ToArray();

                foreach (var f in Fighters) TickTimers(f);
                // Alternate who acts first so neither side has a systematic edge.
                var order = tick % 2 == 0 ? Fighters : Fighters.Reverse().ToArray();
                foreach (var f in order) TryWeaponAttack(f);
                foreach (var f in order) TryTriggerCooldownEffects(f);
                if (Time >= SuddenDeathStart - tiny) ApplySuddenDeath();
                foreach (var f in Fighters) UpdateConditionalBuffs(f, report: true);

                bool dead0 = Fighters[0].Health <= 0, dead1 = Fighters[1].Health <= 0;
                if (dead0 || dead1)
                {
                    if (dead0 && dead1)
                        winner = hpBefore[0] > hpBefore[1] ? 0 : hpBefore[1] > hpBefore[0] ? 1 : -1;
                    else
                        winner = dead0 ? 1 : 0;
                    break;
                }
                if (Time >= HardLimit)
                {
                    var p0 = Fighters[0].Health / Fighters[0].MaxHealth;
                    var p1 = Fighters[1].Health / Fighters[1].MaxHealth;
                    winner = p0 > p1 ? 0 : p1 > p0 ? 1 : -1;
                    break;
                }
            }

            result.Events = events;
            result.Winner = winner;
            result.Duration = Math.Round(Time, 2);
            for (int i = 0; i < 2; i++)
            {
                var f = Fighters[i];
                result.EndHp[i] = Math.Max(0, Math.Round(f.Health, 1));
                result.DamageDealt[i] = Math.Round(f.DamageDealt, 1);
                result.DamageTaken[i] = Math.Round(f.DamageTaken, 1);
                var payouts = f.Effects.Where(e => e.Effect.Type == EffectType.Gamble).Sum(e => e.Payout);
                result.Earnings[i] = (int)Math.Round(f.Earnings + payouts);
            }
            return result;
        }

        private FighterInfo Describe(Fighter f)
        {
            var info = new FighterInfo
            {
                Name = f.Loadout.Name,
                PlayerId = f.Loadout.PlayerId,
                Ghost = f.Loadout.Ghost,
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
            return info;
        }

        // ---------- stats ----------

        private int ActiveStacks(Fighter f, EffectState es)
        {
            if (es.Effect.Trigger != TriggerKind.Static) return es.Stacks;
            if (es.Effect.Condition?.HPBelow is double below)
                return f.Health / f.MaxHealth < below ? 1 : 0;
            return 1;
        }

        /// <summary>Applies every buff on <paramref name="f"/> for <paramref name="stat"/> whose target matches.
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

        public double WeaponStat(Fighter f, string stat)
        {
            if (f.Weapon is null) return 0;
            var value = ItemStat(f, f.Weapon, stat);
            return stat switch
            {
                StatNames.Cooldown => Math.Max(MinCooldown, value),
                StatNames.CritChance => Math.Clamp(value, 0, 1),
                StatNames.CritDamage => Math.Max(1, value),
                _ => Math.Max(0, value)
            };
        }

        private double EffectStat(Fighter f, EffectState es, string stat, double baseValue) =>
            Modify(f, baseValue, stat, t => t == es.Effect.Id);

        private double EffectCooldown(Fighter f, EffectState es) =>
            Math.Max(MinCooldown, EffectStat(f, es, StatNames.Cooldown, es.Effect.Cooldown ?? 0));

        public double DamageReduction(Fighter f) =>
            Math.Clamp(f.Loadout.AllItems().Sum(i => ItemStat(f, i, StatNames.DamageReduction)), 0, MaxDamageReduction);

        public double DodgeChance(Fighter f) =>
            Math.Clamp(f.Loadout.AllItems().Sum(i => ItemStat(f, i, StatNames.DodgeChance)), 0, MaxDodgeChance);

        private double OvertimeMultiplier =>
            Time > OvertimeStart ? 1 + OvertimeRampPerSecond * (Time - OvertimeStart) : 1;

        // ---------- tick steps ----------

        private void TickTimers(Fighter f)
        {
            if (f.InvulnerableLeft > 0) f.InvulnerableLeft = Math.Max(0, f.InvulnerableLeft - SPT);

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
                f.StunLeft = Math.Max(0, f.StunLeft - SPT);
                return;
            }
            f.WeaponTimer -= SPT;
            foreach (var es in f.Effects.Where(e => e.Effect.Trigger == TriggerKind.Cooldown))
                es.CurrentCooldown -= SPT;
        }

        private void TryWeaponAttack(Fighter attacker)
        {
            if (attacker.Stunned || attacker.Weapon is null || attacker.WeaponTimer > tiny) return;
            var weapon = attacker.Weapon;
            var defender = Opponent(attacker);
            attacker.WeaponTimer = Math.Max(0, attacker.WeaponTimer + WeaponStat(attacker, StatNames.Cooldown));

            var ev = new DamageEvent { ItemSource = weapon.Id, WeaponAttack = true, Source = attacker.Index, Target = defender.Index };
            if (defender.Invulnerable)
            {
                ev.Blocked = true;
                Emit(ev);
                return;
            }
            if (defender.Rng.NextDouble() < DodgeChance(defender))
            {
                ev.Dodged = true;
                Emit(ev);
                return;
            }

            bool crit = attacker.Rng.NextDouble() < WeaponStat(attacker, StatNames.CritChance);
            double critMult = crit ? WeaponStat(attacker, StatNames.CritDamage) : 1;
            double damage = WeaponStat(attacker, StatNames.Damage) * critMult
                            * (1 - DamageReduction(defender)) * OvertimeMultiplier;
            ev.CritMultiplier = crit ? Math.Round(critMult, 3) : null;
            DealDamage(attacker, defender, damage, ev);

            FireTriggers(attacker, TriggerKind.Hit, weapon.Id);
            if (crit) FireTriggers(attacker, TriggerKind.Crit, weapon.Id);
        }

        private void DealDamage(Fighter attacker, Fighter defender, double damage, DamageEvent ev)
        {
            defender.Health -= damage;
            attacker.DamageDealt += damage;
            defender.DamageTaken += damage;
            ev.Damage = Math.Round(damage, 2);
            Emit(ev);
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

        private void ApplySuddenDeath()
        {
            double damage = Math.Pow(2, Math.Floor(Time - SuddenDeathStart + tiny));
            foreach (var f in Fighters)
            {
                var other = Opponent(f);
                DealDamage(other, f, damage, new DamageEvent { Source = other.Index, Target = f.Index, SuddenDeath = true });
            }
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
                    if (opponent.Invulnerable)
                    {
                        ev.Blocked = true;
                        Emit(ev);
                        return;
                    }
                    double damage = Math.Max(0, EffectStat(owner, es, StatNames.Damage, (e.Damage ?? 0) * es.Scale))
                                    * (1 - DamageReduction(opponent)) * OvertimeMultiplier;
                    DealDamage(owner, opponent, damage, ev);
                    break;
                }
                case EffectType.Stun:
                {
                    var ev = new StunEvent { Source = owner.Index, Target = opponent.Index, ItemSource = es.Source?.Id, EffectSource = e.Id };
                    if (opponent.Invulnerable)
                    {
                        ev.Blocked = true;
                        Emit(ev);
                        return;
                    }
                    double duration = e.Duration ?? 0;
                    if (es.Source is not null && ReferenceEquals(es.Source, owner.Weapon)
                        && owner.Rng.NextDouble() < WeaponStat(owner, StatNames.CritChance))
                    {
                        duration *= 2;
                        ev.Crit = true;
                    }
                    opponent.StunLeft += duration;
                    ev.Duration = duration;
                    Emit(ev);
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

        /// <summary>Reports conditional static buffs (e.g. Cyan Sigil below 40% HP) turning on or off.</summary>
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

        /// <summary>Resolves an effect's target to what it actually buffs for this fighter, or null if it buffs
        /// nothing in a fight (item not owned, or a shop-only target like special_chance).</summary>
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
                Hp = Fighters.Select(x => Math.Max(0, Math.Round(x.Health, 1))).ToArray()
            };
        }

        private void Emit(CombatEvent ev)
        {
            ev.Tick = tick;
            ev.T = Math.Round(Time, 2);
            ev.Hp = Fighters.Select(x => Math.Max(0, Math.Round(x.Health, 1))).ToArray();
            events.Add(ev);
        }
    }
}
