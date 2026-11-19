using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// One abstract battle (abstract-battles-v1 4.1): two sides of soldiers, each with a real <see cref="WoundBook"/>, fought out in steps of <c>step_s</c> sim-seconds. Each step the
/// engaged attackers of each side swing (the Lanchester exchange of 4.2, sampled: every attack action is a draw from the soldier's wound-table row), archers loose, the routers are
/// pursued; every hit copies a recorded wound set of its class into the defender's book, the books bleed and hurt by the same <see cref="WoundBook.AdvanceTo"/> the live fight runs,
/// and a soldier is down or dead by the same reading of his book as there. Morale is the live model's terms integrated over the step (friends seen falling, blows taken, pain, blood,
/// the captain, winning, outnumbered, flanked, alone); below 20 a soldier routs, under 10 and cornered he yields. The battle ends when a side has nobody left in it, both walk away,
/// or at <c>max_steps</c>; then the aftermath (prisoners, first aid by whoever holds the field, the bleeding) and the report.
/// <para>Integer only. Draws: 0x4A0 engagement jitter (battle, step), 0x4A1 targets, outcomes and templates (battle, attacker, step x 16 + k), 0x4A2 morale sight, rout and surrender
/// (battle, step, soldier), 0x4A3 the leader's fate (battle). Every iteration is in soldier order (team, then setup order); hits are applied in time order.</para>
/// </summary>
public sealed class AbstractBattle
{
    public const int TicksPerS = 20;
    public AbstractBattleDef K { get; }
    public MoraleRulesDef M { get; }
    public WoundTables Tables { get; }
    public BattleInput Input { get; }
    public List<Soldier> Soldiers { get; } = [];
    public readonly List<Soldier>[] Side = [[], []];
    public readonly int[] Team = [0, 1];
    public readonly SideKind[] Kind = [SideKind.Folk, SideKind.Folk];
    public int Step { get; private set; }
    public bool Over { get; private set; }
    public int Winner { get; private set; } = -1;
    public bool Stalemate { get; private set; }
    public int GapMm { get; private set; }
    public int ContactStep { get; private set; } = -1;
    public BattleReport Report { get; } = new();
    public List<BattleCommand> Commands { get; } = [];
    /// <summary>Statistics for AB-5: attack actions and outcome counts by class, and the expected outs from the coefficients.</summary>
    public long Actions, Outs;
    public readonly long[] ActionsBy = new long[2], WoundingBy = new long[2];
    /// <summary>Diagnostics: routs, yields from the line, yields while routing, flights.</summary>
    public int Routs, LineYields, RouterYields, Flights;
    /// <summary>Diagnostics (the calibration tool): every landed outcome with the weapon class and the template copied.</summary>
    public Action<WeaponClass, OutcomeClass, WoundTemplate?>? OnHit;
    /// <summary>Diagnostics and AB-14: every shot's outcome class.</summary>
    public Action<OutcomeClass>? OnShot;
    public long ExpectedOutsPm;
    private readonly ulong _seed;
    private int _quiet;
    private readonly int[] _start = new int[2];
    private readonly int[] _fallsThisStep = new int[2];
    private readonly int[] _routersThisStep = new int[2];
    private readonly List<(int Step, int Side)> _kills = [];
    private readonly bool[] _leaderFell = new bool[2];
    private readonly int[] _captain = [-1, -1];
    private readonly List<Hit> _hits = [];
    private bool _mud, _woods, _contact;
    private readonly int[] _frontSlots = new int[2], _seePm = new int[2];
    private readonly long[] _cornerRatio = new long[2];
    private int _cornerStep = -1;

    private readonly record struct Hit(long Sec, int Defender, int Attacker, int K, WoundTemplate? T, OutcomeClass O, WeaponClass W);

