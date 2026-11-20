using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Construction;

// P5-B: the work (spec 4.4), wages and escrow (4.6), crews, hauling (4.3) and the hero's own hands.
public sealed partial class ConstructionWorld
{
    /// <summary>True while anything has to be advanced in game time: a crew on an open site, or a groan.</summary>
    public bool Busy()
    {
        if (Book.Groans.Count > 0) return true;
        foreach (var s in _siteList) if (s.Open && s.Crew.Count > 0) return true;
        return Book.Contracts.Count > 0 && AnyLiveContract();       // P5-D: a contract's stages and notice run on the clock
    }

    private (int RainTenths, int TempC) WeatherAt(long hour) => Weather?.Invoke(hour) ?? (0, 12);

    private long _wxHour = long.MinValue;
    private bool _wxStops;

    /// <summary>Whether daub, thatch and lime stop in this game hour (rain over 2 mm an hour, or frost); asked once an hour.</summary>
    private bool RainStops(long sec)
    {
        long h = sec / 3600;
        if (h == _wxHour) return _wxStops;
        var (rain, t) = WeatherAt(h);
        _wxHour = h; _wxStops = rain > Rules.RainStopTenthsMmPerHour || t < Rules.FrostBelowC;
        return _wxStops;
    }

    /// <summary>What an idle crew member last found nothing to do with (the site's version, the pile's mass, the rain): while it holds, looking again finds nothing again.</summary>
    private readonly Dictionary<EntityId, (long Version, long PileG, bool Rain, bool RainBlocked)> _idle = [];
    private readonly Dictionary<EntityId, long> _siteVersion = [];
    private void Touch(ConstructionSite s) => _siteVersion[s.Id] = _siteVersion.GetValueOrDefault(s.Id) + 1;

    /// <summary>A cell under a built structure piece (a roof, a floor or a wall above): the weather does not reach it.</summary>
    public bool Sheltered(int x, int y, int z) => BuiltAt(x, y, z + 1) != null;

    private bool WeatherStops(Ghost g, PieceCost c, bool raining) => raining && c.RainStops && c.Outdoor && !Sheltered(g.X, g.Y, g.Z);

    private int SkillLevel(EntityId p, Skill s, int personIndex)
    {
        if (s == Skill.None) return 50;
        if (SkillOf != null) return SkillOf(p, s);
        return Pop != null && personIndex >= 0 && personIndex < Pop.People.Count ? Pop.People.SkillLevelOf(personIndex, s) : 0;
    }

    /// <summary>
    /// Brings every site up to <paramref name="nowSec"/>: whole quanta of game time (60 s by default, aligned to the clock), each one the groans that fall in it, the day's escrow at 06:00
    /// and its pay at 16:00, and the crews' work inside the working day; then any groan due by <paramref name="nowSec"/>. Ascending site id, crew in hire order: the same result whatever
    /// the cadence of the calls (the hourly system, the street view's barrier, a test).
    /// </summary>
    public void AdvanceTo(long nowSec)
    {
        long q = Math.Max(1, Rules.QuantumSeconds);
        if (Book.AdvancedSec < 0) Book.AdvancedSec = nowSec / q * q;
        if (!Busy()) { if (nowSec / q * q > Book.AdvancedSec) Book.AdvancedSec = nowSec / q * q; Publish(nowSec); return; }
        bool changed = false;
        _advLimit = nowSec / q * q;
        while (Book.AdvancedSec + q <= nowSec)
        {
            long t0 = Book.AdvancedSec;
            // P5-D: deferred quanta are applied before anything that reads or changes every site (an hour's turn: escrow, pay, the rain; a groan that may fall)
            if (Book.Groans.Count > 0 || (t0 % 3600 == 0 && (((t0 % 86400) + 86400) % 86400 == Rules.DayStartHour * 3600L || ((t0 % 86400) + 86400) % 86400 == (Rules.DayStartHour + Rules.DayHours) * 3600L))) SettleAll(q);
            changed |= ResolveGroans(t0 + q - 1);
            long tod = ((t0 % 86400) + 86400) % 86400;
            long day = t0 / 86400;
            if (tod == Rules.DayStartHour * 3600L) { EscrowDay(day, t0); changed = true; }
            if (tod == (Rules.DayStartHour + Rules.DayHours) * 3600L) { PayDay(day, t0); changed = true; }
            if (t0 % 3600 == 0 && Book.Contracts.Count > 0) ContractsStep(t0);                  // P5-D: stages, the notice, the builder's morning order
            if (tod >= Rules.DayStartHour * 3600L && tod < (Rules.DayStartHour + Rules.DayHours) * 3600L) { long qt0 = System.Diagnostics.Stopwatch.GetTimestamp(); changed |= WorkQuantum(t0, q); DiagQuantumTicks += System.Diagnostics.Stopwatch.GetTimestamp() - qt0; }
            Book.AdvancedSec = t0 + q;
            if (!Busy()) { SettleAll(q); Book.AdvancedSec = nowSec / q * q; break; }
        }
        SettleAll(q);
        changed |= ResolveGroans(nowSec);
        if (changed) { long st0 = System.Diagnostics.Stopwatch.GetTimestamp(); SyncStructures(); DiagSyncTicks += System.Diagnostics.Stopwatch.GetTimestamp() - st0; }
        Publish(nowSec);
    }

    // ------------------------------------------------------------------ P5-D: the cheap path for sites nobody watches

    /// <summary>For tests: walk every quantum of every site in full (the cheap path off). The result is the same bytes either way (ConstructionCheapPathTests).</summary>
    public bool Eager { get; set; }
    private long _advLimit;
    /// <summary>Diagnostics: site quanta walked in full, and deferred and applied at once.</summary>
    public long DiagFullQuanta, DiagLinearQuanta, DiagSyncTicks, DiagLinTicks;

    /// <summary>Applies every site's deferred quanta (and forgets their spans): before an hour's turn, a groan, the end of an advance.</summary>
    private void SettleAll(long q)
    {
        foreach (var s in _siteList) { if (s.Deferred > 0) Settle(s, q); s.LinearUntil = 0; }
    }

    /// <summary>
    /// The deferred quanta of a site at once: in each, every hand on his piece adds the same units (his rate does not change: no skill gained, no piece finished, the same weather), as the
    /// full walk would, in hire order (the last hand on a piece is its builder).
    /// </summary>
    private void Settle(ConstructionSite s, long q)
    {
        int d = s.Deferred; s.Deferred = 0;
        if (d == 0 || !s.LinearWorks) return;
        DiagLinearQuanta += d;
        foreach (var m in s.Crew)
        {
            if (s.Pause == PauseReason.NoWages && m.Class != CrewClass.Follower) continue;
            if (m.DayCapSec > 0 && m.WorkedTodaySec >= m.DayCapSec) continue;
            if (m.Job != default || m.Ghost == default || m.G is not { } g || g.Id != m.Ghost) continue;
            g.WorkDone = Math.Min(g.WorkNeed, g.WorkDone + Units(q, m.Tm, m.Tp) * d);
            g.Builder = m.Person; g.BuiltSkill = m.C!.Skill == Skill.None ? 50 : (1300 - m.Tm) / 6;
            if (g.State != GhostState.Blocked) g.State = GhostState.InProgress;
            m.WorkedTodaySec += q * d;
            if (m.C.Skill != Skill.None) { m.SkillSec += q * d; continue; }
            // an unskilled piece: the hours of Athletics gained on the way, each at the end of its quantum as the full walk gains them (the rate does not depend on the skill)
            var c = m.C;
            for (int j = 0; j < d; j++)
            {
                m.SkillSec += q;
                while (m.SkillSec >= 3600) { m.SkillSec -= 3600; Train?.Invoke(m.Person, Skill.Athletics, Rules.UnskilledGainDx100, c.Difficulty, s.LinearFrom + (j + 1) * q); }
            }
        }
    }

