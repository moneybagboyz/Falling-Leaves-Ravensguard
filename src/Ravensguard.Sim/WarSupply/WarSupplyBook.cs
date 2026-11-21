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

    public AnimalCounts Animals => new(Riding, Pack, Draught, Oxen);
    public int Total { get { int t = 0; foreach (int c in Men) t += c; return t; } }
    public Roster Roster => new(Men);
}

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
    public void AddLevied(int settlement, int year, int men) => Levied[settlement] = Levied.TryGetValue(settlement, out var l) && l.Year == year ? (year, l.Men + men) : (year, men);
    public bool IsEmpty => NextHost == 1 && Hosts.Count == 0 && OnCampaign.Count == 0 && Blockades.Count == 0 && Levied.Count == 0;

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
        }
        foreach (var (p, hid) in OnCampaign) { L(p); L(hid); }
        foreach (var (s, hid) in Blockades) { L(s); L(hid); }
        foreach (var (s, l) in Levied) { L(s); L(l.Year); L(l.Men); }
        return Convert.ToHexString(h.GetHashAndReset())[..16];
    }
}