    public AbstractBattle(FightContent c, WoundTables tables, BattleInput input, AbstractBattleDef? k = null)
    {
        Tables = tables; Input = input;
        K = k ?? c.Db.All<AbstractBattleDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new AbstractBattleDef { Id = "default" };
        M = MoraleRules.For(c.Db).D;
        _seed = DetRng.Mix(input.Seed ^ DetRng.Mix(input.BattleId + 0x4A0UL));
        var teams = input.Kits.Select(x => x.Team).Distinct().Order().ToList();
        Team[0] = teams.Count > 0 ? teams[0] : 0; Team[1] = teams.Count > 1 ? teams[1] : Team[0] + 1;
        for (int s = 0; s < 2; s++) Kind[s] = input.SideKinds.TryGetValue(Team[s], out var sk) ? sk : SideKind.Folk;
        var terrain = input.Terrain;
        _mud = terrain?.Base is Footing.Mud or Footing.Snow or Footing.Ice;
        _woods = terrain?.Base == Footing.Woods;
        for (int i = 0; i < input.Kits.Count; i++)
        {
            var kit = input.Kits[i];
            int side = kit.Team == Team[0] ? 0 : 1;
            var plan = c.T.Plan("human");                 // the tables and templates are the human plan's (a beast fights as a man of its size: v1)
            var book = input.Books is { } bl && i < bl.Count && bl[i] is { } bk ? BookCopy.Clone(bk) : input.BookOf?.Invoke(kit.Person) is { } b0 ? BookCopy.Clone(b0) : null;
            book ??= new WoundBook(input.Seed, (input.BattleId << 16) + (ulong)i + 1, (int)((long)plan.BloodMl * Math.Max(1, kit.SizeCm3) / plan.SizeCm3), kit.Toughness, plan.Id) { LastSec = input.GameSec };
            if (book.LastSec < input.GameSec) book.AdvanceTo(input.GameSec);
            book.CacheFunction = true;                    // P4 close-out: the body is read before and after every hit; the copy keeps the last reading (cleared in Finish)
            var sh = AbstractClasses.ShooterOf(c, kit);
            var mc = AbstractClasses.MeleeOf(c, kit);
            bool mounted = input.Mounted.Contains(i);
            var so = new Soldier
            {
                Index = i, Side = side, Kit = kit, Melee = mounted && mc == WeaponClass.Polearm ? WeaponClass.Lance : mc, Shooter = sh, Armour = AbstractClasses.ArmourOf(c.T, kit.Worn),
                Skill = kit.Skill, ArcherySkill = kit.ArcherySkill >= 0 ? kit.ArcherySkill : kit.Skill, Shield = kit.Shield != null && sh == null, Mounted = mounted,
                Ammo = sh != null ? (kit.AmmoCount > 0 ? kit.AmmoCount : 24) : 0, Throws = kit.Throwables.Where(t => t.Shape == "javelin").Sum(t => t.Count),
                Braces = mc == WeaponClass.Polearm && !mounted,
                CourageX100 = MoraleRules.For(c.Db).CourageX100(kit.Courage), Book = book, Plan = plan,
            };
            if (input.Statuses is { } st && i < st.Count) so.Status = st[i];
            if (input.Routing is { } rt && i < rt.Count && rt[i] && so.Status == SoldierStatus.Active) { so.Routing = true; so.RoutStep = 0; }
            if (book.Dead) so.Status = SoldierStatus.Dead;
            Soldiers.Add(so); Side[side].Add(so);
        }
        for (int s = 0; s < 2; s++) _start[s] = Math.Max(1, Side[s].Count(x => x.Status == SoldierStatus.Active));
        Place();
        if (input.Leaders)
            for (int s = 0; s < 2; s++)
                if (Side[s].Count >= 3)
                {
                    var best = Side[s].Where(x => x.Active).OrderByDescending(x => x.Skill).ThenBy(x => x.Index).FirstOrDefault();
                    if (best != null) { best.Captain = true; _captain[s] = best.Index; best.Row = Math.Max(best.Row, 1); }      // the captain leads from just behind the front rank
                }
        foreach (var so in Soldiers) Refresh(so);
        for (int s = 0; s < 2; s++) _frontSlots[s] = Side[s].Count(x => x.Row == 0 && x.Active && !x.Mounted);
        // who sees a friend fall (morale 4.1 b: within 10 m): the share of the side within that radius of the average soldier where the setup placed them
        long r2 = (long)M.FriendDownRadiusM * 1000 * M.FriendDownRadiusM * 1000;
        for (int s = 0; s < 2; s++)
        {
            long near = 0; int n = Side[s].Count;
            foreach (var x in Side[s]) foreach (var y in Side[s]) if (x != y) { long dx = x.Kit.X - y.Kit.X, dy = x.Kit.Y - y.Kit.Y; if (dx * dx + dy * dy <= r2) near++; }
            _seePm[s] = n <= 1 ? 1000 : (int)Math.Min(1000, near * 1000 / ((long)n * (n - 1)) * K.MoraleK / 10);
        }
        // the swings of a line are not in step: each soldier starts at his own phase of the blow rate (0x4A0)
        foreach (var so in Soldiers) { so.BlowAccPm = so.Mounted ? 0 : R(RngStreams.AbstractNoise, 0, (ulong)(so.Index + 1) << 8, 2, 1000); so.ShotAccPm = R(RngStreams.AbstractNoise, 0, (ulong)(so.Index + 1) << 8, 3, 1000); }
        Report.Id = input.BattleId; Report.Place = input.Place; Report.GameSec = input.GameSec; Report.Cause = input.Cause;
        Report.Teams.Add(Team[0]); Report.Teams.Add(Team[1]);
        for (int s = 0; s < 2; s++) Report.Sides[s].Start = _start[s];
        Report.QualityX100 = QualityX100();
    }

    // ------------------------------------------------------------------ setup: rows, positions, the gap

    private void Place()
    {
        long[] cx = new long[2], cy = new long[2]; int[] n = new int[2];
        foreach (var s in Soldiers) { cx[s.Side] += s.Kit.X; cy[s.Side] += s.Kit.Y; n[s.Side]++; }
        for (int s = 0; s < 2; s++) if (n[s] > 0) { cx[s] /= n[s]; cy[s] /= n[s]; }
        // the axis between the sides: the way the lines face (side 0's facing less side 1's; a line drawn up faces its enemy), else the line between their centres; a tilt of the
        // centres' line (one end of a line shorter) would put the ends of a straight line in its second rank
        long fx = 0, fy = 0;
        foreach (var s in Soldiers) { int sg = s.Side == 0 ? 1 : -1; fx += sg * s.Kit.FaceX; fy += sg * s.Kit.FaceY; }
        var (ax, ay) = FightMath.Norm((int)Math.Clamp(fx, -2_000_000_000, 2_000_000_000), (int)Math.Clamp(fy, -2_000_000_000, 2_000_000_000));
        long dcx = cx[1] - cx[0], dcy = cy[1] - cy[0];
        if (ax == 0 && ay == 0 || dcx * ax + dcy * ay <= 0) (ax, ay) = FightMath.Norm((int)Math.Clamp(dcx, -2_000_000_000, 2_000_000_000), (int)Math.Clamp(dcy, -2_000_000_000, 2_000_000_000));
        if (ax == 0 && ay == 0) ax = FightMath.One;
        long Along(Soldier s) => ((long)s.Kit.X * ax + (long)s.Kit.Y * ay) / FightMath.One;           // towards side 1
        long Across(Soldier s) => ((long)s.Kit.Y * ax - (long)s.Kit.X * ay) / FightMath.One;
        // each side's main body: the fighters within 6 m (along the line between the sides) of its median; who stands beyond the enemy's ends or behind it is on a flank or the rear
        long[] med = new long[2];
        for (int sd = 0; sd < 2; sd++)
        {
            var al = Side[sd].Where(x => !(x.Shooter != null && x.Melee == WeaponClass.Unarmed)).Select(Along).Order().ToList();
            if (al.Count == 0) al = [.. Side[sd].Select(Along).Order()];
            med[sd] = al.Count == 0 ? 0 : al[al.Count / 2];
        }
        bool Main(Soldier x) => Math.Abs(Along(x) - med[x.Side]) <= 6000;
        long[] front = [long.MinValue, long.MaxValue];
        long[] lo = [long.MaxValue, long.MaxValue], hi = [long.MinValue, long.MinValue], back = [long.MaxValue, long.MinValue];
        foreach (var s in Soldiers)
        {
            if (s.Shooter != null && s.Melee == WeaponClass.Unarmed || !Main(s)) continue;
            long a = Along(s), c = Across(s);
            if (s.Side == 0) { front[0] = Math.Max(front[0], a); back[0] = Math.Min(back[0], a); } else { front[1] = Math.Min(front[1], a); back[1] = Math.Max(back[1], a); }
            lo[s.Side] = Math.Min(lo[s.Side], c); hi[s.Side] = Math.Max(hi[s.Side], c);
        }
        for (int s = 0; s < 2; s++) if (front[s] == long.MinValue || front[s] == long.MaxValue) { front[s] = s == 0 ? Side[0].Select(Along).DefaultIfEmpty(0).Max() : Side[1].Select(Along).DefaultIfEmpty(0).Min(); }
        GapMm = (int)Math.Clamp(front[1] - front[0], 0, 1_000_000);
        foreach (var s in Soldiers)
        {
            long a = Along(s), c = Across(s);
            long depth = s.Side == 0 ? front[0] - a : a - front[1];
            s.Row = s.Shooter != null ? 2 : depth <= 1200 || !Main(s) ? 0 : depth <= 3000 ? 1 : 2;
            int e = 1 - s.Side;
            if (lo[e] != long.MaxValue && (c < lo[e] - 3000 || c > hi[e] + 3000)) s.Position = BattlePosition.Flank;
            if (back[e] != long.MaxValue && back[e] != long.MinValue && (s.Side == 0 ? a > back[e] + 1000 : a < back[e] - 1000)) s.Position = BattlePosition.Rear;
        }
    }

