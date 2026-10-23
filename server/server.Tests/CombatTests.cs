namespace Ashen.Tests;

public class CombatTests
{
    public CombatTests() => TestHelpers.Init();

    [Fact]
    public void EmptyBuildsEndInSuddenDeathDraw()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build(), TestHelpers.Build(), seed: 1);
        Assert.Equal(-1, r.Winner);
        Assert.True(r.Duration > Combat.NoWeaponSuddenDeathStart);
        Assert.True(r.Duration < Combat.NoWeaponSuddenDeathStart + 10);
    }

    [Fact]
    public void WeaponBeatsNoWeapon()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("generic.sword"), TestHelpers.Build(), seed: 2);
        Assert.Equal(0, r.Winner);
        Assert.True(r.DamageDealt[0] > 0);
        Assert.Equal(0, r.DamageDealt[1] - r.Events.OfType<DamageEvent>().Where(e => e.Source == 1).Sum(e => e.Damage), 0);
    }

    [Fact]
    public void DamageReductionReducesHits()
    {
        var naked = Combat.CalculateCombatResults(TestHelpers.Build("generic.hammer"), TestHelpers.Build(), seed: 3);
        var shirt = Combat.CalculateCombatResults(TestHelpers.Build("generic.hammer"), TestHelpers.Build(null, "generic.band_shirt"), seed: 3);
        var hitNaked = naked.Events.OfType<DamageEvent>().First(e => e.WeaponAttack && !e.Dodged && e.CritMultiplier is null);
        var hitShirt = shirt.Events.OfType<DamageEvent>().First(e => e.WeaponAttack && !e.Dodged && e.CritMultiplier is null);
        Assert.Equal(9, hitNaked.Damage, 3);
        Assert.Equal(9 * (1 - 0.12), hitShirt.Damage, 3);
    }

    [Fact]
    public void DamageReductionIsCapped()
    {
        var tank = TestHelpers.Build(null, "z7fox.shirt", "z7fox.chain", "generic.horseshoe", "xxzweiradxx.chain");
        tank.Shirt = TestHelpers.Own("z7fox.shirt");
        var stats = Combat.PreviewStats(tank);
        Assert.Equal(Combat.MaxDamageReduction, stats["damage_reduction"], 6);
    }

    [Fact]
    public void DifferentEffectsMultiply()
    {
        // Pepf Shirt x0.85, Cyan Sigil x0.9, Dragon x0.8 on a 1s guitar
        var stats = Combat.PreviewStats(TestHelpers.Build("pepf.guitar", "pepf.shirt", "pepf.sigil", "pepf.dragon"));
        Assert.Equal(1.0 * 0.85 * 0.9 * 0.8, stats["cooldown"], 6);
    }

    [Fact]
    public void ItemSpecificBuffOnlyAppliesToThatItem()
    {
        var withGuitar = Combat.PreviewStats(TestHelpers.Build("pepf.guitar", null, "pepf.pick"));
        var withSword = Combat.PreviewStats(TestHelpers.Build("generic.sword", null, "pepf.pick"));
        Assert.Equal(4.8 * 1.25 * 1.05, withGuitar["damage"], 6); // Pick x1.25, and 2 Pepf items give +5% synergy
        Assert.Equal(5, withSword["damage"], 6);
    }

    [Fact]
    public void GuitarStacksAddUpAndExpire()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("pepf.guitar"), TestHelpers.Build(), seed: 4);
        var buffs = r.Events.OfType<BuffEvent>().Where(b => b.EffectSource == "pepf.guitar.stacking_damage_buff").ToList();
        Assert.Contains(buffs, b => b.Stacks >= 3);
        var three = buffs.First(b => b.Stacks == 3);
        Assert.Equal(Math.Pow(1.06, 3), three.Mult!.Value, 4); // stacks compound
    }

    [Fact]
    public void DragonInvulnerabilityBlocksHits()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("generic.bow"), TestHelpers.Build(null, null, "pepf.dragon"), seed: 5);
        Assert.Contains(r.Events, e => e is InvulnerabilityEvent { Source: 1 });
        Assert.Contains(r.Events, e => e is DamageEvent { Blocked: true, Source: 0 });
    }

    [Fact]
    public void StaffSmashesEveryTenHits()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("xxzweiradxx.staff"), TestHelpers.Build(), seed: 6);
        Assert.Contains(r.Events, e => e is DamageEvent { EffectSource: "xxzweiradxx.staff.smash" });
    }

    [Fact]
    public void DumbbellLowersSmashHitCondition()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("xxzweiradxx.staff", null, "xxzweiradxx.dumbbell"), TestHelpers.Build(), seed: 7);
        var events = r.Events;
        int hitsBeforeSmash = 0;
        foreach (var e in events)
        {
            if (e is DamageEvent { EffectSource: "xxzweiradxx.staff.smash" } smash)
            {
                Assert.Equal(9, hitsBeforeSmash);
                Assert.Equal(55 * 1.6, smash.Damage, 3);
                return;
            }
            if (e is DamageEvent { WeaponAttack: true, Dodged: false, Blocked: false, Source: 0 }) hitsBeforeSmash++;
        }
        Assert.Fail("no smash happened");
    }

    [Fact]
    public void StunsStopTheWeapon()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build("z7fox.whip"), TestHelpers.Build("generic.bow"), seed: 8);
        var stun = r.Events.OfType<StunEvent>().First(s => !s.Blocked);
        var during = r.Events.OfType<DamageEvent>().Where(d => d.Source == 1 && d.WeaponAttack
            && d.T > stun.T + 0.06 && d.T < stun.T + stun.Duration - 0.06);
        Assert.Empty(during);
    }

    [Fact]
    public void LevelScalesEffectsWithoutLevelStats()
    {
        var crown = TestHelpers.Build(null, null, "generic.crown");
        crown.Dynamics[0] = TestHelpers.Own("generic.crown", 3);
        crown.Money = 100;
        var r = Combat.CalculateCombatResults(crown, TestHelpers.Build(), seed: 9);
        Assert.Equal(20, r.Earnings[0]);
    }

    [Fact]
    public void RandomBuildsAlwaysTerminate()
    {
        var rng = new Random(42);
        var weapons = GameData.Items.Values.Where(i => i.Type == ItemType.Weapon).ToList();
        var shirts = GameData.Items.Values.Where(i => i.Type == ItemType.Shirt).ToList();
        var dynamics = GameData.Items.Values.Where(i => i.IsDynamic).ToList();
        Loadout Random()
        {
            var l = new Loadout { MaxHealth = 1000 * Math.Pow(1.2, rng.Next(0, 8)), Money = rng.Next(0, 300) };
            if (rng.NextDouble() < 0.9) l.Weapon = new OwnedItem(weapons[rng.Next(weapons.Count)], rng.Next(1, 6));
            if (rng.NextDouble() < 0.8) l.Shirt = new OwnedItem(shirts[rng.Next(shirts.Count)], rng.Next(1, 6));
            var picks = dynamics.OrderBy(_ => rng.Next()).Take(rng.Next(0, 5)).ToList();
            for (int i = 0; i < picks.Count; i++) l.Dynamics[i] = new OwnedItem(picks[i], rng.Next(1, 6));
            return l;
        }
        var kits = GameData.Kits.Keys.Append(null).ToList();
        var modifiers = GameData.Modifiers.Select(m => m.Id).Append(null).ToList();
        for (int i = 0; i < 1500; i++)
        {
            var a = Random();
            var b = Random();
            a.Kit = kits[rng.Next(kits.Count)];
            b.Kit = kits[rng.Next(kits.Count)];
            var r = Combat.CalculateCombatResults(a, b, seed: i, rules: new FightRules { Modifier = modifiers[rng.Next(modifiers.Count)] });
            Assert.InRange(r.Duration, 0, Combat.SuddenDeathStart + 25);
            Assert.True(r.Winner is -1 or 0 or 1);
            Assert.All(r.Events, e => Assert.Equal(2, e.Hp.Length));
        }
    }
}
