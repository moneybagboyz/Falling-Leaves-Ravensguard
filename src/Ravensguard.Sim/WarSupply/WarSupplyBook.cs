using System.Security.Cryptography;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WarSupply;

/// <summary>One host's supply account (army-economy 6: the supply accounts of the optional <c>warsupply</c> section). Plain numbers and owner ids only: the chest purse and baggage store are the economy's own records, found again by owner at load.</summary>
public sealed class HostAccount
{
    public int Id, Realm;
    public ulong Lord;
    /// <summary>The pay chest's purse (kind and owner), the baggage store's owner and the treasury the unspent coin goes back to (owner 0: none).</summary>
    public byte ChestKind; public ulong ChestOwner, BaggageOwner;
    public byte TreasuryKind; public ulong TreasuryOwner;
    public bool Purveyance, Plunder;
    public ForagePolicy Forage;
    public int[] Depots = [];
    public int BuyRadiusM = SupplyConfig.BuyRadiusM;
    public int[] Men = new int[Roster.Roles];
    public int Followers, Carts, Outside;
    public int Riding, Pack, Draught, Oxen;
    public HostMode Mode;
    public int PlaceSettlement = -1, PlaceXM, PlaceYM;
    public bool Pasture;
    public long[] Arrears = new long[Roster.Roles];
    public int[] HungerDays = new int[Roster.Roles], UnpaidDays = new int[Roster.Roles];
    /// <summary>Days the host has been fed and paid on (the levies' unpaid days are counted from the first), and the settlement the levies were drawn from (where the soldiers' coin goes home).</summary>
    public int ServiceDays, HomeSettlement = -1;
    /// <summary>Cumulative grams delivered by need and source (pay: mites), then cumulative short by need.</summary>
    public long[] Totals = new long[DayResult.Needs * DayResult.Sources + DayResult.Needs];
    /// <summary>The campaign's chronicle entry (0: none) and whether the host is in the abstract tier (outside every detailed region: it is fed and paid by <see cref="HostSupply"/>'s abstract rule, with the same counters).</summary>
    public ulong Campaign;
    public bool Abstract;
    /// <summary>P7-AM-D: men the host feeds and warms who are not in its roster (a joined warband's: the lord pays them), and the share (permille) the foragers' yield is cut by (the strip limit, set by the military from the days on the same ground).</summary>
    public int Retainers, ForageCutPm;
    /// <summary>P8-WS-2 (P7R-9): the purses that put coin in the pay chest, in the order they did (purse kind, owner, mites): what is left at <c>Disband</c> goes back to them, the newest first, each up to what it put in.</summary>
    public List<(byte Kind, ulong Owner, long Mites)> Funders = [];

    public AnimalCounts Animals => new(Riding, Pack, Draught, Oxen);
    public int Total { get { int t = 0; foreach (int c in Men) t += c; return t; } }
    public Roster Roster => new(Men);
}

/// <summary>Where a purveyance tally stands (army-economy 4.3).</summary>
public enum TallyState : byte { Owed, Paid, Defaulted }

/// <summary>
/// A tally for grain taken by purveyance (army-economy 4.3): the realm owes the household <see cref="Mites"/> (the grain at three quarters of the book's mid), due <see cref="DueDay"/>; paid by the realm's treasury at S5 when it holds more than
/// a season of its payroll (<c>TransferReason.Purveyance</c>), defaulted four seasons past due. <see cref="Entry"/> is the chronicle entry of the taking.
/// </summary>
public sealed class PurveyanceTally
{
    public int Id, Realm, Settlement, Host, TakenDay, DueDay, PaidDay;
    public ulong Household, Entry;
    public long Mites, PaidMites, Grams;
    public TallyState State;
    public long Owing => Math.Max(0, Mites - PaidMites);
}

/// <summary>A merchant that follows a host (army-economy 4.7): the merchant party's id (found again among the books' merchants) and the day it joined.</summary>
public sealed record SutlerRec(int Host, ulong Merchant, int JoinedDay);

/// <summary>Scutage received by an overlord and not yet written to the chronicle: <c>scutage_paid</c> is one entry a season per overlord (army-economy 4.4).</summary>
public sealed record ScutageNote(byte OverlordKind, long Overlord, int Payers, long Total);

/// <summary>A convoy on the road (army-economy 4.1 step 3): the caravan of the host's carts sent to a depot beyond a day's reach. The caravan itself is the continent's record; this row says whose it is, where it goes and where its load and its float go home to.</summary>
public sealed record ConvoyRec(ulong Caravan, int Host, int Depot, int Carts, ulong BaggageOwner, byte ChestKind, ulong ChestOwner, long FloatMites, int SentDay);

