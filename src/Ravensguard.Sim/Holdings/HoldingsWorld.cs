using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Holdings;

/// <summary>The answer to a holdings verb: whether it worked, the text key and its arguments (the desk turns it into the hero's result).</summary>
public sealed record HoldingsResult(bool Ok, string Key, object[] Args, long Mites = 0, bool CannotAfford = false)
{
    public static HoldingsResult No(string key, params object[] args) => new(false, key, args);
    public static HoldingsResult Yes(string key, params object[] args) => new(true, key, args);
}

/// <summary>
/// Holdings (P6-B6, holdings-and-works-v2 3.1, 3.7 and 3.8): the clearing and sowing of a claim, the coppice, horse breeding and the carting cost of a quarry. This part is the hero's acre:
/// <c>Clear</c> makes a <see cref="Clearing"/> of one tile of his claim (90 worker-hours an acre: the hero by hand, hired hands six hours a day), the acre's trees come down one by one with
/// their logs and firewood kept in the clearing's store, the stumps are grubbed, and the acre becomes a parcel of the hero's household (the farm tables credit its harvest to his barn);
/// <c>Sow</c> ploughs and sows it by hand in the window and calls <see cref="LandWorld.Improve"/> with <see cref="ImprovementKind.SownAcre"/>, which grows the claim by eight tiles.
/// </summary>
public sealed partial class HoldingsWorld
{
    /// <summary>90 worker-hours an acre in all (Phase 5 OV-3's figure; background growth's assart rule).</summary>
    public const long AcreMilli = 90_000;
    /// <summary>The first 60% of the work brings the trees down, the rest grubs the stumps.</summary>
    public const int FellPct = 60;
    /// <summary>The hero's hours on clearing in a game day (a long day's work).</summary>
    public const long HeroDayCapMilli = 8_000;
    /// <summary>A hired hand works six hours a day (as growth's assart hands).</summary>
    public const long HandDayMilli = 6_000;
    public const int MaxHands = 6, HireRadiusM = 6_000;
    public const ulong StoreBase = 0x76UL << 40;

    /// <summary>The seasonal furnishing of NPC households' houses (null without a construction world).</summary>
    public Ravensguard.Sim.Building.HouseholdFurnisher? Furnisher { get; set; }
    public HoldingsBook Book { get; }
    public LandWorld Land { get; }
    public EconomyWorld Ew { get; }
    public Population Pop { get; }
    public EconomyState Econ { get; }
    public ulong Seed { get; }
    public int RegionId { get; }
    public RegionData Region => Pop.Region;

    public HoldingsWorld(HoldingsBook book, LandWorld land, EconomyWorld ew, Population pop, EconomyState econ, ulong seed)
    {
        Book = book; Land = land; Ew = ew; Pop = pop; Econ = econ; Seed = seed;
        RegionId = Ravensguard.Sim.WorldGen.Region.RegionCoord.OfCell(pop.Region.CellX, pop.Region.CellY).Id;
    }

    /// <summary>Whether a parcel is a cleared acre of a hero's claim (the farm tables' re-let leaves it alone).</summary>
    public bool IsHeld(int parcel) { foreach (var c in Book.Clearings) if (c.Parcel == parcel && c.State is ClearingState.Cleared or ClearingState.Sown) return true; return false; }

    /// <summary>The store a clearing's logs and firewood lie in (at the stumps).</summary>
    public Store StoreOf(Clearing c) => Econ.OpenStore(StoreKind.Open, EntityId.MakeIn(IdKind.Other, RegionId, StoreBase | (uint)c.Serial));

    private static int DayOfYear(int day) => day % TimeConst.DaysPerYear + 1;
    private static GameTime Day(int day) => GameTime.FromGameDays(day);

    // ------------------------------------------------------------------ clearing an acre

    /// <summary>Trees on an acre: scrub and a few trees on open ground, up to forty in the thick wood (tree cover 0 to 100).</summary>
    public int TreesOn(int tile) => 3 + Region.Forest[tile] * 37 / 100;

    /// <summary>The tiles of a claim that could be cleared now (ascending): not a field, not built over, no road, water or mineral, not already under a clearing.</summary>
    public IEnumerable<int> ClearableTiles(Claim c)
    {
        foreach (int t in c.Tiles)
            if (Region.ParcelMap[t] == 0 && Region.Built[t] == 0 && Region.Road[t] == 0 && Region.Water[t] == 0 && Region.Mineral[t] == 0 && !Book.Clearings.Any(x => x.Tile == t && x.State != ClearingState.Abandoned))
                yield return t;
    }

