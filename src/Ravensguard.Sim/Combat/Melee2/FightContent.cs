using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// Everything a fight reads from content, built once per content database: the v2 tables, the tuning row (<c>combat/calibration.jsonc</c>, <c>melee_tuning</c>), launchers,
/// ammunition, improvised objects and stances, and the synthetic weapons made from them (an arrow head, a stool) through <see cref="CombatTablesV2.Synthetic"/>.
/// </summary>
public sealed class FightContent
{
    public CombatTables Tables { get; }
    public CombatTablesV2 T => Tables.V2;
    public MeleeTuningDef K { get; }
    public IReadOnlyDictionary<string, LauncherDef> Launchers { get; }
    public IReadOnlyDictionary<string, AmmoDef> Ammo { get; }
    public IReadOnlyDictionary<string, ImprovisedDef> Improvised { get; }
    public IReadOnlyDictionary<string, CombatStanceDef> Stances { get; }
    public ContentDatabase Db { get; }

    public FightContent(CombatTables tables, ContentDatabase db)
    {
        Tables = tables; Db = db;
        K = db.All<MeleeTuningDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MeleeTuningDef { Id = "default" };
        Launchers = db.All<LauncherDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        Ammo = db.All<AmmoDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        Improvised = db.All<ImprovisedDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        var st = db.All<CombatStanceDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        if (!st.ContainsKey("balanced")) st["balanced"] = new CombatStanceDef { Id = "balanced" };
        Stances = st;
    }

    public CombatStanceDef Stance(string id) => Stances.TryGetValue(id, out var s) ? s : Stances["balanced"];

    /// <summary>The arrow or bolt head as a weapon (point, edge or blunt; its momentum comes from the launcher).</summary>
    public WeaponV2 AmmoWeapon(AmmoDef a)
    {
        int density = T.Material(a.HeadMaterial).DensityKgM3;
        var shape = new WeaponShapeDef
        {
            Id = "ammo." + a.Id, Name = a.Name, Family = "ammo", ReachMm = 1, StrikeTicks = 1, HeadVolumeCm3 = density <= 0 ? 1 : a.MassG * 1000.0 / density,
            Attacks = [new AttackDef { Id = "shot", Name = a.Name, Verb = "strikes", Type = a.Attack, ContactArea = a.ContactArea, PenetrationMm = a.PenetrationMm, Velocity = 1.0 }],
        };
        return T.Synthetic(shape, a.HeadMaterial);
    }

    /// <summary>The improvised object as a weapon (melee-and-ranged 4.6): area min(60, volume^(2/3) x 2) unless given, velocity 0.7, wind-up 0.45 + 0.1 x kg s.</summary>
    public WeaponV2 ImprovisedWeapon(ImprovisedDef d)
    {
        long c = FightMath.ICbrt((long)(d.VolumeCm3 * 1_000_000));                 // cube root x 100
        double area = d.ContactArea > 0 ? d.ContactArea : Math.Min(60, c * c * 2 / 10000);
        long kgMilli = (long)(d.VolumeCm3 * T.Material(d.Material).DensityKgM3 / 1000);
        double wind = (K.ImprovisedWindupMs + K.ImprovisedWindupPerKgMs * kgMilli / 1000) / 1000.0;
        var shape = new WeaponShapeDef
        {
            Id = "improvised." + d.Id, Name = d.Name, Family = "improvised", ReachMm = 900, StrikeTicks = 2, HeadVolumeCm3 = d.VolumeCm3, WindUpS = wind, RecoverS = wind,
            Attacks = [new AttackDef { Id = "swing", Name = d.Name, Verb = d.Attack == AttackType.Blunt ? "hits" : d.Attack == AttackType.Point ? "jabs" : "hacks", Type = d.Attack, ContactArea = area,
                PenetrationMm = d.PenetrationMm, Velocity = K.ImprovisedVelocityPm / 1000.0 }],
        };
        return T.Synthetic(shape, d.Material);
    }
}
