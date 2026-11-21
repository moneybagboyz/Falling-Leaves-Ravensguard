using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>The numbers of the water (ships-and-naval-war 4.7): swimming speeds, the kit a swimmer can carry, the breath, the climb out and the cold. The breath and sinking numbers are the underground crawl's (<c>site_constants</c>), so the one rule is the one rule.</summary>
public sealed class SwimTuning
{
    /// <summary>Swimming speed, mm/s: 0.6 m/s, 0.9 m/s from Athletics 40.</summary>
    public int SwimMmps = 600, FastSwimMmps = 900, FastFromAthletics = 40;
    /// <summary>A swimmer carrying less worn mass than this (grams) swims; carrying more he sinks unless he sheds kit or holds something that floats.</summary>
    public int SinkGearG = 15_000;
    /// <summary>Seconds a man holds his breath under water: <c>HoldBreathS + Athletics / 2</c>.</summary>
    public int HoldBreathS = 30;
    /// <summary>Shedding kit: each second under water an Athletics test passes with <c>SinkCheckPm + 10 x Athletics</c> permille (at most <see cref="ShedMaxPm"/>); a piece takes its shed seconds of passes.</summary>
    public int SinkCheckPm = 500, ShedMaxPm = 950;
    /// <summary>A swimmer from this Athletics on sheds his kit when he sinks (he knows to); below it he flounders.</summary>
    public int ShedsFromAthletics = 20;
    /// <summary>Seconds to climb out onto a bank or a ship, and how near (mm) he must be to a refuge to start.</summary>
    public int ClimbS = 4, ReachMm = 1000;
    /// <summary>Cold water (under this many degrees C) takes this many permille of stamina a second.</summary>
    public int ColdBelowC = 10, ColdDrainPmPerS = 10;
    public const int TicksPerS = 20;

    public static readonly SwimTuning Default = new();

    public static SwimTuning From(ContentDatabase db)
    {
        var d = db.All<SiteConstantsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        return d == null ? Default : new SwimTuning { HoldBreathS = d.HoldBreathS, SinkGearG = d.SinkGearKg * 1000, SinkCheckPm = d.SinkCheckPm };
    }

    /// <summary>Stamina permille drained a second in water of <paramref name="seaTempC"/> degrees (the weather's sea temperature: the air's, lagged by a season).</summary>
    public int ColdDrainPmS(int seaTempC) => seaTempC < ColdBelowC ? ColdDrainPmPerS : 0;
}

/// <summary>One piece of what a swimmer wears or carries: grams, and seconds of passed tests to shed it under water (a shield 1 s, a mail shirt about 10 s: one second a kilogram).</summary>
public readonly record struct GearPiece(string Name, int MassG, int ShedS, bool Shield = false)
{
    /// <summary>A shield: dropped in one second.</summary>
    public static GearPiece OfShield(string name, int massG) => new(name, massG, 1, true);
    /// <summary>Worn armour: a second a kilogram, at least one.</summary>
    public static GearPiece OfArmour(string name, int massG) => new(name, massG, Math.Max(1, (massG + 999) / 1000));
}

public enum SwimState : byte { Swimming, Sinking, Climbing, Out, Drowned }

/// <summary>A person in the water.</summary>
public sealed class Swimmer
{
    public ulong Person;
    /// <summary>The fighter this person was in a fight (-1 when none).</summary>
    public int Fighter = -1;
    public int XMm, YMm;
    /// <summary>Athletics, 0 to 100.</summary>
    public int Athletics;
    /// <summary>What he wears and carries, in the order he drops it (shields first, then the heaviest).</summary>
    public List<GearPiece> Gear = [];
    /// <summary>Grams of lift from what he holds on to (a plank, an oar, a barrel).</summary>
    public int FloatG;
    /// <summary>Whether he tries to shed his kit when he sinks.</summary>
    public bool Sheds;
    public SwimState State;
    /// <summary>The tick he entered the water, and ticks spent under water this time (his breath).</summary>
    public long EnteredTick;
    public int UnderTicks;
    public int ShedProgressS;
    public int ClimbLeft;
    /// <summary>The refuge he is making for or climbing onto (an id passed to <see cref="Swimmers.Refuges"/>), 0 none.</summary>
    public int Refuge;
    /// <summary>What put him in the water: "blow", "fall", "sinking", "jump".</summary>
    public string Cause = "";
    public int RemX, RemY, RemDx, RemDy;

    public int WornG { get { int g = 0; foreach (var p in Gear) g += p.MassG; return g; } }
    /// <summary>Seconds under water so far.</summary>
    public int UnderS => UnderTicks / SwimTuning.TicksPerS;
}

