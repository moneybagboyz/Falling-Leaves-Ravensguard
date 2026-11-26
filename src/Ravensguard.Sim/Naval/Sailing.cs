using Ravensguard.Content;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>The sea numbers (<c>naval/sea.jsonc</c>); the defaults are the shipped row for maths with no content database.</summary>
public sealed class SeaRules
{
    public SeaRulesDef D { get; }
    public SeaRules(SeaRulesDef d) { D = d; }
    public static readonly SeaRules Default = new(new SeaRulesDef { Id = "default" });
    public static SeaRules From(ContentDatabase db) => db.All<SeaRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() is { } d ? new SeaRules(d) : Default;
}

/// <summary>A ship as the sailing rules see it: its design's figures and its load (4.1).</summary>
public sealed class SeaShip
{
    public EntityId Id { get; init; }
    public VehicleStats Stats { get; init; } = null!;
    public BoatLoad Load { get; init; }
    public SeaRules Rules { get; init; } = SeaRules.Default;
    public WaterTuning Water { get; init; } = WaterTuning.Default;
    /// <summary>The sail area set (m2), at most the design's; a reefed or lowered sail sets less.</summary>
    public int SailM2 { get; set; }
    public int Rowers => Stats.Rowers;

    public static SeaShip Of(EntityId id, VehicleBlueprint bp, int cargoKg, int crew, SeaRules rules, WaterTuning wt) => new()
    {
        Id = id, Stats = bp.Stats, Rules = rules, Water = wt, SailM2 = bp.Stats.SailM2,
        Load = BoatMath.Load(bp.Stats, cargoKg, crew * BoatMath.PersonKg, wt),
    };

    /// <summary>Hull speed, mm/s: <c>1.25 x sqrt(waterline m)</c> m/s, an integer square root (a 22 m cog 5.86 m/s, a 24 m longship 6.12).</summary>
    public int HullSpeedMmps => (int)(Rules.D.HullSpeedPm * IntMath.Isqrt((long)Stats.LengthCells * 1_000_000L) / 1000);

    public long WetMm2 => BoatMath.WetMm2(Stats.WaterplaneMm2, Stats.PerimeterM, Load.DraftMm) * (Slender ? Rules.D.SlenderDragPm : 1000) / 1000;

    /// <summary>A long lean hull (length over beam 4 or more: the longship's 4.8, not the cog's 3.1) drags at 450 permille of the Phase 5 wetted-area rule, which is how 32 oars make the spec's 2.5 m/s (reading of 4.1).</summary>
    public bool Slender => Stats.WidthCells > 0 && Stats.LengthCells * 10 / Stats.WidthCells >= Rules.D.SlenderLbX10;

    /// <summary>Leeway, permille of the speed on a beam reach: 150, and 250 for a shallow keel (a loaded draft under 1 m).</summary>
    public int LeewayPm => Load.DraftMm < Rules.D.ShallowDraftMm ? Rules.D.LeewayShallowPm : Rules.D.LeewayPm;

    /// <summary>Turn rate in degrees a second at 3 m/s, by what steers: a rudder or a steering oar (longships under oars turn faster).</summary>
    public int RudderDegS => Stats.Steer == "steering_oar" ? Rules.D.SteeringOarDegS : Rules.D.RudderDegS;

    /// <summary>The windage the hull gives the wind, m2 (a metre of side above the water a metre of length): what a headwind pushes against.</summary>
    public int WindageM2 => Stats.LengthCells;
}

/// <summary>What the crew is doing: oars out or in, a hard stroke, how tired the rowers are (permille of stamina), the anchor.</summary>
public sealed class SeaCrewState
{
    public int StaminaPm = 1000;
    public bool OarsOut, Hard, Anchored;
    public int RowersManned = -1;
    public int Manned(SeaShip s) => RowersManned < 0 ? s.Rowers : Math.Min(RowersManned, s.Rowers);

    /// <summary>Rowers tire by stroke and rest on the oars' rest: 1 point every 6 s easy, every second hard (400 floor in power), back at 1 point every 2 s with the oars shipped.</summary>
    public void Tire(SeaRules r, int seconds)
    {
        if (seconds <= 0) return;
        if (!OarsOut) { StaminaPm = Math.Min(1000, StaminaPm + seconds / Math.Max(1, r.D.RestSecPerPoint)); return; }
        int per = Hard ? r.D.HardSecPerPoint : r.D.EasySecPerPoint;
        StaminaPm = Math.Max(0, StaminaPm - seconds / Math.Max(1, per));
    }
}