/// <summary>
/// The saved state of the army economy's interface (army-economy 6), in the optional <c>warsupply</c> section: the hosts' accounts, the persons on campaign (by host, so no key is added to the people
/// section) and the settlements under blockade. Empty (so absent from a save) while no host has ever existed.
/// </summary>
public sealed class WarSupplyBook
{
    public int NextHost = 1;
    public SortedDictionary<int, HostAccount> Hosts { get; } = [];
    /// <summary>Person table row to the host he serves in (a levy at the host's muster).</summary>
    public SortedDictionary<int, int> OnCampaign { get; } = [];
    /// <summary>Settlement index to the host besieging it.</summary>
    public SortedDictionary<int, int> Blockades { get; } = [];
    /// <summary>Settlement index to the men the muster of the latest year drew from it (P7-EA3d): the harvest's chronicle names the village and the men it sent. A new year's draw replaces an older year's.</summary>
    public SortedDictionary<int, (int Year, int Men)> Levied { get; } = [];
    /// <summary>Convoys on the road, by caravan id.</summary>
    public SortedDictionary<ulong, ConvoyRec> Convoys { get; } = [];
    /// <summary>Blockaded settlements whose <c>siege_starving</c> is written, and depots whose <c>depot_emptied</c> is written (once each, until the blockade lifts or the depot is refilled).</summary>
    public SortedSet<int> Starving { get; } = [];
    public SortedSet<int> Emptied { get; } = [];
    /// <summary>P7-AE2: the purveyance tallies by id, the sutlers following hosts, the scutage not yet noted, the stats the soak reads, and the next tally id.</summary>
    public SortedDictionary<int, PurveyanceTally> Tallies { get; } = [];
    public List<SutlerRec> Sutlers { get; } = [];
    public List<ScutageNote> ScutageNotes { get; } = [];
    public int NextTally = 1;
    public const int StatCount = 16;
    /// <summary>Running totals: 0 grams purveyed, 1 mites tallied, 2 mites of tallies paid, 3 mites defaulted, 4 households taken from, 5 hidden grams, 6 grams foraged, 7 grams trampled, 8 acres stripped, 9 households robbed, 10 grams sold by sutlers, 11 mites paid to sutlers, 12 mites of scutage, 13 men's service called, 14 pay chests taken (mites), 15 loans raised (mites).</summary>
    public long[] Stats = new long[StatCount];
    /// <summary>P7-AE3: the arms industry's state (orders, refit queue, the arms trade); saved as key 12 while not empty.</summary>
    public ArmsBook Arms { get; set; } = new();
    public void AddLevied(int settlement, int year, int men) => Levied[settlement] = Levied.TryGetValue(settlement, out var l) && l.Year == year ? (year, l.Men + men) : (year, men);
    public bool IsEmpty => NextHost == 1 && Hosts.Count == 0 && OnCampaign.Count == 0 && Blockades.Count == 0 && Levied.Count == 0 && Convoys.Count == 0 && Starving.Count == 0 && Emptied.Count == 0
        && Tallies.Count == 0 && Sutlers.Count == 0 && ScutageNotes.Count == 0 && NextTally == 1 && Stats.All(x => x == 0) && Arms.IsEmpty;

    /// <summary>The <c>SupplyHash</c> of the overview's trail: every account, campaign flag and blockade in id order.</summary>
    public string Hash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(NextHost);
        foreach (var (id, a) in Hosts)
        {
            L(id); L(a.Realm); L((long)a.Lord); L(a.ChestKind); L((long)a.ChestOwner); L((long)a.BaggageOwner); L(a.TreasuryKind); L((long)a.TreasuryOwner);
            L(a.Purveyance ? 1 : 0); L(a.Plunder ? 1 : 0); L((long)a.Forage); foreach (int d in a.Depots) L(d); L(a.BuyRadiusM);
            foreach (int m in a.Men) L(m); L(a.Followers); L(a.Carts); L(a.Outside); L(a.Riding); L(a.Pack); L(a.Draught); L(a.Oxen);
            L((long)a.Mode); L(a.PlaceSettlement); L(a.PlaceXM); L(a.PlaceYM); L(a.Pasture ? 1 : 0);
            foreach (long x in a.Arrears) L(x); foreach (int x in a.HungerDays) L(x); foreach (int x in a.UnpaidDays) L(x); L(a.ServiceDays); L(a.HomeSettlement);
            foreach (long x in a.Totals) L(x);
            if (a.Campaign != 0 || a.Abstract) { L((long)a.Campaign); L(a.Abstract ? 1 : 0); }
            if (a.Retainers != 0 || a.ForageCutPm != 0) { L(-7); L(a.Retainers); L(a.ForageCutPm); }
            if (a.Funders.Count > 0) { L(-9); foreach (var f in a.Funders) { L(f.Kind); L((long)f.Owner); L(f.Mites); } }
        }
        foreach (var (p, hid) in OnCampaign) { L(p); L(hid); }
        foreach (var (s, hid) in Blockades) { L(s); L(hid); }
        foreach (var (s, l) in Levied) { L(s); L(l.Year); L(l.Men); }
        foreach (var (id, c) in Convoys) { L((long)id); L(c.Host); L(c.Depot); L(c.Carts); L((long)c.BaggageOwner); L(c.ChestKind); L((long)c.ChestOwner); L(c.FloatMites); L(c.SentDay); }
        foreach (int s in Starving) L(-1 - s);
        foreach (int s in Emptied) L(-1_000_000 - s);
        if (Tallies.Count > 0 || Sutlers.Count > 0 || ScutageNotes.Count > 0 || NextTally != 1 || Stats.Any(x => x != 0))
        {
            L(NextTally);
            foreach (var (id, t) in Tallies) { L(id); L(t.Realm); L(t.Settlement); L(t.Host); L(t.TakenDay); L(t.DueDay); L(t.PaidDay); L((long)t.Household); L((long)t.Entry); L(t.Mites); L(t.PaidMites); L(t.Grams); L((long)t.State); }
            foreach (var s in Sutlers) { L(s.Host); L((long)s.Merchant); L(s.JoinedDay); }
            foreach (var n in ScutageNotes) { L(n.OverlordKind); L(n.Overlord); L(n.Payers); L(n.Total); }
            foreach (long x in Stats) L(x);
        }
        if (!Arms.IsEmpty) Arms.Fold(h);
        return Convert.ToHexString(h.GetHashAndReset())[..16];
    }
}
