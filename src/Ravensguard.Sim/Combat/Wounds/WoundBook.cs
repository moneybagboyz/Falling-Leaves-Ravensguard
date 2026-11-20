using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using static Ravensguard.Sim.Combat.Wounds.MedicineTables;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>
/// One person's wounds, blood, pain and consciousness (wounds 3.1). Self-contained: it holds no plan, so the live tier (20 Hz) and the abstract tier (hourly)
/// call the same <see cref="AdvanceTo"/> and cannot disagree. Time is game seconds; blood loss is exact per sim-second and spread over its 20 ticks by a
/// floor difference, so the sum over any whole number of sim-seconds is the same whatever the step size (test WB-11, WB-16).
/// </summary>
public sealed partial class WoundBook
{
    public readonly List<WoundRecord> Records = [];
    public long BloodUl, BloodMaxUl;
    /// <summary>Pain pool x100.</summary>
    public int PainX100;
    public int Toughness = 10;
    public Consciousness State;
    /// <summary>What holds the person down (valid while not awake).</summary>
    public DownReason Down;
    /// <summary>The last game second processed.</summary>
    public long LastSec;
    /// <summary>
    /// The book's own day cursor: the last game day whose daily step it has had, or the day it joined the ledger (its birth day), so a book never gets the daily steps of days
    /// before it existed. -1 until a ledger counts it (a book put in by hand then starts from the ledger's <see cref="WoundLedger.LastDay"/>, as before).
    /// </summary>
    public long DayDone = -1;
    public long UnconsciousUntilSec, HeartAtSec = -1, BreathStopSec = -1, CalmSince = -1, DeathSec = -1;
    public int ConcussedUntilDay;
    public DeathKind Death;
    public ulong DeathWound;
    public byte Latch;
    public uint NextId = 1;
    public PermanentInfo Permanent;
    /// <summary>Blood regeneration, permille of 25 ml per game hour (fed 1000, hungry 400, starving 0).</summary>
    public int RegenPm = 1000;
    public ulong Seed, Key;
    /// <summary>Difficulty (wound lethality): percent of the calibrated bleed rate at a wound's creation and of the infection chance per day. Set by the ledger; not saved (the run's record holds it).</summary>
    public int LethalityPct = 100;
    public string PlanId = "human";
    private List<BookEvent>? _events;

    private const byte LBloodCol = 1, LBloodUnc = 2, LPain = 4;

    public WoundBook(ulong seed, ulong key, int bloodMaxMl, int toughness = 10, string planId = "human")
    {
        Seed = seed; Key = key; BloodMaxUl = bloodMaxMl * 1000L; BloodUl = BloodMaxUl; Toughness = toughness; PlanId = planId;
    }

    /// <summary>For the save reader.</summary>
    public WoundBook() { }

    public bool Dead => State == Consciousness.Dead;
    public long KnockoutPainX100 => (60L + 4L * Toughness) * 100;
    public int BloodMl => (int)(BloodUl / 1000);
    /// <summary>Blood lost, permille of the volume.</summary>
    public int LossPm => BloodMaxUl <= 0 ? 0 : (int)((BloodMaxUl - BloodUl) * 1000 / BloodMaxUl);
    /// <summary>A tourniquet is on a limb (its clock runs, medicine 4.3).</summary>
    public bool HasTourniquet { get { foreach (var w in Records) if (w.Dressing == Dressing.Tourniquet && !w.Is(Aid.Resolved)) return true; return false; } }
    public bool Weakened => LossPm >= WeakPm;
    public bool Conscious => State is Consciousness.Awake or Consciousness.Dazed or Consciousness.Collapsed;
    public int OpenCount { get { int n = 0; foreach (var w in Records) if (w.Open) n++; return n; } }

    /// <summary>Anything to simulate each tick: a bleed, pain, a timer, or blood below the maximum.</summary>
    public bool Active => !Dead && (BloodUl < BloodMaxUl || HasTourniquet || PainX100 > 0 || HeartAtSec >= 0 || BreathStopSec >= 0 || State is not (Consciousness.Awake or Consciousness.Dazed) || UnconsciousUntilSec > LastSec || CurrentRateMilli() > 0);
    /// <summary>Wounds that keep the book in the ledger: anything unhealed, or any permanent effect.</summary>
    public bool Empty => Records.Count == 0 && !Dead && BloodUl >= BloodMaxUl && PainX100 == 0 && Permanent.Equals(default(PermanentInfo)) && ConcussedUntilDay == 0 && BreathStopSec < 0;      // P6-B5: a book whose breath is stopped by smoke is not empty

