using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Flavour;

/// <summary>
/// Listens to a <see cref="FightWorld"/> (its <c>OnBlow</c> and <c>OnEvent</c> hooks) and writes the <see cref="FightLog"/>: a line per event, a snapshot of the layer trace and wound list
/// for every blow, and the statistics of the report. It only reads; the fight is exactly the same with or without it (test FL-9, the fight hash).
/// </summary>
public sealed class FlavourRecorder
{
    public FightLog Log { get; }
    public FightWorld World { get; }
    private BlowSnap? _pending;
    private int _pendingId = -1;
    private (int Tick, int Actor, int Target, bool Thrown)? _proj;
    private (int Tick, int Actor)? _over;
    private Action<Fighter, Fighter, BlowResult, StrikeV2>? _chainBlow;
    private Action<FightEvent>? _chainEvent;

    private FlavourRecorder(FightWorld w, FightLog log) { World = w; Log = log; }

    /// <summary>Starts recording <paramref name="w"/> into a new log (or <paramref name="log"/>, to carry on one restored from a save). Chains any hook already set.</summary>
    public static FlavourRecorder Attach(FightWorld w, FightLog? log = null, FlavourBook? book = null)
    {
        log ??= new FightLog { Seed = w.Seed, FightId = w.FightId, GameSec0 = w.GameSec0, StartTick = w.Tick };
        var r = new FlavourRecorder(w, log);
        r._chainBlow = w.OnBlow; r._chainEvent = w.OnEvent;
        w.OnBlow = r.Blow; w.OnEvent = r.Event;
        if (book != null) log.Picker = (l, upTo) => FlavourFormatter.PickUpTo(l, book, upTo);
        for (int i = 0; i < w.Fighters.Count; i++) r.Know(i);
        var hero = w.Fighters.FirstOrDefault(f => f.Player);
        if (hero != null) { log.Hero = hero.Id; log.HeroTeam = hero.Team; }
        return r;
    }

    public void Detach()
    {
        World.OnBlow = _chainBlow; World.OnEvent = _chainEvent;
    }

    /// <summary>Marks the end of the fight (the report's result and duration).</summary>
    public void Finish()
    {
        Log.EndTick = World.Tick; Log.Winner = World.WinnerTeam; Log.Finished = true;
        for (int i = 0; i < World.Fighters.Count; i++) Know(i).End = (byte)World.Fighters[i].Act;
    }

    public static string KindOf(string planId) => planId switch { "human" => "human", "horse" => "horse", "draugr" => "draugr", _ => "beast" };

    private FighterInfo Know(int id)
    {
        if (id < 0 || id >= World.Fighters.Count) return Log.Info(id);
        if (Log.TryInfo(id, out var have) && have.Name.Length > 0 && have.Name[0] != '#') return have;
        var f = World.Fighters[id];
        var info = Log.Info(id);
        info.Name = f.Name; info.Team = f.Team; info.Player = f.Player; info.Person = f.Person; info.PlanId = f.Plan.Id; info.Kind = KindOf(f.Plan.Id);
        return info;
    }

    // ------------------------------------------------------------------ the blow (OnBlow: after the resolver, before the events)

