namespace Ashen.Tests;

public static class TestHelpers
{
    static TestHelpers() => GameData.EnsureLoaded();

    public static void Init() { }

    public static OwnedItem Own(string id, int level = 1)
    {
        Init();
        return new OwnedItem(GameData.Items[id], level);
    }

    public static Loadout Build(string? weapon = null, string? shirt = null, params string[] dynamics)
    {
        Init();
        var l = new Loadout { Name = "test", MaxHealth = 1000 };
        if (weapon is not null) l.Weapon = Own(weapon);
        if (shirt is not null) l.Shirt = Own(shirt);
        for (int i = 0; i < dynamics.Length; i++) l.Dynamics[i] = Own(dynamics[i]);
        return l;
    }
}
