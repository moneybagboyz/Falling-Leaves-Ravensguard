using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Prospecting;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of businesses and prospecting (P5-F): the Ledger (L), the business panel, the prospecting panel with the resource overlay, the survey reports and the licence petition, and
/// the Q-menu verbs (open a business in the building the hero stands in, hire the person beside him for one of his businesses or as steward, buy a share of an owner's business, prospect here). The
/// screens read views built at a barrier; every action is a business or prospecting command through the hero's desk (recorded for replay), its answer toasted when the barrier applied it. The street
/// tells the world where the hero is (the settlement he stands in, for where profit is sent) and which acre he walks (a free walk-by estimate), one command each when it changes.
/// </summary>
public partial class StreetView
{
    LedgerView? _ledgerView; Task<LedgerView?>? _ledgerTask;
    BusinessView? _bizView; Task<BusinessView?>? _bizTask; EntityId _bizId;
    ProspectView? _pxView; Task<ProspectView?>? _pxTask; int _pxTileOverride = -1;
    double _bizAcc; long _bizSeen; int _bizAt = int.MinValue, _walkTile = -1; bool _ownsAny;
    (List<BusinessKind> Kinds, int Structure, string Tools)? _hereKinds; Task<(List<BusinessKind>, int, string)?>? _hereTask;

    static OwnershipWorld? OwnOf(WorldRuntime rt) => rt.State.HomeRecords?.Sim.Get<OwnershipWorld>();

    void RefreshBusiness()
    {
        if (Runtime is not { } rt || _heroLink == null) return;
        int day = (int)(HeroNow() / 86400);
        if (_ledgerTask is { IsCompleted: true } lt) { if (lt.IsCompletedSuccessfully) { _ledgerView = lt.Result; _ownsAny = _ledgerView != null && _ledgerView.Rows.Any(r => r.Tab != LedgerTab.Fief); } _ledgerTask = null; }
        _ledgerTask ??= Bar(rt, () => OwnOf(rt) is { } ow ? Ravensguard.Sim.Rule.RuleLedger.With(rt.State, Ravensguard.Sim.Succession.SuccessionDesk.WithEstate(rt.State, ow.Ledger(day, rt.State.HomeRecords?.Sim.Get<ConstructionWorld>(), rt.State.Mounts)), day) : null);
        if (_bizTask is { IsCompleted: true } bt) { if (bt.IsCompletedSuccessfully) _bizView = bt.Result; _bizTask = null; }
        var id = _bizId;
        if (id != EntityId.None) _bizTask ??= Bar(rt, () => OwnOf(rt)?.Detail(id, day));
        if (_pxTask is { IsCompleted: true } pt) { if (pt.IsCompletedSuccessfully) _pxView = pt.Result; _pxTask = null; }
        int tile = _pxTileOverride >= 0 ? _pxTileOverride : HeroTile();
        var tools = PackToolsLine();
        _pxTask ??= Bar(rt, () => OwnOf(rt)?.Prospect(tile, day, tools));
    }

    /// <summary>The tools in the hero's pack and kit (craft tool ids), read off the desk's view (the barrier checks them again).</summary>
    List<string> PackToolsLine()
    {
        var v = _heroLink?.View; var outp = new List<string>();
        if (v == null) return outp;
        foreach (var i in v.Items) { string id = i.Shape == "dagger" ? "knife" : i.Shape; if (!outp.Contains(id)) outp.Add(id); }
        foreach (var g in v.Goods) if (g.Qty > 0 && !outp.Contains(g.Id)) outp.Add(g.Id);
        return outp;
    }

