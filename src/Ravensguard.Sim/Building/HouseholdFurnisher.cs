using Ravensguard.Content;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Building;

/// <summary>
/// The household furnisher (P6-B6, holdings-and-works-v2 3.6): once a season (H6 S6) a household that lives in a house it owns and finds no hearth, or fewer beds than it has members, puts them in
/// within the season. A straw pallet is 0.5 worker-hours and 15 kg of straw, a hearth a central open hearth (stone and clay); the household uses what its store holds and buys the rest at the goods'
/// reference price from the town (coin to the lord's or town's purse, the goods imported into the store and built in at once, counted by construction's own mass books as growth's pieces are).
/// The pieces are placed where the floor is free of an Indoor room, in (level, y, x) order, as a finished site of the household's own. A household that cannot pay waits for the next season.
/// The player's household furnishes his house himself (build mode). Bedsteads by commission and rooms of the household's own trade are left to the people.
/// </summary>
public sealed class HouseholdFurnisher
{
    public const string PalletPiece = "straw_pallet", HearthPiece = "hearth_central";
    /// <summary>At most this many pallets are laid in a house in a season.</summary>
    public const int PalletsPerSeason = 4;

    private readonly ConstructionWorld _cw;
    private readonly Population _pop;
    private readonly EconomyState _econ;
    private readonly HoldingsBook _book;
    /// <summary>The player's household (-1 none): never furnished by this step.</summary>
    public Func<int> HeroHousehold { get; set; } = () => -1;

    /// <summary>Counters for the tests and the ledger: pieces laid, coin spent (mites) and households that could not pay this season.</summary>
    public int Laid, Waited; public long SpentMites;

    public HouseholdFurnisher(ConstructionWorld cw, Population pop, EconomyState econ, HoldingsBook book) { _cw = cw; _pop = pop; _econ = econ; _book = book; }

    /// <summary>The household of the people a finished site was built for (its owner or payer: a household's id, or a person's household), or -1.</summary>
    public int HouseholdOf(ConstructionSite s)
    {
        foreach (var (id, kind) in new[] { (s.Owner, s.OwnerKind), (s.Payer, s.PayerKind) })
        {
            if (kind == PurseKind.Household && _pop.Households.IndexOf(id) is int h && h >= 0) return h;
            if (kind == PurseKind.Person && _pop.People.IndexOf(id) is int p && p >= 0 && _pop.People.Household[p] is int hh && hh >= 0) return hh;
        }
        return -1;
    }

    /// <summary>What a house lacks for a household of <paramref name="members"/>: hearths (0 or 1) and beds.</summary>
    public static (int Hearths, int Beds) Lacks(BuildingFacts f, int members) => (f.HearthRooms == 0 && !f.Rooms.Any(r => r.Indoor && r.Tags.GetValueOrDefault("hearth") > 0) ? 1 : 0, Math.Max(0, members - f.Beds));

    /// <summary>One season's furnishing; returns the pieces laid.</summary>
    public int Season(long sec, int day)
    {
        int before = Laid;
        int hero = HeroHousehold();
        foreach (var site in _cw.Book.Sites.Values.Where(s => s.State == SiteState.Finished).ToList())
        {
            int hh = HouseholdOf(site);
            if (hh < 0 || hh == hero || _pop.Households.MemberCount[hh] == 0) continue;
            int st = StructureOf(site); if (st <= 0) continue;
            var facts = _cw.Rooms.Facts(st); if (facts == null || facts.Rooms.All(r => !r.Indoor)) continue;
            var (hearths, beds) = Lacks(facts, _pop.Households.MemberCount[hh]);
            if (hearths == 0 && beds == 0) continue;
            var cells = new List<PlaceCell>();
            var free = FreeCells(facts, st).ToList();
            if (hearths > 0 && free.Count > 0) { var c = free[free.Count / 2]; cells.Add(new PlaceCell(c.X, c.Y, c.Z, HearthPiece)); free.Remove(c); }
            foreach (var c in free.Take(Math.Min(beds, PalletsPerSeason))) cells.Add(new PlaceCell(c.X, c.Y, c.Z, PalletPiece));
            if (cells.Count == 0) continue;
            if (!Furnish(site, hh, cells, sec)) Waited++;
        }
        return Laid - before;
    }

    private int StructureOf(ConstructionSite s)
    {
        foreach (var b in _cw.Book.Built.Values) if (b.Site == s.Id) return _cw.Rooms.StructureAt(b.X, b.Y, b.Z);
        return 0;
    }

