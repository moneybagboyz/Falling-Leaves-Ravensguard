using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Game.World.Crawl;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// [exploration] Going down into a site (P4-H, exploration-and-sites 4.1, 7): E at a cave mouth, a ruin's stair, a mine's adit or a barrow's door generates the site on first visit
/// (on a worker, behind a fade; later visits find it as it was left) and attaches the <see cref="SiteCrawl"/> to the live world: the hero's feet belong to it, the surface keeps
/// running, the crawl view covers the street. A fight the crawl starts is fought in the combat stance view like a road fight; the chronicle gets the crawl's entries (a taboo broken,
/// a monster slain); coming back up the stairs detaches it. A save while underground keeps the crawl (the "sites" section) and a load puts the hero back where he stood.
/// </summary>
public partial class StreetView
{
    SiteCrawl? _crawl;
    CrawlView? _crawlView;
    CanvasLayer? _crawlLayer;
    Site? _crawlSite;
    SiteRecord? _crawlRecord;
    Task<SiteRecord>? _siteGen;
    Site? _siteGenSite;
    bool _siteFightOpen, _crawlResumeChecked;

    /// <summary>True while the hero is underground (the crawl view has the keys and the screen).</summary>
    bool Underground => _crawl != null;

    /// <summary>The site whose entrance the hero stands at (within 6 m on the surface), or null.</summary>
    Site? SiteAtHero()
    {
        if (_source?.Region is not { } region || _world.Snapshot.Hero.Z != 0) return null;
        var (wx, wy) = _world.WorldCell;
        Site? best = null; long bd = 6L * 6;
        foreach (var s in region.Sites)
        {
            if (s.Kind is not (SiteKind.Ruin or SiteKind.Cave or SiteKind.Mine or SiteKind.Barrow)) continue;
            long dx = s.X - wx, dy = s.Y - wy, d = dx * dx + dy * dy;
            if (d <= bd) { bd = d; best = s; }
        }
        return best;
    }

    /// <summary>E at a site's entrance: down into it (the first visit generates it behind a fade).</summary>
    bool TryEnterSite()
    {
        if (Underground || _siteGen != null || _fightView != null) return false;
        var site = SiteAtHero();
        if (site == null || Runtime is not { } rt || _source?.Content == null) return false;
        if (_ride is { IsMounted: true } or { IsDriving: true }) { _router?.ShowToast(L.T("ex_ui_no_horses"), 4f); return true; }   // E8: mounts stay at the mouth
        var book = rt.State.Sites ??= new SiteBook();
        var sc = SiteWiring.ContentOf(_source.Content);
        var info = SiteHistoryInfo.Of(site, rt.State.History, _world.Clock.Now.Year);
        ulong seed = rt.State.Seed;
        var rec = book.Record(site.Id.Value);
        _siteGenSite = site;
        if (rec.Generated) { OpenCrawl(site, rec, null); return true; }
        _router?.ShowToast(L.T(site.Kind == SiteKind.Cave ? "ex_ui_fade_cave" : "ex_ui_fade_stone"), 3f);
        _siteGen = Task.Run(() => book.Open(sc, info, seed));
        return true;
    }

    /// <summary>The hero's crawl member: the kit the road fights use, the senses and hands of the record, the torches in the pack, the street torch if it was lit.</summary>
    CrawlMember HeroMember(SiteContent sc)
    {
        var fc = FightContentOf()!;
        var kit = HeroKit(fc);
        var rec = Runtime?.State.Hero ?? _hero;
        int Sk(Skill s) => rec?.Skill(HeroRecord.SkillId(s)) ?? 20;
        int perception = rec == null ? 50 : rec.Attribute(HeroAttr.Perception) * 50 / Math.Max(1, HeroRecord.BaselineAttribute);
        int torches = (int)(_heroLink?.View?.Goods.Where(g => g.Id == "torch").Sum(g => g.Qty) ?? 3);
        var m = new CrawlMember
        {
            Person = EconomyState.HeroOwner.Value, Name = kit.Name, Kit = kit, Perception = perception, Lockpicking = Sk(Skill.Lockpicking), Athletics = Sk(Skill.Athletics), Stealth = Sk(Skill.Stealth),
            SpareTorches = torches, Bandages = (int)(_heroLink?.View?.Goods.Where(g => g.Id == "bandage").Sum(g => g.Qty) ?? 1),
            GearG = (int)Math.Min(int.MaxValue, _heroLink?.View?.MassG ?? 8000), CapacityG = sc.K.CarryBaseKg * 1000 + 2500 * kit.Strength / 125,   // 15 kg + 2.5 kg a strength point (the fight's rule)
        };
        if (_world.Torch) m.Light = CarriedLight.Torch(sc.K);
        m.Light.Lit = _world.Torch;
        return m;
    }

