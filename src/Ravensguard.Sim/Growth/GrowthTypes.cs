using System.Security.Cryptography;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Growth;

/// <summary>What a growth project builds: a cottage (or burgage house) on a plot or a vacant lot, a clearing that becomes a parcel of fields, or the premises of a new business.</summary>
public enum ProjectKind : byte { Cottage = 0, Assart = 1, Premises = 2 }

/// <summary>
/// One piece of work in progress (docs/specs/phase3/background-growth.md section 3): who pays, where, how many worker-hours it takes and how many are done, and how much of
/// each material has been bought. Saved with the growth section, so a save made in the middle of a build resumes it exactly.
/// </summary>
public sealed class BuildProject
{
    public int Serial;
    public ProjectKind Kind;
    public int Settlement;
    public int StartDay;
    /// <summary>The household that pays and that gets the use of the result (-1: the fief treasury pays; <see cref="PayerFief"/> then names the fief).</summary>
    public int Payer = -1;
    public int PayerFief = -1;
    public long HoursNeeded, HoursDone;
    /// <summary>Coin paid so far to hired hands (mites) and seasons gone by without a single hour of progress.</summary>
    public long PaidMites;
    public int StalledSeasons;

    // a cottage: the plot (a new lot) or the vacant lot it refits; the use, blueprint and look of the house
    public int Lot = -1;
    public LotUse Use;
    public DwellingType Dwelling;
    public string Blueprint = "";
    public RectM Rect;
    public byte Facing, Wealth;
    public int Beds;
    /// <summary>Grams of each material line (the tuning's list order) bought and built in so far.</summary>
    public long[] MaterialG = [];

    // an assart: the tiles to clear (region tile indices, ascending) and the grams of wood they carried when the clearing was planned
    public int[] Tiles = [];

    // premises of a business: the kind (index into the content's business kinds) and the person who will own it
    public int BusinessKind = -1;
    public int Owner = -1;
}

/// <summary>A lot grown into the plan: a house on a new plot (<see cref="NewLot"/>) or a vacant house taken up again. Replayed over the generated region on load.</summary>
public sealed record LotRecord(int Settlement, int Lot, bool NewLot, LotUse Use, DwellingType Dwelling, string Blueprint, RectM Rect, byte Facing, byte Wealth, int Beds, int Day);

/// <summary>A parcel of cleared fields appended to a settlement (tiles ascending, scaled fertility per tile). Replayed over the generated region on load.</summary>
public sealed record ParcelRecord(int Settlement, ParcelOwner Owner, byte Slot, int[] Tiles, byte[] Fertility, int Day);

/// <summary>What growth did in one game year, for the soak table.</summary>
public sealed class GrowthYear
{
    public int Year;
    public int CottagesStarted, CottagesBuilt, VacantRefitted, AcresCleared, ClearingsStarted, BusinessesOpened, PlotsGiven;
    public long WagesMites, MaterialsMites;
}

/// <summary>
/// The saved state of background growth for one region: projects in progress, the log of what has been built (the replay over the generated region on load), the
/// seasons each unmet demand has lasted and the yearly counters.
/// </summary>
public sealed class GrowthState
{
    public int NextSerial = 1;
    public List<BuildProject> Projects { get; } = [];
    public List<LotRecord> Lots { get; } = [];
    public List<ParcelRecord> Parcels { get; } = [];
    /// <summary>Consecutive seasons a (settlement, business kind) good has been wanted and unsupplied: key <c>settlement * 4096 + kind</c>, ascending.</summary>
    public SortedDictionary<long, int> Unmet { get; } = [];
    public List<GrowthYear> Years { get; } = [];
    /// <summary>Region tiles cleared of wood (assarting), ascending: the resource layer is a function of the generated region, so these rows are marked again after a load.</summary>
    public List<int> ClearedTiles { get; } = [];
    /// <summary>The fertility scale (permille of the tile's fertility) new fields get: that of the generated fields, fixed at the first clearing.</summary>
    public int FertilityScalePermille;
    public long WoodDrawnG, FirewoodMadeG, LogsMadeG, PolesGatheredG;

    public int BuiltDwellings => Lots.Count;

    public GrowthYear Year(int year)
    {
        for (int i = Years.Count - 1; i >= 0; i--) if (Years[i].Year == year) return Years[i];
        var y = new GrowthYear { Year = year }; Years.Add(y); return y;
    }

    public bool IsEmpty => Projects.Count == 0 && Lots.Count == 0 && Parcels.Count == 0 && Unmet.Count == 0 && Years.Count == 0;

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(System.Text.Encoding.UTF8.GetBytes(s)); }
        L(NextSerial); L(FertilityScalePermille); L(WoodDrawnG); L(FirewoodMadeG); L(LogsMadeG); L(PolesGatheredG);
        L(Projects.Count);
        foreach (var p in Projects)
        {
            L(p.Serial); L((long)p.Kind); L(p.Settlement); L(p.StartDay); L(p.Payer); L(p.PayerFief); L(p.HoursNeeded); L(p.HoursDone); L(p.PaidMites); L(p.StalledSeasons); L(p.Lot);
            L((long)p.Use); L((long)p.Dwelling); S(p.Blueprint); L(p.Rect.X); L(p.Rect.Y); L(p.Rect.W); L(p.Rect.H); L(p.Facing); L(p.Wealth); L(p.Beds);
            foreach (long m in p.MaterialG) L(m);
            foreach (int t in p.Tiles) L(t);
            L(p.BusinessKind); L(p.Owner);
        }
        foreach (var r in Lots) { L(r.Settlement); L(r.Lot); L(r.NewLot ? 1 : 0); L((long)r.Use); L((long)r.Dwelling); S(r.Blueprint); L(r.Rect.X); L(r.Rect.Y); L(r.Rect.W); L(r.Rect.H); L(r.Facing); L(r.Wealth); L(r.Beds); L(r.Day); }
        foreach (var r in Parcels) { L(r.Settlement); L((long)r.Owner.Kind); L(r.Owner.FiefIndex); L(r.Owner.LotIndex); L(r.Slot); L(r.Day); foreach (int t in r.Tiles) L(t); foreach (byte f in r.Fertility) L(f); }
        foreach (var (k, v) in Unmet) { L(k); L(v); }
        foreach (var y in Years) { L(y.Year); L(y.CottagesStarted); L(y.CottagesBuilt); L(y.VacantRefitted); L(y.AcresCleared); L(y.ClearingsStarted); L(y.BusinessesOpened); L(y.PlotsGiven); L(y.WagesMites); L(y.MaterialsMites); }
        foreach (int t in ClearedTiles) L(t);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
