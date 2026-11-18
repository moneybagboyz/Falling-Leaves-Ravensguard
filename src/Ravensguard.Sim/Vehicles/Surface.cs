namespace Ravensguard.Sim.Carts;

/// <summary>What the wheel (or foot) runs on, from the tile and the last day's weather (horses-and-carts spec 4.5). The order is the order of the content tables.</summary>
public enum Surface : byte { Paved, DryRoad, WetRoad, Track, Grass, MudRoad, DeepMud, PackedSnow, DeepSnow, Ice, Sand }

/// <summary>The kind of ground a tile is, before weather.</summary>
public enum Ground : byte { Paved, Road, Track, Field, Sand, FordApproach }

/// <summary>
/// The weather a surface depends on: rain in the last 24 game hours (tenths of a millimetre), snow lying (cm), whether it is freezing, and whether snow is melting. All integers,
/// so <see cref="SurfaceModel.Classify"/> is a pure function the live tier and the abstract caravans both call.
/// </summary>
public readonly record struct WetState(int Rain24Tenths, int SnowCm, bool Frozen, bool Thaw)
{
    public static readonly WetState Dry = default;
}

public static class SurfaceModel
{
    public static readonly int Count = Enum.GetValues<Surface>().Length;
    public const int WetRoadTenths = 20, MudTenths = 100, DeepMudTenths = 300, PackedSnowCm = 2, DeepSnowCm = 15;

    /// <summary>The surface of a tile of <paramref name="g"/> under <paramref name="w"/>: snow and ice first, then rain on dirt (2 mm wets a road, 10 mm turns it to mud, 30 mm turns a field to deep mud).</summary>
    public static Surface Classify(Ground g, in WetState w)
    {
        if (g == Ground.Paved) return w.SnowCm >= PackedSnowCm ? (w.SnowCm >= DeepSnowCm ? Surface.DeepSnow : Surface.PackedSnow) : (w.Frozen && w.Rain24Tenths >= WetRoadTenths ? Surface.Ice : Surface.Paved);
        if (w.SnowCm >= DeepSnowCm) return w.Thaw ? DeepMudOrMud(g) : Surface.DeepSnow;
        if (w.SnowCm >= PackedSnowCm) return w.Thaw ? DeepMudOrMud(g) : Surface.PackedSnow;
        if (w.Frozen && w.Rain24Tenths >= WetRoadTenths) return Surface.Ice;
        int rain = w.Rain24Tenths;
        return g switch
        {
            Ground.Road => rain >= MudTenths ? Surface.MudRoad : rain >= WetRoadTenths ? Surface.WetRoad : Surface.DryRoad,
            Ground.Track => rain >= MudTenths ? Surface.MudRoad : Surface.Track,
            Ground.Field => rain >= DeepMudTenths ? Surface.DeepMud : rain >= MudTenths ? Surface.MudRoad : Surface.Grass,
            Ground.Sand => Surface.Sand,
            _ => rain >= MudTenths ? Surface.DeepMud : Surface.Track,    // the approach to a ford
        };
    }

    private static Surface DeepMudOrMud(Ground g) => g is Ground.Field or Ground.FordApproach ? Surface.DeepMud : Surface.MudRoad;

    /// <summary>The plan's speed multiplier (permille) for people and riders on foot or hoof: rain on roads x0.7, mud x0.6, snow x0.6 to x0.3 (no force model, spec 4.5).</summary>
    public static int PersonSpeedPm(Surface s) => s switch
    {
        Surface.Paved or Surface.DryRoad or Surface.Grass => 1000, Surface.Track => 900, Surface.WetRoad => 700, Surface.MudRoad => 600, Surface.DeepMud => 500,
        Surface.PackedSnow => 600, Surface.DeepSnow => 300, Surface.Ice => 600, _ => 800,
    };

    /// <summary>How hard the surface is on shoes: 10 = one metre of road is one metre of wear; mud and rough ground 20, grass 2.</summary>
    public static int ShoeWearTenths(Surface s) => s switch { Surface.Grass => 2, Surface.MudRoad or Surface.DeepMud or Surface.Sand or Surface.DeepSnow => 20, _ => 10 };

    /// <summary>True for a hard surface (a shoeless hoof is lamed on it).</summary>
    public static bool IsHard(Surface s) => s is Surface.Paved or Surface.DryRoad or Surface.WetRoad or Surface.Track or Surface.Ice;
}

/// <summary>Who tries to cross a river at a ford (spec 4.7).</summary>
public enum Crosser : byte { Walker, Rider, Wagon }

public static class Fords
{
    /// <summary>The ford is closed to wagons from a river high for one day, to riders from two, to walkers from three (a bridge is always open).</summary>
    public static bool IsOpen(Crosser who, int riverHighDays) => riverHighDays < ClosedFromDays(who);
    public static int ClosedFromDays(Crosser who) => who switch { Crosser.Wagon => 1, Crosser.Rider => 2, _ => 3 };
    /// <summary>A frozen river carries walkers and riders from 12 cm of ice and a cart from 20 cm.</summary>
    public static bool IceCarries(Crosser who, int iceCm) => iceCm >= (who == Crosser.Wagon ? 20 : 12);
}