    /// <summary>
    /// The party members who follow the hero (order Follow, up to three, in join order) as crawl members (P4 close-out): kitted as the road fights' rows (a hired hand the <c>guard</c>
    /// row, a recruited companion the <c>traveller</c>), the person's own skills where the person table knows them, no light of their own (they walk in the hero's), treating their own wounds.
    /// </summary>
    List<CrawlMember> PartyMembers(Ravensguard.Sim.Combat.Melee2.FightContent fc)
    {
        var list = new List<CrawlMember>();
        if (_heroLink?.View is not { } v || Runtime is not { } rt) return list;
        var pt = rt.State.Population.People;
        foreach (var f in v.Squad.Where(m => m.Active && m.Order == Ravensguard.Sim.Trade.SquadOrder.Follow).OrderBy(m => m.JoinedDay).ThenBy(m => m.Person.Value).Take(3))
        {
            var row = Ravensguard.Sim.Combat.Melee2.FightSetup.KitDef(fc, f.Origin == Ravensguard.Sim.Trade.FollowerOrigin.Hired ? "guard" : "traveller", "traveller");
            var kit = Ravensguard.Sim.Combat.Melee2.FightSetup.Roll(fc, row, rt.State.Seed, f.Person.Value, f.Name, 0, 0, 0, Ravensguard.Sim.Combat.Melee2.FightMath.One, 0);
            int i = pt.IndexOf(f.Person);
            int Sk(Skill s, int d) => i >= 0 ? Math.Max(d, pt.SkillLevelOf(i, s)) : d;
            list.Add(new CrawlMember
            {
                Person = f.Person.Value, Name = f.Name, Kit = kit, Perception = 40, Lockpicking = Sk(Skill.Lockpicking, 10), Athletics = Sk(Skill.Athletics, 30), Stealth = Sk(Skill.Stealth, 30),
                AutoTreat = true, Bandages = 1, SpareTorches = 0,
            });
        }
        return list;
    }

    void OpenCrawl(Site site, SiteRecord rec, CrawlSave? resume)
    {
        if (Runtime is not { } rt || _source?.Content == null || FightContentOf() is not { } fc || rec.Map == null) return;
        var book = rt.State.Sites!;
        var sc = SiteWiring.ContentOf(_source.Content);
        var ledger = rt.State.Wounds ??= new Ravensguard.Sim.Combat.Wounds.WoundLedger(rt.State.Seed, _world.Clock.Now.Seconds);
        SiteCrawl crawl;
        if (resume != null) crawl = SiteCrawl.Restore(resume, rec, sc, fc, ledger, rt.State.Seed);
        else
        {
            crawl = new SiteCrawl(rec.Map, sc, fc, ledger, _world.Clock.Now.Seconds, rt.State.Seed, rec.Known);
            crawl.Add(HeroMember(sc));
            foreach (var m in PartyMembers(fc)) crawl.Add(m);          // P4 close-out: the party goes down with the hero
        }
        crawl.OwnsFight = false; crawl.Sync = book.Sync;
        if (_source.Region is { } region) crawl.Surgeon = SurgeonInfo.Nearest(region, site.X, site.Y);
        _crawl = crawl; _crawlSite = site; _crawlRecord = rec;
        lock (book.Sync) { book.ActiveSite = site.Id.Value; book.ActiveCrawl = null; }
        rt.State.SiteSource = () => _crawl is { } c ? SiteSave.Capture(book, c) : null;
        if (resume == null) _rec.EnterSite(crawl); else _world.AttachSite(crawl);
        _crawlLayer = new CanvasLayer { Layer = 5, Name = "CrawlLayer" };
        AddChild(_crawlLayer);
        _crawlView = new CrawlView(crawl, i => _rec.SiteInput(i)) { Name = "CrawlView", SiteName = site.Name, OpenMenu = OpenCrawlMenu };
        _crawlLayer.AddChild(_crawlView);
    }