    private void Blow(Fighter a, Fighter t, BlowResult r, StrikeV2 s)
    {
        _chainBlow?.Invoke(a, t, r, s);
        var b = new BlowSnap
        {
            Tick = World.Tick, Attacker = (short)a.Id, Defender = (short)t.Id,
            WeaponShape = Log.Intern(s.Weapon.Shape.Id), WeaponMaterial = Log.Intern(s.Weapon.Head.Id), Attack = (byte)s.Attack,
            AKind = AttackKinds.Of(s.Weapon, s.Attack, s.Missile, false), Family = Log.Intern(s.Weapon.Shape.Family),
            Verb = Log.Intern(s.Weapon.Attacks[Math.Clamp(s.Attack, 0, s.Weapon.Attacks.Length - 1)].Def.Verb),
            AttackType = s.Weapon.Attacks[Math.Clamp(s.Attack, 0, s.Weapon.Attacks.Length - 1)].Type,
            Missile = s.Missile, Called = s.TargetPart >= 0 && (a.Req.Part >= 0 || s.TargetGap >= 0), Redirected = r.Redirected, Severed = r.Severed, Hit = r.Hit, Blocked = r.Blocked,
            Block = s.Block.Kind, Facing = s.Facing, Style = s.Style,
            Part = Log.Intern(r.Part >= 0 && r.Part < s.Plan.Parts.Length ? s.Plan.Parts[r.Part].Id : s.TargetPart >= 0 && s.TargetPart < s.Plan.Parts.Length ? s.Plan.Parts[s.TargetPart].Id : ""),
            InitialMilli = r.InitialMilli, FinalMilli = r.FinalMilli, ExcessMilli = r.Block.ExcessMilli,
        };
        var worn = s.Worn ?? [];
        int n = Math.Min(s.WornCount, worn.Length);
        b.PieceShapes = new ushort[n]; b.PieceMaterials = new ushort[n];
        for (int i = 0; i < n; i++) { b.PieceShapes[i] = Log.Intern(worn[i].Stats.Shape.Id); b.PieceMaterials[i] = Log.Intern(worn[i].Stats.Material.Id); }
        var mats = World.T.MaterialList;
        b.Steps = new StepSnap[r.StepCount];
        for (int i = 0; i < r.StepCount; i++)
        {
            ref var st = ref r.Steps[i];
            b.Steps[i] = new StepSnap
            {
                Piece = st.Piece, IsArmour = st.IsArmour, Outcome = st.Outcome, InMilli = st.InMilli, OutMilli = st.OutMilli, NeedMilli = st.NeedMilli, WearPm = st.WearPm,
                Tissue = st.Tissue >= 0 && st.Tissue < mats.Count ? Log.Intern(mats[st.Tissue].Id) : (ushort)0,
                Part = st.Part >= 0 && st.Part < s.Plan.Parts.Length ? Log.Intern(s.Plan.Parts[st.Part].Id) : (ushort)0,
            };
        }
        b.Wounds = new WoundSnap[r.WoundCount];
        for (int i = 0; i < r.WoundCount; i++)
        {
            ref var w = ref r.Wounds[i];
            var part = w.Part >= 0 && w.Part < s.Plan.Parts.Length ? s.Plan.Parts[w.Part] : null;
            b.Wounds[i] = new WoundSnap
            {
                Part = part == null ? (ushort)0 : Log.Intern(part.Id),
                Tissue = w.Tissue >= 0 && w.Tissue < mats.Count ? Log.Intern(mats[w.Tissue].Id) : (ushort)0,
                Structure = part != null && w.Structure >= 0 && w.Structure < part.Structures.Length ? Log.Intern(part.Structures[w.Structure].Id) : (ushort)0,
                Organ = part != null && w.Organ >= 0 && w.Organ < part.Organs.Length ? Log.Intern(part.Organs[w.Organ].Id) : (ushort)0,
                Kind = w.Kind, Grade = w.Grade, Count = w.Count, DepthPm = w.DepthPm, BleedX10 = w.BleedX10, Flags = w.Flags,
            };
        }
        _pending = b; _pendingId = -1;
    }

    private int PendingId()
    {
        if (_pending == null) return -1;
        if (_pendingId < 0) _pendingId = Log.AddBlow(_pending);
        return _pendingId;
    }

    // ------------------------------------------------------------------ the events

    private void Event(FightEvent e)
    {
        _chainEvent?.Invoke(e);
        var actor = Know(e.Actor); var target = e.Target >= 0 ? Know(e.Target) : null;
        if (e.Kind == FightEventKind.Windup) { actor.Swings++; actor.StaminaLow = Math.Min(actor.StaminaLow, StaminaOf(e.Actor)); return; }
        if (e.Kind is FightEventKind.Reloaded or FightEventKind.Shove) return;
        if (e.Kind == FightEventKind.ProjectileHit) { _proj = (e.Tick, e.Actor, e.Target, e.B != 0); return; }
        if (e.Kind == FightEventKind.Overpowered) { _over = (e.Tick, e.Actor); return; }   // told by the block that follows
        if (e.Kind == FightEventKind.Riposte)
        {
            // the answer to a parry is told by the parry's own line ("parries and answers"): mark it, add nothing
            if (Log.Lines.Count > 0 && Log.Lines[^1] is { Kind: FightEventKind.Parried } last && last.Tick == e.Tick) { last.B = 1; Log.Lines[^1] = last; }
            return;
        }

        var line = new FightLine { Tick = e.Tick, Kind = e.Kind, Actor = (short)e.Actor, Target = (short)e.Target, Part = e.Part, A = e.A, B = e.B, Blow = -1 };
        switch (e.Kind)
        {
            case FightEventKind.Hit:
            {
                var b = _pending; int id = PendingId();
                if (b == null) break;
                line.Blow = id; Stamp(ref line, b);
                line.Flags = FlagsOf(b);
                if (_proj is { } p && p.Tick == e.Tick && p.Actor == e.Actor && p.Target == e.Target)
                {
                    line.Flags |= LineFlags.Missile | (p.Thrown ? LineFlags.Thrown : 0); b.Missile = true; b.Thrown = p.Thrown; _proj = null;
                    if (b.Thrown) { b.AKind = AttackKinds.Thrown; line.AKind = AttackKinds.Thrown; }
                }
                line.Severity = (sbyte)Severity.Of(b.Wounds, Log.Str);
                if (b.Severed) line.Flags |= LineFlags.Severed;
                int ser = Log.Add(line);
                Count(actor, target!, b, line.Severity, ser);
                return;
            }
            case FightEventKind.Blocked: case FightEventKind.Missed: case FightEventKind.Stopped:
            {
                var b = _pending; int id = PendingId();
                if (b == null) break;
                line.Blow = id; Stamp(ref line, b); line.Flags = FlagsOf(b);
                if (e.Kind == FightEventKind.Blocked)
                {
                    if (e.A != 0) line.Flags |= LineFlags.Shield;
                    if (_over is { } o && o.Tick == e.Tick && o.Actor == e.Actor) { line.Flags |= LineFlags.Overpowered; _over = null; }
                    actor.Blocked++;                       // the blocker is the actor of a Blocked event, the attacker its target
                    if (target != null) target.Missed++;
                }
                else if (e.Kind == FightEventKind.Missed) actor.Missed++;
                break;
            }
            case FightEventKind.Dodged: case FightEventKind.Parried: case FightEventKind.Overpowered: case FightEventKind.Riposte:
            {
                // actor is the defender, target the attacker: the attacker's weapon is the one that was swung
                if (e.Target >= 0 && e.Target < World.Fighters.Count) Weapon(ref line, World.Fighters[e.Target]);
                if (e.Kind == FightEventKind.Dodged) actor.Dodged++; else if (e.Kind == FightEventKind.Parried) actor.Parried++;
                if (e.Kind is FightEventKind.Dodged or FightEventKind.Parried && target != null) target.Missed++;
                break;
            }
            case FightEventKind.Whiff: case FightEventKind.Shot: case FightEventKind.Throw: case FightEventKind.Feint:
                if (e.Actor >= 0 && e.Actor < World.Fighters.Count) Weapon(ref line, World.Fighters[e.Actor]);
                break;
            case FightEventKind.Down:
                actor.End = (byte)FAct.Down; actor.DownSerial = Log.NextSerial; Sample(actor, e.Actor); line.Severity = 3;
                break;
            case FightEventKind.Death:
                actor.End = (byte)FAct.Dead; actor.DeathSerial = Log.NextSerial; Sample(actor, e.Actor); line.Severity = 4;
                break;
            case FightEventKind.Wake: actor.End = (byte)FAct.Idle; break;
            case FightEventKind.Fled: actor.End = (byte)FAct.Fled; break;
            case FightEventKind.Surrender: actor.End = (byte)FAct.Surrendered; break;
        }
        Log.Add(line);
    }

