using System.Security.Cryptography;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Production;

/// <summary>A holder's share of a business, permille (businesses spec 3.1). The owner's own share is what the others leave (1,000 less theirs); he keeps control above 500.</summary>
public readonly record struct ShareRow(EntityId Holder, int Pm);

/// <summary>Alerts of a holding (spec 3.1, 6): shown in the Ledger and, once per kind per day, as a toast.</summary>
[Flags]
public enum OwnerAlert : ushort
{
    None = 0, WagesOwed = 1, NoInput = 2, LossSeason = 4, NoStaff = 8, StewardReport = 16, Unlicensed = 32, Exhausted = 64, Flooded = 128, StallUnminded = 256, StewardNoProfit = 512,
    /// <summary>P6-C3: an official's collections fall a quarter or more short of what was expected (rule-and-income 4.8).</summary>
    Shortfall = 1024,
}

/// <summary>
/// The owner's or steward's standing orders (spec 4.4): maximise profit (default), keep a stock of a good, never sell a good below a price, the season's input budget, how often the profit is
/// sent and how much stays in the box, how many staff at most, and whether to close after two loss seasons. Each a plain field, edited in the Ledger.
/// </summary>
public sealed class Directives
{
    /// <summary>Keep at least this much (units) of <see cref="KeepGood"/> in the store (-1: none).</summary>
    public int KeepGood = -1;
    public long KeepQty;
    /// <summary>Never sell <see cref="FloorGood"/> below this many mites a unit (a kilogram or a piece; -1: none).</summary>
    public int FloorGood = -1;
    public long FloorMites;
    /// <summary>The most the business spends on inputs in a season, mites (0: no limit).</summary>
    public long SeasonBudget;
    public int SendEveryDays = 5;
    public long KeepMites = 1000;
    /// <summary>Staff at most (-1: the kind's slots).</summary>
    public int HireUpTo = -1;
    public bool CloseAfterTwoLosses;
    /// <summary>A recipe to make "N a day" (-1: by margin, the Phase 2 planner) and how many.</summary>
    public int Recipe = -1;
    public int PerDay;

    public Directives Clone() => (Directives)MemberwiseClone();
}

/// <summary>One closed season of a holding (the Ledger's season history, spec 6): revenue, costs (wages and inputs), profit, what was paid out and where.</summary>
public readonly record struct SeasonRow(int Season, long Revenue, long Wages, long Inputs, long Profit, long Paid);

/// <summary>
/// The hero's (or a shareholder's) side of one business (spec 3.1): the shares, the steward, the directives, its premises, hands-on or not, each staff member's wage, the season
/// books the Ledger shows (revenue and profit, four seasons back), the profit sent and where it went, and its alerts. The <see cref="Business"/> itself stays in the production system;
/// this holds only what Phase 5 adds, keyed by the business's id.
/// </summary>
public sealed class BusinessHolding
{
    public EntityId Business;
    /// <summary>The holders other than the owner (ascending holder id).</summary>
    public List<ShareRow> Shares { get; } = [];
    /// <summary>The steward's person index (-1: none) and his wage a day.</summary>
    public int Steward = -1;
    public int StewardWage;
    public long StewardOwed;
    public Directives Orders = new();
    /// <summary>The structures the business works in (construction's structure ids).</summary>
    public List<int> Premises { get; } = [];
    /// <summary>The owner works and decides himself; the planner only suggests (no steward).</summary>
    public bool HandsOn = true;
    /// <summary>Each staff member's agreed wage a day, mites (ascending person index; absent: the class wage).</summary>
    public SortedDictionary<int, int> Wages { get; } = [];
    public int FoundedDay = -1;
    /// <summary>The season's revenue so far (sales and what came in), mites.</summary>
    public long RevenueSeason;
    public long SentSeason;
    /// <summary>The last four closed seasons, oldest first.</summary>
    public List<SeasonRow> History { get; } = [];
    /// <summary>The day the profit was last sent, and where it went in all: the owner's purse, his home chest.</summary>
    public int LastSentDay = -1;
    public long SentToPurse, SentToChest;
    public OwnerAlert Alerts;
    /// <summary>Daily mid prices (micro-mites) of the goods the business deals in, newest last, at most 11 days (the steward's lag, spec 4.4): one row per day of the goods in <see cref="PriceGoods"/>.</summary>
    public List<long[]> PriceDays { get; } = [];
    public ushort[] PriceGoods = [];
    /// <summary>Extraction business: the region tile rows of the resource layer it stands on (ascending), the vein it mines (-1), the mineral licence serial (-1: none, unlicensed if needed).</summary>
    public int[] Tiles = [];
    public int Vein = -1;
    public int Licence = -1;
    public bool Unlicensed;
    /// <summary>Royalties paid to the right holder in all, mites, and what the box could not pay yet.</summary>
    public long RoyaltyPaid, RoyaltyOwed;
    public long LastMinedG;
    /// <summary>The steward's short days (three and he leaves) and what he was paid this season (a cost in the season row).</summary>
    public int StewardShortDays;
    public long StewardPaidSeason;
    /// <summary>Batches the owner's "make N a day" recipe started today (the day it counts).</summary>
    public int MadeToday, MadeDay = -1;
    /// <summary>The second the hero's own work at its stations has been credited to (a second never counts twice).</summary>
    public long HandsToSec;
    /// <summary>P5-H: the day the hero's line inherited it (-1 never): the Ledger marks the row "Inherited".</summary>
    public int InheritedDay = -1;