    public HoldingsResult Clear(EntityId hero, int claimSerial, int tile, int day)
    {
        var claim = Land.Book.ClaimOf(claimSerial);
        if (claim == null || claim.Claimant != hero || claim.State is not (ClaimState.Open or ClaimState.Deeded)) return HoldingsResult.No("hw_not_your_claim");
        if (tile < 0) tile = ClearableTiles(claim).OrderBy(t => D2(t, claim.BannerX, claim.BannerY)).ThenBy(t => t).DefaultIfEmpty(-1).First();
        if (tile < 0 || Array.BinarySearch(claim.Tiles, tile) < 0) return HoldingsResult.No("hw_not_in_claim");
        if (!ClearableTiles(claim).Contains(tile)) return HoldingsResult.No("hw_tile_not_clearable");
        var row = Ew.Resources.Find(tile, ResourceKind.Wood);
        var c = new Clearing
        {
            Serial = Book.NextSerial++, Claim = claimSerial, Tile = tile, Owner = hero, Trees = TreesOn(tile), NeedMilli = AcreMilli, StartDay = day, LastDay = day,
            WoodG0 = row >= 0 ? Ew.Resources.StockG(row) : 0, State = ClearingState.Working,
        };
        Book.Clearings.Add(c);
        return HoldingsResult.Yes("hw_clear_started", c.Trees, AcreMilli / 1000);
    }

    private long D2(int t, int x, int y)
    {
        int N = RegionData.Size; long dx = t % N * (long)RegionData.TileM + RegionData.TileM / 2 - x, dy = t / N * (long)RegionData.TileM + RegionData.TileM / 2 - y;
        return dx * dx + dy * dy;
    }

    /// <summary>The hero works the acre by hand from <paramref name="fromSec"/> to <paramref name="toSec"/> (the day's cap bounds it).</summary>
    public HoldingsResult HeroWork(EntityId hero, int serial, long fromSec, long toSec, int day, LandChronicle chron)
    {
        var c = Book.Get(serial);
        if (c == null || c.Owner != hero) return HoldingsResult.No("hw_no_clearing");
        if (!c.Open) return HoldingsResult.No("hw_clearing_done");
        long want = Math.Max(0, toSec - fromSec) * 1000 / 3600;
        if (c.HeroDay != day) { c.HeroDay = day; c.HeroDayMilli = 0; }
        long w = Math.Min(Math.Min(want, HeroDayCapMilli - c.HeroDayMilli), c.NeedMilli - c.DoneMilli);
        if (w <= 0) return HoldingsResult.No("hw_hero_tired");
        c.HeroDayMilli += w; c.HeroMilli += w; c.DoneMilli += w;
        Advance(c, day, chron);
        return HoldingsResult.Yes(c.Open ? "hw_hero_worked" : "hw_clearing_cleared", w / 100 / 10.0, Percent(c));
    }

    public static int Percent(Clearing c) => c.NeedMilli == 0 ? 100 : (int)(c.DoneMilli * 100 / c.NeedMilli);

