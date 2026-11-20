using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// P5-D: building contracts and visible growth in the street (build-mode-and-construction 4.7, 4.9). The contract board (Q > Building work) is filled at a barrier from the construction
/// world and every choice goes through the hero's desk (<c>cn:take</c>, <c>cn:drop</c>, <c>cn:commission</c>: recorded for replay). Growth sites and builders' crews are ordinary building
/// sites, so the ghost layer of P5-B draws them. The dev console's <c>build growth [civic] [percent]</c> stages a growth cottage beside the hero; <c>build hire</c> commissions the
/// cheapest builder for the hero's nearest site.
/// </summary>
public partial class StreetView
{
    ContractBoardData _contracts = new();
    double _contractAcc = 99;

    /// <summary>Reads the board at a barrier (the panel asks every second; a read is cheap).</summary>
    ContractBoardData ContractData()
    {
        if (Runtime is not { } rt || BuildWorld is not { } cw) return _contracts;
        if (_contractAcc < 0.5) return _contracts;
        _contractAcc = 0;
        var (hx, hy) = HeroRegionMm();
        int x = (int)(hx / 1000), y = (int)(hy / 1000);
        var hero = EconomyState.HeroOwner;
        var t = rt.AtBarrier(() => Board(cw, rt, hero, x, y)); t.Wait(500);
        if (t.IsCompletedSuccessfully) _contracts = t.Result;
        return _contracts;
    }

    static ContractBoardData Board(ConstructionWorld cw, Ravensguard.Sim.Worlds.WorldRuntime rt, EntityId hero, int x, int y)
    {
        var d = new ContractBoardData();
        var reg = rt.State.Population.Region;
        string Place(int s) => s >= 0 && s < reg.Settlements.Count ? reg.Settlements[s].Name : "";
        string Title(string bp) => bp.Length == 0 ? L.T("CONTRACT_WORK") : Ravensguard.Game.UI.NewGame.UiKit.Pretty(bp);
        int near = -1; long bd = long.MaxValue;
        for (int i = 0; i < reg.Settlements.Count; i++) { long dx = reg.Settlements[i].CenterX - x, dy = reg.Settlements[i].CenterY - y; if (dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; near = i; } }
        d.PlaceName = Place(near);
        long now = cw.Book.AdvancedSec;
        int Progress(Contract c) { var s = cw.Site(c.Site); if (s == null || c.WorkUnits <= 0) return 1000; return (int)Math.Clamp(1000 - cw.LabourUnits(s) * 1000 / c.WorkUnits, 0, 1000); }
        ContractBoardData.Row RowOf(Contract c) => new(c.Id.Value & ((1UL << 36) - 1), Title(c.Blueprint), Place(c.Settlement), c.LabourMites, c.WorkUnits / 360_000_000, Progress(c), c.ReleasedMites, c.TotalMites,
            c.DueSec > 0 ? Math.Max(0, (c.DueSec - now) / 86400) : 0, L.T("CONTRACT_STATE_" + c.State.ToString().ToUpperInvariant()),
            Math.Max(0, (c.PostedSec + cw.ContractRules.NoticeDays * 86400L - now) / 3600));
        foreach (var c in cw.Board().OrderBy(c => c.Id)) d.Board.Add(RowOf(c));
        if (cw.Book.Contracts.Values.FirstOrDefault(c => c.State == ContractState.Signed && c.Contractor == hero) is { } mine) d.Mine = RowOf(mine);
        foreach (var c in cw.Book.Contracts.Values.Where(c => c.Client == hero && c.Kind == ContractKind.Commission)) d.Commissioned.Add(RowOf(c));
        var site = cw.Book.Sites.Values.Where(s => s.Open && s.Owner == hero && cw.ContractOf(s.Id) == null).OrderBy(s => Math.Abs(s.PileX - x) + Math.Abs(s.PileY - y)).ThenBy(s => s.Id).FirstOrDefault();
        if (site != null)
        {
            d.SiteId = site.Id.Value; d.SiteName = site.Blueprint.Length > 0 ? Title(site.Blueprint) : L.T("CONTRACT_YOUR_SITE"); d.SiteHours10 = cw.LabourUnits(site) / 360_000_000;
            foreach (var b in cw.Bids(site.Id)) d.Bids.Add(new(b.Builder.Id.Value, b.Builder.Name, b.MaterialsMites, b.LabourMites, b.HaulMites, b.Crew, b.ExpectedDaysX10));
        }
        d.CoinMites = cw.Econ.Ledger.TryFind(PurseKind.Person, hero, out var p) ? cw.Econ.Ledger.Balance(p).Mites : 0;
        return d;
    }