    private readonly List<(Ghost G, CrewMember M, long U)> _lin = [];

    /// <summary>
    /// After a full quantum at <paramref name="t0"/>: how many of the following quanta (inside this hour and this advance) the site stays linear: every hand either on his piece (materials
    /// in, not stopped by the weather, his rate read) and not finishing it, not reaching an hour of skill and not his day's cap, or idle with nothing to find (his last look still holds),
    /// or carrying a haul job; no groan pending. 0 when the next quantum must be walked in full.
    /// </summary>
    private int LinearQuanta(ConstructionSite s, long t0, long q, bool raining, out bool works)
    {
        works = false;
        if (Eager || Book.Groans.Count > 0) return 0;
        long t1 = t0 + q, hourEnd = (t0 / 3600 + 1) * 3600;
        long k = (Math.Min(hourEnd, _advLimit) - t1) / q;
        if (k <= 0 && hourEnd >= _advLimit) return 0;
        long pileG = -1;
        bool cross = true;                              // the span may run on past the hour's end: nobody idle (an idle hand looks again at each hour), no contract, the same rain
        k = long.MaxValue;
        _lin.Clear();
        foreach (var m in s.Crew)
        {
            if (s.Pause == PauseReason.NoWages && m.Class != CrewClass.Follower) continue;
            if (m.DayCapSec > 0 && m.WorkedTodaySec >= m.DayCapSec) continue;
            if (m.Job != default) continue;
            if (m.Ghost == default)
            {
                if (!_idle.TryGetValue(m.Person, out var was)) return 0;
                if (pileG < 0) pileG = PileOf(s).TotalMassG();
                if (was.Version != _siteVersion.GetValueOrDefault(s.Id) || was.PileG != pileG || was.Rain != raining) return 0;
                cross = false;
                continue;
            }
            if (m.G is not { } g || g.Id != m.Ghost || !g.Live || g.State == GhostState.Blocked || m.Tm == 0 || m.C == null) return 0;
            if (g.Work == GhostWork.Build && (!g.Materialised || WeatherStops(g, CostOf(g), raining))) return 0;
            if (m.C.Skill != Skill.None) k = Math.Min(k, (3599 - m.SkillSec) / q);         // a skilled hand's hour of skill may change his rate: walked in full
            if (m.DayCapSec > 0) k = Math.Min(k, (m.DayCapSec - m.WorkedTodaySec) / q);
            if (k <= 0) return 0;
            _lin.Add((g, m, Units(q, m.Tm, m.Tp)));
        }
        // how far the span may run: this hour, or on through the following hours of the working day while the rain does not change (spans never pass an advance's end)
        long lim = hourEnd;
        if (cross && (Book.Contracts.Count == 0 || ContractOf(s.Id) == null))
        {
            long dayEnd = t0 / 86400 * 86400 + (Rules.DayStartHour + Rules.DayHours) * 3600L;
            while (lim < dayEnd && lim < _advLimit && RainStops(lim) == raining) lim += 3600;
            RainStops(t0);
        }
        k = Math.Min(k, (Math.Min(lim, _advLimit) - t1) / q);
        if (k <= 0) return 0;
        works = _lin.Count > 0;
        if (!works) return (int)Math.Min(k, (Math.Min(hourEnd, _advLimit) - t1) / q);
        // The first quantum in which a hand would finish his piece is walked in full. Hand i on a piece (hire order) sees, in quantum j, R - (j - 1) S - P_i left (R what is left now, S
        // the units all its hands add in a quantum, P_i those of the hands before him); he finishes when that is at most his threshold (SecondsFor(left) <= q).
        for (int i = 0; i < _lin.Count && k > 0; i++)
        {
            var (g, m, _) = _lin[i];
            long sum = 0, before = 0;
            for (int o = 0; o < _lin.Count; o++) { if (_lin[o].G != g) continue; sum += _lin[o].U; if (o < i) before += _lin[o].U; }
            long room = g.WorkNeed - g.WorkDone - before - Threshold(q, m.Tm, m.Tp);
            if (room <= 0 || sum <= 0) return 0;
            k = Math.Min(k, (room - 1) / sum + 1);
        }
        return (int)k;
    }

    /// <summary><see cref="PieceCosts.UnitsFor"/> for a quantum, in long arithmetic when it fits.</summary>
    private static long Units(long q, int tm, int tp) => q < 9_000_000 ? q * 1_000_000_000_000L / ((long)tm * tp) : PieceCosts.UnitsFor(q, tm, tp);
    /// <summary>The most units left that a hand still finishes within one quantum: the largest R with <c>SecondsFor(R) &lt;= q</c>.</summary>
    private static long Threshold(long q, int tm, int tp) => q < 9_000_000 ? (q * 1_000_000_000_000L + 499_999_999_999L) / ((long)tm * tp) : (long)(((Int128)q * 1_000_000_000_000L + 499_999_999_999L) / ((long)tm * tp));