    private void Refresh(Soldier s)
    {
        if (s.WoundsSeen == s.Book.Records.Count) return;
        s.WoundsSeen = s.Book.Records.Count;
        var bs = s.Book.Function(s.Plan);
        s.WeaponArm = bs.WeaponHand[1] || bs.WeaponHand[0];
        s.ShieldArm = bs.Arm[0] >= 400;
        s.CanStand = bs.WalkSpeedPm > 0 || bs.Balance > 0;
    }

    // ------------------------------------------------------------------ draws (0x4A0 .. 0x4A3)

    private int R(uint stream, int step, ulong entity, uint draw, int n) => DetRng.Range(_seed, step, entity, stream, draw, 0, Math.Max(1, n));

    // ------------------------------------------------------------------ the loop

    /// <summary>Runs the battle to its end and the aftermath; returns the report.</summary>
    public BattleReport Run()
    {
        while (!Over) StepOnce();
        Finish();
        return Report;
    }

    private long StepStartSec => Input.GameSec + (long)Step * K.StepS * TicksPerS;
    private int StepTicks => K.StepS * TicksPerS;

    /// <summary>One step of the exchange (4.1.2).</summary>
    public void StepOnce()
    {
        if (Over) return;
        _hits.Clear();
        _fallsThisStep[0] = _fallsThisStep[1] = 0; _routersThisStep[0] = _routersThisStep[1] = 0;
        _leaderFell[0] = _leaderFell[1] = false;
        foreach (var s in Soldiers) { s.HitsThisStep = 0; s.TargetedBy = 0; }
        foreach (var s in Soldiers) if (s.InLine && s.Target >= 0 && Soldiers[s.Target].Active) Soldiers[s.Target].TargetedBy++; else if (!s.InLine) s.Target = -1;
        int blows0 = 0, blows1 = 0;
        bool contact = GapMm <= K.ContactMm;
        bool chargeStep = false;
        _contact = contact;
        if (!contact)
        {
            // the lines close (4.1.2; a side that is mostly bows stands; a side all mounted rides in)
            int close = 0;
            for (int s = 0; s < 2; s++)
            {
                var line = Side[s].Where(x => x.InLine).ToList();
                if (line.Count == 0) continue;
                int bows = line.Count(x => x.Shooter != null && x.Ammo > 0);
                if (bows * 2 >= line.Count) continue;
                bool rid = line.All(x => x.Mounted) && !_mud;
                bool charge = line.Any(x => x.Kit.Stance == "aggressive");
                close += rid ? K.RiderMmps : charge ? K.ChargeMmps : K.AdvanceMmps;
            }
            if (_mud) close = close * K.MudBlowsPm / 1000;
            int before = GapMm;
            GapMm = Math.Max(0, GapMm - close * K.StepS);
            if (GapMm <= K.ContactMm) { ContactStep = Step; chargeStep = true; contact = true; }
            if (close == 0 && before == GapMm) _quiet++;
        }
        else if (ContactStep < 0) ContactStep = Step;
        for (int s = 0; s < 2; s++)
        {
            int n = Shoot(s, contact);
            if (s == 0) blows0 += n; else blows1 += n;
        }
        _contact = contact;
        if (contact)
            for (int s = 0; s < 2; s++)
            {
                int n = Melee(s, chargeStep);
                n += Pursue(s);
                if (s == 0) blows0 += n; else blows1 += n;
            }
        ApplyHits();
        long end = StepStartSec + StepTicks;
        foreach (var s in Soldiers) Vitals(s, end);
        Morale();
        Report.Timeline.Add(new BattleStepLine(Step, _fallsThisStep[0], _fallsThisStep[1], _routersThisStep[0], _routersThisStep[1], blows0, blows1));
        if (contact) _quiet = blows0 + blows1 == 0 ? _quiet + 1 : 0;
        Step++;
        CheckEnd();
    }

    private int TerrainBlowsPm => (_mud ? K.MudBlowsPm : 1000) * (Input.Night ? K.NightBlowsPm : 1000) / 1000;

    /// <summary>Archers loose and javelins fly (4.1.2 c): each shooter's shots this step at a target by row weight, the row of its launcher at the range band of the gap.</summary>
    private int Shoot(int side, bool contact)
    {
        int e = 1 - side, n = 0;
        var targets = Side[e].Where(x => x.Active && !x.Routing).ToList();
        if (targets.Count == 0) return 0;
        int range = contact ? 12_000 : GapMm;
        if (_woods) range = range * 1000 / Math.Max(1, K.WoodsRangePm);
        int rangeM = range / 1000;
        foreach (var s in Side[side])
        {
            if (!s.InLine || !s.WeaponArm) continue;
            if (s.Shooter is { } wc && s.Ammo > 0 && rangeM <= 200)
            {
                // the live rate of that launcher at that range (draw, aim, loose, nock: the table's measured shots a minute), or the content's fallback
                int perMinX10 = Tables.ShotApmX10(wc, targets[0].Armour, rangeM, false);
                if (perMinX10 <= 0) perMinX10 = 10 * (wc == WeaponClass.Crossbow ? K.CrossbowPerMin : K.LongbowPerMin);
                s.ShotAccPm += perMinX10 * K.StepS * 100 / 60;
                int shots = Math.Min(s.Ammo, s.ShotAccPm / 1000);
                s.ShotAccPm -= shots * 1000;
                int hitPm = (Input.Weather is "rain" or "storm" || Input.Terrain?.HeavyRain == true ? K.RainBowPm : 1000) * (Input.Night ? K.NightBlowsPm : 1000) / 1000;
                for (int k = 0; k < shots; k++)
                {
                    s.Ammo--;
                    Strike(s, targets, wc, rangeM, k, shot: true, shieldOk: true, hitPm);
                    n++;
                }
            }
            else if (s.Throws > 0 && !contact && GapMm <= 20_000 && GapMm >= 4_000)
            {
                s.Throws--;
                Strike(s, targets, WeaponClass.Thrown, Math.Max(6, rangeM), 15, shot: true, shieldOk: true, 1000);
                n++;
            }
        }
        return n;
    }