/// <summary>The result of one reading of the sailing rules: the forces along the keel, the speed through the water along the heading and the downwind slip (mm/s).</summary>
public readonly record struct SailReading(long SailN, long OarN, long WindageN, long NetN, int SpeedMmps, int LeewayMmps, bool HullCapped, int OffAsternQ24);

/// <summary>Sailing by wind (ships-and-naval-war 4.1): points of sail for a square sail, hull speed, leeway, oars, steering. Integer only: Q30 sines from <see cref="FrameMath.CosSin"/>, <see cref="IntMath.Isqrt"/> for roots.</summary>
public static class Sailing
{
    /// <summary>The angle between the heading and the way the wind blows toward, 0 to half a turn: 0 is a wind dead astern, a half turn dead ahead.</summary>
    public static int OffAsternQ24(int headingQ24, int windToQ24)
    {
        int d = (windToQ24 - headingQ24) & FrameMath.Mask;
        return d > FrameMath.Turn / 2 ? FrameMath.Turn - d : d;
    }

    /// <summary>The way the wind blows toward, in the frame's own heading (east 0, counter-clockwise, y north): a wind <paramref name="fromDeg"/> (compass, 0 north) blows toward <c>90 + from</c>.</summary>
    public static int WindToQ24(int windFromDeg) => FrameMath.FromDegrees(90 + windFromDeg);

    private static int CosPm(int angleQ24) => (int)((FrameMath.CosSin(angleQ24).Cos * 1000 + (1L << 29)) >> 30);
    private static int SinAbsPm(int angleQ24) => (int)((Math.Abs(FrameMath.CosSin(angleQ24).Sin) * 1000 + (1L << 29)) >> 30);

    /// <summary>
    /// The sail's push, N: <c>0.6 x m2 x wind2 x cos(angle off astern - brace)</c> (Phase 5's <see cref="BoatMath.SailN"/>), the yards braced round by 45 degrees; 0 with the wind forward of the beam
    /// (a square-rigger points no higher than a beam reach: reading of 4.1, where an unbraced cosine would give a beam reach nothing).
    /// </summary>
    public static long SailForceN(SeaRules r, int sailM2, int windMps, int offAsternQ24)
    {
        if (offAsternQ24 > FrameMath.Turn / 4) return 0;
        int eff = Math.Max(0, offAsternQ24 - FrameMath.FromDegrees(r.D.BraceDeg));
        return BoatMath.SailN(sailM2, windMps, CosPm(eff));
    }

    /// <summary>The oars' push, N: manned rowers x 70 N (x3 on a hard stroke), by their stamina (power runs from 400 permille tired to 1000 fresh).</summary>
    public static long OarForceN(SeaShip s, SeaCrewState c)
    {
        if (!c.OarsOut) return 0;
        int pm = s.Rules.D.PowerFloorPm + (1000 - s.Rules.D.PowerFloorPm) * c.StaminaPm / 1000;
        return (long)c.Manned(s) * s.Water.RowerN * (c.Hard ? 3 : 1) * pm / 1000;
    }

    /// <summary>The wind's push on the hull along the keel, N (negative against a headwind): <c>0.6 x windage m2 x wind2 x cos(off astern)</c>.</summary>
    public static long WindageN(SeaShip s, int windMps, int offAsternQ24) => (long)6 * s.WindageM2 * windMps * windMps * CosPm(offAsternQ24) / 10_000;

    /// <summary>
    /// One reading: heading and wind give the forces, the net force against <c>DragK x A_wet</c> the speed (<see cref="BoatMath.WaterSpeedMmps"/>), capped at hull speed; the slip downwind is the leeway share of the
    /// speed by the sine of the angle off astern, scaled by how much of the push is the sail's (oars alone do not heel the ship).
    /// </summary>
    public static SailReading Read(SeaShip s, SeaCrewState c, int headingQ24, int windToQ24, int windMps)
    {
        int off = OffAsternQ24(headingQ24, windToQ24);
        long sail = c.Anchored ? 0 : SailForceN(s.Rules, s.SailM2, windMps, off), oar = c.Anchored ? 0 : OarForceN(s, c), wind = WindageN(s, windMps, off);
        long net = c.Anchored ? 0 : sail + oar + wind;
        int v = net <= 0 ? 0 : BoatMath.WaterSpeedMmps(net, s.Water.DragK, s.WetMm2);
        int hull = s.HullSpeedMmps; bool capped = v > hull; if (capped) v = hull;
        int lee = 0;
        if (v > 0 && sail > 0) lee = (int)((long)v * s.LeewayPm / 1000 * SinAbsPm(off) / 1000 * sail / Math.Max(1, sail + oar));
        return new SailReading(sail, oar, wind, net, v, lee, capped, off);
    }

