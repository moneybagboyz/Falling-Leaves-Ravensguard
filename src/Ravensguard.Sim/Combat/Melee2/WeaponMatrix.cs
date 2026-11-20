using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The weapon x armour matrix (Phase 4 close-out, item 2; <c>tools/Ravensguard.Calibrate -- --matrix</c>): every melee weapon's main attacks and every missile against six harnesses,
/// on the trunk and, as a called shot, through the armpit, with what the blows do once they land (the hit roll left out: a gap is hit at 0.35 of the chance). A realism check, not
/// an equaliser: weapons may be better or worse than one another. Integer counts only (the tool formats them). Blows go through the shipped resolver, the wound book and the
/// armour wear of <see cref="ArmourWear"/>, so a harness dents and bursts as the blows come in.
/// </summary>
public static class WeaponMatrix
{
    public enum Delivery : byte { Melee, Bow, Crossbow, Thrown }

    /// <summary>A row: a weapon and one of its attacks (melee) or a missile (launcher and ammunition, or a thrown weapon) at <see cref="RangeM"/>.</summary>
    public sealed record Weapon(string Name, Delivery How, string Shape, string Material, string Attack, string? Launcher = null, string? Ammo = null, int RangeM = 10);

    public sealed record Harness(string Name, WornItem[] Worn);

    public static readonly Harness None = new("none", [new("tunic", "linen")]);
    public static readonly Harness Padded = new("padded", [new("tunic", "linen"), new("gambeson", "linen")]);
    public static readonly Harness Leather = new("leather", [new("tunic", "linen"), new("cuir_bouilli", "leather"), new("leather_bracers", "leather")]);
    public static readonly Harness Mail = new("mail", [new("tunic", "linen"), new("mail_hauberk", "iron"), new("mail_coif", "iron")]);
    public static readonly Harness MailPadded = new("mail+padding", [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("mail_coif", "iron")]);
    public static readonly Harness Plate = new("plate", [.. FightCalibration.Plated.Worn]);

    public static IReadOnlyList<Harness> Harnesses { get; } = [None, Padded, Leather, Mail, MailPadded, Plate];

    /// <summary>Secondary attacks left out of the rows (the pommel, the haft, the butt and the flat are told by the main attack's row).</summary>
    private static readonly HashSet<string> Secondary = ["flat", "pommel", "haft", "butt"];
    private static readonly HashSet<string> MeleeFamilies = ["dagger", "blade", "axe", "hafted", "polearm"];

    /// <summary>Every melee weapon shape's main attacks (the lances are the mounted table's) (steel heads; the club and the staff in oak), then the missiles: longbow bodkin, broadhead and blunt, a crossbow quarrel, a thrown javelin and dagger.</summary>
    public static IReadOnlyList<Weapon> Weapons(FightContent c)
    {
        var rows = new List<Weapon>();
        foreach (var s in c.Db.All<WeaponShapeDef>().Where(s => MeleeFamilies.Contains(s.Family) && !s.Id.Contains("lance")).OrderBy(s => s.Family switch { "dagger" => 0, "blade" => 1, "axe" => 2, "hafted" => 3, _ => 4 }).ThenBy(s => s.HeadVolumeCm3 + s.HaftVolumeCm3).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            string mat = s.Id is "club" or "quarterstaff" ? "oak" : "steel";
            foreach (var a in s.Attacks)
                if (!Secondary.Contains(a.Id)) rows.Add(new Weapon($"{s.Id} {a.Id}", Delivery.Melee, s.Id, mat, a.Id));
        }
        rows.Add(new Weapon("longbow bodkin", Delivery.Bow, "", "", "", "longbow", "bodkin_arrow"));
        rows.Add(new Weapon("longbow broadhead", Delivery.Bow, "", "", "", "longbow", "broadhead_arrow"));
        rows.Add(new Weapon("longbow blunt", Delivery.Bow, "", "", "", "longbow", "blunt_arrow"));
        rows.Add(new Weapon("crossbow quarrel", Delivery.Crossbow, "", "", "", "crossbow", "quarrel"));
        rows.Add(new Weapon("thrown javelin", Delivery.Thrown, "javelin", "steel", "thrust"));
        rows.Add(new Weapon("thrown dagger", Delivery.Thrown, "dagger", "steel", "stab"));
        return rows;
    }