    private bool WorkQuantum(long t0, long q)
    {
        bool any = false;
        bool raining = RainStops(t0);
        (JobEnv Env, JobTable Table)? jt = null; bool jtRead = false;
        bool hourStart = t0 % 3600 == 0;
        foreach (var s in _siteList)
        {
            if (!s.Open || s.Crew.Count == 0 || s.Pause == PauseReason.Owner) continue;
            if (s.LinearUntil > t0) { if (s.Deferred++ == 0) s.LinearFrom = t0; any |= s.LinearWorks; continue; }      // P5-D: a linear quantum, applied later at once
            if (s.Deferred > 0) Settle(s, q);
            // P5-D: a contractor's crew starts its day late by the day's pace jitter (0x511): nothing happens on the site until then
            long late = LateStart(s, t0);
            if (late > t0)
            {
                DiagFullQuanta++;
                s.LinearUntil = Eager ? 0 : Math.Min(Math.Min(late / q * q, (t0 / 3600 + 1) * 3600), _advLimit); s.LinearWorks = false;
                continue;
            }
            // P5-D: a site already linear as the last advance left it (every hand on his piece, the same weather) defers this quantum too, without a full walk first
            if (s.Pause != PauseReason.Rain && LinearQuanta(s, t0 - q, q, raining, out bool pre) is int k0 and > 0)
            {
                s.LinearUntil = t0 + k0 * q; s.LinearWorks = pre; s.Deferred = 1; s.LinearFrom = t0; any |= pre;
                continue;
            }
            DiagFullQuanta++;
            bool dirty = false, worked = false, rainBlocked = false, removed = false;
            long pileG = -1;
            _finished.Clear();
            for (int ci = 0; ci < s.Crew.Count; ci++)
            {
                var m = s.Crew[ci];
                if (s.Pause == PauseReason.NoWages && m.Class != CrewClass.Follower) continue;
                if (m.DayCapSec > 0 && m.WorkedTodaySec >= m.DayCapSec) continue;          // P5-D: a growth crew's hours a day (Phase 3's own and hired hours)
                if (m.Job != default)
                {
                    if (!jtRead) { jt = Jobs?.Invoke(); jtRead = true; }
                    if (jt is { } j0 && j0.Table.Get(m.Job) is { Live: true }) continue;
                    m.Job = default;
                }
                if (m.Ghost == default)
                {
                    // an idle hand looks again only when something could have changed: the site, its pile, the rain
                    if (pileG < 0) pileG = PileOf(s).TotalMassG();
                    if (_idle.TryGetValue(m.Person, out var was) && was.Version == _siteVersion.GetValueOrDefault(s.Id) && was.PileG == pileG && was.Rain == raining && !hourStart)
                    { rainBlocked |= was.RainBlocked; continue; }
                    _idle.Remove(m.Person);
                }
                long full = m.DayCapSec > 0 ? Math.Min(q, m.DayCapSec - m.WorkedTodaySec) : q;
                long left = full;
                while (left > 0)
                {
                    // the member's piece, its cost and his rates are cached on him (derived: rebuilt the same after a load) and dropped when the piece or his skill changes
                    var g = m.G != null && m.G.Id == m.Ghost && m.Ghost != default ? m.G : m.Ghost != default ? Book.Ghosts.GetValueOrDefault(m.Ghost) : null;
                    if (g != null && (!g.Live || g.State == GhostState.Blocked || (g.Work == GhostWork.Build && raining && WeatherStops(g, CostOf(g), raining)))) { m.Ghost = default; g = null; }
                    if (g == null)
                    {
                        long pt0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        g = Pick(s, m, raining, ref rainBlocked);
                        DiagPickTicks += System.Diagnostics.Stopwatch.GetTimestamp() - pt0;
                        if (g == null) break;
                        if (!Begin(s, g)) { m.Ghost = default; m.G = null; dirty = true; continue; }
                        m.Ghost = g.Id;
                    }
                    else if (!g.Materialised && !Begin(s, g)) { m.Ghost = default; m.G = null; dirty = true; continue; }
                    if (m.G != g || m.Tm == 0)
                    {
                        m.G = g; m.C = CostOf(g);
                        m.Tm = PieceCosts.TimeMultPm(m.C, SkillLevel(m.Person, m.C.Skill, m.PersonIndex));
                        m.Tp = ToolsOk(m.C, m.Class == CrewClass.Skilled || (m.Class == CrewClass.Follower && BuilderCrew(s)) ? _skilledTools ??= CrewTools(CrewClass.Skilled) : _labourTools ??= CrewTools(CrewClass.Labourer)) ? 1000 : Rules.NoToolPm;
                    }
                    long w = WorkOn(g, m, left, out bool done);
                    left -= w; m.WorkedTodaySec += w; worked = true; any = true;
                    GainSkill(m, g, w, t0 + (full - left));
                    if (done)
                    {
                        if (g.Work == GhostWork.Build) _finished.Add(ConstructionIds.CellKey(g.X, g.Y, g.Z)); else removed = true;
                        Finish(g, t0 + (full - left), m.Person); m.Ghost = default; m.G = null; dirty = true; pileG = -1;
                    }
                }
                if (left == q && m.Ghost == default)
                {
                    if (hourStart)
                    {
                        if (!jtRead) { jt = Jobs?.Invoke(); jtRead = true; }
                        if (jt is { } j && TakeHaul(j.Env, j.Table, s, m, t0)) { any = true; continue; }
                    }
                    if (pileG < 0) pileG = PileOf(s).TotalMassG();
                    bool rb = false; _ = Pick(s, m, raining, ref rb);
                    _idle[m.Person] = (_siteVersion.GetValueOrDefault(s.Id), pileG, raining, rb);
                }
            }
            if (dirty) { long rt0 = System.Diagnostics.Stopwatch.GetTimestamp(); Refresh(s, removed ? null : _finished); DiagRefreshTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rt0; }
            if (s.Pause is PauseReason.None or PauseReason.Rain) s.Pause = !worked && rainBlocked ? PauseReason.Rain : s.Pause == PauseReason.Rain ? PauseReason.None : s.Pause;
            long lt0 = System.Diagnostics.Stopwatch.GetTimestamp(); int lin = LinearQuanta(s, t0, q, raining, out bool works); DiagLinTicks += System.Diagnostics.Stopwatch.GetTimestamp() - lt0;
            s.LinearUntil = lin > 0 ? t0 + q + lin * q : 0; s.LinearWorks = works;
        }
        return any;
    }

    /// <summary>
    /// P5-I fix: whether the site's followers are a builder's own people under a signed contract (the owner and staff of a carpentry or masonry business): they bring their trade's tools,
    /// a skilled hand's kit. They had a labourer's axe and spade, so a mason worked stone half again as slowly as the bid's hours.
    /// </summary>
    private bool BuilderCrew(ConstructionSite s)
    {
        foreach (var c in Book.Contracts.Values) if (c.Site == s.Id && c.State == ContractState.Signed && c.ContractorKind != PurseKind.Person) return true;
        return false;
    }

    /// <summary>The tool qualities a hired hand brings by class.</summary>
    private HashSet<string> CrewTools(CrewClass c)
    {
        var ids = c == CrewClass.Skilled ? Rules.SkilledTools : Rules.LabourerTools;
        return ToolQualities(ids);
    }

