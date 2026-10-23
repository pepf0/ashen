namespace Ashen.Tests;

public class ShopTests
{
    public ShopTests() => TestHelpers.Init();

    [Fact]
    public void SpecialChanceBuffsMultiply()
    {
        var p = new Player();
        Assert.Equal(0.01, p.SpecialChance, 9);
        p.Shirt = TestHelpers.Own("ely7.shirt");
        p.Dynamics[0] = TestHelpers.Own("ely7.clover");
        Assert.Equal(0.0375, p.SpecialChance, 9);
    }

    [Fact]
    public void ShopHasSixItemsAndNoDuplicateSpecials()
    {
        var p = new Player();
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var shop = Shop.Roll(p, new HashSet<string>(), rng);
            Assert.Equal(Shop.Slots, shop.Count);
            var specials = shop.Select(s => GameData.Items[s.ItemId]).Where(x => x.IsSpecial).Select(x => x.Id).ToList();
            Assert.Equal(specials.Count, specials.Distinct().Count());
        }
    }

    [Fact]
    public void UnavailableSpecialsNeverAppear()
    {
        var p = new Player();
        for (int i = 0; i < 4; i++) p.Dynamics[i] = TestHelpers.Own("ely7.clover");
        var blocked = GameData.Specials.Where(s => s.Id != "pepf.guitar").Select(s => s.Id).ToHashSet();
        var rng = new Random(2);
        int guitars = 0;
        for (int i = 0; i < 3000; i++)
        {
            var shop = Shop.Roll(p, blocked, rng);
            Assert.DoesNotContain(shop, s => blocked.Contains(s.ItemId));
            guitars += shop.Count(s => s.ItemId == "pepf.guitar");
        }
        Assert.True(guitars > 0);
    }

    [Fact]
    public void DragonIsAQuarterWeight()
    {
        var rng = new Random(3);
        int dragons = 0, total = 200_000;
        for (int i = 0; i < total; i++)
            if (Shop.WeightedPick(GameData.Specials, rng)!.Id == Shop.DragonId) dragons++;
        double expected = 0.25 / (GameData.Specials.Count - 1 + 0.25);
        Assert.InRange((double)dragons / total, expected * 0.85, expected * 1.15);
    }

    [Fact]
    public void MaxLevelGenericsDisappear()
    {
        var p = new Player { Weapon = TestHelpers.Own("generic.sword", OwnedItem.MaxLevel) };
        var rng = new Random(4);
        for (int i = 0; i < 300; i++)
            Assert.DoesNotContain(Shop.Roll(p, new HashSet<string>(), rng), s => s.ItemId == "generic.sword");
    }
}
