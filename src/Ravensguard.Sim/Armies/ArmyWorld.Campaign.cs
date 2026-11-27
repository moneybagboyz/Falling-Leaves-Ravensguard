using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>
/// P7-AM-E: campaigns and the war score (armies-on-the-map 4.10, 4.11). The campaign brain scores the enemy's places and the hosts it knows of by value over march and siege days, from last-known intel (scouts and rumours, never the
/// truth); a host that has formed marches by road on its objective; hosts of the two sides that meet within 2 km avoid or fight (the player's bubble through <see cref="LiveBattle"/>, else the abstract resolver); what armies do is a
/// <see cref="WarEvent"/> the season's war step turns into the war's score (<see cref="ScoreFor"/>). Sieges go through <see cref="Siege"/>, the seam the siege book fills.
/// </summary>
public sealed partial class ArmyWorld
{
    /// <summary>The siege book's hook (SG-D replaces the stub).</summary>
    public ISiegeHook Siege { get; set; } = new StubSiege();
    /// <summary>The player's bubble: given a field battle one of whose hosts the hero leads, plays it and returns what it left; null leaves it to the abstract resolver. Not saved (the game layer sets it).</summary>
    public Func<ArmyEnv, FieldEngagement, FieldVerdict?>? LiveBattle { get; set; }
    public TimingCell CampaignCost { get; } = new();

    // ------------------------------------------------------------------ where a host is, and what its side knows

    /// <summary>Where a host stands, continent millimetres: the head of its column, or the town it gathered at.</summary>
    public (long X, long Y) PositionOfHost(ArmyHost h, long sec)
    {
        if (h.Supply != 0 && Book.Marches.TryGetValue(h.Supply, out var m) && HeadAt(m, sec) is { } p) return (p.XMm, p.YMm);
        return CenterMm(Math.Max(0, h.Point));
    }

    private static long DistMm(long ax, long ay, long bx, long by) { long dx = ax - bx, dy = ay - by; return CaravanPath.Isqrt(dx * dx + dy * dy); }

    /// <summary>The width of the disc a last-known position stands for (4.10): 4 km for each day of its age, 500 m when fresh. A host seen 3 days ago is somewhere in a disc 12 km wide.</summary>
    public static int DiscWidthM(int ageDays) => Math.Max(CampaignConfig.DiscMinM, ageDays * CampaignConfig.DiscMPerDay);

    public IntelRow? IntelOf(int observer, int subjectHost) => Book.Intel.TryGetValue(ArmyBook.IntelKey(observer, subjectHost), out var r) ? r : null;

    /// <summary>The strength an observer counts on for a host he has heard of: what he was told, trusted less by a tenth for each day, never under two fifths.</summary>
    public int KnownStrength(int observer, int subjectHost, int day)
    {
        var r = IntelOf(observer, subjectHost); if (r == null) return 0;
        int w = Math.Max(CampaignConfig.AgeFloorPm, 1000 - CampaignConfig.AgeLossPm * Math.Max(0, day - r.Day));
        return (int)((long)r.Strength * w / 1000);
    }

    private void Learn(int observer, ArmyHost subject, int day, long x, long y, int strength, byte source)
    {
        long k = ArmyBook.IntelKey(observer, subject.Id);
        int told = (strength + 5) / 10 * 10;                                                   // a scout counts to the nearest ten
        if (Book.Intel.TryGetValue(k, out var r))
        {
            if (r.Day > day || (r.Day == day && r.Source < source)) return;                    // a fresher or a surer word stands
            r.Day = day; r.XMm = x; r.YMm = y; r.Strength = told; r.Source = source;
        }
        else Book.Intel[k] = new IntelRow { Observer = observer, Subject = subject.Id, Day = day, XMm = x, YMm = y, Strength = told, Source = source };
        Book.Touch();
    }

    /// <summary>
    /// The scouts' hour (4.10): by day a host sees a host of the other side in its war within 12 km (scouts 10 km ahead, 2 km of sight) and the sighting is written for its realm; villages of the other realm within 3 km of a host send word of
    /// it (a rumour, every sixth hour). Nothing is read from a host not seen.
    /// </summary>
    private void ScoutHour(ArmyEnv e, List<ArmyHost> live, Dictionary<int, (long X, long Y)> pos)
    {
        var pb = Politics; var hs = Supply; if (pb == null || hs == null) return;
        int day = (int)e.Day, hod = e.HourOfDay; bool daylight = hod >= MarchConfig.DayStartHour && hod < MarchConfig.DayEndHour;
        for (int i = 0; i < live.Count; i++)
            for (int j = 0; j < live.Count; j++)
            {
                if (i == j || live[i].Realm == live[j].Realm || pb.WarOf(live[i].Realm, live[j].Realm) == null) continue;
                var (ax, ay) = pos[live[i].Id]; var (bx, by) = pos[live[j].Id];
                if (daylight && DistMm(ax, ay, bx, by) <= CampaignConfig.ScoutM * 1000L) Learn(live[i].Realm, live[j], day, bx, by, FitMen(live[j]), 0);
            }
        if (hod % 6 != 0) return;
        foreach (var h in live)
        {
            var (x, y) = pos[h.Id];
            for (int s = 0; s < Region.Settlements.Count; s++)
            {
                var (sx, sy) = CenterMm(s);
                if (DistMm(x, y, sx, sy) > CampaignConfig.RumourM * 1000L) continue;
                int r = hs.RealmOf(s);
                if (r >= 0 && r != h.Realm && pb.WarOf(r, h.Realm) != null) Learn(r, h, day, x, y, FitMen(h), 1);
            }
        }
    }