    /// <summary>The free floor cells of a house's Indoor rooms in (level, y, x) order: a built floor, nothing on it, no door or stair.</summary>
    private IEnumerable<(int X, int Y, int Z)> FreeCells(BuildingFacts f, int structure)
    {
        var all = new List<(int X, int Y, int Z)>();
        foreach (var r in f.Rooms.Where(r => r.Indoor))
            foreach (var (x, y) in r.Cells)
                if (_cw.BuiltAt(x, y, r.Z, GhostSlot.Fixture) == null && _cw.GhostAt(x, y, r.Z) == null && _cw.BuiltStructure(x, y, r.Z) is { Kind: PieceKind.Floor }) all.Add((x, y, r.Z));
        all.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        return all;
    }

    private bool Furnish(ConstructionSite site, int hh, List<PlaceCell> cells, long sec)
    {
        var hht = _pop.Households; var id = hht.IdOf(hh);
        var store = _econ.HouseholdStore[hh]; var purse = _econ.HouseholdPurse[hh];
        int s = hht.Settlement[hh];
        var (payee, payeeKind) = s >= 0 ? LandRegistry.TownOf(_pop.Region, s) : (EntityId.None, PurseKind.Settlement);
        // the bill: what the store lacks, at the reference price
        var need = new Dictionary<ushort, long>();
        foreach (var c in cells)
            foreach (var (good, grams) in _cw.MaterialIds(_cw.Costs.Get(c.Piece)!)) if (good != ushort.MaxValue) need[good] = need.GetValueOrDefault(good) + grams;
        long bill = 0;
        foreach (var (g, q) in need) { long lack = Math.Max(0, q - store.Qty(g)); bill += (long)(_econ.Goods.Def(g).BasePriceMitesPerKg * lack / 1000.0 + 0.999); }
        if (bill > 0 && (payee == EntityId.None || _econ.Ledger.Balance(purse).Mites < bill)) return false;
        var pr = _cw.PlaceGrowth(id, PurseKind.Household, cells, site.Blueprint, sec);
        if (!pr.Ok) return false;
        if (bill > 0) { _econ.Ledger.Transfer(purse, _econ.Ledger.Open(payeeKind, payee), new Coin(bill), TransferReason.Trade); SpentMites += bill; }
        foreach (var (g, q) in need)
        {
            long lack = Math.Max(0, q - store.Qty(g));
            if (lack > 0) store.Create(g, lack, CreateReason.Import);
            store.Destroy(g, q, DestroyReason.Consumed);
        }
        foreach (var gid in pr.Ghosts) if (_cw.Book.Ghosts.TryGetValue(gid, out var g)) { _cw.BuiltByGrowth(g, sec); Laid++; }
        if (_cw.Site(pr.Site) is { } ns) { _cw.Refresh(ns); }
        _cw.Publish(sec);
        AssignBeds(hh);
        return true;
    }

    /// <summary>Gives each member of a household a bed of his own in the house (the rooms' bed cells in order; members by id), the owner of the room the household.</summary>
    public void AssignBeds(int hh)
    {
        var hht = _pop.Households; var id = hht.IdOf(hh);
        var beds = new List<long>();
        var structures = new SortedSet<int>();
        foreach (var s in _cw.Book.Sites.Values.Where(x => x.State == SiteState.Finished && HouseholdOf(x) == hh)) { int st0 = StructureOf(s); if (st0 > 0) structures.Add(st0); }
        foreach (int st in structures)
        {
            var f = _cw.Rooms.Facts(st); if (f == null) continue;
            foreach (var r in f.Rooms.Where(r => r.Indoor).OrderBy(r => r.Z))
            {
                _book.RoomOwner[RoomWorld.Key(r.Origin.X, r.Origin.Y, r.Z)] = id.Value;
                foreach (var (x, y) in r.Cells) if (_cw.BuiltAt(x, y, r.Z, GhostSlot.Fixture) is { } b && _cw.Costs.Library.Pieces.TryGet(b.Piece, out var pd) && pd.Beds > 0) beds.Add(RoomWorld.Key(x, y, r.Z));
            }
        }
        beds = [.. beds.Distinct().Order()];
        var members = hht.Members(hh).OrderBy(m => m).ToList();
        for (int i = 0; i < beds.Count; i++)
        {
            if (i < members.Count) _book.BedAssigned[beds[i]] = _pop.People.IdOf(members[i]).Value;
            else _book.BedAssigned.Remove(beds[i]);
        }
    }
}
