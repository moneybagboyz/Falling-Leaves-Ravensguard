using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Armies;

/// <summary>One year of the war soak: the people and the war of the year, read at the year's end.</summary>
public sealed class WarSoakYear
{
    public int Year;
    public long Alive, Births, Deaths, Immigrants, Emigrants, CoinInPurses;
    public int WarsOpen, WarsDeclared, WarsEnded, HostDays, Battles, PlacesTaken, Desertion;
    public long HourMsMax;
    public bool CoinOk = true, MassOk = true, PeopleOk = true;
    public string Hash = "";
}

/// <summary>A war the soak saw end: which realms, how long, what ended it and whether the chronicle's peace entry names the declaration as its cause.</summary>
public sealed record WarSoakEnd(int Serial, int Attacker, int Defender, int StartDay, int EndDay, int Seasons, string Cause, bool Traced);

/// <summary>What a run of the war soak measured (abstract-war 4.6): the raw tallies the gates read, every one an integer.</summary>
public sealed class WarSoakResult
{
    public int World, Years, StartDay;
    public readonly List<WarSoakYear> Rows = [];
    public readonly List<WarSoakEnd> Ends = [];
    public int WarsDeclared, WarsOpenAtEnd, PairSeasons, PairSeasonsAtWar, HostSeasons;
    /// <summary>Settlement-seasons with a host within 8 km (route), those 15% dearer than the settlement's baseline, and the same settlements 2 seasons after the host left, those back within 10%.</summary>
    public int RouteSeasons, RouteDearer, GoneSeasons, GoneBack;
    /// <summary>Settlements purveyed (first purveyance in the run), those whose unrest rose 100 or more within 30 days, and the control group's mean unrest change over 30 days (settlements never purveyed or foraged), in thousandths.</summary>
    public int Purveyed, PurveyedUnrest, ControlWindows;
    public long ControlChangeSum;
    /// <summary>Households of settlements foraged, raided, sacked or besieged in a season, and those that left as migrants with a War or Raid cause (none are counted until the world makes such migrants).</summary>
    public long TouchedHouseholds, WarMigrants, WarReturned;
    /// <summary>P8-WT-1: hosts that took the field (distinct, seen on the road or in camp) and the realm-years in the war years in which a realm was at war: the intensity gate's two numbers.</summary>
    public int HostsFielded, RealmWarYears; public int[] MusterNotes = new int[8];
    /// <summary>Population of settlements a host campaigned within 8 km of against those it never touched: start of the war years and end of them, summed.</summary>
    public long BorderStart, BorderEnd, InteriorStart, InteriorEnd;
    public int BorderSettlements, InteriorSettlements;
    /// <summary>Settlements that lost 10% or more by war and how many of those stand at 95% of the pre-war count at the end (or of the carrying cap), with those the run is long enough to judge (their last war ended 15 years before the end).</summary>
    public int LostTenPct, Recoverable, Recovered;
    public long LostPeopleStart, LostPeopleEnd;
    /// <summary>The worst consecutive days a town or village market held no staple at all, outside a siege, and the realm-years over their loan limit.</summary>
    public int WorstEmptyStreak, MarketDaysOpen, MarketDaysTotal;
    public int MinRealmPopPct = 1000;
    public long CoinStart, CoinEnd, MassBadDays, CoinBadDays, PeopleBadDays;
    public long BreadRouteMilli, BreadElsewhereMilli; public int BreadSamples;
    public long Pop0, PopLow, PopEnd; public int PopLowYear;
    public long WorstHourMs; public string WorstHourAt = "";
    public string TrailHash = "";
    public bool Exact => CoinBadDays == 0 && MassBadDays == 0 && PeopleBadDays == 0;
}

