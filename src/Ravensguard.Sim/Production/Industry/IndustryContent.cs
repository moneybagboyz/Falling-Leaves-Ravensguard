using Ravensguard.Content;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>What a stage's day does (industries 3.1): makes the output from the ground, turns the main good into the outputs, or passes goods on (hauling is handling with a distance).</summary>
public enum StageRole : byte { Extract, Process, Handle, Haul }

/// <summary>One tier of a stage kind (industries 3.1, 5.1), resolved: the crew band, the rate, the machine or station limit, the stage's own store. Kilograms are grams in the sim.</summary>
public sealed class StageTier
{
    public int Tier, CrewMin, CrewMax, SupportPm, PowerW, InputPm = 1000;
    /// <summary>Grams of the main good a worker-day at skill 50.</summary>
    public long RateG;
    /// <summary>Grams a day the stage's stations and machines limit it to; 0 when the crews alone do.</summary>
    public long CapacityG;
    public long StoreG;
    public bool AnyDistance;
    public string Plan = "";
    public string[] Recognise = [];
}

/// <summary>A stage kind (industries 3.1) resolved against the goods: the roles, the occupations, the flows and the three tiers.</summary>
public sealed class StageKind
{
    public ushort Index;
    public string Id = "", Chain = "", Name = "";
    public StageRole Role;
    public Occupation Primary, Support;
    public Skill Skill;
    /// <summary>True for the craft curve (<c>1,000,000 / (1,300 - 6 x skill)</c>); false for the extraction curve (<c>800 + 4 x skill</c>).</summary>
    public bool CraftCurve;
    public WageClass Wage;
    public ushort Main;
    public (ushort Good, int Pm, bool Fuel)[] Inputs = [];
    public (ushort Good, int Pm)[] Outputs = [];
    public int WastePm, YieldLoPm, YieldHiPm, RefDistanceM;
    public StageTier[] Tiers = [];
    /// <summary>The business kind (an entry of <see cref="ProductionContent.Kinds"/>) a stage of this kind runs as.</summary>
    public BusinessKind Business = null!;

    public int TierCount => Tiers.Length;
    public StageTier Tier(int tier) => Tiers[Math.Clamp(tier, 1, Tiers.Length) - 1];
    public bool IsHandling => Role is StageRole.Handle or StageRole.Haul;
    /// <summary>The ids of the goods the stage's own store may hold (the main good and every input and output).</summary>
    public IEnumerable<ushort> Goods() { yield return Main; foreach (var i in Inputs) yield return i.Good; foreach (var o in Outputs) yield return o.Good; }
}

/// <summary>A chain: the stage kinds upstream first (industries 3.1).</summary>
public sealed class ChainKind
{
    public string Id = "", Name = "";
    public StageKind[] Stages = [];
}

/// <summary>
/// The industry content, resolved once (P7-IN1): chains, stage kinds, the runtime's numbers. Loaded with the production content; the stage kinds also appear in <see cref="ProductionContent.Kinds"/> as business kinds (after every ordinary kind, so no
/// ordinary kind's index moves), which is what a stage's <see cref="Business"/> runs as.
/// </summary>
public sealed class IndustryContent
{
    public static readonly IndustryContent Empty = new();
    public ChainKind[] Chains { get; private init; } = [];
    public StageKind[] Stages { get; private init; } = [];
    public IndustrySettingsDef Settings { get; private init; } = new IndustrySettingsDef { Id = "default" };
    /// <summary>The generated mine's composite rate in grams of dressed ore a worker-day (5.12); 0 when the content names no composite.</summary>
    public long CompositeGPerWorkerDay { get; private init; }
    /// <summary>The good the composite rate is for (the generated mine's output); 65535 when none.</summary>
    public ushort CompositeGood { get; private init; } = ushort.MaxValue;
    public bool Any => Stages.Length > 0;

    public ChainKind? Chain(string id) => Chains.FirstOrDefault(c => c.Id == id);
    public StageKind? Stage(string id) => Stages.FirstOrDefault(s => s.Id == id);

