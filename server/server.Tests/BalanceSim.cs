using Xunit.Abstractions;
namespace Ashen.Tests;

/// <summary>Balance report, not a real test: only runs with ASHEN_SIM=1, e.g.
/// ASHEN_SIM=1 dotnet test server.Tests --filter BalanceSim --logger "console;verbosity=detailed"</summary>
public class BalanceSim
{
    private readonly ITestOutputHelper o;
    public BalanceSim(ITestOutputHelper o) { this.o = o; TestHelpers.Init(); }

    static bool Off => Environment.GetEnvironmentVariable("ASHEN_SIM") != "1";

    static OwnedItem Own(string id, int lvl) => TestHelpers.Own(id, GameData.Items[id].IsSpecial ? 1 : lvl);

    static Loadout L(double hp, string? kit, int lvl, string? w, string? s, params string[] d)
    {
        var l = new Loadout { Name = "x", MaxHealth = hp, Kit = kit };
        if (w != null) l.Weapon = Own(w, lvl);
        if (s != null) l.Shirt = Own(s, lvl);
        for (int i = 0; i < d.Length; i++) l.Dynamics[i] = Own(d[i], lvl);
        return l;
    }

    static int Fight(Loadout a, Loadout b, int seed) => Combat.CalculateCombatResults(a, b, seed).Winner;

    /// <summary>Win rate of every build against every other (both sides, n seeds each).</summary>
    void RoundRobin(string title, (string Name, Func<Loadout> Make)[] builds, int n)
    {
        var wins = new double[builds.Length]; var dur = new double[builds.Length];
        for (int i = 0; i < builds.Length; i++)
        for (int j = i + 1; j < builds.Length; j++)
        for (int s = 0; s < n; s++)
        {
            var a = builds[i].Make(); var b = builds[j].Make();
            var r = s % 2 == 0 ? Combat.CalculateCombatResults(a, b, s * 7919 + i * 131 + j) : Combat.CalculateCombatResults(b, a, s * 7919 + i * 131 + j);
            int w = s % 2 == 0 ? r.Winner : (r.Winner == -1 ? -1 : 1 - r.Winner);
            if (w == 0) wins[i]++; else if (w == 1) wins[j]++; else { wins[i] += 0.5; wins[j] += 0.5; }
            dur[i] += r.Duration; dur[j] += r.Duration;
        }
        int games = (builds.Length - 1) * n;
        o.WriteLine($"=== {title} ===");
        foreach (var k in Enumerable.Range(0, builds.Length).OrderByDescending(k => wins[k]))
            o.WriteLine($"  {builds[k].Name,-28} {wins[k] / games * 100,5:F1}%   avg fight {dur[k] / games,4:F1}s");
    }

    static readonly string[] Weapons = { "generic.sword", "generic.hammer", "generic.frying_pan", "generic.bow", "pepf.guitar", "z7fox.whip", "ely7.blade", "xxzweiradxx.staff" };
    static readonly Dictionary<string, string> PackBuff = new() { ["pepf.guitar"] = "pepf.pick", ["z7fox.whip"] = "z7fox.emblem", ["ely7.blade"] = "ely7.whetstone", ["xxzweiradxx.staff"] = "xxzweiradxx.dumbbell" };
    static string Short(string id) => id.Replace("generic.", "").Replace("xxzweiradxx.", "xxz.");

    [Fact]
    public void A_Weapons()
    {
        if (Off) return;
        var b = new List<(string, Func<Loadout>)>();
        foreach (var w in Weapons)
        {
            if (GameData.Items[w].IsSpecial)
            {
                b.Add((Short(w), () => L(250, null, 2, w, "generic.band_shirt", "generic.knight", "generic.horseshoe")));
                b.Add((Short(w) + " + " + Short(PackBuff[w]), () => L(250, null, 2, w, "generic.band_shirt", PackBuff[w], "generic.horseshoe")));
            }
            else
                foreach (int lvl in new[] { 1, 3, 5 })
                    b.Add(($"{Short(w)} L{lvl}", () => L(250, null, lvl, w, "generic.band_shirt", "generic.knight", "generic.horseshoe")));
        }
        b.Add(("no weapon", () => L(250, null, 2, null, "generic.band_shirt", "generic.knight", "generic.horseshoe")));
        RoundRobin("Weapons (band shirt L2, knight L2, horseshoe L2, 250 HP, no kit; generics at L1/3/5)", b.ToArray(), 40);
    }

