using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Flavour;

/// <summary>Hand-made logs for the golden lines and the variety checks: a blow with a known trace and wounds, and optionally forced template picks.</summary>
public sealed class Synth
{
    public FightLog Log { get; } = new() { Seed = 77, FightId = 1, Hero = 0, HeroTeam = 0, TriggerKey = "fl_trigger_duel", Place = "" };

    public Synth Person(int id, string name, int team, string kind = "human", bool female = false)
    {
        var f = Log.Info(id); f.Name = name; f.Team = team; f.Kind = kind; f.Female = female; f.PlanId = kind == "beast" ? "cave_beast" : kind;
        return this;
    }

    public ushort I(string s) => Log.Intern(s);

    public WoundSnap W(string part, string tissue, WoundFlags flags = WoundFlags.None, string structure = "", string organ = "", WoundKind kind = WoundKind.Cut, byte grade = 0, int bleed = 0, byte count = 0) => new()
    {
        Part = I(part), Tissue = I(tissue), Structure = I(structure), Organ = I(organ), Kind = kind, Grade = grade, Count = count, DepthPm = 1000, BleedX10 = bleed, Flags = flags,
    };

    public StepSnap Armour(int piece, LayerOutcome o, long inM = 100_000, long outM = 80_000, long need = 16_100) => new() { Piece = (short)piece, IsArmour = true, Outcome = o, InMilli = inM, OutMilli = outM, NeedMilli = need };
    public StepSnap Layer(string tissue, LayerOutcome o, long inM = 80_000, long outM = 60_000, long need = 9_000) => new() { Piece = -1, Tissue = I(tissue), Outcome = o, InMilli = inM, OutMilli = outM, NeedMilli = need };

    /// <summary>Adds a line for a blow that hits (or is stopped) and returns its index. <paramref name="picks"/> are template ids to force (empty: leave the cursor to pick).</summary>
    public int Blow(int attacker, int defender, string shape, string mat, byte aKind, string verb, string family, string part, string[]? worn, StepSnap[] steps, WoundSnap[] wounds,
        FightEventKind kind = FightEventKind.Hit, LineFlags flags = LineFlags.None, AttackType type = AttackType.Edge, string[]? picks = null, int severity = -1, LayerOutcome[]? _ = null)
    {
        var b = new BlowSnap
        {
            Tick = 100 + Log.Lines.Count * 10, Attacker = (short)attacker, Defender = (short)defender, WeaponShape = I(shape), WeaponMaterial = I(mat), AKind = aKind, Verb = I(verb), Family = I(family),
            AttackType = type, Hit = true, Part = I(part), InitialMilli = 140_000, FinalMilli = 60_000, Steps = steps, Wounds = wounds, Facing = (flags & LineFlags.Rear) != 0 ? Facing.Rear : (flags & LineFlags.Flank) != 0 ? Facing.Flank : Facing.Front,
        };
        if (worn != null)
        {
            b.PieceShapes = [.. worn.Select(w => I(w.Split(':')[0]))]; b.PieceMaterials = [.. worn.Select(w => I(w.Split(':')[1]))];
        }
        int id = Log.AddBlow(b);
        var l = new FightLine
        {
            Tick = b.Tick, Kind = kind, Actor = (short)attacker, Target = (short)defender, Blow = id, WeaponShape = b.WeaponShape, WeaponMaterial = b.WeaponMaterial, AKind = aKind, Verb = b.Verb, Family = b.Family, Flags = flags,
            Severity = (sbyte)(severity >= 0 ? severity : Severity.Of(wounds, Log.Str)),
        };
        return Add(l, picks);
    }

    public int Event(FightEventKind kind, int actor, int target, string shape = "arming_sword", string mat = "steel", byte aKind = 0, string verb = "slashes", string family = "blade", LineFlags flags = LineFlags.None,
        int a = 0, string[]? picks = null, int severity = 0)
    {
        var l = new FightLine { Tick = 100 + Log.Lines.Count * 10, Kind = kind, Actor = (short)actor, Target = (short)target, Blow = -1, WeaponShape = I(shape), WeaponMaterial = I(mat), AKind = aKind, Verb = I(verb), Family = I(family), Flags = flags, A = a, Severity = (sbyte)severity };
        return Add(l, picks);
    }

    int Add(FightLine l, string[]? picks)
    {
        if (picks != null)
        {
            var p = new ushort[5];
            for (int i = 0; i < picks.Length && i < 5; i++) p[i] = I(picks[i]);
            l.P0 = p[0]; l.P1 = p[1]; l.P2 = p[2]; l.P3 = p[3]; l.P4 = p[4]; l.Picked = true;
        }
        Log.Add(l);
        return Log.Lines.Count - 1;
    }
}