    /// <summary>One cell's counts: the first blow on a fresh harness (<see cref="N"/> of them), the series to the man going down (<see cref="Trials"/>), and blows into a heater shield.</summary>
    public sealed class Cell
    {
        public int N, Cut, Bruise, Serious, Trials, BodyPmSum, Downed, DownTicksSum, ShieldN, ShieldStopped;
        public int[] DownTicks = [];
        public int CutPm => N == 0 ? 0 : Cut * 1000 / N;
        public int BruisePm => N == 0 ? 0 : Bruise * 1000 / N;
        public int NonePm => N == 0 ? 0 : (N - Cut - Bruise) * 1000 / N;
        /// <summary>Serious wounds (a broken bone, a mangled muscle, a tendon, nerve, vessel, organ, the spine, a concussion, a severed part) per landed blow, x100.</summary>
        public int SeriousX100 => N == 0 ? 0 : Serious * 100 / N;
        public int DownedPm => Trials == 0 ? 0 : Downed * 1000 / Trials;
        /// <summary>Median ticks to down over the series (-1 when fewer than half went down inside the cap).</summary>
        public int MedianDownTicks
        {
            get
            {
                if (Downed * 2 < Trials || DownTicks.Length == 0) return -1;
                var a = DownTicks.Where(t => t >= 0).OrderBy(t => t).ToArray();
                return a.Length == 0 ? -1 : a[Math.Min(a.Length - 1, (Trials + 1) / 2 - 1)];
            }
        }
        /// <summary>Mean share of the blow's momentum that reached the body (the first tissue layer), permille.</summary>
        public int BodyPm => N == 0 ? 0 : BodyPmSum / N;
        public int ShieldStoppedPm => ShieldN == 0 ? 0 : ShieldStopped * 1000 / ShieldN;
    }

    /// <summary>The cycle of one blow or shot at skill 50, in fight ticks (wind-up, strike and recover; a bow's nock, draw, aim and loose; a crossbow's span).</summary>
    public static int CycleTicks(FightWorld w, FightContent c, Weapon x)
    {
        var K = c.K;
        switch (x.How)
        {
            case Delivery.Melee:
            {
                var s = c.T.Weapon(x.Shape, x.Material).Shape;
                return w.Scaled(FightWorld.TicksOf(s.WindUpS), 50, 0, K.MinWindupTicks) + Math.Max(1, s.StrikeTicks) + w.Scaled(FightWorld.TicksOf(s.RecoverS), 50, 0, K.MinRecoverTicks);
            }
            case Delivery.Bow: case Delivery.Crossbow:
            {
                var l = c.Launchers[x.Launcher!];
                return l.ReloadTicks + (l.DrawTicks > 0 ? w.Scaled(l.DrawTicks, 50, 0, 1) : 0) + K.AimFullTicks + Math.Max(1, l.ReleaseTicks);
            }
            default: return K.ThrowWindupTicks + K.ThrowRecoverTicks;
        }
    }

    /// <summary>The weapon, attack index and (for a missile) momentum of a row.</summary>
    public static (WeaponV2 W, int Attack, long MissileMilli) Arm(FightWorld w, FightContent c, Weapon x, int strength = 1250)
    {
        switch (x.How)
        {
            case Delivery.Melee:
            {
                var wv = c.T.Weapon(x.Shape, x.Material);
                return (wv, Math.Max(0, wv.AttackIndex(x.Attack)), 0);
            }
            case Delivery.Bow: case Delivery.Crossbow:
            {
                var l = c.Launchers[x.Launcher!]; var a = c.Ammo[x.Ammo!];
                int power = l.MinStrength > 0 ? (int)Math.Min(l.PowerPm, (long)l.PowerPm * strength / l.MinStrength) : l.PowerPm;
                return (c.AmmoWeapon(a), 0, w.LauncherMomentumMilli(a.MassG, l.V0Mps * 1000, power, x.RangeM * 1000L));
            }
            default:
            {
                var wv = c.T.Weapon(x.Shape, x.Material);
                int massG = (int)(wv.MassMg / 1000);
                return (wv, Math.Max(0, wv.AttackIndex(x.Attack)), w.ThrowMomentumMilli(massG, w.ThrowSpeedMmps(massG, strength)));
            }
        }
    }

    public static bool Serious(BodyPlanV2 plan, in WoundV2 wd)
    {
        var cat = WoundBook.Categorise(plan, wd);
        return cat switch
        {
            WoundCat.Bone => wd.Grade >= (byte)BoneGrade.Broken,
            WoundCat.Muscle => wd.Grade >= (byte)MuscleGrade.Mangled,
            WoundCat.Skin or WoundCat.Inset => false,
            _ => true,
        };
    }