    [Fact]
    public void B_SupportItems()
    {
        if (Off) return;
        // Each item is put into the same base (sword L2, band shirt L2, horseshoe L2) and fought against a gauntlet.
        var gauntlet = new Func<int, Loadout>[]
        {
            s => L(250, null, 3, "generic.sword", "generic.band_shirt", "generic.knight", "generic.horseshoe"),
            s => L(250, null, 3, "generic.hammer", "generic.mike_shirt", "generic.knight", "generic.knight"),
            s => L(250, null, 3, "generic.bow", "generic.brown_shirt", "generic.horseshoe", "generic.horseshoe"),
            s => L(250, null, 3, "generic.frying_pan", "generic.beige_shirt", "generic.knight", "generic.horseshoe"),
            s => L(250, null, 1, "pepf.guitar", "generic.band_shirt", "generic.knight"),
            s => L(250, null, 1, "z7fox.whip", "generic.band_shirt", "generic.knight"),
        };
        var candidates = GameData.Items.Values.Where(i => i.Type != ItemType.Weapon).Select(i => i.Id).OrderBy(x => x).ToList();
        var rows = new List<(string, double)>();
        foreach (var id in candidates.Prepend("(empty slot)"))
        {
            var item = id == "(empty slot)" ? null : GameData.Items[id];
            double wins = 0; int games = 0;
            for (int g = 0; g < gauntlet.Length; g++)
            for (int s = 0; s < 60; s++)
            {
                var me = item?.Type == ItemType.Shirt
                    ? L(250, null, 2, "generic.sword", id, "generic.horseshoe", "generic.knight")
                    : item is null ? L(250, null, 2, "generic.sword", "generic.band_shirt", "generic.horseshoe", "generic.knight")
                    : L(250, null, 2, "generic.sword", "generic.band_shirt", "generic.horseshoe", "generic.knight", id);
                int w = s % 2 == 0 ? Fight(me, gauntlet[g](s), s * 31 + g) : 1 - Fight(gauntlet[g](s), me, s * 31 + g);
                wins += w == 0 ? 1 : w == 2 ? 0.5 : 0; games++;
            }
            rows.Add(($"{Short(id)} ({item?.Type.ToString().ToLower() ?? "-"}, {item?.Price ?? 0}$)", wins / games * 100));
        }
        o.WriteLine("=== Support items: sword L2 + band shirt L2 + horseshoe L2 + knight L2 + ITEM vs gauntlet (shirts replace the band shirt) ===");
        foreach (var r in rows.OrderByDescending(r => r.Item2)) o.WriteLine($"  {r.Item1,-34} {r.Item2,5:F1}%");
    }

    [Fact]
    public void C_KitsByWeapon()
    {
        if (Off) return;
        var kits = GameData.Kits.Keys.ToList();
        var weapons = new[] { "generic.sword", "generic.hammer", "generic.bow", "generic.frying_pan", "pepf.guitar", "z7fox.whip", "xxzweiradxx.staff", "ely7.blade" };
        o.WriteLine("=== Kits by weapon: same build for both sides, only the kit differs (row = win % vs all other kits) ===");
        o.WriteLine("  " + "weapon".PadRight(14) + string.Join("", kits.Select(k => k.PadLeft(11))));
        var total = kits.ToDictionary(k => k, _ => 0.0); int per = 0;
        foreach (var w in weapons)
        {
            var wins = kits.ToDictionary(k => k, _ => 0.0); int n = 50;
            for (int i = 0; i < kits.Count; i++)
            for (int j = i + 1; j < kits.Count; j++)
            for (int s = 0; s < n; s++)
            {
                Loadout Mk(string k) => L(300, k, 3, w, "generic.band_shirt", "generic.knight", "generic.horseshoe");
                bool swap = s % 2 == 1;
                int r = swap ? Fight(Mk(kits[j]), Mk(kits[i]), s * 17 + i * 7 + j) : Fight(Mk(kits[i]), Mk(kits[j]), s * 17 + i * 7 + j);
                if (r == -1) { wins[kits[i]] += .5; wins[kits[j]] += .5; }
                else if ((r == 0) != swap) wins[kits[i]]++; else wins[kits[j]]++;
            }
            int games = (kits.Count - 1) * n; per += games;
            foreach (var k in kits) total[k] += wins[k];
            o.WriteLine("  " + Short(w).PadRight(14) + string.Join("", kits.Select(k => $"{wins[k] / games * 100,10:F0}%")));
        }
        o.WriteLine("  " + "ALL".PadRight(14) + string.Join("", kits.Select(k => $"{total[k] / per * 100,10:F0}%")));
    }