/// <summary>
/// P7-AW-E: the war soak (abstract-war 4.6). A new game of the world's seed on the real runtime with every Phase 2 to 7 system on, stepped by the hour for <c>years</c> game years; the realms' armies are the game's own
/// (levies, supply, foraging, purveyance, sieges), and the war driver of the harness only gives the realms their quarrels: in the war years (4 to 9) each season an on-map pair that is at peace, under no truce and in contact
/// draws a grievance on stream <see cref="RngStreams.WarSoakVariation"/> (key world and pair) and, on the draw, the attacker declares through <see cref="Diplomacy.Declare"/>; peace comes from the war score and the weariness as in a game.
/// Everything is integer; every loop runs in id order.
/// </summary>
public sealed class WarSoak(WarLoadRun run, int years, int world = 0)
{
    public const int QuarrelPct = 70, RouteRangeMm = 8_000_000;
    /// <summary>The first game year the driver may give the realms a quarrel (4: the baseline needs three peaceful years; the CI part passes 1) and the last (five years on).</summary>
    public int WarYearFrom { get; init; } = 4;
    public int WarYearTo => WarYearFrom + 5;
    private const int SeasonDays = TimeConst.DaysPerSeason;

    private readonly WorldState _st = run.State;
    private ArmyWorld _aw = null!;
    private PoliticsBook _pb = null!;
    private readonly WarSoakResult _r = new() { World = world, Years = years };
    private int[] _bookOf = [], _books = [];
    private ushort[] _staple = [];
    private long[] _base = [], _seasonSum = [], _seasonN = [];
    private readonly SortedDictionary<int, List<long>> _hist = [];                 // book -> mean staple price of each season so far (mites a kg x 1000)
    private readonly SortedDictionary<int, SortedDictionary<int, int>> _near = []; // season -> book -> days with a host within range
    private readonly SortedSet<int> _touched = [], _purveyedEver = [], _fielded = [];
    private readonly SortedSet<(int Realm, int Year)> _warYears = [];
    private readonly SortedDictionary<int, (int Day, int Base, int Peak)> _pend = [];
    private readonly SortedDictionary<int, int> _lastTouchDay = [];
    private int[] _pop0 = [], _popPre = [];
    private int _cursor;
    private readonly List<int[]> _yearPop = [];
    private int _nBattle, _nTaken, _nDesert, _wars0;
    private readonly HashSet<int> _ended = [];
    private readonly SortedDictionary<int, int> _emptyStreak = [];

    public WarSoakResult Result => _r;

