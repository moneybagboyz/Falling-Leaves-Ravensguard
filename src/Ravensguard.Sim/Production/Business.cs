using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Production;

/// <summary>A business kind resolved against the recipes and extraction kinds (production spec 2.2).</summary>
public sealed class BusinessKind
{
    public ushort Index;
    public string Id = "";
    public Occupation OwnerOccupation;
    public LotUse? Lot;
    public (string Kind, int Count)[] Stations = [];
    public (Occupation Occupation, int Count, WageClass Wage)[] Slots = [];
    public Recipe[] Recipes = [];
    public ExtractionKind[] Extractions = [];
    public bool Trader;
    /// <summary>Staff are paid by the day they are on the books (manor, stall) not by the batch.</summary>
    public bool Salaried;
    /// <summary>P5-F: founded only by the player (never generated or grown); its recipes' worker-hours in permille (a water sawmill: 125).</summary>
    public bool FoundedOnly;
    public int LabourPm = 1000;
    /// <summary>P7-LB1: the worker-hours in permille while the premises' machines are powered (0: no machine changes it); a saw mill's gang saw: 125.</summary>
    public int PoweredLabourPm;
    /// <summary>P7-IN1: the stage kind a business of this kind is a stage of (a works' stage, run by <c>IndustryWorld</c>, never by Phase 2's planner); null for every ordinary kind.</summary>
    public Industry.StageKind? Stage;
    public int StaffCapacity => Slots.Sum(s => s.Count);
    /// <summary>The recipes a station of this kind works, in recipe order (built once per recipe list and shared by the region workers: the planner asks for every station every day).</summary>
    public Recipe[] RecipesFor(string station)
    {
        var snap = Volatile.Read(ref _byStation);
        if (snap == null || !ReferenceEquals(snap.Recipes, Recipes))
        {
            var rs = Recipes; var map = new Dictionary<string, Recipe[]>(StringComparer.Ordinal);
            foreach (var r in rs) if (!map.ContainsKey(r.Station)) map[r.Station] = [.. rs.Where(x => x.Station == r.Station)];
            Volatile.Write(ref _byStation, snap = new StationRecipes(rs, map));
        }
        return snap.Map.TryGetValue(station, out var a) ? a : [];
    }
    private sealed record StationRecipes(Recipe[] Recipes, Dictionary<string, Recipe[]> Map);
    private StationRecipes? _byStation;
}

/// <summary>Everything the production systems read from content, resolved once (recipes, business kinds, extraction kinds, resource constants).</summary>
public sealed class ProductionContent
{
    public required RecipeBook Recipes { get; init; }
    public required BusinessKind[] Kinds { get; init; }
    public required ExtractionKind[] Extractions { get; init; }
    public required ResourceSettingsDef Settings { get; init; }
    /// <summary>How households spend above their reserve (integration decision I37), by role id; empty when the content has none.</summary>
    public IReadOnlyList<SpendingRuleDef> Spending { get; init; } = [];
    /// <summary>Tax, toll and treasury constants (taxes spec 2.2).</summary>
    public TaxPolicyDef Tax { get; init; } = new TaxPolicyDef { Id = "default" };
    /// <summary>The tunables of background growth (cottages, assarting, new businesses).</summary>
    public GrowthTuningDef Growth { get; init; } = new GrowthTuningDef { Id = "growth" };
    /// <summary>P5-F: owned businesses, stewards, day labour and stalls.</summary>
    public OwnershipRulesDef Ownership { get; init; } = new OwnershipRulesDef { Id = "default" };
    /// <summary>P7-ES1: division of labour, crowding, span and the guild caps' numbers (economies-of-scale 3).</summary>
    public ScaleSettingsDef Scale { get; init; } = new ScaleSettingsDef { Id = "default" };
    /// <summary>P7-IN1: the staged chains and stage kinds (industries 3.1); their business kinds are the last entries of <see cref="Kinds"/>.</summary>
    public Industry.IndustryContent Industries { get; init; } = Industry.IndustryContent.Empty;
    /// <summary>P7-ES1: a station kind's crew limit k (5.1).</summary>
    public int CrewMax(string station)
    {
        foreach (var r in Scale.CrewMax) if (r.Station == station) return Math.Max(1, r.Max);
        return Math.Max(1, Scale.CrewMaxDefault);
    }

    public BusinessKind Kind(string id) => Kinds.First(k => k.Id == id);

