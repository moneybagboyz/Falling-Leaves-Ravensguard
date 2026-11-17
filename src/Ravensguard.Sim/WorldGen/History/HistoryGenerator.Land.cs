using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

public sealed partial class HistoryGenerator
{
    private readonly int[] _culPop = new int[8];
    private readonly int[] _culK = new int[8];
    private readonly int[] _badKind = new int[8], _badStreak = new int[8];
    private readonly EntityId[] _weatherEv = new EntityId[8];
    private bool _culKDone;

    private static int SIdx(EntityId id) => (int)id.Counter - 1;
    private static int RIdx(EntityId id) => (int)id.Counter - 1;

    // ---------- weather (plan §3c): a bad year per culture region, rolled each spring ----------
    private void WeatherPass()
    {
        if (_season != 0) return;
        for (int c = 0; c < _c.Cultures.Count && c < 8; c++)
        {
            bool had = _badKind[c] != 0;
            if (!Chance((ulong)c, S.Weather, _s.BadYearPermille * 1000L)) { _badKind[c] = 0; _badStreak[c] = 0; continue; }
            int kind = 1 + Roll((ulong)c, S.Weather, 0, 3);
            _badKind[c] = kind; _badStreak[c] = had ? _badStreak[c] + 1 : 0;
            string cu = Culture(c);
            var (title, text, note) = kind switch
            {
                1 => (_badStreak[c] > 0 ? "The Great Drought" : "A Dry Year", $"No rain falls on the {cu} lands from spring to harvest; streams shrink and wells fail", $"a stalled high-pressure front over the {cu} lands"),
                2 => ("The Cold Summer", $"Late frosts and a sunless summer blight the {cu} fields", "a cold year after distant fires darkened the sky"),
                _ => ("The Rotting Rains", $"Rain falls without end on the {cu} lands and the crops rot in the field", "storm fronts stacked up against the hills"),
            };
            var subj = _h.Realms.Where(r => r.Alive && r.CultureId == c).Select(r => r.Id).Take(6).ToArray();
            _weatherEv[c] = Ev("bad_year", title, text, _c.Cultures[c].SeedCell, subj, Cz(CauseKind.Weather, EntityId.None, note));
        }
    }