    public WarSoakResult Run(Action<WarSoakYear>? each = null)
    {
        _aw = _st.HomeRecords!.Sim.Get<ArmyWorld>()!; _pb = _st.Politics!;
        _aw.OnFlight = (_, _, n) => _r.WarMigrants += n; _aw.OnReturn = (_, _, n) => _r.WarReturned += n;
        var econ = run.Economy; var markets = econ.Markets; var region = _st.Population.Region; int n = region.Settlements.Count;
        _bookOf = [.. markets.BookOf]; _books = [.. Enumerable.Range(0, n).Where(s => markets.Books[s] != null)];
        var ids = new List<ushort>(); foreach (var g in new[] { "wheat", "rye", "barley", "bread" }) if (run.Econ.Goods.TryIdOf(g, out var id)) ids.Add(id);
        _staple = [.. ids];
        long hour0 = run.HourDone; int day0 = (int)(hour0 / 24); day0 += hour0 % 24 == 0 ? 0 : 1;
        run.Runtime.AdvanceTo(GameTime.FromGameHours((long)day0 * 24));
        _r.StartDay = day0;
        var v = _st.Abstract!.Vital; long alive0 = v.Alive, b0 = v.Births, d0 = v.Deaths, i0 = v.Immigrants, e0 = v.Emigrants;
        _r.Pop0 = v.Alive; _r.PopLow = v.Alive; _r.CoinStart = run.Econ.Ledger.Audit().InPurses.Mites;
        _pop0 = PopBySettlement(); _popPre = (int[])_pop0.Clone();
        _cursor = _st.Chronicle.All.Count;
        int warsDeclared0 = _pb.Wars.Count; _wars0 = warsDeclared0;
        var realmPop0 = RealmPops();
        var yr = new WarSoakYear(); int hostDays = 0;
        int battles0 = 0, taken0 = 0, desert0 = 0;
        long b1 = b0, d1 = d0, i1 = i0, e1 = e0; int wars0 = 0, ends0 = 0;
        var sw = new System.Diagnostics.Stopwatch();
        int totalDays = years * TimeConst.DaysPerYear;
        for (int d = 0; d < totalDays; d++)
        {
            int day = day0 + d, doy = d % TimeConst.DaysPerYear, year = d / TimeConst.DaysPerYear + 1;
            if (doy == 0) { yr = new WarSoakYear { Year = year }; yr.CoinOk = yr.MassOk = yr.PeopleOk = true; wars0 = _pb.Wars.Count; ends0 = _pb.Wars.Count(w => !w.Alive); hostDays = 0; }
            if (d % SeasonDays == 0) { SeasonStart(day, year); CloseSeason(d / SeasonDays); }
            SnapshotUnrest();
            for (int h = 0; h < 24; h++)
            {
                sw.Restart();
                run.Runtime.AdvanceTo(GameTime.FromGameHours((long)day * 24 + h + 1));
                long ms = sw.ElapsedMilliseconds; yr.HourMsMax = Math.Max(yr.HourMsMax, ms); if (d >= 2 && ms > _r.WorstHourMs) { _r.WorstHourMs = ms; _r.WorstHourAt = $"day {d} hour {h}"; }
                if (h == 12) hostDays += Sample(day, d / SeasonDays);
            }
            // the day's end: the audits, the chronicle's news, the unrest watch
            bool coin = run.CoinConserved, mass = run.MassOk;
            long expect = alive0 + (v.Births - b0) - (v.Deaths - d0) + (v.Immigrants - i0) - (v.Emigrants - e0);
            bool people = v.Alive == expect;
            if (!coin) { _r.CoinBadDays++; yr.CoinOk = false; }
            if (!mass) { _r.MassBadDays++; yr.MassOk = false; }
            if (!people) { _r.PeopleBadDays++; yr.PeopleOk = false; }
            if (v.Alive < _r.PopLow) { _r.PopLow = v.Alive; _r.PopLowYear = year; }
            foreach (var w in _pb.Wars) if (w.Alive) { _warYears.Add((w.Attacker, year)); _warYears.Add((w.Defender, year)); }
            ReadNews(day); WatchUnrest(day); WatchMarkets(day); RecordEnds();
            if (doy == TimeConst.DaysPerYear - 1)
            {
                yr.Alive = v.Alive; yr.Births = v.Births - b1; yr.Deaths = v.Deaths - d1; yr.Immigrants = v.Immigrants - i1; yr.Emigrants = v.Emigrants - e1; b1 = v.Births; d1 = v.Deaths; i1 = v.Immigrants; e1 = v.Emigrants;
                yr.CoinInPurses = run.Econ.Ledger.Audit().InPurses.Mites; yr.WarsOpen = _pb.Wars.Count(w => w.Alive); yr.WarsDeclared = _pb.Wars.Count - wars0; yr.WarsEnded = _pb.Wars.Count(w => !w.Alive) - ends0; yr.HostDays = hostDays;
                yr.Battles = _nBattle - battles0; yr.PlacesTaken = _nTaken - taken0; yr.Desertion = _nDesert - desert0; battles0 = _nBattle; taken0 = _nTaken; desert0 = _nDesert;
                yr.Hash = _aw.Book.Hash().ToString("X16");
                _r.Rows.Add(yr); each?.Invoke(yr);
                MinRealm(realmPop0); _yearPop.Add(PopBySettlement());
            }
        }
        Finish(day0, warsDeclared0);
        return _r;
    }

    // ---------------------------------------------------------------- the war driver