    void OpenContracts()
    {
        if (_router == null || Runtime == null || _heroLink == null || BuildWorld == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _contractAcc = 99; ContractData();
        _router.Push(new ContractPanel(_router, () => { _contractAcc += 1; return ContractData(); }, TakeContract, DropContract, Commission) { ShotPath = _opt.Panel == "contracts" ? _opt.ShotPath : null });
    }

    EntityId ContractId(ulong serial) => ConstructionIds.Make(BuildWorld!.RegionId, ConstructionIds.ContractSlot, serial);
    void TakeContract(ulong serial) { _heroLink?.Issue(ConstructionDesk.TakeContract(ContractId(serial), HeroNow())); _contractAcc = 99; }
    void DropContract(ulong serial) { _heroLink?.Issue(ConstructionDesk.DropContract(ContractId(serial), HeroNow())); _contractAcc = 99; }
    void Commission(ulong site, ulong builder, int crew) { _heroLink?.Issue(ConstructionDesk.Commission(new EntityId(site), new EntityId(builder), crew, HeroNow())); _contractAcc = 99; }

    /// <summary>The Q menu's line for the contract board.</summary>
    void ContractVerbs(List<QMenu.Verb> v)
    {
        if (Runtime == null || _heroLink == null || BuildWorld == null) return;
        v.Add(new(L.T("CONTRACT_Q"), OpenContracts));
    }

    // ------------------------------------------------------------------ dev: build growth | build hire

    /// <summary>The dev console's contract verbs (under <c>build</c>); null when the verb is not one of them.</summary>
    string? DevContracts(string[] a)
    {
        if (Runtime is not { } rt || BuildWorld is not { } cw || a.Length == 0) return null;
        var s = _world.Snapshot;
        int hx = s.OriginX + s.Hero.CellX, hy = s.OriginY + s.Hero.CellY;
        long now = _world.Clock.Now.Seconds;
        switch (a[0])
        {
            case "growth":
            {
                bool civic = a.Skip(1).Contains("civic");
                int pct = a.Skip(1).Select(x => int.TryParse(x, out var v) ? v : -1).FirstOrDefault(v => v >= 0, 40);
                // an open patch beside the hero for the plot (growth's own plots are on the plan's streets; this is a stage)
                var size = PlanSize("peasant_cottage");
                (int X, int Y)? spot = null;
                // the nearest open patch within the camera's reach (first without props, then with them: placing does not fell trees)
                foreach (bool props in new[] { true, false })
                    for (int ring = 0; ring <= 8 && spot == null; ring++)
                        for (int oy = -ring; oy <= ring && spot == null; oy++)
                            for (int ox = -ring; ox <= ring && spot == null; ox++)
                                if (Math.Max(Math.Abs(ox), Math.Abs(oy)) == ring && OpenPatch(hx + 2 + ox * 2, hy + 1 + oy * 2, size.W + 4, size.H + 4, props)) spot = (hx + 2 + ox * 2, hy + 1 + oy * 2);
                if (spot is not { } at) return L.T("CONTRACT_DEV_NO_ROOM");
                return rt.AtBarrier(() =>
                {
                    var ew = rt.State.HomeRecords!.Economy; var g = ew.Growth; var reg = rt.State.Population.Region; var hht = rt.State.Population.Households;
                    int st = 0; long bd = long.MaxValue;
                    for (int i = 0; i < reg.Settlements.Count; i++) { long dx = reg.Settlements[i].CenterX - hx, dy = reg.Settlements[i].CenterY - hy; if (dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; st = i; } }
                    int lord = g.LordOf(st), payer = civic ? lord : -1;
                    if (!civic) for (int h = 0; h < hht.Count && payer < 0; h++) if (h != lord && hht.Settlement[h] == st && hht.MemberCount[h] >= 2 && hht.Class[h] != Ravensguard.Sim.People.SocialClass.Gentry) payer = h;
                    if (payer < 0) return L.T("CONTRACT_DEV_NO_PAYER");
                    var t = g.Tuning;
                    var p = new BuildProject
                    {
                        Serial = g.State.NextSerial++, Kind = ProjectKind.Cottage, Settlement = st, StartDay = (int)(now / 86400) - 100, Payer = payer, Rect = new RectM(at.X, at.Y, size.W + 4, size.H + 4), Facing = 2,
                        Use = LotUse.Cottage, Dwelling = DwellingType.Cottage, Blueprint = "peasant_cottage", Beds = t.CottageBeds, HoursNeeded = 250, HoursDone = 250L * Math.Clamp(pct, 0, 99) / 100,
                        MaterialG = [.. t.CottageMaterials.Select(m => (long)m.Kg * 1000)],
                    };
                    g.State.Projects.Add(p);
                    cw.GrowthSites?.LinkSeen(new GameTime(now));
                    if (!cw.Book.Growth.TryGetValue(p.Serial, out var link)) { g.State.Projects.Remove(p); return L.T("CONTRACT_DEV_NOT_LAID"); }
                    // what Phase 3 never buys (planks, nails) the stage brings, so the crew can finish it
                    if (cw.Site(link.Site) is { } site)
                    {
                        var pile = cw.PileOf(site);
                        foreach (var (good, q) in cw.Needed(site)) { long miss = q - pile.Qty(good); if (miss > 0) pile.Create(good, miss, Ravensguard.Sim.Goods.CreateReason.Import); }
                        cw.Refresh(site);
                    }
                    return L.F("CONTRACT_DEV_GROWTH", reg.Settlements[st].Name, pct, civic ? L.T("CONTRACT_DEV_CIVIC") : L.T("CONTRACT_DEV_HOUSEHOLD"));
                }).GetAwaiter().GetResult();
            }
            case "hire":
            {
                return rt.AtBarrier(() =>
                {
                    var site = cw.Book.Sites.Values.Where(x => x.Open && x.Owner == EconomyState.HeroOwner && cw.ContractOf(x.Id) == null).OrderBy(x => Math.Abs(x.PileX - hx) + Math.Abs(x.PileY - hy)).FirstOrDefault();
                    if (site == null) return L.T("BUILD_NO_SITE");
                    var bid = cw.Bids(site.Id).FirstOrDefault();
                    if (bid == null) return L.T("CONTRACT_NO_BUILDERS");
                    cw.DevStock(site.Id, now);                   // the stage's materials: the builder's cart would bring them in a few hours
                    // dev only: the stage's coin for the first third, minted into the hero's purse
                    var led = cw.Econ.Ledger; var hp = led.Open(PurseKind.Person, EconomyState.HeroOwner);
                    if (led.Balance(hp).Mites < bid.TotalMites / 3) led.Mint(hp, new Coin(bid.TotalMites / 3 - led.Balance(hp).Mites + 100), SourceReason.Seed);
                    var r = cw.Commission(EconomyState.HeroOwner, PurseKind.Person, site.Id, bid.Builder.Id, bid.Crew, now);
                    return L.F(r.Key, r.Args);
                }).GetAwaiter().GetResult();
            }
            default: return null;
        }
    }

    bool OpenPatch(int x0, int y0, int w, int h, bool props = true)
    {
        var m = Map;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int lx = x0 + x - m.OriginX, ly = y0 + y - m.OriginY;
                if (!m.Grid.InBounds(lx, ly, 0)) return false;
                var g = m.GroundAt(lx, ly, 0);
                if (g is Ravensguard.Sim.Live.GroundKind.Cobble or Ravensguard.Sim.Live.GroundKind.Dirt or Ravensguard.Sim.Live.GroundKind.Water or Ravensguard.Sim.Live.GroundKind.DeepWater || m.StructureAt(lx, ly, 0) != null) return false;
            }
        int ax = x0 - m.OriginX, ay = y0 - m.OriginY;
        return !props || !m.Props.Any(p => p.Z == 0 && p.X >= ax - 1 && p.X <= ax + w && p.Y >= ay - 1 && p.Y <= ay + h);
    }

