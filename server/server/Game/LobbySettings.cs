namespace Ashen;

public enum SpecialMode
{
    /// <summary>Each player can obtain each special once.</summary>
    PerPlayer,
    /// <summary>Once anyone obtained a special, nobody else can roll it.</summary>
    PerLobby
}

public class LobbySettings
{
    public bool Public { get; set; } = true;
    public int Lives { get; set; } = 5;
    public int ShopSeconds { get; set; } = 60;
    public int StartMoney { get; set; } = 100;
    public int Income { get; set; } = 20;
    public int WinBonus { get; set; } = 10;
    public SpecialMode SpecialMode { get; set; } = SpecialMode.PerPlayer;
    public double StartHp { get; set; } = 100;
    public double HpUpgradeMult { get; set; } = 1.2;
    public int HpUpgradeCost { get; set; } = 10;
    public int HpUpgradeCostIncrease { get; set; } = 5;
    /// <summary>Multiplies every player's special item chance in the shop.</summary>
    public double SpecialChanceMult { get; set; } = 1;

    /// <summary>Clamps every value into a sane range so a host can't break the game.</summary>
    public LobbySettings Sanitized() => new()
    {
        Public = Public,
        Lives = Math.Clamp(Lives, 1, 20),
        ShopSeconds = Math.Clamp(ShopSeconds, 10, 600),
        StartMoney = Math.Clamp(StartMoney, 0, 10000),
        Income = Math.Clamp(Income, 0, 1000),
        WinBonus = Math.Clamp(WinBonus, 0, 1000),
        SpecialMode = SpecialMode,
        StartHp = Math.Clamp(double.IsFinite(StartHp) ? StartHp : 100, 1, 1_000_000),
        HpUpgradeMult = Math.Clamp(double.IsFinite(HpUpgradeMult) ? HpUpgradeMult : 1.2, 1, 10),
        HpUpgradeCost = Math.Clamp(HpUpgradeCost, 0, 10000),
        HpUpgradeCostIncrease = Math.Clamp(HpUpgradeCostIncrease, 0, 10000),
        SpecialChanceMult = Math.Clamp(double.IsFinite(SpecialChanceMult) ? SpecialChanceMult : 1, 0, 100)
    };
}