    private PoliticsCtx Ctx(int day)
    {
        var sim = _st.HomeRecords!.Sim; var rule = RuleWiring.Bind(_st, _st.Content!)!;
        PoliticsWiring.Bind(_pb, _st.Content!, _st.Seed);
        return PoliticsEngine.Context(sim, _pb, rule, RuleChron.Of(_st.Chronicle, _st.Ids), day);
    }

    private List<int> OnMapRealms()
    {
        var set = new SortedSet<int>(); var hs = _aw.Supply!;
        for (int s = 0; s < _st.Population.Region.Settlements.Count; s++) { int r = hs.RealmOf(s); if (r >= 0) set.Add(r); }
        return [.. set];
    }

    private void SeasonStart(int day, int year)
    {
        var realms = OnMapRealms(); var ctx = Ctx(day);
        for (int i = 0; i < realms.Count; i++)
            for (int j = i + 1; j < realms.Count; j++)
            {
                int a = realms[i], b = realms[j];
                if (_aw.MusterPointFor(a, b) < 0 && _aw.MusterPointFor(b, a) < 0) continue;
                _r.PairSeasons++;
                if (_pb.WarOf(a, b) != null) { _r.PairSeasonsAtWar++; continue; }
                if (year < WarYearFrom || year > WarYearTo || _pb.WarOf(a) != null || _pb.WarOf(b) != null || _pb.TruceHolds(a, b, day) || !ctx.Alive(a) || !ctx.Alive(b)) continue;
                ulong key = ((ulong)world << 24) ^ ((ulong)a << 12) ^ (ulong)b;
                if (DetRng.Range(_st.Seed, day, key, RngStreams.WarSoakVariation, 0, 0, 100) >= QuarrelPct) continue;
                int att = DetRng.Range(_st.Seed, day, key, RngStreams.WarSoakVariation, 1, 0, 2) == 0 ? a : b, def = att == a ? b : a;
                var cb = Diplomacy.AddCb(ctx, att, def, CbKind.Insult);
                Diplomacy.Declare(ctx, att, def, cb, claim: false, utility: 50);
            }
    }

    private void CloseSeason(int season)
    {
        // what the season before left: settlements exposed to a host and their price against the baseline
        int prev = season - 1; if (prev < 0) return;
        foreach (int bk in _books)
        {
            var hist = _hist.GetValueOrDefault(bk); if (hist == null || hist.Count <= prev) continue;
            bool exposed = _near.TryGetValue(prev, out var m) && m.GetValueOrDefault(bk) >= 3;
            if (!exposed) continue;
            // the baseline: the (up to) 12 seasons before the settlement's first exposure
            int first = _near.Where(kv => kv.Value.GetValueOrDefault(bk) >= 3).Select(kv => kv.Key).DefaultIfEmpty(prev).Min();
            long sum = 0; int cnt = 0;
            for (int k = Math.Max(0, first - 12); k < first && k < hist.Count; k++) { sum += hist[k]; cnt++; }
            if (cnt == 0 || sum == 0) continue;
            long bas = sum / cnt;
            _r.RouteSeasons++; if (hist[prev] * 100 >= bas * 115) _r.RouteDearer++;
            _r.BreadRouteMilli += hist[prev]; _r.BreadSamples++;
        }
        // two seasons after the host has gone: exposed in prev-2 and not in prev-1 or prev
        int back = season - 3; if (back >= 0 && _near.ContainsKey(back))
            foreach (int bk in _books)
            {
                if (_near[back].GetValueOrDefault(bk) < 3) continue;
                if (_near.TryGetValue(back + 1, out var n1) && n1.GetValueOrDefault(bk) > 0) continue;
                if (_near.TryGetValue(back + 2, out var n2) && n2.GetValueOrDefault(bk) > 0) continue;
                var hist = _hist.GetValueOrDefault(bk); if (hist == null || hist.Count <= back + 2) continue;
                int first = _near.Where(kv => kv.Value.GetValueOrDefault(bk) >= 3).Select(kv => kv.Key).Min();
                long sum = 0; int cnt = 0; for (int k = Math.Max(0, first - 12); k < first && k < hist.Count; k++) { sum += hist[k]; cnt++; }
                if (cnt == 0 || sum == 0) continue;
                long bas = sum / cnt; long now = hist[back + 2];
                _r.GoneSeasons++; if (Math.Abs(now - bas) * 100 <= bas * 10) _r.GoneBack++;
            }
    }