/// <summary>A place a swimmer can climb out onto: a bank point or a ship's side (world mm). Ships move: the host updates the point each tick.</summary>
public sealed class Refuge
{
    public int Id;
    public int XMm, YMm;
    /// <summary>The ship it is the side of (0 for a bank) and the point of its deck (local mm) it is anchored to: the carrier layer moves it with the ship each tick.</summary>
    public ulong Frame;
    public int AnchorLx, AnchorLy;
}

/// <summary>
/// P7-SH-A, overboard, swimming and drowning (ships-and-naval-war 4.7, replacing Phase 5's "people to the bank"): a person in deep water swims at 0.6 m/s (0.9 from Athletics 40) while he wears under 15 kg; wearing more he sinks, unless he holds
/// something that floats (<see cref="Swimmer.FloatG"/>) or sheds kit (a shield in a second, mail a second a kilogram, an Athletics test each second, stream 0x7D2). Under water he holds his breath for <c>HoldBreathS + Athletics / 2</c> seconds (the
/// Phase 4 site crawl's rule, its numbers) and then drowns. A swimmer that reaches a refuge climbs out in 4 s. A current carries them all. One call a 20 Hz tick; swimmers are stepped in the order they entered. Integer only.
/// </summary>
public sealed class Swimmers
{
    public ulong Seed { get; }
    public SwimTuning K { get; }
    public long Tick { get; private set; }
    public List<Swimmer> All { get; } = [];
    public List<Refuge> Refuges { get; } = [];
    /// <summary>The current's drift, mm/s (a river's; the open sea's is 0).</summary>
    public int CurrentXMmps, CurrentYMmps;
    /// <summary>He went under (worn mass over the limit).</summary>
    public Action<Swimmer>? OnSank;
    /// <summary>He dropped a piece of kit.</summary>
    public Action<Swimmer, GearPiece>? OnShed;
    /// <summary>His breath ran out: the person dies (<c>DeathCause.Suffocation</c>, the Phase 4 breath kind; the chronicle names <see cref="Swimmer.Cause"/> as its chain).</summary>
    public Action<Swimmer>? OnDrowned;
    public Action<Swimmer>? OnClimbedOut;

    public Swimmers(ulong seed, SwimTuning? k = null) { Seed = seed; K = k ?? SwimTuning.Default; }

    /// <summary>A person goes into the water at (x, y): he swims or sinks at once by what he wears and holds. Gear is put in the order he drops it.</summary>
    public Swimmer Enter(ulong person, int xMm, int yMm, int athletics, IEnumerable<GearPiece> gear, string cause, int floatG = 0, int fighter = -1, bool? sheds = null)
    {
        var s = new Swimmer { Person = person, Fighter = fighter, XMm = xMm, YMm = yMm, Athletics = Math.Clamp(athletics, 0, 100), FloatG = floatG, Cause = cause, EnteredTick = Tick };
        s.Gear = [.. gear.OrderByDescending(g => g.Shield).ThenByDescending(g => g.MassG).ThenBy(g => g.Name, StringComparer.Ordinal)];
        s.Sheds = sheds ?? s.Athletics >= K.ShedsFromAthletics;
        All.Add(s);
        Settle(s, entering: true);
        return s;
    }

    /// <summary>
    /// A ship founders: every person aboard goes into the water where he stood (his world point through the frame), in (kind, id) order, and leaves the frame. <paramref name="kit"/> gives each one's Athletics and what he wears;
    /// <paramref name="wreckageG"/> is the lift a broken ship leaves each of them to hold on to (an oar, a plank, a barrel).
    /// </summary>
    public List<Swimmer> Abandon(ShipFrame ship, Func<ulong, (int Athletics, IEnumerable<GearPiece> Gear)> kit, string cause, int wreckageG = 0)
    {
        var got = new List<Swimmer>();
        foreach (var r in ship.Riders.Where(x => x.Kind == RiderKind.Person).ToList())
        {
            var (wx, wy) = ship.WorldOf(r); var (ath, gear) = kit(r.Id);
            ship.Leave(RiderKind.Person, r.Id);
            got.Add(Enter(r.Id, wx, wy, ath, gear, cause, wreckageG));
        }
        return got;
    }

