using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// P5-D: background growth made visible (build-mode-and-construction.md 4.9). A growth project whose plot lies in a chunk the player has seen becomes a real building site: the lot's own
/// blueprint laid out exactly as the chunk generator will stamp the finished lot, the payer household as owner and payer, the abstract hours already worked mapped onto pieces in build order
/// (built pieces' hours = the done share of the plan's hours) and what the project had bought in the pile. From then on the site is the truth: the household's own hands and hired idle hands
/// build it piece by piece at Phase 3's hours a day (own 2 h, hired 4 h, the unskilled wage) through the one work engine, the seasonal step only supplies materials and re-forms the crew,
/// and the finished site becomes the Phase 3 lot (the same <see cref="LotRecord"/>; its structure record dropped). A cottage the lord pays for is posted as civic work on the notice board
/// instead (the hero may take it). Elsewhere (a chunk never seen, the headless soak) growth stays as Phase 3. Clearings (assarts) seen by the player are shown as clearings with their hours;
/// their trees fall when Phase 3 finishes them.
/// </summary>
public sealed class GrowthSites : IGrowthSites
{
    public const byte KindCottage = 0, KindAssart = 1, KindPremises = 2;
    private readonly ConstructionWorld _cw;
    private readonly GrowthSystem _g;
    private readonly ChunkStore _chunks;
    private readonly ChunkGenerator _gen;
    private Population Pop => _cw.Pop!;
    private RegionData Region => Pop.Region;

    public GrowthSites(ConstructionWorld cw, GrowthSystem growth, ChunkStore chunks, ChunkGenerator gen) { _cw = cw; _g = growth; _chunks = chunks; _gen = gen; }

    public bool Enabled => _cw.ContractRules.GrowthSites && _cw.Pop != null;

    public bool Shows(RectM plot)
    {
        int x0 = ChunkStreamer.FloorDiv(plot.X, ChunkContent.Size), x1 = ChunkStreamer.FloorDiv(plot.Right - 1, ChunkContent.Size);
        int y0 = ChunkStreamer.FloorDiv(plot.Y, ChunkContent.Size), y1 = ChunkStreamer.FloorDiv(plot.Bottom - 1, ChunkContent.Size);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (_chunks.GetSnapshot(new ChunkKey(x, y, 0)) != null) return true;
        return false;
    }

    private bool ShowsTiles(int[] tiles)
    {
        foreach (int t in tiles)
            if (Shows(new RectM(t % RegionData.Size * RegionData.TileM, t / RegionData.Size * RegionData.TileM, RegionData.TileM, RegionData.TileM))) return true;
        return false;
    }

    public void Started(BuildProject p, GameTime now)
    {
        if (!Enabled) { if (p.Kind == ProjectKind.Premises) Fallback(p, now); return; }
        if (p.Kind == ProjectKind.Assart) { if (ShowsTiles(p.Tiles)) LinkClearing(p, now); return; }
        if (!Shows(p.Rect) || !Link(p, now)) { if (p.Kind == ProjectKind.Premises) Fallback(p, now); }
    }

    /// <summary>Premises that cannot be laid out are added at once, as Phase 3 adds them.</summary>
    private void Fallback(BuildProject p, GameTime now)
    {
        _g.CompletePremises(p, now);
        _g.State.Projects.Remove(p);
    }

    /// <summary>The hourly look (the construction's system): projects whose plot the player has now seen become sites, clearings show their hours.</summary>
    public void LinkSeen(GameTime now)
    {
        if (!Enabled) return;
        foreach (var p in _g.State.Projects.ToList())
        {
            if (_cw.Book.Growth.TryGetValue(p.Serial, out var link))
            {
                if (link.Kind == KindAssart) { link.DoneMilli = p.HoursDone * 1000; link.NeedMilli = p.HoursNeeded * 1000; }
                continue;
            }
            if (p.Kind == ProjectKind.Assart) { if (ShowsTiles(p.Tiles)) LinkClearing(p, now); }
            else if (p.Kind == ProjectKind.Cottage && p.Lot < 0 && Shows(p.Rect)) Link(p, now);
        }
        foreach (var serial in _cw.Book.Growth.Keys.ToList())
            if (_cw.Book.Growth[serial].Kind == KindAssart && !_g.State.Projects.Any(p => p.Serial == serial)) _cw.Book.Growth.Remove(serial);   // the clearing is done
    }