    public int OwnerPm => 1000 - Shares.Sum(s => s.Pm);
    public long MeanProfit => History.Count == 0 ? 0 : History.Skip(Math.Max(0, History.Count - 4)).Sum(h => h.Profit) / Math.Min(4, History.Count);
}

/// <summary>A market stall the hero minds or has minded (spec 4.5): the stall's store and purse are the business's when a business sells there, else the hero's own.</summary>
public sealed class StallHolding
{
    /// <summary>The stall's key (<c>LandIds.StallSubject</c>) and the settlement.</summary>
    public long Subject;
    public int Settlement;
    /// <summary>The business selling from it (-1: the hero sells his own goods from his own stall store).</summary>
    public int Business = -1;
    /// <summary>The stall's own goods (the hero's): a store of the economy, keyed by the stall's id.</summary>
    public EntityId StoreId;
    /// <summary>The owner's ask in permille of the book's ask (1,000 default).</summary>
    public int AskPm = 1000;
    /// <summary>Who minds it: the hero (by the hours he spends there), a staff member (person index), or nobody (-1).</summary>
    public int Minder = -1;
    /// <summary>Hours the hero has minded today (the desk's watermark: a second never counts twice), the day, and what sold today.</summary>
    public long MindedToSec;
    public int Day = -1;
    public long SoldToday, TakingsToday, TollToday;
    public long TakingsSeason, SoldSeason;
}

/// <summary>
/// The saved state of ownership for the home region (the optional "ownership" save section; absent while empty, so a world that never touched it keeps its bytes): the hero's holdings in
/// businesses, his stalls, the prospect book (knowledge and survey reports), mineral licences, where the hero last told the world he is (for where coin is sent), and the serial counter.
/// </summary>
public sealed class OwnershipBook
{
    public int NextSerial = 1;
    public SortedDictionary<EntityId, BusinessHolding> Holdings { get; } = [];
    public SortedDictionary<long, StallHolding> Stalls { get; } = [];
    public Ravensguard.Sim.Prospecting.ProspectBook Prospects { get; } = new();
    public List<Ravensguard.Sim.Prospecting.MiningLicence> Licences { get; } = [];
    /// <summary>The settlement the hero is in (-1 out of any place, -2 out of the region), as the street view last sent it.</summary>
    public int HeroAt = -1;
    /// <summary>Alert kinds already toasted today (cleared each day).</summary>
    public int ToastDay = -1;
    public OwnerAlert Toasted;

    public bool IsEmpty => NextSerial == 1 && Holdings.Count == 0 && Stalls.Count == 0 && Prospects.IsEmpty && Licences.Count == 0 && HeroAt == -1;

    public BusinessHolding? Of(EntityId business) => Holdings.TryGetValue(business, out var h) ? h : null;

