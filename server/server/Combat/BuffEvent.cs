namespace Ashen.Combat;
    public class BuffEvent : CombatEvent
    {
        public Item ItemTarget { get; set; }
        public string Stat { get; set; }
        public double? Mult { get; set; }
        public double? Add { get; set; }
        public double? Duration { get; set; }
        public int? MaxStacks { get; set; }
    }