    /// <summary>The melee exchange of one side (4.1.2 a, b): the engaged attackers (front rows first, capped by the frontage the enemy gives), their blows this step, each at a target.</summary>
    private int Melee(int side, bool chargeStep)
    {
        int e = 1 - side;
        var attackers = Side[side].Where(x => x.InLine && x.WeaponArm && x.CanStand && !(x.Shooter != null && x.Ammo > 0)).OrderBy(x => x.Row).ThenBy(x => x.Index).ToList();
        var targets = Side[e].Where(x => x.Active && !x.Routing).ToList();
        if (attackers.Count == 0 || targets.Count == 0) return 0;
        int eng = Input.Narrow ? K.EngagementNarrowPm : K.EngagementPm;
        int cap = Input.Narrow ? K.OutnumberCapNarrowPm : K.OutnumberCapPm;
        long frontage = (long)eng * targets.Count * cap / 1_000_000;
        int jit = 1000 - K.JitterPm + R(RngStreams.AbstractNoise, Step, (ulong)(side + 1), 0, 2 * K.JitterPm + 1);
        int engaged = (int)Math.Clamp(frontage * jit / 1000, 1, attackers.Count);
        // the front rank fights; the ranks behind step into the gaps it leaves, and a polearm reaches over a friend's shoulder (4.1.2 a; the line's width is its front rank at the start)
        int slots = Math.Max(1, _frontSlots[side]);
        int n = 0, used = 0, engagedSoFar = 0;
        bool braced = targets.Count(x => x.Braces && x.InLine) * 4 >= targets.Count;
        foreach (var a in attackers)
        {
            bool riding = a.Mounted && !_mud && !_woods;
            if (!a.Mounted)
            {
                bool reachesOver = a.Melee is WeaponClass.Polearm && a.Row == 1;
                if (engagedSoFar >= engaged) continue;
                if (used >= slots && !reachesOver) continue;
                if (!reachesOver) used++;
            }
            engagedSoFar++;
            var wc = a.Melee == WeaponClass.Lance && !riding ? WeaponClass.Polearm : a.Melee;
            var tgt = PickTarget(a, targets, false, 0);
            bool shieldOk = a.Position == BattlePosition.Front;
            // the attack rate is the live fight's own for this matchup (the table's apm), scaled to the press; a rider strikes once a pass (hit and run, on any ground: on mud or in
            // woods he cannot gallop and has no charge); the charge is the first pass's own extra
            int apm = a.Mounted ? (riding ? K.RiderBlowsPerMinX10 : K.RiderSlowBlowsPerMinX10) : Tables.ApmX10(wc, tgt.Armour, a.Skill - tgt.Skill, shieldOk && tgt.Shield) * K.PressRatePct / 100;
            if (!a.Mounted && tgt.Mounted) apm = apm * (_mud || _woods ? K.VsRiderSlowRatePm : K.VsRiderRatePm) / 1000;
            if (!a.Mounted && a.TargetedBy > 1) apm = (int)((long)apm * 1000 / (1000 + K.BesetRatePm * (a.TargetedBy - 1)));        // a man beset by two or three defends more than he strikes
            int rate = apm * K.StepS * 100 / 60 * TerrainBlowsPm / 1000;      // permille of a blow per step
            if (a.Position == BattlePosition.Flank) rate = rate * (1000 + K.FlankBonusPm) / 1000;
            else if (a.Position == BattlePosition.Rear) rate = rate * (1000 + K.RearBonusPm) / 1000;
            if (chargeStep && riding && !braced) rate += 1000 + K.ChargePm;                       // the charge: a blow at contact for every rider, plus charge_pm more
            a.BlowAccPm += rate;
            int blows = Math.Min(15, a.BlowAccPm / 1000);
            a.BlowAccPm -= blows * 1000;
            for (int k = 0; k < blows; k++) { Strike(a, tgt, wc, 0, k, shot: false, shieldOk, K.PressHitPm); n++; }
        }
        return n;
    }

    /// <summary>Pursuit (4.1.4): enemies free of the line (not engaged; riders always) chase this side's routers in reach: blows from behind, no shield, the rear bonus.</summary>
    private int Pursue(int side)
    {
        int e = 1 - side;
        var routers = Side[e].Where(x => x.Active && x.Routing).ToList();
        if (routers.Count == 0) return 0;
        var line = Side[e].Count(x => x.InLine);
        var free = Side[side].Where(x => x.InLine && x.WeaponArm && x.CanStand && !(x.Shooter != null && x.Ammo > 0)).OrderBy(x => x.Row).ThenBy(x => x.Index).ToList();
        int busy = (int)Math.Min(free.Count, (long)K.EngagementPm * line * K.OutnumberCapPm / 1_000_000);
        int n = 0, i = 0;
        foreach (var a in free)
        {
            if (i++ < busy && !a.Mounted) continue;
            if (!a.Mounted && R(RngStreams.AbstractNoise, Step, (ulong)(a.Index + 1) << 8, 1, 1000) >= K.PursuePm) continue;
            int rate = K.PursuitBlowsPerMinX10 * K.StepS * 100 / 60 * TerrainBlowsPm / 1000 * (1000 + K.RearBonusPm) / 1000;
            a.BlowAccPm += rate;
            int blows = Math.Min(15, a.BlowAccPm / 1000);
            a.BlowAccPm -= blows * 1000;
            var wc = a.Melee == WeaponClass.Lance && (_mud || _woods) ? WeaponClass.Polearm : a.Melee;
            for (int k = 0; k < blows; k++) { Strike(a, routers, wc, 0, k, shot: false, shieldOk: false, K.PressHitPm); n++; }
        }
        return n;
    }

    private static bool IsShooter(Soldier s) => s.Shooter != null && s.Ammo > 0;

    private int Weight(Soldier t) => t.Row == 0 ? K.FrontPm : t.Row == 1 ? K.MiddlePm : K.RearPm;

