namespace Ashen;

/// <summary>A fight running in real time during the combat phase, with its players and spectators.</summary>
public class LiveFight
{
    public int Id { get; init; }
    public required Fight Fight { get; init; }
    /// <summary>Players by fighter index; a ghost side is null.</summary>
    public Player?[] Players { get; init; } = new Player?[2];
    public HashSet<long> Spectators { get; } = new();
    public bool Ended { get; set; }
    public int TicksDone { get; set; }

    public int? IndexOf(long playerId) =>
        Players[0]?.Id == playerId ? 0 : Players[1]?.Id == playerId ? 1 : null;

    public object Summary() => new
    {
        id = Id,
        names = new[] { Fight.Info(0).Name, Fight.Info(1).Name },
        arena = Fight.Arena,
        ended = Ended,
        winner = Fight.Result?.Winner
    };
}
