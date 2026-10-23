namespace Ashen;

public enum EventTrigger
{
    WinFight,
    FlawlessWin,
    OwnSpecial,
    OwnItem,
    FullPack,
    MoneyAtLeast,
    /// <summary>Not claimed: everyone who fought rounds 1 and 2 weaponless gets the shop 3 special offer.</summary>
    WeaponlessStart
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
    /// <summary>Chance the event is active in a match (null = always). Inactive events are hidden.</summary>
    public double? Chance { get; set; }
    /// <summary>Rounds in which an active event is announced to everyone as a banner.</summary>
    public List<int>? AnnounceRounds { get; set; }
    public string? AnnounceText { get; set; }
}