    /// <summary>One attack action (4.1.2 d, e): the target by row weight, the outcome from the row, the template; the hit waits for the step's ordered application.</summary>
    /// <summary>Who an attack action goes at (4.1.2 d): in the press a man keeps his opponent until that one is out or runs (the live brain's target holds); a new one is picked by
    /// row weight, the less beset first; a shot goes at anyone by row weight.</summary>
    private Soldier PickTarget(Soldier a, List<Soldier> targets, bool shot, int k)
    {
        if (!shot && a.Target >= 0 && Soldiers[a.Target] is { } held && held.Side != a.Side && held.Active && held.Routing == (targets.Count > 0 && targets[0].Routing)) return held;
        uint d = (uint)(Step * 16 + Math.Min(15, k)) * 4u + (shot ? 0x8000_0000u : 0u);
        long sum = 0;
        foreach (var t in targets) sum += Weight(t) * 1000 / (1000 + (shot ? 0 : K.BesetPm * t.TargetedBy));
        long roll = DetRng.Range(_seed, Step, (ulong)(a.Index + 1), RngStreams.WoundSample, d, 0, (int)Math.Min(int.MaxValue, Math.Max(1, sum)));
        var tgt = targets[^1];
        foreach (var t in targets) { roll -= Weight(t) * 1000 / (1000 + (shot ? 0 : K.BesetPm * t.TargetedBy)); if (roll < 0) { tgt = t; break; } }
        if (!shot)
        {
            if (a.Target >= 0) Soldiers[a.Target].TargetedBy = Math.Max(0, Soldiers[a.Target].TargetedBy - 1);
            a.Target = tgt.Index; tgt.TargetedBy++;
        }
        return tgt;
    }

    private void Strike(Soldier a, List<Soldier> targets, WeaponClass wc, int rangeM, int k, bool shot, bool shieldOk, int hitPm) =>
        Strike(a, PickTarget(a, targets, shot, k), wc, rangeM, k, shot, shieldOk, hitPm);

    /// <summary>One attack action (4.1.2 e): the outcome from the row, the template; the hit waits for the step's ordered application.</summary>
    private void Strike(Soldier a, Soldier tgt, WeaponClass wc, int rangeM, int k, bool shot, bool shieldOk, int hitPm)
    {
        uint d = (uint)(Step * 16 + Math.Min(15, k)) * 4u + (shot ? 0x8000_0000u : 0u);
        bool shield = shieldOk && tgt.Shield && tgt.ShieldArm;
        if (!shot && tgt.TargetedBy > 1) hitPm = (int)((long)hitPm * (1000 + K.BesetHitPm * Math.Min(3, tgt.TargetedBy - 1)) / 1000);     // and cannot ward every blade at once
        if (!shot && tgt.Mounted && !a.Mounted) hitPm = (int)((long)hitPm * (_mud || _woods ? K.VsRiderSlowHitPm : K.VsRiderHitPm) / 1000);        // a rider who keeps moving is seldom in reach
        if (Input.Uphill != 0) hitPm = hitPm * (Input.Uphill == (a.Side == 0 ? -1 : 1) ? 1000 * 1000 / K.UphillHitPm : 1000) / 1000;      // the uphill defender is harder to reach
        int r = DetRng.Range(_seed, Step, (ulong)(a.Index + 1), RngStreams.WoundSample, d + 1, 0, 1000);
        var o = shot ? Tables.Draw(wc, tgt.Armour, rangeM, shield, r, shot: true, hitPm: hitPm)
                     : Tables.Draw(wc, tgt.Armour, (a.Mounted ? a.Skill : a.Skill) - tgt.Skill, shield, r, hitPm: hitPm);
        if (shot) OnShot?.Invoke(o);
        Actions++; ActionsBy[a.Side]++;
        if (o >= OutcomeClass.Light) WoundingBy[a.Side]++;
        ExpectedOutsPm += shot ? 0 : Tables.OutPm(wc, tgt.Armour, a.Skill - tgt.Skill, shield, hitPm);
        if (o >= OutcomeClass.Serious && !shot) Outs++;
        if (o == OutcomeClass.Miss) return;
        WoundTemplate? tpl = null;
        if (o >= OutcomeClass.Light)
        {
            var lib = Tables.Templates(wc, tgt.Armour, o);
            if (lib.Length > 0) tpl = lib[DetRng.Range(_seed, Step, (ulong)(a.Index + 1), RngStreams.WoundSample, d + 2, 0, lib.Length)];
        }
        OnHit?.Invoke(wc, o, tpl);
        long sec = StepStartSec + (long)StepTicks * (k + 1) / 17;
        _hits.Add(new Hit(sec, tgt.Index, a.Index, k, tpl, o, wc));
    }

    private void ApplyHits()
    {
        _hits.Sort((x, y) => x.Sec != y.Sec ? x.Sec.CompareTo(y.Sec) : x.Defender != y.Defender ? x.Defender.CompareTo(y.Defender) : x.Attacker != y.Attacker ? x.Attacker.CompareTo(y.Attacker) : x.K.CompareTo(y.K));
        foreach (var h in _hits)
        {
            var t = Soldiers[h.Defender];
            if (t.Status is SoldierStatus.Dead or SoldierStatus.Fled or SoldierStatus.Surrendered) continue;
            t.HitsTaken++; t.HitsThisStep++;
            if (h.T == null) continue;
            t.Book.Apply(t.Plan, h.T.Wounds, Math.Max(h.Sec, t.Book.LastSec), h.Sec - Input.GameSec, Input.BattleId, h.T.SkullMilli, dirty: false);
            if (h.O > t.Worst) t.Worst = h.O;
            t.LastHitBy = h.Attacker; t.LastHitWith = h.W; t.LastHitClass = h.O;
            if (h.O == OutcomeClass.Severed) t.PenaltyX100 += M.LimbLostX100;
        }
    }

    /// <summary>The soldier's body at the step's end (the live fight's <c>UpdateVitals</c>): dead, down (unconscious, dying, collapsed or unable to stand), or back up.</summary>
    private void Vitals(Soldier s, long endSec)
    {
        if (s.Status is SoldierStatus.Dead or SoldierStatus.Fled or SoldierStatus.Surrendered or SoldierStatus.Captured) { if (s.Status != SoldierStatus.Dead) s.Book.AdvanceTo(endSec); return; }
        s.Book.AdvanceTo(endSec);
        Refresh(s);
        if (s.Book.Dead) { Out(s, SoldierStatus.Dead); return; }
        bool down = s.Book.State is Consciousness.Unconscious or Consciousness.Dying or Consciousness.Collapsed || !s.CanStand;
        if (down && s.Status == SoldierStatus.Active) Out(s, SoldierStatus.Down);
        else if (!down && s.Status == SoldierStatus.Down) { s.Status = SoldierStatus.Active; s.OutStep = -1; }
    }

