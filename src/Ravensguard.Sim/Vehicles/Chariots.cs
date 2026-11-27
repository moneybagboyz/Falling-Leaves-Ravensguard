using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Sim.Carts;

/// <summary>One crewman thrown when a chariot crashes: who, and the momentum of his fall (milli, <see cref="ChargeMath.FallMilli"/> at the car's speed from the rules' height).</summary>
public readonly record struct ChariotFall(EntityId Person, long Milli);

/// <summary>What a second of a chariot's driving did: the speed it ended at, whether it went down (and how: a broken wheel or axle, or a ditch or stakes at speed) and who was thrown.</summary>
public readonly record struct ChariotStep(int SpeedMmps, bool Crashed, string Why, IReadOnlyList<ChariotFall> Falls);

/// <summary>One chariot at work (sieges-and-engines 4.9): a finished vehicle of the <c>war_chariot</c> template, its team of horses, its driver and its fighter (an archer or a spearman).</summary>
public sealed class Chariot
{
    public EntityId Vehicle;
    public int Horses = 2;
    public EntityId Driver, Fighter;
    public bool Spear;
    /// <summary>The driver's Riding (his driving).</summary>
    public int Riding;
    public int SpeedMmps;
    public bool Crashed;
}

/// <summary>
/// P7-SG-E (sieges-and-engines 4.9): chariots, open to every culture. A chariot is a vehicle with a team of Phase 4 horses harnessed (its <see cref="VehicleRow.Team"/>), a driver who drives with Riding and a fighter. In passing the fighter's
/// thrust adds momentum as a rider's does (<see cref="ChargeMath.ExtraMilli"/> with the whole car's mass and a spear's 300 permille transfer from a car) and the team tramples, a horse each; braced spears turn the team (the Phase 4 refusal);
/// the ground rules the car: fast on dry open flat ground, forbidden in woods, 1 m/s in mud, x0.4 over rough and ploughed ground, a crash into a ditch or stakes at speed; a broken wheel or axle crashes it at its speed and each crewman falls.
/// A car turns no faster than 45 degrees a second at speed. All integer; one second a step.
/// </summary>
public sealed class ChariotBook
{
    public ContentDatabase Content { get; }
    public RideBook Rides { get; }
    public ChariotRulesDef Rules { get; }
    public ulong Seed { get; }
    public SortedDictionary<EntityId, Chariot> Chariots { get; } = [];
    private readonly FootingRule[] _footing;
    private readonly ChariotGroundDef?[] _ground;
    public SiegeChronicle? Chronicle;
    public EntityId Cause;

    public ChariotBook(ContentDatabase content, RideBook rides, ulong seed)
    {
        Content = content; Rides = rides; Seed = seed;
        Rules = content.All<ChariotRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ChariotRulesDef { Id = "chariot_rules" };
        _footing = TerrainFooting.From(content.All<MountedTuningDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MountedTuningDef { Id = "default" });
        _ground = new ChariotGroundDef?[TerrainFooting.Ids.Length];
        for (int i = 0; i < _ground.Length; i++) _ground[i] = Rules.Ground.GetValueOrDefault(TerrainFooting.Ids[i]);
    }

    public Chariot? Find(EntityId vehicle) => Chariots.GetValueOrDefault(vehicle);

    /// <summary>Takes a finished chariot into the book with its crew (null when the vehicle is not one).</summary>
    public Chariot? Raise(VehicleRow v, EntityId driver, EntityId fighter, bool spear, int riding = 40, int horses = 2)
    {
        if (!v.Finished || v.Design != Rules.Template) return null;
        if (Chariots.TryGetValue(v.Id, out var had)) return had;
        var c = new Chariot { Vehicle = v.Id, Driver = driver, Fighter = fighter, Spear = spear, Riding = riding, Horses = horses };
        Chariots[v.Id] = c;
        return c;
    }

    /// <summary>The whole car's mass: the vehicle's own (300 kg of car, wheels and pole), the team and the crew: 300 + 900 + 160 = 1,360 kg.</summary>
    public int TotalKg(Chariot c)
    {
        var v = Rides.Vehicles.Find(c.Vehicle)!;
        return Rides.Vehicles.StatsOf(v).MassKg + c.Horses * Rules.HorseKg + 2 * Rules.CrewKg;
    }

    /// <summary>The fighter's thrust in passing, extra momentum in milli at a closing speed (mm/s): <c>charge_k x kg x v_c x transfer</c>; 10 m/s with the 1,360 kg of the spec is 40,800.</summary>
    public long ThrustMilli(Chariot c, int vcMmps) => ChargeMath.ExtraMilli(Rules.ChargeK, TotalKg(c), vcMmps, Rules.TransferPm);

    /// <summary>The team's trample on a man in its path, milli: one entry a horse, each rolled on stream 0x7E8 (key the chariot, the second, the horse) against the hit chance; 0 where the hooves miss.</summary>
    public long[] Trample(Chariot c, int vcMmps, long sec)
    {
        var r = new long[c.Horses];
        for (int h = 0; h < c.Horses; h++)
            if (DetRng.Range(Seed, sec, c.Vehicle.Value, RngStreams.ChariotTrample, (uint)h, 0, 1000) < Rules.TrampleHitPm) r[h] = ChargeMath.TrampleMilli(Rules.TrampleK, Rules.HorseKg, vcMmps);
        return r;
    }