    [Fact]
    public void H_KitMatrix()
    {
        if (Off) return;
        // Counter circle: Trickster > Berserker > Sniper > Guardian > Duelist > Gambler > Trickster.
        var kits = new[] { "trickster", "berserker", "sniper", "guardian", "duelist", "gambler" };
        var weapons = new[] { "generic.sword", "generic.hammer", "generic.bow", "generic.frying_pan", "pepf.guitar", "z7fox.whip" };
        var win = new double[kits.Length, kits.Length];
        int n = 40;
        for (int i = 0; i < kits.Length; i++)
        for (int j = i + 1; j < kits.Length; j++)
        for (int w = 0; w < weapons.Length; w++)
        for (int s = 0; s < n; s++)
        {
            Loadout Mk(string k) => L(300, k, 3, weapons[w], "generic.band_shirt", "generic.knight", "generic.horseshoe");
            bool swap = s % 2 == 1; int seed = s * 17 + i * 7 + j * 3 + w * 1009;
            int r = swap ? Fight(Mk(kits[j]), Mk(kits[i]), seed) : Fight(Mk(kits[i]), Mk(kits[j]), seed);
            double a = r == -1 ? .5 : (r == 0) != swap ? 1 : 0;
            win[i, j] += a; win[j, i] += 1 - a;
        }
        int games = weapons.Length * n;
        o.WriteLine("=== Kit vs kit (row win %, same build L3 across 6 weapons) ===");
        o.WriteLine("  " + "".PadRight(10) + string.Join("", kits.Select(k => k[..5].PadLeft(7))) + "    ALL");
        for (int i = 0; i < kits.Length; i++)
        {
            double all = Enumerable.Range(0, kits.Length).Where(j => j != i).Sum(j => win[i, j]) / (games * (kits.Length - 1));
            o.WriteLine("  " + kits[i].PadRight(10) + string.Join("", kits.Select((_, j) => i == j ? "      -" : $"{win[i, j] / games * 100,6:F0}%")) + $"{all * 100,6:F0}%");
        }
        o.WriteLine("  circle: " + string.Join("  ", kits.Select((k, i) => $"{k[..4]}>{kits[(i + 1) % kits.Length][..4]} {win[i, (i + 1) % kits.Length] / games * 100:F0}%")));
    }

    [Fact]
    public void D_Packs()
    {
        if (Off) return;
        var packs = new[] { "pepf", "z7fox", "ely7", "xxzweiradxx" };
        var b = new List<(string, Func<Loadout>)>();
        foreach (var p in packs)
        {
            var items = GameData.Items.Values.Where(i => i.Pack.ToString().Equals(p, StringComparison.OrdinalIgnoreCase)).ToList();
            string w = items.First(i => i.Type == ItemType.Weapon).Id, s = items.First(i => i.Type == ItemType.Shirt).Id;
            var dyn = items.Where(i => i.Type != ItemType.Weapon && i.Type != ItemType.Shirt).Select(i => i.Id).ToArray();
            b.Add(($"{p} full pack", () => L(400, null, 1, w, s, dyn)));
        }
        b.Add(("generics L5", () => L(400, null, 5, "generic.sword", "generic.band_shirt", "generic.knight", "generic.horseshoe", "generic.knight", "generic.horseshoe")));
        b.Add(("hammer L5 generics", () => L(400, null, 5, "generic.hammer", "generic.mike_shirt", "generic.knight", "generic.horseshoe", "generic.knight", "generic.horseshoe")));
        RoundRobin("Full packs (all 5 items, 400 HP, no kit) vs maxed generics", b.ToArray(), 100);
    }

