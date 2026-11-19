using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The duel harness on the shipped fight (resolver-v2 8, melee ML-14; <c>tools/Ravensguard.Calibrate</c>): seeded AI-vs-AI duels per pairing through <see cref="FightWorld"/>
/// and <see cref="CombatBrain"/>, every blow through resolver v2 and every wound into a <see cref="WoundBook"/>, then a minute of aftermath with or without the winner's first
/// aid (<see cref="TreatAi"/> and <see cref="Treatment"/>, the real rules). Statistics only (a tool, not sim state), so the averages are doubles.
/// </summary>
public static class FightCalibration
{
    public sealed record Kit(string Name, WornItem[] Worn, string? Shield = null);

    public static readonly Kit Unarmoured = new("unarmoured", [new("tunic", "linen")]);
    public static readonly Kit Padded = new("padded", [new("tunic", "linen"), new("gambeson", "linen")]);
    public static readonly Kit Mailed = new("mail", [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")]);
    /// <summary>A full harness (P4-C): the step-A kit plus gauntlets, cuisses, a backplate and a mail coif, so the hands, thighs, back and neck are not open (the AI aims at gaps).</summary>
    public static readonly Kit Plated = new("plate", [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("mail_coif", "iron"), new("breastplate", "steel"),
        new("backplate", "steel"), new("helm", "steel"), new("vambraces", "steel"), new("gauntlets", "steel"), new("cuisses", "steel"), new("greaves", "steel")]);

    public sealed record Pairing(string Name, WeaponInstance WeaponA, Kit KitA, WeaponInstance WeaponB, Kit KitB, string? ShieldA = null, string? ShieldB = null);

    public static readonly WeaponInstance Sword = new("arming_sword", "steel");
    public static readonly WeaponInstance Spear = new("spear", "steel");
    public static readonly WeaponInstance Axe = new("axe", "steel");
    public static readonly WeaponInstance Mace = new("mace", "steel");

    /// <summary>The main pairings of the calibration table (the spike's three, the armour ladder for each weapon against a swordsman, and the mace against plate).</summary>
    public static IReadOnlyList<Pairing> MainPairings() =>
    [
        new("sword vs sword (padded)", Sword, Padded, Sword, Padded),
        new("sword vs sword (unarmoured)", Sword, Unarmoured, Sword, Unarmoured),
        new("spear vs sword (unarmoured)", Spear, Padded, Sword, Unarmoured),
        new("spear vs sword (padded)", Spear, Padded, Sword, Padded),
        new("axe vs sword (mail)", Axe, Padded, Sword, Mailed),
        new("mace vs sword (padded)", Mace, Padded, Sword, Padded),
        new("sword vs sword (plate)", Sword, Plated, Sword, Plated),
        new("mace vs sword (plate)", Mace, Plated, Sword, Plated),
        new("sword (padded) vs sword (plate)", Sword, Padded, Sword, Plated),
        new("mace (padded) vs sword (plate)", Mace, Padded, Sword, Plated),
        new("sword+heater vs sword (padded)", Sword, Padded, Sword, Padded, "heater"),
    ];

    public sealed class Row
    {
        public required string Name { get; init; }
        public int N, WinsA, WinsB, Fatal, Knockouts, KoBledOut, KoBledOutTreated, KoTreatedN;
        public double TotalS;
        public long Thrown, Blocked, Parried, Dodged, Severs, Landed;
        public double MeanS => N == 0 ? 0 : TotalS / N;
        public double DefendedPct => Thrown == 0 ? 0 : 100.0 * (Blocked + Parried + Dodged) / Thrown;
        public double WinAPct => N == 0 ? 0 : 100.0 * WinsA / N;
        public double SeversPerDuel => N == 0 ? 0 : (double)Severs / N;
        public double FatalPct => N == 0 ? 0 : 100.0 * Fatal / N;
        public double BledOutPct => Knockouts == 0 ? 0 : 100.0 * KoBledOut / Knockouts;
        public double BledOutTreatedPct => KoTreatedN == 0 ? 0 : 100.0 * KoBledOutTreated / KoTreatedN;
    }

    public sealed record Duel(double Seconds, int Winner, bool LoserDied, bool LoserKo, bool BledOutLater, int Thrown, int Blocked, int Parried, int Dodged, int Severs, int Landed, ulong Hash);

    public static FighterKit KitOf(string name, int team, WeaponInstance w, Kit k, string? shield, int skill, int x, int faceX) =>
        new() { Name = name, Team = team, Weapon = w, Worn = k.Worn, Shield = shield, Skill = skill, X = x, FaceX = faceX };

    /// <summary>One seeded duel: skills 30-59 per (duel, side) as the spike's, 6 m apart, up to 240 s; then 60 s of aftermath, the winner treating the loser when <paramref name="treat"/>.</summary>
    public static Duel RunDuel(FightContent content, Pairing p, int i, bool treat, int maxS = 240, int aftermathS = 60)
    {
        var w = new FightWorld(content, (ulong)(1000 + i), (ulong)(i + 1), gameSec0: 8 * 3600) { RecordEvents = false };
        int severs = 0;
        w.OnBlow = (_, _, r, _) => { if (r.Severed) severs++; };
        var a = w.Add(KitOf("A", 0, p.WeaponA, p.KitA, p.ShieldA, Melee.MeleeScenarios.SkillFor(i, 0), -3000, FightMath.One));
        var b = w.Add(KitOf("B", 1, p.WeaponB, p.KitB, p.ShieldB, Melee.MeleeScenarios.SkillFor(i, 1), 3000, -FightMath.One));
        int max = maxS * 20;
        while (!w.IsOver && w.Tick < max) w.StepWithAi();
        double secs = w.Tick / 20.0;
        int winner = w.WinnerTeam;
        var loser = winner == 0 ? b : winner == 1 ? a : (a.Active ? b : a);
        bool died = loser.Act == FAct.Dead;
        bool ko = loser.Act == FAct.Down;
        bool later = false;
        if (ko)
        {
            var medic = winner == 0 ? a : b;
            var items = new ItemBag().Add("rope", 1).Add("bandage", 4);
            int end = w.Tick + aftermathS * 20, busyUntil = w.Tick + 3 * 20;
            while (w.Tick < end && !loser.Book.Dead)
            {
                if (treat && winner >= 0 && w.Tick >= busyUntil)
                {
                    var req = TreatAi.Next(loser.Book, loser.Plan, items, w.GameSec, medic.Skill / 2, (ulong)(medic.Id + 1));
                    if (req is { } rq)
                    {
                        var res = Treatment.Perform(loser.Book, loser.Plan, rq, items);
                        busyUntil = w.Tick + Math.Max(1, res.SimSeconds) * 20;
                    }
                    else busyUntil = w.Tick + 20;
                }
                w.StepWithAi();
            }
            later = loser.Book.Dead;
        }
        return new Duel(secs, winner, died, ko, later, a.Thrown + b.Thrown, a.BlockedBy + b.BlockedBy, a.ParriedBy + b.ParriedBy, a.DodgedBy + b.DodgedBy, severs,
            a.Landed + b.Landed, w.Hash());
    }

    /// <summary>
    /// Two lines of <paramref name="perSide"/> fighters 14 m apart (the spike's skirmish on the shipped fight): the first side spears and swords with heaters, the second axes, maces,
    /// swords and a bow; skills 30-64 by (seed, side, index). The ML-15 and ML-16 fight, and the Godot demo.
    /// </summary>
    public static FightWorld Skirmish(FightContent c, ulong seed, int perSide = 10, bool? near = null)
    {
        var w = new FightWorld(c, seed, seed, 8 * 3600) { ForceNear = near };
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
            {
                int x = team == 0 ? -7000 : 7000, y = (i * 2 - (perSide - 1)) * 700;
                int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(team * 100 + i), 7, 0) % 35);
                int face = team == 0 ? FightMath.One : -FightMath.One;
                FighterKit k;
                if (team == 0)
                    k = i % 3 == 0 ? KitOf($"Wolf {i + 1}", 0, Spear, Padded, null, skill, x - 600, face) with { Y = y }
                      : KitOf($"Wolf {i + 1}", 0, Sword, i % 3 == 1 ? Mailed : Padded, "heater", skill, x, face) with { Y = y };
                else
                    k = i % 5 == 4 ? new FighterKit { Name = $"Crow {i + 1}", Team = 1, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = Padded.Worn, Skill = skill, X = x + 6000, Y = y, FaceX = face }
                      : KitOf($"Crow {i + 1}", 1, i % 4 == 0 ? Axe : i % 4 == 1 ? Mace : Sword, i % 4 == 0 ? Mailed : Padded, i % 2 == 0 ? "round_shield" : null, skill, x, face) with { Y = y };
                w.Add(k);
            }
        return w;
    }

