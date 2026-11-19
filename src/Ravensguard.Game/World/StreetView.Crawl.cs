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
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
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
            foreach (var (type, data) in c.Chronicle)
            {
                if (_crawlSite != null) data["site_name"] = _crawlSite.Name;
                if (_crawlSite != null && _source?.Region is { } rg && rg.Settlements.Count > 0)
                    data["settlement"] = rg.Settlements.OrderBy(t => (long)(t.CenterX - _crawlSite.X) * (t.CenterX - _crawlSite.X) + (long)(t.CenterY - _crawlSite.Y) * (t.CenterY - _crawlSite.Y)).First().Name;   // where the tale is told
                rt2.State.Chronicle.Add(new ChronicleEntry(rt2.State.Ids.Allocate(IdKind.Event), _world.Clock.Now, type, [EconomyState.HeroOwner], [new Cause(CauseKind.Other, null, type)], data));
            }
            c.Chronicle.Clear();
        }
        if (c.Ended) LeaveCrawl();
    }

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
        int value = c.Hero.Loot.Sum(i => i.ValueMites);
        if (c.Hero.Dead) _router?.ShowToast(L.T("ex_ui_died_below"), 6f);
        else _router?.ShowToast(value > 0 ? L.F("ex_ui_left_loot", c.Hero.Loot.Count, Ravensguard.Sim.Trade.Quotes.Money(value)) : L.T("ex_ui_left"), 5f);
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