    private static LineFlags FlagsOf(BlowSnap b)
    {
        var f = LineFlags.None;
        if (b.Style == AttackStyle.Heavy) f |= LineFlags.Heavy; else if (b.Style == AttackStyle.Quick) f |= LineFlags.Quick;
        if (b.Called) f |= LineFlags.Called;
        if (b.Facing == Facing.Flank) f |= LineFlags.Flank; else if (b.Facing == Facing.Rear) f |= LineFlags.Rear;
        if (b.Redirected) f |= LineFlags.Redirected;
        if (b.Missile) f |= LineFlags.Missile;
        return f;
    }

    private static void Stamp(ref FightLine line, BlowSnap b)
    {
        line.WeaponShape = b.WeaponShape; line.WeaponMaterial = b.WeaponMaterial; line.Attack = b.Attack; line.AKind = b.AKind; line.Verb = b.Verb; line.Family = b.Family;
    }

    private void Weapon(ref FightLine line, Fighter f)
    {
        var w = f.Weapon;
        int atk = Math.Clamp(f.Req.Attack, 0, Math.Max(0, w.Attacks.Length - 1));
        line.WeaponShape = Log.Intern(w.Shape.Id); line.WeaponMaterial = Log.Intern(w.Head.Id); line.Attack = (byte)atk; line.Family = Log.Intern(w.Shape.Family);
        line.AKind = AttackKinds.Of(w, atk, f.Launcher != null, false); line.Verb = Log.Intern(w.Attacks[atk].Def.Verb);
        if (f.Heavy) line.Flags |= LineFlags.Heavy;
    }

    private int StaminaOf(int id) => id >= 0 && id < World.Fighters.Count ? World.Fighters[id].Stamina : 10000;

    private void Sample(FighterInfo info, int id)
    {
        if (id < 0 || id >= World.Fighters.Count) return;
        var f = World.Fighters[id];
        info.LossHighPm = Math.Max(info.LossHighPm, f.Book.LossPm);
        info.StaminaLow = Math.Min(info.StaminaLow, f.Stamina);
    }

    private void Count(FighterInfo atk, FighterInfo def, BlowSnap b, int severity, int serial)
    {
        atk.Landed++;
        switch (b.Missile ? 3 : (int)b.AttackType) { case 0: atk.Edge++; break; case 1: atk.Point++; break; case 2: atk.Blunt++; break; default: atk.Missile++; break; }
        if (severity > atk.BestSeverity || severity == atk.BestSeverity && b.InitialMilli > atk.BestMomentum) { atk.BestSeverity = severity; atk.BestMomentum = b.InitialMilli; atk.BestLine = serial; }
        def.LastHitSerial = serial;
        int bleed = 0; foreach (var w in b.Wounds) bleed += w.BleedX10;
        if (bleed >= def.WorstBleedX10) { def.WorstBleedX10 = bleed; def.WorstHitSerial = serial; }
        Sample(def, def.Id);
    }
}