    /// <summary>Sinks or floats him by what he wears now.</summary>
    private void Settle(Swimmer s, bool entering)
    {
        bool sinks = s.WornG - s.FloatG >= K.SinkGearG;
        if (sinks) { if (s.State != SwimState.Sinking) { s.State = SwimState.Sinking; s.UnderTicks = 0; OnSank?.Invoke(s); } }
        else { s.State = SwimState.Swimming; s.UnderTicks = 0; }
        _ = entering;
    }

    /// <summary>One 20 Hz tick.</summary>
    public void Step()
    {
        Tick++;
        foreach (var s in All)
        {
            if (s.State is SwimState.Out or SwimState.Drowned) continue;
            Drift(s);
            switch (s.State)
            {
                case SwimState.Sinking: Sink(s); break;
                case SwimState.Swimming: Swim(s); break;
                case SwimState.Climbing:
                    if (--s.ClimbLeft <= 0) { s.State = SwimState.Out; OnClimbedOut?.Invoke(s); }
                    break;
            }
        }
    }

    private void Drift(Swimmer s)
    {
        if (CurrentXMmps == 0 && CurrentYMmps == 0 || s.State == SwimState.Climbing) return;
        int ax = CurrentXMmps + s.RemDx, ay = CurrentYMmps + s.RemDy;
        s.XMm += ax / SwimTuning.TicksPerS; s.YMm += ay / SwimTuning.TicksPerS; s.RemDx = ax % SwimTuning.TicksPerS; s.RemDy = ay % SwimTuning.TicksPerS;
    }

    private void Sink(Swimmer s)
    {
        s.UnderTicks++;
        if (s.UnderTicks % SwimTuning.TicksPerS != 0) return;
        int sec = s.UnderTicks / SwimTuning.TicksPerS;
        if (s.Sheds && s.Gear.Count > 0)
        {
            int pm = Math.Min(K.ShedMaxPm, K.SinkCheckPm + 10 * s.Athletics);
            long second = Tick / SwimTuning.TicksPerS;
            if (DetRng.Range(Seed, second, s.Person, RngStreams.SheddingKit, 0, 0, 1000) < pm)
            {
                var piece = s.Gear[0];
                if (++s.ShedProgressS >= piece.ShedS)
                {
                    s.Gear.RemoveAt(0); s.ShedProgressS = 0;
                    OnShed?.Invoke(s, piece);
                    Settle(s, entering: false);                        // he may float now: his breath is his again
                    if (s.State == SwimState.Swimming) return;
                }
            }
        }
        if (sec >= K.HoldBreathS + s.Athletics / 2) { s.State = SwimState.Drowned; OnDrowned?.Invoke(s); }
    }

    private void Swim(Swimmer s)
    {
        Refuge? best = null; long bd = long.MaxValue;
        foreach (var r in Refuges)
        {
            long dx = r.XMm - s.XMm, dy = r.YMm - s.YMm, d = dx * dx + dy * dy;
            if (d < bd || (d == bd && best != null && r.Id < best.Id)) { bd = d; best = r; }
        }
        if (best == null) return;
        long dist = IntMath.Isqrt(bd);
        if (dist <= K.ReachMm) { s.Refuge = best.Id; s.State = SwimState.Climbing; s.ClimbLeft = K.ClimbS * SwimTuning.TicksPerS; return; }
        int speed = s.Athletics >= K.FastFromAthletics ? K.FastSwimMmps : K.SwimMmps;
        // a step of speed / 20 mm toward it, the remainder carried (the direction is the exact ratio, not a table)
        long ax = (long)(best.XMm - s.XMm) * speed + s.RemX, ay = (long)(best.YMm - s.YMm) * speed + s.RemY;
        long per = dist * SwimTuning.TicksPerS;
        long mx = ax / per, my = ay / per;
        s.RemX = (int)(ax - mx * per); s.RemY = (int)(ay - my * per);
        s.XMm += (int)mx; s.YMm += (int)my; s.Refuge = best.Id;
    }

    /// <summary>FNV-1a over every swimmer (a part of the <c>NavalHash</c>).</summary>
    public ulong Hash(ulong h)
    {
        h = ShipFrame.Mix(h, Tick);
        foreach (var s in All)
        {
            h = ShipFrame.Mix(h, (long)s.Person); h = ShipFrame.Mix(h, s.XMm); h = ShipFrame.Mix(h, s.YMm); h = ShipFrame.Mix(h, (long)s.State | (long)s.UnderTicks << 8);
            h = ShipFrame.Mix(h, s.WornG); h = ShipFrame.Mix(h, s.ShedProgressS | s.ClimbLeft << 16); h = ShipFrame.Mix(h, s.RemX); h = ShipFrame.Mix(h, s.RemY);
        }
        return h;
    }
}
