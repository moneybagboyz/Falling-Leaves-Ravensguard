using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Disease;

/// <summary>The illnesses of a tracked person (disease-and-plague 3). Append only. Wound fever is the Phase 4 infection and has no course here.</summary>
public enum IllnessKind : byte { Cough, Ague, Flux, LungFever, WoundFever, Plague }

/// <summary>What nursed a sick person that day (4.1): nothing, a household or party member at the bedside, or a physician's visit.</summary>
public enum IllnessCare : byte { None, Nursed, Physician }

/// <summary>
/// The numbers of disease as data (disease-and-plague 3): <see cref="Defaults"/> holds the spec's values and <see cref="From"/> reads <c>medicine/illness.jsonc</c> and <c>disease/plague.jsonc</c> over them,
/// so a world with no such files plays as the spec has it. <see cref="Current"/> is swapped whole (never edited); <see cref="Install"/> is called where a world takes its content.
/// </summary>
public sealed class DiseaseRules
{
    public required DiseaseSettingsDef S { get; init; }
    /// <summary>One row per <see cref="IllnessKind"/>, indexed by the kind (wound fever's is a blank).</summary>
    public required IllnessDef[] Rows { get; init; }

    private static volatile DiseaseRules _current = Defaults();
    public static DiseaseRules Current => _current;
    public static void Install(ContentDatabase? db) => _current = db == null ? Defaults() : From(db);

    /// <summary>The kind's id in the content (<c>lung_fever</c>).</summary>
    public static string IdOf(IllnessKind k) => k switch { IllnessKind.LungFever => "lung_fever", IllnessKind.WoundFever => "wound_fever", _ => k.ToString().ToLowerInvariant() };

    /// <summary>The text key of an illness's name (a chronicle parameter and the panel's line).</summary>
    public static string NameKey(IllnessKind k) => "ill_name_" + IdOf(k);

    public IllnessDef Row(IllnessKind k) => Rows[(int)k];

    public static DiseaseRules Defaults()
    {
        IllnessDef R(IllnessKind k, int course, int worst, int u, int n, int p, bool contagious = false, int stam = 0, int str = 0, int sleep = 0, int water = 100, bool bed = false, int noise = 0, int shorten = 1)
            => new() { Id = IdOf(k), CourseDays = course, WorstDay = worst, DeathUntreatedPpm = u, DeathNursedPpm = n, DeathPhysicianPpm = p, Contagious = contagious, StaminaRegenLossPct = stam, StrengthLoss = str,
                SleepExtraHours = sleep, WaterNeedPct = water, BedBound = bed, NoiseM = noise, CareShortensDays = shorten };
        return new DiseaseRules
        {
            S = new DiseaseSettingsDef { Id = "disease" },
            Rows =
            [
                R(IllnessKind.Cough, 5, 3, 0, 0, 0, contagious: true, stam: 20, noise: 4),
                R(IllnessKind.Ague, 7, 4, 5000, 2000, 1000, str: 10, sleep: 4),
                R(IllnessKind.Flux, 6, 3, 30000, 10000, 5000, contagious: true, stam: 30, water: 200),
                R(IllnessKind.LungFever, 10, 6, 100000, 50000, 30000, bed: true),
                R(IllnessKind.WoundFever, 0, 0, 0, 0, 0),
                R(IllnessKind.Plague, 10, 6, 600000, 550000, 500000, contagious: true, bed: true, shorten: 0),
            ],
        };
    }

    public static DiseaseRules From(ContentDatabase db)
    {
        var d = Defaults();
        var s = db.All<DiseaseSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? d.S;
        var rows = (IllnessDef[])d.Rows.Clone();
        foreach (var r in db.All<IllnessDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
            foreach (var k in Enum.GetValues<IllnessKind>())
                if (IdOf(k) == r.Id) rows[(int)k] = r;
        return new DiseaseRules { S = s, Rows = rows };
    }

    /// <summary>The death chance of one check, parts per million, for a kind and the care it had; the great plague's is scaled by the slider's mortality and capped at 90 percent.</summary>
    public int DeathPpm(IllnessKind k, IllnessCare care, PlagueSeverity severity = PlagueSeverity.Normal)
    {
        var r = Row(k);
        long ppm = care switch { IllnessCare.Physician => r.DeathPhysicianPpm, IllnessCare.Nursed => r.DeathNursedPpm, _ => r.DeathUntreatedPpm };
        if (k == IllnessKind.Plague) ppm = Math.Min(900_000, ppm * SeverityMultPct(severity) / 100);
        return (int)ppm;
    }

    /// <summary>The slider's case-fatality multiplier, percent (rare 50, normal 100, frequent 150).</summary>
    public static int SeverityMultPct(PlagueSeverity s) => s == PlagueSeverity.Rare ? 50 : s == PlagueSeverity.Frequent ? 150 : 100;
}