    /// <summary>Once per sim tick: the generation coming back, the fight handed to the combat stance and back, the crawl's chronicle entries, the way out.</summary>
    void CrawlFrame()
    {
        if (!_crawlResumeChecked) { _crawlResumeChecked = true; ResumeCrawl(); }
        SiteResults();
        if (_siteGen is { IsCompleted: true } g)
        {
            _siteGen = null;
            if (g.IsCompletedSuccessfully && _siteGenSite is { } s) OpenCrawl(s, g.Result, null);
            else _router?.ShowToast(L.T("ex_ui_gen_failed"), 4f);
        }
        if (_crawl is not { } c) return;
        if (c.Fight != null && _fightView == null && _world.Fight != null && c.HeroFighter >= 0)
        {
            OpenFightView(_world.Fight, c.HeroFighter);
            _siteFightOpen = true;
            if (_fightView != null) { _fightView.Log.Info(c.HeroFighter).Name = _hero?.Name ?? c.Hero.Name; _fightView.Ground = new Color(0.16f, 0.13f, 0.11f); }   // stone underfoot
        }
        if (_siteFightOpen && _world.Fight == null)
        {
            _siteFightOpen = false;
            if (_fightView != null) { var f = _fightView.W; FightEnded(f); }
            _fightView?.QueueFree(); _fightLayer?.QueueFree(); _fightView = null; _fightLayer = null;
            if (Runtime is { } rt) rt.State.FightSource = null;
        }
        if (c.Chronicle.Count > 0 && Runtime is { } rt2)
        {
            var entries = new List<(string, Dictionary<string, string>)>();
            foreach (var (type, data) in c.Chronicle)
            {
                if (_crawlSite != null) data["site_name"] = _crawlSite.Name;
                if (_crawlSite != null && _source?.Region is { } rg && rg.Settlements.Count > 0)
                    data["settlement"] = rg.Settlements.OrderBy(t => (long)(t.CenterX - _crawlSite.X) * (t.CenterX - _crawlSite.X) + (long)(t.CenterY - _crawlSite.Y) * (t.CenterY - _crawlSite.Y)).First().Name;   // where the tale is told
                entries.Add((type, data));
            }
            c.Chronicle.Clear();
            var when = _world.Clock.Now;
            // numbered and added at the barrier: the world's chronicle and id allocator belong to the tick worker (review, game finding 1)
            _ = Bar(rt2, () =>
            {
                foreach (var (type, data) in entries)
                    rt2.State.Chronicle.Add(new ChronicleEntry(rt2.State.Ids.Allocate(IdKind.Event), when, type, [EconomyState.HeroOwner], [new Cause(CauseKind.Other, null, type)], data));
                return entries.Count;
            });
        }
        // resting below fast-forwards (P4 close-out): 8x while the party rests, back to 1x the moment the rest stops (any interrupt stops it in the crawl); the speed changes are recorded
        if (c.Resting && !_crawlRestFast && c.Fight == null) { _crawlRestFast = true; _rec.SetWantedSpeed(SimSpeed.X8); _rec.SetSpeed(SimSpeed.X8); }
        else if (_crawlRestFast && (!c.Resting || c.Fight != null)) { _crawlRestFast = false; _rec.SetWantedSpeed(SimSpeed.X1); _rec.SetSpeed(SimSpeed.X1); }
        if (c.Ended) { if (_crawlRestFast) { _crawlRestFast = false; _rec.SetWantedSpeed(SimSpeed.X1); _rec.SetSpeed(SimSpeed.X1); } LeaveCrawl(); }
    }

