using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>The five classes of a count host (abstract-war 3: levy, foot, missile, horse, elite).</summary>
public enum CountClass : byte { Levy, Foot, Missile, Horse, Elite }

/// <summary>One count scenario (AW-10): the two sides' men by class. Side 0 stands west, side 1 east.</summary>
public sealed record CountSpec(string Name, int[] A, int[] B)
{
    public int[] Side(int s) => s == 0 ? A : B;
}

/// <summary>
/// The 40 count scenarios of the AW-10 gate (abstract-war 4.4: class mixes, 200 to 4,000 a side) as kits for the v2 resolver, and the per-class result of a v2 run, which the
/// calibrate tool (<c>--abstract count</c>) fits the count tables to and the gate compares them with. The kits are the v2 scenarios' own (levy: axe or spear, padded, no shield;
/// foot: sword and heater, padded; missile: longbow, padded; horse: lance, mail, heater, mounted; elite: sword and heater, mail), laid out as the battle spec's deployment: foot in
/// ranks of five at 1.2 m a file, missiles three metres behind them, horse forty metres back charging.
/// </summary>
public static class CountScenarios
{
    public const int Classes = 5;
    private static readonly string[] MixNames = ["levy", "foot", "elite", "levy+foot", "foot+missile", "levy+missile", "foot+horse", "foot+missile+horse", "elite+horse", "foot+elite", "levy+foot+missile", "horse+foot"];
    private static readonly int[][] Mixes =
    [
        [10, 0, 0, 0, 0], [0, 10, 0, 0, 0], [0, 0, 0, 0, 10], [5, 5, 0, 0, 0], [0, 7, 3, 0, 0], [6, 0, 4, 0, 0],
        [0, 8, 0, 2, 0], [0, 5, 3, 2, 0], [0, 0, 0, 3, 7], [0, 6, 0, 0, 4], [4, 3, 3, 0, 0], [0, 3, 1, 6, 0],
    ];
    private static readonly int[] Sizes = [200, 300, 500, 800, 1000, 1500, 2000, 3000, 4000];

    private static int[] Of(int n, int[] mix)
    {
        var r = new int[Classes]; int left = n;
        for (int c = 0; c < Classes; c++) { r[c] = n * mix[c] / 10; left -= r[c]; }
        for (int c = 0; left > 0; c = (c + 1) % Classes) if (mix[c] > 0) { r[c]++; left--; }
        return r;
    }

    /// <summary>The gate's 40 scenarios, in a fixed order (mix pairs and sizes cycle through the tables; side B is a size ratio of side A).</summary>
    public static IReadOnlyList<CountSpec> Gate { get; } = Build();

    private static List<CountSpec> Build()
    {
        var l = new List<CountSpec>();
        int[] ratioNum = [1, 1, 3, 2, 1, 4, 5, 1], ratioDen = [1, 1, 2, 3, 2, 3, 4, 1];
        for (int i = 0; i < 40; i++)
        {
            int ma = i * 7 % 12, mb = (i * 5 + 3 + i / 12) % 12;
            if (ma == mb) mb = (mb + 1) % 12;
            int na = Sizes[(i * 4 + i / 9) % Sizes.Length], r = i % ratioNum.Length;
            int nb = Math.Clamp(na * ratioNum[r] / ratioDen[r], 200, 4000);
            l.Add(new CountSpec($"S{i + 1:00} {MixNames[ma]} {na} v {MixNames[mb]} {nb}", Of(na, Mixes[ma]), Of(nb, Mixes[mb])));
        }
        return l;
    }

    private static int Skill(ulong seed, int side, int i, int level) => Math.Clamp(level - 5 + (int)(DetRng.NextUInt(seed, 0, (ulong)(side * 10_000 + i), 7, 1) % 11), 0, 100);

    private static FighterKit Man(string name, int team, int i, WeaponInstance? w, WornItem[] worn, string? shield, int skill, int x, int y) =>
        new() { Name = name, Team = team, Person = (ulong)(team * 100_000 + i + 1), Weapon = w, Worn = worn, Shield = shield, Skill = skill, X = x, Y = y, FaceX = team == 0 ? FightMath.One : -FightMath.One, Courage = 70 };