    /// <summary>Runs <paramref name="duels"/> duels of a pairing (in parallel; each duel is its own seeded world, so the result does not depend on the thread count).</summary>
    public static Row Run(FightContent content, Pairing p, int duels, bool withTreatment = true)
    {
        var plain = new Duel[duels];
        var treated = new Duel[duels];
        Parallel.For(0, duels, i =>
        {
            plain[i] = RunDuel(content, p, i, treat: false);
            if (withTreatment) treated[i] = RunDuel(content, p, i, treat: true);
        });
        var row = new Row { Name = p.Name };
        for (int i = 0; i < duels; i++)
        {
            var d = plain[i];
            row.N++; row.TotalS += d.Seconds;
            if (d.Winner == 0) row.WinsA++; else if (d.Winner == 1) row.WinsB++;
            row.Thrown += d.Thrown; row.Blocked += d.Blocked; row.Parried += d.Parried; row.Dodged += d.Dodged; row.Severs += d.Severs; row.Landed += d.Landed;
            if (d.LoserDied || d.BledOutLater) row.Fatal++;
            if (d.LoserKo) { row.Knockouts++; if (d.BledOutLater) row.KoBledOut++; }
            if (withTreatment && treated[i].LoserKo) { row.KoTreatedN++; if (treated[i].BledOutLater) row.KoBledOutTreated++; }
        }
        return row;
    }