    bool _crawlRestFast;

    void LeaveCrawl()
    {
        var c = _crawl; if (c == null) return;
        _rec.LeaveSite();
        if (Runtime is { } rt && rt.State.Sites is { } book && _crawlRecord is { } rec)
        {
            lock (book.Sync)
            {
                rec.Visits++;
                if (c.ClearedDay >= 0) rec.Cleared = c.ClearedDay;
                if (c.TabooBroken) rec.Curse = true;
                foreach (var s in c.Slain) { book.Slain.Add(s); book.Renown += 10; }
                book.ActiveSite = 0; book.ActiveCrawl = null;
            }
            rt.State.SiteSource = null;
        }
        _rec.SetTorch(c.Hero.Light.Lit);
        // the bandages the hero carried down came from the pack: what he used below (his own dressing) leaves it at the barrier
        int packBandages = (int)(_heroLink?.View?.Goods.Where(g => g.Id == "bandage").Sum(g => g.Qty) ?? 0);
        if (!c.Hero.Dead && _heroLink != null && packBandages > c.Hero.Bandages)
            _heroLink.Issue(Ravensguard.Sim.Combat.Aftermath.AftermathDesk.DressCommand(packBandages - c.Hero.Bandages, 0, _world.Clock.Now.Seconds));
        if (c.Hero.Dead) { _router?.ShowToast(L.T("ex_ui_died_below"), 6f); HeroDied("su_cause_below"); }   // the heir takes over (StreetView.Succession.cs)
        else if (_heroLink != null && _crawlSite != null && (c.Hero.Loot.Count > 0 || c.Hero.TorchesUsed > 0))
            SiteCommand(SiteDesk.TakeCommand(_crawlSite.Id.Value, c.Hero.Loot, c.Hero.TorchesUsed, _world.Clock.Now.Seconds));   // the haul into the pack and the burnt torches out, at the barrier
        else _router?.ShowToast(L.T("ex_ui_left"), 5f);
        _crawlView?.QueueFree(); _crawlLayer?.QueueFree();
        _crawlView = null; _crawlLayer = null; _crawl = null; _crawlSite = null; _crawlRecord = null;
    }

    /// <summary>A save made underground: the hero is put back into the crawl where he stood.</summary>
    void ResumeCrawl()
    {
        if (Runtime is not { } rt || rt.State.Sites is not { ActiveCrawl: { } cs } book || _source?.Region is not { } region) return;
        var site = region.Sites.FirstOrDefault(s => s.Id.Value == cs.Site);
        if (site == null || !book.Records.TryGetValue(cs.Site, out var rec) || rec.Map == null) return;
        OpenCrawl(site, rec, cs);
    }

    /// <summary>An old battlefield within 150 m of the hero, or a hoard's marker within 6 m (the surface sites, 4.2).</summary>
    Site? SurfaceSiteAtHero()
    {
        if (_source?.Region is not { } region || _world.Snapshot.Hero.Z != 0) return null;
        var (wx, wy) = _world.WorldCell;
        foreach (var s in region.Sites)
        {
            long dx = s.X - wx, dy = s.Y - wy, d = dx * dx + dy * dy;
            if (s.Kind == SiteKind.Battlefield && d <= 150L * 150 || s.Kind == SiteKind.Hoard && d <= 6L * 6) return s;
        }
        return null;
    }

    /// <summary>The Q menu's verbs at a surface site: Search the field (60 sim-seconds a cell), Dig here (a hoard).</summary>
    void SurfaceSiteVerbs(List<QMenu.Verb> v)
    {
        if (SurfaceSiteAtHero() is not { } s || Runtime == null || _source?.Content == null) return;
        if (s.Kind == SiteKind.Battlefield)
            v.Insert(1, new QMenu.Verb(L.T("ex_q_search"), () => StartSiteJob(false), _heroLink == null ? L.T("MAP_NONE") : null));
        else
            v.Insert(1, new QMenu.Verb(L.T("ex_q_dig"), () => StartSiteJob(true), _heroLink == null ? L.T("MAP_NONE") : null));
    }

