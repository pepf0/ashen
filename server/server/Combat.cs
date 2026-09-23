using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ashen;

public static class Combat
{
    public static int TPS { get; set; } = 20;

    public static List<CombatEvent> CalculateCombatResults(PlayerInfo p1, PlayerInfo p2)
    {
        int tick = 0;
        List<CombatEvent> events = new();
        //Combat start events
        foreach (var passive in p1.Dynamics.Concat(p2.Dynamics))
        {
        }

        //Loop
        while (p1.Health > 0 || p2.Health > 0)
        {
            tick++;
        }

        return events;
    }

}

public abstract class CombatEvent
{
    public int Tick { get; set; }
    public Item Source { get; set; }
}

public class DamageEvent : CombatEvent
{
    
}
