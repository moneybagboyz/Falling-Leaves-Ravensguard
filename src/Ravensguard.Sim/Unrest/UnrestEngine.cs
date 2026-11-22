using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Unrest;

/// <summary>
/// The daily and hourly passes of unrest (unrest-and-revolt 4.1 to 4.3, 4.5): at hour 0 each household's grievances are counted, each settlement's unrest and rung set, a riot or a revolt raised when the ladder says so;
/// every hour the riots that are gathering or acting take their step and a revolt whose day has come is fought. Everything is recomputed from the saved book and the live world, so a resumed run sees what an uninterrupted one saw.
/// People who die die through <see cref="Demography.Kill"/> with a cause chain back to the riot; coin and grain move by ledger transfers and store moves.
/// </summary>
public static class UnrestEngine
{
    /// <summary>Grams of grain a mite buys, for the value a theft is written at (the law measures a bounty by it; nothing here moves coin for it).</summary>
    public const int GramsPerMite = 250;
    public const int RevoltWithinDays = 3, RevoltFightHour = 12;

    // ------------------------------------------------------------------ the people of a settlement

    /// <summary>A settlement's households with members (ascending index) and its adults (ascending row), built once a pass.</summary>
    public sealed class Census
    {
        public readonly Dictionary<int, List<int>> Households = [];
        public readonly Dictionary<int, List<int>> Adults = [];
    }