    private void Out(Soldier s, SoldierStatus st)
    {
        bool wasActive = s.Status == SoldierStatus.Active;
        s.Status = st;
        if (!wasActive) return;
        s.OutStep = Step;
        if (st is SoldierStatus.Down or SoldierStatus.Dead or SoldierStatus.Surrendered)
        {
            _fallsThisStep[s.Side]++;
            _kills.Add((Step, 1 - s.Side));
            if (s.Captain) _leaderFell[s.Side] = true;
        }
    }

    // ------------------------------------------------------------------ morale (4.1.2 g: the live model's terms over the step)

    private void Morale()
    {
        var R0 = M;
        int dt = K.StepS;
        int[] inLine = [Side[0].Count(x => x.InLine), Side[1].Count(x => x.InLine)];
        int[] routing = [Side[0].Count(x => x.Active && x.Routing), Side[1].Count(x => x.Active && x.Routing)];
        bool[] flanked = [Side[1].Any(x => x.InLine && x.Position != BattlePosition.Front), Side[0].Any(x => x.InLine && x.Position != BattlePosition.Front)];
        int routBelow = R0.RoutBelowX100 + (Input.Night ? K.NightRoutPoints * 100 : 0);
        for (int side = 0; side < 2; side++)
        {
            int e = 1 - side;
            int obsPm = _seePm[side];
            bool captain = _captain[side] >= 0 && Soldiers[_captain[side]].InLine;
            bool captainShoots = captain && IsShooter(Soldiers[_captain[side]]);
            // the press is local (morale 4.1: outnumbered within 12 m, the captain within 15 m): a man in the melee counts the melee, a shooter behind it counts his own
            int meleeOurs = Side[side].Count(x => x.InLine && !IsShooter(x)), meleeTheirs = Side[e].Count(x => x.InLine && !IsShooter(x));
            bool outMelee = _contact && meleeTheirs > 0 && (long)meleeTheirs * 1000 >= (long)R0.OutnumberedRatioPm * Math.Max(1, meleeOurs);
            int window = Math.Max(1, R0.WinningWindowS / Math.Max(1, dt));
            int ours = 0, theirs = 0;
            foreach (var (st, by) in _kills) if (st > Step - window) { if (by == side) ours++; else theirs++; }
            bool winning = ours >= 2 && (long)ours * 1000 >= (long)R0.WinningRatioPm * theirs;
            bool outnumbered = inLine[e] > 0 && (long)inLine[e] * 1000 >= (long)R0.OutnumberedRatioPm * Math.Max(1, inLine[side]);
            int falls = _fallsThisStep[side];
            int routSeen = routing[side] * obsPm / 1000;
            bool chased = inLine[e] > 0;
            foreach (var s in Side[side])
            {
                if (!s.Active) continue;
                int seen = 0;
                for (int j = 0; j < Math.Min(falls, 48); j++) if (R(RngStreams.RoutSample, Step, (ulong)(s.Index + 1), (uint)j, 1000) < obsPm) seen++;
                s.PenaltyX100 = Math.Max(0, s.PenaltyX100 - R0.DecayX100PerS * dt);
                s.PenaltyX100 += Math.Min(R0.FriendDownCapX100 * dt * MoraleRules.TicksPerS / MoraleRules.EveryTicks, seen * R0.FriendDownX100) + s.HitsThisStep * R0.HitTakenX100;
                if (_leaderFell[side]) s.PenaltyX100 += R0.LeaderDownX100;
                if (routSeen > 0) s.RoutAccX100 = Math.Min(R0.RoutingCapX100, s.RoutAccX100 + R0.RoutingNeighbourX100PerS * dt * routSeen);
                else s.RoutAccX100 = Math.Max(0, s.RoutAccX100 - R0.DecayX100PerS * dt);
                int pain = (int)Math.Min(100, s.Book.PainX100 * 100L / Math.Max(1, s.Book.KnockoutPainX100));
                // beset in the press (the live flank check: enemies close on two sides) for the share of the line in contact, or round a flank by position
                int flank = flanked[side] ? R0.FlankedX100 : _contact && s.Row < 2 ? R0.FlankedX100 * K.MeleeFlankedPm / 1000 : 0;
                long v = s.CourageX100 - s.PenaltyX100 - s.RoutAccX100 - flank
                    - Math.Clamp(pain, 0, 100) * R0.PainKPm / 10 - Math.Clamp(s.Book.LossPm, 0, 1000) * R0.BloodLossKPm / 100
                    + (captain && captainShoots == IsShooter(s) ? R0.CaptainX100 : 0) + (winning ? R0.WinningX100 : 0) - ((IsShooter(s) ? outnumbered : outMelee || outnumbered) ? R0.OutnumberedX100 : 0);
                if (inLine[side] == 1 && inLine[e] > 0 && s.InLine) v -= Math.Min(R0.AloneCapX100, R0.AloneX100 + R0.AlonePerFoeX100 * Math.Max(0, Math.Min(inLine[e], 5) - 1));
                s.MoraleX100 = (int)Math.Clamp(v, 0, 10_000);
                if (s.InLine) { if (s.MoraleX100 < routBelow) Rout(s, e, _contact); }
                else if (chased && _contact && s.MoraleX100 < K.RouterYieldBelow * 100 && Step > s.RoutStep) TrySurrender(s, e, CorneredPm(side, K.RouterCorneredPm));
            }
        }
        // collapse (4.1.2 g): a side with too few still in the line, or leaderless and thin, breaks at once
        for (int side = 0; side < 2; side++)
        {
            int line = Side[side].Count(x => x.InLine);
            if (line == 0 || _start[side] == 0) continue;
            bool leaderless = _captain[side] >= 0 && !Soldiers[_captain[side]].InLine;
            if ((long)line * 1000 < (long)K.CollapseActivePm * _start[side] || leaderless && (long)line * 1000 < (long)K.LeaderlessCollapsePm * _start[side])
                foreach (var s in Side[side]) if (s.InLine) Rout(s, 1 - side, _contact);
        }
        // routers out of reach have fled
        int flee = Math.Max(1, (K.FleeS + K.StepS - 1) / K.StepS);
        foreach (var s in Soldiers)
            if (s.Active && s.Routing && Step - s.RoutStep >= flee) { s.Status = SoldierStatus.Fled; s.Routing = false; s.OutStep = Step; Flights++; }
    }