    public static IndustryContent Load(ContentDatabase db, GoodCatalog cat)
    {
        var defs = db.All<StageKindDef>().OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (defs.Count == 0) return Empty;
        var settings = db.All<IndustrySettingsDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new IndustrySettingsDef { Id = "default" };
        var stages = new List<StageKind>();
        foreach (var d in defs)
        {
            if (!Enum.TryParse<StageRole>(d.Role, true, out var role)) throw new InvalidDataException($"stage kind '{d.Id}': unknown role '{d.Role}'");
            var prim = Enum.Parse<Occupation>(d.Occupation); var sup = Enum.Parse<Occupation>(d.SupportOccupation);
            var k = new StageKind
            {
                Index = (ushort)stages.Count, Id = d.Id, Chain = d.Chain, Name = d.Name, Role = role, Primary = prim, Support = sup,
                Skill = d.Skill.Length > 0 ? Enum.Parse<Skill>(d.Skill) : OccupationInfo.Skills(prim).A,
                CraftCurve = d.Curve == "craft", Wage = Enum.Parse<WageClass>(d.Wage, true), Main = cat.IdOf(d.Main),
                Inputs = [.. d.Inputs.Select(i => (cat.IdOf(i.Good), i.PerMainPm, i.Fuel))],
                Outputs = [.. d.Outputs.Select(o => (cat.IdOf(o.Good), o.PerMainPm))],
                WastePm = d.WastePm, YieldLoPm = d.YieldLoPm, YieldHiPm = d.YieldHiPm, RefDistanceM = d.RefDistanceM,
            };
            if (d.Tiers.Count == 0) throw new InvalidDataException($"stage kind '{d.Id}' has no tiers");
            k.Tiers = [.. d.Tiers.OrderBy(t => t.Tier).Select(t => new StageTier
            {
                Tier = t.Tier, CrewMin = t.CrewMin, CrewMax = t.CrewMax, RateG = t.Rate * 1000L, CapacityG = t.Capacity * 1000L, SupportPm = t.SupportPm, PowerW = t.PowerW,
                StoreG = t.StoreKg * 1000L, InputPm = t.InputPm, AnyDistance = t.AnyDistance, Plan = t.Plan, Recognise = [.. t.Recognise],
            })];
            for (int i = 0; i < k.Tiers.Length; i++)
                if (k.Tiers[i].Tier != i + 1 || k.Tiers[i].CrewMin > k.Tiers[i].CrewMax) throw new InvalidDataException($"stage kind '{d.Id}': tier {i + 1} is out of order or its crew band is empty");
            stages.Add(k);
        }
        var chains = new List<ChainKind>();
        foreach (var c in db.All<ChainDef>().OrderBy(d => d.Id, StringComparer.Ordinal))
            chains.Add(new ChainKind { Id = c.Id, Name = c.Name, Stages = [.. c.Stages.Select(id => stages.FirstOrDefault(s => s.Id == id) ?? throw new InvalidDataException($"chain '{c.Id}': unknown stage kind '{id}'"))] });
        var (comp, good) = Composite(settings, stages, cat);
        return new IndustryContent { Chains = [.. chains], Stages = [.. stages], Settings = settings, CompositeGPerWorkerDay = comp, CompositeGood = good };
    }

    /// <summary>
    /// The composite rate (industries 5.12): grams of the dressed ore one worker makes a day when the first-tier stages of the generated kind's chain are all worked by the same hands: for 1,000 kg of rough ore, the hewers' days (and the carriers'
    /// the first stage's <c>support_pm</c> asks for) and the dressing days, against the dressed share. The default content gives 600 kg for 2 + 1 + 2.5 days: 109 kg.
    /// </summary>
    private static (long, ushort) Composite(IndustrySettingsDef s, List<StageKind> stages, GoodCatalog cat)
    {
        if (s.CompositeStages.Count == 0 || !cat.TryIdOf(s.CompositeGood, out var good)) return (0, ushort.MaxValue);
        long amountG = 1_000_000, daysMilli = 0;
        foreach (var id in s.CompositeStages)
        {
            var st = stages.FirstOrDefault(x => x.Id == id); if (st == null) return (0, ushort.MaxValue);
            var t = st.Tier(1);
            daysMilli += amountG * 1000 / t.RateG * (1000 + t.SupportPm) / 1000;
        }
        long outG = amountG * s.CompositeYieldPm / 1000;
        return daysMilli <= 0 ? (0, ushort.MaxValue) : (outG * 1000 / daysMilli, good);
    }
}
