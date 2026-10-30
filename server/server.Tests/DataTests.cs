namespace Ashen.Tests;

public class DataTests
{
    public DataTests() => TestHelpers.Init();

    [Fact]
    public void AllEffectTargetsResolve()
    {
        foreach (var effect in GameData.Effects.Values.Where(e => e.Target is not null))
        {
            var t = effect.Target!;
            bool ok = t is "type.weapon" or "special_chance" || GameData.Items.ContainsKey(t) || GameData.Effects.ContainsKey(t);
            Assert.True(ok, $"{effect.Id} targets unknown '{t}'");
        }
    }

    [Fact]
    public void SignatureSetsBelongOnlyToTheirOwners()
    {
        var empty = new HashSet<string>();
        var fox = GameData.Cosmetics.Single(c => c.Id == "name_z7fox");
        Assert.True(Progression.Unlocked(fox, 1, empty, new CosmeticOwner(false, "Z7FOX")));
        Assert.False(Progression.Unlocked(fox, 99, empty, new CosmeticOwner(false, "ely7")));
        Assert.False(Progression.Unlocked(fox, 99, empty, new CosmeticOwner(true, "pepf")));
        Assert.False(Progression.Unlocked(fox, 99, empty));
        var prime = GameData.Cosmetics.Single(c => c.Id == "name_pepf");
        Assert.True(Progression.Unlocked(prime, 1, empty, new CosmeticOwner(true, "pepf")));
        Assert.False(Progression.Unlocked(prime, 99, empty, new CosmeticOwner(false, "z7fox")));
        foreach (var key in new[] { "z7fox", "ely7", "xxz", "raul" })
            Assert.Equal(7, GameData.Cosmetics.Count(c => c.Id.EndsWith("_" + key) && c.Unlock.Users.Count > 0));
    }

    [Fact]
    public void TriggersParse()
    {
        Assert.Equal(TriggerKind.Hit, GameData.Effects["pepf.guitar.stacking_damage_buff"].Trigger);
        Assert.Equal("pepf.guitar", GameData.Effects["pepf.guitar.stacking_damage_buff"].TriggerItemId);
        Assert.Equal(TriggerKind.Crit, GameData.Effects["ely7.blade.stacking_crit_damage_buff"].Trigger);
        Assert.Equal(TriggerKind.Cooldown, GameData.Effects["pepf.dragon.invulnerability"].Trigger);
        Assert.Equal(TriggerKind.RoundStart, GameData.Effects["generic.crown.interest"].Trigger);
        Assert.Equal(TriggerKind.Static, GameData.Effects["pepf.pick.guitar_damage_buff"].Trigger);
    }

    [Fact]
    public void LevelStatsAreLoaded()
    {
        var sword = TestHelpers.Own("generic.sword", 3);
        Assert.Equal(5 + 2 * 0.5, sword.Stat(StatNames.Damage), 6);
        Assert.Equal(1 - 2 * 0.025, sword.Stat(StatNames.Cooldown), 6);
    }

    [Fact]
    public void EveryPackHasFiveItems()
    {
        foreach (var pack in GameData.Specials.GroupBy(i => i.Pack))
            Assert.Equal(5, pack.Count());
    }
}