    /// <summary>The kits of one scenario (side 0 first), with the riders flagged in <paramref name="mounted"/>.</summary>
    public static List<FighterKit> Kits(CountSpec spec, ulong seed, out HashSet<int> mounted)
    {
        var kits = new List<FighterKit>();
        mounted = [];
        var padded = FightCalibration.Padded.Worn.ToArray();
        var mailed = FightCalibration.Mailed.Worn.ToArray();
        for (int side = 0; side < 2; side++)
        {
            var n = spec.Side(side);
            int sign = side == 0 ? -1 : 1;
            int footN = n[(int)CountClass.Levy] + n[(int)CountClass.Foot] + n[(int)CountClass.Elite];
            int files = footN > 0 ? Math.Max(1, (footN + 4) / 5) : Math.Max(1, (n[(int)CountClass.Missile] + n[(int)CountClass.Horse] + 1) / 2);
            int ranks = footN > 0 ? (footN + files - 1) / files : 0;
            int width = files * 1200;
            int idx = 0;
            // foot in ranks of five: foot, elite, levy fill from the front
            for (int c = 0; c < 3; c++)
            {
                var cls = c == 0 ? CountClass.Foot : c == 1 ? CountClass.Elite : CountClass.Levy;
                for (int j = 0; j < n[(int)cls]; j++, idx++)
                {
                    int file = idx % files, rank = idx / files;
                    int x = sign * (4000 + (ranks - 1 - rank) * 1000), y = (file - files / 2) * 1200 + 600;
                    FighterKit k = cls switch
                    {
                        CountClass.Foot => Man($"Foot {idx + 1}", side, idx, FightCalibration.Sword, padded, "heater", Skill(seed, side, idx, 40), x, y),
                        CountClass.Elite => Man($"Guard {idx + 1}", side, idx, new WeaponInstance("arming_sword", "steel"), mailed, "heater", Skill(seed, side, idx, 65), x, y),
                        _ => Man($"Levy {idx + 1}", side, idx, idx % 2 == 0 ? FightCalibration.Spear : new WeaponInstance("axe", "iron"), padded, null, Skill(seed, side, idx, 30), x, y),
                    };
                    kits.Add(k);
                }
            }
            int back = 4000 + Math.Max(0, ranks) * 1000 + 3000;
            int ms = n[(int)CountClass.Missile], mfiles = Math.Max(1, Math.Min(ms, Math.Max(files, 1) * 2));
            for (int j = 0; j < ms; j++, idx++)
            {
                int file = j % mfiles, rank = j / mfiles;
                int sk = Skill(seed, side, idx, 45);
                kits.Add(Man($"Archer {idx + 1}", side, idx, null, padded, null, sk, sign * (footN > 0 ? back + rank * 1000 : 4000 + rank * 1000), (file - mfiles / 2) * Math.Max(600, width / mfiles) + 300) with { Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 24, ArcherySkill = sk });
            }
            int hs = n[(int)CountClass.Horse], hfiles = Math.Max(1, Math.Min(hs, Math.Max(files, 1)));
            for (int j = 0; j < hs; j++, idx++)
            {
                int file = j % hfiles, rank = j / hfiles;
                int sk = 45 + (int)(DetRng.NextUInt(seed, 0, (ulong)(side * 10_000 + idx), 7, 2) % 25);
                mounted.Add(kits.Count);
                kits.Add(Man($"Rider {idx + 1}", side, idx, MountedCalibration.Lance, mailed, "heater", sk, sign * (footN + ms > 0 ? back + 37_000 + rank * 2000 : 40_000 + rank * 2000), (file - hfiles / 2) * Math.Max(1500, width / hfiles) + 750));
            }
        }
        return kits;
    }

    public static BattleInput InputOf(CountSpec spec, ulong seed)
    {
        var kits = Kits(spec, seed, out var mounted);
        return new BattleInput(kits, seed, seed, 8 * 3600, "count scenario") { Mounted = mounted, Leaders = true, FirstAid = false, Aftermath = false, SideKinds = new Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = SideKind.Army } };
    }

    /// <summary>What a v2 run of a count scenario came to, per side and class.</summary>
    public sealed class V2Result
    {
        public int Winner, DurationS;
        public readonly int[][] Start = [new int[Classes], new int[Classes]], Casualties = [new int[Classes], new int[Classes]], Dead = [new int[Classes], new int[Classes]];
        public readonly int[][] Crippled = [new int[Classes], new int[Classes]], Fled = [new int[Classes], new int[Classes]], Yielded = [new int[Classes], new int[Classes]];
        public int Total(int[][] a, int s) => a[s].Sum();
    }

    /// <summary>Fights the scenario in v2 and reads the end state by the kit's class (the kits are in class order within a side: foot, elite, levy, missile, horse).</summary>
    public static V2Result RunV2(FightContent fc, WoundTables tables, AbstractBattleDef k, CountSpec spec, ulong seed)
    {
        var b = new AbstractBattle(fc, tables, InputOf(spec, seed), k);
        while (!b.Over) b.StepOnce();
        var r = new V2Result { Winner = b.Winner, DurationS = b.Step * b.K.StepS };
        var order = new[] { CountClass.Foot, CountClass.Elite, CountClass.Levy, CountClass.Missile, CountClass.Horse };
        int[] at = new int[2];
        foreach (var x in b.Soldiers)
        {
            int side = x.Side; int pos = at[side]++;
            int acc = 0; CountClass cls = CountClass.Foot;
            foreach (var o in order) { acc += spec.Side(side)[(int)o]; if (pos < acc) { cls = o; break; } }
            int c = (int)cls; r.Start[side][c]++;
            if (x.Status is SoldierStatus.Down or SoldierStatus.Dead)
            {
                r.Casualties[side][c]++;
                if (x.Status == SoldierStatus.Dead || x.Worst >= OutcomeClass.Grave) r.Dead[side][c]++; else if (x.Worst >= OutcomeClass.Serious) r.Crippled[side][c]++;
            }
            else if (x.Status == SoldierStatus.Fled || x.Routing) r.Fled[side][c]++;
            else if (x.Status == SoldierStatus.Surrendered) r.Yielded[side][c]++;
        }
        return r;
    }
}
