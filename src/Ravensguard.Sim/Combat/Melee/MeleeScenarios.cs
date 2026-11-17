using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee;

/// <summary>Stock loadouts, duel/skirmish setup and the headless benchmark used by the spike (docs/spikes/02-melee.md).</summary>
public static class MeleeScenarios
{
    public static readonly WeaponInstance Sword = new("arming_sword", "steel");
    public static readonly WeaponInstance Axe = new("axe", "steel");
    public static readonly WeaponInstance Spear = new("spear", "steel");
    public static readonly WeaponInstance Maul = new("maul", "steel");

    public static readonly WornItem[] Unarmoured = [new("tunic", "linen")];
    public static readonly WornItem[] Padded = [new("tunic", "linen"), new("gambeson", "linen")];
    public static readonly WornItem[] Mailed = [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")];

    public sealed record Matchup(string Name, Func<int, FighterSpec> A, Func<int, FighterSpec> B);

    private static FighterSpec Spec(string name, int team, WeaponInstance w, WornItem[] worn, Vec2 at, Vec2 face, int skill, double shield = 0, string sprite = "person.soldier_red")
        => new(name, team, w, worn, at, skill, ShieldCover: shield, Sprite: sprite) { FaceDir = face };

    /// <summary>The three benchmark matchups from the spike brief. The int is the duel index (seeds the skills).</summary>
    public static IReadOnlyList<Matchup> BenchMatchups { get; } =
    [
        new("sword vs sword (padded)",
            i => Spec("Aldric", 0, Sword, Padded, new(-3, 0), new(1, 0), SkillFor(i, 0), 0, "person.soldier_blue"),
            i => Spec("Bram", 1, Sword, Padded, new(3, 0), new(-1, 0), SkillFor(i, 1))),
        new("axe vs mail",
            i => Spec("Aldric", 0, Axe, Padded, new(-3, 0), new(1, 0), SkillFor(i, 0), 0, "person.coif_axeman"),
            i => Spec("Bram", 1, Sword, Mailed, new(3, 0), new(-1, 0), SkillFor(i, 1))),
        new("spear vs unarmoured",
            i => Spec("Aldric", 0, Spear, Padded, new(-3, 0), new(1, 0), SkillFor(i, 0), 0, "person.spearman_red"),
            i => Spec("Bram", 1, Sword, Unarmoured, new(3, 0), new(-1, 0), SkillFor(i, 1))),
    ];

    /// <summary>Skill 30-59, deterministic per (duel, side).</summary>
    public static int SkillFor(int duel, int side) => 30 + (int)(DetRng.NextUInt(0x5EED, duel, (ulong)side, 9, 0) % 30);

    // ---------------------------------------------------------------- duel

    public sealed record DuelResult(
        int Seed, double DurationS, int WinnerTeam, Outcome LoserOutcome, DownReason LoserDownReason, bool LoserDiedLater,
        int BlowsThrown, int Landed, int Blocked, int Parried, int Dodged, int Whiffed, int Feints, IReadOnlyDictionary<string, int> Wounds);

    public static DuelResult RunDuel(CombatTables tables, Matchup m, int duel, double maxS = 240, double aftermathS = 60)
    {
        var w = new MeleeWorld(tables, (ulong)(1000 + duel)) { EmitText = false };
        w.Add(m.A(duel)); w.Add(m.B(duel));
        int max = (int)(maxS * MeleeRules.TickHz);
        while (!w.IsOver && w.Tick < max) w.StepWithAi();
        double dur = w.TimeS;
        var inactive = w.Fighters.Where(f => !f.Active).ToList();
        int winner = inactive.Count == 1 ? w.Fighters.First(f => f.Active).Team : -1;
        var loser = inactive.Count > 0 ? inactive[0] : w.Fighters[0];
        var outcome = inactive.Count == 0 ? Outcome.Stalemate : w.OutcomeOf(loser);
        var reason = loser.Down;
        // aftermath: does the loser still die with nobody tending the wounds?
        int end = w.Tick + (int)(aftermathS * MeleeRules.TickHz);
        bool wasAlive = loser.Alive;
        while (w.Tick < end && loser.Alive && loser.State == Act.Down) w.StepWithAi();
        bool diedLater = wasAlive && !loser.Alive;
        var counts = new Dictionary<string, int>();
        foreach (var (_, wd) in w.WoundLog) counts[Categorise(wd)] = counts.GetValueOrDefault(Categorise(wd)) + 1;
        int Count(EventKind k) => w.Events.Count(e => e.Kind == k);
        return new DuelResult(1000 + duel, dur, winner, outcome, reason, diedLater,
            w.Fighters.Sum(f => f.BlowsThrown), w.Fighters.Sum(f => f.HitsLanded), Count(EventKind.Blocked), Count(EventKind.Parried), Count(EventKind.Dodged),
            Count(EventKind.Whiff), Count(EventKind.Feint), counts);
    }

    public static string Categorise(Wound w)
    {
        var f = w.Flags;
        if ((f & WoundFlags.Severed) != 0) return "severed limb";
        if ((f & WoundFlags.Vital) != 0) return "vital organ (heart/brain)";
        if ((f & WoundFlags.ArteryHit) != 0) return "artery cut";
        if ((f & WoundFlags.VeinHit) != 0) return "vein cut";
        if ((f & WoundFlags.OrganHit) != 0) return "organ";
        if ((f & (WoundFlags.NerveHit | WoundFlags.TendonHit)) != 0) return "nerve/tendon";
        if ((f & WoundFlags.BoneBroken) != 0) return "fracture";
        if (w.Kind == WoundKind.Crush) return "crush/bruise";
        return w.TissueId == "muscle" ? "muscle cut" : "skin/fat cut";
    }

    public sealed record BenchRow(string Matchup, int N, double MeanS, double MedianS, double P10S, double P90S, double WinsA,
        IReadOnlyDictionary<Outcome, int> Outcomes, int DiedLater, double Landed, double Thrown, double Blocked, double Parried, double Dodged,
        IReadOnlyDictionary<string, double> WoundsPerDuel);

    public static BenchRow Bench(CombatTables tables, Matchup m, int duels)
    {
        var rs = new List<DuelResult>();
        for (int i = 0; i < duels; i++) rs.Add(RunDuel(tables, m, i));
        var d = rs.Select(r => r.DurationS).OrderBy(x => x).ToList();
        var oc = new Dictionary<Outcome, int>();
        foreach (var r in rs) oc[r.LoserOutcome] = oc.GetValueOrDefault(r.LoserOutcome) + 1;
        var wc = new Dictionary<string, double>();
        foreach (var r in rs) foreach (var (k, v) in r.Wounds) wc[k] = wc.GetValueOrDefault(k) + v / (double)duels;
        return new BenchRow(m.Name, duels, d.Average(), d[d.Count / 2], d[duels / 10], d[duels * 9 / 10], rs.Count(r => r.WinnerTeam == 0) / (double)duels,
            oc, rs.Count(r => r.LoserDiedLater), rs.Average(r => r.Landed), rs.Average(r => r.BlowsThrown), rs.Average(r => r.Blocked),
            rs.Average(r => r.Parried), rs.Average(r => r.Dodged), wc);
    }

    public static string ToMarkdown(IEnumerable<BenchRow> rows)
    {
        var sb = new StringBuilder();
        var all = rows.ToList();
        sb.AppendLine("| Matchup | n | mean s | median s | p10-p90 s | A wins | death | knockout | rout | surrender | stalemate | KO'd who then bled out (60 s) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in all)
        {
            string P(Outcome o) => $"{100.0 * r.Outcomes.GetValueOrDefault(o) / r.N:0}%";
            sb.AppendLine($"| {r.Matchup} | {r.N} | {r.MeanS:0.0} | {r.MedianS:0.0} | {r.P10S:0.0}-{r.P90S:0.0} | {100 * r.WinsA:0}% | {P(Outcome.Death)} | {P(Outcome.Knockout)} | {P(Outcome.Rout)} | {P(Outcome.Surrender)} | {P(Outcome.Stalemate)} | {r.DiedLater} |");
        }
        sb.AppendLine();
        sb.AppendLine("| Matchup | blows thrown / duel | landed (wounding) | blocked | parried | dodged |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in all) sb.AppendLine($"| {r.Matchup} | {r.Thrown:0.0} | {r.Landed:0.0} | {r.Blocked:0.0} | {r.Parried:0.0} | {r.Dodged:0.0} |");
        sb.AppendLine();
        var cats = all.SelectMany(r => r.WoundsPerDuel.Keys).Distinct().OrderBy(x => x).ToList();
        sb.AppendLine("| Wound records per duel | " + string.Join(" | ", all.Select(r => r.Matchup)) + " |");
        sb.AppendLine("|---|" + string.Concat(all.Select(_ => "---|")));
        foreach (var c in cats) sb.AppendLine($"| {c} | " + string.Join(" | ", all.Select(r => $"{r.WoundsPerDuel.GetValueOrDefault(c):0.00}")) + " |");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- skirmish

    /// <summary>Two lines of <paramref name="perSide"/> fighters facing each other ~14 m apart.</summary>
    public static MeleeWorld Skirmish(CombatTables tables, ulong seed, int perSide = 10, bool playerInTeam0 = false, bool emitText = true)
    {
        var w = new MeleeWorld(tables, seed) { EmitText = emitText };
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
            {
                double x = team == 0 ? -7 : 7, y = (i - (perSide - 1) / 2.0) * 1.7;
                var face = new Vec2(team == 0 ? 1 : -1, 0);
                int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(team * 100 + i), 7, 0) % 35);
                FighterSpec s;
                if (team == 0) // spear-and-shield line: padded, half with heaters
                    s = i % 3 == 0
                        ? Spec($"Wolf {i + 1}", 0, Spear, Padded, new(x - 0.6, y), face, skill, 0, "person.spearman_red")
                        : Spec($"Wolf {i + 1}", 0, Sword, i % 3 == 1 ? Mailed : Padded, new(x, y), face, skill, MeleeRules.HeaterCover, "person.soldier_blue");
                else
                    s = i % 4 == 0
                        ? Spec($"Crow {i + 1}", 1, Axe, Mailed, new(x, y), face, skill, 0, "person.coif_axeman")
                        : i % 4 == 1 ? Spec($"Crow {i + 1}", 1, Maul, Padded, new(x, y), face, skill, 0, "person.soldier_red")
                        : Spec($"Crow {i + 1}", 1, Sword, Padded, new(x, y), face, skill, i % 2 == 0 ? MeleeRules.HeaterCover : 0, "person.soldier_green");
                w.Add(s, isPlayer: playerInTeam0 && team == 0 && i == perSide / 2);
            }
        return w;
    }
}