    public static ProductionContent Load(ContentDatabase db, GoodCatalog cat)
    {
        var book = RecipeBook.Build(db, cat);
        var ext = Extraction.Load(db, cat);
        var kinds = new List<BusinessKind>();
        BusinessKind Make(BusinessDef d)
        {
            var k = new BusinessKind { Index = (ushort)kinds.Count, Id = d.Id, Trader = d.Trader, Salaried = d.Salaried || d.Trader, OwnerOccupation = Enum.Parse<Occupation>(d.OwnerOccupation), FoundedOnly = d.FoundedOnly, LabourPm = Math.Clamp(d.LabourPm, 1, 1000), PoweredLabourPm = Math.Clamp(d.PoweredLabourPm, 0, 1000) };
            if (d.LotUse.Length > 0) k.Lot = Enum.Parse<LotUse>(d.LotUse);
            foreach (var s in d.Stations) if (Array.IndexOf(RecipeBook.StationKinds, s.Kind) < 0) throw new InvalidDataException($"business '{d.Id}': unknown station kind '{s.Kind}'");
            k.Stations = [.. d.Stations.Select(s => (s.Kind, s.Count))];
            k.Slots = [.. d.StaffSlots.Select(s => (Enum.Parse<Occupation>(s.Occupation), s.Count, Enum.Parse<WageClass>(s.WageClass, true)))];
            k.Recipes = [.. d.Recipes.Select(id => book.TryGet(id, out var r) ? r : throw new InvalidDataException($"business '{d.Id}': unknown recipe '{id}'"))];
            foreach (var r in k.Recipes) if (!k.Stations.Any(s => s.Kind == r.Station)) throw new InvalidDataException($"business '{d.Id}': recipe '{r.Id}' needs a '{r.Station}' station it does not have");
            k.Extractions = [.. d.Extractions.Select(id => ext.FirstOrDefault(e => e.Id == id) ?? throw new InvalidDataException($"business '{d.Id}': unknown extraction '{id}'"))];
            return k;
        }
        foreach (var d in db.All<BusinessDef>().Where(b => !b.Late).OrderBy(b => b.Id, StringComparer.Ordinal)) kinds.Add(Make(d));
        // P7-LB2: the large buildings' kinds come right after the ordinary ones (by id), so no ordinary index moves; the stage kinds follow them
        foreach (var d in db.All<BusinessDef>().Where(b => b.Late).OrderBy(b => b.Id, StringComparer.Ordinal)) kinds.Add(Make(d));
        var industry = Industry.IndustryContent.Load(db, cat);
        foreach (var sk in industry.Stages)
        {
            // P7-IN1: a stage kind is a business kind of its own (appended after every ordinary kind, so none of their indices move); staff come through the works' crew target, never the kind's slots
            var bk = new BusinessKind { Index = (ushort)kinds.Count, Id = sk.Id, OwnerOccupation = Occupation.Lord, FoundedOnly = true, Stage = sk, Slots = [(sk.Primary, sk.Tiers[^1].CrewMax, sk.Wage)] };
            sk.Business = bk; kinds.Add(bk);
        }
        var cfg = db.All<ResourceSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ResourceSettingsDef { Id = "resources" };
        var scale = db.All<ScaleSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ScaleSettingsDef { Id = "default" };
        foreach (var r in book.Recipes)
        {
            r.Grades = GradesFor(scale, r);
            r.Pattern = Patterns.Of(scale, r.Id);
            if (r.Pattern != null && r.WorkerHours != Patterns.FullHours(r.Pattern)) throw new InvalidDataException($"scale_settings: recipe '{r.Id}' has {r.WorkerHours} worker-hours but its pattern's full run is {Patterns.FullHours(r.Pattern)}");
        }
        foreach (var row in scale.Patterns) if (!book.TryGet(row.Recipe, out _)) throw new InvalidDataException($"scale_settings: pattern names unknown recipe '{row.Recipe}'");
        return new ProductionContent { Recipes = book, Kinds = [.. kinds], Extractions = ext, Settings = cfg, Spending = [.. db.All<SpendingRuleDef>().OrderBy(x => x.Id, StringComparer.Ordinal)],
            Tax = db.All<TaxPolicyDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new TaxPolicyDef { Id = "default" },
            Growth = db.All<GrowthTuningDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new GrowthTuningDef { Id = "growth" }, Industries = industry,
            Ownership = db.All<OwnershipRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new OwnershipRulesDef { Id = "default" }, Scale = scale };
    }