    private void LinkClearing(BuildProject p, GameTime now)
    {
        int n = RegionData.Size;
        int x0 = p.Tiles.Min(t => t % n), x1 = p.Tiles.Max(t => t % n), y0 = p.Tiles.Min(t => t / n), y1 = p.Tiles.Max(t => t / n);
        _cw.Book.Growth[p.Serial] = new GrowthLink
        {
            Serial = p.Serial, Kind = KindAssart, Settlement = p.Settlement, X = x0 * RegionData.TileM, Y = y0 * RegionData.TileM, W = (x1 - x0 + 1) * RegionData.TileM, H = (y1 - y0 + 1) * RegionData.TileM,
            DoneMilli = p.HoursDone * 1000, NeedMilli = p.HoursNeeded * 1000, LinkedSec = now.Seconds,
        };
    }

    private EntityId HouseholdId(int hh) => Pop.Households.IdOf(hh);

    /// <summary>
    /// Makes a project a site (spec 4.9): the lot's plan laid out as the generator will stamp it, the pieces the abstract hours built counted as built in build order (until their hours
    /// reach the done share of the plan), the piece in hand started, the rest of the bought materials in the pile; then the crew, or the civic contract when the lord pays.
    /// </summary>
    public bool Link(BuildProject p, GameTime now)
    {
        if (p.Payer < 0 || p.Payer >= Pop.Households.Count || Pop.Households.MemberCount[p.Payer] == 0) return false;
        var plan = Region.Settlements[p.Settlement].Plan;
        var lot = new Lot(plan.Lots.Count, p.Use, p.Dwelling, p.Blueprint, p.Rect, p.Facing, p.Wealth);
        if (_gen.PlanFor(p.Settlement, lot) is not { } placed) return false;
        var cells = _cw.Costs.PlanCells(placed.Grid).Select(c => new PlaceCell(placed.X0 + c.X, placed.Y0 + c.Y, c.Z, c.Piece)).ToList();
        long sec = now.Seconds;
        var owner = HouseholdId(p.Payer);
        var pr = _cw.PlaceGrowth(owner, PurseKind.Household, cells, p.Blueprint, sec);
        if (!pr.Ok) return false;
        var site = _cw.Site(pr.Site)!;
        _cw.Book.Growth[p.Serial] = new GrowthLink
        {
            Serial = p.Serial, Kind = p.Kind == ProjectKind.Premises ? KindPremises : KindCottage, Site = site.Id, Settlement = p.Settlement, X = p.Rect.X, Y = p.Rect.Y, W = p.Rect.W, H = p.Rect.H, LinkedSec = sec,
        };
        // the bought (and gathered) materials, by good: the abstract build consumed them as they came
        var pool = new SortedDictionary<ushort, long>();
        var lines = _g.Tuning.CottageMaterials; var goods = _cw.Econ.Goods;
        for (int i = 0; i < p.MaterialG.Length && i < lines.Count; i++) if (p.MaterialG[i] > 0 && goods.TryIdOf(lines[i].Good, out var gid)) pool[gid] = pool.GetValueOrDefault(gid) + p.MaterialG[i];
        // the abstract hours onto pieces, in build order: the done share of the plan's hours
        var order = _cw.BuildOrder(site);
        long planUnits = order.Sum(g => g.WorkNeed);
        long target = p.HoursNeeded <= 0 ? 0 : (long)((Int128)planUnits * Math.Min(p.HoursDone, p.HoursNeeded) / p.HoursNeeded);
        long cum = 0;
        foreach (var g in order)
        {
            if (cum >= target) break;
            var mats = _cw.MaterialIds(_cw.CostOf(g));
            if (mats.Any(m => m.Good == ushort.MaxValue || pool.GetValueOrDefault(m.Good) < m.Grams)) continue;     // Phase 3 never bought it (planks, nails): the piece waits for its goods
            if (!_cw.Stands(g)) continue;
            foreach (var (good, grams) in mats) pool[good] -= grams;
            if (cum + g.WorkNeed <= target) { cum += g.WorkNeed; _cw.BuiltByGrowth(g, sec); }
            else { _cw.StartedByGrowth(g, target - cum); cum = target; }
        }
        foreach (var (good, grams) in pool) _cw.ToPileByGrowth(site, good, grams);
        _cw.SyncStructures();
        // who builds the rest: a cottage the lord pays for is civic work on the board; any other the household's own and hired hands
        bool lord = p.Payer == _g.LordOf(p.Settlement);
        if (lord && p.Kind == ProjectKind.Cottage)
        {
            long labour = _cw.LabourMites(_cw.LabourUnits(site));
            _g.TopUp(p.Payer, p.Settlement, labour + _g.ReserveMites);
            if (_cw.Post(owner, PurseKind.Household, site.Id, ContractKind.Civic, p.Settlement, sec, p.Serial).Ok) { _cw.Refresh(site); _cw.Publish(sec); return true; }
        }
        Crew(p, site, (int)now.TotalDays, sec);
        _cw.Refresh(site);
        _cw.Publish(sec);
        return true;
    }

