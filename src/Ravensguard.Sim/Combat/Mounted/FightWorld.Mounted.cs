using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// Mounted combat inside the live fight (docs/specs/phase4/mounted-combat.md): a rider and his horse are two fighters with one position; the horse is a body with its own wounds,
/// panic instead of pain, stamina, speed and a turning circle; a charge adds the horse's momentum to the rider's blow through resolver v2 (<see cref="StrikeV2.ExtraMomentumMilli"/>);
/// a heavy blow can unhorse, a falling horse throws and may pin; braced spears and pikes strike a closing horse and make it balk; mud, slopes and woods slow horses and ice and rubble
/// trip them. No mounted-only damage rule exists: every wound is the resolver's. The hooks in <c>FightWorld.cs</c> call into this file only when <see cref="Mounts"/> is set.
/// Randomness: 0x470 unhorse, pin, shatter; 0x471 panic and refusal; 0x472 fall placement; 0x473 slips; all keyed on the FightTick.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The mounted layer; null in a fight without horses.</summary>
    public MountedLayer? Mounts { get; private set; }

    private FightTerrain? _setupTerrain;

    /// <summary>The ground and weather of the fight's setup (overview 3): the horses read it once a rider is in the fight.</summary>
    public void UseTerrain(FightTerrain t) { _setupTerrain = t; if (Mounts != null) Mounts.Terrain = t; }

    private MountedLayer EnsureMounts()
    {
        Mounts ??= new MountedLayer(MountedTuning.For(Content.Db, T)) { Terrain = _setupTerrain ?? new FightTerrain() };     // P4-F: the setup's ground
        Mounts.Grow(Fighters.Count);
        return Mounts;
    }

    // ------------------------------------------------------------------ setup

    /// <summary>Adds a rider on a horse: the rider's fighter, then the horse's (its id after every fighter added so far).</summary>
    public Fighter AddMounted(FighterKit rider, MountKit mount, bool player = false)
    {
        var r = Add(rider, player);
        Mount(r, mount);
        return r;
    }

    /// <summary>Puts an existing fighter on a horse (the hero of a road fight who was riding): the horse is a new fighter of the same side at the same place.</summary>
    public MountedFighter Mount(Fighter r, MountKit k)
    {
        var L = EnsureMounts();
        var tu = L.Tuning;
        var g = tu.Gaits(k.Breed);
        tu.Breeds.TryGetValue(k.Breed, out var sd);
        int trained = k.TrainedPm >= 0 ? k.TrainedPm : sd is { TrainedPm: > 0 } ? sd.TrainedPm : 700;
        int temper = k.Temper != int.MinValue ? k.Temper : sd?.Temper ?? 0;
        var plan = T.Plan("horse");
        var hk = new FighterKit
        {
            Name = k.Name.Length > 0 ? k.Name : sd?.Name ?? "Horse", Team = r.Team, Person = 0, Weapon = new WeaponInstance("natural_horse", "bone"), Worn = k.Barding,
            Skill = 0, Toughness = 5000, SizeCm3 = plan.SizeCm3, Courage = 100, Plan = "horse", X = r.X, Y = r.Y, FaceX = r.FX, FaceY = r.FY,
        };
        var h = Add(hk);
        h.Stance = L.HorseStance;
        int barding = 0;
        foreach (var w in k.Barding)
        {
            var a = T.Armour(w.ShapeId, w.MaterialId);
            int defDensity = a.Shape.DefaultMaterial != null && T.HasMaterial(a.Shape.DefaultMaterial) ? T.Material(a.Shape.DefaultMaterial).DensityKgM3 : a.DensityKgM3;
            barding += defDensity <= 0 ? a.Shape.MassG : (int)((long)a.Shape.MassG * a.DensityKgM3 / defDensity);
        }
        int gear = k.GearKg >= 0 ? k.GearKg : (int)((r.ArmourG + r.Weapon.MassMg / 1000 + (r.Shield?.Def.MassG ?? 0)) / 1000);
        var m = new MountedFighter
        {
            Index = L.Pairs.Count, Rider = r.Id, Horse = h.Id, Animal = k.Animal, Gaits = g, RiderKg = k.RiderKg, GearKg = gear, BardingKg = barding / 1000, TrainedPm = trained, Temper = temper,
            Riding = k.Riding, StaminaU = (k.StaminaSp >= 0 ? k.StaminaSp : g.StaminaSp) * 1000L, HX = r.FX, HY = r.FY, HorseWoundsSeen = h.Book.Records.Count,
        };
        for (int i = 0; i < 4; i++) { m.HistX[i] = r.X; m.HistY[i] = r.Y; }
        L.Pairs.Add(m);
        // pairs are kept in rider id order (the order a restored fight rebuilds them in)
        L.Pairs.Sort((x, y) => x.Rider.CompareTo(y.Rider));
        for (int i = 0; i < L.Pairs.Count; i++) { L.Pairs[i].Index = i; L.Index(L.Pairs[i], Fighters.Count); }
        if (!IsLance(r)) r.ReachMm += L.D.MountedReachMm;
        return m;
    }

    public static bool IsLance(Fighter f) => f.Weapon.Shape.Id is "lance" or "tourney_lance";

    /// <summary>
    /// The reach of <paramref name="a"/>'s weapon against <paramref name="t"/> (4.1): a rider's non-lance weapon reaches 0.4 m further down at a man on foot (or a horse), but not
    /// against another rider, whose body is as high as his own (close-out item 5; it was +0.4 m against riders too).
    /// </summary>
    public int ReachAgainst(Fighter a, Fighter t)
    {
        if (Mounts == null || IsLance(a) || Riding(a) == null || Riding(t) is not { } mt || mt.Rider != t.Id) return a.ReachMm;
        return Math.Max(500, a.ReachMm - Mounts.D.MountedReachMm);
    }

    /// <summary>A spear, pike, javelin or bill that can be planted against a horse (4.4); not a lance.</summary>
    public static bool CanBrace(Fighter f) => f.Family == "polearms" && f.Launcher == null && !IsLance(f) && f.WeaponArm;

    public bool IsHorse(Fighter f) => Mounts != null && Mounts.IsHorse(f.Id);
    public MountedFighter? PairOf(Fighter f) => Mounts?.PairOf(f.Id);
    /// <summary>The pair while this fighter sits its horse.</summary>
    public MountedFighter? Riding(Fighter f) => Mounts?.Riding(f.Id);

    // ------------------------------------------------------------------ geometry helpers

    private static readonly int[] CosHalfDeg = [16384,16383,16382,16378,16374,16368,16362,16353,16344,16333,16322,16309,16294,16279,16262,16244,16225,16204,16182,16159,16135,16110,16083,16055,16026,15996,15964,15931,15897,15862,15826,15788,15749,15709,15668,15626,15582,15537,15491,15444,15396,15346,15296,15244,15191,15137,15082,15025,14968,14909,14849,14788,14726,14663,14598,14533,14466,14399,14330,14260,14189,14117,14044,13970,13894,13818,13741,13662,13583,13502,13421,13338,13255,13170,13085,12998,12911,12822,12733,12642,12551,12458,12365,12271,12176,12080,11982,11885,11786,11686,11585];
    private static readonly int[] SinHalfDeg = [0,143,286,429,572,715,857,1000,1143,1285,1428,1570,1713,1855,1997,2139,2280,2422,2563,2704,2845,2986,3126,3266,3406,3546,3686,3825,3964,4102,4240,4378,4516,4653,4790,4927,5063,5199,5334,5469,5604,5738,5872,6005,6138,6270,6402,6533,6664,6794,6924,7053,7182,7311,7438,7565,7692,7818,7943,8068,8192,8316,8438,8561,8682,8803,8923,9043,9162,9280,9397,9514,9630,9746,9860,9974,10087,10199,10311,10422,10531,10641,10749,10856,10963,11069,11174,11278,11381,11484,11585];

    /// <summary>Turns the Q10 heading toward (dx, dy) by at most <paramref name="halfDeg"/> half degrees (a horse's turning rate, 4.1); snaps when closer.</summary>
    public static (int X, int Y) TurnHalfDeg(int hx, int hy, int dx, int dy, int halfDeg)
    {
        var (nx, ny) = FightMath.Norm(dx, dy);
        if (nx == 0 && ny == 0) return (hx, hy);
        if (hx == 0 && hy == 0) return (nx, ny);
        int k = Math.Clamp(halfDeg, 0, 90);
        if (FightMath.Dot(hx, hy, nx, ny) >= CosHalfDeg[k] >> 4) return (nx, ny);
        long cross = (long)hx * ny - (long)hy * nx;
        int c = CosHalfDeg[k], s = cross >= 0 ? SinHalfDeg[k] : -SinHalfDeg[k];
        long rx = ((long)hx * c - (long)hy * s) >> 14, ry = ((long)hx * s + (long)hy * c) >> 14;
        return FightMath.Norm(rx, ry);
    }

    /// <summary>The horse's speed toward <paramref name="t"/> from the last four ticks' positions (plus the other horse's toward us when the target rides too, capped), rounded down to 0.1 m/s.</summary>
    public int ClosingMmps(MountedFighter m, Fighter from, Fighter t)
    {
        var D = Mounts!.D;
        var (ux, uy) = FightMath.Norm(t.X - from.X, t.Y - from.Y);
        if (ux == 0 && uy == 0) { ux = m.HX; uy = m.HY; }
        long v = ((long)m.VX * ux + (long)m.VY * uy) >> 10;
        int cap = D.ChargeMaxMmps;
        if (PairOf(t) is { Mounted: true } mt && mt != m)
        {
            v += Math.Max(0, ((long)mt.VX * -ux + (long)mt.VY * -uy) >> 10);
            cap = D.ClosingMaxMmps;
        }
        if (v > 0 && SlopeAlong(from.X, from.Y, m.HX, m.HY) < 0) v += v * D.DownhillChargePm / 1000;          // a downhill charge
        return ChargeMath.RoundVc((int)Math.Clamp(v, 0, cap));
    }

    /// <summary>The rise per metre (permille) along a heading at a point.</summary>
    private int SlopeAlong(int x, int y, int hx, int hy)
    {
        return Mounts!.Terrain.SlopeAlongPm(x, y, hx, hy);                                              // [P7 CV-C] the cells' heights when the box has them, else the plane
    }

    /// <summary>What the charge would add to the rider's blow now, milli (the HUD's charge meter): the horse's speed along its heading with the weapon in hand.</summary>
    public long ChargePreviewMilli(MountedFighter m)
    {
        var r = Fighters[m.Rider];
        int pm = WeaponPmFor(r, IsLance(r) ? r.Weapon.AttackIndex("couch") : 0);
        long v = ((long)m.VX * m.HX + (long)m.VY * m.HY) >> 10;
        return ChargeMath.ExtraMilli(Mounts!.D.ChargeKMilliPerKgmps, m.TotalKg, (int)Math.Clamp(v, 0, Mounts.D.ChargeMaxMmps), pm);
    }

    private int WeaponPmFor(Fighter a, int attack)
    {
        var tu = Mounts!.Tuning;
        if (a.Launcher != null) return tu.WeaponPm("bow", 0);
        var atk = a.Weapon.Attacks[Math.Clamp(attack, 0, a.Weapon.Attacks.Length - 1)];
        if (IsLance(a) && atk.Id == "couch") return a.Weapon.Shape.Id == "tourney_lance" ? tu.WeaponPm("tourney_couched", 500) : tu.WeaponPm("lance_couched", 1000);
        return a.Family == "polearms" ? tu.WeaponPm("polearm_overhand", 600) : tu.WeaponPm("one_hand", 350);
    }

    /// <summary>The blow <paramref name="a"/> would deal <paramref name="t"/> now (the charge and the mounted points included): the HUD's tooltips and the tests.</summary>
    public StrikeV2 StrikePreview(Fighter a, Fighter t, int attack, int part) =>
        StrikeOf(a, t, Math.Clamp(attack, 0, a.Weapon.Attacks.Length - 1), part, -1, FacingOf(t, a.X, a.Y), AttackStyle.Normal, t.Act == FAct.Down);

    /// <summary>The chance (basis points) a blow from <paramref name="a"/> now would unhorse the rider <paramref name="t"/> (the HUD's "unhorse chance" tooltip).</summary>
    public int UnhorsePreviewBp(Fighter a, Fighter t, int attack)
    {
        if (Riding(t) is not { } mt) return 0;
        var s = StrikePreview(a, t, attack, Math.Max(0, t.Plan.IndexOf("upper_body")));
        var U = Mounts!.D.Unhorse;
        return ChargeMath.UnhorseBp(Resolver.InitialMomentumMilli(s), mt.Riding, U.HoldBase, U.HoldPerRidingPm, U.KBpPerUnit, U.MaxBp, a.Weapon.Attacks[s.Attack].Def.Hook ? U.HookPm : 1000);
    }

    // ------------------------------------------------------------------ hook: the brain has thought (StepWithAi)

    /// <summary>Horses take no orders from the combat brain; AI riders follow the cavalry brain; AI spear and pike bearers brace against a closing horse (4.4, 4.6).</summary>
    internal void MountedAfterThink()
    {
        var L = Mounts!;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        L.Grow(Fighters.Count);
        foreach (var m in L.Pairs) Fighters[m.Horse].Cmd.ClearAll();
        foreach (var m in L.Pairs)
        {
            var r = Fighters[m.Rider];
            if (m.Phase != MountedFighter.PhNone && r.Active) { PhaseCommands(m, r); continue; }      // [P7 CV-B] getting down or up, walking to the horse, a holder at his post
            if (m.Mounted && !r.Player && r.Active) MountedBrain.Ride(this, m, r);
        }
        MountedBrain.BraceScan(this);                               // [P7 CV-A] who wants to brace, found from the riders' side through the grid
        foreach (var f in Fighters)
        {
            if (f.Player || !f.Active || L.PairOf(f.Id) is { } p && (p.Horse == f.Id || p.Mounted)) continue;
            ref var ex = ref L.Extra[f.Id];
            ex.BraceWanted = L.BraceFace[f.Id] >= 0;
            var face = ex.BraceWanted ? Fighters[L.BraceFace[f.Id]] : null;
            if (!ex.BraceWanted) continue;
            var c = f.Cmd;
            c.Start = null; c.Feint = false; c.Throw = null; c.MoveX = c.MoveY = 0; c.Sprint = false; c.Block = false; c.DodgeX = c.DodgeY = 0;
            if (face != null) { c.FaceX = face.X - f.X; c.FaceY = face.Y - f.Y; }
        }
        L.PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    // ------------------------------------------------------------------ hook: before the commands (after vitals and morale)

    internal void MountedBeforeCommands()
    {
        var L = Mounts!; var D = L.D;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        L.Grow(Fighters.Count);
        if (L.Terrain.Thunderstorm && D.ThunderEveryTicks > 0 && Tick % D.ThunderEveryTicks == 0)
            foreach (var m in L.Pairs) if (m.Mounted) AddPanic(m, D.Panic.Thunder);
        FindFallen();                                                                                 // [P7 CV-B] the horses lying down are obstacles this tick
        foreach (var m in L.Pairs)
        {
            var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
            h.MoralePenalty = 0; h.Routing = false;
            if (h.Book.Records.Count != m.HorseWoundsSeen) WoundPanic(m, h);
            if (Tick % 10 == 0) Nerves(m, r, h);
            if (m.Phase != MountedFighter.PhNone || m.Holding) PhaseStep(m, r, h);                    // [P7 CV-B] dismounting, fetching, mounting, a holder's duty
            if (!m.Mounted) continue;
            if (r.Act == FAct.Fled)
            {
                if (h.Act is not (FAct.Fled or FAct.Dead or FAct.Down)) { h.Act = FAct.Fled; h.EndedAt = Tick; }
                continue;
            }
            if (h.Act is FAct.Dead or FAct.Down || h.WalkPm <= 0 || m.Lame) { HorseFalls(m, r, h); continue; }
            if (r.Act is FAct.Dead or FAct.Down) { Unseat(m, r, h, -1, fall: true, 0); continue; }
            if (r.Act == FAct.Surrendered) { Unseat(m, r, h, -1, fall: false, 0); continue; }
            if (r.WalkPm <= 100) { m.GripPm = 0; Unseat(m, r, h, -1, fall: true, 0); continue; }
            // Ctrl + direction is the horse's side-step (7)
            var c = r.Cmd;
            if (c.HasDodge)
            {
                if (Tick >= m.SideReady && m.SideTicks == 0 && m.StaminaU >= D.SideStepSp * 1000L)
                {
                    (m.SideX, m.SideY) = FightMath.Norm(c.DodgeX, c.DodgeY);
                    m.SideTicks = D.SideStepTicks; m.SideReady = Tick + D.SideStepTicks + D.SideStepCooldownTicks; m.StaminaU -= D.SideStepSp * 1000L;
                }
                c.DodgeX = c.DodgeY = 0;
            }
        }
        foreach (var f in Fighters) BraceState(f);
        L.PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>Panic from the horse's new wounds (4.2): a cut +80, torn muscle or a tendon +150, an artery or an organ +300, a broken leg +600, then temper and training.</summary>
    private void WoundPanic(MountedFighter m, Fighter h)
    {
        var P = Mounts!.D.Panic;
        int raw = 0;
        var recs = h.Book.Records;
        for (int i = Math.Max(0, m.HorseWoundsSeen); i < recs.Count; i++)
        {
            var w = recs[i];
            if (w.Has(WoundFlags.ArteryHit) || w.Cat == WoundCat.Organ) raw += P.Artery;
            else if (w.Cat == WoundCat.Bone && w.Grade >= (byte)BoneGrade.Broken && h.Plan.Parts[w.Part].Limb == "leg") raw += P.LegBroken;
            else if (w.Cat == WoundCat.Tendon || w.Cat == WoundCat.Severed || w.Cat == WoundCat.Muscle && w.Grade >= (byte)MuscleGrade.Torn) raw += P.Torn;
            else if (w.Kind == WoundKind.Cut || w.Cat is WoundCat.Skin or WoundCat.Muscle or WoundCat.Vessel) raw += P.Cut;
        }
        m.HorseWoundsSeen = recs.Count;
        AddPanic(m, raw);
        m.Lame = h.Book.Function(h.Plan).Lameness >= 3;
    }

    /// <summary>Adds raw panic (before temper and training).</summary>
    public void AddPanic(MountedFighter m, int raw)
    {
        var P = Mounts!.D.Panic;
        m.PanicPm = Math.Min(1000, m.PanicPm + ChargeMath.PanicOf(raw, m.Temper, m.TrainedPm, P.TemperDiv, P.TrainedAt, P.TrainedDampPm));
    }

    /// <summary>The 2 Hz nerves (4.2, <c>HorseNerves</c>): decay, then rearing (the rider may be thrown each second) and bolting.</summary>
    private void Nerves(MountedFighter m, Fighter r, Fighter h)
    {
        var D = Mounts!.D; var P = D.Panic;
        m.PanicPm = Math.Max(0, m.PanicPm - P.DecayPmPerS * 10 / 20);
        if (!m.Mounted || h.Act is FAct.Dead or FAct.Down) return;
        if (m.PanicPm >= P.BoltsAt && Tick >= m.BoltUntil)
        {
            var e = NearestEnemy(r);
            var (ax, ay) = e != null ? FightMath.Norm(r.X - e.X, r.Y - e.Y) : (-m.HX, -m.HY);
            if (ax == 0 && ay == 0) (ax, ay) = (-m.HX, -m.HY);
            int k = FightMath.DirIndex(ax, ay);
            int jit = DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.HorsePanic, 3, -4, 5);         // the bolt's direction: away, give or take 22 degrees
            (m.BoltX, m.BoltY) = FightMath.Dir((k + jit + 64) % 64);
            m.BoltUntil = Tick + P.BoltTicks;
            Emit(FightEventKind.Bolted, h.Id, r.Id, -1, m.PanicPm);
        }
        else if (m.PanicPm >= P.RearsAt && Tick >= m.BoltUntil && Tick % 20 == 0)
        {
            Emit(FightEventKind.Reared, h.Id, r.Id, -1, m.PanicPm);
            int pm = ChargeMath.RearThrowPm(m.Riding, P.RearThrowBasePct, P.RearThrowRidingDiv, P.RearThrowMinPct, P.RearThrowMaxPct);
            if (DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.HorsePanic, 1, 0, 1000) < pm) Unseat(m, r, h, -1, fall: true, 2);
        }
    }

    /// <summary>A spear or pike bearer braces when it has stood still and wants to (the AI's call, or RMB held with horses about), and stops on moving, a wound or letting go (4.4).</summary>
    private void BraceState(Fighter f)
    {
        var L = Mounts!;
        if (L.PairOf(f.Id) is { } p && (p.Horse == f.Id || p.Mounted)) return;
        ref var ex = ref L.Extra[f.Id];
        bool wanted = CanBrace(f) && (f.Player ? f.Cmd.Block && AnyEnemyRider(f) : ex.BraceWanted);
        if (f.Act == FAct.Bracing)
        {
            bool moved = f.BraceSince > ex.BracedAt;
            if (!wanted || moved || f.Book.Records.Count != ex.HitsAtBrace || !f.Active) { if (f.Act == FAct.Bracing) f.Act = FAct.Idle; }
        }
        else if (wanted && f.CanAct && f.Act is FAct.Idle or FAct.Block && Tick - f.BraceSince >= L.D.BraceStillTicks)
        {
            f.Act = FAct.Bracing; ex.BracedAt = Tick; ex.HitsAtBrace = f.Book.Records.Count;
        }
    }

    public bool AnyEnemyRider(Fighter f)
    {
        foreach (var m in Mounts!.Pairs) if (m.Mounted && Fighters[m.Rider].Team != f.Team && Fighters[m.Rider].Active) return true;
        return false;
    }

    // ------------------------------------------------------------------ hook: movement (Move)

    /// <summary>Moves a mounted rider with his horse; a horse is placed by its rider (or runs off on its own). False: an ordinary foot fighter.</summary>
    internal bool MountedMove(Fighter f)
    {
        var m = Mounts!.PairOf(f.Id);
        if (m == null) return false;
        if (f.Id == m.Horse) { f.LastMoveMm = 0; return true; }
        if (!m.Mounted) return false;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        MoveMount(m, f, Fighters[m.Horse]);
        Mounts.PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        return true;
    }

    private void MoveMount(MountedFighter m, Fighter r, Fighter h)
    {
        var L = Mounts!; var D = L.D; var g = m.Gaits; var tr = L.Terrain;
        var c = r.Cmd;
        int dx = c.MoveX, dy = c.MoveY;
        bool gallop = c.Sprint;
        bool bolting = Tick < m.BoltUntil;
        if (bolting) { dx = m.BoltX; dy = m.BoltY; gallop = true; }
        int want = dx == 0 && dy == 0 ? 0 : gallop && !m.Winded ? g.GallopMmps : m.Walk ? g.WalkMmps : g.TrotMmps;
        if (!bolting && m.PanicPm >= D.Panic.RearsAt) want = 0;                                    // rearing and bucking
        bool balking = Tick < m.RefusedUntil;
        if (balking) want = 0;
        // footing, slope, wounds and load
        var kind = tr.At(r.X, r.Y);
        var rule = L.Tuning.Footing[(int)kind];
        want = HorseCapMmps(m, r, h, want, kind, rule);
        if (Formations != null && r.FmRow >= 0 && Formations.CavalryCap(r, Tick) is > 0 and var fcap && want > fcap) want = fcap;      // [P7 CV-A] a cavalry rider keeps his slot: the formation's speed for him this tick
        // accelerate or brake
        int acc = D.AccelMmps2 / 20, brk = balking ? Math.Max(D.BrakeMmps2 / 20, m.RefuseBrake) : D.BrakeMmps2 / 20;
        m.Speed = m.Speed < want ? Math.Min(want, m.Speed + acc) : Math.Max(want, m.Speed - brk);
        // turn: a gallop is a committed line (60 degrees a second), a trot 150, a walk 270
        int rate = m.Speed > g.TrotMmps + 300 ? D.TurnGallopDegPerS : m.Speed > g.WalkMmps + 300 ? D.TurnTrotDegPerS : D.TurnWalkDegPerS;
        if (dx != 0 || dy != 0) (m.HX, m.HY) = TurnHalfDeg(m.HX, m.HY, dx, dy, rate / 10);
        // move (a side-step goes sideways on top)
        int step = m.Speed / 20;
        long mx = (long)m.HX * step >> 10, my = (long)m.HY * step >> 10;
        if (m.SideTicks > 0) { mx += (long)m.SideX * (D.SideStepMmps / 20) >> 10; my += (long)m.SideY * (D.SideStepMmps / 20) >> 10; m.SideTicks--; }
        int nx = (int)(r.X + mx), ny = (int)(r.Y + my);
        if (L.Fallen.Count > 0) FallenAhead(m, r, h, ref nx, ref ny, (int)mx, (int)my);               // [P7 CV-B] a horse lying down in the way: jump it, fall, or go round
        if (L.Tuning.Footing[(int)tr.At(nx, ny)].Forbidden) { nx = r.X; ny = r.Y; m.Speed = 0; }       // stairs and ladders: horses do not take portals
        if (Fort != null && (nx != r.X || ny != r.Y))                                                 // [P8 LW-3] a fort's boards stop a horse as they stop a man
        {
            var (sx, sy) = WallSlide(r, nx - r.X, ny - r.Y);
            if (sx == 0 && sy == 0) m.Speed = 0;
            nx = r.X + sx; ny = r.Y + sy;
        }
        int moved = (int)FightMath.Len(nx - r.X, ny - r.Y);
        r.X = nx; r.Y = ny; r.LastMoveMm = moved;
        h.X = nx; h.Y = ny; h.FX = m.HX; h.FY = m.HY; h.LastMoveMm = moved;
        // stamina: per metre by gait, regained at a walk or standing; a winded horse cannot gallop
        int cost = m.Speed > g.TrotMmps + 300 ? g.GallopCostSp : m.Speed > g.WalkMmps + 300 ? g.TrotCostSp : 0;
        m.StaminaU -= (long)moved * cost;
        if (cost == 0) m.StaminaU = Math.Min(g.StaminaSp * 1000L, m.StaminaU + (m.Speed == 0 ? g.RegenRestSp : g.RegenWalkSp) * 1000L);
        if (m.StaminaU <= 0) { m.StaminaU = 0; m.Winded = true; }
        else if (m.Winded && m.StaminaU >= D.WindedFreeSp * 1000L) m.Winded = false;
        // the closing speed is read from the position four ticks ago (6)
        int slot = Tick & 3;
        m.VX = (nx - m.HistX[slot]) * 5; m.VY = (ny - m.HistY[slot]) * 5;
        m.HistX[slot] = nx; m.HistY[slot] = ny;
        GroundHazards(m, r, h, kind, tr.At(nx, ny));                                                // [P7 CV-C] stakes, ditches, branches, fire
        // a gallop over ice or rubble can go down, once a second
        if ((Tick + h.Id) % 20 == 0)
        {
            bool fast = m.Speed > g.TrotMmps + 300 || kind == Footing.Ice && m.Speed > g.WalkMmps + 300;       // [P7 CV-C] ice holds a horse to the trot, so there a trot slips (the frost row's slip, read at last)
            int slip = TerrainFooting.SlipPm(rule, fast, tr.HeavyRain, D.RainSlipPm);
            if (TerrainFooting.Slips(Seed, Tick, h.Id, slip)) { Emit(FightEventKind.HorseFell, h.Id, r.Id, -1, 1); _slipped.Add(m.Index); }
        }
    }

    /// <summary>
    /// [P7 CV-C] What the ground does to a horse that has just moved (cavalry-and-mounted-war 4.4, 4.8): running onto a stake cell at the stake speed or more is struck as by a braced spear with the stake row's
    /// <c>M0</c> and no wielder; a gallop entering a ditch falls with <c>ditch_fall_pm</c> (stream 0x473, sub-key 1); in woods a rider moving at speed is swept by a branch (50 pm a second, once per 20 s, 0x792); a horse near a
    /// burning cell gains fire panic each second.
    /// </summary>
    private void GroundHazards(MountedFighter m, Fighter r, Fighter h, Footing from, Footing into)
    {
        var L = Mounts!; var D = L.D; var tr = L.Terrain;
        if (into == Footing.Stakes && from != Footing.Stakes && m.Speed >= D.StakeMinMmps) StakeStrike(m, h);
        else if (into == Footing.Ditch && from != Footing.Ditch && m.Speed > m.Gaits.TrotMmps + 300 && D.DitchFallPm > 0
            && DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.TerrainSlip, 1, 0, 1000) < D.DitchFallPm)
        { Emit(FightEventKind.HorseFell, h.Id, r.Id, -1, 2); _slipped.Add(m.Index); }
        if ((Tick + h.Id) % 20 != 0) return;
        if (into == Footing.Woods && m.Speed >= D.BranchMinMmps && r.Active && (L.Extra[r.Id].BranchAt == 0 || Tick - L.Extra[r.Id].BranchAt >= D.BranchEveryTicks)
            && DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.CavalryBranches, 0, 0, 1000) < D.BranchPmPerS)
        {
            L.Extra[r.Id].BranchAt = Math.Max(1, Tick);
            int head = r.Plan.IndexOf("head"), upper = r.Plan.IndexOf("upper_body");
            int part = head >= 0 && (upper < 0 || DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.CavalryBranches, 1, 0, 1000) < 500) ? head : upper;
            if (part >= 0) { r.Serial++; FallBlow(r, D.BranchMilli, part, r.Serial, h); }
        }
        if (tr.Cells is { HasFire: true } cells && cells.FireNear(h.X, h.Y, D.FireRadiusMm)) AddPanic(m, D.Panic.FirePerS);
    }

    /// <summary>A horse onto a stake cell: one thrust at its chest (or the least barded fore part) with the stake row's momentum, through the resolver; the stake has no skill.</summary>
    private void StakeStrike(MountedFighter m, Fighter h)
    {
        var L = Mounts!; var D = L.D;
        int part = BraceTargetPart(h);
        var facing = FacingOf(h, h.X + (int)((long)m.HX * 1000 >> 10), h.Y + (int)((long)m.HY * 1000 >> 10));
        var s = new StrikeV2
        {
            Strength = 0, Skill = 0, SizeCm3 = 70000, SpeciesSizeCm3 = 70000, MomentumPm = 1000, Weapon = L.Tuning.Stake, Attack = 0, Style = AttackStyle.Normal,
            Plan = h.Plan, DefenderSizeCm3 = h.Kit.SizeCm3, DefenderSkill = 0, Worn = h.Worn, WornCount = h.Worn.Length, MissingMask = h.MissingMask,
            TargetPart = part, TargetGap = -1, Facing = facing, Missile = true, ExtraMomentumMilli = D.StakeM0Milli,
            SituationPoints = D.BracePoints,
        };
        h.Serial++;
        Resolver.Resolve(s, KeyOf(h, h.Serial), _r);
        Emit(FightEventKind.BraceStrike, h.Id, h.Id, part, (int)(_r.InitialMilli / 1000), m.Speed);
        if (_r.WoundCount > 0) Land(h, h, facing, _r);
    }

    /// <summary>What the footing, the slope, the horse's wounds and the load leave of a wanted speed (the arithmetic <see cref="MoveMount"/> moves a horse by).</summary>
    private int HorseCapMmps(MountedFighter m, Fighter r, Fighter h, int want, Footing kind, FootingRule rule)
    {
        var L = Mounts!; var D = L.D; var g = m.Gaits; var tr = L.Terrain;
        want = TerrainFooting.CapMmps(rule, kind, want, g.TrotMmps, D.GallopChargeMinMmps, tr.HeavyRain, D.RainFieldPm);
        if (tr.CavSpeedPm != 1000 && want > g.WalkMmps) want = Math.Max(g.WalkMmps, (int)((long)want * tr.CavSpeedPm / 1000));      // [P7 CV-C] snow and blizzard slow the horses (the weather row's cavalry speed)
        int slope = SlopeAlong(r.X, r.Y, m.HX, m.HY);
        if (slope != 0) want = (int)((long)want * TerrainFooting.SlopePm(slope, D.UphillKPm, D.UphillMinPm, D.DownhillKPm, D.DownhillCapPm) / 1000);
        want = (int)((long)want * Math.Clamp(h.WalkPm, 0, 1000) / 1000);
        if (h.Book.Weakened) want = want * 800 / 1000;
        int load = m.RiderKg + m.GearKg + m.BardingKg;
        return (int)((long)want * RideMath.LoadFactorPm(RideMath.LoadRatioPm(load, Math.Max(1, g.CarryKg - m.BardingKg))) / 1000);
    }

    /// <summary>What this pair's horse can do at a gait (0 walk, 1 trot, 2 gallop) from where it stands now: the formation's anchor is braked by the front rank's (cavalry 4.1).</summary>
    public int GaitMmps(MountedFighter m, int gait)
    {
        var L = Mounts!; var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        var kind = L.Terrain.At(r.X, r.Y);
        int want = gait >= 2 ? m.Gaits.GallopMmps : gait == 1 ? m.Gaits.TrotMmps : m.Gaits.WalkMmps;
        return HorseCapMmps(m, r, h, want, kind, L.Tuning.Footing[(int)kind]);
    }

    private readonly List<int> _slipped = [];

    // ------------------------------------------------------------------ hook: separation (Separate)

    /// <summary>Horses do not push (the rider's body stands for the pair); a horse at speed runs through a foot fighter (the trample) unless he is braced or in a braced wall.</summary>
    internal bool MountedSkipPair(Fighter a, Fighter b)
    {
        var L = Mounts!;
        if (L.IsHorse(a.Id) || L.IsHorse(b.Id)) return true;
        var ma = L.Riding(a.Id); var mb = L.Riding(b.Id);
        if (ma != null && mb == null && a.Team != b.Team && ma.Speed >= L.D.TrampleMinMmps && !BracedAgainstHorse(b)) return true;
        if (mb != null && ma == null && a.Team != b.Team && mb.Speed >= L.D.TrampleMinMmps && !BracedAgainstHorse(a)) return true;
        return false;
    }

    private bool BracedAgainstHorse(Fighter f) => f.Act == FAct.Bracing || f.Shield != null && BracedNeighbours(f) >= 1
        || Mounts!.Extra.Length > f.Id && Mounts.Extra[f.Id].BraceStruckAt > 0 && Tick - Mounts.Extra[f.Id].BraceStruckAt < 20;

    // ------------------------------------------------------------------ hook: after movement (refusal, the braced strike, the trample)

    internal void MountedAfterMove()
    {
        var L = Mounts!; var D = L.D;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        LeadHorses();                                                                                  // [P7 CV-B] the horses a holder leads follow him
        foreach (var m in L.Pairs)
        {
            var h = Fighters[m.Horse];
            if (m.Mounted || h.Act != FAct.Fled || m.LeftAt < 0 || Tick - m.LeftAt > 60) continue;
            int step = m.Gaits.TrotMmps / 20;                                                                // a riderless horse runs off
            int rx = (int)((long)m.RunX * step >> 10), ry = (int)((long)m.RunY * step >> 10);
            if (Fort != null) (rx, ry) = WallSlide(h, rx, ry);                                                 // [P8 LW-3] a loose horse does not run through the boards either
            h.X += rx; h.Y += ry;
        }
        foreach (int i in _slipped) { var m = L.Pairs[i]; if (m.Mounted) { var h = Fighters[m.Horse]; Unseat(m, Fighters[m.Rider], h, -1, fall: true, 3); if (h.Act is not (FAct.Dead or FAct.Down)) GoDown(h); } }
        _slipped.Clear();
        foreach (var m in L.Pairs) if (m.Mounted) Refusal(m);
        foreach (var f in Fighters)
        {
            if (f.Act != FAct.Bracing) continue;
            MountedFighter? hit = null; int hitVc = 0;
            foreach (int hid in Near(3, f, f.ReachMm))                                                     // [P7 CV-A] the grid: the horses within the bearer's reach; the first pair in rider order strikes
            {
                var m = L.PairOf(hid);
                if (m == null || m.Horse != hid || !m.Mounted || hit != null && m.Index > hit.Index) continue;
                var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
                if (r.Team == f.Team || !h.Active) continue;
                if (DistMm(f, h) > f.ReachMm) continue;
                var (ux, uy) = FightMath.Norm(h.X - f.X, h.Y - f.Y);
                if (FightMath.Dot(f.FX, f.FY, ux, uy) < K.FrontArcQ10) continue;
                int vc = ClosingMmps(m, h, f);
                if (vc < D.BraceMinMmps) continue;
                hit = m; hitVc = vc;
            }
            if (hit != null) BraceStrike(f, hit, Fighters[hit.Horse], hitVc);
        }
        foreach (var m in L.Pairs) if (m.Mounted && m.Speed >= D.TrampleMinMmps) Trample(m);
        foreach (var m in L.Pairs) HorseKicks(m);                                                      // [P7 CV-B] kicks and bites
        L.PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>
    /// Refusal (4.4): a horse closing on a braced polearm in its path rolls once, 8 m out, against <c>200 + 150 x n - 2 x (trained - 700) - 2 x Riding</c> permille (n the braced polearms within
    /// 6 m facing it; doubled in a thunderstorm). A refusing horse stops 4 m out and balks for 2 s; at a gallop the rider is thrown on 20% of refusals.
    /// </summary>
    private void Refusal(MountedFighter m)
    {
        var L = Mounts!; var R = L.D.Refusal;
        var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        if (m.Speed < L.D.TrampleMinMmps || Tick < m.RefusedUntil) return;
        Fighter? near = null; long nd = long.MaxValue;
        foreach (int bid in Near(3, h, R.RollAtMm + 4000))                                                  // [P7 CV-A] the grid: only the bearers within the roll's range are looked at
        {
            var b = Fighters[bid];
            if (b.Team == r.Team || b.Act != FAct.Bracing) continue;
            long d = DistMm(h, b);
            if (d > R.RollAtMm + 4000) continue;
            var (ux, uy) = FightMath.Norm(b.X - h.X, b.Y - h.Y);
            if (FightMath.Dot(m.HX, m.HY, ux, uy) < 887) continue;                                         // in its path: within 30 degrees of the heading
            if (d < nd) { nd = d; near = b; }
        }
        // [P7 CV-C] a stake row ahead is an obstacle of its own: the nearest of the bearer and the first stake cell in the path; stake cells in the arc add to the refusal
        int sx = 0, sy = 0; long sd = long.MaxValue;
        if (L.Terrain.Cells != null && L.Terrain.Cells.FirstAlong(Footing.Stakes, h.X, h.Y, m.HX, m.HY, R.RollAtMm, out sx, out sy)) sd = FightMath.Len(sx - h.X, sy - h.Y);
        // [P8 LW-3] a fort's board or a cheval de frise ahead is the third thing a horse refuses: the standing boards in the arc count as braced polearms
        long fd = long.MaxValue; int fx = 0, fy = 0, fn = 0;
        if (Fort != null) { int fa = Fort.Ahead(r.Team, h.X, h.Y, m.HX, m.HY, R.RollAtMm, out fx, out fy, out fn); if (fa >= 0) fd = fa; }
        bool byStake = sd < nd && sd <= fd;
        bool byFort = !byStake && fd < nd;
        if (near == null && !byStake && !byFort) { m.RefusalFor = -1; return; }
        int rollFor = byFort ? -1_000_000 - ((fy >> 10) & 0x3FF) * 1024 - ((fx >> 10) & 0x3FF) : byStake ? -2 - ((sy - L.Terrain.Cells!.Oy) / FootingCells.CellMm * L.Terrain.Cells.W + (sx - L.Terrain.Cells.Ox) / FootingCells.CellMm) : near!.Id;
        long rd = byFort ? fd : byStake ? sd : nd;
        if (rd > R.RollAtMm || m.RefusalFor == rollFor) return;
        int n = byFort ? fn : near != null && !byStake ? BracedFacing(near, h, r.Team) : 0;
        int stakes = byFort ? 0 : L.Terrain.Cells != null ? L.Terrain.Cells.CountNear(Footing.Stakes, byStake ? sx : near!.X, byStake ? sy : near!.Y, R.ArcMm) : 0;
        int p = ChargeMath.RefusalPm(n, m.TrainedPm, m.Riding, R.BasePm, R.PerBracedPolearmPm, R.TrainedK, R.TrainedRef, R.RidingK, R.MaxPm, L.Terrain.Thunderstorm ? L.D.StormRefusalPm : 1000, stakes * L.D.StakeRefusalPm);
        m.RefusalFor = rollFor;
        if (DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.HorsePanic, 0, 0, 1000) >= p) return;
        m.RefusedUntil = Tick + R.BalkTicks;
        long room = Math.Max(500, rd - R.StopAtMm);
        m.RefuseBrake = (int)Math.Min(m.Speed, (long)m.Speed * m.Speed / (2 * room) / 20 + 1);
        Emit(FightEventKind.Balked, h.Id, byStake || byFort ? h.Id : near!.Id, -1, p);
        if (m.Speed > m.Gaits.TrotMmps + 300 && DetRng.Range(Seed, Tick, (ulong)(h.Id + 1), RngStreams.HorsePanic, 2, 0, 1000) < L.D.Unhorse.RefusalThrowPm)
            L.PendingUnseat.Add((r.Id, -1));
    }

    /// <summary>Braced polearms within the refusal arc of <paramref name="near"/> (it included) whose front faces the horse.</summary>
    public int BracedFacing(Fighter near, Fighter horse, int riderTeam, bool countReady = false)
    {
        int n = 0; var R = Mounts!.D.Refusal;
        foreach (int bid in Near(2, near, R.ArcMm))                                                          // [P7 CV-A] the grid (depth 2: the pick-target scan above it holds depth 3)
        {
            var b = Fighters[bid];
            if (b.Team == riderTeam || !b.Active || !CanBrace(b)) continue;
            if (!(b.Act == FAct.Bracing || countReady && b.Act is FAct.Idle or FAct.Block)) continue;
            if (b != near && DistMm(b, near) > R.ArcMm) continue;
            var (ux, uy) = FightMath.Norm(horse.X - b.X, horse.Y - b.Y);
            if (FightMath.Dot(b.FX, b.FY, ux, uy) < K.FrontArcQ10) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// The braced polearm's strike (4.4): one automatic thrust at the horse's chest (or its least armoured fore part) with <c>M0 x skill + brace_return x charge_k x kg x v_c</c>,
    /// +10 points; the horse impales itself. The bearer then recovers.
    /// </summary>
    private void BraceStrike(Fighter b, MountedFighter m, Fighter h, int vc)
    {
        var D = Mounts!.D;
        int atk = 0;
        for (int i = 0; i < b.Weapon.Attacks.Length; i++) if (b.Weapon.Attacks[i].Type == AttackType.Point) { atk = i; break; }
        int part = BraceTargetPart(h);
        var facing = FacingOf(h, b.X, b.Y);
        b.Serial++;
        var s = StrikeOf(b, h, atk, part, -1, facing, AttackStyle.Normal, false);
        s.ExtraMomentumMilli += ChargeMath.BraceReturnMilli(D.ChargeKMilliPerKgmps, m.TotalKg, vc, D.BraceReturnPm);
        s.SituationPoints += D.BracePoints;
        b.Thrown++;
        Resolver.Resolve(s, KeyOf(b, b.Serial), _r);
        OnBlow?.Invoke(b, h, _r, s);
        Emit(FightEventKind.BraceStrike, b.Id, h.Id, part, (int)(_r.InitialMilli / 1000), vc);
        if (_r.WoundCount > 0) Land(b, h, facing, _r);
        else if (!_r.Hit) Emit(FightEventKind.Missed, b.Id, h.Id, part);
        else Emit(FightEventKind.Stopped, b.Id, h.Id, _r.Part, (int)(_r.InitialMilli / 1000));
        if (b.Act == FAct.Bracing) { b.Act = FAct.Recover; b.ActTicks = RecoverTicks(b); }
        Mounts!.Extra[b.Id].BraceStruckAt = Tick;
    }

    /// <summary>The chest, unless barding covers it: then the least covered of the chest, the neck, the belly and the forelegs.</summary>
    private static int BraceTargetPart(Fighter h)
    {
        var plan = h.Plan;
        int Cover(int pi)
        {
            int c = 0;
            foreach (var w in h.Worn) if (w.Stats.Layer == ArmourLayer.Armour) c = Math.Max(c, w.Stats.CoverageByPlan[plan.Index][plan.Parts[pi].BaseIndex]);
            return c;
        }
        int chest = plan.IndexOf("chest");
        if (chest < 0) return 0;
        if (Cover(chest) < 500) return chest;
        int best = chest, bc = Cover(chest);
        foreach (var id in new[] { "neck", "belly", "fore_leg_u_l", "fore_leg_u_r" })
        {
            int pi = plan.IndexOf(id);
            if (pi >= 0 && (h.MissingMask & (1UL << pi)) == 0 && Cover(pi) < bc) { best = pi; bc = Cover(pi); }
        }
        return best;
    }

    /// <summary>
    /// The trample (4.1): a horse at speed overruns a standing or prone foot fighter in its way with a blunt blow of <c>trample_k x kg x v_c</c> (area 80; prone doubles); a dodging
    /// target is hit at 0.4 of the chance. A braced polearm or a braced shield wall is not trampled: the horse pulls up.
    /// </summary>
    private void Trample(MountedFighter m)
    {
        var L = Mounts!; var D = L.D;
        var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        foreach (int tid in Near(3, h, D.TrampleRadiusMm))                                                 // [P7 CV-A] the grid: only the men within the horse's radius
        {
            var t = Fighters[tid];
            if (t.Team == r.Team || t.Act is FAct.Dead or FAct.Fled or FAct.Surrendered || L.PairOf(t.Id) is { } p && (p.Horse == t.Id || p.Mounted)) continue;
            if (DistMm(h, t) > D.TrampleRadiusMm) continue;
            ref var ex = ref L.Extra[t.Id];
            if (Tick < ex.TrampledUntil) continue;
            if (BracedAgainstHorse(t)) { m.Speed = Math.Min(m.Speed, m.Gaits.WalkMmps); continue; }
            int vc = ClosingMmps(m, h, t);
            if (vc < D.TrampleMinMmps) continue;
            bool prone = t.Act == FAct.Down;
            long mom = ChargeMath.TrampleMilli(D.TrampleK, m.Gaits.MassKg, vc) * (prone ? 2 : 1);
            var facing = FacingOf(t, h.X, h.Y);
            int part = t.Plan.IndexOf("upper_body");
            if (part < 0) part = AreaPart(h, t.Plan, h.Serial);
            var s = new StrikeV2
            {
                Strength = 1250, Skill = m.Riding, SizeCm3 = h.Kit.SizeCm3, SpeciesSizeCm3 = h.Plan.SizeCm3, MomentumPm = 1000, Weapon = L.Tuning.Trample, Attack = 0, Style = AttackStyle.Normal,
                Plan = t.Plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = prone ? 0 : t.Skill, Worn = t.Worn, WornCount = t.Worn.Length, MissingMask = t.MissingMask,
                Prone = prone, TargetPart = part, TargetGap = -1, Facing = facing, Missile = true, ExtraMomentumMilli = mom,
            };
            if (t.Act == FAct.Dodge) s.SituationPoints += ChargeMath.ScalePoints(s.Skill - s.DefenderSkill, D.TrampleDodgePm);
            h.Serial++;
            Resolver.Resolve(s, KeyOf(h, h.Serial), _r);
            OnBlow?.Invoke(h, t, _r, s);
            ex.TrampledUntil = Tick + 20;
            Emit(FightEventKind.Trampled, h.Id, t.Id, _r.Part, (int)(mom / 1000), _r.Hit ? 1 : 0);
            if (_r.WoundCount > 0) Land(h, t, facing, _r);
            if (_r.Hit && t.Active && t.Act is not (FAct.Down or FAct.Stagger)) { t.Act = FAct.Stagger; t.ActTicks = D.TrampleStaggerTicks; }
        }
    }

    // ------------------------------------------------------------------ hook: the blow (StrikeOf, ResolveImpact)

    /// <summary>
    /// A mounted rider's blow takes the charge (4.1): <c>M_extra = charge_k x (horse + rider + gear) kg x v_c x weapon_pm</c> after the skill and style multipliers, and the accuracy points of
    /// a mounted swing; a lance on foot is unwieldy; the near-side lower body of a rider is screened by the horse against a foot attacker.
    /// </summary>
    internal void MountedStrike(Fighter a, Fighter t, ref StrikeV2 s)
    {
        var L = Mounts!; var D = L.D;
        var ma = L.Riding(a.Id);
        if (ma != null && !s.Missile)
        {
            bool couch = IsLance(a) && a.Weapon.Attacks[s.Attack].Id == "couch";
            int vc = ClosingMmps(ma, a, t);
            s.ExtraMomentumMilli += ChargeMath.ExtraMilli(D.ChargeKMilliPerKgmps, ma.TotalKg, vc, WeaponPmFor(a, s.Attack));
            s.SituationPoints += ChargeMath.AimPoints(ma.Speed, couch, ma.Riding, D.GallopChargeMinMmps, D.TrampleMinMmps, D.AimChargePoints, D.AimTrotPoints, D.AimGallopSwingPoints);
        }
        else if (ma == null && IsLance(a) && !L.IsHorse(a.Id)) s.SituationPoints -= D.LanceOnFootPoints;
        if (ma == null && L.Riding(t.Id) != null && s.TargetPart >= 0 && s.TargetPart < t.Plan.Parts.Length)
        {
            var p = t.Plan.Parts[s.TargetPart];
            if (p.BaseId is "lower_body" or "upper_leg" or "lower_leg" or "foot")
            {
                long rx = -t.FY, ry = t.FX;                                                                      // the rider's right
                bool attackerRight = rx * (a.X - t.X) + ry * (a.Y - t.Y) > 0;
                bool near = p.Side == 0 || (p.Side == 2) == attackerRight;
                if (near) s.SituationPoints += ChargeMath.ScalePoints(s.Skill - s.DefenderSkill + s.SituationPoints, D.NearSideLowerPm);
            }
        }
    }

    /// <summary>An unaimed blow at a mounted pair lands on the rider 60% and the horse 40% (4.2); an aimed part on the other's plan is dropped.</summary>
    internal void MountedRetarget(Fighter a, ref Fighter t, ref AttackReq req)
    {
        var L = Mounts!;
        // the blow found another body than the one aimed at (a man's part or gap on a horse, a fallen horse beside its rider): it lands by area
        if (req.Part >= 0 && req.Target >= 0 && req.Target < Fighters.Count && Fighters[req.Target].Plan != t.Plan) { req.Part = -1; req.Gap = -1; }
        var mt = L.PairOf(t.Id);
        if (mt == null || !mt.Mounted || L.PairOf(a.Id) == mt) return;
        if (req.Part < 0)
        {
            int roll = DetRng.Range(Seed, Tick, (ulong)(a.Id + 1), RngStreams.StanceNoise, a.Serial * 8u + 5, 0, 1000);
            var pick = roll < L.D.RiderSharePm ? Fighters[mt.Rider] : Fighters[mt.Horse];
            if (Hittable(pick)) t = pick;
        }
    }

    /// <summary>After a resolved blow: the lance shatters (60% at 150 units or more), a rider struck or blocking may be unhorsed (4.3), barding that stops a blow still frightens.</summary>
    internal void MountedOnBlow(Fighter a, Fighter t, in StrikeV2 s)
    {
        var L = Mounts!; var D = L.D;
        bool delivered = _r.Hit || _r.Blocked;
        if (!delivered) return;
        var atk = a.Weapon.Attacks[Math.Clamp(s.Attack, 0, a.Weapon.Attacks.Length - 1)];
        if (IsLance(a) && atk.Id == "couch" && _r.InitialMilli >= D.ShatterMinUnits * 1000L
            && DetRng.Range(Seed, Tick, (ulong)(a.Id + 1), RngStreams.UnhorseRoll, 3, 0, 1000) < D.ShatterPm && !L.PendingShatter.Contains(a.Id))
            L.PendingShatter.Add(a.Id);
        if (L.Riding(t.Id) is { } mt && mt.Horse != a.Id)
        {
            var U = D.Unhorse;
            int bp = ChargeMath.UnhorseBp(_r.InitialMilli, mt.Riding, U.HoldBase, U.HoldPerRidingPm, U.KBpPerUnit, U.MaxBp, atk.Def.Hook ? U.HookPm : 1000);
            if (bp > 0 && DetRng.Range(Seed, Tick, (ulong)(t.Id + 1), RngStreams.UnhorseRoll, 1u + 8u * (uint)(a.Id + 1), 0, 10_000) < bp) L.PendingUnseat.Add((t.Id, a.Id));
        }
        if (L.PairOf(t.Id) is { } mh && mh.Horse == t.Id && _r.Hit && _r.WoundCount == 0) AddPanic(mh, D.Panic.BardingStop);
    }

    /// <summary>The swing's timeline: a couched lance at a run needs only the point lowered (the horse is the wind-up); a lance on foot winds up x1.5; an AI's blow at a mounted pair picks rider or horse.</summary>
    internal void MountedSwingStart(Fighter f)
    {
        var L = Mounts!; var D = L.D;
        if (f.Req.Kind != BlowKind.Melee) return;
        var m = L.Riding(f.Id);
        if (m != null && IsLance(f) && f.Weapon.Attacks[Math.Clamp(f.Req.Attack, 0, f.Weapon.Attacks.Length - 1)].Id == "couch")
        {
            f.WindNeeded = Math.Max(1, D.CouchTicks);
            m.Couched = true;
            if (m.Speed >= D.GallopChargeMinMmps) Emit(FightEventKind.Charge, f.Id, f.Req.Target, -1, (int)(ChargePreviewMilli(m) / 1000), m.Speed);
        }
        else if (m == null && IsLance(f)) f.WindNeeded = (f.WindNeeded * D.LanceOnFootWindupPm + 999) / 1000;
        if (!f.Player && f.Req.Target >= 0 && f.Req.Target < Fighters.Count && L.PairOf(f.Req.Target) is { Mounted: true } mt && L.PairOf(f.Id) != mt)
        {
            int roll = DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), RngStreams.StanceNoise, f.Serial * 8u + 4, 0, 1000);
            int want = roll < D.RiderSharePm ? mt.Rider : mt.Horse;
            if (want != f.Req.Target && Fighters[want].Active)
            {
                var nt = Fighters[want];
                f.Req.Target = want;
                f.Req.Part = CombatBrain.PickPart(this, f, nt, Tick);
                f.Req.Gap = -1;
            }
        }
        if (f.Req.Auto) f.ImpactAt = Tick + f.WindNeeded - 1;
    }

    // ------------------------------------------------------------------ hook: after the impacts

    internal void MountedAfterImpacts()
    {
        var L = Mounts!;
        foreach (int id in L.PendingShatter) Shatter(Fighters[id]);
        L.PendingShatter.Clear();
        foreach (var (rid, att) in L.PendingUnseat)
        {
            if (L.Riding(rid) is not { } m) continue;
            Unseat(m, Fighters[m.Rider], Fighters[m.Horse], att, fall: true, att >= 0 ? 1 : 4);
        }
        L.PendingUnseat.Clear();
        foreach (var m in L.Pairs)
        {
            if (!m.Mounted) continue;
            var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
            h.X = r.X; h.Y = r.Y; h.FX = m.HX; h.FY = m.HY;
        }
    }

    /// <summary>The lance breaks and leaves its stub (a shaft to bash with).</summary>
    private void Shatter(Fighter a)
    {
        if (!IsLance(a)) return;
        bool mounted = Riding(a) != null;
        a.Weapon = T.Weapon("lance_stub", "ash");
        a.Family = FamilyOf(a.Weapon.Shape); a.ReachMm = Math.Max(500, a.Weapon.Shape.ReachMm) + (mounted ? Mounts!.D.MountedReachMm : 0);
        a.StrikeTicks = Math.Max(1, a.Weapon.Shape.StrikeTicks); a.BaseWindTicks = TicksOf(a.Weapon.Shape.WindUpS); a.BaseRecoverTicks = TicksOf(a.Weapon.Shape.RecoverS);
        Ground.Add(new GroundItem("lance", "steel", a.X, a.Y, true));
        Emit(FightEventKind.Shattered, a.Id, -1);
    }

    // ------------------------------------------------------------------ falls

    /// <summary>The horse goes down (a broken leg, a cut tendon, blood loss, death, a slip): the rider comes down with it at its speed, and a leg may be pinned under it.</summary>
    private void HorseFalls(MountedFighter m, Fighter r, Fighter h)
    {
        Emit(FightEventKind.HorseFell, h.Id, r.Id, -1, 0);
        if (h.Act is not (FAct.Dead or FAct.Down)) GoDown(h);
        Unseat(m, r, h, -1, fall: true, 5);
    }

    /// <summary>
    /// The rider leaves the saddle (4.3): with <paramref name="fall"/> he lands on a part chosen by weight (arms 30%, upper body 20%, legs 25%, head 10%, hand 15%; stream 0x472) with
    /// <c>100 x kg x (3 x 1.8 + 0.5 x v)</c> milli of blunt momentum; under a falling horse a leg is pinned 60% of the time (4 s) and crushed (40 units). A riderless horse runs off.
    /// <paramref name="why"/>: 0 down or thrown, 1 a blow, 2 rearing, 3 a slip, 4 a refusal, 5 the horse fell.
    /// </summary>
    private void Unseat(MountedFighter m, Fighter r, Fighter h, int cause, bool fall, int why)
    {
        if (!m.Mounted) return;
        var L = Mounts!; var U = L.D.Unhorse;
        int speed = m.Speed;
        m.Mounted = false; m.Couched = false; m.LeftAt = Tick; m.RunX = m.HX; m.RunY = m.HY;
        m.Phase = MountedFighter.PhNone; m.Solo = false; m.Remount = false;                           // [P7 CV-B] thrown while getting down or up
        if (!IsLance(r)) r.ReachMm = Math.Max(500, r.ReachMm - L.D.MountedReachMm);
        bool pinned = false;
        if (fall && r.Act != FAct.Dead)
        {
            FallBlow(r, ChargeMath.FallMilli(m.RiderKg, speed, U.FallHeightMm), FallPart(r), 0, h);
            bool horseDown = h.Act is FAct.Dead or FAct.Down;
            if (horseDown && DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.UnhorseRoll, 2, 0, 1000) < U.PinPm && r.Act != FAct.Dead)
            {
                pinned = true;
                m.PinnedUntil = Tick + U.PinTicks;
                int side = DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.FallPlacement, 2, 0, 2);
                int leg = r.Plan.IndexOf(side == 0 ? "lower_leg_l" : "lower_leg_r");
                if (leg >= 0) FallBlow(r, U.PinCrushMilli, leg, 1, h);
            }
        }
        m.Speed = 0;
        if (r.Act is not (FAct.Dead or FAct.Down or FAct.Fled or FAct.Surrendered)) { r.Act = FAct.Stagger; r.ActTicks = pinned ? U.PinTicks : U.DownTicks; }
        Emit(FightEventKind.Unhorsed, r.Id, cause, -1, why, pinned ? 1 : 0);
        if (h.Act is not (FAct.Dead or FAct.Down or FAct.Fled)) { h.Act = FAct.Fled; h.EndedAt = Tick; }
    }

    /// <summary>Where a falling rider lands (stream 0x472).</summary>
    private int FallPart(Fighter r)
    {
        int g = DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.FallPlacement, 0, 0, 100);
        int k = DetRng.Range(Seed, Tick, (ulong)(r.Id + 1), RngStreams.FallPlacement, 1, 0, 4);
        string side = (k & 1) == 0 ? "_l" : "_r";
        string id = g < 30 ? ((k & 2) == 0 ? "upper_arm" : "lower_arm") + side : g < 50 ? "upper_body" : g < 75 ? ((k & 2) == 0 ? "upper_leg" : "lower_leg") + side : g < 85 ? "head" : "hand" + side;
        int i = r.Plan.IndexOf(id);
        if (i < 0 || (r.MissingMask & (1UL << i)) != 0) i = r.Plan.IndexOf("upper_body");
        return Math.Max(0, i);
    }

    /// <summary>A blunt fall or crush on a part through the resolver's fall (area 40): the armour absorbs as for any blunt blow; the wounds go into the book.</summary>
    private void FallBlow(Fighter r, long momentumMilli, int part, uint serial, Fighter cause)
    {
        var s = StrikeV2.Create(T.Weapon("fist", "bone"), 0, r.Plan, part);
        s.Worn = r.Worn; s.WornCount = r.Worn.Length; s.DefenderSizeCm3 = r.Kit.SizeCm3; s.MissingMask = r.MissingMask; s.Facing = Facing.Front;
        Resolver.ResolveFall(s, momentumMilli, new BlowKey(Seed, Tick, 0x4D00_0000UL + (ulong)r.Id, serial), _r);
        Emit(FightEventKind.Fell, r.Id, cause.Id, part, (int)(momentumMilli / 1000), _r.WoundCount);
        if (_r.WoundCount == 0 || r.Act == FAct.Dead) return;
        r.Book.Apply(r.Plan, _r, GameSec, Tick, (ulong)(cause.Id + 1));
        RefreshBody(r);
        r.MoralePenalty += K.HitTakenPenalty;
        if (r.Book.Dead) Kill(r);
        else if (!r.Book.Conscious && r.Act != FAct.Down) GoDown(r);
    }

    /// <summary>The cavalry brain's retreat (4.6): the rider rides off the field with his horse.</summary>
    public void RideOff(MountedFighter m)
    {
        var r = Fighters[m.Rider]; var h = Fighters[m.Horse];
        if (!r.Active) return;
        r.Act = FAct.Fled; r.EndedAt = Tick;
        Emit(FightEventKind.Fled, r.Id, -1);
        Announce(r);
        if (h.Active) { h.Act = FAct.Fled; h.EndedAt = Tick; }
    }

    // ------------------------------------------------------------------ hash and save

    /// <summary>The <c>MountedHash</c> (6): every pair's integers and the layer's per-fighter state, mixed into the fight hash of a mounted fight.</summary>
    public ulong MountedHash(ulong h = 14695981039346656037UL)
    {
        var L = Mounts;
        if (L == null) return h;
        foreach (var m in L.Pairs) h = m.Hash(h);
        for (int i = 0; i < Fighters.Count && i < L.Extra.Length; i++)
        {
            var e = L.Extra[i];
            h = FightMath.Mix(h, (e.BraceWanted ? 1L : 0) | (long)e.BracedAt << 8 | (long)e.HitsAtBrace << 40);
            h = FightMath.Mix(h, (long)(uint)e.TrampledUntil | (long)e.BraceStruckAt << 32);
        }
        var tr = L.Terrain;
        h = MixGround(h, tr);
        h = FightMath.Mix(h, (long)tr.Base | (long)tr.SlopeXPm << 8 | (long)tr.SlopeYPm << 32 | (tr.HeavyRain ? 1L << 56 : 0) | (tr.Thunderstorm ? 1L << 57 : 0));
        return h;
    }

    private const long TailMagic = 0x4D4E5431;      // "MNT1"
    private const long CvbMagic = 0x43564231;       // "CVB1"

    /// <summary>
    /// The mounted layer's part of a fighter's saved state (the "fight" section's fighter row ends with it; a foot fight writes none): the layer's per-fighter state, the ground and
    /// weather on fighter 0, and the pair on its rider's row.
    /// </summary>
    /// <summary>[P7 CV-C] The cells, the wet ground, the weather row's cavalry speed and sight, and the branches' cooldowns join the hash only when set, so a fight without them hashes as before.</summary>
    private ulong MixGround(ulong h, FightTerrain tr)
    {
        if (tr.Cells != null) h = FightMath.Mix(h, (long)tr.Cells.Hash());
        if (tr.Wet is { } g) h = FightMath.Mix(h, 0x5745_5400L | (long)(uint)g.WetHours << 40 ^ (long)(g.TempTenthsC + 5000) << 20 ^ (long)g.SnowCm << 8 ^ (g.Wet ? 1 : 0));
        if (tr.CavSpeedPm != 1000 || tr.VisionMm != 0) h = FightMath.Mix(h, 0x5649_5300L | (long)tr.CavSpeedPm << 32 ^ (long)tr.VisionMm << 8);
        var L = Mounts!;
        for (int i = 0; i < L.Extra.Length; i++) if (L.Extra[i].BranchAt != 0) h = FightMath.Mix(h, (long)i << 32 | (uint)L.Extra[i].BranchAt);
        return h;
    }

    /// <summary>[P7 CV-C] The terrain's weather flags packed into the two saved flag slots (bit 0 of each is the old 0 or 1; the rest is new and zero in an older save).</summary>
    private static (long Rain, long Storm) PackWeather(FightTerrain tr)
    {
        long rain = tr.HeavyRain ? 1 : 0, storm = tr.Thunderstorm ? 1 : 0;
        if (tr.Wet is { } g) rain |= 2 | (g.Wet ? 4L : 0) | (long)Math.Clamp(g.WetHours, 0, 1023) << 8 | (long)Math.Clamp(g.SnowCm, 0, 1023) << 18 | (long)Math.Clamp(g.TempTenthsC + 2000, 0, 32767) << 28;
        storm |= (long)Math.Clamp(tr.VisionMm / 1000, 0, 65535) << 8 | (long)(tr.CavSpeedPm != 1000 ? Math.Clamp(tr.CavSpeedPm, 0, 4095) : 0) << 24;
        return (rain, storm);
    }

    private static void UnpackWeather(FightTerrain tr, long rain, long storm)
    {
        tr.HeavyRain = (rain & 1) != 0; tr.Thunderstorm = (storm & 1) != 0;
        if ((rain & 2) != 0) tr.Wet = new FootingCells.Ground((int)(rain >> 8 & 1023), (int)(rain >> 28 & 32767) - 2000, (int)(rain >> 18 & 1023), (rain & 4) != 0);
        tr.VisionMm = (int)(storm >> 8 & 65535) * 1000;
        int cav = (int)(storm >> 24 & 4095); tr.CavSpeedPm = cav == 0 ? 1000 : cav;
    }

    public long[] MountedTail(Fighter f)
    {
        var L = Mounts;
        if (L == null) return [];
        L.Grow(Fighters.Count);
        var e = L.Extra[f.Id];
        var t = new List<long> { TailMagic, e.BraceWanted ? 1 : 0, e.BracedAt, e.HitsAtBrace, e.TrampledUntil, e.BraceStruckAt };
        if (f.Id == 0) { var tr = L.Terrain; var (rain, storm) = PackWeather(tr); t.AddRange([1, (long)tr.Base, tr.SlopeXPm, tr.SlopeYPm, rain, storm]); }
        else t.Add(0);
        var pair = L.Pairs.FirstOrDefault(m => m.Rider == f.Id);
        if (pair != null) { t.Add(1); t.AddRange(pair.Pack()); } else t.Add(0);
        if (e.BranchAt != 0) t.Add(e.BranchAt);                                    // [P7 CV-C] additive: only a rider a branch has struck carries it
        if (pair != null && pair.HasCvb) { t.Add(CvbMagic); t.AddRange(pair.CvbPack()); }      // [P7 CV-B] additive: only a pair that has got down, led a horse, kicked or met a fallen horse carries it
        return [.. t];
    }

    /// <summary>Rebuilds the mounted layer from the saved fighter rows (<see cref="MountedTail"/>), after every fighter is back; nothing for a foot fight.</summary>
    public void RestoreMounted(IReadOnlyList<long[]> states)
    {
        var pairs = new List<MountedFighter>();
        MountedExtra[]? extra = null; FightTerrain? terrain = null;
        for (int i = 0; i < states.Count && i < Fighters.Count; i++)
        {
            var s = states[i];
            int n = Save.FightRecord.Pack(Fighters[i]).Length;
            if (s.Length <= n || s[n] != TailMagic) continue;
            extra ??= new MountedExtra[Fighters.Count];
            int k = n + 1;
            extra[i] = new MountedExtra { BraceWanted = s[k++] != 0, BracedAt = (int)s[k++], HitsAtBrace = (int)s[k++], TrampledUntil = (int)s[k++], BraceStruckAt = (int)s[k++] };
            if (s[k++] != 0) { terrain = new FightTerrain { Base = (Footing)s[k++], SlopeXPm = (int)s[k++], SlopeYPm = (int)s[k++] }; long rain = s[k++], storm = s[k++]; UnpackWeather(terrain, rain, storm); }
            MountedFighter? pair = null;
            if (s[k++] != 0) { pair = MountedFighter.Unpack(s.AsSpan(k, MountedFighter.PackLength), 0); pairs.Add(pair); k += MountedFighter.PackLength; }
            if (s.Length > k && s[k] != CvbMagic) extra[i].BranchAt = (int)s[k++];
            if (pair != null && s.Length >= k + 1 + MountedFighter.CvbLength && s[k] == CvbMagic) pair.CvbUnpack(s.AsSpan(k + 1, MountedFighter.CvbLength));
        }
        if (extra == null) return;
        var L = EnsureMounts();
        Array.Copy(extra, L.Extra, Math.Min(extra.Length, L.Extra.Length));
        if (terrain != null) L.Terrain = terrain;
        foreach (var m in pairs.OrderBy(p => p.Rider))
        {
            m.Index = L.Pairs.Count;
            L.Pairs.Add(m);
            L.Index(m, Fighters.Count);
            Fighters[m.Horse].Stance = L.HorseStance;
            if (m.Mounted && !IsLance(Fighters[m.Rider])) Fighters[m.Rider].ReachMm += L.D.MountedReachMm;
        }
    }
}