    /// <summary>Search the field or dig the hoard: the clock time passes as a camp job (at 8x), then the find is rolled at the barrier (<see cref="SiteDesk"/>) and goes into the pack.</summary>
    void StartSiteJob(bool dig)
    {
        if (_heroLink == null) return;
        if (_travel is not { } t) { SiteJobDone(dig ? CampJobKind.Dig : CampJobKind.Search); return; }
        _router?.ShowToast(L.T(dig ? "ex_job_dig" : "ex_job_search"), 3f);
        StartCampJob(t, dig ? CampJobKind.Dig : CampJobKind.Search, dig ? SiteDesk.DigSec : SiteDesk.SearchSec);
    }

    /// <summary>The search's or the dig's time is up: the command for the site the hero stands at (none if he walked off it).</summary>
    void SiteJobDone(CampJobKind kind)
    {
        _rec.SetWantedSpeed(SimSpeed.X1);
        if (SurfaceSiteAtHero() is not { } s) { _router?.ShowToast(L.T(kind == CampJobKind.Dig ? "ex_ui_dig_none" : "ex_ui_search_none"), 4f); return; }
        long now = _world.Clock.Now.Seconds;
        SiteCommand(kind == CampJobKind.Dig ? SiteDesk.DigCommand(s.Id.Value, now) : SiteDesk.SearchCommand(s.Id.Value, now));
    }

    readonly HashSet<long> _siteSeqs = [];

    /// <summary>Sends a site command (recorded for replay by the hero link) and shows its answer when the barrier has applied it.</summary>
    void SiteCommand(Ravensguard.Sim.Trade.HeroCommand cmd)
    {
        if (_heroLink == null) return;
        _siteSeqs.Add(_heroLink.Issue(cmd).Seq);
    }

    /// <summary>Per frame: the answers of the site commands (what was found, what was carried out).</summary>
    void SiteResults()
    {
        if (_siteSeqs.Count == 0 || _heroLink?.View is not { } v) return;
        foreach (var r in v.Results)
            if (_siteSeqs.Remove(r.Seq)) _router?.ShowToast(r.Text, 5f);
    }

    /// <summary>Q underground: the crawl's verbs (exploration-and-sites 7).</summary>
    void OpenCrawlMenu()
    {
        if (_router == null || _crawl is not { } c) return;
        var h = c.Hero;
        var v = new List<QMenu.Verb>
        {
            new(L.T("ex_q_head"), () => { }, Heading: true),
            new(L.T("ex_q_retrace"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.Retrace))),
            new(L.T("ex_q_mark"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.Mark))),
            new(L.T("ex_q_map"), () => _crawlView?.OpenMap(h.Level)),
            new(L.T("ex_q_treat"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.Treat)), h.Book.RateMlPerS <= 0 ? L.T("ex_q_why_not_bleeding") : null),
            new(L.T("ex_q_ladder"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.PullLadder)), c.Map.PortalAt(h.Cell) is { Kind: PortalKindSite.Ladder } ? null : L.T("ex_q_why_no_ladder")),
            new(L.T("ex_q_rest"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.Rest)), c.CanRest(h) ? null : L.T("ex_q_why_air")),
            new(L.T("ex_q_drag"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.DragOut)), c.Members.Any(m => m != h && m.Unconscious) ? null : L.T("ex_q_why_nobody")),
            new(L.T("ex_q_leave"), () => _rec.SiteInput(new CrawlInput(CrawlInputKind.Leave)), h.Cell == c.Map.Mouth ? null : L.T("ex_q_why_not_mouth")),
        };
        _router.Push(new QMenu(_router, L.T("ex_q_title"), v));
    }
}