    /// <summary>
    /// The household's crew (spec 4.9, Phase 3's labour): its members of 14 to 59 build their own house two hours a day for nothing (none for gentry), and up to the growth crew of idle
    /// hands of the settlement are hired four hours a day at the unskilled wage, paid through the site's escrow by the household.
    /// </summary>
    private void Crew(BuildProject p, ConstructionSite s, int day, long sec)
    {
        if (_cw.ContractOf(s.Id) is { } c && c.State == ContractState.Signed) return;       // a builder's crew is his own
        var pt = Pop.People; var hht = Pop.Households; var t = _g.Tuning;
        s.Crew.RemoveAll(m => m.PersonIndex < 0 || m.PersonIndex >= pt.Count || !pt.IsAlive(m.PersonIndex) || pt.AgeYears(m.PersonIndex, day) >= 60);
        var taken = new HashSet<EntityId>(_cw.Book.Sites.Values.Where(o => o.Open).SelectMany(o => o.Crew.Select(m => m.Person)));
        if (hht.Class[p.Payer] != SocialClass.Gentry)
            foreach (int m in hht.Members(p.Payer))
            {
                if (!pt.IsAlive(m) || pt.AgeYears(m, day) is < 14 or >= 60 || taken.Contains(pt.IdOf(m)) || s.Crew.Count >= _cw.Rules.CrewMax) continue;
                s.Crew.Add(new CrewMember { Person = pt.IdOf(m), PersonIndex = m, Class = CrewClass.Follower, DayCapSec = t.OwnHoursPerMemberDay * 3600L });
                taken.Add(pt.IdOf(m));
            }
        int hired = s.Crew.Count(m => m.Class == CrewClass.Labourer);
        foreach (int m in _g.IdleHands(p.Settlement, day))
        {
            if (hired >= t.CrewMax || s.Crew.Count >= _cw.Rules.CrewMax) break;
            if (pt.Household[m] == p.Payer || taken.Contains(pt.IdOf(m))) continue;
            s.Crew.Add(new CrewMember { Person = pt.IdOf(m), PersonIndex = m, Class = CrewClass.Labourer, WageMitesPerDay = _cw.Rules.WageLabourerMitesPerDay, DayCapSec = t.HiredHoursPerHandDay * 3600L });
            taken.Add(pt.IdOf(m)); hired++;
        }
        if (_cw.Book.AdvancedSec < 0) _cw.Book.AdvancedSec = sec / _cw.Rules.QuantumSeconds * _cw.Rules.QuantumSeconds;
    }

    public bool Advance(BuildProject p, GameTime now, RegionOutbox ob, out bool done)
    {
        done = false;
        if (!_cw.Book.Growth.TryGetValue(p.Serial, out var link) || link.Kind == KindAssart) return false;
        var s = _cw.Site(link.Site);
        long sec = now.Seconds; int day = (int)now.TotalDays;
        if (s == null) { _cw.Book.Growth.Remove(p.Serial); return false; }
        var hht = Pop.Households;
        if (p.Payer < 0 || p.Payer >= hht.Count || hht.MemberCount[p.Payer] == 0)
        {
            // the payer is gone: the project is given up as Phase 3 gives it up; the half-built house stays
            _cw.GiveUp(s, sec); _cw.Book.Growth.Remove(p.Serial); done = true; return true;
        }
        _cw.AdvanceTo(sec);
        // the plan's hours done so far (the site is the truth): the project's own count follows it
        long left = _cw.LabourUnits(s), total = Math.Max(1, left + _cw.Book.Built.Values.Where(b => b.Site == s.Id).Sum(b => Math.Max(1, _cw.Costs[b.Piece].WorkUnits)));
        long doneHours = p.HoursNeeded - (long)((Int128)p.HoursNeeded * left / total);
        if (doneHours > p.HoursDone) { p.HoursDone = doneHours; p.StalledSeasons = 0; } else p.StalledSeasons++;
        int seasonsOld = (day - p.StartDay) / TimeConst.DaysPerSeason;
        if (s.State == SiteState.Finished && !_cw.GhostsOf(s.Id).Any())
        {
            if (seasonsOld < _g.Tuning.MinSeasons) return true;
            p.HoursDone = p.HoursNeeded;
            _cw.HandOver(s, _cw.Econ.HouseholdStore[p.Payer], sec);
            _cw.Book.Growth.Remove(p.Serial);
            if (p.Kind == ProjectKind.Premises) _g.CompletePremises(p, now); else _g.Complete(p, now, ob);
            done = true; return true;
        }
        if (p.StalledSeasons >= _g.Tuning.GiveUpSeasons) { _cw.GiveUp(s, sec); _cw.Book.Growth.Remove(p.Serial); done = true; return true; }
        Supply(p, s, now);
        Crew(p, s, day, sec);
        _cw.Refresh(s);
        _cw.Publish(sec);
        return true;
    }

