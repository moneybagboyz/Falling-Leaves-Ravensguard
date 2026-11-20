using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Law;

/// <summary>What the seeing rule needs of the place and the hour (crime-and-justice 4.1): line of sight, light, roofs and the weather. The live tier answers from its grids (<see cref="GridScene"/>).</summary>
public interface IWitnessScene
{
    DayPhase Phase { get; }
    bool HeavyRain { get; }
    /// <summary>Walls and closed doors block the line between two cells; portals are the level grid's business.</summary>
    bool Los(Cell3 a, Cell3 b);
    bool Lit(Cell3 c);
    bool Indoor(Cell3 c);
}

/// <summary>The scene of a live street: the light grid's opaque cells block, a cell is lit by its point light, indoors by its roof flag.</summary>
public sealed class GridScene(LightGrid grid, LightFrame? frame, Ambient ambient, bool heavyRain, int litLevel) : IWitnessScene
{
    public DayPhase Phase => ambient.Phase;
    public bool HeavyRain => heavyRain;
    public bool Indoor(Cell3 c) => grid.IsIndoor(c.X, c.Y, c.Z);
    public bool Lit(Cell3 c) => frame != null && c.X >= 0 && c.Y >= 0 && c.X < frame.Width && c.Y < frame.Height && c.Z >= frame.MinZ && c.Z <= frame.MaxZ && frame.Point(c.X, c.Y, c.Z) >= litLevel;

    /// <summary>A Bresenham walk on the actor's level between the cells; an opaque cell strictly between them blocks. Different levels see each other only through the same column of open cells (no rule yet: they do not).</summary>
    public bool Los(Cell3 a, Cell3 b)
    {
        if (a.Z != b.Z) return false;
        int x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y, dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (x0 != x1 || y0 != y1)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            if ((x0 != x1 || y0 != y1) && grid.IsOpaque(x0, y0, a.Z)) return false;
        }
        return true;
    }
}

/// <summary>What the outer layer of a person's clothes looks like at a distance: the dyes of the cloak or tabard and the build (4.1, open question 3).</summary>
public readonly record struct ActorLook(uint[] Colours, byte Build)
{
    public static readonly ActorLook None = new([], 0);
    public bool Matches(ActorLook now) => Build == now.Build && Colours.Length > 0 && Colours.All(c => Array.IndexOf(now.Colours, c) >= 0);
}

/// <summary>The one who acts: where, sneaking or not (and how well), hooded, bearing a visible mark, his renown (permille) and what he wears.</summary>
public sealed record WitnessActor(EntityId Person, Cell3 At, bool Sneaking, int Stealth, bool Hooded, bool Marked, int RenownPm, ActorLook Look);

/// <summary>Someone who could see: where, awake, how well the actor is known (familiarity), his trust of the actor (permille), kin, and what he would do about a crime (a guard, his melee skill).</summary>
public sealed class Observer
{
    public EntityId Person;
    public Cell3 At;
    public bool Awake = true;
    public int Familiarity;
    public int TrustPm;
    public bool Kin;
    public bool Guard;
    public int Melee;
}

/// <summary>One witness: who saw, from how far (centimetres) and whether he knows who it was.</summary>
public readonly record struct Sighting(Observer Who, int DistCm, bool Recognises);

/// <summary>The seen-or-unseen rule (crime-and-justice 4.1): geometry and light, never a dice roll, so the HUD can show it before the act and the act uses the same answer.</summary>
public static class Witnessing
{
    /// <summary>How far an observer can see this actor, in centimetres: daylight 30 m, dusk and dawn 15 m, night 6 m, 6 m more where the actor's cell is lit; indoors 12 m lit and 4 m dark; heavy rain x0.7; sneaking shrinks it by Stealth / 2 percent.</summary>
    public static int SightCm(LawSettingsDef s, IWitnessScene scene, WitnessActor actor)
    {
        bool lit = scene.Lit(actor.At);
        long m = scene.Indoor(actor.At)
            ? (lit ? s.SightIndoorLitM : s.SightIndoorDarkM)
            : (scene.Phase switch { DayPhase.Day => s.SightDayM, DayPhase.Night => s.SightNightM, _ => s.SightDuskM } + (lit ? s.SightLitBonusM : 0));
        long cm = m * 100;
        if (scene.HeavyRain) cm = cm * s.RainPct / 100;
        if (actor.Sneaking) cm = cm * (100 - Math.Clamp(actor.Stealth / Math.Max(1, s.StealthDivisor), 0, 100)) / 100;
        return (int)cm;
    }

    /// <summary>Centimetres between two cells (a level apart counts three metres).</summary>
    public static int DistCm(Cell3 a, Cell3 b)
    {
        long dx = (a.X - b.X) * 100L, dy = (a.Y - b.Y) * 100L, dz = (a.Z - b.Z) * 300L;
        return (int)Math.Min(int.MaxValue, IntMath.Isqrt(dx * dx + dy * dy + dz * dz));
    }

    /// <summary>The observers who see the act now, in person-id order, with whether each recognises the actor: familiarity of 1 or more, or within 10 m in light (5 m when hooded), or a visible mark.</summary>
    public static List<Sighting> Evaluate(LawSettingsDef s, IWitnessScene scene, WitnessActor actor, IEnumerable<Observer> observers)
    {
        int range = SightCm(s, scene, actor);
        bool light = scene.Phase != DayPhase.Night || scene.Lit(actor.At);
        int near = (actor.Hooded ? s.RecogniseHoodedM : s.RecogniseM) * 100;
        var res = new List<Sighting>();
        foreach (var o in observers.OrderBy(o => o.Person))
        {
            if (!o.Awake || o.Person == actor.Person) continue;
            int d = DistCm(o.At, actor.At);
            if (d > range || !scene.Los(o.At, actor.At)) continue;
            bool knows = o.Familiarity >= 1 || actor.Marked || (light && d <= near);
            res.Add(new Sighting(o, d, knows));
        }
        return res;
    }

    /// <summary>The HUD's number: how many would see the act now (0 is Unseen).</summary>
    public static int SeenCount(LawSettingsDef s, IWitnessScene scene, WitnessActor actor, IEnumerable<Observer> observers) => Evaluate(s, scene, actor, observers).Count;

    /// <summary>A guard matches a described person by the clothes he now wears (4.2, stream 0x611): never when a colour differs, 70% when every described colour matches.</summary>
    public static bool GuardRecognises(ulong seed, EntityId guard, EntityId person, int day, ActorLook described, ActorLook now) =>
        described.Matches(now) && DetRng.Range(seed, day, guard.Value, RngStreams.GuardRecognition, (uint)(person.Value & 0xFFFFFFFFUL), 0, 100) < 70;
}
