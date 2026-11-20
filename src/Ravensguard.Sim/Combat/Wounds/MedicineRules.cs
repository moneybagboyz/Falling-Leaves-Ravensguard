using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>One treatment's row (medicine spec 3): time, skill floor, what it uses, the tool class and light.</summary>
public sealed record TreatRow(int SimS, int MinSkill, (string Item, int Count)[] Needs, string ToolQuality, int ToolLevel, bool Light);

/// <summary>The healing table of wounds 3.4 (days x 10), the Phase 4 values unless content says otherwise.</summary>
public sealed record HealingRow(
    int ShallowTreated, int Shallow, int DeepTreated, int Deep, int DeepFromPm, int[] Muscle, int[] Ligament, int Treated, int Untreated,
    int[] BoneTreated, int[] Bone, int BoneOther, int Tendon, int OrganRepaired, int Severed, int[] Malunion);

/// <summary>
/// The tables of <see cref="MedicineTables"/> and <see cref="Treatment"/> as data (medicine 3): <see cref="Defaults"/> holds the Phase 4 constants and <see cref="From"/> reads
/// <c>content/base/medicine/*.jsonc</c> over them, so a world with no medicine files plays as Phase 4 did (MT-1). <see cref="Current"/> is swapped whole (never edited), so the
/// tick workers read one consistent set; <see cref="Install"/> is called where a world takes its content.
/// </summary>
public sealed class MedicineRules
{
    public required TreatRow[] Treat { get; init; }
    public required HealingRow Healing { get; init; }
    /// <summary>Pain points per game hour and the hours, by dose id (ale, poultice, poppy_milk).</summary>
    public required IReadOnlyDictionary<string, (int PerHour, int Hours)> Relief { get; init; }
    public required IReadOnlyDictionary<string, MedicalAidDef> Aids { get; init; }
    /// <summary>Fee in mites by service id, and whether a barber-surgeon offers it.</summary>
    public required IReadOnlyDictionary<string, (int Mites, bool Barber)> Prices { get; init; }
    public required MedicineRulesDef Rules { get; init; }

    private static volatile MedicineRules _current = Defaults();
    public static MedicineRules Current => _current;
    public static void Install(ContentDatabase? db) => _current = db == null ? Defaults() : From(db);

    /// <summary>The snake-case id of a <see cref="TreatKind"/> in the content (<c>set_bone</c>).</summary>
    public static string IdOf(TreatKind k)
    {
        var n = k.ToString(); var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n.Length; i++) { if (i > 0 && char.IsUpper(n[i])) sb.Append('_'); sb.Append(char.ToLowerInvariant(n[i])); }
        return sb.ToString();
    }

    public static MedicineRules Defaults()
    {
        var t = new TreatRow[13];
        int[] secs = [2, 4, 6, 12, 20, 30, 20, 10, 90, 90, 180, 60, 6];
        for (int i = 0; i < t.Length; i++) t[i] = new TreatRow(secs[i], 0, [], "", 0, false);
        t[(int)TreatKind.Bandage] = t[(int)TreatKind.Bandage] with { Needs = [("bandage", 1)] };
        t[(int)TreatKind.Tourniquet] = t[(int)TreatKind.Tourniquet] with { Needs = [("rope", 1), ("bandage", 2)] };
        t[(int)TreatKind.Splint] = t[(int)TreatKind.Splint] with { Needs = [("splint", 1)] };
        foreach (var (k, skill) in new[] { (TreatKind.Stitch, 10), (TreatKind.TendonRepair, 50), (TreatKind.LigamentRepair, 60), (TreatKind.OrganRepair, 60) })
            t[(int)k] = t[(int)k] with { MinSkill = skill, Needs = [("needle_and_thread", 1)], ToolQuality = "sewing", ToolLevel = 1, Light = true };
        t[(int)TreatKind.Amputate] = t[(int)TreatKind.Amputate] with { MinSkill = 30 };
        var h = new HealingRow(60, 100, 80, 140, 500, [30, 150, 400], [120, 300], 10, 15, [100, 200, 280, 400], [150, 350, 450, 600], 30, 400, 300, 100, [0, 50, 80, 100]);
        var relief = new Dictionary<string, (int, int)> { ["ale"] = (8, 4), ["poultice"] = (15, 4), ["poppy_milk"] = (40, 4) };
        var aids = new Dictionary<string, MedicalAidDef>
        {
            ["crutch"] = new() { Id = "crutch", Hands = 1, OneLegPm = 550 }, ["splint"] = new() { Id = "splint", Hands = 0 },
            ["litter"] = new() { Id = "litter", Hands = 0, Carriers = 2, CarrySpeedPm = 600 },
        };
        var prices = new Dictionary<string, (int, bool)>
        {
            ["visit"] = (50, true), ["clean"] = (100, true), ["stitch"] = (300, true), ["set_bone"] = (400, true), ["splint"] = (150, true), ["tourniquet"] = (50, true),
            ["tendon_repair"] = (1200, false), ["ligament_repair"] = (1500, false), ["organ_repair"] = (2000, false), ["amputate"] = (800, true), ["poppy_dose"] = (600, false), ["bed_day"] = (60, false),
        };
        return new MedicineRules { Treat = t, Healing = h, Relief = relief, Aids = aids, Prices = prices, Rules = new MedicineRulesDef { Id = "default" } };
    }

    /// <summary>The Phase 4 tables with every row the content gives laid over them; a missing file leaves its table as it was.</summary>
    public static MedicineRules From(ContentDatabase db)
    {
        var d = Defaults();
        var t = (TreatRow[])d.Treat.Clone();
        foreach (var def in db.All<TreatmentDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            int k = -1;
            for (int i = 0; i < t.Length; i++) if (IdOf((TreatKind)i) == def.Id) { k = i; break; }
            if (k < 0) throw new FormatException($"treatment '{def.Id}': not a treatment kind");
            t[k] = new TreatRow(def.SimS, def.MinSkill, [.. def.Needs.Select(n => (n.Item, n.Count))], def.ToolQuality, def.ToolLevel, def.Light);
        }
        var h = d.Healing;
        var hd = db.All<HealingTableDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (hd != null)
            h = new HealingRow(hd.ShallowTreatedX10, hd.ShallowX10, hd.DeepTreatedX10, hd.DeepX10, hd.DeepFromPm, [.. hd.MuscleX10], [.. hd.LigamentX10], hd.TreatedX10, hd.UntreatedX10,
                [.. hd.BoneTreatedX10], [.. hd.BoneX10], hd.BoneOtherX10, hd.TendonX10, hd.OrganRepairedX10, hd.SeveredX10, [.. hd.MalunionPct]);
        var relief = new Dictionary<string, (int PerHour, int Hours)>(d.Relief);
        foreach (var r in db.All<PainReliefDef>()) relief[r.Id] = (r.PointsPerHour, r.Hours);
        var aids = new Dictionary<string, MedicalAidDef>(d.Aids);
        foreach (var a in db.All<MedicalAidDef>()) aids[a.Id] = a;
        var prices = new Dictionary<string, (int Mites, bool Barber)>(d.Prices);
        foreach (var p in db.All<MedicinePriceDef>()) prices[p.Id] = (p.Mites, p.Barber);
        var rules = db.All<MedicineRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? d.Rules;
        return new MedicineRules { Treat = t, Healing = h, Relief = relief, Aids = aids, Prices = prices, Rules = rules };
    }
}
