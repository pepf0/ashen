namespace Ashen;

public enum EventTrigger
{
    WinFight,
    FlawlessWin,
    OwnSpecial,
    OwnItem,
    FullPack,
    MoneyAtLeast
}

/// <summary>A "first to ..." lobby event from events.json. Each can be claimed once per lobby.</summary>
public class LobbyEvent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public EventTrigger Trigger { get; set; }
    public double? Value { get; set; }
    public string? Item { get; set; }
    public int? RewardMoney { get; set; }
    public List<string>? RewardEffects { get; set; }
    public string Reward { get; set; } = "";
    /// <summary>Events that can't be claimed in round 1.</summary>
    public bool NotFirstRound { get; set; }
}