    private readonly Dictionary<string, HashSet<string>> _toolCache = new(StringComparer.Ordinal);
    /// <summary>The tool qualities of a list of craft_tool ids (content).</summary>
    public HashSet<string> ToolQualities(IEnumerable<string> toolIds)
    {
        string key = string.Join(',', toolIds);
        if (_toolCache.TryGetValue(key, out var have)) return have;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in toolIds)
            if (Content?.TryGet<CraftToolDef>(id, out var t) == true) foreach (var q in t.Qualities.Keys) set.Add(q);
        return _toolCache[key] = set;
    }

    /// <summary>The content (tools and their qualities); null in a bare test world.</summary>
    public ContentDatabase? Content { get; init; }

    public static bool ToolsOk(PieceCost c, IReadOnlySet<string> qualities)
    {
        foreach (var (q, _) in c.Tools) if (!qualities.Contains(q)) return false;
        return true;
    }

    private static int KindRank(PieceKind k) => k switch
    {
        PieceKind.Post or PieceKind.Wall => 0,
        PieceKind.Door or PieceKind.Window => 1,
        PieceKind.Floor or PieceKind.Ground => 2,
        PieceKind.Beam => 3,
        PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor => 4,
        PieceKind.Furniture or PieceKind.Workstation => 5,
        _ => 6,
    };

    /// <summary>The ghost a worker takes next (spec 4.4): the lowest (priority, z, kind rank, distance from the pile, id) ready one with room for another worker, the skill and the weather.</summary>
    private Ghost? Pick(ConstructionSite s, CrewMember m, bool raining, ref bool rainBlocked)
    {
        Ghost? best = null; (int, int, int, long, ulong) bestKey = default;
        NewMemo();
        int skillFor = -1; Skill skillAsked = Skill.None;
        foreach (var g in SetOf(s.Id))
        {
            if (g.State is not (GhostState.Ready or GhostState.InProgress)) continue;
            var c = CostOf(g);
            if (g.Work == GhostWork.Build)
            {
                if (WeatherStops(g, c, raining)) { rainBlocked = true; continue; }
                if (c.MinSkill > 0)
                {
                    if (skillFor < 0 || skillAsked != c.Skill) { skillAsked = c.Skill; skillFor = SkillLevel(m.Person, c.Skill, m.PersonIndex); }
                    if (skillFor < c.MinSkill) continue;
                }
            }
            // building rises from the lowest level; taking down starts at the top (roofs, then floors, then walls)
            bool down = g.Work == GhostWork.Deconstruct;
            var key = (-g.Priority, down ? 100 - g.Z : g.Z, down ? 6 - KindRank(c.Kind) : KindRank(c.Kind), (long)Math.Abs(g.X - s.PileX) + Math.Abs(g.Y - s.PileY), g.Id.Value);
            if (best != null && key.CompareTo(bestKey) >= 0) continue;               // P5-D: the costlier checks only for a ghost that would win
            if (g.Work == GhostWork.Build && !g.Materialised && !PileHasMemo(s, c)) continue;
            int on = 0; foreach (var o in s.Crew) if (o.Ghost == g.Id) on++;
            if (on >= Costs.MaxWorkers(c)) continue;
            best = g; bestKey = key;
        }
        return best;
    }

    /// <summary>Starts work on a ghost: its materials move from the pile into its own store (spec 4.3, as a craft job's do). False when the pile lacks them.</summary>
    private bool Begin(ConstructionSite s, Ghost g)
    {
        if (g.Work != GhostWork.Build || g.Materialised) return true;
        var c = CostOf(g);
        if (!PileHas(s, c)) { g.Block = GhostBlock.Materials; g.State = GhostState.Blocked; s.FastOk = false; return false; }
        var pile = PileOf(s); var own = Econ.OpenStore(StoreKind.Open, g.Id);
        foreach (var (good, grams) in c.Materials) Store.Move(pile, own, Econ.Goods.IdOf(good), grams);
        g.Materialised = true; g.State = GhostState.InProgress;
        return true;
    }

    private HashSet<string>? _skilledTools, _labourTools;
    private readonly List<long> _finished = [];

    /// <summary>A crew member on his cached piece and rates (the same arithmetic as <see cref="WorkOn(Ghost, EntityId, int, bool, long, out bool)"/>).</summary>
    private static long WorkOn(Ghost g, CrewMember m, long seconds, out bool done)
    {
        long need = PieceCosts.SecondsFor(g.WorkNeed - g.WorkDone, m.Tm, m.Tp);
        long w = Math.Min(seconds, Math.Max(0, need));
        if (w >= need) { g.WorkDone = g.WorkNeed; done = true; }
        else { g.WorkDone = Math.Min(g.WorkNeed, g.WorkDone + PieceCosts.UnitsFor(w, m.Tm, m.Tp)); done = g.WorkDone >= g.WorkNeed; }
        g.Builder = m.Person; g.BuiltSkill = m.C!.Skill == Skill.None ? 50 : (1300 - m.Tm) / 6;
        if (g.State != GhostState.Blocked) g.State = GhostState.InProgress;
        return w;
    }

    /// <summary>One worker on a ghost for up to <paramref name="seconds"/>: returns the seconds used; <paramref name="done"/> when the piece is finished.</summary>
    private long WorkOn(Ghost g, EntityId who, int skill, bool tools, long seconds, out bool done)
    {
        var c = CostOf(g);
        int tm = PieceCosts.TimeMultPm(c, skill), tp = tools ? 1000 : Rules.NoToolPm;
        long need = PieceCosts.SecondsFor(g.WorkNeed - g.WorkDone, tm, tp);
        long w = Math.Min(seconds, Math.Max(0, need));
        if (w >= need) { g.WorkDone = g.WorkNeed; done = true; }
        else { g.WorkDone = Math.Min(g.WorkNeed, g.WorkDone + PieceCosts.UnitsFor(w, tm, tp)); done = g.WorkDone >= g.WorkNeed; }
        g.Builder = who; g.BuiltSkill = c.Skill == Skill.None ? 50 : skill;
        if (g.State != GhostState.Blocked) g.State = GhostState.InProgress;
        return w;
    }

    private void GainSkill(CrewMember m, Ghost g, long seconds, long sec)
    {
        var c = CostOf(g);
        m.SkillSec += seconds;
        while (m.SkillSec >= 3600)
        {
            m.SkillSec -= 3600;
            Train?.Invoke(m.Person, c.Skill == Skill.None ? Skill.Athletics : c.Skill, c.Skill == Skill.None ? Rules.UnskilledGainDx100 : Math.Max(25, c.Difficulty) * 2, c.Difficulty, sec);
            if (c.Skill != Skill.None) m.Tm = 0;                                     // a skill gained: the rate is read again (P5-D: an unskilled piece's rate never changes)
        }
    }

    /// <summary>
    /// A piece is finished (spec 4.10, barrier): built, its materials become the piece (destroyed as Materialised, counted in the book's own audit) and it is a tile and a cell of a
    /// structure; taken down, the salvage share goes to the pile and the rest to the site's rubble, the cell is cleared and what it held is checked (a groan when it held something up).
    /// </summary>
    /// <summary>Diagnostics (budgets): pieces finished and the stopwatch ticks spent finishing them (the barrier part: tiles, rooms, support).</summary>
    public long DiagFinished, DiagFinishTicks, DiagRefreshTicks, DiagPickTicks, DiagQuantumTicks;

    private void Finish(Ghost g, long sec, EntityId by)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { FinishCore(g, sec, by); } finally { DiagFinished++; DiagFinishTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; }
    }

    private void FinishCore(Ghost g, long sec, EntityId by)
    {
        var s = Site(g.Site)!;
        var c = CostOf(g);
        if (g.Work == GhostWork.Build)
        {
            if (Econ.OtherStores.TryGetValue(g.Id, out var own))
                foreach (var (good, qty) in own.Rows()) { own.Destroy(good, qty, DestroyReason.Materialised); Book.MaterialisedG[good] = Book.MaterialisedG.GetValueOrDefault(good) + qty; }
            var b = new BuiltCell { X = g.X, Y = g.Y, Z = g.Z, Slot = g.Slot, Piece = g.Piece, Hp = PieceCosts.BuiltHp(c, g.BuiltSkill), BuiltSkill = g.BuiltSkill, Site = s.Id, BuiltSec = sec };
            AddBuilt(b);
            g.State = GhostState.Built;
            DropGhost(g);
            var change = Rooms.Apply([new CellEdit(g.X, g.Y, g.Z, g.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, g.Piece)]);
            WriteTile(g.X, g.Y, g.Z);
            s.PiecesBuilt++;
            Event(ConstructionEventKind.Built, sec, g.X, g.Y, g.Z, g.Piece, s.Id);
            CheckDwelling(change, g.X, g.Y, g.Z, sec);
        }
        else
        {
            g.State = GhostState.Built;
            DropGhost(g);
            TakeDown(s, g.X, g.Y, g.Z, g.Slot, sec, by, "taken down");
        }
        _builtVersion++;
        if (!GhostsOf(s.Id).Any())
        {
            s.State = SiteState.Finished;
            Event(ConstructionEventKind.Finished, sec, s.PileX, s.PileY, s.PileZ, s.Blueprint, s.Id);
            Chron(sec, "site_finished", [s.Owner, s.Id], [new Cause(CauseKind.Other, null, "the last piece built")], ("blueprint", s.Blueprint), ("pieces", s.PiecesBuilt.ToString()));
        }
    }

    /// <summary>Removes a built piece by hand: the salvage share of its materials to the pile, the rest to the rubble (both made again as Dematerialised: mass exact), then the support check.</summary>
    private void TakeDown(ConstructionSite s, int x, int y, int z, GhostSlot slot, long sec, EntityId by, string why)
    {
        var key = ConstructionIds.SlotKey(x, y, z, slot);
        if (!RemoveBuilt(key, out var b)) return;
        var c = Costs[b.Piece];
        var pile = PileOf(s); var rubble = Econ.OpenStore(StoreKind.Open, s.Rubble);
        if (!Book.Placed.ContainsKey(s.Rubble)) Book.Placed[s.Rubble] = new PlacedStore(s.Rubble, s.PileX, s.PileY, s.PileZ);
        Unbuild(c, pile, rubble);
        Rooms.Apply([new CellEdit(x, y, z, slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, null)]);
        WriteTile(x, y, z);
        Event(ConstructionEventKind.Removed, sec, x, y, z, b.Piece, s.Id);
        if (slot == GhostSlot.Structure) StartGroan([ConstructionIds.CellKey(x, y, z)], sec, by, why);
    }

    /// <summary>A piece's materials made again: the salvage share as goods into <paramref name="salvage"/>, the rest into <paramref name="waste"/>; counted as Dematerialised.</summary>
    private void Unbuild(PieceCost c, Store salvage, Store waste)
    {
        foreach (var (good, grams) in c.Materials)
        {
            var gid = Econ.Goods.IdOf(good);
            long keep = grams * c.SalvagePm / 1000;
            if (keep > 0) salvage.Create(gid, keep, CreateReason.Dematerialised);
            if (grams - keep > 0) waste.Create(gid, grams - keep, CreateReason.Dematerialised);
            Book.DematerialisedG[gid] = Book.DematerialisedG.GetValueOrDefault(gid) + grams;
        }
    }

    private void WriteTile(int x, int y, int z) => Tiles?.Set(x, y, z, BuiltAt(x, y, z)?.Piece, BuiltAt(x, y, z, GhostSlot.Fixture)?.Piece);

    /// <summary>A dwelling recognised on a claim improves the claim, once per structure (land 4.4, LD-10).</summary>
    private void CheckDwelling(RoomChange change, int x, int y, int z, long sec)
    {
        if (DwellingBuilt == null) return;
        int st = Rooms.StructureAt(x, y, z);
        if (st <= 0) foreach (var t in change.Touched) { st = t; break; }
        if (st <= 0 || Rooms.Facts(st) is not { IsDwelling: true }) return;
        int serial = DwellingBuilt(x, y, Day(sec));
        if (serial < 0) return;
        Book.Improved.Add(((long)serial << 32) | (uint)st);
    }

    /// <summary>Whether the claim <paramref name="serial"/> has been improved by the dwelling <paramref name="structure"/> already (the land hook asks before it improves).</summary>
    public bool ImprovedBy(int serial, int structure) => Book.Improved.Contains(((long)serial << 32) | (uint)structure);

    /// <summary>
    /// Keeps the book's copy of the structure ids (saved) in step with the RoomWorld. P5-D: lazily: the copy is marked stale and made when the book is saved or hashed
    /// (<see cref="ConstructionBook.Sync"/>), not after every hour of work (it walks every built cell).
    /// </summary>
    public void SyncStructures()
    {
        _structuresStale = true;
        Book.Flush = FlushStructures;
    }

    private bool _structuresStale;
    private void FlushStructures()
    {
        if (!_structuresStale) return;
        _structuresStale = false;
        Book.Structures = Rooms.StructureParts();
        Book.NextStructureId = Rooms.NextStructureId;
    }

    // ------------------------------------------------------------------ wages and escrow (spec 4.6)

    private PurseId? PayerPurse(ConstructionSite s) => Econ.Ledger.TryFind(s.PayerKind, s.Payer, out var p) ? p : null;
    private PurseId EscrowPurse(ConstructionSite s) => Econ.Ledger.Open(PurseKind.Chest, s.Escrow);
    private static bool Paid(CrewMember m) => m.Class != CrewClass.Follower && m.WageMitesPerDay > 0;

    /// <summary>06:00: the payer puts a day's wages for the paid crew into the site's escrow, or the site stops (NoWages) and the hands count a short day; three in a row and they quit.</summary>
    private void EscrowDay(long day, long sec)
    {
        foreach (var s in Book.Sites.Values)
        {
            if (!s.Open || s.Pause == PauseReason.Owner) continue;
            FillEscrow(s, day, sec);
        }
    }

    private void FillEscrow(ConstructionSite s, long day, long sec)
    {
        if (s.EscrowDay == day) return;
        long need = s.Crew.Where(Paid).Sum(m => m.WageMitesPerDay);
        if (need == 0) { s.EscrowDay = day; if (s.Pause == PauseReason.NoWages) s.Pause = PauseReason.None; return; }
        var payer = PayerPurse(s);
        if (payer is { } p && Econ.Ledger.Balance(p).Mites >= need && Econ.Ledger.Transfer(p, EscrowPurse(s), new Coin(need), JobReasons.Escrow))
        {
            s.EscrowDay = day;
            foreach (var m in s.Crew) m.ShortDays = 0;
            if (s.Pause == PauseReason.NoWages) { s.Pause = PauseReason.None; Refresh(s); }
            return;
        }
        if (s.Pause != PauseReason.NoWages)
        {
            s.Pause = PauseReason.NoWages;
            Event(ConstructionEventKind.Paused, sec, s.PileX, s.PileY, s.PileZ, "", s.Id);
            Chron(sec, "site_stopped", [s.Owner, s.Id], [new Cause(CauseKind.Other, null, "no wages")], ("need", need.ToString()));
        }
        foreach (var m in s.Crew.Where(Paid).ToList())
        {
            m.Ghost = default;
            if (++m.ShortDays < Rules.QuitAfterShortDays) continue;
            s.Crew.Remove(m);
            Chron(sec, "builder_quit", [m.Person, s.Id], [new Cause(CauseKind.Other, null, "unpaid")], ("days", m.ShortDays.ToString()));
        }
    }

    /// <summary>16:00: each paid hand gets <c>wage x hours worked / 10</c> out of the escrow, the rest goes back to the payer; a finished site lets its crew go.</summary>
    private void PayDay(long day, long sec)
    {
        long dayLen = Rules.DayHours * 3600L;
        foreach (var s in Book.Sites.Values)
        {
            if (s.Crew.Count == 0 && !Econ.Ledger.TryFind(PurseKind.Chest, s.Escrow, out _)) continue;
            var escrow = EscrowPurse(s);
            foreach (var m in s.Crew)
            {
                if (Paid(m) && m.WorkedTodaySec > 0)
                {
                    long pay = Math.Min(Econ.Ledger.Balance(escrow).Mites, m.WageMitesPerDay * Math.Min(m.WorkedTodaySec, dayLen) / dayLen);
                    if (pay > 0 && (PayeeOf?.Invoke(m.Person) ?? (Econ.Ledger.TryFind(PurseKind.Person, m.Person, out var pp) ? pp : null)) is { } to && Econ.Ledger.Transfer(escrow, to, new Coin(pay), JobReasons.Pay))
                    { m.PaidMites += pay; s.PaidMites += pay; }
                }
                m.WorkedTodaySec = 0;
            }
            long left = Econ.Ledger.Balance(escrow).Mites;
            if (left > 0 && PayerPurse(s) is { } back && Econ.Ledger.Transfer(escrow, back, new Coin(left), JobReasons.Refund)) s.RefundedMites += left;
            if (s.State == SiteState.Finished) s.Crew.Clear();
        }
    }

    // ------------------------------------------------------------------ crews

    /// <summary>
    /// Hires <paramref name="count"/> hands for a site from the settlements near it (spec 4.4: labourers and idle adults; skilled: carpenters and masons, Carpentry or Masonry above 30):
    /// adults of 16 to 60 not on another crew, nearest settlement first, the most skilled first for a skilled hand, then person row. Within working hours the day's escrow is put up at once.
    /// </summary>
    public ConstructionResult Hire(EntityId by, int household, EntityId siteId, int count, bool skilled, long nowSec) => Hire(by, household, siteId, count, skilled, nowSec, Skill.None);

    /// <summary>As <see cref="Hire(EntityId, int, EntityId, int, bool, long)"/>; skilled hands are taken best first in <paramref name="trade"/> when one is named (P5-I: a builder hires his job's trade).</summary>
    public ConstructionResult Hire(EntityId by, int household, EntityId siteId, int count, bool skilled, long nowSec, Skill trade)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (s.Owner != by && s.Payer != by) return ConstructionResult.No("cn_not_your_site");
        if (Pop == null) return ConstructionResult.No("cn_nobody_to_hire");
        count = Math.Min(count, Rules.CrewMax - s.Crew.Count);
        if (count <= 0) return ConstructionResult.No("cn_crew_full", Rules.CrewMax);
        var pt = Pop.People; var reg = Pop.Region;
        int nowDay = Day(nowSec);
        var taken = new HashSet<EntityId>(Book.Sites.Values.SelectMany(o => o.Open ? o.Crew.Select(m => m.Person) : []));
        var near = new Dictionary<int, long>();
        for (int i = 0; i < reg.Settlements.Count; i++)
        {
            long dx = reg.Settlements[i].CenterX - s.PileX, dy = reg.Settlements[i].CenterY - s.PileY;
            long d2 = dx * dx + dy * dy;
            if (d2 <= (long)Rules.HireRadiusM * Rules.HireRadiusM) near[i] = d2;
        }
        var picks = new List<(long D, int Skill, int Row)>();
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i)) continue;
            int age = pt.AgeYears(i, nowDay);
            if (age < 16 || age > 60) continue;
            if (!near.TryGetValue(pt.HomeSettlement[i], out long d)) continue;
            var id = pt.IdOf(i);
            if (taken.Contains(id)) continue;
            var occ = pt.Occupation[i];
            int sk = Math.Max(pt.SkillLevelOf(i, Skill.Carpentry), pt.SkillLevelOf(i, Skill.Masonry));
            if (skilled) { if (!(occ is Occupation.Carpenter or Occupation.Mason || sk > Rules.SkilledAboveSkill)) continue; }
            else if (occ is not (Occupation.None or Occupation.Labourer)) continue;
            picks.Add((d, skilled ? -(trade == Skill.None ? sk : pt.SkillLevelOf(i, trade)) : 0, i));
        }
        picks.Sort();
        var hired = new List<string>();
        foreach (var (_, _, row) in picks.Take(count))
        {
            int sk = Math.Max(pt.SkillLevelOf(row, Skill.Carpentry), pt.SkillLevelOf(row, Skill.Masonry));
            var cls = skilled && sk > Rules.SkilledAboveSkill || skilled && pt.Occupation[row] is Occupation.Carpenter or Occupation.Mason ? CrewClass.Skilled : CrewClass.Labourer;
            s.Crew.Add(new CrewMember { Person = pt.IdOf(row), PersonIndex = row, Class = cls, WageMitesPerDay = cls == CrewClass.Skilled ? Rules.WageSkilledMitesPerDay : Rules.WageLabourerMitesPerDay });
            hired.Add(pt.FullName(row));
        }
        if (hired.Count == 0) return ConstructionResult.No("cn_nobody_to_hire");
        long tod = nowSec % 86400;
        if (tod >= Rules.DayStartHour * 3600L && tod < (Rules.DayStartHour + Rules.DayHours) * 3600L && s.EscrowDay != nowSec / 86400 && s.Pause != PauseReason.NoWages) FillEscrow(s, nowSec / 86400, nowSec);
        else if (s.EscrowDay == nowSec / 86400 && tod < (Rules.DayStartHour + Rules.DayHours) * 3600L)
        {
            // hired after the day's escrow: the newcomers' day is put up too, or they wait for tomorrow's
            long extra = s.Crew.TakeLast(hired.Count).Where(Paid).Sum(m => m.WageMitesPerDay);
            if (extra > 0 && PayerPurse(s) is { } p && Econ.Ledger.Balance(p).Mites >= extra) Econ.Ledger.Transfer(p, EscrowPurse(s), new Coin(extra), JobReasons.Escrow);
        }
        if (Book.AdvancedSec < 0) Book.AdvancedSec = nowSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        Publish(nowSec);
        return ConstructionResult.Yes("cn_hired", hired.Count, string.Join(", ", hired));
    }

    /// <summary>A follower (or any person the owner orders) builds on the site without a site wage (the hero's payroll pays him, spec 4.6).</summary>
    public ConstructionResult AddWorker(EntityId siteId, EntityId person, int personIndex, CrewClass cls = CrewClass.Follower, long wage = 0, long nowSec = 0)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (s.Crew.Any(m => m.Person == person)) return ConstructionResult.No("cn_already_on_crew");
        if (s.Crew.Count >= Rules.CrewMax) return ConstructionResult.No("cn_crew_full", Rules.CrewMax);
        s.Crew.Add(new CrewMember { Person = person, PersonIndex = personIndex, Class = cls, WageMitesPerDay = wage });
        if (Book.AdvancedSec < 0) Book.AdvancedSec = nowSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        Publish(nowSec);
        return ConstructionResult.Yes("cn_worker_added", NameOf?.Invoke(person) ?? "");
    }

    /// <summary>Lets a crew member go (wages already earned were paid at the day's end).</summary>
    public ConstructionResult Dismiss(EntityId by, EntityId siteId, EntityId person, long nowSec)
    {
        var s = Site(siteId);
        if (s == null) return ConstructionResult.No("cn_no_site");
        if (s.Owner != by && s.Payer != by) return ConstructionResult.No("cn_not_your_site");
        int n = s.Crew.RemoveAll(m => m.Person == person);
        Publish(nowSec);
        return n > 0 ? ConstructionResult.Yes("cn_dismissed") : ConstructionResult.No("cn_not_on_crew");
    }

    /// <summary>
    /// Pauses or resumes a site (spec 4.4): anyone the land rule allows may resume, an heir included; the one who resumes becomes its owner and payer. A refused stranger is told whose land
    /// it is (BC-7).
    /// </summary>
    public ConstructionResult SetPaused(EntityId by, PurseKind byKind, int household, EntityId siteId, bool pause, long nowSec)
    {
        var s = Site(siteId);
        if (s == null || !(s.Open || s.State == SiteState.Abandoned)) return ConstructionResult.No("cn_no_site");
        if (s.Owner != by)
        {
            if (May == null) return ConstructionResult.No("cn_not_your_site");
            var m = May(by, household, LandAction.Build, s.PileX, s.PileY);
            if (!m.Allowed) return ConstructionResult.No(m.Why.Length > 0 ? m.Why : "cn_not_your_site", m.HolderName);
            s.Owner = by; s.OwnerKind = byKind; s.Payer = by; s.PayerKind = byKind; s.Deed = m.Deed;
            foreach (var c in s.Crew.Where(Paid)) c.ShortDays = 0;
        }
        if (pause) { s.Pause = PauseReason.Owner; s.State = SiteState.Paused; foreach (var c in s.Crew) c.Ghost = default; }
        else
        {
            s.State = SiteState.Active;
            if (s.Pause is PauseReason.Owner or PauseReason.NoWages) s.Pause = PauseReason.None;
            s.EscrowDay = -1;
            long tod = nowSec % 86400;
            if (tod >= Rules.DayStartHour * 3600L && tod < (Rules.DayStartHour + Rules.DayHours) * 3600L) FillEscrow(s, nowSec / 86400, nowSec);
            Refresh(s);
        }
        Publish(nowSec);
        return ConstructionResult.Yes(pause ? "cn_site_paused" : "cn_site_resumed");
    }

    /// <summary>The owner died or left (the inheritance step calls this): the site stops with everything on it, for whoever the land passes to.</summary>
    public void Orphan(EntityId owner, long nowSec)
    {
        foreach (var s in Book.Sites.Values.Where(s => s.Open && s.Owner == owner))
        {
            s.State = SiteState.Abandoned; s.Pause = PauseReason.Owner;
            foreach (var c in s.Crew) c.Ghost = default;
            s.Crew.Clear();
            if (Econ.Ledger.TryFind(PurseKind.Chest, s.Escrow, out var e) && Econ.Ledger.Balance(e).Mites > 0 && PayerPurse(s) is { } p) Econ.Ledger.Transfer(e, p, Econ.Ledger.Balance(e), JobReasons.Refund);
        }
        Publish(nowSec);
    }

    // ------------------------------------------------------------------ materials on site (spec 4.3)

    /// <summary>What a site still needs, grams per good (the ghosts not yet started), in the content's good order.</summary>
    public SortedDictionary<ushort, long> Needed(ConstructionSite s)
    {
        var need = new SortedDictionary<ushort, long>();
        foreach (var g in GhostsOf(s.Id))
        {
            if (g.Work != GhostWork.Build || g.Materialised) continue;
            foreach (var (good, grams) in CostOf(g).Materials) { var id = Econ.Goods.IdOf(good); need[id] = need.GetValueOrDefault(id) + grams; }
        }
        return need;
    }

    /// <summary>Goods carried to the pile by hand (the hero standing within 3 m, or a cart unloading): a store move, nothing made.</summary>
    public ConstructionResult Deliver(Store from, EntityId siteId, ushort good, long qty, int x, int y, long nowSec)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (Math.Max(Math.Abs(x - s.PileX), Math.Abs(y - s.PileY)) > Rules.PileReachM) return ConstructionResult.No("cn_pile_too_far", Rules.PileReachM);
        long moved = Store.MoveUpTo(from, PileOf(s), good, qty);
        if (moved <= 0) return ConstructionResult.No("cn_nothing_to_unload");
        Refresh(s);
        Publish(nowSec);
        return new ConstructionResult(true, "cn_unloaded", [Econ.Goods.Def(good).Name, moved], 0, s.Id);
    }

    /// <summary>The load one haul job carries: half a strength-50 carrier's capacity, 70 kg (real carry limits, Encumbrance).</summary>
    public long HaulLoadG => Encumbrance.CapacityG(Rules.HaulCarrierStrength) * Rules.HaulLoadPm / 1000;

    /// <summary>
    /// Posts haul jobs for goods the payer owns elsewhere (spec 4.3): one job per carrier's load from the source store at (sx, sy) to the pile, each paid by the payer at the board's haul
    /// rate (Phase 3 jobs unchanged). Crew members with nothing to build take them first; anyone on the board may.
    /// </summary>
    public ConstructionResult PostHaul(EntityId siteId, EntityId srcOwner, int sx, int sy, ushort good, long qty, long nowSec, int settlement = -1)
    {
        var s = Site(siteId);
        if (s == null || !s.Open) return ConstructionResult.No("cn_no_site");
        if (Jobs?.Invoke() is not { } jt) return ConstructionResult.No("cn_no_board");
        if (jt.Env.StoreOf(srcOwner) is not { } src || src.Qty(good) <= 0) return ConstructionResult.No("cn_nothing_to_haul");
        if (PayerPurse(s) is not { } payer) return ConstructionResult.No("cn_cannot_pay");
        qty = Math.Min(qty, src.Qty(good));
        long perLoad = Econ.Goods.IsPiece(good) ? Math.Max(1, HaulLoadG / Math.Max(1, Econ.Goods.MassG(good, 1))) : HaulLoadG;
        int posted = 0; long wages = 0;
        if (settlement < 0) settlement = NearestSettlement(s.PileX, s.PileY);
        while (qty > 0)
        {
            long load = Math.Min(perLoad, qty);
            long pathMm = (long)Math.Sqrt((double)(sx - s.PileX) * (sx - s.PileX) + (double)(sy - s.PileY) * (sy - s.PileY)) * 1000;
            long wage = Math.Max(1, JobPay.Haul(jt.Env.Tuning, jt.Env.UnskilledWage, pathMm, Econ.Goods.MassG(good, load)));
            var job = jt.Table.Post(jt.Env, OddJobKind.Haul, s.Payer, payer, RegionId, settlement, nowSec, nowSec + Rules.HaulDeadlineHours * 3600L, wage, j =>
            {
                j.P0 = sx * 1000L; j.P1 = sy * 1000L; j.P2 = s.PileX * 1000L; j.P3 = s.PileY * 1000L; j.P4 = good; j.P5 = load;
                j.SrcOwner = srcOwner; j.DstOwner = s.Pile; j.Title = "haul to the building site";
            });
            if (job == null) break;
            posted++; wages += wage; qty -= load;
        }
        Publish(nowSec);
        return posted == 0 ? ConstructionResult.No("cn_cannot_pay") : new ConstructionResult(true, "cn_haul_posted", [posted, Econ.Goods.Def(good).Name], wages, s.Id);
    }

    private int NearestSettlement(int x, int y)
    {
        if (Pop == null) return 0;
        int best = 0; long bd = long.MaxValue; var ss = Pop.Region.Settlements;
        for (int i = 0; i < ss.Count; i++) { long dx = ss[i].CenterX - x, dy = ss[i].CenterY - y, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// <summary>A crew member with nothing to build takes the oldest open haul job to his site.</summary>
    private bool TakeHaul(JobEnv env, JobTable table, ConstructionSite s, CrewMember m, long t0)
    {
        foreach (var j in table.Jobs.Values)
        {
            if (j.State != JobState.Open || j.Kind != OddJobKind.Haul || j.DstOwner != s.Pile) continue;
            int age = Pop != null && m.PersonIndex >= 0 && m.PersonIndex < Pop.People.Count ? Pop.People.AgeYears(m.PersonIndex, Day(t0)) : 30;
            var r = table.Claim(env, new ClaimRequest(j.Id, m.Person, m.PersonIndex, t0, 0, s.PileX * 1000L, s.PileY * 1000L, 0, age) { Origin = CommandOrigin.Npc }, t0);
            if (r != ClaimResult.Taken) continue;
            m.Job = j.Id;
            return true;
        }
        return false;
    }

    /// <summary>The haul jobs to the sites' piles, brought up to <paramref name="nowSec"/> (their goods reach the pile at the end of unloading).</summary>
    public void AdvanceHauls(long nowSec)
    {
        if (Jobs?.Invoke() is not { } jt) return;
        var piles = new HashSet<EntityId>(Book.Sites.Values.Where(s => s.Open).Select(s => s.Pile));
        if (piles.Count == 0) return;
        jt.Table.AdvanceWhere(jt.Env, nowSec, j => piles.Contains(j.DstOwner));
        foreach (var s in Book.Sites.Values) if (s.Open) Refresh(s);
    }

    // ------------------------------------------------------------------ the hero's own hands (spec 4.4, 6)

    /// <summary>
    /// The hero worked on a ghost from <paramref name="fromSec"/> to <paramref name="untilSec"/> standing at (x, y, z) (a recorded desk command): he must be within reach of it, in light, out
    /// of the rain for daub and thatch, with its materials in the pile. The hours count at his skill (time multiplier), x1.5 without the tools; a finished piece is built and the rest of the
    /// time goes to the next ready ghost of the site within reach. Each attended hour trains Carpentry or Masonry (Athletics on unskilled pieces); his tools wear.
    /// </summary>
    public ConstructionResult HeroWork(EntityId hero, PurseKind heroKind, int household, EntityId ghostId, long fromSec, long untilSec, int x, int y, int z, IReadOnlySet<string> tools,
        Action<string, long>? wear = null)
    {
        var g = Book.Ghosts.GetValueOrDefault(ghostId);
        if (g == null || !g.Live) return ConstructionResult.No("cn_no_ghost");
        var s = Site(g.Site)!;
        if (s.Owner != hero && !IsContractor(s, hero) && May != null && !May(hero, household, LandAction.Build, g.X, g.Y).Allowed) return ConstructionResult.No("cn_not_your_site");   // P5-D: a contractor works where he was hired
        long from = Math.Max(fromSec, Book.HeroWorkedTo);
        if (untilSec <= from) return ConstructionResult.Yes("cn_worked", 0);
        int done = 0; long worked = 0; long t = from;
        while (t < untilSec && g != null)
        {
            if (Math.Max(Math.Abs(x - g.X), Math.Abs(y - g.Y)) > Rules.ReachM || Math.Abs(z - g.Z) > 1) { if (worked == 0) return ConstructionResult.No("cn_too_far", Rules.ReachM); break; }
            var c = CostOf(g);
            long hod = t % 86400 / 3600;
            if (c.Outdoor && !Sheltered(g.X, g.Y, g.Z) && (hod < Rules.LightFromHour || hod >= Rules.LightToHour)) { if (worked == 0) return ConstructionResult.No("cn_too_dark"); break; }
            if (g.Work == GhostWork.Build && WeatherStops(g, c, RainStops(t))) { if (worked == 0) return ConstructionResult.No("cn_rain", c.Name); break; }
            if (g.State == GhostState.Blocked) { if (worked == 0) return ConstructionResult.No(g.Block == GhostBlock.Materials ? "cn_no_materials" : "cn_no_support_yet", c.Name); break; }
            if (!Begin(s, g)) { if (worked == 0) return ConstructionResult.No("cn_no_materials", c.Name); break; }
            int skill = SkillLevel(hero, c.Skill, -1);
            long w = WorkOn(g, hero, skill, ToolsOk(c, tools), untilSec - t, out bool fin);
            t += w; worked += w;
            if (c.Skill != Skill.None) foreach (var (q, _) in c.Tools) wear?.Invoke(q, w);
            var sk = c.Skill == Skill.None ? Skill.Athletics : c.Skill;
            long acc = Book.HeroSkillSec.GetValueOrDefault((byte)sk) + w;
            while (acc >= 3600) { acc -= 3600; Train?.Invoke(hero, sk, c.Skill == Skill.None ? Rules.UnskilledGainDx100 : Math.Max(25, c.Difficulty) * 2, c.Difficulty, t); }
            Book.HeroSkillSec[(byte)sk] = acc;
            if (!fin) break;
            Finish(g, t, hero); done++;
            Refresh(s);
            g = NextNear(s, x, y, z);
        }
        Book.HeroWorkedTo = Math.Max(Book.HeroWorkedTo, Math.Max(t, from));
        if (Book.AdvancedSec < 0) Book.AdvancedSec = untilSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        SyncStructures();
        Publish(untilSec);
        return new ConstructionResult(true, done > 0 ? "cn_built_pieces" : "cn_worked", done > 0 ? [done] : [worked / 60], 0, s.Id);
    }

    private Ghost? NextNear(ConstructionSite s, int x, int y, int z)
    {
        bool dummy = false;
        var me = new CrewMember { Person = s.Owner };
        foreach (var g in GhostsOf(s.Id).OrderBy(g => g.Z).ThenBy(g => KindRank(CostOf(g).Kind)).ThenBy(g => Math.Max(Math.Abs(g.X - x), Math.Abs(g.Y - y))).ThenBy(g => g.Id))
        {
            if (g.State is not (GhostState.Ready or GhostState.InProgress)) continue;
            if (Math.Max(Math.Abs(x - g.X), Math.Abs(y - g.Y)) > Rules.ReachM || Math.Abs(z - g.Z) > 1) continue;
            if (g.Work == GhostWork.Build && !g.Materialised && !PileHas(s, CostOf(g))) continue;
            return g;
        }
        _ = dummy; _ = me;
        return null;
    }

    /// <summary>Raises a ghost's priority (Q on a ghost): workers take it before the others of its level.</summary>
    public ConstructionResult SetPriority(EntityId ghostId, byte priority, long nowSec)
    {
        if (Book.Ghosts.GetValueOrDefault(ghostId) is not { } g) return ConstructionResult.No("cn_no_ghost");
        g.Priority = priority;
        Publish(nowSec);
        return ConstructionResult.Yes("cn_priority_set");
    }
}