    public static Census TakeCensus(SimWorld w, int adultAge, int day)
    {
        var c = new Census(); var hht = w.Households; var pt = w.People;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Settlement[h] < 0) continue;
            if (!c.Households.TryGetValue(hht.Settlement[h], out var l)) c.Households[hht.Settlement[h]] = l = [];
            l.Add(h);
        }
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || pt.Household[i] < 0 || pt.Household[i] >= hht.Count || hht.MemberCount[pt.Household[i]] == 0) continue;
            if (pt.AgeYears(i, day) < adultAge) continue;
            int s = hht.Settlement[pt.Household[i]]; if (s < 0) continue;
            if (!c.Adults.TryGetValue(s, out var l)) c.Adults[s] = l = [];
            l.Add(i);
        }
        return c;
    }

    // ------------------------------------------------------------------ a household's points

    /// <summary>The conditions that apply to every household of a settlement today.</summary>
    public readonly record struct Place(int Settlement, int Fief, TaxLevel Level, bool StandingLow, bool Banned, bool Slavery, bool Dole);

    public static Place PlaceOf(UnrestCtx c, int s)
    {
        int f = c.FiefOfSettlement(s);
        var tax = c.W.Get<EconomyWorld>()?.Taxes;
        var level = tax != null && f >= 0 ? tax.LevelOfFief(f) : TaxLevel.Normal;
        long lord = f >= 0 ? c.LordOf(f) : -1;
        bool low = lord >= 0 && c.Links.StandingWithSettlement(new EntityId((ulong)lord), s) < c.S.StandingBelow;
        bool dole = f >= 0 && c.B.DoleUntil.TryGetValue(f, out int until) && until > c.Day;
        return new Place(s, f, level, low, f >= 0 && c.Links.LordBanned(f), c.Links.SlaveryUnlawful(s), dole);
    }

    public static HouseholdInputs InputsOf(UnrestCtx c, in Place p, int h)
    {
        var e = c.W.Economy; var g = e.Grievances[h];
        var id = c.W.Households.IdOf(h);
        return new HouseholdInputs((g & Grievance.Hunger) != 0, p.Level, c.RB.GrievancePoints(id.Value, c.Day), (g & Grievance.Crowding) != 0,
            (g & Grievance.Crime) != 0 || c.Links.CrimeVictim(h), p.StandingLow, p.Banned, p.Slavery, p.Dole);
    }

    /// <summary>A household's points today and its kinds.</summary>
    public static (int Points, List<(string Kind, int Points)> Kinds) PointsOf(UnrestCtx c, in Place p, int h)
    {
        var id = c.W.Households.IdOf(h).Value; var inp = InputsOf(c, p, h);
        var kinds = UnrestRules.Breakdown(c.B, inp, c.B.Live(id, c.Day), out int total);
        return (total, kinds);
    }

    /// <summary>A cheap bound on a household's points, so the daily pass builds a breakdown only for households that could be aggrieved.</summary>
    private static int UpperBound(UnrestCtx c, in HouseholdInputs i, ulong id)
    {
        int n = (i.Hungry && !i.Dole ? 2 : 0) + (i.Level == TaxLevel.Crushing ? 2 : i.Level == TaxLevel.High ? 1 : 0) + Math.Min(2, i.LevyPoints) + (i.Crowded ? 1 : 0) + (i.CrimeVictim ? 1 : 0)
            + (i.StandingLow ? 1 : 0) + (i.Banned ? 2 : 0) + (i.Slavery ? 1 : 0);
        if (c.B.Rows.Count > 0) foreach (var r in c.B.Live(id, c.Day)) n += r.Points;
        return n;
    }

    /// <summary>One settlement's tally: its households, the aggrieved among them with their kinds, and how many carry each kind.</summary>
    public sealed class Assessment
    {
        public int Households;
        public readonly List<int> Aggrieved = [];
        public readonly SortedDictionary<string, int> Counts = new(StringComparer.Ordinal);
        /// <summary>The first chronicle entry among the aggrieved households' rows of each kind: the event that raised it.</summary>
        public readonly Dictionary<string, ulong> Entries = new(StringComparer.Ordinal);
        public readonly HashSet<int> AggrievedSet = [];
    }

    public static Assessment Assess(UnrestCtx c, int s, IReadOnlyList<int> households)
    {
        var a = new Assessment { Households = households.Count };
        var p = PlaceOf(c, s); var hht = c.W.Households;
        foreach (int h in households)
        {
            var id = hht.IdOf(h).Value; var inp = InputsOf(c, p, h);
            if (UpperBound(c, inp, id) < c.S.AggrievedAt) continue;
            var kinds = UnrestRules.Breakdown(c.B, inp, c.B.Live(id, c.Day), out int total);
            if (!UnrestRules.Aggrieved(c.S, total)) continue;
            a.Aggrieved.Add(h); a.AggrievedSet.Add(h);
            foreach (var (k, _) in kinds)
            {
                a.Counts[k] = a.Counts.GetValueOrDefault(k) + 1;
                if (!a.Entries.ContainsKey(k)) foreach (var r in c.B.Live(id, c.Day)) if (r.Kind == k && r.Entry != 0) { a.Entries[k] = r.Entry; break; }
            }
        }
        return a;
    }

    /// <summary>Whether a household pays no hearth tax this season (the tax's hook, UR-2): its settlement is at the evasion rung, it is aggrieved, and the draw (stream 0x682, key household and season) falls under the percent.</summary>
    public static bool Evades(UnrestCtx c, int s, int h)
    {
        if (c.B.RungOf(s) < 2) return false;
        var p = PlaceOf(c, s);
        if (!UnrestRules.Aggrieved(c.S, PointsOf(c, p, h).Points)) return false;
        return DetRng.Range(c.Seed, c.SeasonIndex, c.W.Households.IdOf(h).Value, RngStreams.RevoltAndEvasion, 1, 0, 100) < c.S.EvasionPct;
    }

    /// <summary>
    /// Whether a household hides the man the levy asked for (P7-AM-B, armies-on-the-map 4.2): the same rung-2 test as <see cref="Evades"/> (an aggrieved household of a settlement at the evasion rung), with its own draw (stream 0x682, draw 2,
    /// key the household and the season) so the households that dodge the muster are not the very ones that dodge the hearth tax. The draw hides the man; it adds no grievance of its own.
    /// </summary>
    public static bool EvadesLevy(UnrestCtx c, int s, int h)
    {
        if (c.B.RungOf(s) < 2) return false;
        var p = PlaceOf(c, s);
        if (!UnrestRules.Aggrieved(c.S, PointsOf(c, p, h).Points)) return false;
        return DetRng.Range(c.Seed, c.SeasonIndex, c.W.Households.IdOf(h).Value, RngStreams.RevoltAndEvasion, 2, 0, 100) < c.S.EvasionPct;
    }

    // ------------------------------------------------------------------ the daily pass (H5 hour 0)

    /// <summary>The day's pass, once for a day number: grievances counted, unrest and rungs set, riots and revolts raised.</summary>
    public static void Daily(UnrestCtx c)
    {
        var b = c.B; if (b.LastDay == c.Day) return;
        b.LastDay = c.Day; b.Prune(c.Day);
        var census = TakeCensus(c.W, c.S.AdultAge, c.Day);
        foreach (var s in census.Households.Keys.OrderBy(x => x))
        {
            var hs = census.Households[s]; int f = c.FiefOfSettlement(s);
            var a = Assess(c, s, hs);
            var su = b.Peek(s) ?? new SettlementUnrest();
            bool fear = c.Day < su.FearUntil;
            int u = UnrestRules.UnrestPm(c.S, a.Aggrieved.Count, a.Households, fear);
            int old = su.Rung, rung = UnrestRules.RungFor(c.S, u);
            su.Unrest = u; su.Rung = rung;
            su.HighDays = u >= c.S.Rung3 ? su.HighDays + 1 : 0;
            su.TopDays = u >= c.S.Rung4 ? su.TopDays + 1 : 0;
            if (rung > old && rung >= 1) RungRose(c, s, f, su, rung, u, a);
            if (rung >= 1 && su.GrumbleSeason != c.SeasonIndex && f >= 0 && c.LordOf(f) >= 0) Grumble(c, s, f, su, u, a);
            bool trigger = su.TriggerDay >= 0 && c.Day - su.TriggerDay <= 2;
            if (su.TriggerDay >= 0 && c.Day - su.TriggerDay > 2) { su.TriggerDay = -1; su.TriggerKind = ""; }
            if (b.ActiveRiot(s) == null && c.Day >= su.CooldownUntil && UnrestRules.RiotDue(c.S, u, su.HighDays, trigger) && a.Aggrieved.Count > 0)
            {
                StartRiot(c, s, f, su, a, census, trigger ? su.TriggerKind : "");
                su.TriggerDay = -1; su.TriggerKind = ""; su.HighDays = 0;
            }
            if (f >= 0 && UnrestRules.RevoltDue(c.S, u, su.TopDays, 0) && !b.Revolts.Any(v => v.Fief == f && v.State == RevoltState.Pending)) StartRevolt(c, f, s, null, a, census);
            if (su.IsDefault) b.Settlements.Remove(s); else b.Settlements[s] = su;
        }
        foreach (var s in b.Settlements.Keys.ToList())              // a settlement that has no households any more keeps nothing
            if (!census.Households.ContainsKey(s)) b.Settlements.Remove(s);
    }

    /// <summary>The top grievances of a count table, largest first (ties by kind id), as chronicle causes: each names how many households carry it and, where the event is known, the entry that raised it.</summary>
    public static List<Cause> CausesOf(UnrestCtx c, Assessment a, int take = 3)
    {
        var causes = new List<Cause>();
        foreach (var (k, n) in Ranked(c, a).Take(take))
        {
            var def = c.B.Kinds[k];
            var kind = k == "hunger" ? CauseKind.Famine : k.StartsWith("tax", StringComparison.Ordinal) ? CauseKind.Tax : CauseKind.Unrest;
            causes.Add(new Cause(kind, a.Entries.TryGetValue(k, out ulong e) ? new EntityId(e) : null, "", def.NoteKey, [ChronParam.Count("count", n)]));
        }
        if (causes.Count == 0) causes.Add(new Cause(CauseKind.Unrest, null, "", "ch_note_ur_unknown"));
        return causes;
    }

    /// <summary>The counts largest first; a tie goes to the kind that weighs more points, then by id.</summary>
    public static IEnumerable<(string Kind, int Count)> Ranked(UnrestCtx c, Assessment a) =>
        a.Counts.Select(x => (Kind: x.Key, Count: x.Value)).OrderByDescending(x => x.Count).ThenByDescending(x => c.B.Kinds.TryGetValue(x.Kind, out var d) ? d.Points : 0).ThenBy(x => x.Kind, StringComparer.Ordinal);

    private static void RungRose(UnrestCtx c, int s, int f, SettlementUnrest su, int rung, int u, Assessment a)
    {
        var place = c.PlaceId(s);
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Count("rung", rung), ChronParam.Count("unrest", u), ChronParam.Count("households", a.Aggrieved.Count) };
        c.Chron.Chron(c.Now, "unrest_rung", "ch_ur_rung", ps, [place], CausesOf(c, a));
    }

    private static void Grumble(UnrestCtx c, int s, int f, SettlementUnrest su, int u, Assessment a)
    {
        su.GrumbleSeason = c.SeasonIndex;
        var lord = new EntityId((ulong)c.LordOf(f)); var place = c.PlaceId(s);
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Person("lord", lord), ChronParam.Count("unrest", u) };
        var e = c.Chron.Chron(c.Now, "unrest_grumbling", "ch_ur_grumbling", ps, [place, lord], CausesOf(c, a));
        c.Links.Deed?.Invoke(lord, "unrest_grumbling", e, s, c.Day);
    }

    // ------------------------------------------------------------------ riots

    /// <summary>The leader of a crowd: the aggrieved adult with the highest Leadership plus Speech; an exact tie of equal skills is drawn (stream 0x680, key settlement and day). Returns the person row.</summary>
    public static int LeaderOf(UnrestCtx c, int s, IReadOnlyList<int> adultRows)
    {
        var pt = c.W.People; int best = int.MinValue; var tied = new List<int>();
        foreach (int r in adultRows)
        {
            int score = pt.SkillLevelOf(r, Skill.Leadership) + pt.SkillLevelOf(r, Skill.Speech);
            if (score > best) { best = score; tied.Clear(); tied.Add(r); } else if (score == best) tied.Add(r);
        }
        if (tied.Count == 0) return -1;
        if (tied.Count == 1) return tied[0];
        tied.Sort((x, y) => pt.IdOf(x).Value.CompareTo(pt.IdOf(y).Value));
        ulong key = c.PlaceId(s).Value != 0 ? c.PlaceId(s).Value : (ulong)(uint)s;
        return tied[DetRng.Range(c.Seed, c.Day, key, RngStreams.UnrestLeaderTies, 0, 0, tied.Count)];
    }

    public static Riot? StartRiot(UnrestCtx c, int s, int f, SettlementUnrest su, Assessment a, Census census, string triggerKind)
    {
        var pt = c.W.People; var hht = c.W.Households;
        var rows = new List<int>();
        long lordId = f >= 0 ? c.LordOf(f) : -1;
        if (census.Adults.TryGetValue(s, out var adults)) foreach (int r in adults) if (a.AggrievedSet.Contains(pt.Household[r]) && (long)pt.IdOf(r).Value != lordId) rows.Add(r);
        if (rows.Count == 0) return null;
        int leaderRow = LeaderOf(c, s, rows); if (leaderRow < 0) return null;
        ulong leader = pt.IdOf(leaderRow).Value;
        var riot = new Riot { Id = c.B.NextRiot++, Settlement = s, Fief = f, Leader = leader, StartDay = c.Day, UnrestPm = su.Unrest };
        // the crowd: each aggrieved adult joins with the crowd's share (stream 0x681, key person and riot); the leader always does
        int pct = UnrestRules.CrowdPct(c.S, su.Unrest);
        var crowd = new SortedSet<ulong> { leader };
        var order = new List<(ulong Key, ulong Id)>();
        foreach (int r in rows)
        {
            ulong id = pt.IdOf(r).Value; ulong d = DetRng.Next(c.Seed, c.Day, id, RngStreams.RiotJoiners, (uint)riot.Id);
            if ((int)(d % 100) < pct) crowd.Add(id); else order.Add((d, id));
        }
        int need = Math.Min(rows.Count, c.S.CrowdMinPeople);
        foreach (var (_, id) in order.OrderBy(x => x.Key).ThenBy(x => x.Id)) { if (crowd.Count >= need) break; crowd.Add(id); }
        riot.Crowd = [.. crowd];
        riot.Counts = [.. Ranked(c, a)];
        riot.TopKind = riot.Counts.Count > 0 ? riot.Counts[0].Kind : "";
        riot.Target = riot.TopKind.Length > 0 && c.B.Kinds.TryGetValue(riot.TopKind, out var def) ? UnrestRules.TargetOf(def.Target) : RiotTarget.Manor;
        var place = c.PlaceId(s); var lid = new EntityId(leader);
        var causes = CausesOf(c, a);
        if (triggerKind.Length > 0) causes.Insert(0, new Cause(CauseKind.Unrest, null, "", "ch_note_ur_trigger_" + triggerKind));
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Person("leader", lid), ChronParam.Count("unrest", su.Unrest), ChronParam.Count("households", a.Aggrieved.Count),
            ChronParam.TextKey("target", "ur_target_" + riot.Target.ToString().ToLowerInvariant()) };
        riot.RiseEntry = c.Chron.Chron(c.Now, "riot_rises", "ch_ur_riot_rises", ps, [lid, place], causes).Value;
        c.B.Riots.Add(riot);
        return riot;
    }

    // ------------------------------------------------------------------ the hourly step

    public static void Hourly(UnrestCtx c)
    {
        int hod = (int)(c.Hour % 24);
        foreach (var r in c.B.Riots.ToList())
        {
            if (r.State == RiotState.Ended) continue;
            if (r.State == RiotState.Gathering) { if (c.Day > r.StartDay || hod >= c.S.GatherHour) Act(c, r); continue; }
            Step(c, r);
        }
        foreach (var v in c.B.Revolts.ToList())
            if (v.State == RevoltState.Pending && (c.Day > v.ResolveDay || (c.Day == v.ResolveDay && hod >= RevoltFightHour))) Fight(c, v);
    }

    private static List<int> LiveCrowd(UnrestCtx c, Riot r)
    {
        var pt = c.W.People; var rows = new List<int>();
        foreach (ulong id in r.Crowd) { int row = pt.IndexOf(new EntityId(id)); if (row >= 0 && pt.IsAlive(row)) rows.Add(row); }
        return rows;
    }

    /// <summary>The settlement's guards on their feet (rows ascending).</summary>
    public static List<int> GuardsOf(UnrestCtx c, int s)
    {
        if (c.Links.Guards != null) return [.. c.Links.Guards(s).Where(r => c.W.People.IsAlive(r))];
        var pt = c.W.People; var l = new List<int>();
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.Occupation[i] == Occupation.Guard && pt.WorkSettlement[i] == s) l.Add(i);
        return l;
    }

    private static IReadOnlyList<EntityId> Witnesses(UnrestCtx c, int s)
    {
        var g = GuardsOf(c, s);
        if (g.Count > 0) return [c.W.People.IdOf(g[0])];
        int f = c.FiefOfSettlement(s);
        if (f >= 0 && c.RB.OfficeOf(OfficeKind.Reeve, f) is { } o) return [new EntityId((ulong)o.Holder)];
        return [];
    }

    private static void Crime(UnrestCtx c, Riot r, string kind, ulong doer, ulong victim, long value, string note, bool report)
    {
        var w = Witnesses(c, r.Settlement); if (w.Count == 0 || c.Links.Crime == null) return;
        c.Links.Crime(new RiotCrime(kind, new EntityId(doer), new EntityId(victim), r.Settlement, c.Hour * 3600, value, w, note, r.RiseEntry, report || doer == r.Leader));
    }

    /// <summary>The crowd gathers at the market square at the gather hour and goes for its target (4.2).</summary>
    public static void Act(UnrestCtx c, Riot r)
    {
        var crowd = LiveCrowd(c, r);
        r.State = RiotState.Active; r.ActedHour = c.Hour;
        if (crowd.Count == 0) { End(c, r, RiotEnd.BurnedOut); return; }
        var pt = c.W.People; var hht = c.W.Households; var e = c.W.Economy;
        int s = r.Settlement, f = r.Fief;
        var households = crowd.Select(x => pt.Household[x]).Where(h => h >= 0 && h < hht.Count).Distinct().Order().ToList();
        switch (r.Target)
        {
            case RiotTarget.Barn:
            {
                Store? src = null;
                if (f >= 0 && f < c.W.Population.FiefLordHousehold.Length && c.W.Population.FiefLordHousehold[f] is int lh and >= 0 && e.OtherStores.TryGetValue(FarmIds.Barn(lh), out var barn)) src = barn;
                if (src == null && e.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var gr)) src = gr;
                long moved = 0;
                if (src != null && households.Count > 0)
                    foreach (ushort g in e.Food.Staples)
                    {
                        if (!e.Food.IsGrain(g)) continue;
                        long take = src.Qty(g) * c.S.RiotBarnPct / 100; if (take <= 0) continue;
                        long each = take / households.Count, rest = take - each * households.Count;
                        for (int i = 0; i < households.Count; i++) { long q = each + (i == 0 ? rest : 0); if (q > 0) moved += Store.MoveUpTo(src, e.HouseholdStore[households[i]], g, q); }
                    }
                r.LootGrams += moved;
                long value = Math.Max(1, moved / GramsPerMite) / Math.Max(1, crowd.Count);
                if (moved > 0) foreach (int row in crowd) Crime(c, r, "theft", pt.IdOf(row).Value, 0, value, "ch_note_crime_riot", false);
                break;
            }
            case RiotTarget.Reeve:
            {
                var reeve = f >= 0 ? c.RB.OfficeOf(OfficeKind.Reeve, f) ?? c.RB.OfficeOf(OfficeKind.Bailiff, f) : null;
                if (reeve != null) Crime(c, r, "assault", r.Leader, (ulong)reeve.Holder, 0, "ch_note_crime_riot", false);
                int slot = c.R.T != null && f >= 0 && f < c.R.T.FiefSlot.Length ? c.R.T.FiefSlot[f] : -1;
                if (slot >= 0 && households.Count > 0)
                {
                    var chest = c.R.T![slot].Purse; long take = c.R.Led.Balance(chest).Mites * c.S.RiotChestPct / 100; long got = 0;
                    long each = take / households.Count;
                    if (each > 0) foreach (int h in households) if (c.R.Led.Transfer(chest, e.HouseholdPurse[h], new Coin(each), TransferReason.RiotLoot)) got += each;
                    r.LootMites += got;
                    if (got > 0) Crime(c, r, "theft", r.Leader, 0, got, "ch_note_crime_riot", false);
                }
                break;
            }
            case RiotTarget.Manor:
                if (TryArson(c, r)) Crime(c, r, "arson", r.Leader, 0, 0, "ch_note_crime_riot", false);
                break;
            case RiotTarget.Gaol:
            {
                var freed = c.Links.Jailbreak?.Invoke(s, c.Hour * 3600) ?? [];
                if (freed.Count > 0) Crime(c, r, "jailbreak", r.Leader, 0, 0, "ch_note_crime_riot", false);
                break;
            }
        }
    }

    /// <summary>Lights the building nearest the settlement's centre (the manor stands at the heart of its place) through B5's fire world, with the riot as the cause; false without a fire world or a burnable building.</summary>
    private static bool TryArson(UnrestCtx c, Riot r)
    {
        var fw = c.W.Get<Ravensguard.Sim.Burning.FireWorld>(); if (fw == null || c.Ob == null || (uint)r.Settlement >= (uint)c.R.Region.Settlements.Count) return false;
        var st = c.R.Region.Settlements[r.Settlement]; var ob = c.Ob; var prev = fw.Chronicle;
        fw.Chronicle = (when, type, key, ps, subj, causes, data) => ob.Chronicle(when, type, key, ps, subj, causes, data);
        try
        {
            int pick = -1; long bestD = long.MaxValue;
            foreach (var p in fw.Profiles())
            {
                if (p.FlammableCells == 0 || fw.Book.Buildings.ContainsKey(p.Structure)) continue;
                long dx = p.CentreX - st.CenterX, dy = p.CentreY - st.CenterY, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; pick = p.Structure; }
            }
            return pick >= 0 && fw.IgniteBuilding(pick, c.Hour, Ravensguard.Sim.Burning.FireCause.Riot(new EntityId(r.RiseEntry)));
        }
        finally { fw.Chronicle = prev; }
    }

    private static void Step(UnrestCtx c, Riot r)
    {
        if (r.Conceded) { End(c, r, RiotEnd.Conceded); return; }
        long elapsed = c.Hour - r.ActedHour;
        int guards = GuardsOf(c, r.Settlement).Count + (r.HeroSide == 3 ? 1 : 0);
        if (guards > 0 && elapsed >= 1) { Disperse(c, r); return; }
        if (elapsed >= c.S.RiotHours) End(c, r, RiotEnd.BurnedOut);
    }

    /// <summary>The guards disperse the crowd with no lethal force; a cruel lord's guards kill one rioter in twelve, at least one (4.2). Each death is by <see cref="Demography.Kill"/> and puts a grievance on the dead man's kin.</summary>
    public static void Disperse(UnrestCtx c, Riot r)
    {
        long lord = r.Fief >= 0 ? c.LordOf(r.Fief) : -1;
        bool cruel = lord >= 0 && LordTraits.Has(LordTraits.Of(c.Seed, (ulong)lord), LordTrait.Cruel);
        if (cruel) Kill(c, r, Math.Max(1, LiveCrowd(c, r).Count / Math.Max(1, c.S.CruelDeathsDiv)));
        End(c, r, RiotEnd.Dispersed);
    }

    /// <summary>Kills <paramref name="n"/> of the crowd (by a hash order that does not favour low ids), each with a cause chain to the riot.</summary>
    public static int Kill(UnrestCtx c, Riot r, int n)
    {
        var pt = c.W.People; var hht = c.W.Households; int done = 0;
        var live = LiveCrowd(c, r).OrderBy(x => DetRng.Next(c.Seed, c.Day, pt.IdOf(x).Value, RngStreams.RiotJoiners, 1000u + (uint)r.Id)).ThenBy(x => x).Take(n).ToList();
        foreach (int row in live)
        {
            int hh = pt.Household[row]; var hid = hh >= 0 && hh < hht.Count ? hht.IdOf(hh).Value : 0UL;
            Demography.Kill(c.W, row, DeathCause.Violence, c.Now, c.Ob!, "", new Cause(CauseKind.Unrest, new EntityId(r.RiseEntry), "", "ch_note_ur_riot_dispersed"));
            if (hid != 0) c.B.Add(hid, "kin_punished", c.Day, r.RiseEntry);
            r.Deaths++; done++;
        }
        return done;
    }

    public static void End(UnrestCtx c, Riot r, RiotEnd how)
    {
        r.State = RiotState.Ended; r.End = how; r.EndedHour = c.Hour;
        var su = c.B.Peek(r.Settlement) ?? new SettlementUnrest();
        su.CooldownUntil = c.Day + c.S.CooldownDays; c.B.Settlements[r.Settlement] = su;
        var place = c.PlaceId(r.Settlement); var lid = new EntityId(r.Leader);
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Person("leader", lid), ChronParam.Count("deaths", r.Deaths), ChronParam.Mites("loot", r.LootMites),
            ChronParam.TextKey("how", "ur_end_" + how.ToString().ToLowerInvariant()) };
        r.EndEntry = c.Chron.Chron(c.Now, "riot_ends", "ch_ur_riot_ends", ps, [lid, place], [new Cause(CauseKind.Unrest, new EntityId(r.RiseEntry), "", "ch_note_ur_riot_ran")]).Value;
        if (r.Fief >= 0 && !r.Bonded && UnrestRules.RevoltDue(c.S, su.Unrest, 0, r.Deaths) && !c.B.Revolts.Any(v => v.Fief == r.Fief && v.State == RevoltState.Pending))
        {
            var census = TakeCensus(c.W, c.S.AdultAge, c.Day);
            var a = Assess(c, r.Settlement, census.Households.GetValueOrDefault(r.Settlement) ?? []);
            StartRevolt(c, r.Fief, r.Settlement, r, a, census);
        }
    }

    // ------------------------------------------------------------------ revolts

    /// <summary>The claimant of a revolt (4.3): a rival with a claim (E3's heirs first), else a neighbouring lord whose opinion of the lord is at or under the line (lowest first), else the riot's leader. Returns (person, kind 1 rival or 2 leader).</summary>
    public static (ulong Claimant, byte Kind) ClaimantOf(UnrestCtx c, int fief, Riot? riot)
    {
        long lord = c.LordOf(fief);
        foreach (ulong p in c.Links.Claimants(fief)) if (p != 0 && (long)p != lord && c.R.Alive((long)p)) return (p, 1);
        ulong best = 0; int bestOp = int.MaxValue;
        foreach (var x in c.RB.Fiefs.Values)
        {
            if (x.Fief == fief || x.Holder < 0 || x.Forfeited || x.Holder == lord || !c.R.Alive(x.Holder) || c.Links.IsHero((ulong)x.Holder)) continue;
            int op = lord >= 0 ? Opinions.Value(c.RB, (ulong)x.Holder, (ulong)lord) : 0;
            if (op <= c.S.ClaimantOpinion && op < bestOp) { best = (ulong)x.Holder; bestOp = op; }
        }
        if (best != 0) return (best, 1);
        return riot != null ? (riot.Leader, (byte)2) : (0, (byte)0);
    }

    public static Revolt? StartRevolt(UnrestCtx c, int fief, int settlement, Riot? riot, Assessment a, Census census)
    {
        var (claimant, kind) = ClaimantOf(c, fief, riot);
        if (claimant == 0) return null;
        var f = c.RB.FiefOf(fief); if (f == null) return null;
        int seat = (uint)fief < (uint)c.R.Region.Fiefs.Count ? c.R.Region.Fiefs[fief].SeatSettlement : settlement; if (seat < 0) seat = settlement;
        var v = new Revolt { Id = c.B.NextRevolt++, Fief = fief, Settlement = seat, StartDay = c.Day, ResolveDay = c.Day + RevoltWithinDays, RiotId = riot?.Id ?? 0, Claimant = claimant, ClaimantKind = kind,
            OldLord = f.Holder < 0 ? 0 : (ulong)f.Holder, Leader = riot?.Leader ?? claimant };
        var pt = c.W.People; var hht = c.W.Households;
        foreach (var s in census.Households.Keys.OrderBy(x => x))
        {
            if (c.FiefOfSettlement(s) != fief) continue;
            var asm = s == settlement ? a : Assess(c, s, census.Households[s]);
            if (!census.Adults.TryGetValue(s, out var adults)) continue;
            foreach (int r in adults)
            {
                if (pt.Sex[r] != Sex.Male || !asm.AggrievedSet.Contains(pt.Household[r]) || (long)pt.IdOf(r).Value == c.LordOf(fief)) continue;
                ulong id = pt.IdOf(r).Value;
                if (DetRng.Next(c.Seed, c.Day, id, RngStreams.RevoltAndEvasion, 100u + (uint)v.Id) % (ulong)Math.Max(1, c.S.LevyOneIn) == 0) v.Rebels.Add(id);
            }
        }
        v.Rebels.Sort(); v.Rebels0 = v.Rebels.Count;
        if (v.Rebels.Count == 0 && riot != null) { v.Rebels.Add(riot.Leader); v.Rebels0 = 1; }
        v.Defenders = DefendersOf(c, fief, census);
        var causes = new List<Cause>();
        if (riot != null) causes.Add(new Cause(CauseKind.Unrest, new EntityId(riot.EndEntry != 0 ? riot.EndEntry : riot.RiseEntry), "", "ch_note_ur_revolt_riot"));
        causes.AddRange(CausesOf(c, a));
        var place = c.PlaceId(seat);
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Person("claimant", new EntityId(claimant)), ChronParam.Count("rebels", v.Rebels.Count), ChronParam.Count("defenders", v.Defenders) };
        var subj = new List<EntityId> { new EntityId(claimant), place };
        v.RiseEntry = c.Chron.Chron(c.Now, "revolt_rises", "ch_ur_revolt_rises", ps, subj, causes).Value;
        c.B.Revolts.Add(v);
        return v;
    }

    /// <summary>A rising of the bonded (D1, slavery 4.5): the bonded are the rebels, there is no claimant, and the survivors go free as a band. D1 has already struck their chains and written its entry, which is the cause.</summary>
    public static Revolt BeginSlaveRising(UnrestCtx c, int place, ulong leader, IReadOnlyList<ulong> bonded, ulong entry)
    {
        int fief = c.FiefOfSettlement(place);
        var v = new Revolt { Id = c.B.NextRevolt++, Fief = fief, Settlement = place, StartDay = c.Day, ResolveDay = c.Day + 1, Claimant = 0, ClaimantKind = 0, Leader = leader, Bonded = true,
            OldLord = fief >= 0 && c.RB.FiefOf(fief) is { Holder: >= 0 } fr ? (ulong)fr.Holder : 0, RiseEntry = entry };
        v.Rebels = [.. bonded.Order()]; v.Rebels0 = v.Rebels.Count;
        var census = new Census();
        v.Defenders = GuardsOf(c, place).Count * c.S.GuardWeight;
        c.B.Revolts.Add(v);
        return v;
    }

    private static int DefendersOf(UnrestCtx c, int fief, Census census)
    {
        int guards = 0, loyal = 0; var pt = c.W.People;
        foreach (var s in census.Households.Keys)
        {
            if (c.FiefOfSettlement(s) != fief) continue;
            guards += GuardsOf(c, s).Count;
            if (census.Adults.TryGetValue(s, out var adults)) foreach (int r in adults) if (pt.Sex[r] == Sex.Male && pt.Occupation[r] != Occupation.Guard) loyal++;
        }
        // the lord calls one man in LevyOneIn of those who are not rebels
        return guards * c.S.GuardWeight + Math.Max(0, loyal - 0) / Math.Max(1, c.S.LevyOneIn) ;
    }

    /// <summary>The revolt's day (4.3): an abstract battle of strengths (a man a point, a guard <see cref="UnrestSettingsDef.GuardWeight"/>) with a variance drawn on stream 0x682 (key revolt and day), the winner losing a tenth and the loser three tenths.</summary>
    public static void Fight(UnrestCtx c, Revolt v)
    {
        var pt = c.W.People;
        var rebels = v.Rebels.Where(id => { int r = pt.IndexOf(new EntityId(id)); return r >= 0 && pt.IsAlive(r); }).ToList();
        var guards = GuardsOf(c, v.Settlement);
        int defenders = v.Bonded ? guards.Count * c.S.GuardWeight : Math.Max(v.Defenders, guards.Count * c.S.GuardWeight);
        long atk = rebels.Count; int var = c.S.BattleVarPct;
        if (v.ClaimantKind == 1) { int cf = c.RB.Fiefs.Values.FirstOrDefault(x => (ulong)x.Holder == v.Claimant)?.Fief ?? -1; if (cf >= 0 && (uint)cf < (uint)c.R.Region.Fiefs.Count && c.R.Region.Fiefs[cf].SeatSettlement >= 0) atk += GuardsOf(c, c.R.Region.Fiefs[cf].SeatSettlement).Count * c.S.GuardWeight; }
        int da = DetRng.Range(c.Seed, c.Day, (ulong)v.Id, RngStreams.RevoltAndEvasion, 10, 0, 2 * var + 1), dd = DetRng.Range(c.Seed, c.Day, (ulong)v.Id, RngStreams.RevoltAndEvasion, 11, 0, 2 * var + 1);
        bool rebelsWin = UnrestRules.Strength(atk, da, var) > UnrestRules.Strength(defenders, dd, var);
        // casualties: the rebels' dead are drawn from the rebels, the defenders' from the guards
        int rebelLoss = rebels.Count == 0 ? 0 : Math.Max(1, rebels.Count * (rebelsWin ? c.S.WinnerLossPct : c.S.LoserLossPct) / 100);
        int guardLoss = guards.Count == 0 ? 0 : Math.Max(1, guards.Count * (rebelsWin ? c.S.LoserLossPct : c.S.WinnerLossPct) / 100);
        var cause = new Cause(CauseKind.Unrest, new EntityId(v.RiseEntry), "", "ch_note_ur_revolt_fought");
        foreach (ulong id in rebels.OrderBy(x => DetRng.Next(c.Seed, c.Day, x, RngStreams.RevoltAndEvasion, 200u + (uint)v.Id)).ThenBy(x => x).Take(rebelLoss))
        { int r = pt.IndexOf(new EntityId(id)); if (r >= 0) { Demography.Kill(c.W, r, DeathCause.Violence, c.Now, c.Ob!, "", cause); v.Dead++; } }
        foreach (int r in guards.OrderBy(x => DetRng.Next(c.Seed, c.Day, pt.IdOf(x).Value, RngStreams.RevoltAndEvasion, 300u + (uint)v.Id)).ThenBy(x => x).Take(guardLoss))
        { Demography.Kill(c.W, r, DeathCause.Violence, c.Now, c.Ob!, "", cause); v.Dead++; }
        v.State = rebelsWin ? RevoltState.Won : RevoltState.Lost;
        var place = c.PlaceId(v.Settlement);
        var su = c.B.Peek(v.Settlement) ?? new SettlementUnrest(); su.TopDays = 0; su.HighDays = 0;
        if (rebelsWin) Won(c, v, place); else Lost(c, v, place, su);
        c.B.Settlements[v.Settlement] = su;
    }

    private static void Won(UnrestCtx c, Revolt v, EntityId place)
    {
        var causes = new List<Cause> { new(CauseKind.Unrest, new EntityId(v.RiseEntry), "", "ch_note_ur_revolt_won") };
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Count("dead", v.Dead) };
        if (v.Bonded)
        {
            ps.Add(ChronParam.Person("leader", new EntityId(v.Leader)));
            v.EndEntry = c.Chron.Chron(c.Now, "slave_rising_won", "ch_ur_slave_won", ps, [new EntityId(v.Leader), place], causes).Value;
            return;
        }
        var f = c.RB.FiefOf(v.Fief); string how = "ur_liege_accepts"; ulong ruler = 0;
        if (f != null)
        {
            f.Holder = (long)v.Claimant; f.TookDay = c.Day; f.Forfeited = false;
            if (f.Realm >= 0 && c.RB.Rulers.TryGetValue(f.Realm, out long rl)) ruler = (ulong)rl;
            if (ruler != 0)
            {
                if (Opinions.Value(c.RB, ruler, v.Claimant) > c.S.LiegeAccepts) { how = "ur_liege_accepts"; }
                else { how = "ur_liege_refuses"; if (c.PB != null) Opinions.Add(c.PB, c.RB, ruler, v.Claimant, "insult", c.Day, v.RiseEntry); else c.RB.AddOpinion(ruler, v.Claimant, -30); }
            }
            else how = "ur_liege_none";
            if (v.OldLord != 0 && c.PB != null) Opinions.Add(c.PB, c.RB, v.Claimant, v.OldLord, "insult", c.Day, v.RiseEntry);
        }
        ps.Add(ChronParam.Person("claimant", new EntityId(v.Claimant))); ps.Add(ChronParam.Person("lord", new EntityId(v.OldLord))); ps.Add(ChronParam.TextKey("how", how));
        v.EndEntry = c.Chron.Chron(c.Now, "revolt_won", "ch_ur_revolt_won", ps, [new EntityId(v.Claimant), new EntityId(v.OldLord), place], causes).Value;
    }

    private static void Lost(UnrestCtx c, Revolt v, EntityId place, SettlementUnrest su)
    {
        var causes = new List<Cause> { new(CauseKind.Unrest, new EntityId(v.RiseEntry), "", "ch_note_ur_revolt_lost") };
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.Count("dead", v.Dead) };
        su.FearUntil = c.Day + c.S.FearSeasons * Ravensguard.Sim.Core.TimeConst.DaysPerSeason;
        string type = v.Bonded ? "slave_rising_lost" : "revolt_lost", key = v.Bonded ? "ch_ur_slave_lost" : "ch_ur_revolt_lost";
        ps.Add(ChronParam.Person("leader", new EntityId(v.Leader)));
        v.EndEntry = c.Chron.Chron(c.Now, type, key, ps, [new EntityId(v.Leader), place], causes).Value;
        // the leaders are tried for treason: a crime for the law, whose court sentences by the culture's key
        var w = Witnesses(c, v.Settlement);
        if (w.Count > 0 && c.Links.Crime != null)
        {
            var doers = new SortedSet<ulong> { v.Leader }; if (v.ClaimantKind == 2) doers.Add(v.Claimant);
            foreach (ulong d in doers) if (c.R.Alive((long)d)) c.Links.Crime(new RiotCrime("treason", new EntityId(d), EntityId.None, v.Settlement, c.Hour * 3600, 0, w, "ch_note_crime_revolt", v.EndEntry, true));
        }
    }
}
