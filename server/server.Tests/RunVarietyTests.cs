namespace Ashen.Tests;

public class RunVarietyTests
{
    public RunVarietyTests() => TestHelpers.Init();

    [Fact]
    public void DataFilesLoad()
    {
        Assert.Equal(32, GameData.Augments.Count);
        Assert.Equal(16, GameData.Mutators.Count);
        Assert.Equal(8, GameData.Arenas.Count);
        Assert.All(Enum.GetValues<AugmentTier>(), t => Assert.True(GameData.Augments.Count(a => a.Tier == t) >= 8));
    }

    [Fact]
    public void AugmentModsCombine()
    {
        var mods = Augment.Combine(new[] { "sharpened", "glass_heart", "thick_skin" });
        Assert.Equal(1.15 * 1.3, mods.Get("weapon_damage_mult"), 6);
        Assert.Equal(0.8, mods.Get("max_hp_mult"), 6);
        Assert.Equal(0.08, mods.Get("damage_reduction_add"), 6);
        Assert.Equal(1, mods.Get("crit_damage_mult"));
        Assert.Equal(0, mods.Get("lifesteal"));
    }

    [Fact]
    public void AugmentOffersAreDistinctSameTierAndNew()
    {
        var rng = new Random(1);
        for (int i = 0; i < 200; i++)
        {
            var owned = new List<string> { "sharpened", "vampire" };
            var offer = Augment.RollOffer(owned, rng);
            Assert.Equal(3, offer.Count);
            Assert.Equal(3, offer.Distinct().Count());
            Assert.DoesNotContain("sharpened", offer);
            Assert.Single(offer.Select(id => GameData.Augments.First(a => a.Id == id).Tier).Distinct());
        }
    }

    [Fact]
    public void AugmentsChangeCombatStats()
    {
        var plain = TestHelpers.Build("generic.sword");
        var buffed = TestHelpers.Build("generic.sword");
        buffed.AugmentMods = Augment.Combine(new[] { "sharpened", "quick_hands", "vitality", "lucky_strikes" });
        var a = Combat.PreviewStats(plain);
        var b = Combat.PreviewStats(buffed);
        Assert.Equal(a["damage"] * 1.15, b["damage"], 6);
        Assert.Equal(a["cooldown"] * 0.9, b["cooldown"], 6);
        Assert.Equal(a["max_hp"] * 1.15, b["max_hp"], 6);
        Assert.Equal(a["crit_chance"] + 0.1, b["crit_chance"], 6);
    }

    [Fact]
    public void SecondWindSavesOnce()
    {
        var weak = TestHelpers.Build();
        weak.MaxHealth = 10;
        weak.AugmentMods = Augment.Combine(new[] { "second_wind" });
        var r = Combat.CalculateCombatResults(weak, TestHelpers.Build("generic.hammer"), seed: 1);
        Assert.Contains(r.Events, e => e is AbilityEvent { Ability: "second_wind", Source: 0 });
        Assert.Single(r.Events.OfType<AbilityEvent>(), e => e.Ability == "second_wind");
        Assert.Equal(1, r.Winner);
    }

    [Fact]
    public void StartShieldAbsorbsFirstHits()
    {
        var tank = TestHelpers.Build();
        tank.AugmentMods = Augment.Combine(new[] { "iron_wall" });
        var r = Combat.CalculateCombatResults(tank, TestHelpers.Build("generic.hammer"), seed: 2);
        var first = r.Events.OfType<DamageEvent>().First(e => e.WeaponAttack && e.Source == 1);
        Assert.Equal(10, first.Absorbed, 3);
    }

    [Fact]
    public void ArenasApply()
    {
        var build = TestHelpers.Build("generic.sword");
        Assert.Equal(Combat.PreviewStats(build)["cooldown"] * 1.15,
            Combat.PreviewStats(build, new FightRules { Arena = "ice" })["cooldown"], 6);
        // Lava burns both sides even without weapons.
        var lava = Combat.CalculateCombatResults(TestHelpers.Build(), TestHelpers.Build(), seed: 3, rules: new FightRules { Arena = "lava" });
        Assert.True(lava.DamageTaken[0] > 0);
        var storm = Combat.CalculateCombatResults(TestHelpers.Build("generic.bow"), TestHelpers.Build("generic.bow"), seed: 4, rules: new FightRules { Arena = "storm" });
        Assert.Contains(storm.Events, e => e is DamageEvent { AbilitySource: "lightning" });
        Assert.Equal("storm", storm.Arena);
    }

    [Fact]
    public void ShopOptionsWork()
    {
        var p = new Player();
        var rng = new Random(5);
        var opts = new ShopRollOptions { Slots = 8, NoShirts = true, Discount = 0.1 };
        for (int i = 0; i < 200; i++)
        {
            var shop = Shop.Roll(p, new HashSet<string>(), rng, opts);
            Assert.Equal(8, shop.Count);
            Assert.DoesNotContain(shop, s => GameData.Items[s.ItemId].Type == ItemType.Shirt);
            Assert.All(shop, s => Assert.Equal((int)Math.Round(GameData.Items[s.ItemId].Price * 0.9), s.Price));
        }
        var smith = new Player { Weapon = TestHelpers.Own("generic.sword", 5) };
        Assert.Contains(Enumerable.Range(0, 200).SelectMany(_ => Shop.Roll(smith, new HashSet<string>(), rng, new ShopRollOptions { MaxLevel = 7 })),
            s => s.ItemId == "generic.sword");
    }
}