    /// <summary>The chance a man of <paramref name="side"/> is cornered (morale 4.1: enemies close on two sides or across his way out), by how many enemies still fight for each of his
    /// side still on its feet: <c>cornered_pm x (enemies / ours)^3</c>, at most 1000 (a thin line in front leaves the way back open; one that wraps round closes it).</summary>
    private int CorneredPm(int side, int basePm)
    {
        if (_cornerStep != Step)
        {
            _cornerStep = Step;
            for (int sd = 0; sd < 2; sd++)
            {
                int foes = 0, ours = 0;
                foreach (var x in Side[1 - sd]) if (x.InLine && !(x.Shooter != null && x.Ammo > 0)) foes++;
                foreach (var x in Side[sd]) if (x.Active) ours++;
                _cornerRatio[sd] = Math.Min(4000L, (long)foes * 1000 / Math.Max(1, ours));             // enemies per man, x1000: surrounded grows as its cube
            }
        }
        long r = _cornerRatio[side];
        return (int)Math.Min(1000L, basePm * r / 1000 * r / 1000 * r / 1000);
    }

    private bool TrySurrender(Soldier s, int enemySide, int corneredPm)
    {
        if (R(RngStreams.RoutSample, Step, (ulong)(s.Index + 1), 100, 1000) >= corneredPm) return false;
        int accept = Kind[enemySide] switch { SideKind.Army => M.AcceptArmyPm, SideKind.Bandits => M.AcceptBanditPm, SideKind.Beasts or SideKind.Draugr => 0, _ => M.AcceptFolkPm };
        if (R(RngStreams.RoutSample, Step, (ulong)(s.Index + 1), 101, 1000) >= accept) return false;
        if (s.Routing) RouterYields++; else LineYields++;
        s.Routing = false;
        Out(s, SoldierStatus.Surrendered);
        return true;
    }

    private void Rout(Soldier s, int enemySide, bool contact)
    {
        if (s.Routing) return;
        // under 10 and cornered: he asks to yield, and the winner's kind accepts or not (morale 4.2)
        if (s.MoraleX100 < M.SurrenderBelowX100 && TrySurrender(s, enemySide, contact ? CorneredPm(s.Side, K.CorneredPm) : 0)) return;
        s.Routing = true; s.RoutStep = Step; Routs++;
        _routersThisStep[s.Side]++;
    }

    private void CheckEnd()
    {
        bool a0 = Side[0].Any(x => x.Active), a1 = Side[1].Any(x => x.Active);
        if (!a0 || !a1) { Over = true; Winner = a0 ? 0 : a1 ? 1 : -1; return; }
        if (_quiet * K.StepS >= M.WalkAwayS && ContactStep >= 0 || Step >= K.MaxSteps)
        {
            Over = true;
            int o0 = Side[0].Count(x => !x.InLine), o1 = Side[1].Count(x => !x.InLine);
            Winner = o0 == o1 ? -1 : o0 < o1 ? 0 : 1;
            Stalemate = Winner < 0;
        }
    }

    // ------------------------------------------------------------------ the aftermath, the report and the commands

    private void Finish()
    {
        var rep = Report;
        rep.Winner = Winner; rep.Stalemate = Stalemate;
        rep.DurationS = Step * K.StepS;
        rep.ContactS = ContactStep < 0 ? -1 : ContactStep * K.StepS;
        for (int s = 0; s < 2; s++)
        {
            var t = rep.Sides[s];
            foreach (var x in Side[s])
            {
                if (x.Status is SoldierStatus.Dead or SoldierStatus.Down) { t.Casualties++; t.WorstClass[(int)x.Worst]++; }
            }
        }
        long sec = StepStartSec;
        if (Input.Aftermath)
        {
            // prisoners and the loser's downed (morale 4.4 by the winner's kind): an army takes them, bandits rob them, beasts maul them, folk let them be
            if (Winner >= 0)
            {
                int l = 1 - Winner;
                foreach (var x in Side[l])
                {
                    if (x.Status is not (SoldierStatus.Down or SoldierStatus.Surrendered)) continue;
                    if (Kind[Winner] == SideKind.Army || x.Status == SoldierStatus.Surrendered && Kind[Winner] != SideKind.Bandits) x.Status = SoldierStatus.Captured;
                    else if (Kind[Winner] is SideKind.Beasts && R(RngStreams.RoutSample, Step + 1, (ulong)(x.Index + 1), 200, 1000) < 500) { x.Book.AdvanceTo(sec); KillBook(x); }
                }
                // the leader's fate (0x4A3): a downed or yielded leader is taken by an army or a ransoming band; one who runs is sometimes run down
                foreach (var side in new[] { 0, 1 })
                {
                    if (_captain[side] < 0) continue;
                    var c = Soldiers[_captain[side]];
                    if (side == l && c.Status == SoldierStatus.Down && Kind[Winner] == SideKind.Bandits && DetRng.Range(_seed, 0, (ulong)(side + 1), RngStreams.LeaderFate, 0, 0, 1000) < 300) c.Status = SoldierStatus.Captured;
                    if (side == l && c.Status == SoldierStatus.Fled && (Side[Winner].Any(x => x.Mounted && x.InLine)) && DetRng.Range(_seed, 0, (ulong)(side + 1), RngStreams.LeaderFate, 1, 0, 1000) < 250) c.Status = SoldierStatus.Captured;
                    // his own men close round a fallen or yielded captain and carry him off (or he slips away in the press), alive, wounds and all, more often than not
                    if (side == l && c.Status is SoldierStatus.Captured or SoldierStatus.Down or SoldierStatus.Surrendered && !c.Book.Dead
                        && DetRng.Range(_seed, 0, (ulong)(side + 1), RngStreams.LeaderFate, 2, 0, 1000) < K.LeaderCarriedPm)
                        c.Status = SoldierStatus.Fled;
                }
            }
            // first aid by whoever holds the field (wounds 5, B6): 90% with a medic or 40%; the fled see to themselves at 40%
            if (Input.FirstAid)
                foreach (var x in Soldiers)
                {
                    if (x.Status == SoldierStatus.Dead || x.Book.OpenCount == 0) continue;
                    bool held = Winner >= 0 && (x.Side == Winner || x.Status == SoldierStatus.Captured);
                    int pct = held ? (Kind[Winner] is SideKind.Army or SideKind.Guards ? K.MedicPct : K.NoMedicPct) : x.Status == SoldierStatus.Fled ? K.NoMedicPct : 0;
                    if (pct == 0 || R(RngStreams.RoutSample, Step + 2, (ulong)(x.Index + 1), 300, 100) >= pct) continue;
                    x.Book.AdvanceTo(sec);
                    for (int n = 0; n < 4; n++)
                    {
                        int i = Treatment.WorstBleeder(x.Book, -1, sec);
                        if (i < 0) break;
                        var wd = x.Book.Records[i];
                        wd.Dressing = Dressing.Bandage; wd.DressFromS = sec / MedicineTables.TicksPerSimS; wd.DressedAt = sec; wd.State = WoundState.Dressed;
                        WoundBook.Retime(ref wd); x.Book.Records[i] = wd;
                    }
                }
            long after = sec + (long)K.AftermathS * TicksPerS;
            foreach (var x in Soldiers)
            {
                if (x.Status == SoldierStatus.Dead) continue;
                x.Book.AdvanceTo(after);
                if (x.Book.Dead) { x.Status = SoldierStatus.Dead; rep.Sides[x.Side].DiedAfter++; }
            }
        }
        for (int s = 0; s < 2; s++)
        {
            var t = rep.Sides[s];
            foreach (var x in Side[s])
                switch (x.Status)
                {
                    case SoldierStatus.Dead: t.Dead++; break;
                    case SoldierStatus.Down: t.Down++; break;
                    case SoldierStatus.Captured: t.Captured++; break;
                    case SoldierStatus.Fled: t.Fled++; break;
                    case SoldierStatus.Surrendered: t.Surrendered++; break;
                    default: if (x.Routing) t.Fled++; else t.Standing++; break;
                }
        }
        // notables: the leaders and every named person (a person row: id != 0)
        foreach (var x in Soldiers)
        {
            if (!x.Captain && x.Person == 0) continue;
            var fate = x.Status switch
            {
                SoldierStatus.Dead => BattleFate.Dead, SoldierStatus.Down => BattleFate.Down, SoldierStatus.Captured => BattleFate.Captured, SoldierStatus.Fled => BattleFate.Fled,
                SoldierStatus.Surrendered => BattleFate.Surrendered, _ => x.Routing ? BattleFate.Fled : x.Worst >= OutcomeClass.Light ? BattleFate.Wounded : BattleFate.Unhurt,
            };
            rep.Notables.Add(new BattleNotable(x.Person, x.Name, x.Side, fate, x.LastHitClass, x.LastHitWith, x.Captain));
        }
        foreach (var x in Soldiers) x.Book.CacheFunction = false;          // the books leave the battle as they came: no cached reading
        // commands, in person order
        foreach (var x in Soldiers.Where(x => x.Person != 0).OrderBy(x => x.Person))
        {
            if (x.Book.Records.Count > 0 || x.Book.Dead) Commands.Add(new BattleCommand(x.Person, BattleCommandKind.Wounds, x.Status, x.Book));
            if (x.Status != SoldierStatus.Active) Commands.Add(new BattleCommand(x.Person, BattleCommandKind.Status, x.Status, null, Winner >= 0 ? Team[Winner] : -1));
            if (x.Status is SoldierStatus.Dead or SoldierStatus.Captured or SoldierStatus.Down && Winner >= 0 && x.Side != Winner) Commands.Add(new BattleCommand(x.Person, BattleCommandKind.Loot, x.Status, null, Team[Winner]));
        }
        rep.Hash = Hash();
    }

