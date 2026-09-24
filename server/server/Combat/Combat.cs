using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Ashen;

public static class Combat
{
    public static int TPS { get; set; } = 20;
    public static double SPT => 1.0 / TPS;
    private const double tiny = 0.0001;

    public static List<CombatEvent> CalculateCombatResults(Player p1, Player p2)
    {
        int tick = 0;
        List<CombatEvent> events = new();
        ApplyRoundStartEffects(p1);
        ApplyRoundStartEffects(p2);

        var p1Random = new Random();
        var p2Random = new Random();

        while (p1.Health > 0 && p2.Health > 0)
        {
            TickCooldowns(p1);
            TickCooldowns(p2);



            TryWeaponAttack(p1, p2, p1Random, tick, events);
            TryWeaponAttack(p2, p1, p2Random, tick, events);

            TryTriggerCooldownEffects(p1, p2, p1Random, tick, events);
            TryTriggerCooldownEffects(p2, p1, p2Random, tick, events);

            tick++;
        }

        return events;
    }

    private static IEnumerable<OwnedItem> AllItems(Player player)
    {
        if (player.Weapon is not null) yield return player.Weapon;
        if (player.Shirt is not null) yield return player.Shirt;
        foreach (var item in player.Dynamics)
            if (item is not null) yield return item;
    }

    private static void TickCooldowns(Player player)
    {
        if (player.Stunned)
        {
            player.StunDurationLeft -= SPT;
            if (player.StunDurationLeft <= tiny)
            {
                player.Stunned = false;
                player.StunDurationLeft = 0;
            } 
            else return;   
        }
        if (player.Weapon is not null)
            player.Weapon.CurrentCooldown -= SPT;

        foreach (var item in AllItems(player))
            foreach (var effect in item.ActiveEffects.Where(e => e.RealCooldown > 0))
                effect.CurrentCooldown -= SPT;
    }

    private static void TryWeaponAttack(Player attacker, Player defender, Random rng, int tick, List<CombatEvent> events)
    {
        if (attacker.Stunned) return;
        var weapon = attacker.Weapon;
        if (weapon is null || weapon.CurrentCooldown > tiny) return;

        bool crit = rng.NextDouble() < weapon.RealCritChance;
        events.Add(new DamageEvent
        {
            Tick = tick,
            Source = attacker,
            Target = defender,
            ItemSource = weapon,
            Damage = weapon.RealDamage,
            CritMultiplier = crit ? weapon.RealCritDamage : null
        });

        weapon.CurrentCooldown += weapon.RealCooldown;
        defender.Health -= weapon.RealDamage * (crit ? weapon.RealCritDamage : 1);
    }

    private static void TryTriggerCooldownEffects(Player owner, Player opponent, Random rng, int tick, List<CombatEvent> events)
    {
        if (owner.Stunned) return;
        foreach (var item in AllItems(owner))
        {
            foreach (var effect in item.ActiveEffects.Where(e => e.RealCooldown > 0 && e.CurrentCooldown <= tiny))
            {
                if (effect.Effect.Chance is double chance && rng.NextDouble() >= chance)
                {
                    effect.CurrentCooldown += effect.RealCooldown;
                    continue;
                }

                DispatchEffect(owner, opponent, item, effect, tick, events);
                effect.CurrentCooldown += effect.RealCooldown;
            }
        }
    }

    private static void DispatchEffect(Player owner, Player opponent, OwnedItem item, OwnedItemEffect effect, int tick, List<CombatEvent> events)
    {
        if (owner.Stunned) return;
        switch (effect.Effect.Type)
        {
            case EffectType.Stun:
                events.Add(new StunEvent
                {
                    Tick = tick,
                    Source = owner,
                    Target = opponent,
                    ItemSource = item,
                    Duration = effect.Effect.Duration ?? 0
                });
                opponent.Stunned = true;
                opponent.StunDurationLeft += effect.Effect.Duration ?? 0;
                break;
        }
    }

    private static void ApplyRoundStartEffects(Player player)
    {
        foreach (var item in AllItems(player))
        {
            foreach (var effect in item.Effects?.Where(e => e.Trigger == "round-start") ?? Enumerable.Empty<Effect>())
            {
                if (effect.Type == EffectType.Interest)
                {
                    player.Money *= 1 + (effect.Gain ?? 0);
                }
            }
        }
    }
}