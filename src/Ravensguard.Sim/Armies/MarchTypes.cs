using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Armies;

/// <summary>Where a marching host stands in its journey. Append only (the save stores the number).</summary>
public enum MarchState : byte { Marching, Camped, Arrived, Left }

/// <summary>
/// The numbers of marching and camping (armies-on-the-map 4.4, 4.5), in one place until the content file arrives (the pattern of <c>SupplyConfig</c>). A host walks at the column's 1.3 m/s: 65 millimetres a game second,
/// 234 metres a game hour, 16 hours a day (05:00 to 21:00); 8 km of road is 34 hours and a little over two days with a night camp between.
/// </summary>
public static class MarchConfig
{
    public const int ColumnMmPerGs = 65, DayStartHour = 5, DayEndHour = 21;
    /// <summary>The column's layout (4.4): three abreast, 1.5 m between ranks, 1.2 m between men of a rank, wagons 12 m apart, the van this many metres ahead of the foot, the baggage this many behind it.</summary>
    public const int Abreast = 3, RankMm = 1500, LaneMm = 1200, WagonMm = 12_000, VanGapMm = 40_000, BaggageGapMm = 20_000, VanSharePct = 5;
    /// <summary>A camp (4.5): six men a tent, one in 25 on watch by night in three watches, a fire for three tents, a latrine for 50 men, a horse line for 20 animals, a tent pitched in a quarter of a worker-hour (900 s).</summary>
    public const int MenPerTent = 6, SentryOneIn = 25, Watches = 3, TentsPerFire = 3, MenPerLatrine = 50, AnimalsPerLine = 20, PitchSecPerTent = 900, PitchSecPerFire = 360, PitchSecPerLine = 900, PitchSecPerLatrine = 1800, PitchSecPavilion = 3600;
    /// <summary>The ring of a camp for a stay of two nights or more: a palisade piece every 2 m of a ring round the tents (4 worker-hours a piece) and a shallow ditch outside it.</summary>
    public const int PalisadeAfterNights = 2, PalisadeStepMm = 2000;
    /// <summary>The camp is pitched within this many metres of water (a settlement's well); further, the host carries its water (3 L a man a day in casks from the baggage).</summary>
    public const int WaterReachM = 300;
    /// <summary>A camp near a town or another camp draws its water below them: the days that count toward the disease rule's unboiled-water term.</summary>
    public const int BelowTownM = 1500;
    /// <summary>The night alarm (4.5): a sentry sees a man in the dark this far (metres), a shout wakes the sleepers within this many metres, and a woken sleeper stirs 5 to 20 game seconds after it (stream 0x424).</summary>
    public const int NightSightM = 60, AlarmM = 30, WakeMinSec = 5, WakeMaxSec = 20;
    /// <summary>Speed of the baggage on a surface, permille of the cart's best (dry road 1,000; mud 450; deep mud 250; snow and ice slower): a host with wagons walks at the slower of the column and the baggage.</summary>
    public static int BaggagePm(Surface s) => s switch
    {
        Surface.Paved or Surface.DryRoad => 1000, Surface.WetRoad => 850, Surface.Track => 800, Surface.Grass => 750, Surface.MudRoad => 450, Surface.DeepMud => 250,
        Surface.PackedSnow => 600, Surface.DeepSnow => 250, Surface.Ice => 500, _ => 700,
    };
}

/// <summary>A camp as the record keeps it (4.5): where, since when, how big, whether it has its palisade, how many keep watch, and how many nights it has stood. The plan itself is a pure function of these and the host.</summary>
public sealed class CampRecord
{
    /// <summary>The centre in continent millimetres, the settlement nearest it and the road heading there (unit vector x1024).</summary>
    public long XMm, YMm;
    public int Near = -1, HeadX = 1024, HeadY;
    /// <summary>The game second the host arrived, the camp's serial for its host (the jitter's key), the men it holds, the nights it has stood and the watch the sentries are on now.</summary>
    public long SinceSec;
    public int Serial, Men, Nights, Watch;
    public bool Palisade, Latrine = true, WaterCarried, BelowTown;
    /// <summary>The persons and counts: the pieces of the plan placed so far, and the sentries on this watch (a count: who they are is a pure function of the persons' order and the night).</summary>
    public int Pieces, Sentries;
}

/// <summary>
/// A host on the road (4.4): the route over the region's road graph as a list of stops (a settlement is its index, a gate of the region <c>-1 - gate</c>), the distance walked along the whole route at the start of the hour
/// <see cref="HourSec"/> and what it walks in that hour, the one before it (so a column drawn in the middle of the hour never jumps), and the camp when it sleeps. The position of every man is a pure function of this
/// and the game second (<see cref="HostMotion"/>): the abstract hour and the live column cannot drift apart.
/// </summary>
public sealed class HostMarch
{
    public int Host;
    public List<int> Stops { get; } = [];
    public long DistMm, RateHourMm, HourSec = long.MinValue, PrevDistMm, PrevRateHourMm, PrevHourSec = long.MinValue;
    public MarchState State = MarchState.Marching;
    public CampRecord? Camp;
    public int CampsPitched, StartDay, Hours, MarchHours;
    /// <summary>The hour at which the host was last asked to move and the text key of why it marches (every march has a reason).</summary>
    public string Cause = "";
    /// <summary>Persons made for the host's men from outside the region, by person id: the troop role (a <c>TroopRole</c> value). They live in the region while the host does; on leaving the region the living ones are <c>Departed</c> rows kept here and the dead are not on this list.</summary>
    public SortedDictionary<ulong, byte> Outsiders { get; } = [];
    /// <summary>Whether the outsiders' rows are in the region now (alive in the people table) or away (departed, kept for the next time the host comes in).</summary>
    public bool InRegion;
    /// <summary>A halt (a stay: the siege's camp, a rest): the host stays camped through the day and builds its ring after two nights.</summary>
    public bool Hold;
    /// <summary>The chronicle entry of the host's arrival (0 until it has arrived).</summary>
    public ulong ChronEntry;
}

/// <summary>The pure position of a marching host (a function of the record and the game second).</summary>
public static class HostMotion
{
    /// <summary>The distance along the route at <paramref name="sec"/> (millimetres): the hour's start plus the hour's rate times the seconds into it; before the hour began, the hour before's.</summary>
    public static long DistAt(HostMarch m, long sec)
    {
        if (m.State is MarchState.Arrived or MarchState.Left) return m.DistMm;
        if (m.HourSec == long.MinValue) return m.DistMm;
        if (sec >= m.HourSec) return m.DistMm + m.RateHourMm * Math.Min(3600, sec - m.HourSec) / 3600;
        if (m.PrevHourSec != long.MinValue && sec >= m.PrevHourSec) return m.PrevDistMm + m.PrevRateHourMm * Math.Min(3600, sec - m.PrevHourSec) / 3600;
        return m.DistMm;
    }
}
