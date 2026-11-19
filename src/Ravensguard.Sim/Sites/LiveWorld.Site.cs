using Ravensguard.Sim.Core;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Live;

/// <summary>
/// [exploration] The hero underground (exploration-and-sites 4.4, 5; P4-H): while a <see cref="SiteCrawl"/> is attached the hero's feet belong to it (as to a fight), the surface
/// keeps running, and every live tick steps the crawl <c>speed</c> times (one crawl step is one game second, so a torch lasts its 1,200 sim-seconds at any speed, EX-5). A fight
/// the crawl starts is attached as the live fight (one game second a step, the clock held at 1x) and handed back when it is over.
/// </summary>
public sealed partial class LiveWorld
{
    public SiteCrawl? Site { get; private set; }
    /// <summary>Rebuilds a crawl from a site id when a replay reaches the tick it began (the game keeps the crawl's starting state).</summary>
    public Func<ulong, SiteCrawl?>? SiteFactory { get; set; }

    public void AttachSite(SiteCrawl crawl) { Site = crawl; crawl.OwnsFight = false; _usePending = false; }

    public void DetachSite()
    {
        if (Site?.Fight != null && Fight != null) EndFight();
        Site = null;
    }

    public void EnterSiteById(ulong site) { if (SiteFactory?.Invoke(site) is { } c) AttachSite(c); }

    /// <summary>One crawl input (applied at the crawl's next step).</summary>
    public void SiteInput(CrawlInput i) => Site?.Input(i);

    /// <summary>The crawl's share of the tick: <c>speed</c> crawl steps (one during a fight), the fight handed over and back.</summary>
    private void SiteTick()
    {
        var c = Site!;
        int n = Fight != null ? (Clock.Speed == SimSpeed.Paused ? 0 : 1) : (int)Clock.Speed;
        for (int i = 0; i < n && !c.Ended; i++)
        {
            c.Step();
            if (c.Fight != null && Fight == null) { AttachFight(c.Fight, c.HeroFighter); break; }
            if (c.Fight == null && Fight != null) { EndFight(); break; }
        }
    }
}