    [Fact]
    public void E_RandomTournament()
    {
        if (Off) return;
        // Random mid-game builds; win rate of each item / kit when present.
        var rng = new Random(12345);
        var generics = GameData.Generics.ToList();
        var specials = GameData.Specials.ToList();
        var kits = GameData.Kits.Keys.ToList();
        Item Pick(ItemType? type, bool dyn)
        {
            bool special = rng.NextDouble() < 0.3;
            var pool = (special ? specials : generics).Where(i => dyn ? i.Type is not (ItemType.Weapon or ItemType.Shirt) : i.Type == type).ToList();
            return pool[rng.Next(pool.Count)];
        }
        Loadout Random()
        {
            var l = new Loadout { Name = "r", MaxHealth = 200 * Math.Pow(1.2, rng.Next(0, 4)), Kit = kits[rng.Next(kits.Count)] };
            if (rng.NextDouble() < 0.95) { var w = Pick(ItemType.Weapon, false); l.Weapon = new OwnedItem(w, w.IsSpecial ? 1 : rng.Next(1, 5)); }
            if (rng.NextDouble() < 0.85) { var s = Pick(ItemType.Shirt, false); l.Shirt = new OwnedItem(s, s.IsSpecial ? 1 : rng.Next(1, 5)); }
            int nd = rng.Next(1, 5); var used = new HashSet<string>();
            for (int i = 0; i < nd; i++) { var d = Pick(null, true); if (!used.Add(d.Id)) continue; l.Dynamics[i] = new OwnedItem(d, d.IsSpecial ? 1 : rng.Next(1, 5)); }
            return l;
        }
        var stat = new Dictionary<string, (double W, int N)>();
        void Add(string k, double w) { var c = stat.GetValueOrDefault(k); stat[k] = (c.W + w, c.N + 1); }
        double dur = 0; int fights = 6000;
        for (int f = 0; f < fights; f++)
        {
            var a = Random(); var b = Random();
            var r = Combat.CalculateCombatResults(a, b, f);
            dur += r.Duration;
            for (int side = 0; side < 2; side++)
            {
                var me = side == 0 ? a : b;
                double w = r.Winner == -1 ? 0.5 : r.Winner == side ? 1 : 0;
                Add("kit " + me.Kit, w);
                foreach (var it in me.AllItems().Select(x => x.Id).Distinct()) Add(Short(it), w);
                if (me.Weapon is null) Add("(no weapon)", w);
                if (me.Weapon is { } wpn && PackBuff.TryGetValue(wpn.Id, out var buff) && me.Dynamics.Any(d => d?.Id == buff)) Add("COMBO " + Short(wpn.Id) + "+" + Short(buff), w);
            }
        }
        o.WriteLine($"=== Random tournament: {fights} fights of random mid-game builds (30% specials, random kits, 200-346 HP); avg fight {dur / fights:F1}s ===");
        foreach (var kv in stat.OrderByDescending(kv => kv.Value.W / kv.Value.N))
            o.WriteLine($"  {kv.Key,-30} {kv.Value.W / kv.Value.N * 100,5:F1}%   (n={kv.Value.N})");
    }
}

public class SpecialVsL5Sim
{
    private readonly ITestOutputHelper o;
    public SpecialVsL5Sim(ITestOutputHelper o) { this.o = o; TestHelpers.Init(); }