    // ---------------------------------------------------------------- the day's samples

    private int _season = -1; private long[] _sum = [], _cnt = [];

    /// <summary>Noon: where the hosts are (settlements within 8 km of one count a day for the season) and what the staple costs in every market; returns the hosts in the field.</summary>
    private int Sample(int day, int season)
    {
        var region = _st.Population.Region; var hosts = 0; var markets = run.Economy.Markets; long sec = ((long)day * 24 + 12) * 3600;
        if (_season != season) { _season = season; _sum = new long[region.Settlements.Count]; _cnt = new long[region.Settlements.Count]; }
        var near = _near.TryGetValue(season, out var nm) ? nm : _near[season] = [];
        foreach (var h in _aw.Book.Hosts.Values)
        {
            if (h.State is HostState.Summoned or HostState.Mustering or HostState.Disbanding) continue;
            var (px, py) = _aw.PositionOfHost(h, sec); hosts++; _fielded.Add(h.Id);
            foreach (int bk in _books)
            {
                var (cx, cy) = _aw.CenterMm(bk); long dx = cx - px, dy = cy - py;
                if (dx * dx + dy * dy <= (long)RouteRangeMm * RouteRangeMm) { near[bk] = near.GetValueOrDefault(bk) + 1; _touched.Add(bk); _lastTouchDay[bk] = day; }
            }
        }
        foreach (int bk in _books)
        {
            long s = 0; foreach (var g in _staple) s += markets.Books[bk]!.MidMicro(g);
            long p = _staple.Length == 0 ? 0 : s / _staple.Length / 1000 * 1000 / 1000;     // mites a kg x 1000 of micro-mites / gram
            _sum[bk] += p; _cnt[bk]++;
            var hist = _hist.TryGetValue(bk, out var l) ? l : _hist[bk] = [];
            while (hist.Count <= season) hist.Add(0);
            hist[season] = _sum[bk] / Math.Max(1, _cnt[bk]);
        }
        return hosts;
    }

