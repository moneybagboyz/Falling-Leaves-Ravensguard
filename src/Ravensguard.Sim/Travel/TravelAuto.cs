using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Travel;

/// <summary>The ETA of a journey (spec 4.2): 1 m/s at 1x covers 180 m per game hour.</summary>
public readonly record struct Eta(int GameHoursTenths, long RealSeconds1x, long RealSeconds8x)
{
    public static Eta Of(long pathM, int speedCmps)
    {
        if (speedCmps <= 0) return new(0, 0, 0);
        // game_hours = path_m / (speed_m_s x 180); real_seconds = path_m / (speed_m_s x multiplier). Whole numbers: speed in cm/s.
        int tenths = (int)((pathM * 100 * 10 + (long)speedCmps * 180 / 2) / ((long)speedCmps * 180));
        long r1 = (pathM * 100 + speedCmps / 2) / speedCmps;
        long r8 = (pathM * 100 + (long)speedCmps * 8 / 2) / ((long)speedCmps * 8);
        return new(tenths, r1, r8);
    }
}

public sealed partial class TravelState
{
    public const long SlotSec = 600;

    /// <summary>Starts a trip along <paramref name="path"/> at game second <paramref name="sec"/>. The leg counter moves on (once-per-leg encounters reset).</summary>
    public Journey Begin(string destName, RoutePath path, long sec)
    {
        Bus.Resume(); Bus.ClearSpeedCap();
        LegSerial++; LegFired.Clear();
        Journey = new Journey { DestName = destName, Path = path, StartSec = sec, Sec = sec, StartXMm = path.X[0], StartYMm = path.Y[0], DestXMm = path.X[^1], DestYMm = path.Y[^1], State = JourneyState.Travelling, Leg = LegSerial };
        return Journey;
    }

    public (long X, long Y) HeroPosition => Journey is { } j ? j.Path.At(j.ProgressMm) is var p ? (p.X, p.Y) : (0, 0) : (0, 0);

    /// <summary>The hero's speed here in mm per game second: the base (jog, mount, cart) x the ground x the weather x mud.</summary>
    public int SpeedMmPerGs(in LegSample s, in TravelStatus h)
    {
        int ground = Cfg.TerrainSpeedPm.TryGetValue(s.Ground ?? "road", out int g) ? g : 1000;
        int wx = Cfg.WeatherSpeedPm.TryGetValue(s.Weather.ToString(), out int w) ? w : 1000;
        int mud = (s.Terrain & TerrainFlags.Mud) != 0 ? 700 : 1000;
        long v = (long)Math.Max(1, h.BaseMmPerGs == 0 ? 150 : h.BaseMmPerGs) * ground / 1000 * wx / 1000 * mud / 1000;
        return (int)Math.Max(1, v);
    }

    /// <summary>Whether 8x is allowed and why not (spec 4.2 speed gate).</summary>
    public (bool Allowed, string Reason) Gate8x(in TravelStatus h)
    {
        if (h.HostileM != int.MaxValue) return (false, (string.IsNullOrEmpty(h.HostileName) ? "A stranger" : h.HostileName) + " in sight");
        if (h.PartyThreat) return (false, "The party is threatened");
        if (h.InCombatStance) return (false, "In combat stance");
        if (Bus.Paused) return (false, "Paused: " + Bus.PausedBy!.Value.Kind);
        return (true, "");
    }

    public bool IsDusk(long sec)
    {
        var t = new GameTime(sec); int m = t.Hour * 60 + t.Minute, ss = Daylight.SunsetMinute(LatitudeTenths, t.DayOfYear);
        return m >= ss - Cfg.DuskLeadMinutes && m < ss + 30;
    }

