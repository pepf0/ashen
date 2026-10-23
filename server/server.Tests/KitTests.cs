namespace Ashen.Tests;

public class KitTests
{
    public KitTests() => TestHelpers.Init();

    private static Loadout WithKit(string kit, string? weapon = "generic.sword", double hp = 1000)
    {
        var l = TestHelpers.Build(weapon);
        l.Kit = kit;
        l.MaxHealth = hp;
        return l;
    }

    private static Fight Start(Loadout a, Loadout b, string? modifier = null) =>
        new(a, b, new FightRules { Modifier = modifier }, 1);

    [Fact]
    public void SixKitsWithThreeSkillsAndAnUltimate()
    {
        Assert.Equal(6, GameData.Kits.Count);
        foreach (var kit in GameData.Kits.Values)
        {
            Assert.Equal(new[] { "q", "w", "e", "r" }, kit.Abilities.Select(a => a.Key));
            Assert.True(kit.Abilities[3].Ultimate);
            Assert.All(kit.Abilities.Take(3), a => Assert.NotNull(a.Cooldown));
        }
    }

    [Fact]
    public void SkillGoesOnCooldownAndUltNeedsCharge()
    {
        var fight = Start(WithKit("berserker"), TestHelpers.Build());
        fight.Queue(0, 0); // Rage
        fight.Queue(0, 3); // Rampage: not charged yet
        fight.Step();
        var used = fight.TakeNewEvents().OfType<AbilityEvent>().Select(e => e.Ability).ToList();
        Assert.Equal(new[] { "rage" }, used);
        fight.Queue(0, 0);
        fight.Step();
        Assert.Empty(fight.TakeNewEvents().OfType<AbilityEvent>());
    }

    [Fact]
    public void UltChargesFromDamage()
    {
        // 40% of 100 HP = 40 damage dealt + taken charges the ultimate.
        var a = WithKit("sniper", "generic.hammer", hp: 100);
        var fight = Start(a, TestHelpers.Build("generic.hammer"));
        bool ready = false;
        for (int i = 0; i < 20 * 30 && !fight.Finished && !ready; i++)
        {
            fight.Step();
            fight.Queue(0, 3);
            ready = fight.TakeNewEvents().OfType<AbilityEvent>().Any(e => e.Ability == "headshot");
        }
        Assert.True(ready);
    }

    [Fact]
    public void ParryBlocksAndReflects()
    {
        var fight = Start(WithKit("duelist", weapon: null), TestHelpers.Build("generic.hammer"));
        // Hammer swings at 2.0s; parry right before it.
        while (fight.Time < 1.7) fight.Step();
        fight.Queue(0, 0);
        while (fight.Time < 2.2) fight.Step();
        var events = fight.TakeNewEvents();
        Assert.Contains(events, e => e is DamageEvent { Parried: true, Source: 1 });
        Assert.Contains(events, e => e is DamageEvent { Reflected: true, Source: 0, Damage: 4.5 });
    }

    [Fact]
    public void ShieldAbsorbsDamage()
    {
        var fight = Start(WithKit("guardian", weapon: null, hp: 100), TestHelpers.Build("generic.hammer"));
        fight.Queue(0, 0);
        while (fight.Time < 2.2) fight.Step();
        var hit = fight.TakeNewEvents().OfType<DamageEvent>().First(e => e.Source == 1 && e.WeaponAttack);
        Assert.Equal(9, hit.Absorbed + hit.Damage, 3);
        Assert.True(hit.Absorbed > 0);
    }

    [Fact]
    public void ShoutStunsAndCleanseRemovesIt()
    {
        var fight = Start(WithKit("berserker"), WithKit("guardian"));
        fight.Queue(0, 2); // Shout
        fight.Step();
        Assert.Contains(fight.TakeNewEvents(), e => e is StunEvent { Blocked: false, Target: 1 });
        fight.Queue(1, 2); // Cleanse, usable while stunned
        fight.Step();
        Assert.Contains(fight.TakeNewEvents(), e => e is AbilityEvent { Ability: "cleanse" });
    }

    [Fact]
    public void FeintMakesNextAttackMiss()
    {
        var fight = Start(WithKit("duelist", weapon: null), TestHelpers.Build("generic.hammer"));
        fight.Queue(0, 2);
        while (fight.Time < 2.2) fight.Step();
        Assert.Contains(fight.TakeNewEvents(), e => e is DamageEvent { Missed: true, Source: 1 });
    }

    [Fact]
    public void NoWeaponsMeansEarlySuddenDeath()
    {
        var r = Combat.CalculateCombatResults(TestHelpers.Build(), TestHelpers.Build(), seed: 3);
        Assert.InRange(r.Duration, Combat.NoWeaponSuddenDeathStart, Combat.NoWeaponSuddenDeathStart + 10);
    }

    [Fact]
    public void ModifiersChangeStats()
    {
        var build = TestHelpers.Build("generic.sword", "generic.band_shirt");
        var normal = Combat.PreviewStats(build);
        Assert.Equal(normal["damage"] * 0.75, Combat.PreviewStats(build, new FightRules { Modifier = "rust" })["damage"], 6);
        Assert.Equal(normal["damage_reduction"] * 0.5, Combat.PreviewStats(build, new FightRules { Modifier = "fragile" })["damage_reduction"], 6);
        Assert.Equal(normal["max_hp"] * 0.7, Combat.PreviewStats(build, new FightRules { Modifier = "glass_cannon" })["max_hp"], 6);
        Assert.Equal(normal["cooldown"] * 0.8, Combat.PreviewStats(build, new FightRules { Modifier = "overclock" })["cooldown"], 6);
    }

    [Fact]
    public void PackSynergyBoostsWeaponDamage()
    {
        var two = Combat.PreviewStats(TestHelpers.Build("pepf.guitar", null, "generic.knight"));
        var withSigil = Combat.PreviewStats(TestHelpers.Build("pepf.guitar", "pepf.shirt"));
        Assert.Equal(5, two["damage"], 6);
        Assert.Equal(5 * 1.05, withSigil["damage"], 6);
    }
}