    // ------------------------------------------------------------------ the brain

    private static int PWinPm(long mine, long theirs)
    {
        if (mine <= 0) return 0; if (theirs <= 0) return 1000;
        long r = mine * 1000 / theirs;
        return (int)(r * r * 1000 / (1_000_000 + r * r));
    }

    /// <summary>
    /// A candidate scored by 4.10: <c>value</c> (the fief's receipts, or its people at 30 cp a head when the books show none, times 4 for a seat taken and 1 for a sack; +30% for a claim; a lord's ransom at one in five), <c>days</c> (the
    /// march by road at 3.7 km a day plus the siege), <c>p_win</c> (the square law on what is known: the holding's garrison and the hosts heard of), <c>cost</c> (the days at the host's daily cost, plus the harvest shortfall if the days
    /// meet the reap window), <c>score = (value x p_win / 1000 - cost) / days</c>. All integers.
    /// </summary>
    public CampaignScore ScoreTarget(ArmyEnv e, int realm, int from, int target, int men, long dailyCostMites, long harvestMites, int enemyKnown, bool claim)
    {
        var st = Region.Settlements[target]; int fief = st.FiefIndex;
        var fr = Rule?.Fiefs.TryGetValue(fief, out var f0) == true ? f0 : null;
        long receipts = Math.Max(fr?.LastYearReceipts ?? 0, (long)Region.Fiefs[fief].Pop * 30 * Coin.MitesPerCopper);
        bool seat = Region.Fiefs[fief].SeatSettlement == target;
        long value = receipts * (seat ? CampaignConfig.TakeYears : CampaignConfig.SackYears);
        if (claim && seat) value += value * CampaignConfig.ClaimPct / 100;
        if (seat) value += (long)CampaignConfig.LordCp * Coin.MitesPerCopper * CampaignConfig.LordChancePct / 100;
        int march = Math.Max(1, (DistM(from, target) + CampaignConfig.MarchMPerDay - 1) / CampaignConfig.MarchMPerDay);
        int garrison = TargetSize(fief); int siege = Siege.SiegeDays(this, target, garrison);
        int pwin = PWinPm(men, garrison + enemyKnown);
        int days = Math.Max(1, march + siege); int doy = e.DayOfYear;
        long cost = days * dailyCostMites + (doy <= Rules.DesertHarvestToDoy && doy + days >= Rules.DesertHarvestFromDoy ? harvestMites : 0);
        long score = (value * pwin / 1000 - cost) / days;
        return new CampaignScore(target, seat ? (byte)0 : (byte)1, value, march, siege, pwin, cost, score, 0);
    }

    /// <summary>The host of the other side a realm has heard of, as a candidate: its disc's nearest place, worth what a beaten host is (cp a man known), no siege.</summary>
    public CampaignScore? ScoreHost(ArmyEnv e, int realm, int from, ArmyHost enemy, int men, long dailyCostMites)
    {
        var r = IntelOf(realm, enemy.Id); int day = (int)e.Day; if (r == null || day - r.Day > CampaignConfig.IntelKeepDays) return null;
        var (s, _) = NearestTo(r.XMm, r.YMm); if (s < 0 || (s != from && PathOf(from, s) == null)) return null;
        int known = KnownStrength(realm, enemy.Id, day);
        long value = (long)known * CampaignConfig.HostManCp * Coin.MitesPerCopper;
        int march = Math.Max(1, (DistM(from, s) + CampaignConfig.MarchMPerDay - 1) / CampaignConfig.MarchMPerDay); int pwin = PWinPm(men, known);
        long cost = march * dailyCostMites; long score = (value * pwin / 1000 - cost) / march;
        return new CampaignScore(s, 2, value, march, 0, pwin, cost, score, enemy.Id);
    }

