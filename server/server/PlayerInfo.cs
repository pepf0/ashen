using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ashen;

namespace ashen;

public class PlayerInfo
{
    public OwnedItem Weapon { get; set; }
    public OwnedItem Shirt { get; set; }
    public OwnedItem[] Dynamics { get; set; } = new OwnedItem[4];
    public double Health { get; set; } = 1000;
}

public class OwnedItem : Item
{
    public double RealCooldown { get; set; }
    public double CurrentCooldown { get; set; }
}