    /// <summary>The "OwnershipHash" of the hash trail (overview 8.9).</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(NextSerial); L(HeroAt); L(ToastDay); L((long)Toasted); L(Holdings.Count);
        foreach (var x in Holdings.Values)
        {
            L((long)x.Business.Value); L(x.Steward); L(x.StewardWage); L(x.StewardOwed); L(x.HandsOn ? 1 : 0); L(x.FoundedDay); L(x.RevenueSeason); L(x.SentSeason); L(x.LastSentDay);
            L(x.SentToPurse); L(x.SentToChest); L((long)x.Alerts); L(x.Vein); L(x.Licence); L(x.Unlicensed ? 1 : 0); L(x.RoyaltyPaid); L(x.RoyaltyOwed); L(x.LastMinedG);
            L(x.StewardShortDays); L(x.StewardPaidSeason); L(x.MadeToday); L(x.MadeDay); L(x.HandsToSec);
            if (x.InheritedDay >= 0) L(x.InheritedDay);
            var o = x.Orders; L(o.KeepGood); L(o.KeepQty); L(o.FloorGood); L(o.FloorMites); L(o.SeasonBudget); L(o.SendEveryDays); L(o.KeepMites); L(o.HireUpTo); L(o.CloseAfterTwoLosses ? 1 : 0); L(o.Recipe); L(o.PerDay);
            L(x.Shares.Count); foreach (var s in x.Shares) { L((long)s.Holder.Value); L(s.Pm); }
            L(x.Premises.Count); foreach (int p in x.Premises) L(p);
            L(x.Wages.Count); foreach (var (p, w) in x.Wages) { L(p); L(w); }
            L(x.History.Count); foreach (var r in x.History) { L(r.Season); L(r.Revenue); L(r.Wages); L(r.Inputs); L(r.Profit); L(r.Paid); }
            L(x.PriceGoods.Length); foreach (var g in x.PriceGoods) L(g);
            L(x.PriceDays.Count); foreach (var d in x.PriceDays) foreach (long v in d) L(v);
            L(x.Tiles.Length); foreach (int t in x.Tiles) L(t);
        }
        L(Stalls.Count);
        foreach (var s in Stalls.Values)
        {
            L(s.Subject); L(s.Settlement); L(s.Business); L((long)s.StoreId.Value); L(s.AskPm); L(s.Minder); L(s.MindedToSec); L(s.Day); L(s.SoldToday); L(s.TakingsToday); L(s.TollToday); L(s.TakingsSeason); L(s.SoldSeason);
        }
        Prospects.Hash(L);
        L(Licences.Count);
        foreach (var l in Licences) { L(l.Serial); L((long)l.Holder.Value); L(l.Vein); L(l.Tile); L((long)l.Mineral); L((long)l.Grantor); L(l.GrantorIndex); L(l.FeeMites); L(l.RoyaltyPm); L(l.Day); L(l.Revoked ? 1 : 0); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}

/// <summary>
/// The one valuation (spec 3.2), shown everywhere a price is asked: <c>premises (land's building value + plot price) + stock at bid + station items and tools at bid + cash box + goodwill</c>,
/// <c>goodwill = max(0, mean profit of the last four seasons) x 2</c>. Pure integers.
/// </summary>
public static class Valuation
{
    public static long Goodwill(IReadOnlyList<long> profits, int seasons = 2)
    {
        if (profits.Count == 0) return 0;
        var last = profits.Skip(Math.Max(0, profits.Count - 4)).ToList();
        long mean = last.Sum() / last.Count;
        return Math.Max(0, mean) * seasons;
    }

    public static long Value(long premises, long stockBid, long toolsBid, long cash, IReadOnlyList<long> profits, int goodwillSeasons = 2)
        => premises + stockBid + toolsBid + cash + Goodwill(profits, goodwillSeasons);

    /// <summary>A share's price: <c>value x share / 1000</c> (floor).</summary>
    public static long SharePrice(long value, int pm) => value * pm / 1000;

    /// <summary>
    /// Splits a dividend among the holders pro rata (spec 4.1, 5: permille, the remainder to the largest holder, ties to the lower id): returns (holder, mites) in holder order, the owner
    /// first under <paramref name="owner"/>.
    /// </summary>
    public static List<(EntityId Holder, long Mites)> Split(long dividend, EntityId owner, IReadOnlyList<ShareRow> shares)
    {
        var rows = new List<(EntityId Holder, int Pm)> { (owner, 1000 - shares.Sum(s => s.Pm)) };
        foreach (var s in shares) rows.Add((s.Holder, s.Pm));
        var outp = rows.Select(r => (r.Holder, Mites: dividend * r.Pm / 1000)).ToList();
        long rest = dividend - outp.Sum(x => x.Mites);
        if (rest > 0)
        {
            int best = 0;
            for (int i = 1; i < rows.Count; i++) if (rows[i].Pm > rows[best].Pm || (rows[i].Pm == rows[best].Pm && rows[i].Holder.Value < rows[best].Holder.Value)) best = i;
            outp[best] = (outp[best].Holder, outp[best].Mites + rest);
        }
        return outp;
    }

    /// <summary>
    /// The season's projected profit of a workshop at the solver's prices (spec 4.6, BS-9): each batch earns 45% of its wage cost (Phase 2 3.6), a worker of skill <paramref name="skill"/> does
    /// <c>10 h / (worker_hours x (1300 - 6 x skill) / 1000)</c> batches a day; 15 days a season. Floor at the end only.
    /// </summary>
    public static long ProjectedSeasonProfit(int workers, int skill, int workerHours, int wagePerHour, int days = 15, int workdayHours = 10)
    {
        long marginPerBatch = (long)workerHours * wagePerHour * 450;              // milli-mites: 45% of the batch's wage cost
        long mult = 1300 - 6 * skill;                                             // permille time multiplier
        // batches = workers x days x 10 h / (worker_hours x mult / 1000)
        return marginPerBatch * workers * days * workdayHours * 1000 / ((long)workerHours * mult) / 1000;
    }
}