    /// <summary>
    /// One cell: <paramref name="n"/> first blows on a fresh harness, <paramref name="trials"/> series of blows a cycle apart on one man (his harness wearing as it goes) until he is
    /// down (unconscious, dying, collapsed or unable to stand) or <paramref name="capTicks"/> have passed, and <paramref name="n"/> blows into a guarded heater shield.
    /// <paramref name="calledGap"/> aims every blow through the armpit (a blunt blow cannot slip through a gap and lands on the trunk).
    /// </summary>
    public static Cell Run(FightContent c, Weapon x, Harness h, bool calledGap, int n = 400, int trials = 100, int capTicks = 60 * 20, ulong seed = 7)
    {
        var world = new FightWorld(c, seed);
        var T = c.T; var plan = T.Plan("human"); var resolver = new ResolverV2(T);
        var (wv, attack, missileM) = Arm(world, c, x);
        int cycle = Math.Max(1, CycleTicks(world, c, x));
        int trunk = plan.IndexOf("upper_body");
        int gap = calledGap ? plan.GapIndex("armpit") : -1;
        var cell = new Cell();
        var r = new BlowResult();
        WornV2[] Fresh()
        {
            var a = new WornV2[h.Worn.Length];
            for (int i = 0; i < a.Length; i++) a[i] = WornV2.Of(T.Armour(h.Worn[i].ShapeId, h.Worn[i].MaterialId), T.Qa(h.Worn[i].Quality), fitCm3: 70000);
            return a;
        }
        StrikeV2 Strike(WornV2[] worn, ulong missing)
        {
            var s = new StrikeV2
            {
                Strength = 1250, Skill = 50, SizeCm3 = 70000, SpeciesSizeCm3 = plan.SizeCm3, MomentumPm = 1000, Weapon = wv, Attack = attack, Style = AttackStyle.Normal,
                Plan = plan, DefenderSizeCm3 = 70000, DefenderSkill = 50, Worn = worn, WornCount = worn.Length, MissingMask = missing, TargetPart = trunk, TargetGap = gap, Facing = Facing.Front,
            };
            if (x.How != Delivery.Melee) { s.Missile = true; s.ExtraMomentumMilli = missileM; }
            return s;
        }
        // first blows on a fresh harness
        for (int i = 0; i < n; i++)
        {
            var worn = Fresh();
            resolver.ResolveImpact(Strike(worn, 0), new BlowKey(seed, i, 1, (uint)i), r);
            Tally(cell, plan, r);
        }
        // series to the ground
        cell.Trials = trials; cell.DownTicks = new int[trials];
        for (int k = 0; k < trials; k++)
        {
            var worn = Fresh();
            var book = new WoundBook(seed, (ulong)(k + 1), plan.BloodMl, 10, plan.Id) { LastSec = 0 };
            ulong missing = 0;
            int down = -1;
            for (int t = 0; t <= capTicks && down < 0; t += cycle)
            {
                book.AdvanceTo(t);
                if (IsDown(book, plan)) { down = t; break; }
                resolver.ResolveImpact(Strike(worn, missing), new BlowKey(seed, 100_000 + t, (ulong)(k + 2), (uint)(k * 1000 + t / cycle)), r);
                ArmourWear.Apply(worn, r);
                if (r.WoundCount > 0)
                {
                    book.Apply(plan, r, t, t, 1);
                    missing = MissingOf(book, plan);
                    if (IsDown(book, plan)) { down = t; break; }
                }
                // bleeding between blows
                for (int u = t + 20; u < t + cycle && down < 0; u += 20) { book.AdvanceTo(u); if (IsDown(book, plan)) down = u; }
            }
            cell.DownTicks[k] = down;
            if (down >= 0) { cell.Downed++; cell.DownTicksSum += down; }
        }
        // into a guarded heater shield, from the front
        var shield = T.Shield("heater");
        for (int i = 0; i < n; i++)
        {
            var worn = Fresh();
            var s = Strike(worn, 0);
            s.Block = new BlockInput { Kind = BlockKind.Shield, Shield = shield, ShieldConditionPm = 1000, BlockerSkill = 50, ArmPart = Math.Max(0, plan.IndexOf("lower_arm_l")) };
            resolver.Resolve(s, new BlowKey(seed, 200_000 + i, 1, (uint)i), r);
            cell.ShieldN++;
            if (r.Blocked && r.WoundCount == 0) cell.ShieldStopped++;
        }
        return cell;
    }

    private static void Tally(Cell cell, BodyPlanV2 plan, BlowResult r)
    {
        cell.N++;
        bool cut = false, any = false;
        foreach (var w in r.WoundSpan)
        {
            any = true;
            if (w.Kind == WoundKind.Cut) cut = true;
            if (Serious(plan, w)) cell.Serious++;
        }
        if (cut) cell.Cut++; else if (any) cell.Bruise++;
        long body = 0;
        foreach (var st in r.StepSpan) if (!st.IsArmour && st.Tissue >= 0) { body = st.InMilli; break; }
        cell.BodyPmSum += r.InitialMilli <= 0 ? 0 : (int)(body * 1000 / r.InitialMilli);
    }

    /// <summary>Down as the fight reads it: unconscious, dying, collapsed or dead, or no leg left to stand on.</summary>
    public static bool IsDown(WoundBook b, BodyPlanV2 plan)
    {
        if (b.State is not (Consciousness.Awake or Consciousness.Dazed)) return true;
        var bs = b.Function(plan);
        return !(bs.WalkSpeedPm > 0 || bs.Balance > 0);
    }

    private static ulong MissingOf(WoundBook b, BodyPlanV2 plan)
    {
        var bs = b.Function(plan);
        ulong m = 0;
        for (int i = 0; i < bs.Parts.Length && i < 64; i++) if (bs.Parts[i].Severed || bs.Parts[i].Lost > 0 && !plan.Parts[i].IsHosted) m |= 1UL << i;
        return m;
    }
}
