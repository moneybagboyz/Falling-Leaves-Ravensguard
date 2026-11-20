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
            void Chron(string type, Site? site, string note, (string, string)[] data) => ctx.Outbox.Chronicle(ctx.Time, type, site == null ? [] : [site.Id], [Why.Of(type == "mine_flooded" ? CauseKind.Weather : CauseKind.Raid, note, ChronParam.Place("site", site?.Id ?? EntityId.None))], data);
            Daily(book, content, region, ctx.Seed, (int)ctx.Day, Chron);
            if (ctx.Time.DayOfYear == Core.TimeConst.DaysPerYear) Yearly(book, content, region, ctx.Seed, ctx.Year);
        }
    }

    /// <summary>The lairs' legends (4.9): a rumour seed per monster lair, told in the nearest settlement.</summary>
    private static void Legends(TickContext ctx, Region region)
    {
        foreach (var s in region.Sites)
        {
            if (!s.MonsterLair) continue;
            int near = Nearest(region, s);
            ctx.Outbox.Chronicle(ctx.Time, "lair_legend", [s.Id], [Why.After(CauseKind.Raid, s.HistoryEvent == EntityId.None ? null : s.HistoryEvent, "ch_why_lair_tale", ChronParam.Place("site", s.Id))],
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

    /// <summary>The day's steps (floods, the fled coming back, beasts healing, reoccupation), for every site but the hero's. <paramref name="chronicle"/> writes an entry.</summary>
    public static void Daily(SiteBook book, SiteContent c, Region region, ulong seed, int day, Action<string, Site?, string, (string, string)[]> chronicle)
    {
        int heavy = book.HeavyOfLastDay;
        foreach (var s in region.Sites)
        {
            if (s.Id.Value == book.ActiveSite) continue;
            if (s.Kind == SiteKind.Mine && s.Mine is MineState.Active or MineState.Abandoned && SiteBook.FloodRolls(seed, day, s.Id.Value, heavy, c.K))
            {
                var r = book.Record(s.Id.Value);
                r.FloodSince = (long)(day + 1) * 86400;
                if (r.Map is { } m) m.FloodCm[m.Levels - 1] = Math.Max(m.FloodCm[m.Levels - 1], c.K.FloodHours * c.K.FloodRiseCmPerHour);
                int near = Nearest(region, s);
                chronicle("mine_flooded", s, "ch_why_heavy_rain", [("site", s.Name), ("settlement", near >= 0 ? region.Settlements[near].Name : ""), ("text_key", "ex_chron_flooded")]);
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
            if (!SiteBook.ReoccupyRolls(seed, day, id, c.K.ReoccupyPressure, c.K)) continue;
            var band = SiteBook.NewBand(c, m, seed, day, 1000 * (r.Reoccupied + 1), 0);
            m.Inhabitants.RemoveAll(n => n.State == InhabitantState.Dead);
            m.Inhabitants.AddRange(band);
            r.Reoccupied++; r.Cleared = -1;
            var site = region.Sites.FirstOrDefault(x => x.Id.Value == id);
            chronicle("site_reoccupied", site, "ch_why_outlaws_moved_in", [("site", site?.Name ?? ""), ("band", band.Count.ToString()), ("text_key", "ex_chron_reoccupied")]);
        }
    }

    /// <summary>The scavengers (4.3): once a game year one in ten untouched ruins loses 5% of what is left (stream 0x4B9, key year and site, draws from 1000).</summary>
    public static void Yearly(SiteBook book, SiteContent c, Region region, ulong seed, int year)
    {
        foreach (var s in region.Sites)
        {
            if (s.Kind != SiteKind.Ruin || s.Id.Value == book.ActiveSite) continue;
            if (DetRng.Range(seed, year, s.Id.Value, RngStreams.Scavenge, 1000, 0, 1000) >= c.K.ScavengeSitesPm) continue;
            var r = book.Record(s.Id.Value);
            if (r.Map is { } m) { if (m.Containers.All(x => !x.Opened)) SiteBook.Scavenge(m, c.K.ScavengeLossPm); }
            else r.LootPm = r.LootPm * (1000 - c.K.ScavengeLossPm) / 1000;
        }
    }
}