    /// <summary>Screenshot driver hook (--ng-shot contracts): a growth cottage beside the hero (household or civic), a builder hired for the hero's cottage, or the board.</summary>
    public void DebugContracts(string scene)
    {
        if (BuildWorld is not { } cw) { GD.PushWarning("[contracts] no construction world"); return; }
        switch (scene)
        {
            case "builder":
                GD.Print("[contracts] " + DevBuild(["plan", "test_cottage"]));
                GD.Print("[contracts] " + DevBuild(["hire"]));
                GD.Print("[contracts] " + DevBuild(["finish", "24"]));
                break;
            case "board":
                GD.Print("[contracts] " + DevBuild(["growth", "civic", "20"]));
                GD.Print("[contracts] " + DevBuild(["plan", "test_cottage"]));
                break;
            default:
                GD.Print("[contracts] " + DevBuild(["growth", "40"]));
                break;
        }
        var s = _world.Snapshot;
        // the camera on the middle of the newest site's footprint (its ghosts and built cells), a little above it so the hints below do not cover it
        var site = Runtime!.AtBarrier(() =>
        {
            var last = cw.Book.Sites.Values.OrderByDescending(x => x.Id).FirstOrDefault();
            if (last == null) return (X: 0, Y: 0);
            var xs = cw.GhostsOf(last.Id).Select(g => (X: g.X, Y: g.Y)).Concat(cw.Book.Built.Values.Where(b => b.Site == last.Id).Select(b => (X: b.X, Y: b.Y))).ToList();
            return xs.Count == 0 ? (X: last.PileX, Y: last.PileY) : (X: (xs.Min(p => p.X) + xs.Max(p => p.X)) / 2, Y: (xs.Min(p => p.Y) + xs.Max(p => p.Y)) / 2);
        }).GetAwaiter().GetResult();
        if (site != (0, 0)) _pan = new Vector2((site.X - s.OriginX - s.Hero.CellX) * Tile, (site.Y + 2 - s.OriginY - s.Hero.CellY) * Tile);
        GD.Print($"[contracts] site centre at {site.X},{site.Y}; hero at {s.OriginX + s.Hero.CellX},{s.OriginY + s.Hero.CellY}; pan {_pan}");
    }

    /// <summary>For the screenshot driver: the contract board opened.</summary>
    public void DebugOpenContracts() { _contractAcc = 99; OpenContracts(); }
}