    // ---------- population ----------
    private void PopulationPass()
    {
        Array.Clear(_border); Array.Clear(_cultCount); Array.Clear(_culPop);
        foreach (var r in _h.Realms) { r.Cells = 0; r.Pop = 0; }
        if (!_culKDone)
        {
            for (int i = 0; i < A; i++) { int cu = _c.CultureId[i]; if (cu < 8 && _owner[i] >= 0) _culK[cu] += _K[i]; }
            _culKDone = true;
        }
        ulong key = DetRng.Next(_seed, _tick, 0, S.Pop, 0);
        int rateQ = _s.RuralGrowthPermilleYear * SeasonGrow[_season];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x, o = _owner[i];
                if (o < 0) continue;
                var r = _h.Realms[o];
                r.Cells++;
                int k = _K[i];
                if (k > 0)
                {
                    int p = _pop[i];
                    if (p > 0)
                    {
                        long num = (long)p * rateQ * (k - p), den = (long)k * 400_000;
                        long fl = num >= 0 ? num / den : -((-num + den - 1) / den);
                        long rem = num - fl * den;
                        if (rem > 0 && (long)(DetRng.Mix(key ^ ((ulong)i * 0x9E3779B97F4A7C15UL)) % (ulong)den) < rem) fl++;
                        p = (int)Math.Max(0, p + fl);
                    }
                    else if (k >= 8)
                    {
                        int nb = 0;
                        if (x > 0) nb = Math.Max(nb, _pop[i - 1]);
                        if (x < N - 1) nb = Math.Max(nb, _pop[i + 1]);
                        if (y > 0) nb = Math.Max(nb, _pop[i - N]);
                        if (y < N - 1) nb = Math.Max(nb, _pop[i + N]);
                        if (nb * 2 > k && (DetRng.Mix(key ^ (ulong)i) & 7) == 0) p = 2;
                    }
                    _pop[i] = p;
                    r.Pop += p;
                    int cu = _c.CultureId[i];
                    if (cu < 8) _culPop[cu] += p;
                }
                if (x < N - 1) { int o2 = _owner[i + 1]; if (o2 >= 0 && o2 != o) { _border[o * _R + o2]++; _border[o2 * _R + o]++; } }
                if (y < N - 1) { int o2 = _owner[i + N]; if (o2 >= 0 && o2 != o) { _border[o * _R + o2]++; _border[o2 * _R + o]++; } }
                int cc = _c.CultureId[i];
                if (cc < 8) _cultCount[o * 8 + cc]++;
            }
    }

    private int LogisticDelta(int pop, int cap, int rateQ, ulong ent)
    {
        cap = Math.Max(cap, 1);
        long num = (long)pop * rateQ * (cap - pop), den = (long)cap * 400_000;
        long fl = num >= 0 ? num / den : -((-num + den - 1) / den);
        long rem = num - fl * den;
        if (rem > 0 && Roll(ent, S.Pop, 0, (int)Math.Min(den, int.MaxValue)) < rem * Math.Min(den, int.MaxValue) / den) fl++;
        return (int)Math.Max(fl, -(pop / 4) - 1);
    }

    private int RuralPop25(Settlement s)
    {
        int x = Cx(s.Cell), y = Cy(s.Cell), sum = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (In(x + dx, y + dy)) sum += _pop[(y + dy) * N + x + dx];
        return sum;
    }

    // ---------- settlements: growth, promotion, abandonment ----------
    private void SettlementPass()
    {
        int rateQ = _s.RuralGrowthPermilleYear * SeasonGrow[_season] * 16 / 10;
        int n = _h.Settlements.Count;
        for (int i = 0; i < n; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin) continue;
            s.Realm = _owner[s.Cell];
            int cap = RuralPop25(s) * _s.UrbanCapPct / 100 + s.Mining * 120 + s.RouteCount * 50;
            cap = s.Minor ? Math.Min(cap, _s.MinorMaxPop) : Math.Min(cap, _s.UrbanMaxPop);
            if (s.Realm >= 0 && Rm(s.Realm).Capital == i) cap += 400;
            s.Pop = Math.Max(0, s.Pop + LogisticDelta(s.Pop, cap, rateQ, s.Id.Value));
            if (s.Pop > s.PeakPop) s.PeakPop = s.Pop;
            if (s.Realm >= 0) Rm(s.Realm).Pop += s.Pop;
            var kind = s.Kind;
            if (kind > s.PeakKind)
            {
                s.PeakKind = kind;
                if (kind == SettlementKind.City || (kind == SettlementKind.Town && s.Id.Value % 3 == 0))
                {
                    var rn = s.Realm >= 0 ? Rm(s.Realm).Name : "no realm";
                    string what = kind == SettlementKind.City ? "a city" : "a market town";
                    string note = s.OnRiver ? "river trade and a bountiful hinterland" : s.Coastal ? "sea trade and a bountiful hinterland" : s.Mining > 0 ? "the wealth of nearby mines" : "a bountiful hinterland and growing markets";
                    Ev("settlement_grew", $"{s.Name} becomes {what}", $"{s.Name} in {rn} now counts {Num(s.Pop)} souls", s.Cell,
                        s.Realm >= 0 ? [s.Id, Rm(s.Realm).Id] : [s.Id], Cz(CauseKind.Other, EntityId.None, note));
                }
            }
            if (s.Minor && _tick > 0 && Chance(s.Id.Value, S.Misc, _s.MinorAbandonPermilleYear * 250L)) AbandonHardship(i);
            else if (s.Pop < 5 && _tick > 0) AbandonQuiet(i);
            else if (_season == 0 && s.Kind <= SettlementKind.Village && s.Hinterland > 0 && RuralPop25(s) * 100 < s.Hinterland * 33 && Pct(s.Id.Value, S.Misc, 5)) AbandonQuiet(i);
        }
    }

    private void AbandonQuiet(int i)
    {
        var s = _h.Settlements[i];
        EntityId cause;
        string kind = "abandoned", why = "drained by hard years, the last families leave";
        if (s.LastBlow.Value != 0 && _tick - s.LastBlowTick < 48) { cause = s.LastBlow; why = "the last families leave after the blow that struck it"; }
        else
        {
            cause = Ev("emigration", $"Families leave {s.Name}", $"The people of {s.Name} drift away in search of better land", s.Cell,
                [s.Id], Cz(CauseKind.Other, EntityId.None, "poor soil and failing harvests"));
        }
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        var ev = Ev("settlement_abandoned", $"{s.Name} is abandoned", $"{why}; {s.Name} falls silent", s.Cell, rn.Value != 0 ? [s.Id, rn] : [s.Id],
            Cz(CauseKind.Other, cause, why));
        MakeRuin(i, kind, ev, cause);
    }

    /// <summary>A small settlement dies of ordinary hard luck; each way has its own cause event.</summary>
    private void AbandonHardship(int i)
    {
        var s = _h.Settlements[i];
        int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        EntityId[] subj = rn.Value != 0 ? [s.Id, rn] : [s.Id];
        int way = Roll(s.Id.Value, S.Misc, 0, 5);
        if (way == 0 && _badKind[cu] == 0) way = 1;
        string kind = "abandoned", ruinText, why;
        EntityId cause;
        switch (way)
        {
            case 0:
                cause = _weatherEv[cu]; why = "failing harvests"; ruinText = $"After the hard year the last families of {s.Name} give up the land and leave";
                break;
            case 1:
                if (s.Manor)
                {
                    cause = Ev("line_extinct", $"The line of {s.Name} fails", $"The lord of {s.Name} dies and no kin will hold it", s.Cell, subj, Cz(CauseKind.Death, EntityId.None, "a lord who left no heirs"));
                    why = "no lord to hold it"; ruinText = $"With no lord, the households of {s.Name} drift away and the hall stands empty";
                }
                else
                {
                    cause = Ev("emigration", $"Families leave {s.Name}", $"The people of {s.Name} drift off to richer ground", s.Cell, subj, Cz(CauseKind.Other, EntityId.None, "poor soil and the pull of the towns"));
                    why = "drained by the towns"; ruinText = $"The last families leave {s.Name}";
                }
                break;
            case 2:
                cause = Ev("bandit_raid", $"Bandits strike {s.Name}", $"A band of outlaws plunders {s.Name} and carries off its stores", s.Cell, subj, Cz(CauseKind.Crime, EntityId.None, "hungry outlaws in the unwatched marches"));
                s.LastBlow = cause; s.LastBlowTick = _tick;
                why = "plundered by outlaws"; kind = s.Manor ? "sacked" : "burned"; ruinText = $"Outlaws put {s.Name} to the torch and the survivors scatter";
                break;
            case 3:
                cause = Ev("spring_failed", $"The spring of {s.Name} runs dry", $"The wells and the stream at {s.Name} fail", s.Cell, subj, Cz(CauseKind.Weather, _badKind[cu] != 0 ? _weatherEv[cu] : EntityId.None, "a shifting watercourse and a run of dry summers"));
                why = "no water"; ruinText = $"Without water, {s.Name} is left to the weeds";
                break;
            default:
                cause = Ev("tithe_dispute", $"The tithe quarrel at {s.Name}", $"A bitter dispute over rents and tithes divides {s.Name}", s.Cell, subj, Cz(CauseKind.Law, EntityId.None, "a quarrel over rents and tithes"));
                why = "a ruinous quarrel"; ruinText = $"Half of {s.Name} leaves in anger and the rest cannot hold it";
                break;
        }
        if (cause.Value == 0) cause = Ev("emigration", $"Families leave {s.Name}", $"The people of {s.Name} drift away", s.Cell, subj, Cz(CauseKind.Other, EntityId.None, "poor soil and failing harvests"));
        var ev = Ev("settlement_abandoned", $"{s.Name} is abandoned", $"{ruinText}; {s.Name} falls silent", s.Cell, subj, Cz(CauseKind.Other, cause, why));
        MakeRuin(i, kind, ev, cause);
    }

    private void MakeRuin(int i, string kind, EntityId ruinEv, EntityId causeEv)
    {
        var s = _h.Settlements[i];
        if (s.IsRuin) return;
        s.IsRuin = true; s.RuinTick = _tick; s.RuinKind = kind; s.RuinEvent = ruinEv; s.RuinCauseEvent = causeEv; s.Pop = 0;
        if (s.Realm >= 0 && Rm(s.Realm).Capital == i) FixCapital(Rm(s.Realm));
        foreach (var rt in _h.TradeRoutes)
            if (!rt.Abandoned && (rt.A == s.Id || rt.B == s.Id))
            {
                rt.Abandoned = true;
                _h.Settlements[SIdx(rt.A)].RouteCount = Math.Max(0, _h.Settlements[SIdx(rt.A)].RouteCount - 1);
                _h.Settlements[SIdx(rt.B)].RouteCount = Math.Max(0, _h.Settlements[SIdx(rt.B)].RouteCount - 1);
            }
        foreach (var a in _h.Artefacts)
            if (a.Status == ArtefactStatus.Held && a.Holder == s.Id)
            {
                if (Pct(a.Id.Value, S.Art, 70)) ArtefactVanish(a, ArtefactStatus.Buried, s.Cell, ruinEv, $"buried in the ruins of {s.Name} ({kind})");
                else ArtefactVanish(a, ArtefactStatus.Lost, s.Cell, ruinEv, $"lost when {s.Name} fell to ruin ({kind})");
            }
        foreach (var n in _h.Notables)
            if (n.Alive && n.Role is NotableRole.Saint && n.Residence == i) n.Residence = -1;
    }

    private void FixCapital(RealmState r)
    {
        int best = -1, bp = -1;
        for (int i = 0; i < _h.Settlements.Count; i++)
        {
            var s = _h.Settlements[i];
            if (!s.IsRuin && s.Realm == r.Index && s.Pop > bp) { bp = s.Pop; best = i; }
        }
        r.Capital = best;
    }

    // ---------- founding of new settlements ----------
    private bool SettledRiver(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!In(x + dx, y + dy)) continue;
                int j = (y + dy) * N + x + dx;
                if (_c.RiverFlow[j] >= _c.Settings.RiverFlowThreshold && !_c.Ocean[j]) return true;
            }
        return false;
    }

    /// <summary>Towns: spacing is wider on poor ground and narrows to <c>FoundSpacingRich</c> in fertile river lowlands.</summary>
    private void FoundingPass(bool initial)
    {
        int attempts = initial ? _s.FoundInitialAttempts : _s.FoundAttemptsPerTick;
        for (int a = 0; a < attempts; a++)
        {
            int cell = Roll((ulong)a, S.Found, 0, A);
            if (_K[cell] < 12) continue;
            int o = _owner[cell];
            if (o < 0 || !Rm(o).Alive) continue;
            if (_settleAt[cell] >= 0) continue;
            int x = Cx(cell), y = Cy(cell);
            int hint = 0, rural = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (!In(x + dx, y + dy)) continue;
                    int j = (y + dy) * N + x + dx;
                    hint += _K[j]; rural += _pop[j];
                }
            bool river = SettledRiver(x, y);
            bool rich = river && hint >= _s.FoundRichHinterland;
            int sp = rich ? _s.FoundSpacingRich : _s.FoundSpacing;
            bool clash = false;
            for (int dy = -_s.FoundSpacing; dy <= _s.FoundSpacing && !clash; dy++)
                for (int dx = -_s.FoundSpacing; dx <= _s.FoundSpacing; dx++)
                {
                    if (!In(x + dx, y + dy)) continue;
                    int si = _settleAt[(y + dy) * N + x + dx];
                    if (si < 0) continue;
                    var other = _h.Settlements[si];
                    int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (cheb <= 1 || (!other.IsRuin && !other.Minor && cheb <= sp)) { clash = true; break; }
                }
            if (clash) continue;
            if (hint < _s.FoundMinHinterland) continue;
            if (!initial && rural * 100 < hint * _s.FoundRegrowPct) continue;
            int pct = _s.FoundChancePct * (100 + (river ? 60 : 0) + (_c.CoastDistance[cell] <= 2 ? 40 : 0) + (rich ? 80 : 0)) / 100;
            if (!Pct((ulong)a, S.Found, pct)) continue;
            int pop = initial ? 30 + rural / 14 + Roll((ulong)a, S.Found, 0, 40) : 18 + Roll((ulong)a, S.Found, 0, 20);
            AddSettlement(cell, pop, o, null);
            if (!initial) _pop[cell] = Math.Max(0, _pop[cell] - 10);
        }
    }

    /// <summary>Hamlets, villages and manors fill the gaps between the towns; the ones that fail leave the ruins.</summary>
    private void MinorFoundingPass(bool initial)
    {
        int attempts = initial ? 30000 : _s.MinorFoundAttemptsPerTick;
        int sp = _s.MinorSpacing;
        for (int a = 0; a < attempts; a++)
        {
            int cell = Roll((ulong)a + 1_000_000UL, S.Found, 0, A);
            if (_K[cell] < 24) continue;
            int o = _owner[cell];
            if (o < 0 || !Rm(o).Alive || _settleAt[cell] >= 0) continue;
            int x = Cx(cell), y = Cy(cell);
            bool clash = false;
            for (int dy = -sp; dy <= sp && !clash; dy++)
                for (int dx = -sp; dx <= sp; dx++)
                {
                    if (!In(x + dx, y + dy)) continue;
                    int si = _settleAt[(y + dy) * N + x + dx];
                    if (si < 0) continue;
                    var other = _h.Settlements[si];
                    if (!other.IsRuin || (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)) { clash = true; break; }
                }
            if (clash) continue;
            int rural = 0, cnt = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (In(x + dx, y + dy)) { rural += _pop[(y + dy) * N + x + dx]; cnt++; }
            if (!initial && rural < 18 * cnt) continue;
            bool river = SettledRiver(x, y);
            int pct = _s.MinorFoundChancePct * (100 + (river ? 40 : 0) + (_c.CoastDistance[cell] <= 2 ? 20 : 0)) / 100;
            if (!Pct((ulong)a + 1_000_000UL, S.Found, pct)) continue;
            bool manor = Pct((ulong)a + 2_000_000UL, S.Found, _s.ManorPct);
            int pop = initial ? Math.Min(_s.MinorMaxPop, 30 + rural / 40 + Roll((ulong)a, S.Found, 0, 60)) : 14 + Roll((ulong)a, S.Found, 0, 24);
            AddSettlement(cell, pop, o, null, minor: true, manor: manor);
            if (!initial) _pop[cell] = Math.Max(0, _pop[cell] - 6);
        }
    }

    // ---------- disasters ----------
    private bool _plagueActive;
    private string _plagueName = "";
    private EntityId _plagueStart;
    private int _plagueDead, _plagueTowns, _plagueLastInfect, _plagueCell;
    private readonly List<int> _inf = [];
    private static readonly string[] PlagueNames = ["The Grey Death", "The Sweating Sickness", "The Red Plague", "The Wasting", "The Black Cough", "The Pale Fever"];

    private int InfTick(int i) { while (_inf.Count <= i) _inf.Add(-1); return _inf[i]; }
    private void SetInf(int i, int t) { InfTick(i); _inf[i] = t; }

    private void DisasterPass()
    {
        int n = _h.Settlements.Count;
        for (int i = 0; i < n; i++)
        {
            var s = _h.Settlements[i];
            if (s.IsRuin) continue;
            int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
            ulong ent = s.Id.Value;
            // fire (drought years burn thatch faster)
            long fire = _s.FirePermilleYear * 250L * (100 + s.Pop / 20) / 100 * (_badKind[cu] == 1 ? 3 : 1);
            if (Chance(ent, S.Fire, fire)) Fire(i);
            if (s.IsRuin) continue;
            // spring floods on rivers; wet years are worse
            if (_season == 0 && s.OnRiver && Chance(ent, S.Flood, _s.FloodPermilleYear * 1000L * (_badKind[cu] == 3 ? 4 : 1))) Flood(i, cu);
            if (s.IsRuin) continue;
            if (s.Mining > 0 && s.Pop >= 40 && Chance(ent, S.Mine, _s.MineCollapsePermilleYear * 250L)) MineCollapse(i);
        }
        PlagueStep();
        if (_season == 2) FamineStep();
    }

    private void Fire(int i)
    {
        var s = _h.Settlements[i];
        int cu = _c.CultureId[s.Cell] < 8 ? _c.CultureId[s.Cell] : 0;
        bool drought = _badKind[cu] == 1;
        int loss = Roll(s.Id.Value, S.Fire, 15, 46) * s.Pop / 100;
        bool burnt = s.Pop < 150 && Pct(s.Id.Value, S.Fire, 22);
        if (burnt) loss = s.Pop;
        s.Pop -= loss;
        string how = drought ? "drought-dried thatch" : Roll(s.Id.Value, S.Fire, 0, 3) switch { 0 => "a smith's forge left burning", 1 => "lightning in a dry spell", _ => "a baker's oven in a crowded lane" };
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        var ev = Ev("fire", $"The Great Fire of {s.Name}", burnt ? $"Flames consume {s.Name}; of {Num(loss)} souls few remain" : $"Flames sweep {s.Name}; {Num(loss)} perish or flee", s.Cell,
            rn.Value != 0 ? [s.Id, rn] : [s.Id], Cz(CauseKind.Fire, drought ? _weatherEv[cu] : EntityId.None, how));
        s.LastBlow = ev; s.LastBlowTick = _tick;
        foreach (var a in _h.Artefacts)
            if (a.Status == ArtefactStatus.Held && a.Holder == s.Id && Pct(a.Id.Value, S.Art, 35))
                ArtefactVanish(a, ArtefactStatus.Lost, s.Cell, ev, $"burned in the fire of {s.Name}");
        if (burnt || s.Pop < 5) MakeRuin(i, "burned", ev, ev);
    }

    private void Flood(int i, int cu)
    {
        var s = _h.Settlements[i];
        int loss = Roll(s.Id.Value, S.Flood, 8, 31) * s.Pop / 100;
        s.Pop -= loss;
        bool wet = _badKind[cu] == 3;
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        var ev = Ev("flood", $"The Flood at {s.Name}", $"The river rises and drowns the low streets of {s.Name}; {Num(loss)} lost", s.Cell,
            rn.Value != 0 ? [s.Id, rn] : [s.Id], Cz(CauseKind.Weather, wet ? _weatherEv[cu] : EntityId.None, wet ? "a wet year swelled the river" : "spring snowmelt and heavy rain"));
        s.LastBlow = ev; s.LastBlowTick = _tick;
        Devastate(s.Cell, 2, 12);
        if (s.Pop < 5 || (s.Kind == SettlementKind.Hamlet && Pct(s.Id.Value, S.Flood, 12))) MakeRuin(i, "flooded", ev, ev);
    }

    private void MineCollapse(int i)
    {
        var s = _h.Settlements[i];
        int x = Cx(s.Cell), y = Cy(s.Cell), vein = s.Cell;
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) if (In(x + dx, y + dy) && _metal[(y + dy) * N + x + dx]) vein = (y + dy) * N + x + dx;
        int loss = Roll(s.Id.Value, S.Mine, 3, 9) * s.Pop / 100;
        s.Pop -= loss;
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        var ev = Ev("mine_collapse", $"The Mine Collapse near {s.Name}", $"The galleries above {s.Name} fall in; {Num(loss)} miners and their families are lost", vein,
            rn.Value != 0 ? [s.Id, rn] : [s.Id], Cz(CauseKind.MineFlood, EntityId.None, Roll(s.Id.Value, S.Mine, 0, 2) == 0 ? "rotten timbering and rising water" : "firedamp in the deep galleries"));
        _h.MineCollapses.Add((vein, ev));
        s.LastBlow = ev; s.LastBlowTick = _tick;
        s.Mining = 0;
        if (s.Pop < 5) MakeRuin(i, "collapse", ev, ev);
    }

    private void PlagueStep()
    {
        if (!_plagueActive)
        {
            if (_season > 1 || !Chance(0, S.Plague, _s.PlaguePermilleYear * 500L)) return;
            // origin: weighted by population among towns and cities (the hubs of trade)
            long tot = 0;
            for (int i = 0; i < _h.Settlements.Count; i++) { var s = _h.Settlements[i]; if (!s.IsRuin && s.Kind >= SettlementKind.Town) tot += s.Pop; }
            if (tot == 0) return;
            long pick = Roll(1, S.Plague, 0, (int)Math.Min(int.MaxValue, tot)); int origin = -1;
            for (int i = 0; i < _h.Settlements.Count; i++)
            {
                var s = _h.Settlements[i];
                if (s.IsRuin || s.Kind < SettlementKind.Town) continue;
                pick -= s.Pop;
                if (pick < 0) { origin = i; break; }
            }
            if (origin < 0) return;
            var o = _h.Settlements[origin];
            _plagueName = PlagueNames[Roll(2, S.Plague, 0, PlagueNames.Length)];
            _plagueActive = true; _plagueDead = 0; _plagueTowns = 0; _plagueCell = o.Cell;
            for (int i = 0; i < _h.Settlements.Count; i++) SetInf(i, -1);
            _plagueStart = Ev("plague_begins", $"{_plagueName} begins", $"A sickness breaks out in {o.Name} and follows the trade roads", o.Cell,
                o.Realm >= 0 ? [o.Id, Rm(o.Realm).Id] : [o.Id], Cz(CauseKind.Other, EntityId.None, "a sickness carried in on the trade roads"));
            Infect(origin);
            _plagueLastInfect = _tick;
            return;
        }
        // spread along trade routes from settlements infected last tick
        var fresh = new List<int>();
        foreach (var rt in _h.TradeRoutes)
        {
            if (rt.Abandoned) continue;
            int a = SIdx(rt.A), b = SIdx(rt.B);
            for (int pass = 0; pass < 2; pass++)
            {
                int from = pass == 0 ? a : b, to = pass == 0 ? b : a;
                if (InfTick(from) == _tick - 1 && InfTick(to) < 0 && !fresh.Contains(to) && !_h.Settlements[to].IsRuin
                    && Pct(rt.A.Value ^ rt.B.Value, S.Plague, _s.PlagueSpreadPct)) fresh.Add(to);
            }
        }
        fresh.Sort();
        foreach (var t in fresh) { Infect(t); _plagueLastInfect = _tick; }
        if (_tick - _plagueLastInfect >= 2)
        {
            _plagueActive = false;
            var ev = Ev("plague_ends", $"{_plagueName} ends", $"The sickness burns itself out after reaching {_plagueTowns} {Plural(_plagueTowns, "settlement", "settlements")}; about {Num(_plagueDead)} dead", _plagueCell,
                [], Cz(CauseKind.Death, _plagueStart, "the plague runs its course"));
        }
    }

    private void Infect(int i)
    {
        var s = _h.Settlements[i];
        SetInf(i, _tick);
        _plagueTowns++;
        int mort = s.Kind >= SettlementKind.Town ? _s.PlagueTownMortalityPct : _s.PlagueTownMortalityPct * 55 / 100;
        mort = Math.Max(3, mort * Roll(s.Id.Value, S.Plague, 70, 131) / 100);
        int dead = s.Pop * mort / 100;
        if (s.Minor && Pct(s.Id.Value, S.Plague, _s.PlagueEmptyPct)) { PlagueEmpties(i); return; }
        s.Pop -= dead;
        int rural = Kill(s.Cell, RuralPop25(s) * 5 / 100, 2);
        _plagueDead += dead + rural;
        s.LastBlow = _plagueStart; s.LastBlowTick = _tick;
        if (s.Kind >= SettlementKind.Town && s.Id.Value != 0 && _plagueTowns > 1)
            Ev("plague_spreads", $"{_plagueName} reaches {s.Name}", $"{Num(dead)} die in {s.Name}", s.Cell,
                s.Realm >= 0 ? [s.Id, Rm(s.Realm).Id] : [s.Id], Cz(CauseKind.Other, _plagueStart, "contagion along the trade roads"));
        if (s.Pop < 5) MakeRuin(i, "plague", _plagueStart, _plagueStart);
        else if (Pct(s.Id.Value, S.Saint, 12)) MaybeSaint(i, _plagueStart, _plagueName);
        foreach (var n in _h.Notables)
            if (n.Alive && n.Residence == i && Pct(n.Id.Value, S.Death, mort / 2)) Die(n, "the " + _plagueName.ToLowerInvariant().Replace("the ", ""), _plagueStart);
    }

    /// <summary>The sickness takes a small settlement whole: the survivors flee and none come back.</summary>
    private void PlagueEmpties(int i)
    {
        var s = _h.Settlements[i];
        int dead = s.Pop * 2 / 3;
        _plagueDead += dead + Kill(s.Cell, RuralPop25(s) * 3 / 100, 2);
        var rn = s.Realm >= 0 ? Rm(s.Realm).Id : EntityId.None;
        var ev = Ev("settlement_emptied", $"{s.Name} is emptied by the sickness", $"{_plagueName} leaves {s.Name} with {Num(dead)} dead; the few who live flee and no one will return to a plague house",
            s.Cell, rn.Value != 0 ? [s.Id, rn] : [s.Id], Cz(CauseKind.Death, _plagueStart, "the contagion along the roads"));
        s.LastBlow = ev; s.LastBlowTick = _tick;
        MakeRuin(i, "plague", ev, _plagueStart);
    }

    private void FamineStep()
    {
        for (int c = 0; c < _c.Cultures.Count && c < 8; c++)
        {
            if (_badKind[c] == 0 || _culK[c] == 0) continue;
            int pressure = (int)((long)_culPop[c] * 100 / _culK[c]);
            if (!(pressure >= 62 || _badStreak[c] >= 1 || Pct((ulong)c, S.Famine, 22))) continue;
            int lossPct = Roll((ulong)c, S.Famine, Math.Max(3, _s.FamineMaxLossPct / 2), _s.FamineMaxLossPct + 1) * (100 + _badStreak[c] * 25) / 100;
            if (_badKind[c] == 1) lossPct = lossPct * 115 / 100;
            long dead = 0;
            for (int i = 0; i < A; i++)
            {
                if (_c.CultureId[i] != c || _owner[i] < 0 || _pop[i] == 0) continue;
                int p = lossPct * (_c.Fertility[i] < 45 ? 130 : 100) / 100;
                int d = _pop[i] * p / 100;
                _pop[i] -= d; dead += d;
            }
            int hardest = -1, hp = -1;
            for (int i = 0; i < _h.Settlements.Count; i++)
            {
                var s = _h.Settlements[i];
                if (s.IsRuin || _c.CultureId[s.Cell] != c) continue;
                int d = s.Pop * lossPct * 7 / 1000;
                s.Pop -= d; dead += d;
                if (s.Pop > hp) { hp = s.Pop; hardest = i; }
            }
            string cu = Culture(c);
            string what = _badKind[c] switch { 1 => "drought", 2 => "cold", _ => "rotting rains" };
            var seat = hardest >= 0 ? _h.Settlements[hardest] : null;
            var subj = _h.Realms.Where(r => r.Alive && r.CultureId == c).Select(r => r.Id).Take(6).ToList();
            if (seat != null) subj.Insert(0, seat.Id);
            string title = _badStreak[c] > 0 ? $"The Hunger of {_year}" : $"The {cu} Famine";
            var ev = Ev("famine", title, $"After the {what}, the harvest fails across the {cu} lands; some {Num((int)Math.Min(dead, int.MaxValue))} starve", seat?.Cell ?? _c.Cultures[c].SeedCell,
                subj.ToArray(), Cz(CauseKind.Famine, _weatherEv[c], $"harvest failure after the {what}"));
            for (int i = 0; i < _h.Settlements.Count; i++)
            {
                var s = _h.Settlements[i];
                if (s.IsRuin || _c.CultureId[s.Cell] != c) continue;
                s.LastBlow = ev; s.LastBlowTick = _tick;
                if (s.Minor && Pct(s.Id.Value, S.Famine, _s.FamineEmptyPct * (100 + _badStreak[c] * 40) / 100))
                {
                    var starved = Ev("settlement_starved", $"{s.Name} starves", $"Hunger empties {s.Name}; the survivors take to the roads", s.Cell, [s.Id], Cz(CauseKind.Famine, ev, "the famine's toll on a small place"));
                    MakeRuin(i, "famine", starved, ev);
                }
            }
            foreach (var r in _h.Realms)
                if (r.Alive && r.CultureId == c) { r.Unrest = Math.Min(100, r.Unrest + 15); r.LastFamineTick = _tick; }
            if (hardest >= 0) MaybeSaint(hardest, ev, "the famine");
            if (Pct((ulong)c, S.Outlaw, 45)) SpawnOutlaw(hardest >= 0 ? hardest : 0, ev, "famine");
        }
    }

    // ---------- population helpers ----------
    private int Kill(int cell, int total, int radius)
    {
        if (total <= 0) return 0;
        int x = Cx(cell), y = Cy(cell); long sum = 0;
        for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++) if (In(x + dx, y + dy)) sum += _pop[(y + dy) * N + x + dx];
        if (sum == 0) return 0;
        int killed = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!In(x + dx, y + dy)) continue;
                int j = (y + dy) * N + x + dx;
                int take = (int)Math.Min(_pop[j], (long)_pop[j] * total / sum);
                _pop[j] -= take; killed += take;
            }
        return killed;
    }

    private void Devastate(int cell, int radius, int pct)
    {
        int x = Cx(cell), y = Cy(cell);
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (In(x + dx, y + dy)) { int j = (y + dy) * N + x + dx; _pop[j] -= _pop[j] * pct / 100; }
    }

    /// <summary>Contiguous cells owned by <paramref name="owner"/> around <paramref name="start"/>, nearest first.</summary>
    private List<int> Region(int start, int owner, int max)
    {
        var list = new List<int>(); int stamp = ++_stampN; var q = new Queue<int>();
        if (_owner[start] != owner) return list;
        _stamp[start] = stamp; q.Enqueue(start);
        while (q.Count > 0 && list.Count < max)
        {
            int cur = q.Dequeue(); list.Add(cur);
            int x = Cx(cur), y = Cy(cur);
            for (int d = 0; d < 4; d++)
            {
                int nx = x + D4x[d], ny = y + D4y[d];
                if (!In(nx, ny)) continue;
                int j = ny * N + nx;
                if (_stamp[j] == stamp || _owner[j] != owner) continue;
                _stamp[j] = stamp; q.Enqueue(j);
            }
        }
        return list;
    }

    private void Transfer(List<int> cells, int to)
    {
        foreach (var i in cells)
        {
            int from = _owner[i];
            if (from == to) continue;
            if (from >= 0) Rm(from).Cells--;
            Rm(to).Cells++;
            _owner[i] = to;
            int si = _settleAt[i];
            if (si >= 0) _h.Settlements[si].Realm = to;
        }
    }

    // ---------- trade routes ----------
    private int[] _rdist = new int[A], _rprev = new int[A], _rstamp = new int[A];
    private int _rgen;

    private int RouteCost(int i)
    {
        if (_c.Ocean[i]) return -1;
        if (_c.Lake[i]) return 8;
        if (_c.RiverFlow[i] >= _c.Settings.StreamFlowThreshold) return 4;
        return _c.Biomes[i] switch { Biome.Mountain => 40, Biome.Marsh => 25, Biome.TemperateForest or Biome.Taiga => 14, Biome.Desert => 14, _ => 10 };
    }

    private void RoutesPass()
    {
        var existing = new HashSet<(ulong, ulong)>();
        foreach (var rt in _h.TradeRoutes) if (!rt.Abandoned) existing.Add((Math.Min(rt.A.Value, rt.B.Value), Math.Max(rt.A.Value, rt.B.Value)));
        int n = _h.Settlements.Count;
        for (int si = 0; si < n; si++)
        {
            var s = _h.Settlements[si];
            if (s.IsRuin || s.Pop < 60) continue;
            int gen = ++_rgen, found = 0;
            var pq = new PriorityQueue<int, long>();
            _rstamp[s.Cell] = gen; _rdist[s.Cell] = 0; _rprev[s.Cell] = -1;
            pq.Enqueue(s.Cell, s.Cell);
            while (pq.Count > 0 && found < 2)
            {
                int cur = pq.Dequeue();
                int cd = _rdist[cur];
                int ti = _settleAt[cur];
                if (cur != s.Cell && ti >= 0 && !_h.Settlements[ti].IsRuin && _h.Settlements[ti].Pop >= 60 && cd > 0)
                {
                    var t = _h.Settlements[ti];
                    var key = (Math.Min(s.Id.Value, t.Id.Value), Math.Max(s.Id.Value, t.Id.Value));
                    found++;
                    if (existing.Add(key))
                    {
                        var path = new List<int>();
                        for (int p = cur; p >= 0; p = _rprev[p]) path.Add(p);
                        path.Reverse();
                        var rt = new TradeRoute { A = s.Id, B = t.Id, Cost = cd, Cells = path.ToArray(), FoundedTick = _tick };
                        _h.TradeRoutes.Add(rt);
                        s.RouteCount++; t.RouteCount++;
                        if (s.Kind >= SettlementKind.Town && t.Kind >= SettlementKind.Town && _tick > 0)
                            Ev("trade_route", $"The {s.Name} road", $"Merchants link {s.Name} and {t.Name} by a road of {path.Count} leagues", s.Cell,
                                [s.Id, t.Id], Cz(CauseKind.Other, EntityId.None, "growing trade between the two towns"));
                    }
                    continue;
                }
                if (cd > 600) continue;
                int x = Cx(cur), y = Cy(cur);
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + D4x[d], ny = y + D4y[d];
                    if (!In(nx, ny)) continue;
                    int j = ny * N + nx, rc = RouteCost(j);
                    if (rc < 0) continue;
                    int nd = cd + rc;
                    if (_rstamp[j] == gen && _rdist[j] <= nd) continue;
                    _rstamp[j] = gen; _rdist[j] = nd; _rprev[j] = cur;
                    pq.Enqueue(j, ((long)nd << 16) | (uint)j);
                }
            }
        }
    }
}