    private int[] PopBySettlement()
    {
        var pt = _st.Population.People; var a = new int[_st.Population.Region.Settlements.Count];
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i)) { int s = pt.HomeSettlement[i]; if ((uint)s < (uint)a.Length) a[s]++; }
        return a;
    }

    // ---------------------------------------------------------------- unrest: the purveyed and the control group

    private int[] _unrest0 = [];
    private readonly SortedDictionary<int, int[]> _snap = [];

    private void SnapshotUnrest()
    {
        var ub = _st.Unrest; int n = _st.Population.Region.Settlements.Count; var a = new int[n];
        for (int s = 0; s < n; s++) a[s] = ub?.UnrestOf(s) ?? 0;
        _unrest0 = a;
        int day = (int)(run.HourDone / 24); _snap[day] = a; _snap.Remove(day - 40);
    }

    private static readonly string[] TouchTypes = ["purveyance_taken", "army_foraged", "town_sacked", "siege_begun", "warband_raid", "place_taken", "sea_raid", "raid_in_peace"];

    private void ReadNews(int day)
    {
        var all = _st.Chronicle.All; var region = _st.Population.Region;
        for (; _cursor < all.Count; _cursor++)
        {
            var e = all[_cursor];
            if (e.TypeId == "battle_report") _nBattle++; else if (e.TypeId == "place_taken") _nTaken++; else if (e.TypeId == "desertion_wave") _nDesert++;
            if (Array.IndexOf(TouchTypes, e.TypeId) < 0) continue;
            foreach (var sid in e.Subjects)
            {
                int s = region.Settlements.FindIndex(x => x.Id == sid); if (s < 0) continue;
                _purveyedEver.Add(s); _lastTouchDay[s] = (int)e.When.TotalDays; _touched.Add(s);
                if (e.TypeId == "purveyance_taken" && !_pend.ContainsKey(s) && !_donePurv.Contains(s))
                {
                    int ed = (int)e.When.TotalDays; int[] snap = _snap.TryGetValue(ed, out var sn) ? sn : _unrest0;
                    _pend[s] = (ed, snap[s], snap[s]); _donePurv.Add(s);
                }
            }
        }
    }
    private readonly SortedSet<int> _donePurv = [];
    private int _ctrlDay = int.MinValue;

    private void WatchUnrest(int day)
    {
        var ub = _st.Unrest; if (ub == null) return;
        foreach (var s in _pend.Keys.ToList())
        {
            var (d0, b0, peak) = _pend[s]; int now = ub.UnrestOf(s); peak = Math.Max(peak, now); _pend[s] = (d0, b0, peak);
            if (day - d0 >= 30) { _r.Purveyed++; if (peak - b0 >= 100) _r.PurveyedUnrest++; _pend.Remove(s); }
        }
        // the control group: every 30 days of the war years, the change of the settlements never touched
        int doy = day - _r.StartDay;
        if (doy >= WarYearFrom * TimeConst.DaysPerYear - TimeConst.DaysPerYear && doy % 30 == 0 && day != _ctrlDay && _snap.TryGetValue(day - 30, out var then))
        {
            _ctrlDay = day;
            for (int s = 0; s < then.Length; s++) if (!_purveyedEver.Contains(s) && !_touched.Contains(s) && _snap.TryGetValue(day, out var nowS)) { _r.ControlWindows++; _r.ControlChangeSum += nowS[s] - then[s]; }
        }
    }

    private void WatchMarkets(int day)
    {
        var markets = run.Economy.Markets; var sup = run.Supply;
        foreach (int bk in _books)
        {
            _r.MarketDaysTotal++; long stock = 0; foreach (var g in _staple) stock += markets.Books[bk]!.Goods[g].StockUnits;
            if (stock > 0 || sup.IsBlockaded(bk)) { _emptyStreak[bk] = 0; _r.MarketDaysOpen++; continue; }
            int k = _emptyStreak.GetValueOrDefault(bk) + 1; _emptyStreak[bk] = k; if (k > _r.WorstEmptyStreak) _r.WorstEmptyStreak = k;
        }
    }

    private SortedDictionary<int, long> RealmPops()
    {
        var d = new SortedDictionary<int, long>(); var hs = _aw.Supply!; var pop = PopBySettlement();
        for (int s = 0; s < pop.Length; s++) { int r = hs.RealmOf(s); if (r >= 0) d[r] = d.GetValueOrDefault(r) + pop[s]; }
        return d;
    }

    private void MinRealm(SortedDictionary<int, long> start)
    {
        var now = RealmPops();
        foreach (var (r, p0) in start) if (p0 > 0) _r.MinRealmPopPct = Math.Min(_r.MinRealmPopPct, (int)(now.GetValueOrDefault(r) * 1000 / p0));
    }

    /// <summary>The wars that ended today, each read while its peace entry is fresh (the chronicle forgets the old): the peace caused by the declaration, and what made the loser tire.</summary>
    private void RecordEnds()
    {
        for (int i = _wars0; i < _pb.Wars.Count; i++)
        {
            var w = _pb.Wars[i]; if (w.Alive || !_ended.Add(w.Serial)) continue;
            bool traced = w.DeclEntry != 0 && _st.Chronicle.ByType("realm_peace").Any(p => p.Causes.Any(c => c.EventId?.Value == w.DeclEntry));
            int loser = w.Score > 0 ? w.Defender : w.Attacker; var ctx = Ctx(w.EndDay);
            string cause = ctx.TreasurySeasons(loser) < 1 ? "money" : (w.Seasons * 10 >= (loser == w.Attacker ? w.LossA : w.LossD) / 10 * 5 ? "weariness" : "weariness-dead");
            if (!traced) cause += "(no peace entry)";
            _r.Ends.Add(new WarSoakEnd(w.Serial, w.Attacker, w.Defender, w.StartDay, w.EndDay, w.Seasons, cause, traced));
        }
    }

    // ---------------------------------------------------------------- the end

    private void Finish(int day0, int warsBefore)
    {
        var v = _st.Abstract!.Vital; _r.PopEnd = v.Alive; _r.CoinEnd = run.Econ.Ledger.Audit().InPurses.Mites;
        _r.WarsDeclared = _pb.Wars.Count - warsBefore; _r.WarsOpenAtEnd = _pb.Wars.Count(w => w.Alive);
        // the settlements a host touched against the interior, over the war years (the pre-war count is the end of the year before the first war year); the recovery of those that lost a tenth
        var end = PopBySettlement(); int endDay = day0 + years * TimeConst.DaysPerYear;
        var pre = WarYearFrom >= 2 && _yearPop.Count >= WarYearFrom - 1 ? _yearPop[WarYearFrom - 2] : _pop0;
        var hht = _st.Population.Households; long touchedHh = 0;
        for (int hh = 0; hh < hht.Count; hh++) if (hht.MemberCount[hh] > 0 && _touched.Contains(hht.Settlement[hh])) touchedHh++;
        _r.TouchedHouseholds = touchedHh; _r.HostsFielded = _fielded.Count; _r.MusterNotes = [.. _aw.MusterNotes]; _r.RealmWarYears = _warYears.Count;
        for (int s = 0; s < end.Length; s++)
        {
            if (pre[s] <= 0) continue;
            int low = pre[s]; for (int y = WarYearFrom - 1; y < _yearPop.Count; y++) low = Math.Min(low, _yearPop[y][s]);
            int atWarEnd = _yearPop.Count >= WarYearTo ? _yearPop[WarYearTo - 1][s] : end[s];
            if (_touched.Contains(s)) { _r.BorderSettlements++; _r.BorderStart += pre[s]; _r.BorderEnd += atWarEnd; } else { _r.InteriorSettlements++; _r.InteriorStart += pre[s]; _r.InteriorEnd += atWarEnd; }
            if (_touched.Contains(s) && pre[s] >= 30 && low * 100 <= pre[s] * 90)
            {
                _r.LostTenPct++;
                if (_lastTouchDay.GetValueOrDefault(s) + 15 * TimeConst.DaysPerYear <= endDay) { _r.Recoverable++; if (end[s] * 100 >= pre[s] * 95) _r.Recovered++; }
            }
        }
        _r.TrailHash = TrailOf();
    }

    /// <summary>The run's determinism trail: every year's hash of the armies book and the people and coin of the year, folded.</summary>
    private string TrailOf()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(long x) { h ^= (ulong)x; h *= 0x100000001B3UL; h ^= h >> 29; }
        foreach (var r in _r.Rows) { M(r.Year); M(r.Alive); M(r.Births); M(r.Deaths); M(r.Immigrants); M(r.Emigrants); M(r.CoinInPurses); M(r.WarsOpen); M(r.WarsDeclared); M(r.WarsEnded); M(r.HostDays); M(r.Battles); M(r.PlacesTaken); foreach (char c in r.Hash) M(c); }
        foreach (var e in _r.Ends) { M(e.Serial); M(e.StartDay); M(e.EndDay); M(e.Seasons); }
        M(_r.PairSeasons); M(_r.PairSeasonsAtWar); M(_r.RouteSeasons); M(_r.RouteDearer); M(_r.Purveyed); M(_r.PurveyedUnrest);
        return h.ToString("X16");
    }
}