    /// <summary>Per frame: views kept fresh every two seconds, answers toasted, where the hero is and which acre he walks told to the world when they change.</summary>
    void BusinessTick(double delta)
    {
        _bizAcc += delta;
        if (_bizAcc >= 2.0) { _bizAcc = 0; RefreshBusiness(); }
        if (_heroLink?.View is { } v)
            foreach (var r in v.Results.Where(r => r.Kind is HeroCommandKind.Business or HeroCommandKind.Prospect && r.Seq >= 0 && (r.Seq > _bizSeen || r.Seq == 0 && r.AtSec > _bizSeenSec)))
            {
                if (!r.Message.IsEmpty) _router?.ShowToast(r.Text, 4f);
                if (r.Seq > 0) _bizSeen = r.Seq; else _bizSeenSec = r.AtSec;
            }
        if (_heroLink != null && Runtime != null && _frames > 30)
        {
            int s = HeroSettlement();
            if (_ownsAny && s != _bizAt) { _bizAt = s; _heroLink.Issue(BusinessDesk.At(s, HeroNow())); }
            int t = HeroTile();
            if (t != _walkTile) { _walkTile = t; _heroLink.Issue(BusinessDesk.Walk(t, HeroNow())); }
        }
        BusinessShots();
    }
    long _bizSeenSec;