    public IReadOnlyList<BookEvent> Events => _events ?? (IReadOnlyList<BookEvent>)Array.Empty<BookEvent>();
    public List<BookEvent> DrainEvents() { var e = _events ?? []; _events = null; return e; }
    private void Note(long sec, BookEventKind k, int part = -1, string note = "") => (_events ??= []).Add(new BookEvent(sec, k, part, note));

    // ---------------------------------------------------------------- bleeding

    /// <summary>The loss of one wound in sim-second <paramref name="s"/>, ml x 1000 (rate x clot x dressing; a pure function of the record and s).</summary>
    public static long LossMilli(in WoundRecord w, long s)
    {
        if (w.BleedX10 <= 0 || w.State == WoundState.Scar) return 0;
        long m = w.BleedX10 * 100L;
        if (w.Internal) return w.Is(Aid.Repaired) ? 0 : m;
        if (w.Clots) m = (long)(((UInt128)(ulong)m * ClotQ32(s - w.Born / TicksPerSimS)) >> 32);
        if (w.Dressing is Dressing.Pressure or Dressing.Bandage or Dressing.Tourniquet && s >= w.DressFromS)
        {
            m = m * DressPm(w.Dressing) / 1000;
            if (w.Dressing != Dressing.Tourniquet) m = (long)(((UInt128)(ulong)m * ClotQ32(2 * (s - w.DressFromS))) >> 32);   // a dressed wound clots, half-life 45 s
        }
        return m;
    }