    /// <summary>The best candidate within reach, by score (ties the lower place), or null when there is none: the enemy realm's places and the hosts of it the realm has heard of.</summary>
    public CampaignScore? BestCampaign(ArmyEnv e, int realm, int enemy, int from, int men, long dailyCostMites, long harvestMites, bool claim)
    {
        var hs = Supply; if (hs == null) return null;
        int day = (int)e.Day; long known = 0;
        foreach (var (k, r) in Book.Intel) if (r.Observer == realm && Book.Hosts.Values.FirstOrDefault(h => h.Id == r.Subject) is { } sh && sh.Realm == enemy && day - r.Day <= CampaignConfig.IntelKeepDays) known += KnownStrength(realm, r.Subject, day);
        CampaignScore? best = null;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            if (hs.RealmOf(s) != enemy || s == from) continue;
            if (PathOf(from, s) == null || DistM(from, s) > CampaignConfig.ReachDays * CampaignConfig.MarchMPerDay) continue;
            var c = ScoreTarget(e, realm, from, s, men, dailyCostMites, harvestMites, (int)known, claim);
            if (best == null || c.Score > best.Score) best = c;
        }
        foreach (var h in Book.Hosts.Values)
        {
            if (h.Realm != enemy || h.State == HostState.Disbanding) continue;
            var c = ScoreHost(e, realm, from, h, men, dailyCostMites);
            if (c != null && (best == null || c.Score > best.Score)) best = c;
        }
        return best;
    }

    private (long Daily, long Harvest) CostsOf(int realm, int men)
    {
        var hs = Supply!;
        var b = hs.Budget(realm, new Roster([(TroopRole.Levy, Math.Max(1, men))]), Rules.FundDays);
        long harvest = 0; foreach (var f in FiefsOfRealm(realm)) { int q = QuotaOf(f.Fief, true); if (q > 0) harvest += hs.HarvestRiskPriced(f.Fief, q, Rules.ReapWindowDays).Mites; }
        return (b.DailyCostMites, harvest);
    }

    /// <summary>A realm at war weighs a campaign before it calls its levy: the best candidate from the muster point, or null when none scores above nothing.</summary>
    public CampaignScore? PlanCampaign(ArmyEnv e, int realm, int enemy, int point, int men, bool anyTarget = false)
    {
        var (daily, harvest) = CostsOf(realm, men);
        var war = Politics?.WarOf(realm, enemy);
        var best = BestCampaign(e, realm, enemy, point, men, daily, harvest, war?.Cb == CbKind.Claim && war.Attacker == realm);
        return best is { Score: > 0 } || (anyTarget && best != null) ? best : null;           // P8-WT-1: a realm at war with its host funded goes to the best target there is, though it pays no more than it costs
    }

    /// <summary>The call of an AI realm's levy on the first day of summer (replacing AM-B's call with no campaign behind it): only for a campaign that scores above nothing, and the host is worth its expected value to its ruler, so he holds it for the harvest only if it is.</summary>
    public ArmyHost? CallCampaign(ArmyEnv e, int realm, int enemy, int point, int men, int maxLevies = int.MaxValue, bool anyTarget = false)
    {
        var plan = PlanCampaign(e, realm, enemy, point, men, anyTarget); if (plan == null) return null;
        var host = CallMuster(e, new MusterCall(realm, point, Worth: Math.Max(0, plan.Value * plan.PWinPm / 1000), MaxLevies: maxLevies)); if (host == null) return null;
        SetPlan(e, host, plan);
        return host;
    }

    private void SetPlan(ArmyEnv e, ArmyHost host, CampaignScore plan)
    {
        var c = Book.Campaigns.TryGetValue(host.Id, out var old) ? old : Book.Campaigns[host.Id] = new HostCampaign { Host = host.Id };
        bool fresh = old == null || old.Objective != plan.Target;
        c.Objective = plan.Target; c.Kind = plan.Kind; c.EnemyHost = plan.EnemyHost; c.PlanDay = (int)e.Day; c.Value = plan.Value; c.Cost = plan.Cost; c.Score = plan.Score; c.PWinPm = plan.PWinPm; c.Days = plan.Days;
        Book.Touch();
        if (!fresh || host.Entry == 0) return;
        Chron(e, "campaign_planned", "ch_am_campaign", [ChronParam.Person("ruler", new EntityId(host.Commander)), ChronParam.Place("place", PlaceId(plan.Target)), ChronParam.Count("men", Math.Max(host.Expected, 1)), ChronParam.Mites("value", plan.Value), ChronParam.Count("days", plan.Days)],
            [new EntityId(host.Commander), PlaceId(plan.Target)], new Cause(CauseKind.Campaign, new EntityId(host.Entry), "", plan.Kind == 2 ? "ch_note_am_plan_meet" : plan.Kind == 1 ? "ch_note_am_plan_sack" : "ch_note_am_plan_take"));
    }

    // ------------------------------------------------------------------ the hour: scouts, arrivals, contact, battles due

    /// <summary>One hour of the campaigns (H1, after the march): intel, hosts arriving at their objectives, hosts meeting, the battles whose hour has come.</summary>
    public void CampaignHour(ArmyEnv e)
    {
        var hs = Supply; if (hs == null || Book.Hosts.Count == 0) return;
        long sec = e.Hour * 3600;
        var live = Book.Hosts.Values.Where(h => h.Supply != 0 && h.State is not (HostState.Summoned or HostState.Mustering or HostState.Disbanding) && hs.Exists(new HostId(h.Supply))).OrderBy(h => h.Id).ToList();
        if (live.Count == 0) return;
        var pos = new Dictionary<int, (long X, long Y)>(); foreach (var h in live) pos[h.Id] = PositionOfHost(h, sec);
        if (Politics is { } pb0 && pb0.Wars.Any(w => w.Alive)) ScoutHour(e, live, pos);
        foreach (var h in live)
            if (Book.Campaigns.TryGetValue(h.Id, out var c) && c.Stage == 1 && h.Supply != 0 && Book.Marches.TryGetValue(h.Supply, out var m) && m.State == MarchState.Arrived) Arrive(e, h, c);
        for (int i = 0; i < live.Count; i++)
            for (int j = i + 1; j < live.Count; j++)
            {
                var a = live[i]; var b = live[j];
                if (a.Realm == b.Realm || a.State is HostState.Engaged or HostState.Retreating || b.State is HostState.Engaged or HostState.Retreating) continue;
                if (Politics?.WarOf(a.Realm, b.Realm) == null) continue;
                if (DistMm(pos[a.Id].X, pos[a.Id].Y, pos[b.Id].X, pos[b.Id].Y) <= CampaignConfig.ContactM * 1000L) Meet(e, a, b, sec);
            }
        foreach (var c in Book.Campaigns.Values.Where(c => c.BattleHour > 0 && e.Hour >= c.BattleHour).OrderBy(c => c.Host).ToList()) FightDue(e, c);
    }

    private long NextBattleHour(long hour) { long h = hour / 24 * 24 + CampaignConfig.BattleHour; return h <= hour ? h + 24 : h; }

    private HostCampaign CampaignOf(ArmyHost h) => Book.Campaigns.TryGetValue(h.Id, out var c) ? c : Book.Campaigns[h.Id] = new HostCampaign { Host = h.Id, Kind = 2, Stage = 3 };

    /// <summary>
    /// Two hosts of belligerents meet (4.10): a side whose known ratio is under 0.7 avoids (it falls back on its muster point), else it offers battle where it stands; if neither avoids both halt and fight at 08:00 next. The
    /// hero's host never avoids of itself: it stands until its commander orders.
    /// </summary>
    private void Meet(ArmyEnv e, ArmyHost a, ArmyHost b, long sec)
    {
        int day = (int)e.Day; bool avoidA = Avoids(a, b, day), avoidB = Avoids(b, a, day);
        if (avoidA) Avoid(e, a, b, sec); if (avoidB) Avoid(e, b, a, sec);
        if (avoidA || avoidB) return;
        long at = NextBattleHour(e.Hour);
        foreach (var (h, o) in new[] { (a, b), (b, a) })
        {
            var c = CampaignOf(h); c.BattleHour = at; c.EnemyHost = o.Id;
            if (h.Supply != 0) Halt(new HostId(h.Supply), true);
            h.State = HostState.Engaged;
        }
        Book.Touch();
    }

    private bool Avoids(ArmyHost me, ArmyHost they, int day)
    {
        if (IsHero(me.Commander)) return false;
        int mine = FitMen(me), known = KnownStrength(me.Realm, they.Id, day); if (known <= 0) known = FitMen(they);
        return known > 0 && (long)mine * 1000 / known < CampaignConfig.AvoidPermille;
    }

    private void Avoid(ArmyEnv e, ArmyHost me, ArmyHost they, long sec)
    {
        var hid = new HostId(me.Supply); var (here, _) = NearestTo(PositionOfHost(me, sec).X, PositionOfHost(me, sec).Y);
        me.State = HostState.Retreating; var c = CampaignOf(me); c.Stage = 3; c.BattleHour = 0;
        if (here >= 0 && here != me.Point) BeginMarch(e, hid, [here, me.Point], "ch_note_am_retreat");
        Chron(e, "host_avoided", "ch_am_avoided", [ChronParam.Person("ruler", new EntityId(me.Commander)), ChronParam.Place("place", PlaceId(here)), ChronParam.Count("ratio", (int)((long)FitMen(me) * 1000 / Math.Max(1, KnownStrength(me.Realm, they.Id, (int)e.Day))))],
            [new EntityId(me.Commander), PlaceId(here)], new Cause(CauseKind.Campaign, me.Entry != 0 ? new EntityId(me.Entry) : null, "", "ch_note_am_ratio_low"));
    }

    private void FightDue(ArmyEnv e, HostCampaign c)
    {
        if (!Book.Hosts.TryGetValue(c.Host, out var a)) { c.BattleHour = 0; return; }
        if (!Book.Hosts.TryGetValue(c.EnemyHost, out var b) || a.State != HostState.Engaged || b.State != HostState.Engaged) { c.BattleHour = 0; if (a.State == HostState.Engaged) a.State = HostState.Camped; return; }
        if (a.Id > b.Id) return;                                                                  // the lower id of the pair fights it for both
        FieldBattle(e, a, b);
    }

    // ------------------------------------------------------------------ the battle

    private List<HostMan> Fit(ArmyHost h)
    {
        var hs = Supply!; var hid = new HostId(h.Supply); var st = Book.Rations.TryGetValue(hid.Value, out var r) ? r : null;
        return MenOf(hs, hid, h).Where(m => st == null || !st.Wounded.ContainsKey(m.Id)).OrderBy(m => m.Id).ToList();
    }

    /// <summary>
    /// A field battle between two hosts at their meeting place (4.9, 4.10): the player's bubble when the hero leads one of them and the game has set <see cref="LiveBattle"/>, else the abstract resolver on the men of both rolls
    /// (at most <see cref="CampaignConfig.AbstractCap"/> a side, in the ratio of the two hosts; the rest are the reserve that was not engaged). The dead are killed with their chains, both hosts fold back by 4.9 (the loser's
    /// morale -25, the winner's +15; a host that lost over half retreats, under a quarter left is disbanded), and the war gets its events.
    /// </summary>
    public (int Winner, FoldResult WinnerFold, FoldResult LoserFold)? FieldBattle(ArmyEnv e, ArmyHost a, ArmyHost b)
    {
        var hs = Supply; if (hs == null) return null;
        long sec = e.Hour * 3600; var (px, py) = PositionOfHost(a, sec); int place = NearestTo(px, py).Settlement; int day = (int)e.Day;
        FieldVerdict? live = null;
        if ((IsHero(a.Commander) || IsHero(b.Commander)) && LiveBattle != null) live = LiveBattle(e, new FieldEngagement(a, b, place));
        int winner; IReadOnlyList<BattleCommand> cmds; ulong report;
        if (live != null) { winner = live.WinnerHost; cmds = live.Commands; report = live.Entry; }
        else (winner, cmds, report) = Abstract(e, a, b, place);
        var won = winner == a.Id ? a : b; var lost = won == a ? b : a;
        var fw = FoldBack(e, won, cmds, true); var fl = FoldBack(e, lost, cmds, false);
        int lostPct = fl.Before > 0 ? (fl.Before - fl.Left) * 100 / fl.Before : 0; var war = Politics?.WarOf(a.Realm, b.Realm);
        if (war != null)
        {
            AddEvent(war.Serial, day, WarEventKind.FieldBattle, won.Realm, Math.Min(CampaignConfig.BattleMax, lostPct / 2), fl.Before - fl.Left, fw.Before - fw.Left, place, report);
            if (fl.Disbanded) AddEvent(war.Serial, day, WarEventKind.HostDestroyed, won.Realm, CampaignConfig.DestroyedPoints, fl.Before - fl.Left, 0, place, report);
        }
        foreach (var h in new[] { a, b })
        {
            var c = CampaignOf(h); c.BattleHour = 0; c.EnemyHost = 0;
            if (h == lost) { c.Stage = 3; continue; }
            if (h.State == HostState.Engaged) h.State = HostState.Camped;
            if (h.Supply != 0) Halt(new HostId(h.Supply), false);
            c.Stage = 0;
        }
        Book.Touch();
        return (won.Id, fw, fl);
    }

    private (int Winner, IReadOnlyList<BattleCommand> Commands, ulong Report) Abstract(ArmyEnv e, ArmyHost a, ArmyHost b, int place)
    {
        var w = W; var pt = People; int day = (int)e.Day;
        var ob = e.Ob ?? new RegionOutbox(w.Ids);
        var ab = w.Get<AbstractBattleService>();
        var ma = Fit(a); var mb = Fit(b);
        if (ab is not { Ready: true } || ma.Count == 0 || mb.Count == 0)
        {
            // a bare world with no wound tables (or an empty side): the larger roll wins and a fifth of the loser falls, the lowest ids first
            bool aw = ma.Count >= mb.Count; var losers = aw ? mb : ma; var began = new Cause(CauseKind.Wound, null, "", "ch_note_am_field");
            foreach (var m in losers.Take(Math.Max(1, losers.Count / 5))) Demography.Kill(w, m.Row, DeathCause.Violence, e.Time, ob, chain: began);
            return (aw ? a.Id : b.Id, [], 0);
        }
        var fc = ab.Fight; ulong seed = w.Seed ^ DetRng.Mix((ulong)a.Id * 7919UL + (ulong)b.Id * 104729UL + (ulong)day);
        int mx = Math.Max(ma.Count, mb.Count); int ka = Math.Max(1, Math.Min(ma.Count, CampaignConfig.AbstractCap * ma.Count / mx)), kb = Math.Max(1, Math.Min(mb.Count, CampaignConfig.AbstractCap * mb.Count / mx));
        var soldier = FightSetup.KitDef(fc, Rules.FightKit, Rules.FightKit); var guard = FightSetup.KitDef(fc, Rules.GuardKit, Rules.GuardKit);
        List<FighterKit> Kits(List<HostMan> men, int k, int team)
        {
            var l = new List<FighterKit>();
            foreach (var m in men.OrderBy(x => DetRng.Mix(x.Id ^ seed)).Take(k).OrderBy(x => x.Id))
                l.Add(FightSetup.Roll(fc, m.Kind == 0 ? soldier : guard, seed, m.Id, pt.FullName(m.Row), team, 0, 0, FightMath.One, 0) with { Stance = "balanced" });
            return l;
        }
        ulong battleId = (ulong)e.Hour * 4096 + ((ulong)(a.Id * 64 + b.Id) & 4095);
        var setup = FightSetup.Raid(Kits(ma, ka, 0), Kits(mb, kb, 1), seed, battleId, e.Hour * 3600, "host_field", raiderKind: SideKind.Army);
        var ledger = w.Get<WoundLedger>();
        WoundBook? BookOf(ulong person) { if (ledger == null) return null; lock (ledger.Sync) return ledger.Find(person); }
        var resolver = new AbstractBattles { BookOf = BookOf, PlaceKey = BattleChronicle.RoadFrom, PlaceId = PlaceId(place) };
        var rep = resolver.Resolve(setup, fc); var br = rep.Battle!;
        var beganParams = new List<ChronParam> { ChronParam.Place("place", PlaceId(place)), ChronParam.Person("a", new EntityId(a.Commander)), ChronParam.Person("b", new EntityId(b.Commander)) };
        foreach (var cmd in rep.Commands)
        {
            int p = pt.IndexOf(new EntityId(cmd.Person)); if (p < 0) continue;
            if (cmd.Kind == BattleCommandKind.Wounds && cmd.Book != null && ledger != null && !cmd.Book.Dead) ledger.Adopt(cmd.Person, cmd.Book, e.Hour * 3600);
            if (cmd.Kind != BattleCommandKind.Status || cmd.Status != SoldierStatus.Dead || pt.Status[p] != PersonStatus.Alive) continue;
            var n = br.Notables.FirstOrDefault(x => x.Person == cmd.Person);
            var book = rep.Commands.FirstOrDefault(x => x.Person == cmd.Person && x.Kind == BattleCommandKind.Wounds)?.Book;
            var dc = book is { Dead: true } ? WoundSystem.CauseOf(book.Death) : DeathCause.Violence;
            Demography.Kill(w, p, dc, e.Time, ob, chain: BattleChronicle.DeathChain(n, br, "ch_began_am_field", beganParams));
        }
        var cause = new Cause(CauseKind.War, null, "", "ch_note_am_field", beganParams);
        var entry = BattleChronicle.Report(ob, e.Time, br, new EntityId(a.Commander), PlaceId(place), "ch_side_am_host", "ch_side_am_host", cause);
        int winTeam = br.Winner;
        if (winTeam < 0) winTeam = ma.Count * kb >= mb.Count * ka ? 0 : 1;                      // a drawn field goes to the host that kept the greater share
        return (winTeam == 0 ? a.Id : b.Id, rep.Commands, entry.Value);
    }

    private void AddEvent(int war, int day, WarEventKind kind, int doer, int points, int lost, int ownLost, int place, ulong entry)
    {
        Book.WarEvents.Add(new WarEvent { War = war, Day = day, Kind = kind, Doer = doer, Points = points, Lost = lost, OwnLost = ownLost, Place = place, Entry = entry });
        Book.Touch();
    }

    // ------------------------------------------------------------------ marching out, arriving, sieges (the daily step)

    /// <summary>
    /// A day of the campaigns (ArmiesDaily, after the muster's day): a formed host that is not the hero's marches out on its objective by road; a host on the road watches its plan (a host it meant to meet has moved: the plan follows the
    /// newest word within the day); a besieging host asks the siege hook; the old intel is let go.
    /// </summary>
    public void CampaignDay(ArmyEnv e)
    {
        var hs = Supply; if (hs == null || Book.Hosts.Count == 0) return;
        int day = (int)e.Day;
        foreach (var k in Book.Intel.Where(kv => day - kv.Value.Day > CampaignConfig.IntelKeepDays || !Book.Hosts.ContainsKey(kv.Value.Subject)).Select(kv => kv.Key).ToList()) { Book.Intel.Remove(k); Book.Touch(); }
        foreach (var k in Book.Campaigns.Keys.Where(k => !Book.Hosts.ContainsKey(k)).ToList()) { Book.Campaigns.Remove(k); Book.Touch(); }
        foreach (var h in Book.Hosts.Values.OrderBy(h => h.Id).ToList())
        {
            if (h.Supply == 0 || h.State == HostState.Disbanding || !Book.Campaigns.TryGetValue(h.Id, out var c) || !hs.Exists(new HostId(h.Supply))) continue;
            if (c.Stage == 2 && c.Kind != 2 && h.State is HostState.Besieging or HostState.Camped) { SiegeDay(e, h, c); continue; }
            if (IsHero(h.Commander)) continue;                                                    // the player orders his own host
            if (c.BattleHour > 0 || h.State == HostState.Engaged) continue;
            if (h.State == HostState.Camped && c.Stage == 0)
            {
                if (!ObjectiveLive(h, c) && !Replan(e, h, c)) { c.Stage = 3; Release(e, hs, h, "ch_note_am_plan_dropped"); continue; }
                StartMarch(e, h, c);
            }
            else if (c.Stage is 1 or 2 && c.Kind == 2) FollowPlan(e, h, c);
            else if (c.Stage == 1 && !ObjectiveLive(h, c) && Replan(e, h, c)) StartMarch(e, h, c);
        }
    }

    private bool ObjectiveLive(ArmyHost h, HostCampaign c)
    {
        if (c.Objective < 0 || (uint)c.Objective >= (uint)Region.Settlements.Count) return false;
        if (c.Kind == 2) return Book.Hosts.ContainsKey(c.EnemyHost);
        var hs = Supply!; int r = hs.RealmOf(c.Objective); return r >= 0 && r != h.Realm && Politics?.WarOf(h.Realm, r) != null;
    }

    private bool Replan(ArmyEnv e, ArmyHost h, HostCampaign c)
    {
        var war = Politics?.WarOf(h.Realm); if (war == null) return false;
        int enemy = war.Attacker == h.Realm ? war.Defender : war.Attacker; int men = Math.Max(1, FitMen(h));
        var (daily, harvest) = CostsOf(h.Realm, men);
        var (here, _) = NearestTo(PositionOfHost(h, e.Hour * 3600).X, PositionOfHost(h, e.Hour * 3600).Y);
        var best = BestCampaign(e, h.Realm, enemy, here, men, daily, harvest, war.Cb == CbKind.Claim && war.Attacker == h.Realm);
        if (best is not { Score: > 0 }) return false;
        SetPlan(e, h, best); return true;
    }

    /// <summary>A host that means to meet another follows the newest word of it: when the place its disc is nearest changes, the plan changes within the day (4.10).</summary>
    private void FollowPlan(ArmyEnv e, ArmyHost h, HostCampaign c)
    {
        var war = Politics?.WarOf(h.Realm); if (war == null) return;
        if (!Book.Hosts.TryGetValue(c.EnemyHost, out var en)) { if (Replan(e, h, c)) StartMarch(e, h, c); return; }
        int men = Math.Max(1, FitMen(h)); var (daily, _) = CostsOf(h.Realm, men);
        var (here, _) = NearestTo(PositionOfHost(h, e.Hour * 3600).X, PositionOfHost(h, e.Hour * 3600).Y);
        var s = ScoreHost(e, h.Realm, here, en, men, daily);
        if (s == null || s.Target == c.Objective) return;
        SetPlan(e, h, s); StartMarch(e, h, c);
    }

    /// <summary>The host sets out by road on its objective (or stands, when it is there already). The hero's call goes through here too (<see cref="Lead"/>).</summary>
    private void StartMarch(ArmyEnv e, ArmyHost h, HostCampaign c)
    {
        var hs = Supply!; var hid = new HostId(h.Supply);
        var (here, _) = NearestTo(PositionOfHost(h, e.Hour * 3600).X, PositionOfHost(h, e.Hour * 3600).Y);
        if (here == c.Objective || c.Objective < 0) { c.Stage = 1; h.State = HostState.Marching; if (Book.Marches.TryGetValue(h.Supply, out var mm)) mm.State = MarchState.Arrived; Arrive(e, h, c); return; }
        var m = BeginMarch(e, hid, [here, c.Objective], "ch_note_am_march_campaign");
        if (m == null) { c.Stage = 3; Release(e, hs, h, "ch_note_am_no_road"); return; }
        h.State = HostState.Marching; c.Stage = 1;
        var entry = Chron(e, "host_marched", "ch_am_marched", [ChronParam.Person("commander", new EntityId(h.Commander)), ChronParam.Place("from", PlaceId(here)), ChronParam.Place("to", PlaceId(c.Objective)), ChronParam.Count("men", Math.Max(1, FitMen(h)))],
            [new EntityId(h.Commander), PlaceId(here), PlaceId(c.Objective)], new Cause(CauseKind.Campaign, h.Entry != 0 ? new EntityId(h.Entry) : null, "", "ch_note_am_march_campaign"));
        m.ChronEntry = entry.Value;
        Book.Touch();
    }

    /// <summary>The hero leads his host (4.13): he names the objective, a place of the enemy or of his own; the host marches there by road.</summary>
    public bool Lead(ArmyEnv e, ArmyHost host, int objective, byte kind = 0)
    {
        if (Supply == null || host.Supply == 0 || !Supply.Exists(new HostId(host.Supply)) || (uint)objective >= (uint)Region.Settlements.Count) return false;
        var c = CampaignOf(host); c.Objective = objective; c.Kind = kind; c.Stage = 0; c.BattleHour = 0; c.PlanDay = (int)e.Day;
        if (host.State is HostState.Engaged or HostState.Besieging) host.State = HostState.Camped;
        StartMarch(e, host, c); return host.State == HostState.Marching || c.Stage == 2;
    }

    private void Arrive(ArmyEnv e, ArmyHost h, HostCampaign c)
    {
        if (c.Kind == 2) { c.Stage = 2; h.State = HostState.Camped; return; }
        c.Stage = 2; h.State = HostState.Besieging; c.SiegeDay = (int)e.Day;
        Book.Touch();
    }

    private void SiegeDay(ArmyEnv e, ArmyHost h, HostCampaign c)
    {
        var hs = Supply!; int day = (int)e.Day; var war = Politics?.WarOf(h.Realm);
        var v = Siege.Resolve(e, this, h, c.Objective, day - c.SiegeDay);
        if (v == SiegeVerdict.Continue) return;
        if (v == SiegeVerdict.Lifted) { c.Stage = 3; Release(e, hs, h, "ch_note_am_siege_lifted"); return; }
        var def = Siege.DefenceOf(this, c.Objective);
        if (war != null)
        {
            var kind = def == DefenceClass.Open ? WarEventKind.Raid : WarEventKind.PlaceTaken;
            int pts = def == DefenceClass.Stone ? CampaignConfig.TakenStone : def == DefenceClass.Palisade ? CampaignConfig.TakenPalisade : CampaignConfig.RaidPoints;
            var entry = Chron(e, "place_taken", "ch_am_taken", [ChronParam.Person("ruler", new EntityId(h.Commander)), ChronParam.Place("place", PlaceId(c.Objective)), ChronParam.Count("men", Math.Max(1, FitMen(h)))],
                [new EntityId(h.Commander), PlaceId(c.Objective)], new Cause(CauseKind.Campaign, h.Entry != 0 ? new EntityId(h.Entry) : null, "", def == DefenceClass.Open ? "ch_note_am_took_sack" : "ch_note_am_took_storm"));
            AddEvent(war.Serial, day, kind, h.Realm, pts, 0, 0, c.Objective, entry.Value);
        }
        h.State = HostState.Camped; c.Stage = 0; c.Objective = -1; c.SiegeDay = -1;
    }

    /// <summary>The ruler kept a levy host in the field through the harvest: the war's weariness grows by it (4.11).</summary>
    private void NoteHarvestHeld(ArmyEnv e, ArmyHost host)
    {
        if (Politics?.WarOf(host.Realm) is { } war) AddEvent(war.Serial, (int)e.Day, WarEventKind.HarvestHeld, host.Realm, 0, 0, 0, host.Point, host.Entry);
    }

    // ------------------------------------------------------------------ the war score (read by Diplomacy.StepWar)

    /// <summary>
    /// The war's events not yet scored, for a war between two realms that both hold fiefs in this region (a war with an abstract realm keeps Phase 6's resolver: null). The events are marked scored by <see cref="ScoredWar"/>.
    /// </summary>
    public WarScoreReport? ScoreFor(War w)
    {
        var rb = Rule; if (rb == null) return null;
        bool a = false, d = false; foreach (var f in rb.Fiefs.Values) { if (f.Realm == w.Attacker) a = true; else if (f.Realm == w.Defender) d = true; if (a && d) break; }
        if (!a || !d) return null;
        var ev = Book.WarEvents.Where(x => x.War == w.Serial && !x.Scored).ToList();
        int Held(int realm) => Book.WarEvents.Count(x => x.War == w.Serial && x.Kind == WarEventKind.HarvestHeld && x.Doer == realm);          // every harvest held, scored or not: the weariness of the war so far
        return new WarScoreReport(ev, Held(w.Attacker), Held(w.Defender));
    }

    /// <summary>The season's war step has counted these events: they are done with (the book keeps the last two years of them).</summary>
    public void ScoredWar(War w)
    {
        foreach (var x in Book.WarEvents) if (x.War == w.Serial) x.Scored = true;
        if (Book.WarEvents.RemoveAll(x => x.Scored && (x.Kind != WarEventKind.HarvestHeld || !(Politics?.Wars.Any(z => z.Serial == x.War && z.Alive) ?? false))) > 0) Book.Touch();
    }
}