    /// <summary>Hires up to <paramref name="count"/> hands for the clearing: idle adults of 16 to 60 within reach of the tile, nearest place first, then row. Each earns a day's wage each day it works.</summary>
    public HoldingsResult Hire(EntityId hero, int serial, int count, int day)
    {
        var c = Book.Get(serial);
        if (c == null || c.Owner != hero) return HoldingsResult.No("hw_no_clearing");
        if (!c.Open) return HoldingsResult.No("hw_clearing_done");
        count = Math.Min(count, MaxHands - c.Hands.Count);
        if (count <= 0) return HoldingsResult.No("hw_crew_full", MaxHands);
        var pt = Pop.People; int N = RegionData.Size;
        int x = c.Tile % N * RegionData.TileM + RegionData.TileM / 2, y = c.Tile / N * RegionData.TileM + RegionData.TileM / 2;
        var near = new Dictionary<int, long>();
        for (int i = 0; i < Region.Settlements.Count; i++)
        {
            long dx = Region.Settlements[i].CenterX - x, dy = Region.Settlements[i].CenterY - y, d2 = dx * dx + dy * dy;
            if (d2 <= (long)HireRadiusM * HireRadiusM) near[i] = d2;
        }
        var taken = new HashSet<EntityId>(Book.Clearings.Where(o => o.Open).SelectMany(o => o.Hands));
        if (Ew.World.Get<Ravensguard.Sim.Construction.ConstructionWorld>() is { } cw) foreach (var s in cw.Book.Sites.Values) if (s.Open) foreach (var m in s.Crew) taken.Add(m.Person);
        var picks = new List<(long D, int Row)>();
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i)) continue;
            int age = pt.AgeYears(i, day); if (age < 16 || age > 60) continue;
            if (!near.TryGetValue(pt.HomeSettlement[i], out long d)) continue;
            if (pt.Occupation[i] is not (Occupation.None or Occupation.Labourer)) continue;
            if (taken.Contains(pt.IdOf(i))) continue;
            picks.Add((d, i));
        }
        picks.Sort();
        var names = new List<string>();
        foreach (var (_, row) in picks.Take(count)) { c.Hands.Add(pt.IdOf(row)); names.Add(pt.FullName(row)); }
        if (names.Count == 0) return HoldingsResult.No("hw_nobody_to_hire");
        return HoldingsResult.Yes("hw_hired", names.Count, string.Join(", ", names));
    }

    public HoldingsResult Dismiss(EntityId hero, int serial)
    {
        var c = Book.Get(serial);
        if (c == null || c.Owner != hero) return HoldingsResult.No("hw_no_clearing");
        int n = c.Hands.Count; c.Hands.Clear();
        return HoldingsResult.Yes("hw_dismissed", n);
    }

    /// <summary>
    /// One day of the hired hands (the daily step): each is paid a day's wage out of the hero's purse (a hand that cannot be paid goes home, and the clearing waits), then works six hours.
    /// </summary>
    public void Daily(int day, LandChronicle chron)
    {
        var pt = Pop.People; long wage = Ew.Farming.Settings.Def.WageLabourerMitesPerDay;
        foreach (var c in Book.Clearings.ToList())
        {
            if (!c.Open || c.LastDay == day) continue;
            c.LastDay = day;
            if (c.Hands.Count == 0) continue;
            var purse = Econ.Ledger.Open(PurseKind.Person, c.Owner);
            int worked = 0;
            foreach (var id in c.Hands.ToList())
            {
                int row = pt.IndexOf(id);
                if (row < 0 || !pt.IsAlive(row)) { c.Hands.Remove(id); continue; }
                int hh = pt.Household[row];
                if (wage > 0 && (hh < 0 || Econ.Ledger.Balance(purse).Mites < wage || !Econ.Ledger.Transfer(purse, Econ.HouseholdPurse[hh], new Coin(wage), TransferReason.Wage))) { c.ShortDays++; continue; }
                worked++;
            }
            long w = Math.Min(worked * HandDayMilli, c.NeedMilli - c.DoneMilli);
            if (w > 0) { c.DoneMilli += w; Advance(c, day, chron); }
            if (!c.Open) c.Hands.Clear();
        }
    }

    /// <summary>Brings the trees down and the stumps out as far as the hours reach; at the end of the work the acre is cleared.</summary>
    private void Advance(Clearing c, int day, LandChronicle chron)
    {
        long fellMilli = c.NeedMilli * FellPct / 100, grubMilli = c.NeedMilli - fellMilli;
        int felled = (int)Math.Min(c.Trees, c.Trees * Math.Min(c.DoneMilli, fellMilli) / Math.Max(1, fellMilli));
        int grubbed = (int)Math.Min(c.Trees, c.Trees * Math.Max(0, Math.Min(c.DoneMilli - fellMilli, grubMilli)) / Math.Max(1, grubMilli));
        if (felled > c.Felled)
        {
            int row = Ew.Resources.Find(c.Tile, ResourceKind.Wood);
            long per = c.Trees == 0 ? 0 : c.WoodG0 / c.Trees;
            int fire = Land.GrowthTuning.AssartFirewoodPct;
            var store = StoreOf(c);
            for (int k = c.Felled; k < felled; k++)
            {
                long g = row >= 0 ? Ew.Resources.Take(row, per, day) : 0;
                if (g <= 0) continue;
                long fw = g * fire / 100, logs = g - fw;
                if (fw > 0) store.Create(Econ.Food.Firewood, fw, CreateReason.Cut);
                if (logs > 0) store.Create(Econ.Goods.IdOf("logs"), logs, CreateReason.Cut);
                c.FirewoodG += fw; c.LogsG += logs; Book.LogsFelledG += logs;
            }
            c.Felled = felled;
        }
        if (grubbed > c.Grubbed) c.Grubbed = grubbed;
        if (c.DoneMilli >= c.NeedMilli) Complete(c, day, chron);
    }

    /// <summary>The acre is clear: what wood is left comes off the row, the tile leaves the woodland for good and becomes a one-acre parcel of the hero's household.</summary>
    private void Complete(Clearing c, int day, LandChronicle chron)
    {
        var r = Region; int N = RegionData.Size; int t = c.Tile;
        var gs = Ew.Growth.State; var farming = Ew.Farming;
        int row = Ew.Resources.Find(t, ResourceKind.Wood);
        long rest = row >= 0 ? Ew.Resources.Clear(row, day) : 0;
        if (row >= 0) { gs.ClearedTiles.Add(t); gs.ClearedTiles.Sort(); }
        if (rest > 0) { StoreOf(c).Create(Econ.Food.Firewood, rest, CreateReason.Cut); c.FirewoodG += rest; }
        c.Felled = c.Grubbed = c.Trees; c.DoneMilli = c.NeedMilli; c.State = ClearingState.Cleared; c.ClearedDay = day;
        if (gs.FertilityScalePermille == 0)
        {
            long a = 0, b = 0; var F = farming.State.Fields;
            for (int i = 0; i < F.Count; i++) { a += F.Fertility[i]; b += r.Fertility[F.Tile[i]]; }
            gs.FertilityScalePermille = b == 0 ? 1000 : (int)Math.Clamp(a * 1000 / b, 500, 4000);
        }
        int s = NearestSettlement(t);
        byte fert = (byte)Math.Clamp(r.Fertility[t] * (long)gs.FertilityScalePermille / 1000, 0, 100);
        var claim = Land.Book.ClaimOf(c.Claim);
        var owner = new ParcelOwner(OwnerKind.Freeholder, claim?.Fief ?? -1, 0);
        var rec = new ParcelRecord(s, owner, 2, [t], [fert], day);
        int hh = HouseholdOf(c.Owner);
        var rect = new RectM(t % N * RegionData.TileM - RegionData.TileM, t / N * RegionData.TileM - RegionData.TileM, 3 * RegionData.TileM, 3 * RegionData.TileM);
        int pidx = RegionGrowth.Change(r, Ew.Growth.ChunkSink, [rect], () =>
        {
            int pi = RegionGrowth.ApplyParcel(r, rec);
            farming.AddParcels([pi], hh);
            return pi;
        });
        gs.Parcels.Add(rec);
        c.Parcel = pidx;
        chron(Day(day), "land_cleared", [c.Owner], [new Cause(CauseKind.Other, null, NoteKey: "ch_note_homestead_acre")],
            ("settlement", s >= 0 ? r.Settlements[s].Name : ""), ("acres", "1"), ("wood_kg", ((c.LogsG + c.FirewoodG) / 1000).ToString()));
    }

    private int NearestSettlement(int tile)
    {
        int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < Region.Settlements.Count; i++) { long d = D2(tile, Region.Settlements[i].CenterX, Region.Settlements[i].CenterY); if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// <summary>The household a person belongs to (-1 none).</summary>
    public int HouseholdOf(EntityId person)
    {
        int row = Pop.People.IndexOf(person);
        return row >= 0 ? Pop.People.Household[row] : -1;
    }

    // ------------------------------------------------------------------ sowing

    /// <summary>
    /// Ploughs and sows the cleared acre by hand in the sowing window, from the hero's own seed, and calls <see cref="LandWorld.Improve"/>: the claim grows. The household's field holds the crop
    /// from then on (its harvest goes to the household's barn).
    /// </summary>
    public HoldingsResult Sow(EntityId hero, int serial, int day, Store seedFrom, LandChronicle chron)
    {
        var c = Book.Get(serial);
        if (c == null || c.Owner != hero) return HoldingsResult.No("hw_no_clearing");
        if (c.State == ClearingState.Sown) return HoldingsResult.No("hw_already_sown");
        if (c.State != ClearingState.Cleared || c.Parcel < 0) return HoldingsResult.No("hw_not_cleared");
        string? why = Ew.Farming.SowByHand(c.Parcel, seedFrom, DayOfYear(day), out var crop);
        if (why != null) return HoldingsResult.No(why);
        c.State = ClearingState.Sown; c.SownDay = day; c.Crop = crop.ToString().ToLowerInvariant();
        var imp = Land.Improve(c.Claim, ImprovementKind.SownAcre, day, chron);
        return HoldingsResult.Yes("hw_sown", c.Crop, imp.Ok ? imp.Args.FirstOrDefault() ?? 0 : 0);
    }

    /// <summary>The hero takes the logs and firewood from the stumps into his pack.</summary>
    public HoldingsResult Collect(EntityId hero, int serial, Store pack)
    {
        var c = Book.Get(serial);
        if (c == null || c.Owner != hero) return HoldingsResult.No("hw_no_clearing");
        var store = StoreOf(c); long got = 0;
        foreach (var (g, q) in store.Rows().ToList()) { got += Store.MoveUpTo(store, pack, g, q); }
        return got > 0 ? HoldingsResult.Yes("hw_collected", got / 1000) : HoldingsResult.No("hw_nothing_to_collect");
    }
}