    private static void KillBook(Soldier x)
    {
        // a mauled man (beasts, morale 4.4): the wound that kills is a template-free death by blood loss
        x.Book.BloodUl = Math.Min(x.Book.BloodUl, x.Book.BloodMaxUl / 2 - 1);
        x.Book.AdvanceTo(x.Book.LastSec + 20);
        x.Status = x.Book.Dead ? SoldierStatus.Dead : x.Status;
    }

    /// <summary>The quality factor (4.2): <c>beta_AB / beta_BA</c>, x100, from the tables' out-rates averaged over the sides' soldiers (each attacker against each defender).</summary>
    public int QualityX100()
    {
        long Beta(int a, int d)
        {
            long sum = 0, n = 0;
            foreach (var x in Side[a].Take(32))
                foreach (var y in Side[d].Take(32))
                {
                    var wc = x.Shooter ?? x.Melee;
                    sum += AbstractClasses.IsRanged(wc) ? Tables.Share(wc, y.Armour, 1, y.Shield, OutcomeClass.Serious) + Tables.Share(wc, y.Armour, 1, y.Shield, OutcomeClass.Grave)
                        : Tables.OutPm(wc, y.Armour, x.Skill - y.Skill, y.Shield);
                    n++;
                }
            return n == 0 ? 0 : sum * Math.Max(1, Side[a].Count) / n;
        }
        long ab = Beta(0, 1), ba = Beta(1, 0);
        return ba <= 0 ? 999 : (int)Math.Min(9999, ab * 100 / ba);
    }

    /// <summary>The <c>BattleHash</c> (6): the report's numbers, every soldier's status and book, the commands' order.</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        h = FightMath.Mix(h, (long)Input.BattleId); h = FightMath.Mix(h, Step); h = FightMath.Mix(h, Winner); h = FightMath.Mix(h, GapMm);
        foreach (var s in Soldiers)
        {
            h = FightMath.Mix(h, s.Index); h = FightMath.Mix(h, (long)s.Status + (s.Routing ? 256L : 0L) + ((long)s.Worst << 12)); h = FightMath.Mix(h, s.MoraleX100);
            h = s.Book.Hash(h);
        }
        foreach (var c in Commands) { h = FightMath.Mix(h, (long)c.Person); h = FightMath.Mix(h, (long)c.Kind << 8 | (long)c.Status); }
        return h;
    }
}

/// <summary>A deep copy of a wound book (the battle fights on copies; the person's own is replaced at the barrier, so nothing changes before it).</summary>
public static class BookCopy
{
    public static WoundBook Clone(WoundBook b)
    {
        var c = new WoundBook
        {
            BloodUl = b.BloodUl, BloodMaxUl = b.BloodMaxUl, PainX100 = b.PainX100, Toughness = b.Toughness, State = b.State, Down = b.Down, LastSec = b.LastSec,
            UnconsciousUntilSec = b.UnconsciousUntilSec, HeartAtSec = b.HeartAtSec, BreathStopSec = b.BreathStopSec, CalmSince = b.CalmSince, DeathSec = b.DeathSec,
            ConcussedUntilDay = b.ConcussedUntilDay, Death = b.Death, DeathWound = b.DeathWound, Latch = b.Latch, NextId = b.NextId, Permanent = b.Permanent, RegenPm = b.RegenPm,
            Seed = b.Seed, Key = b.Key, PlanId = b.PlanId,
        };
        c.Records.AddRange(b.Records);
        return c;
    }
}