    /// <summary>The braced spears in the way turn the team: the one refusal formula of the horses, <paramref name="trainedPm"/> the team's training.</summary>
    public static int RefusalPm(int bracedPolearms, int trainedPm, int riding, MountedRefusalDef r, int stormPm = 1000)
        => ChargeMath.RefusalPm(bracedPolearms, trainedPm, riding, r.BasePm, r.PerBracedPolearmPm, r.TrainedK, r.TrainedRef, r.RidingK, r.MaxPm, stormPm);

    // ------------------------------------------------------------------ the ground

    /// <summary>The speed a car may hold on this ground, mm/s, wanting <paramref name="wantMmps"/>: the footing's share, its cap, the slope (uphill slows, downhill helps), 0 where the ground is forbidden.</summary>
    public int Cap(Footing f, int wantMmps, int slopePm = 0)
    {
        var g = _ground[(int)f]; var cav = _footing[(int)f];
        if (g?.Forbidden == true || (g == null && cav.Forbidden)) return 0;
        long v = Math.Min(wantMmps, Rules.TopMmps);
        v = v * (g?.SpeedPm ?? 1000) / 1000;
        if (g is { MaxMmps: > 0 }) v = Math.Min(v, g.MaxMmps);
        v = v * TerrainFooting.SlopePm(slopePm) / 1000;
        return (int)Math.Max(0, v);
    }

    /// <summary>The widest a car turns in one second at a speed, in byte units of a full turn (45 degrees a second is 32); unlimited at a walk.</summary>
    public int TurnLimit(int speedMmps) => speedMmps >= Rules.TurnFromMmps ? Rules.TurnDegPerS * 256 / 360 : 256;

    // ------------------------------------------------------------------ driving

    /// <summary>
    /// Drives the car one game second toward a wanted speed and heading (a byte of a full turn) over this ground: the speed climbs by the acceleration or falls by the braking toward what the ground allows, the heading turns by no more than
    /// the limit, the car moves, its wheels wear. A broken wheel or axle, or a ditch or stakes at speed, crashes it: the speed is lost and each crewman falls.
    /// </summary>
    public ChariotStep Step(Chariot c, int wantMmps, int wantHeading, Footing f, int slopePm, long sec, EngineWorld? w = null)
    {
        var v = Rides.Vehicles.Find(c.Vehicle)!;
        if (c.Crashed) return new ChariotStep(0, false, "", []);
        if (v.Broken && c.SpeedMmps > 0) return Crash(c, v, sec, "ch_note_sg_wheel", w);
        var g = _ground[(int)f];
        if (g is { Crash: true } && c.SpeedMmps >= Rules.CrashMinMmps) return Crash(c, v, sec, "ch_note_sg_ditch", w);
        int cap = Cap(f, wantMmps, slopePm);
        c.SpeedMmps = c.SpeedMmps < cap ? Math.Min(cap, c.SpeedMmps + Rules.AccelMmps) : Math.Max(cap, c.SpeedMmps - Rules.BrakeMmps);
        if (v.Broken) c.SpeedMmps = 0;
        int turn = ((wantHeading - v.Facing + 384) & 255) - 128;                       // the shortest way round, -128..127
        int lim = TurnLimit(c.SpeedMmps);
        v.Facing = (byte)((v.Facing + Math.Clamp(turn, -lim, lim)) & 255);
        if (c.SpeedMmps > 0)
        {
            var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
            v.XMm += (c.SpeedMmps * cos + (1L << 29)) >> 30; v.YMm += (c.SpeedMmps * sin + (1L << 29)) >> 30;
            Rides.Vehicles.Wear(v, c.SpeedMmps, g?.SpeedPm < 700 ? 600 : 0);
        }
        return new ChariotStep(c.SpeedMmps, false, "", []);
    }

    private ChariotStep Crash(Chariot c, VehicleRow v, long sec, string note, EngineWorld? w)
    {
        int speed = c.SpeedMmps; c.SpeedMmps = 0; c.Crashed = true;
        var falls = new List<ChariotFall>(2);
        foreach (var p in new[] { c.Driver, c.Fighter }) if (p != default) falls.Add(new ChariotFall(p, ChargeMath.FallMilli(Rules.CrewKg, speed, Rules.FallHeightMm)));
        falls.Sort((a, b) => a.Person.CompareTo(b.Person));
        Chronicle?.Invoke(new GameTime(sec), "chariot_crash", "ch_sg_chariot_crash", [ChronParam.Count("x", v.XMm / 1000), ChronParam.Count("y", v.YMm / 1000), ChronParam.Count("speed", speed / 1000)], [],
            [new Cause(CauseKind.Siege, Cause == default ? null : Cause, "", note)], [("x", (v.XMm / 1000).ToString()), ("y", (v.YMm / 1000).ToString())]);
        return new ChariotStep(0, true, note, falls);
    }
}