    /// <summary>The grades of a recipe (economies-of-scale 3): a row naming its id wins, then one naming its skill; none means every step is a hand's. Rows that do not sum to 1,000 are a content error.</summary>
    public static Grades GradesFor(ScaleSettingsDef s, Recipe r)
    {
        ScaleGradeRow? row = s.Grades.FirstOrDefault(g => g.Recipe.Length > 0 && g.Recipe == r.Id) ?? s.Grades.FirstOrDefault(g => g.Recipe.Length == 0 && g.Skill.Length > 0 && g.Skill == r.Skill.ToString());
        if (row == null) return Grades.Plain;
        if (row.GradesPm.Count != 3 || row.GradesPm.Sum() != 1000 || row.GradesPm.Any(x => x < 0)) throw new InvalidDataException($"scale_settings: grades_pm for '{(row.Recipe.Length > 0 ? row.Recipe : row.Skill)}' must be three non-negative numbers summing 1000");
        return new Grades(row.GradesPm[0], row.GradesPm[1], row.GradesPm[2]);
    }
}

/// <summary>One workstation with its batch in progress (production spec 2.1). Progress is in micro-ppm: 1e12 is a finished batch.</summary>
public sealed class Station
{
    public const long Complete = 1_000_000_000_000;
    public string Kind = "";
    public int Recipe = -1;
    public long ProgressMicro;
    public long BusyUntilHour;
    public int ScalePermille;
    public long StartedHour;
    public int MinSkill = 100;
    /// <summary>Workers credited in the current hour (set by assignment, not saved).</summary>
    public List<int> Crew { get; } = [];
    public bool Busy => Recipe >= 0;

    /// <summary>
    /// Progress one worker adds in one hour, in micro-ppm: <c>ceil(1e18 / (worker_hours x scale x time_mult))</c> with worker-hours in thousandths already folded
    /// into <paramref name="hoursTimesScale"/> (<c>worker_hours x scale permille</c>) and <paramref name="timeMultPermille"/> = <c>1300 - 6 x skill</c>.
    /// The ceiling means a batch of <c>n</c> worker-hours is done after exactly <c>n</c> worker-hours (production spec 3.1, G-B1).
    /// </summary>
    public static long PerWorkerHour(long hoursTimesScale, long timeMultPermille)
    {
        long den = hoursTimesScale * timeMultPermille;
        return (1_000_000_000_000_000_000L + den - 1) / den;
    }
    /// <summary>Progress in ppm (0..1,000,000).</summary>
    public int ProgressPpm => (int)(ProgressMicro / 1_000_000);
}

public enum BusinessState : byte { Open, Closed }

/// <summary>A tile an extraction crew works for one extraction kind.</summary>
public sealed class CrewJob { public ushort Kind; public int Row = -1; public int Workers; }

/// <summary>
/// A business (production spec 2.1): an owner, a workplace, workstations, a cash box (a Business purse), a store and staff. Persons stay in the people tables; the
/// business refers to them by index. Wages accrue per worker-hour and are paid from the cash box at the daily slice; whatever it cannot pay stays owed.
/// </summary>
public sealed class Business
{
    public int Index;
    public EntityId Id;
    public ushort Kind;
    public int Settlement, Lot;
    public int Owner, OwnerHousehold;
    public bool OwnerWorks = true;
    public PurseId Purse;
    public Store Store = null!;
    public MarketParty Party = null!;
    public List<Station> Stations { get; } = [];
    public List<int> Staff { get; } = [];
    public List<long> Owed { get; } = [];
    public List<byte> ShortDays { get; } = [];
    public List<CrewJob> Jobs { get; } = [];
    public BusinessState State;
    public byte LossSeasons;
    public int IdleDays;
    public int UnfilledDays;
    public short WageOfferMites;

    // season books (production spec 3.5)
    public long WagesSeason, InputsSeason, DividendsSeason, CashStart;
    /// <summary>Index into the production system's mines, -1 for none.</summary>
    public int Mine = -1;
    public long WageBillPerDay, InputSpendPerDay;
    public long Dividends, ToolPiecesWorn, Batches;
    /// <summary>Working capital the business was started with: dividends are paid only from what the cash box holds above it (integration decision I2).</summary>
    public long Capital;
    public int[] Quality = new int[5];
    public int LastQuality = -1;

    public long ArrearsMites { get { long s = 0; foreach (var o in Owed) s += o; return s; } }
    public bool IsWorkshop(BusinessKind k) => k.Stations.Length > 0;
}