    public static string Table(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Pairing | n | mean s | defended | A wins | severs/duel | fatal | KO'd then bled out (untreated) | (with first aid) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Name} | {r.N} | {r.MeanS:0.0} | {r.DefendedPct:0}% | {r.WinAPct:0}% | {r.SeversPerDuel:0.00} | {r.FatalPct:0}% | {(r.Knockouts == 0 ? "-" : $"{r.BledOutPct:0}%")} | {(r.KoTreatedN == 0 ? "-" : $"{r.BledOutTreatedPct:0}%")} |");
        long ko = rows.Sum(r => r.Knockouts), kb = rows.Sum(r => r.KoBledOut), kt = rows.Sum(r => r.KoTreatedN), kbt = rows.Sum(r => r.KoBledOutTreated);
        sb.AppendLine();
        sb.AppendLine($"Knocked out and bled out: {100.0 * kb / Math.Max(1, ko):0.0}% untreated, {100.0 * kbt / Math.Max(1, kt):0.0}% with first aid (target <= 35%).");
        sb.AppendLine("Targets (resolver-v2 8): duels 10-20 s; defended 40-60%; spear vs unarmoured sword >= 55% (aim 55-65); sword vs sword 50 +- 5; mace beats a sword against plate; severs <= 0.12 per duel.");
        return sb.ToString();
    }
}
