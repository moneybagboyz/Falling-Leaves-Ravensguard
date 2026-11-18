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
    public int StaffCapacity => Slots.Sum(s => s.Count);
    public IEnumerable<Recipe> RecipesFor(string station) { foreach (var r in Recipes) if (r.Station == station) yield return r; }
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

    public BusinessKind Kind(string id) => Kinds.First(k => k.Id == id);

    public static ProductionContent Load(ContentDatabase db, GoodCatalog cat)
    {
        var book = RecipeBook.Build(db, cat);
        var ext = Extraction.Load(db, cat);
        var kinds = new List<BusinessKind>();
        foreach (var d in db.All<BusinessDef>().OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var k = new BusinessKind { Index = (ushort)kinds.Count, Id = d.Id, Trader = d.Trader, Salaried = d.Salaried || d.Trader, OwnerOccupation = Enum.Parse<Occupation>(d.OwnerOccupation) };
            if (d.LotUse.Length > 0) k.Lot = Enum.Parse<LotUse>(d.LotUse);
            foreach (var s in d.Stations) if (Array.IndexOf(RecipeBook.StationKinds, s.Kind) < 0) throw new InvalidDataException($"business '{d.Id}': unknown station kind '{s.Kind}'");
            k.Stations = [.. d.Stations.Select(s => (s.Kind, s.Count))];
            k.Slots = [.. d.StaffSlots.Select(s => (Enum.Parse<Occupation>(s.Occupation), s.Count, Enum.Parse<WageClass>(s.WageClass, true)))];
            k.Recipes = [.. d.Recipes.Select(id => book.TryGet(id, out var r) ? r : throw new InvalidDataException($"business '{d.Id}': unknown recipe '{id}'"))];
            foreach (var r in k.Recipes) if (!k.Stations.Any(s => s.Kind == r.Station)) throw new InvalidDataException($"business '{d.Id}': recipe '{r.Id}' needs a '{r.Station}' station it does not have");
            k.Extractions = [.. d.Extractions.Select(id => ext.FirstOrDefault(e => e.Id == id) ?? throw new InvalidDataException($"business '{d.Id}': unknown extraction '{id}'"))];
            kinds.Add(k);
        }
        var cfg = db.All<ResourceSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ResourceSettingsDef { Id = "resources" };
        return new ProductionContent { Recipes = book, Kinds = [.. kinds], Extractions = ext, Settings = cfg, Spending = [.. db.All<SpendingRuleDef>().OrderBy(x => x.Id, StringComparer.Ordinal)] };
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