    void IssueBiz(HeroCommand c)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(c);
        _bizAcc = 2.0;
    }

    // ------------------------------------------------------------------ the screens

    /// <summary>L: the Ledger (spec 6). It is there once the hero holds anything; empty tables say what each would hold.</summary>
    void OpenLedger() => OpenLedger(LedgerTab.Businesses);

    void OpenLedger(LedgerTab tab)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        RefreshBusiness();
        _router.Push(new LedgerScreen(_router, () => { RefreshBusiness(); return _ledgerView; }, OpenBusiness) { ShotPath = _opt.Panel == "ledger" ? _opt.ShotPath : null, StartTab = tab });
    }

    void OpenBusiness(EntityId id)
    {
        if (_router == null) return;
        _bizId = id; _bizView = null; _bizTask = null;
        RefreshBusiness();
        _router.Push(new BusinessPanel(_router, () => { RefreshBusiness(); return _bizView is { } v && v.Id == _bizId ? v : null; }, BizAct()) { ShotPath = _opt.Panel is "business" or "stall" ? _opt.ShotPath : null });
    }

    void OpenProspect(bool licence = false)
    {
        if (_router == null || Runtime == null || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        RefreshBusiness();
        _router.Push(new ProspectPanel(_router, () => { RefreshBusiness(); return _pxView; }, PxAct()) { ShotPath = _opt.Panel is "prospect" or "licence" ? _opt.ShotPath : null, ShowLicence = licence });
    }

    BusinessActions BizAct() => new()
    {
        Hire = (b, p, w) => IssueBiz(BusinessDesk.Hire(b, p, w, HeroNow())),
        Fire = (b, p) => IssueBiz(BusinessDesk.Fire(b, p, HeroNow())),
        Steward = (b, p) => IssueBiz(BusinessDesk.Steward(b, p, HeroNow())),
        Unsteward = b => IssueBiz(BusinessDesk.Unsteward(b, HeroNow())),
        Orders = (b, spec) => IssueBiz(BusinessDesk.Orders(b, spec, HeroNow())),
        Cash = (b, m) => IssueBiz(BusinessDesk.Cash(b, m, HeroNow())),
        SupplyAll = b => { foreach (var g in SuppliesFor(b)) IssueBiz(BusinessDesk.Supply(b, g.Id, g.Qty, HeroNow())); },
        TakeOutputs = b => { if (_bizView is { } v && v.Id == b) foreach (var s in v.Stock) IssueBiz(BusinessDesk.Take(b, s.Good, s.Qty, HeroNow())); },
        OpenStall = b => { int s = HeroSettlement(); if (s >= 0 && _bizView is { } v) IssueBiz(BusinessDesk.Stall(s, 0, v.Index, HeroNow())); },
        MindHour = subj => { long now = HeroNow(); IssueBiz(BusinessDesk.Mind(subj, now, now + 3600)); },
        Pump = b => IssueBiz(BusinessDesk.Pump(b, HeroNow())),
        SellShare = b => IssueBiz(BusinessDesk.SellShare(b, HeroNow())),
        Foreman = (b, p) => IssueBiz(BusinessDesk.Foreman(b, p, HeroNow())),
        Unforeman = (b, p) => IssueBiz(BusinessDesk.Unforeman(b, p, HeroNow())),
        Manufactory = b => IssueBiz(BusinessDesk.Manufactory(b, HeroNow())),
        EndContract = id => IssueBiz(BusinessDesk.EndContract(id, HeroNow())),
    };

    /// <summary>What the hero carries that the business's recipes take (all of it goes in).</summary>
    IEnumerable<(string Id, long Qty)> SuppliesFor(EntityId b)
    {
        if (_heroLink?.View is not { } v) yield break;
        // HW-13 (G2): the inputs come with the panel's view (built at a barrier); no table is read on the main thread
        if (_bizView is not { } bv || bv.Id != b || bv.Inputs is not { } list) yield break;
        var inputs = new HashSet<string>(list, StringComparer.Ordinal);
        foreach (var g in v.Goods) if (inputs.Contains(g.Id) && g.Qty > 0) yield return (g.Id, g.Qty);
    }

    ProspectActions PxAct() => new()
    {
        Start = m => IssueBiz(BusinessDesk.Start(m, HeroTile(), HeroNow())),
        Cancel = () => IssueBiz(BusinessDesk.Cancel(HeroNow())),
        Write = () => IssueBiz(BusinessDesk.Write(HeroTile(), HeroNow())),
        Read = item => IssueBiz(BusinessDesk.Read(item, HeroNow())),
        Licence = () => IssueBiz(BusinessDesk.Licence(_pxTileOverride >= 0 ? _pxTileOverride : HeroTile(), HeroNow())),
    };

    // ------------------------------------------------------------------ the Q menu

    /// <summary>The kinds of business the room the hero stands in offers (its workshop stations, RoomDetector v2), asked at a barrier.</summary>
    void RefreshHere()
    {
        if (Runtime is not { } rt) return;
        if (_hereTask is { IsCompleted: true } t) { if (t.IsCompletedSuccessfully) _hereKinds = t.Result; _hereTask = null; }
        var (wx, wy) = _world.WorldCell; int z = _world.Snapshot.Hero.Z;
        _hereTask ??= Bar(rt, () =>
        {
            var cw = rt.State.HomeRecords?.Sim.Get<ConstructionWorld>(); var ow = OwnOf(rt);
            if (cw == null || ow == null) return ((List<BusinessKind>, int, string)?)null;
            int st = cw.Rooms.StructureAt(wx, wy, z); if (st <= 0) return (new List<BusinessKind>(), 0, "");
            var stations = ow.StationsOf(st);
            return (ow.KindsFor(stations), st, string.Join(", ", stations.Select(kv => $"{kv.Key} x{kv.Value}")));
        });
    }

    /// <summary>The Q menu's business and prospecting verbs (P5-F).</summary>
    void BusinessVerbs(List<QMenu.Verb> v)
    {
        if (Runtime is not { } rt || _heroLink == null) return;
        RefreshHere();
        v.Add(new(L.T("bz_q_head"), () => { }, Heading: true));
        v.Add(new(L.T("bz_q_ledger"), OpenLedger));
        // open a business in the building the hero may use, of a kind its stations offer
        if (_hereKinds is { Kinds.Count: > 0 } here)
            foreach (var k in here.Kinds.Take(3))
            {
                var kk = k; int s = HeroSettlement();
                v.Add(new(L.F("bz_q_open", L.T("bz_kind_" + k.Id)), () => ConfirmFound(kk.Id, s, here.Structure, here.Tools), s < 0 ? L.T("bz_no_place") : null));
            }
        else v.Add(new(L.T("bz_q_open_none"), () => { }, L.T("bz_q_no_workshop")));
        // the person beside the hero: hire him for a business, as steward, or buy a share of his business
        var near = NearPerson(3000);
        var owned = _ledgerView?.Tab(LedgerTab.Businesses).ToList() ?? [];
        if (near is { } p)
        {
            foreach (var b in owned.Take(2))
            {
                var bid = b.Id; string name = b.Cells[0];
                v.Add(new(L.F("bz_q_hire_for", p.Name, name), () => HireNear(p.Id, bid, false)));
                v.Add(new(L.F("bz_q_steward", p.Name, name), () => HireNear(p.Id, bid, true)));
            }
            v.Add(new(L.F("bz_q_share", p.Name), () => ShareNear(p.Id)));
        }
        // prospecting where the hero stands
        v.Add(new(L.T("px_q_head"), () => { }, Heading: true));
        v.Add(new(L.T("px_q_prospect"), () => OpenProspect()));
        foreach (var m in new[] { ProspectMethod.ReadLand, ProspectMethod.Pan, ProspectMethod.TestPit, ProspectMethod.TrialShaft })
        {
            var mm = m; bool tools = Prospector.HasTools(m, PackToolsLine());
            v.Add(new(L.F("px_q_method", L.T("px_m_" + m.ToString().ToLowerInvariant())), () => IssueBiz(BusinessDesk.Start(mm, HeroTile(), HeroNow())), tools ? null : L.F("px_need_tools", string.Join(" + ", Prospector.Tools(m).Select(g => string.Join("/", g))))));
        }
        v.Add(new(L.T("px_q_licence"), () => OpenProspect(licence: true), _pxView is { OreKnownHere: true } ? null : L.T("px_no_ore_known")));
    }

    void ConfirmFound(string kind, int settlement, int structure, string stations)
    {
        if (_router == null) return;
        long capital = 5000;
        _router.ShowModal(L.T("bz_q_found_title"), L.F("bz_q_found_body", L.T("bz_kind_" + kind), stations, Quotes.Money(capital)),
            (L.T("bz_q_btn_open"), () => IssueBiz(BusinessDesk.Found(kind, settlement, -1, structure, capital, HeroNow()))), (L.T("UI_CANCEL"), null));
    }

    /// <summary>The person index of a live person, asked at the barrier; then the hire (a wage above his own) or the steward command.</summary>
    void HireNear(EntityId person, EntityId biz, bool steward)
    {
        if (Runtime is not { } rt) return;
        long now = HeroNow();
        var t = Bar(rt, () =>
        {
            int i = rt.State.Population.People.IndexOf(person); if (i < 0 || OwnOf(rt) is not { } ow) return ((int, int)?)null;
            return (i, Math.Max(ow.CurrentWage(i, out _) + 50, 350));
        });
        // the command is issued on the main thread (the replay trace records it there)
        t.ContinueWith(r =>
        {
            if (!r.IsCompletedSuccessfully || r.Result is not { } x) return;
            Callable.From(() => IssueBiz(steward ? BusinessDesk.Steward(biz, x.Item1, now) : BusinessDesk.Hire(biz, x.Item1, x.Item2, now))).CallDeferred();
        });
    }

    /// <summary>A share of the business the person beside the hero owns (40%, at its value; refused when it is not his or it is the hero's own).</summary>
    void ShareNear(EntityId person)
    {
        if (Runtime is not { } rt || _router == null) return;
        long now = HeroNow();
        var t = Bar(rt, () =>
        {
            int i = rt.State.Population.People.IndexOf(person);
            var b = rt.State.Production?.Businesses.FirstOrDefault(x => x.Owner == i && x.State == BusinessState.Open);
            if (b == null || OwnOf(rt) is not { } ow) return ((EntityId, string, long)?)null;
            return (b.Id, ow.NameOf(b), Valuation.SharePrice(ow.ValueOf(b), 400));
        });
        t.ContinueWith(r =>
        {
            if (!r.IsCompletedSuccessfully || r.Result is not { } s) { Callable.From(() => _router?.ShowToast(L.T("bz_q_no_business"))).CallDeferred(); return; }
            Callable.From(() => _router?.ShowModal(L.T("bz_q_share_title"), L.F("bz_q_share_body", s.Item2, Quotes.Money(s.Item3)),
                (L.T("bz_q_btn_buy"), () => IssueBiz(BusinessDesk.BuyShare(s.Item1, 400, s.Item3 * 11 / 10, now))), (L.T("UI_CANCEL"), null))).CallDeferred();
        });
    }

    // ------------------------------------------------------------------ dev screenshots: --shot-panel ledger | business | stall | prospect | licence

    int _bizShot, _bizShotAt;

    /// <summary>
    /// Sets up a little of everything at the barrier (dev only, never play: a smithy owned outright with its staff and a steward and a season's books, a share of a baker's, a stall, a mine on the
    /// nearest vein, a walk and pits around the hero, a report), then opens the screen the option names.
    /// </summary>
    void BusinessShots()
    {
        if (_opt.Panel is not ("ledger" or "business" or "stall" or "prospect" or "licence") || _heroLink == null || Runtime is not { } rt) return;
        if (_bizShot == 0 && _frames >= 60 && rt.HourDone >= _world.Clock.Now.TotalHours) { _bizShot = 1; _bizShotAt = _frames; DevBusinesses(rt); }
        if (_bizShot == 1 && _frames >= _bizShotAt + 90) { _bizShot = 2; RefreshBusiness(); }
        if (_bizShot != 2 || _frames != _bizShotAt + 150) return;
        _bizShot = 3;
        switch (_opt.Panel)
        {
            case "ledger": OpenLedger(); break;
            case "business": case "stall": if (_ledgerView?.Tab(LedgerTab.Businesses).FirstOrDefault() is { } row) OpenBusiness(row.Id); break;
            case "prospect": OpenProspect(); break;
            case "licence": OpenProspect(licence: true); break;
        }
    }

    /// <summary>Dev (--ng-shot business): sets up the holdings for the screenshots.</summary>
    public void DebugBusinessShot(string scene) { if (Runtime is { } rt) { int v = DevBusinesses(rt); if (scene == "licence" && v >= 0) _pxTileOverride = v; RefreshBusiness(); } }

    /// <summary>Dev: opens the screen of a scene once the views are in (a business's panel, the stall's, the prospecting panel with or without the licence petition, the Ledger).</summary>
    public void DebugBusinessOpen(string scene)
    {
        RefreshBusiness();
        switch (scene)
        {
            case "business": if (_ledgerView?.Tab(LedgerTab.Businesses).FirstOrDefault() is { } row) OpenBusiness(row.Id); break;
            case "stall": OpenLedger(LedgerTab.Stalls); break;
            case "prospect": OpenProspect(); break;
            case "licence": OpenProspect(licence: true); break;
            default: OpenLedger(); break;
        }
    }

    int DevBusinesses(WorldRuntime rt)
    {
        int s = HeroSettlement(); int tile = HeroTile(); long now = HeroNow();
        var r = rt.AtBarrier(() =>
        {
            var st = rt.State; var ow = OwnOf(rt); var prod = st.Production; if (ow == null || prod == null || st.Hero == null) return -1;
            int town = s >= 0 ? s : 0; int day = (int)(now / 86400);
            var e = st.Economy!; var hero = EconomyState.HeroOwner;
            var purse = e.Ledger.Open(PurseKind.Person, hero);
            e.Ledger.Mint(purse, Coin.FromCrowns(1000), SourceReason.Seed);                          // dev faucet (the shots only)
            var kind = prod.Content.Kind("smithy");
            var b = prod.NewBusiness(kind, town, -1, st.Hero.PersonIndex); b.OwnerWorks = false;
            foreach (var (k, n) in kind.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Station { Kind = k });
            e.Ledger.Transfer(purse, b.Purse, new Coin(30_000), TransferReason.Gift); b.Capital = 30_000; b.CashStart = 30_000;
            var h = ow.Hold(b); h.FoundedDay = day - 30;
            b.Store.Create(e.Goods.IdOf("iron_bar"), 80_000, Ravensguard.Sim.Goods.CreateReason.Import);
            h.History.Add(new SeasonRow(day / 15 - 2, 31_200, 10_500, 14_000, 6_700, 2_000));
            h.History.Add(new SeasonRow(day / 15 - 1, 42_800, 12_000, 20_500, 10_300, 4_100));
            var pt = st.Population.People;
            var idle = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) is >= 20 and < 50
                && pt.Occupation[i] is not (Occupation.Lord or Occupation.Priest or Occupation.Mayor or Occupation.GuildMaster) && !prod.Businesses.Any(x => x.Owner == i)).OrderBy(i => pt.Occupation[i] == Occupation.None ? 0 : 1).ThenBy(i => i).Take(4).ToList();
            if (idle.Count >= 3)
            {
                pt.SetSkill(idle[0], 0, Ravensguard.Sim.People.Skill.Trading, 45); pt.SetSkill(idle[0], 1, Ravensguard.Sim.People.Skill.Leadership, 30);
                var a1 = ow.HireSteward(b.Id, idle[0], day); var a2 = ow.Hire(b.Id, idle[1], 900, day); var a3 = ow.Hire(b.Id, idle[2], 900, day);
                GD.Print($"[business] dev hires: {a1.Key} {a2.Key} {a3.Key}");
            }
            ow.Orders(b.Id, "recipe=forge_nails");
            var npc = prod.Businesses.FirstOrDefault(x => x.State == BusinessState.Open && x.Owner >= 0 && x.Owner != st.Hero.PersonIndex && prod.KindOf(x).Id == "bakery");
            if (npc != null) ow.BuyShare(npc.Id, 400, 0, day);
            if (ow.Land != null) { ow.Land.LeaseStall(hero, PurseKind.Person, town, 0, false, day, (w, t, su, c, d) => default); ow.OpenStall(town, 0, b.Index); if (idle.Count >= 3) ow.StallMinder(LandIds.StallSubject(town, 0), idle[2]); }
            // knowledge around the hero, and a vein's ore for the licence picture
            var rules = rt.State.Content?.All<Ravensguard.Content.ProspectRulesDef>().FirstOrDefault() ?? new Ravensguard.Content.ProspectRulesDef { Id = "d" };
            int N = RegionData.Size;
            for (int dy = -8; dy <= 8; dy++) for (int dx = -8; dx <= 8; dx++)
                {
                    int t = Math.Clamp(tile / N + dy, 0, N - 1) * N + Math.Clamp(tile % N + dx, 0, N - 1);
                    foreach (var row in Prospector.Walk(ow.Truth, rules, st.Seed, hero, t, 40, day)) ow.Book.Prospects.Learn(row);
                    if ((dx + dy) % 3 == 0) foreach (var row in Prospector.TestPit(ow.Truth, rules, st.Seed, hero, t, 40, day)) ow.Book.Prospects.Learn(row);
                }
            var (_, hints) = Prospector.ReadLand(ow.Truth, rules, st.Seed, hero, tile, 60, day); ow.Book.Prospects.Hints.AddRange(hints);
            ow.Book.Prospects.Pans.Add(new PanFinding(hero, tile, [0, 1], day));
            int vt = -1;
            foreach (var v in st.Population.Region.Veins) { int t = RegionData.Index(v.TileX, v.TileY); if (ProspectTruth.IsOre(v.Type) && v.Type is not (Ravensguard.Sim.WorldGen.MineralType.Silver or Ravensguard.Sim.WorldGen.MineralType.Gold) && ow.RightHolder(t, v.Type) != null) { vt = t; break; } }
            if (vt >= 0)
            {
                foreach (var row in Prospector.TrialShaft(ow.Truth, rules, st.Seed, hero, vt, 60, day)) ow.Book.Prospects.Learn(row);
                for (int k = 1; k < 6; k++) foreach (var row in Prospector.Walk(ow.Truth, rules, st.Seed, hero, vt + k, 40, day)) ow.Book.Prospects.Learn(row);
                ow.WriteReport(vt, now);
            }
            return vt;
        }).GetAwaiter().GetResult();
        if (_opt.Panel == "licence" && r >= 0) _pxTileOverride = r;
        GD.Print($"[business] dev businesses set up (vein tile {r})");
        return r;
    }
}