    /// <summary>Total rate now, milli-ml per sim-second.</summary>
    public long CurrentRateMilli() { return RateMilliAt(LastSec / TicksPerSimS); }
    public long RateMilliAt(long s) { long r = 0; foreach (ref readonly var w in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Records)) r += LossMilli(w, s); return r; }

    /// <summary>ml per sim-second as a whole number (display and tests).</summary>
    public int RateMlPerS => (int)(CurrentRateMilli() / 1000);

    /// <summary>Seconds until the person collapses at the current rate (sim-seconds, rounded; -1 if never at this rate), for the "time to collapse" display.</summary>
    public int SecondsToCollapse()
    {
        long r = CurrentRateMilli(); if (r <= 0) return -1;
        long left = BloodUl - (BloodMaxUl - BloodMaxUl * CollapsePm / 1000);
        if (left <= 0) return 0;
        return (int)((left + r - 1) / r);
    }

    // ---------------------------------------------------------------- advancing time

    /// <summary>Processes every game second up to <paramref name="toSec"/> (never backwards). Both tiers call this.</summary>
    public void AdvanceTo(long toSec)
    {
        if (toSec <= LastSec) return;
        if (Dead || !Active) { LastSec = toSec; return; }
        while (LastSec < toSec && !Dead)
        {
            long g = LastSec;
            long s = g / TicksPerSimS; int j = (int)(g % TicksPerSimS);
            long rate = RateMilliAt(s);
            if (j == 0 && toSec - g >= TicksPerSimS && QuietSecond(s, rate))
            {
                BloodUl -= rate; LastSec += TicksPerSimS;
                EndSecond(s);
                if (!Active) { LastSec = toSec; break; }
                continue;
            }
            TicksOfSecond(g, s, j, toSec);
        }
        if (Dead && LastSec < toSec) LastSec = toSec;
    }

    /// <summary>A sim-second in which no threshold or timer can fire: awake, nothing pending, and the blood ends well above collapse.</summary>
    private bool QuietSecond(long s, long rate)
    {
        if (State is not (Consciousness.Awake or Consciousness.Dazed) || HeartAtSec >= 0 || BreathStopSec >= 0 || CalmSince >= 0 || UnconsciousUntilSec > LastSec || Latch != 0) return false;
        if (PainX100 + 100 > KnockoutPainX100 * 9 / 10) return false;
        long after = BloodUl - rate;
        return after > BloodMaxUl - BloodMaxUl * (CollapsePm - 20) / 1000;
    }

    /// <summary>
    /// The game seconds of one sim-second from tick <paramref name="j"/> on, up to <paramref name="toSec"/>: exactly what <see cref="Tick"/> does tick by tick, with each wound's loss of the
    /// sim-second read once (the loss depends on the wound and the sim-second only, and nothing in a tick changes the records), so a downed man's long bleed costs a sum, not twenty walks of the book.
    /// </summary>
    private void TicksOfSecond(long g, long s, int j, long toSec)
    {
        int n = Records.Count;
        if (n > 64) { Tick(g, s, j); return; }
        Span<long> l = stackalloc long[n];
        var recs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Records);
        for (int k = 0; k < n; k++) l[k] = LossMilli(recs[k], s);
        bool whole = j == 0 && toSec - g >= TicksPerSimS;
        for (; j < TicksPerSimS && g < toSec; j++, g++)
        {
            long lost = 0;
            for (int k = 0; k < n; k++) { long x = l[k]; if (x > 0) lost += x * (j + 1) / TicksPerSimS - x * j / TicksPerSimS; }
            BloodUl -= lost;
            LastSec = g + 1;
            if (whole && j == 1)
            {
                // the second tick of a whole sim-second: when it changes nothing that the first did not, and nothing the rest of the second reads can change (the blood stays in the same
                // band of every threshold Evaluate tests, the timers do not come due), the other ticks would each read and write the same: their blood is summed and they are passed over
                var st = State; byte la = Latch; var dn = Down; long cs = CalmSince; int ev = _events?.Count ?? 0;
                Evaluate(g + 1);
                if (Dead) return;
                if (st == State && la == Latch && dn == Down && cs == CalmSince && CalmSince < 0 && (_events?.Count ?? 0) == ev && BreathStopSec < 0
                    && (HeartAtSec < 0 || HeartAtSec <= g + 1) && (UnconsciousUntilSec <= g + 1 || UnconsciousUntilSec > g + TicksPerSimS - 1))
                {
                    long rest = 0;
                    for (int k = 0; k < n; k++) { long x = l[k]; if (x > 0) rest += x - x * 2 / TicksPerSimS; }
                    if (Band(LossPm) == Band(LossPmAt(BloodUl - rest)))
                    {
                        BloodUl -= rest;
                        LastSec = g - 1 + TicksPerSimS;
                        EndSecond(s);
                        return;
                    }
                }
                continue;
            }
            Evaluate(g + 1);
            if (Dead) return;
            if (j == TicksPerSimS - 1) EndSecond(s);
        }
    }

    private int LossPmAt(long bloodUl) => BloodMaxUl <= 0 ? 0 : (int)((BloodMaxUl - bloodUl) * 1000 / BloodMaxUl);

    /// <summary>Which side of each blood threshold <see cref="Evaluate"/> tests a loss lies on (a count: the loss only grows within a sim-second).</summary>
    private static int Band(int lossPm) =>
        (lossPm >= DeadPm ? 1 : 0) + (lossPm >= UnconsciousPm ? 1 : 0) + (lossPm >= UnconsciousPm - 20 ? 1 : 0) + (lossPm >= CollapsePm ? 1 : 0) + (lossPm >= CollapsePm - 20 ? 1 : 0);

    private void Tick(long g, long s, int j)
    {
        long lost = 0;
        foreach (var w in Records)
        {
            long l = LossMilli(w, s);
            if (l > 0) lost += l * (j + 1) / TicksPerSimS - l * j / TicksPerSimS;
        }
        BloodUl -= lost;
        LastSec = g + 1;
        Evaluate(g + 1);
        if (Dead) return;
        if (j == TicksPerSimS - 1) EndSecond(s);
    }

    /// <summary>The once-a-sim-second work: pain decays and the open wounds add to it, the hourly regeneration, the heart and breath timers.</summary>
    private void EndSecond(long s)
    {
        int ongoing = 0;
        foreach (var w in Records)
        {
            if (w.Is(Aid.Numb) || w.State is WoundState.Scar or WoundState.Healing || w.Is(Aid.Resolved)) continue;
            int p = w.PainC / 100;
            if (w.Dressing != Dressing.None && w.Dressing != Dressing.Splint) p /= 2;
            if (w.Is(Aid.Splinted) && w.Cat == WoundCat.Bone) p = p * 3 / 10;
            if (w.InfectionLevel > 0) p = p * (100 + 20 * w.InfectionLevel) / 100;
            ongoing += p;
        }
        PainX100 = (int)((long)PainX100 * PainDecayPct / 100) + ongoing;
        StepTourniquets(s);
        if ((s + 1) % SimSPerHour == 0 && BloodUl < BloodMaxUl && RegenPm > 0)
            BloodUl = Math.Min(BloodMaxUl, BloodUl + RegenMlPerHour * 1000L * RegenPm / 1000);
        Evaluate(LastSec);
    }

    /// <summary>Decides consciousness at <paramref name="sec"/> from blood, pain, the heart, a concussion and the breath.</summary>
    private void Evaluate(long sec)
    {
        if (Dead) return;
        int loss = LossPm;
        if (loss >= DeadPm) { Die(sec, DominantInternal() ? DeathKind.Organ : DeathKind.BloodLoss, 0); return; }
        if (BreathStopSec >= 0 && sec - BreathStopSec >= BreathDeadS * TicksPerSimS) { Die(sec, DeathKind.Breath, 0); return; }
        if (loss >= UnconsciousPm) Latch |= LBloodUnc; else if (loss < UnconsciousPm - 20) Latch &= unchecked((byte)~LBloodUnc);
        if (loss >= CollapsePm) Latch |= LBloodCol; else if (loss < CollapsePm - 20) Latch &= unchecked((byte)~LBloodCol);
        if (PainX100 > KnockoutPainX100) Latch |= LPain; else if (PainX100 < KnockoutPainX100 * 7 / 10) Latch &= unchecked((byte)~LPain);
        var why = DownReason.None;
        if ((Latch & LBloodUnc) != 0) why |= DownReason.Blood;
        if ((Latch & LPain) != 0) why |= DownReason.Pain;
        if (HeartAtSec >= 0 && sec >= HeartAtSec) why |= DownReason.Heart;
        if (sec < UnconsciousUntilSec) why |= DownReason.Concussion;
        if (BreathStopSec >= 0 && sec - BreathStopSec >= BreathUnconsciousS * TicksPerSimS) why |= DownReason.Breath;
        bool unc = why != DownReason.None;
        bool was = State is Consciousness.Unconscious or Consciousness.Dying;
        if (!unc && was)
        {
            if (CalmSince < 0) CalmSince = sec + (long)DetRng.Range(Seed, sec / TicksPerSimS, Key, RngStreams.WakeJitter, 0, 0, 5) * TicksPerSimS;
            if (sec - CalmSince < WakeCalmS * TicksPerSimS) { unc = true; why = Down; }
        }
        else if (unc) CalmSince = -1;
        else CalmSince = -1;
        Consciousness next;
        if (unc)
        {
            bool dying = (why & (DownReason.Blood | DownReason.Heart | DownReason.Breath)) != 0;
            next = dying ? Consciousness.Dying : Consciousness.Unconscious;
        }
        else if ((Latch & LBloodCol) != 0) next = Consciousness.Collapsed;
        else if (sec / GameSecPerDay < ConcussedUntilDay) next = Consciousness.Dazed;
        else next = Consciousness.Awake;
        Down = why;
        if (next == State) return;
        var prev = State; State = next;
        if (next is Consciousness.Unconscious or Consciousness.Dying && prev is not (Consciousness.Unconscious or Consciousness.Dying)) Note(sec, BookEventKind.Unconscious);
        else if (next == Consciousness.Collapsed && prev == Consciousness.Awake) Note(sec, BookEventKind.Collapsed);
        else if (next == Consciousness.Awake && prev is Consciousness.Unconscious or Consciousness.Dying or Consciousness.Collapsed) Note(sec, BookEventKind.Woke);
        else if (next == Consciousness.Dazed && prev is Consciousness.Unconscious or Consciousness.Dying) Note(sec, BookEventKind.Woke);
    }

    private bool DominantInternal()
    {
        long s = LastSec / TicksPerSimS, inner = 0, outer = 0;
        foreach (var w in Records) { long l = LossMilli(w, s); if (w.Internal) inner += l; else outer += l; }
        return inner > outer;
    }

    /// <summary>P6-C1 (crime and justice 4.6): the court's rope. The body is brought to the second, the breath stops and the person dies of it (<see cref="DeathKind.Breath"/>); the caller's chronicle entry says it was the court.</summary>
    public void Hang(long sec)
    {
        AdvanceTo(sec);
        if (Dead) return;
        if (BreathStopSec < 0) BreathStopSec = sec;
        Die(sec, DeathKind.Breath, 0);
    }

    private void Die(long sec, DeathKind kind, ulong wound)
    {
        if (Dead) return;
        State = Consciousness.Dead; Death = kind; DeathSec = sec; DeathWound = wound; Down = DownReason.None;
        Note(sec, BookEventKind.Died, -1, kind.ToString());
    }

    // ---------------------------------------------------------------- the blow

    /// <summary>The momentum that reached the skull in a blow, milli-units (the deepest layer the trace shows on the head).</summary>
    public static long SkullMomentum(BlowResult r)
    {
        long m = 0;
        foreach (var st in r.StepSpan) if (!st.IsArmour && st.InMilli > 0) m = st.InMilli;
        return m;
    }

    /// <summary>Adds the wounds of one resolved blow (wounds 4.1). Call in the tick the blow lands.</summary>
    public void Apply(BodyPlanV2 plan, BlowResult r, long gameSec, long fightTick, ulong cause = 0, bool dirty = false)
        => Apply(plan, r.WoundSpan, gameSec, fightTick, cause, SkullMomentum(r), dirty);

    public void Apply(BodyPlanV2 plan, ReadOnlySpan<WoundV2> wounds, long gameSec, long fightTick, ulong cause, long skullMilli, bool dirty)
    {
        AdvanceTo(gameSec);
        if (Dead) return;
        var bs = Function(plan);
        int concGrade = -1;
        foreach (var w in wounds)
        {
            var rec = Make(plan, bs, w, gameSec, cause, dirty);
            Add(rec);
            PainX100 += rec.PainC;
            if (w.Organ >= 0)
            {
                string oid = plan.Parts[w.Part].Organs[w.Organ].Id;
                if (w.Has(WoundFlags.Vital) && oid == "brain") { Die(gameSec, DeathKind.Brain, rec.Id); return; }
                if (w.Has(WoundFlags.Vital) && HeartAtSec < 0) HeartAtSec = gameSec + HeartCollapseTicks;
            }
            if (w.Has(WoundFlags.Concussion)) concGrade = Math.Max(concGrade, rec.Cat == WoundCat.Bone ? rec.Grade : 1);
        }
        if (concGrade >= 0) Concuss(gameSec, fightTick, skullMilli, concGrade);
        var after = Function(plan);
        if ((after.BreathStopped || after.Breath <= 0) && BreathStopSec < 0) BreathStopSec = gameSec;
        Evaluate(gameSec);
    }

    private void Concuss(long sec, long fightTick, long skullMilli, int grade)
    {
        long baseS = Math.Clamp(10 + Math.Max(0, skullMilli - 40_000) * 2 / 5000, 10, 600);
        int jit = DetRng.Range(Seed, fightTick, Key, RngStreams.ConcussionJitter, 0, 900, 1101);
        long until = sec + baseS * jit / 1000 * TicksPerSimS;
        UnconsciousUntilSec = Math.Max(UnconsciousUntilSec, until);
        int day = (int)(sec / GameSecPerDay);
        ConcussedUntilDay = Math.Max(ConcussedUntilDay, day + 1 + grade);
        Permanent.Concussions = (byte)Math.Min(255, Permanent.Concussions + 1);
        int c0 = Permanent.Conc0, c1 = Permanent.Conc1;
        Permanent.Conc2 = c1; Permanent.Conc1 = c0; Permanent.Conc0 = day + 1;
        if (Permanent.Conc2 > 0 && day + 1 - Permanent.Conc2 <= 30 && Permanent.WitsLoss < 255 && Permanent.Concussions == 3)
        { Permanent.WitsLoss++; Note(sec, BookEventKind.PermanentLoss, -1, "wits"); }
        Note(sec, BookEventKind.Dazed);
    }

    private int Scale(int bleedX10) => LethalityPct == 100 ? bleedX10 : bleedX10 <= 0 ? bleedX10 : Math.Max(1, (int)((long)bleedX10 * LethalityPct / 100));

    private WoundRecord Make(BodyPlanV2 plan, BodyState bs, in WoundV2 w, long sec, ulong cause, bool dirty)
    {
        var part = plan.Parts[w.Part];
        var cat = Categorise(plan, w);
        var rec = new WoundRecord
        {
            Id = NextId++, Part = w.Part, Tissue = w.Tissue, Structure = w.Structure, Organ = w.Organ, Kind = w.Kind, Cat = cat, Grade = w.Grade, Grade0 = w.Grade, Count = w.Count,
            Residual = w.Residual, DepthPm = (ushort)Math.Clamp(w.DepthPm, 0, 1000), BleedX10 = plan.Bleeds ? Scale(w.BleedX10) : 0, Flags = w.Flags, Born = sec, Cause = cause,
            Internal = w.Organ >= 0 && w.BleedX10 > 0, Clots = plan.Bleeds && w.Structure < 0 && w.Organ < 0 && !w.Has(WoundFlags.Severed) && !w.Has(WoundFlags.ArteryHit) && !w.Has(WoundFlags.VeinHit),
            Aids = dirty ? Aid.Dirty : Aid.None,
        };
        if (w.Has(WoundFlags.Concussion) && w.Organ >= 0) rec.Aids |= Aid.Resolved;      // the brain bruise is the concussion timer, not a wound to heal
        int pain = PainOf(plan, w, cat);
        long pm = plan.FeelsPain ? part.PainPm : 0;
        if (bs.Parts[w.Part].Numb) { pm = pm * 300 / 1000; rec.Aids |= Aid.Numb; }
        rec.PainC = (int)(pain * 100L * pm / 1000);
        if (w.Has(WoundFlags.SensoryNerveHit) || w.Has(WoundFlags.NerveHit)) { }
        rec.NeedX10 = NeedX10(cat, w.Grade, w.DepthPm, false, false);
        if (cat == WoundCat.Bone) rec.Attr = AttrOf(plan, w.Part);
        return rec;
    }

    private static AttrLoss AttrOf(BodyPlanV2 plan, int part)
    {
        var p = plan.Parts[part]; string b = p.BaseId;
        if (p.IsHosted && p.Host >= 0 && b is not ("jaw" or "collarbone" or "kneecap" or "finger" or "fingers" or "thumb")) b = plan.Parts[p.Host].BaseId;
        if (b == "jaw") return AttrLoss.Speech;
        if (b is "skull" or "nose" or "teeth" or "eye" or "ear") return AttrLoss.None;
        if (b.Contains("arm") || b.Contains("shoulder") || b.Contains("rib") || b == "collarbone" || b.Contains("upper_body")) return AttrLoss.Strength;
        if (b.Contains("hand") || b.Contains("finger") || b.Contains("thumb") || b.Contains("leg") || b.Contains("thigh") || b.Contains("shin") || b.Contains("foot") || b.Contains("kneecap") || b.Contains("pelvis") || b.Contains("lower_body")) return AttrLoss.Dexterity;
        return p.Limb == "arm" ? AttrLoss.Strength : p.Limb == "leg" ? AttrLoss.Dexterity : AttrLoss.Strength;
    }

    public static WoundCat Categorise(BodyPlanV2 plan, in WoundV2 w)
    {
        if (w.Has(WoundFlags.Severed)) return w.Has(WoundFlags.InsetHit) && plan.Parts[w.Part].Severable == false ? WoundCat.Inset : WoundCat.Severed;
        if (w.Organ >= 0) return w.Has(WoundFlags.Concussion) ? WoundCat.Concussion : WoundCat.Organ;
        if (w.Structure >= 0)
            return plan.Parts[w.Part].Structures[w.Structure].Kind switch
            {
                StructureKind.Tendon => WoundCat.Tendon,
                StructureKind.Ligament => WoundCat.Ligament,
                StructureKind.SpinalCord => WoundCat.Spine,
                StructureKind.MotorNerve or StructureKind.SensoryNerve or StructureKind.Nerve => WoundCat.Nerve,
                _ => WoundCat.Vessel,
            };
        var part = plan.Parts[w.Part];
        if (part.Shell) return WoundCat.Bone;
        foreach (var l in part.Layers)
            if (l.Tissue.Index == w.Tissue) return l.Role == TissueRole.Bone ? WoundCat.Bone : l.Role == TissueRole.Muscle ? WoundCat.Muscle : part.IsHosted ? WoundCat.Inset : WoundCat.Skin;
        return WoundCat.Skin;
    }

    /// <summary>Pain at the moment of the blow, units (wounds 3.3), before the part and numbness factors.</summary>
    public static int PainOf(BodyPlanV2 plan, in WoundV2 w, WoundCat cat)
    {
        var part = plan.Parts[w.Part];
        if (w.Has(WoundFlags.Severed)) return part.IsHosted ? (part.BaseId == "eye" ? 40 : part.BaseId == "teeth" ? 4 * Math.Max(1, (int)w.Count) : 12) : 45;
        switch (cat)
        {
            case WoundCat.Skin: return 3;
            case WoundCat.Inset: return part.BaseId == "nose" ? 12 : part.BaseId == "eye" ? 40 : 6;
            case WoundCat.Muscle: return w.Grade switch { 0 => 1, 1 => 4, 2 => 10, _ => 20 };
            case WoundCat.Bone: return part.BaseId == "jaw" && w.Grade >= 2 ? 30 : w.Grade switch { 0 => 2, 1 => 15, 2 => 30, 3 => 40, _ => 55 };
            case WoundCat.Ligament: return w.Grade switch { 1 => 10, 2 => 20, _ => 35 };
            case WoundCat.Tendon: return 15;
            case WoundCat.Nerve: return 20;
            case WoundCat.Spine: return 40;
            case WoundCat.Vessel: return 6;
            case WoundCat.Organ:
                string id = part.Organs[w.Organ].Id;
                return id == "heart" ? 60 : id == "brain" ? 50 : 35;
            case WoundCat.Concussion: return 50;
            default: return 3;
        }
    }

    private void Add(in WoundRecord rec)
    {
        if (Records.Count >= 32) Compact();
        Records.Add(rec);
    }

    private void Compact()
    {
        for (int i = 0; i < Records.Count && Records.Count >= 32; i++)
        {
            var w = Records[i];
            if (w.State == WoundState.Scar && w.Cat is WoundCat.Skin or WoundCat.Inset or WoundCat.Vessel or WoundCat.Concussion || w.Is(Aid.Resolved) && w.State == WoundState.Scar)
            { Records.RemoveAt(i); i--; }
        }
    }

    // ---------------------------------------------------------------- the body as it is now

    /// <summary>The wounds as the resolver's records, for <see cref="BodyState"/> (healed and repaired ones left out).</summary>
    public List<WoundV2> FunctionWounds()
    {
        var l = new List<WoundV2>(Records.Count);
        foreach (var w in Records)
        {
            if (w.Is(Aid.Resolved)) continue;
            l.Add(new WoundV2
            {
                Part = w.Part, Tissue = w.Tissue, Structure = w.Structure, Organ = w.Organ, Kind = w.Kind, DepthPm = w.DepthPm, Grade = w.Grade, Count = w.Count,
                Residual = w.Residual, BleedX10 = w.BleedX10, Flags = w.Flags,
            });
        }
        return l;
    }

    public BodyAids AidsFor(bool crutch = false)
    {
        ulong splint = 0, rep = 0;
        foreach (var w in Records)
        {
            if (w.Is(Aid.Splinted) && w.Cat == WoundCat.Bone && !w.Is(Aid.Resolved)) splint |= 1UL << w.Part;
            if (w.Is(Aid.Repaired) && w.Cat == WoundCat.Ligament) rep |= 1UL << w.Part;
        }
        return new BodyAids(crutch, splint, rep);
    }

    /// <summary>The derived capacities now (walk, grip, sight, paralysis, numbness...): splints and crutches included.</summary>
    public BodyState Function(BodyPlanV2 plan, bool crutch = false)
    {
        var aids = AidsFor(crutch);
        if (CacheFunction && _fnState != null && ReferenceEquals(_fnPlan, plan) && _fnAids == aids && SameFunctionWounds()) return _fnState;
        var f = FunctionWounds();
        var bs = BodyState.From(plan, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(f), aids);
        if (CacheFunction) { _fnState = bs; _fnKey = [.. f]; _fnPlan = plan; _fnAids = aids; }
        return bs;
    }

    /// <summary>
    /// P4 close-out (abstract battle performance): the book keeps the last <see cref="Function"/> and hands it back while the function wounds, the aids and the plan are unchanged (an
    /// abstract battle reads a soldier's body before and after every hit and after every step). Only the abstract battle sets it, on its own copies, and clears it before the books leave
    /// the battle; callers there only read the state. Off, nothing is cached.
    /// </summary>
    internal bool CacheFunction
    {
        get => _cacheFunction;
        set { _cacheFunction = value; if (!value) { _fnState = null; _fnKey = null; _fnPlan = null; } }
    }
    private bool _cacheFunction;
    private BodyState? _fnState;
    private WoundV2[]? _fnKey;
    private BodyPlanV2? _fnPlan;
    private BodyAids _fnAids;

    private bool SameFunctionWounds()
    {
        var key = _fnKey!; int i = 0;
        foreach (ref readonly var w in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Records))
        {
            if (w.Is(Aid.Resolved)) continue;
            if (i >= key.Length) return false;
            ref readonly var k = ref key[i++];
            if (k.Part != w.Part || k.Tissue != w.Tissue || k.Structure != w.Structure || k.Organ != w.Organ || k.Kind != w.Kind || k.DepthPm != w.DepthPm || k.Grade != w.Grade
                || k.Count != w.Count || k.Residual != w.Residual || k.BleedX10 != w.BleedX10 || k.Flags != w.Flags) return false;
        }
        return i == key.Length;
    }

    /// <summary>What blood loss and infection do to strength, permille (weakened -20%, fever -20%).</summary>
    public int StrengthPm
    {
        get
        {
            int p = 1000; if (Weakened) p -= 200;
            foreach (var w in Records) if (w.InfectionLevel >= 2) { p -= 200; break; }
            return p;
        }
    }

    public int MaxInfection { get { int m = 0; foreach (var w in Records) m = Math.Max(m, w.InfectionLevel); return m; } }

    /// <summary>Pain relief: ale -8, poultice -15, poppy -40 (wounds 3.3).</summary>
    public void Relieve(int pain) => PainX100 = Math.Max(0, PainX100 - pain * 100);

    // ---------------------------------------------------------------- hashing

    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        static ulong Mix(ulong h, long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; } } return h; }
        h = Mix(h, (long)Key); h = Mix(h, BloodUl); h = Mix(h, BloodMaxUl); h = Mix(h, PainX100); h = Mix(h, (long)State); h = Mix(h, (long)Down); h = Mix(h, LastSec);
        h = Mix(h, UnconsciousUntilSec); h = Mix(h, HeartAtSec); h = Mix(h, BreathStopSec); h = Mix(h, CalmSince); h = Mix(h, ConcussedUntilDay); h = Mix(h, (long)Death); h = Mix(h, Latch); h = Mix(h, NextId);
        h = Mix(h, (long)Permanent.StrengthLoss | (long)Permanent.DexterityLoss << 8 | (long)Permanent.WitsLoss << 16 | (long)Permanent.SpeechCap << 24 | (long)Permanent.Malunions << 32 | (long)Permanent.Concussions << 40);
        h = Mix(h, (long)Permanent.AmputatedMask);
        foreach (var w in Records)
        {
            if (w.TourniquetOnS != 0) { h = Mix(h, w.TourniquetOnS); h = Mix(h, (long)w.DistalMask); }
            h = Mix(h, w.Id); h = Mix(h, w.Part); h = Mix(h, w.Tissue); h = Mix(h, w.Structure); h = Mix(h, w.Organ); h = Mix(h, (long)w.Kind | (long)w.Cat << 8 | (long)w.Grade << 16 | (long)w.Count << 24);
            h = Mix(h, w.DepthPm); h = Mix(h, w.BleedX10); h = Mix(h, (long)w.Flags); h = Mix(h, (long)w.State | (long)w.Dressing << 8 | (long)w.Aids << 16 | (w.Internal ? 1L << 24 : 0) | (w.Clots ? 1L << 25 : 0));
            h = Mix(h, w.Born); h = Mix(h, w.DressFromS); h = Mix(h, w.HealX10); h = Mix(h, w.NeedX10); h = Mix(h, w.PainC); h = Mix(h, w.InfectionLevel); h = Mix(h, w.SepsisDay); h = Mix(h, w.DeathDay); h = Mix(h, w.StitchedAt); h = Mix(h, w.DressedAt); h = Mix(h, w.Grade0);
        }
        return h;
    }
}