    /// <summary>
    /// The turn a tick, Q24: <c>rudder rate x speed / 3 m/s</c> degrees a second (a cog 3 deg/s at 3 m/s, a longship under oars 6), none at no speed; a ship with its oars out and rowers manned turns on the spot at
    /// 10 deg/s. <paramref name="helm"/> is -1, 0 or 1.
    /// </summary>
    public static int TurnPerTick(SeaShip s, SeaCrewState c, int speedMmps, int helm)
    {
        if (helm == 0 || c.Anchored) return 0;
        long q;
        if (speedMmps > 0) q = (long)s.RudderDegS * speedMmps * FrameMath.Turn / (3000L * 360 * 20);
        else if (c.OarsOut && c.Manned(s) > 0 && c.StaminaPm > 0) q = (long)s.Rules.D.SpotTurnDegS * FrameMath.Turn / (360L * 20);
        else return 0;
        return (int)(helm > 0 ? q : -q);
    }

    /// <summary>One live tick (20 Hz): reads the rules at the frame's heading, sets its speed and turn and steps it; the leeway carries into <paramref name="slip"/> (the frame's own motion is along the keel).</summary>
    public static SailReading Tick(SeaShip s, SeaCrewState c, ShipFrame f, int windToQ24, int windMps, int helm, ref SlipAcc slip)
    {
        var r = Read(s, c, f.Heading, windToQ24, windMps);
        f.SpeedMmps = r.SpeedMmps; f.TurnPerTick = TurnPerTick(s, c, r.SpeedMmps, helm);
        f.Step();
        slip.Add(f, r.LeewayMmps, windToQ24);
        return r;
    }
}

/// <summary>The sub-millimetre remainder of a slip, so ticks add up to the hour's whole millimetres.</summary>
public struct SlipAcc
{
    private long _x, _y;
    public void Add(ShipFrame f, int leewayMmps, int windToQ24)
    {
        if (leewayMmps <= 0) return;
        var (c, sn) = FrameMath.CosSin(windToQ24);
        _x += leewayMmps * c; _y += leewayMmps * sn;
        const long Per = 20L * (1L << 30);
        long dx = _x / Per, dy = _y / Per; _x -= dx * Per; _y -= dy * Per;
        f.XMm += (int)dx; f.YMm += (int)dy;
    }
}

/// <summary>Sea legs, live and abstract (4.1, "same numbers live and abstract"; VD-19 at sea): the same speed from the same rules, the same seconds from <see cref="Voyages.LegSeconds"/>.</summary>
public static class SeaLegs
{
    /// <summary>The seconds of a leg of <paramref name="lengthMm"/> at <paramref name="mmps"/>.</summary>
    public static long Seconds(long lengthMm, int mmps) => Voyages.LegSeconds(lengthMm, mmps);

    /// <summary>The abstract hourly step: the distance a ship covers in one game hour at a speed (the live tier adds <c>speed / 20</c> a second, 3,600 of them).</summary>
    public static long HourMm(int mmps) => (long)mmps * 3600 / 20;
}

/// <summary>NPC masters (4.3): the best seaman of the crew commands, by Seamanship, or half his Fishing; ties go to the lower id.</summary>
public static class Masters
{
    public static int Skill(int seamanship, int fishing) => Math.Max(seamanship, fishing / 2);

    public static ulong? Pick(IEnumerable<(ulong Id, int Seamanship, int Fishing)> crew)
    {
        ulong? best = null; int bs = -1;
        foreach (var (id, sea, fish) in crew.OrderBy(c => c.Id))
        {
            int s = Skill(sea, fish);
            if (s > bs) { bs = s; best = id; }
        }
        return best;
    }
}