    /// <summary>
    /// The season's materials for a site (Phase 3's sources, into the pile instead of consumed): poles cut in the settlement's woodland, clay dug from the pits within 3 km, thatch bound
    /// from the commons' straw bought from the fief's treasury, the rest bought at the market by the household and carried to its plot.
    /// </summary>
    private void Supply(BuildProject p, ConstructionSite s, GameTime now)
    {
        var ew = _g.Economy; var e = _cw.Econ; int day = (int)now.TotalDays;
        var pile = _cw.PileOf(s);
        var party = ew.Markets.HouseholdParty(p.Payer);
        var yr = _g.State.Year(now.Year);
        foreach (var (good, need) in _cw.Needed(s))
        {
            long miss = need - pile.Qty(good);
            if (miss > 0) yr.MaterialsMites += Source(p.Settlement, good, miss, pile, party, _g.ReserveMites / 2, day);
        }
    }

    /// <summary>
    /// Gets up to <paramref name="miss"/> of a building good by Phase 3's sources into <paramref name="into"/> (a pile, or a builder's store): poles cut in the settlement's woodland (all it gives: Phase 3 buys 30% at a market that rarely holds logs), clay dug
    /// from the pits within 3 km, thatch bound from the commons' straw (bought from the fief's treasury at the book's reference price), the rest bought at the market (a real order) and carried
    /// there. The payer keeps <paramref name="keep"/> mites back. Returns the coin spent.
    /// </summary>
    public long Source(int settlement, ushort good, long miss, Store into, MarketParty party, long keep, int day)
    {
        var ew = _g.Economy; var e = _cw.Econ; var cat = e.Goods;
        var book = ew.Markets.BookForSettlement(settlement);
        long Bal() => e.Ledger.Balance(party.Purse).Mites;
        long spent = 0;
        string id = cat.StringId(good);
        if (id == "logs") { long got = ew.Woodland.Draw(settlement, miss, day); if (got > 0) { into.Create(good, got, CreateReason.Cut); _g.State.PolesGatheredG += got; miss -= got; } }
        else if (id == "clay") { long got = _g.DigClay(settlement, miss, day); if (got > 0) { into.Create(good, got, CreateReason.Mined); miss -= got; } }
        else if (id == "thatch" && e.OtherStores.TryGetValue(Ravensguard.Sim.Farming.FarmIds.Fodder(settlement), out var fodder))
        {
            ushort straw = cat.IdOf("straw"); long take = Math.Min(miss, fodder.Qty(straw) / 7);
            long price = take <= 0 ? 0 : Math.Min((long)((Int128)take * book.Goods[straw].RefMicro / Ravensguard.Sim.Economy.Markets.PriceCurve.Micro), Math.Max(0, Bal() - keep));
            int fief = Region.Settlements[settlement].FiefIndex;
            if (take > 0 && price > 0 && fodder.Destroy(straw, take, DestroyReason.Consumed))
            {
                into.Create(good, take, CreateReason.Crafted);                          // the straw bound into thatch
                if (fief >= 0 && e.Ledger.TryFind(PurseKind.Treasury, Region.Fiefs[fief].Id, out var tr)) e.Ledger.Transfer(party.Purse, tr, new Coin(price), TransferReason.Rent);
                spent += price; miss -= take;
            }
        }
        if (miss <= 0) return spent;
        long limit = Math.Max(0, Bal() - keep);
        if (limit <= 0) return spent;
        var res = book.Buy(party, good, miss, 3000, day, limit);
        if (res.Qty <= 0) return spent;
        if (party.Store != into) Store.Move(party.Store, into, good, res.Qty);
        return spent + res.PaidMites;
    }
}