    /// <summary>
    /// Raises every travel interrupt whose condition just turned on (edge-triggered, so calling it each tick is cheap and repeatable), applies the hostile speed cap, and runs the hunger auto-eat.
    /// Returns true when the journey was paused by a Pause-policy kind.
    /// </summary>
    public bool Evaluate(in LegSample s, in TravelStatus h, long sec, long xMm, long yMm)
    {
        var b = Bus;
        b.Edge(InterruptKind.Hostile, h.HostileM != int.MaxValue, sec, xMm, yMm, (string.IsNullOrEmpty(h.HostileName) ? "Hostiles" : h.HostileName) + " in sight");
        if (h.HostileM == int.MaxValue && !h.PartyThreat && !h.InCombatStance && b.SpeedCapped) b.ClearSpeedCap();
        b.Edge(InterruptKind.Attacked, h.Attacked, sec, xMm, yMm, "Under attack");
        bool badWx = s.Weather is WeatherState.Thunderstorm or WeatherState.Blizzard or WeatherState.Heatwave || (s.Weather == WeatherState.HeavyRain && (s.Terrain & TerrainFlags.RiverCrossing) != 0);
        b.Edge(InterruptKind.Weather, badWx, sec, xMm, yMm, s.Weather.ToString());
        b.Edge(InterruptKind.Cold, h.ColdStressPm > 400 || h.CoreBelow36, sec, xMm, yMm, "Getting cold");
        b.Edge(InterruptKind.Heat, h.HeatStressPm > 400, sec, xMm, yMm, "Overheating");
        bool hungry = h.HoursSinceMeal >= Cfg.HungerEatHours;
        if (b.Edge(InterruptKind.Hunger, hungry || h.FoodLow, sec, xMm, yMm, hungry ? "Time to eat" : "Food is running low") && hungry) AutoEat(sec);
        b.Edge(InterruptKind.Fatigue, h.FatiguePm > 800, sec, xMm, yMm, "Camp needed");
        b.Edge(InterruptKind.Dusk, IsDusk(sec) && s.Daytime, sec, xMm, yMm, "Dusk is near");
        b.Edge(InterruptKind.Horse, h.HorseTired || h.HorseLame, sec, xMm, yMm, h.HorseLame ? "Your horse is lame" : "Your horse is tiring");
        b.Edge(InterruptKind.CartStuck, h.CartStuck, sec, xMm, yMm, "The cart is stuck");
        b.Edge(InterruptKind.Companion, h.CompanionTrouble, sec, xMm, yMm, "A companion needs you");
        b.Edge(InterruptKind.Found, h.FoundWithinM <= 25, sec, xMm, yMm, "Something to see here");
        if (b.Paused && Journey is { State: JourneyState.Travelling } j) j.State = JourneyState.Paused;
        return b.Paused;
    }

    /// <summary>The meal break of the hunger interrupt: eat one person-day of the pack's food, rest 10 game minutes. Does nothing without food in the pack.</summary>
    private void AutoEat(long sec)
    {
        var pack = PackStore?.Invoke(); if (pack == null || Goods == null) return;
        long ate = FeedFromStore(pack, 1, sec: sec);
        if (ate <= 0) return;
        LastMealSec = sec;
        if (Journey is { } j) { j.FoodUsedG += ate; j.BreakUntilSec = sec + Cfg.MealBreakMinutes * TimeConst.SecondsPerMinute; }
    }

    /// <summary>Takes the player's choice after a Pause: the game runs again.</summary>
    public void Resume()
    {
        Bus.Resume();
        if (Journey is { State: JourneyState.Paused } j) j.State = JourneyState.Travelling;
    }

    public void Cancel(long sec)
    {
        if (Journey is not { } j || j.State is JourneyState.Arrived or JourneyState.Cancelled) return;
        j.State = JourneyState.Cancelled; Bus.Resume();
        Finish(j, sec, false);
    }

    private void Finish(Journey j, long sec, bool completed)
    {
        Logs.Add(new JourneyLog(j.StartSec, sec, j.DestName, j.ProgressMm / 1000, j.CoinSpent, j.FoodUsedG, [.. j.EncounterIds], completed));
        if (Logs.Count > 100) Logs.RemoveAt(0);
    }

