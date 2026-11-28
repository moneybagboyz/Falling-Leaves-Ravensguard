using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// [exploration] The fight's part of exploration-and-sites (P4-H): light and weather as situational points on the hit formula (4.8, overview Q24), the draugr's tirelessness
/// and the fire that undoes it (4.9, anatomy 4.3: the head burns through in about 30 sim-seconds), and a monster whose animating core is destroyed going down for good.
/// Everything here is off (zero, null) unless a site fight or a weather row switches it on, so other fights and their hashes are unchanged.
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The light (0..255) at a fight position (mm); null outside a site fight. A target in less light is harder to hit (4.8).</summary>
    public Func<int, int, int>? SiteLight { get; set; }
    /// <summary>Points on every attacker's skill delta from the place (bad air -10: 4.5.1), by fighter; null: none.</summary>
    public Func<Fighter, int>? SitePlacePoints { get; set; }
    /// <summary>The weather-in-combat row (4.8) for an outdoor fight; null underground and indoors.</summary>
    public WeatherCombatDef? WeatherRow { get; set; }

    private Dictionary<int, long>? _burning;
    private Dictionary<int, int>? _coreSeen;

    /// <summary>True for a creature that sees by its own senses (the draugr's 12 m, the cave beast's hearing): light does not change its aim.</summary>
    public static bool SeesInDark(Fighter f) => f.Plan.DarkSight;          // [P8-LM-3] the plan's trait: the draugr, the cave beast and the four more kinds

    /// <summary>The situational points of one blow (overview 8.4, Q24): the target's light band for an attacker who needs light, the place's own points, the fog's surprise.</summary>
    internal int SitePoints(Fighter a, Fighter t)
    {
        int p = 0;
        if (SiteLight != null && !SeesInDark(a)) p += Sites.SiteLight.LightPoints(SiteLight(t.X, t.Y));
        if (SitePlacePoints != null) p += SitePlacePoints(a);
        if (WeatherRow is { } w && Tick < 20 * 5 && w.SurprisePoints != 0) p += w.SurprisePoints;     // the first blows out of the fog
        p += WeatherPoints();                                                                          // P4 close-out: footing and grip (FightWorld.Weather.cs)
        return p;
    }

    /// <summary>Sets a part of a fighter alight (a torch thrust at a draugr's head); it burns until it is through.</summary>
    public bool Ignite(int fighter, int part)
    {
        if (fighter < 0 || fighter >= Fighters.Count || !Fighters[fighter].Active) return false;
        _burning ??= [];
        long key = (long)fighter << 8 | (uint)part;
        if (_burning.ContainsKey((int)key)) return false;
        _burning[(int)key] = 0;
        return true;
    }

    public bool Burning(int fighter) { if (_burning == null) return false; foreach (var k in _burning.Keys) if (k >> 8 == fighter) return true; return false; }

    /// <summary>[exploration] Each tick, before the vitals: a draugr never tires; a burning part burns on; an undone monster goes down for good.</summary>
    internal void SiteVitals(Fighter f)
    {
        if (f.Plan.Tireless) f.Stamina = K.StaminaMaxX100;
        if (_burning != null)
            foreach (var key in _burning.Keys.ToList())
            {
                if (key >> 8 != f.Id) continue;
                int part = key & 0xFF;
                long b = _burning[key];
                bool through = Burn.Step(f.Plan.Parts[part], T.Cal, ref b);
                _burning[key] = b;
                if (!through) continue;
                _burning.Remove(key);
                var w = new WoundV2 { Part = (short)part, Tissue = 0, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = 1000, Flags = WoundFlags.Burn };
                f.Book.Apply(f.Plan, [w], GameSec, Tick, 0xF1E0UL, 0, false);
                RefreshBody(f);
            }
        if (f.Plan.AnimatingCore >= 0 && (_coreSeen == null || !_coreSeen.TryGetValue(f.Id, out int seen) || seen != f.Book.Records.Count))
        {
            (_coreSeen ??= [])[f.Id] = f.Book.Records.Count;
            if (f.Book.Records.Count > 0 && f.Book.Function(f.Plan).Undone) Kill(f);
        }
    }

    /// <summary>The ranged sigma of the weather (4.8): x1.30 in heavy rain, x1.4 beyond 15 m in fog; permille.</summary>
    public int WeatherSigmaPm(long rangeMm) => WeatherRow is not { } w ? 1000 : w.SigmaBeyondM > 0 && rangeMm <= w.SigmaBeyondM * 1000L ? 1000 : w.SigmaPm;
}
