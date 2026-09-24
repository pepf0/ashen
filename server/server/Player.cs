using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ashen;

public class Player
{
    public OwnedItem? Weapon { get; set; }
    public OwnedItem? Shirt { get; set; }
    public OwnedItem?[] Dynamics { get; set; } = new OwnedItem?[4];
    public double Health { get; set; } = 1000;
    public double Money { get; set; } = 100;
    public double SpecialChance { get; set; } = 0.01;
    public bool Stunned { get; set; } = false;
    public double StunDurationLeft { get; set; } = 0;
    public double DodgeChance { get; set; } = 0;
    public double DamageReduction { get; set; } = 0;
    public bool Invulnerable { get; set; } = false;
    public double InvulnerableDurationLeft { get; set; } = 0;

}
