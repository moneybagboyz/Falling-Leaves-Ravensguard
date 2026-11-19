using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// The duel harness behind <c>tools/Ravensguard.Calibrate</c> (resolver-v2 8): many seeded AI-vs-AI duels per weapon and armour pairing, run
/// headless through the Phase 0 spike's real-time exchange loop (<see cref="MeleeWorld"/>: wind-up, block, parry, dodge, bleeding, pain,
/// morale) with every landed blow resolved by <see cref="ResolverV2"/> (or v1, for comparison). It reports what the resolver decides (duel
/// length, severs, wound mix, mail conversions, plate soak, the spear-vs-sword win rate) and the defence rate, which is the melee layer's
/// (P4-C). Statistics only: this is a tool, not sim state, so its averages are plain doubles.
/// </summary>
public static class Calibration
{
    public sealed record Kit(string Name, WornItem[] Worn);

    public static readonly Kit Unarmoured = new("unarmoured", [new("tunic", "linen")]);
    public static readonly Kit Padded = new("padded", [new("tunic", "linen"), new("gambeson", "linen")]);
    public static readonly Kit Mailed = new("mail", [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")]);
    public static readonly Kit Plated = new("plate", [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("breastplate", "steel"),
        new("helm", "steel"), new("vambraces", "steel"), new("greaves", "steel")]);

    public sealed record Pairing(string Name, WeaponInstance WeaponA, Kit KitA, WeaponInstance WeaponB, Kit KitB);

    /// <summary>The spike's three benchmark matchups, sword vs sword bare, then each weapon against a swordsman in each kit.</summary>
    public static IReadOnlyList<Pairing> DefaultPairings()
    {
        var sword = new WeaponInstance("arming_sword", "steel");
        var list = new List<Pairing>
        {
            new("sword vs sword (padded)", sword, Padded, sword, Padded),
            new("sword vs sword (unarmoured)", sword, Unarmoured, sword, Unarmoured),
            new("spear vs sword (unarmoured)", new("spear", "steel"), Padded, sword, Unarmoured),
            new("axe vs sword (mail)", new("axe", "steel"), Padded, sword, Mailed),
        };
        foreach (var (wn, w) in new[] { ("sword", sword), ("axe", new WeaponInstance("axe", "steel")), ("mace", new WeaponInstance("mace", "steel")), ("spear", new WeaponInstance("spear", "steel")) })
            foreach (var k in new[] { Unarmoured, Padded, Mailed, Plated })
                list.Add(new($"{wn} (padded) vs sword ({k.Name})", w, Padded, sword, k));
        return list;
    }

    public sealed class Row
    {
        public required string Name { get; init; }
        public int N, WinsA, Fatal, KoBledOut, KnockedOut;
        public double TotalS;
        public readonly List<double> Durations = [];
        public long Thrown, Landed, Blocked, Parried, Dodged, Severs, BlowsOnMailEdged, MailConverted, PlateStopped, PlateStoppedIn, PlateStoppedOut;
        public readonly Dictionary<string, long> Wounds = [];
        public double MeanS => N == 0 ? 0 : TotalS / N;
        public double MedianS { get { var d = Durations.OrderBy(x => x).ToList(); return d.Count == 0 ? 0 : d[d.Count / 2]; } }
        public double DefendedPct => Thrown == 0 ? 0 : 100.0 * (Blocked + Parried + Dodged) / Thrown;
        public double SeversPerDuel => N == 0 ? 0 : (double)Severs / N;
        public double WinAPct => N == 0 ? 0 : 100.0 * WinsA / N;
    }

    /// <summary>Wound categories for the mix (v2 grades, not the spike's flags).</summary>
    public static string Category(BodyPlanV2 plan, IReadOnlyList<MaterialV2> mats, WoundV2 w)
    {
        var f = w.Flags;
        if ((f & WoundFlags.Severed) != 0) return "severed";
        if ((f & WoundFlags.Vital) != 0) return "vital organ";
        if ((f & WoundFlags.OrganHit) != 0) return "organ";
        if ((f & WoundFlags.ArteryHit) != 0) return "artery";
        if ((f & WoundFlags.VeinHit) != 0) return "vein";
        if ((f & (WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit | WoundFlags.NerveHit | WoundFlags.TendonHit | WoundFlags.SpineHit)) != 0) return "nerve/tendon/cord";
        if ((f & WoundFlags.LigamentHit) != 0) return "ligament";
        string t = mats[w.Tissue].Id;
        if (t.StartsWith("bone", StringComparison.Ordinal)) return w.Bone >= BoneGrade.Cracked ? "fracture" : "bone bruise";
        if (w.Structure < 0 && plan.Parts[w.Part].Layers.Any(l => l.Tissue.Index == w.Tissue && l.IsMuscle))
            return w.Muscle >= MuscleGrade.Torn ? (w.Kind == WoundKind.Cut ? "muscle cut" : "muscle crushed") : "muscle bruise";
        return w.Kind == WoundKind.Cut ? "skin/fat cut" : "skin/fat bruise";
    }

    /// <summary>Runs <paramref name="duels"/> seeded duels of a pairing. Skills 30-59 per seed (the spike's), steel weapons.</summary>
    public static Row Run(CombatTables tables, Pairing p, int duels, bool v2 = true, bool treat = false)
    {
        var row = new Row { Name = p.Name };
        var adapter = new V1Adapter(tables);
        var mats = tables.V2.MaterialList;
        var m = new MeleeScenarios.Matchup(p.Name,
            i => new FighterSpec("A", 0, p.WeaponA, p.KitA.Worn, new(-3, 0), MeleeScenarios.SkillFor(i, 0)) { FaceDir = new(1, 0) },
            i => new FighterSpec("B", 1, p.WeaponB, p.KitB.Worn, new(3, 0), MeleeScenarios.SkillFor(i, 1)) { FaceDir = new(-1, 0) });
        adapter.OnBlow = (r, s) =>
        {
            if (!r.Hit) return;
            foreach (var w in r.WoundSpan) { var c = Category(s.Plan, mats, w); row.Wounds[c] = row.Wounds.GetValueOrDefault(c) + 1; }
            if (r.Severed) row.Severs++;
            foreach (var st in r.StepSpan)
            {
                if (!st.IsArmour || st.Piece < 0 || st.Outcome is LayerOutcome.NotCovered or LayerOutcome.Bypassed) continue;
                var a = s.Worn![st.Piece].Stats;
                bool edge = s.Weapon.Attacks[s.Attack].Type != AttackType.Blunt;
                if (a.Elastic && edge && st.NeedMilli > 0)
                {
                    row.BlowsOnMailEdged++;
                    if (st.Outcome is LayerOutcome.Crushed or LayerOutcome.Stopped) row.MailConverted++;
                }
                if (!a.Elastic && a.Material.Category == "metal" && st.Outcome == LayerOutcome.Stopped)
                { row.PlateStopped++; row.PlateStoppedIn += st.InMilli; row.PlateStoppedOut += st.OutMilli; }
                if (a.Elastic && edge) break;    // count the outermost piece's conversion only
            }
        };
        for (int i = 0; i < duels; i++)
        {
            var d = MeleeScenarios.RunDuel(tables, m, i, setup: v2 ? w => w.Strike = adapter.Resolve : null, aftermath: treat ? Medic() : null);
            row.N++; row.TotalS += d.DurationS; row.Durations.Add(d.DurationS);
            if (d.WinnerTeam == 0) row.WinsA++;
            row.Thrown += d.BlowsThrown; row.Landed += d.Landed; row.Blocked += d.Blocked; row.Parried += d.Parried; row.Dodged += d.Dodged;
            if (d.LoserOutcome == Outcome.Death || d.LoserDiedLater) row.Fatal++;
            if (d.LoserOutcome == Outcome.Knockout) row.KnockedOut++;
            if (d.LoserDiedLater) row.KoBledOut++;
            if (!v2) foreach (var (k, v) in d.Wounds) row.Wounds[k] = row.Wounds.GetValueOrDefault(k) + v;
            if (!v2 && d.Wounds.TryGetValue("severed limb", out var sv)) row.Severs += sv;
        }
        return row;
    }

    private static readonly string[] InternalIds = ["heart", "brain", "lung", "liver", "spleen", "kidney", "stomach", "intestine", "trachea"];
    private static bool IsInternal(ActiveWound w) => w.StructureId != null && InternalIds.Any(i => w.StructureId.StartsWith(i, StringComparison.Ordinal));
    private static string? Group(string part) => part.Contains("arm") || part.Contains("hand") ? "arm" + part[^2..] : part.Contains("leg") || part.Contains("foot") ? "leg" + part[^2..] : null;

    /// <summary>
    /// The aftermath helper of the treatment run (wounds 4.3, WB-15): the winner walks over (3 sim-s), then dresses the loser's worst undressed external wound with the Phase 4 rules:
    /// a tourniquet (6 s, -98%) on a spurting limb, else a bandage (4 s, -80%, and the dressed wound clots), else a pressure hold; internal wounds cannot be dressed. Kit: one rope, four bandages.
    /// The same constants as <see cref="Ravensguard.Sim.Combat.Wounds.Treatment"/>; the spike's world keeps its own bleeding model, so this applies them to its wounds.
    /// </summary>
    public static Action<MeleeWorld, Fighter, int> Medic()
    {
        int ready = 3 * MeleeRules.TickHz, doneAt = -1, rope = 1, bandages = 4;
        ActiveWound? target = null; bool tourniquet = false, bandage = false;
        var dressed = new HashSet<ActiveWound>();
        return (w, loser, t) =>
        {
            if (!w.Fighters.Any(f => f.Active)) return;
            if (target != null && t >= doneAt)
            {
                if (tourniquet) { string? g = Group(target.PartId); foreach (var x in loser.Wounds) if (!IsInternal(x) && Group(x.PartId) == g) { x.Rate *= 0.02; dressed.Add(x); } }
                else if (bandage) { foreach (var x in loser.Wounds) if (!IsInternal(x) && x.PartId == target.PartId && !dressed.Contains(x)) { x.Rate *= 0.2; x.Clots = true; dressed.Add(x); } }
                else { target.Rate *= 0.2; target.Clots = true; dressed.Add(target); }
                target = null; ready = t;
            }
            if (target != null || t < ready) return;
            ActiveWound? best = null;
            foreach (var x in loser.Wounds) if (!IsInternal(x) && !dressed.Contains(x) && x.Rate > 0.2 && (best == null || x.Rate > best.Rate)) best = x;
            if (best == null) return;
            target = best; tourniquet = best.Rate >= 10 && Group(best.PartId) != null && (rope > 0 || bandages >= 2); bandage = !tourniquet && bandages > 0;
            if (tourniquet) { if (rope > 0) rope--; else bandages -= 2; } else if (bandage) bandages--;
            doneAt = t + MeleeRules.TickHz * (tourniquet ? 6 : bandage ? 4 : 2);
        };
    }

    /// <summary>The summary table, with the resolver-v2 8 targets.</summary>
    public static string Table(IReadOnlyList<Row> rows, IReadOnlyList<Row>? treated = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Pairing | n | mean s | median s | A wins | defended | severs/duel | fatal | KO'd then bled out (untreated) |" + (treated != null ? " KO'd then bled out (with a medic) |" : "") + " mail turned cuts | plate soak |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|" + (treated != null ? "---|" : ""));
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var r = rows[ri];
            string mail = r.BlowsOnMailEdged == 0 ? "-" : $"{100.0 * r.MailConverted / r.BlowsOnMailEdged:0}%";
            string plate = r.PlateStopped == 0 ? "-" : $"{100.0 - 100.0 * r.PlateStoppedOut / Math.Max(1, r.PlateStoppedIn):0.0}%";
            string ko = r.KnockedOut == 0 ? "-" : $"{100.0 * r.KoBledOut / r.KnockedOut:0}%";
            sb.AppendLine($"| {r.Name} | {r.N} | {r.MeanS:0.0} | {r.MedianS:0.0} | {r.WinAPct:0}% | {r.DefendedPct:0}% | {r.SeversPerDuel:0.00} | {100.0 * r.Fatal / Math.Max(1, r.N):0}% | {ko} | " + (treated != null ? $"{(treated[ri].KnockedOut == 0 ? "-" : $"{100.0 * treated[ri].KoBledOut / treated[ri].KnockedOut:0}%")} | " : "") + $"{mail} | {plate} |");
        }
        sb.AppendLine();
        var cats = rows.SelectMany(r => r.Wounds.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        sb.AppendLine("| Wounds per duel | " + string.Join(" | ", cats) + " |");
        sb.AppendLine("|---|" + string.Concat(cats.Select(_ => "---|")));
        foreach (var r in rows) sb.AppendLine($"| {r.Name} | " + string.Join(" | ", cats.Select(c => $"{(double)r.Wounds.GetValueOrDefault(c) / Math.Max(1, r.N):0.00}")) + " |");
        sb.AppendLine();
        sb.AppendLine("Targets (resolver-v2 8): duels 10-20 s; defended 40-60% (melee layer, P4-C); severs <= 0.12 per duel; sword vs sword A 50% +- 5;");
        sb.AppendLine("spear vs unarmoured sword >= 55%; fatal 15-35%; KO'd who bleed out <= 35% with treatment (P4-B); mail turns cuts; rigid plate soaks 98-99% of a stopped blow.");
        return sb.ToString();
    }

    /// <summary>
    /// The metal ranking (df-calibration.md): cut rate of the outer piece by a seeded spread of sword slashes against a leather jerkin, a bone breastplate, bronze mail and iron mail.
    /// The bone plate (DF's bone, SF 0.13) is the piece whose need falls inside every metal's spread of blows; with DF's leather (SF 0.025) copper and bronze both cut a jerkin every time.
    /// </summary>
    public static IReadOnlyList<(string Metal, int CutsPerMille)> MetalRanking(CombatTablesV2 t, int blows = 2000)
    {
        var res = new ResolverV2(t);
        var plan = t.Plan("human");
        var r = new BlowResult();
        var list = new List<(string, int)>();
        (string, string)[] armour = [("jerkin", "leather"), ("breastplate", "bone"), ("mail_hauberk", "bronze"), ("mail_hauberk", "iron")];
        foreach (var metal in new[] { "silver", "copper", "bronze", "iron", "steel" })
        {
            var w = t.Weapon("arming_sword", metal);
            int cuts = 0;
            foreach (var a in armour)
                for (int i = 0; i < blows; i++)
                {
                    var s = StrikeV2.Create(w, w.AttackIndex("slash"), plan, plan.IndexOf("upper_body"), (int)(DetRng.NextUInt(7, i, 0, 1, 0) % 101));
                    s.Style = (AttackStyle)(DetRng.NextUInt(7, i, 0, 1, 1) % 3);
                    s.Worn = [WornV2.Of(t.Armour(a.Item1, a.Item2))]; s.WornCount = 1;
                    res.ResolveImpact(s, new BlowKey(7, i, 1, 0), r);
                    if (r.StepCount > 0 && r.Steps[0].Outcome == LayerOutcome.Cut) cuts++;
                }
            list.Add((metal, (int)(1000L * cuts / (blows * armour.Length))));
        }
        return list;
    }
}
