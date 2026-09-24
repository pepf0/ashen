namespace Ashen;

public abstract class CombatEvent
{
    public int Tick { get; set; }
    public Player Source { get; set; }
    public Player Target { get; set; }
    public Item ItemSource { get; set; }
}