    [Fact]
    public void G_SpecialsVsLevel5()
    {
        if (Environment.GetEnvironmentVariable("ASHEN_SIM") != "1") return;
        var specials = new (string W, string? Buff)[] { ("pepf.guitar", null), ("pepf.guitar", "pepf.pick"), ("z7fox.whip", null), ("z7fox.whip", "z7fox.emblem"),
            ("ely7.blade", null), ("ely7.blade", "ely7.whetstone"), ("xxzweiradxx.staff", null), ("xxzweiradxx.staff", "xxzweiradxx.dumbbell") };
        var generics = new[] { "generic.sword", "generic.hammer", "generic.frying_pan", "generic.bow" };
        Loadout Mk(string w, int lvl, string? buff)
        {
            var l = new Loadout { Name = "x", MaxHealth = 300, Weapon = TestHelpers.Own(w, lvl), Shirt = TestHelpers.Own("generic.band_shirt", 3) };
            l.Dynamics[0] = TestHelpers.Own("generic.knight", 3); l.Dynamics[1] = TestHelpers.Own("generic.horseshoe", 3);
            if (buff != null) l.Dynamics[2] = TestHelpers.Own(buff);
            return l;
        }
        foreach (var probe in new[] { Mk("pepf.guitar", 1, null), Mk("generic.sword", 5, null), Mk("generic.sword", 1, null) })
            o.WriteLine("  probe " + probe.Weapon!.Id + " L" + probe.Weapon.Level + " " + string.Join(", ", Combat.PreviewStats(probe).Select(kv => $"{kv.Key}={kv.Value:F2}")));
        var dbg = Combat.CalculateCombatResults(Mk("generic.sword", 5, null), Mk("pepf.guitar", 1, null), 3);
        o.WriteLine($"  dbg sword5 vs guitar: winner {dbg.Winner} dur {dbg.Duration:F1} dealt {string.Join("/", dbg.DamageDealt.Select(d => d.ToString("F0")))}");
        o.WriteLine("=== Special weapon (+buff) vs level 5 generic weapons, same support (band shirt, knight, horseshoe at L3), 300 HP, no kit ===");
        foreach (var (w, buff) in specials)
        {
            double wins = 0; int games = 0;
            var per = new List<string>();
            foreach (var g in generics)
            {
                double gw = 0;
                for (int s = 0; s < 100; s++)
                {
                    bool swap = s % 2 == 1;
                    var a = Mk(w, 1, buff); var b = Mk(g, 5, null);
                    int r = Combat.CalculateCombatResults(swap ? b : a, swap ? a : b, s * 131 + g.Length).Winner;
                    double v = r == -1 ? 0.5 : (r == 0) != swap ? 1 : 0;
                    gw += v; wins += v; games++;
                }
                per.Add($"{g.Replace("generic.", "")} {gw}%");
            }
            o.WriteLine($"  {w + (buff != null ? " + " + buff.Split('.')[1] : ""),-38} {wins / games * 100,5:F1}%   ({string.Join(", ", per)})");
        }
    }
}

public class GenericComboSim
{
    private readonly ITestOutputHelper o;
    public GenericComboSim(ITestOutputHelper o) { this.o = o; TestHelpers.Init(); }

    [Fact]
    public void F_GenericCombos()
    {
        if (Environment.GetEnvironmentVariable("ASHEN_SIM") != "1") return;
        var weapons = new[] { "sword", "hammer", "frying_pan", "bow" };
        var shirts = new[] { "band_shirt", "brown_shirt", "beige_shirt", "mike_shirt" };
        var dyns = new[] { new[] { "knight", "horseshoe" }, new[] { "knight" }, new[] { "horseshoe" } };
        var builds = new List<(string Name, Func<Loadout> Make)>();
        foreach (var w in weapons) foreach (var s in shirts) foreach (var d in dyns)
            builds.Add(($"{w} + {s} + {string.Join("/", d)}", () =>
            {
                var l = new Loadout { Name = "g", MaxHealth = 250, Weapon = TestHelpers.Own("generic." + w, 3), Shirt = TestHelpers.Own("generic." + s, 3) };
                for (int i = 0; i < d.Length; i++) l.Dynamics[i] = TestHelpers.Own("generic." + d[i], 3);
                return l;
            }));
        var wins = new double[builds.Count]; int n = 8;
        for (int i = 0; i < builds.Count; i++)
        for (int j = i + 1; j < builds.Count; j++)
        for (int s = 0; s < n; s++)
        {
            bool swap = s % 2 == 1;
            var r = Combat.CalculateCombatResults(swap ? builds[j].Make() : builds[i].Make(), swap ? builds[i].Make() : builds[j].Make(), s * 997 + i * 61 + j).Winner;
            if (r == -1) { wins[i] += .5; wins[j] += .5; } else if ((r == 0) != swap) wins[i]++; else wins[j]++;
        }
        int games = (builds.Count - 1) * n;
        o.WriteLine("=== Generic-only builds (each item once), all level 3, 250 HP, no kit ===");
        foreach (var k in Enumerable.Range(0, builds.Count).OrderByDescending(k => wins[k]))
            o.WriteLine($"  {builds[k].Name,-55} {wins[k] / games * 100,5:F1}%");
    }
}