    /// <summary>
    /// Advances the journey to game second <paramref name="toSec"/>. Work happens at 10-minute slot starts (speed from the ground and weather, interrupts, the encounter roll) and between them the hero
    /// walks at that slot's speed, so any chunking of <paramref name="toSec"/> (20 s a tick at 1x, 160 s at 8x) gives the same positions and the same encounters. Stops early at a Pause interrupt or arrival.
    /// </summary>
    public void Advance(ITravelWorld w, long toSec)
    {
        if (Journey is not { State: JourneyState.Travelling } j) return;
        int guard = 0;
        while (j.Sec < toSec && j.State == JourneyState.Travelling && guard++ < 1_000_000)
        {
            long sec = j.Sec, slot = sec / SlotSec;
            var (px, py, hx, hy) = j.Path.At(j.ProgressMm);
            if (slot != j.SlotDone)
            {
                j.SlotDone = slot;
                var s = w.Sample(sec, px, py); var h = w.Status(sec);
                j.SlotSpeedMmPerGs = SpeedMmPerGs(s, h);
                bool paused = Evaluate(s, h, sec, px, py);
                if (sec % SlotSec == 0 && sec > j.StartSec) RollEncounter(j, s, h, sec, px, py);
                if (Bus.Paused || j.State != JourneyState.Travelling) break;
                if (paused) break;
            }
            long end = Math.Min(toSec, (slot + 1) * SlotSec);
            if (sec < j.BreakUntilSec) { j.Sec = Math.Min(end, j.BreakUntilSec); continue; }
            long dt = end - sec, speed = j.SlotSpeedMmPerGs, need = j.TotalMm - j.ProgressMm;
            if (need <= 0 || speed * dt >= need)
            {
                long take = speed <= 0 ? dt : (need + speed - 1) / speed;
                j.Sec = sec + Math.Min(dt, Math.Max(0, take)); j.ProgressMm = j.TotalMm; j.State = JourneyState.Arrived;
                var (ax, ay, _, _) = j.Path.At(j.ProgressMm);
                Bus.Raise(InterruptKind.Arrived, j.Sec, ax, ay, "Arrived at " + j.DestName);
                Finish(j, j.Sec, true);
                break;
            }
            j.ProgressMm += speed * dt; j.Sec = end;
        }
    }

    private void RollEncounter(Journey j, in LegSample s, in TravelStatus h, long sec, long px, long py)
    {
        var d = EncounterDirector.Roll(this, s, h, sec);
        if (d == null) return;
        int slotKey = (int)(sec % TimeConst.SecondsPerHour / SlotSec);
        var cast = new List<ulong>(); long sx = px, sy = py;
        for (int gi = 0; gi < d.Groups.Count; gi++)
        {
            var g = d.Groups[gi];
            int dist = EncounterPlacement.Distance(this, sec, slotKey, gi, g), n = EncounterPlacement.Count(this, sec, slotKey, gi, g);
            var (gx, gy) = g.Place == "behind" ? j.Path.Ahead(j.ProgressMm, -dist * 1000L) : j.Path.Ahead(j.ProgressMm, dist * 1000L);
            if (gi == 0) { sx = gx; sy = gy; }
            foreach (var id in EncounterPlacement.Cast(this, d, g, n, sec, gx, gy)) cast.Add(id.Value);
        }
        var ev = new EncounterEvent(sec, d.Id, d.Kind, px, py, sx, sy, j.Leg, [.. cast], d.Bark);
        Encounters.Add(ev); if (Encounters.Count > 200) Encounters.RemoveAt(0);
        j.EncounterIds.Add(d.Id); PendingEncounter = ev;
        LastEncounterSec = sec; LastById[d.Id] = sec; if (d.OncePerLeg) LegFired.Add(d.Id);
        var kind = d.Kind switch
        {
            "Ambush" or "Animal" or "Demand" => InterruptKind.Hostile,
            "Obstacle" => InterruptKind.BlockedPath,
            "Pass" or "Omen" or "Find" => InterruptKind.Found,
            _ => InterruptKind.Contract,
        };
        Bus.Raise(kind, sec, px, py, d.Name);
        if (Bus.Paused) j.State = JourneyState.Paused;
    }
}
