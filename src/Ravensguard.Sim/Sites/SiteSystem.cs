using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>The hour's weather above the region's sites (heavy rain or a storm), for the flood rule; the runtime installs it next to the <see cref="SiteBook"/>.</summary>
public sealed class SiteWeather(Func<long, bool> heavyAt)
{
    public bool HeavyAt(long hour) => heavyAt(hour);
}

/// <summary>
/// The abstract tier of the sites (exploration-and-sites 5): every hour it notes the weather; at the day boundary it floods mines after storms (4%), lets bands move into cleared
/// sites after 20 days (2% x pressure/500), and brings back the fled after three days; once a year the scavengers take 5% from one in ten untouched ruins; once, at the
/// start, it tells the legends of the lairs (the monsters are rumours until met). A site the hero is in is never touched (the bubble rule). Chronicle entries go through the
/// region outbox. A no-op without a <see cref="SiteBook"/> service.
/// </summary>
[AbstractSystem]
public sealed class SiteSystem : AbstractSystem
{
    public const int OrderSlot = 7_060;
    public SiteSystem() : base("sites", TickPhase.H7Outbox, OrderSlot) { }

    public override void Step(TickContext ctx)
    {
        var book = ctx.World.Get<SiteBook>();
        var content = ctx.World.Get<SiteContent>();
        var region = ctx.World.Population.Region;
        if (book == null || content == null || region == null) return;
        var weather = ctx.World.Get<SiteWeather>();
        book.NoteHour(ctx.Hour, weather?.HeavyAt(ctx.Hour) == true);
        lock (book.Sync)
        {
            if (!book.LegendsSeeded) { book.LegendsSeeded = true; Legends(ctx, region); }
            if (ctx.HourOfDay != 23) return;
            Daily(ctx, book, content, region);
            if (ctx.Time.DayOfYear == Core.TimeConst.DaysPerYear) Yearly(ctx, book, content, region);
        }
    }

    /// <summary>The lairs' legends (4.9): a rumour seed per monster lair, told in the nearest settlement.</summary>
    private static void Legends(TickContext ctx, Region region)
    {
        foreach (var s in region.Sites)
        {
            if (!s.MonsterLair) continue;
            int near = Nearest(region, s);
            ctx.Outbox.Chronicle(ctx.Time, "lair_legend", [s.Id], [new Cause(CauseKind.Other, s.HistoryEvent == EntityId.None ? null : s.HistoryEvent, "an old tale")],
                ("site", s.Name), ("monster", s.LairKind), ("settlement", near >= 0 ? region.Settlements[near].Name : ""), ("text_key", "ex_chron_legend"));
        }
    }

    private static int Nearest(Region region, Site s)
    {
        long bd = long.MaxValue; int best = -1;
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            var t = region.Settlements[i]; long dx = t.CenterX - s.X, dy = t.CenterY - s.Y;
            if (dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; best = i; }
        }
        return best;
    }

    private static void Daily(TickContext ctx, SiteBook book, SiteContent c, Region region)
    {
        int day = (int)ctx.Day;
        int heavy = book.HeavyOfLastDay;
        foreach (var s in region.Sites)
        {
            if (s.Id.Value == book.ActiveSite) continue;
            if (s.Kind == SiteKind.Mine && s.Mine is MineState.Active or MineState.Abandoned && SiteBook.FloodRolls(ctx.Seed, day, s.Id.Value, heavy, c.K))
            {
                var r = book.Record(s.Id.Value);
                r.FloodSince = (long)(day + 1) * 86400;
                if (r.Map is { } m) m.FloodCm[m.Levels - 1] = Math.Max(m.FloodCm[m.Levels - 1], c.K.FloodHours * c.K.FloodRiseCmPerHour);
                int near = Nearest(region, s);
                ctx.Outbox.Chronicle(ctx.Time, "mine_flooded", [s.Id], [new Cause(CauseKind.Weather, null, $"heavy rain for {heavy} hours")],
                    ("site", s.Name), ("settlement", near >= 0 ? region.Settlements[near].Name : ""), ("text_key", "ex_chron_flooded"));
            }
        }
        foreach (var (id, r) in book.Records)
        {
            if (id == book.ActiveSite || r.Map is not { } m) continue;
            // the fled come back after three days
            foreach (var n in m.Inhabitants)
                if (n.State == InhabitantState.Fled && n.FledDay >= 0 && day - n.FledDay >= c.K.FledReturnDays) { n.State = InhabitantState.Home; var (l, x, y) = m.At(n.Home); n.Level = l; n.X = x * 1000 + 500; n.Y = y * 1000 + 500; }
            // a beast at its lair heals a little each hour (1% of its blood an hour: 24% a day)
            foreach (var n in m.Inhabitants) if (n.LossPm > 0 && n.State is not InhabitantState.Dead) n.LossPm = Math.Max(0, n.LossPm - 24 * (c.Monsters.GetValueOrDefault(n.Monster)?.RecoverPmPerHour ?? 0));
            bool empty = !m.Inhabitants.Any(n => n.Hostile && n.State is not (InhabitantState.Dead or InhabitantState.Fled or InhabitantState.Away));
            if (r.Cleared < 0 || !empty || day < r.Cleared + c.K.ReoccupyAfterDays) continue;
            if (!SiteBook.ReoccupyRolls(ctx.Seed, day, id, c.K.ReoccupyPressure, c.K)) continue;
            var band = SiteBook.NewBand(c, m, ctx.Seed, day, 1000 * (r.Reoccupied + 1), 0);
            m.Inhabitants.RemoveAll(n => n.State == InhabitantState.Dead);
            m.Inhabitants.AddRange(band);
            r.Reoccupied++; r.Cleared = -1;
            var site = region.Sites.FirstOrDefault(x => x.Id.Value == id);
            ctx.Outbox.Chronicle(ctx.Time, "site_reoccupied", site == null ? [] : [site.Id], [new Cause(CauseKind.Other, null, "a band of outlaws moved in")],
                ("site", site?.Name ?? ""), ("band", band.Count.ToString()), ("text_key", "ex_chron_reoccupied"));
        }
    }

    /// <summary>The scavengers (4.3): once a game year one in ten untouched ruins loses 5% of what is left (stream 0x4B9, key year and site, draws from 1000).</summary>
    private static void Yearly(TickContext ctx, SiteBook book, SiteContent c, Region region)
    {
        int year = ctx.Year;
        foreach (var s in region.Sites)
        {
            if (s.Kind != SiteKind.Ruin || s.Id.Value == book.ActiveSite) continue;
            if (DetRng.Range(ctx.Seed, year, s.Id.Value, RngStreams.Scavenge, 1000, 0, 1000) >= c.K.ScavengeSitesPm) continue;
            var r = book.Record(s.Id.Value);
            if (r.Map is { } m) { if (m.Containers.All(x => !x.Opened)) SiteBook.Scavenge(m, c.K.ScavengeLossPm); }
            else r.LootPm = r.LootPm * (1000 - c.K.ScavengeLossPm) / 1000;
        }
    }
}
