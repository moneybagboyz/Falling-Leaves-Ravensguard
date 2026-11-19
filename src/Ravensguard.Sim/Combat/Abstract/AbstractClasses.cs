using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Abstract;

// abstract-battles-v1 3.1: the small integer classes the wound tables are keyed by. Append only (the generated table names its rows by these).

/// <summary>Weapon class (3.1): the spec's nine, plus the couched lance (<see cref="Lance"/>, a mounted rider's own rows: mounted-combat 5) and the wooden club and staff
/// (<see cref="Club"/>: split from hafted, whose metal heads wound twice as often; with one class for both the militia's clubs struck like maces and equivalence A failed).</summary>
public enum WeaponClass : byte { Blade, Dagger, Axe, Hafted, Polearm, Longbow, Crossbow, Thrown, Unarmed, Lance, Club }

/// <summary>Armour class (3.1): the nearest of seven by the protection index (none 0 .. plate 1000).</summary>
public enum ArmourClass : byte { None, Cloth, Padded, LeatherMailLight, Mail, MailPlate, Plate }

/// <summary>What one attack action did (3.2), in rising order of harm. <see cref="Miss"/>: dodged, parried, blocked clean or missed.</summary>
public enum OutcomeClass : byte { Miss, Nothing, Light, Serious, Grave, Severed, Instant }

/// <summary>
/// The classes of a soldier (3.1) and of a blow's outcome (3.2), and the representative kits the table builder fights with. The protection index is
/// <see cref="RiskEstimate.ProtectionPm"/> (the area-weighted cover of the worn layers by hardness), computed once per armour set and cached.
/// </summary>
public static class AbstractClasses
{
    public const int WeaponClasses = 11, ArmourClasses = 7, Bands = 7, Outcomes = 7;
    /// <summary>The skill-delta bands: -60 .. +60 in steps of 20 (3.1).</summary>
    public static int BandDelta(int band) => -60 + 20 * band;

    public static bool IsRanged(WeaponClass c) => c is WeaponClass.Longbow or WeaponClass.Crossbow or WeaponClass.Thrown;

    /// <summary>The range bands of the shooting rows (the skill-delta slot of a ranged row): their centres in metres.</summary>
    public static readonly int[] RangeBandM = [6, 15, 25, 40, 60, 80, 110];

    /// <summary>The band of a range: the nearest centre at or above it (the last band beyond).</summary>
    public static int RangeBand(int metres)
    {
        for (int i = 0; i < RangeBandM.Length - 1; i++) if (metres <= (RangeBandM[i] + RangeBandM[i + 1]) / 2) return i;
        return RangeBandM.Length - 1;
    }

    public static string Name(WeaponClass c) => Flavour.FlavourBook.Snake(c.ToString());
    public static string Name(ArmourClass c) => Flavour.FlavourBook.Snake(c.ToString());
    public static string Name(OutcomeClass c) => Flavour.FlavourBook.Snake(c.ToString());

    /// <summary>The armour groups of the wound-template library: one per armour class (a hit's class sets how bad it is; the armour keeps what it lets through: a cut under cloth,
    /// a crushed bone under mail). Grouping the soft classes together lowered a blade's pain per hit by a sixth and moved equivalence A by twenty points of win rate.</summary>
    public const int Groups = ArmourClasses;
    public static int Group(ArmourClass a) => (int)a;
    public static string GroupName(int g) => Name((ArmourClass)g);

    /// <summary>The weapon class of a weapon shape (3.1): blade (swords, falchion), dagger, axe, hafted (mace, hammer, maul, flail), club (club, staff), polearm (spear, pike, javelin).</summary>
    public static WeaponClass Of(WeaponShapeDef s) => s.Family switch
    {
        "blade" => WeaponClass.Blade, "dagger" => WeaponClass.Dagger, "axe" => WeaponClass.Axe, "polearm" or "lance" => WeaponClass.Polearm,
        "hafted" => s.Id is "club" or "quarterstaff" or "lance_stub" ? WeaponClass.Club : WeaponClass.Hafted,
        "ranged" => s.Id == "crossbow" ? WeaponClass.Crossbow : WeaponClass.Longbow, _ => s.Id == "lance" ? WeaponClass.Polearm : WeaponClass.Unarmed,
    };

    /// <summary>The melee class of a kit (the weapon in hand; fists without one).</summary>
    public static WeaponClass MeleeOf(FightContent c, FighterKit k)
    {
        if (k.Weapon == null || !c.T.HasMaterial(k.Weapon.MaterialId)) return WeaponClass.Unarmed;
        try { return Of(c.T.Weapon(k.Weapon.ShapeId, k.Weapon.MaterialId).Shape); }
        catch (KeyNotFoundException) { return WeaponClass.Unarmed; }
    }

    /// <summary>The shooting class of a kit (a launcher with ammunition), or null.</summary>
    public static WeaponClass? ShooterOf(FightContent c, FighterKit k) =>
        k.Launcher != null && k.Ammo != null && c.Launchers.TryGetValue(k.Launcher, out var l) && c.Ammo.ContainsKey(k.Ammo) ? (l.Shape == "crossbow" ? WeaponClass.Crossbow : WeaponClass.Longbow) : null;

    // ------------------------------------------------------------------ armour

    private sealed class ProtCache { public readonly Dictionary<string, int> ByKey = new(StringComparer.Ordinal); public int[]? ClassPm; }
    private static readonly ConditionalWeakTable<CombatTablesV2, ProtCache> Cache = new();

    private static string KeyOf(IReadOnlyList<WornItem> worn) => string.Join('|', worn.Select(w => w.ShapeId + ":" + w.MaterialId + ":" + (int)w.Quality));

    /// <summary>The protection index of a worn set (0 none .. about 1000 a full harness), cached per (set, materials).</summary>
    public static int ProtectionPm(CombatTablesV2 t, IReadOnlyList<WornItem> worn, string plan = "human")
    {
        var cache = Cache.GetValue(t, _ => new ProtCache());
        string key = plan + "#" + KeyOf(worn);
        lock (cache)
        {
            if (cache.ByKey.TryGetValue(key, out int v)) return v;
            var p = t.Plan(plan);
            var ws = new List<WornV2>();
            foreach (var w in worn)
            {
                if (!t.HasMaterial(w.MaterialId)) continue;
                try { ws.Add(WornV2.Of(t.Armour(w.ShapeId, w.MaterialId), CombatTablesV2.QualityPm[(int)w.Quality])); } catch (KeyNotFoundException) { }
            }
            v = RiskEstimate.ProtectionPm(p, ws);
            cache.ByKey[key] = v;
            return v;
        }
    }

    /// <summary>The representative kit of each armour class (the table builder's defender): none, a tunic, a gambeson, leather and a mail coif, a mail hauberk, mail with plate, a full harness.</summary>
    public static WornItem[] Kit(ArmourClass a) => a switch
    {
        ArmourClass.None => [new("shirt", "linen")],
        ArmourClass.Cloth => [new("shirt", "linen"), new("tunic", "wool")],
        ArmourClass.Padded => [new("tunic", "linen"), new("gambeson", "linen")],
        ArmourClass.LeatherMailLight => [new("tunic", "wool"), new("cuir_bouilli", "leather"), new("leather_bracers", "leather"), new("mail_coif", "iron")],
        ArmourClass.Mail => [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")],
        ArmourClass.MailPlate => [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("breastplate", "steel"), new("helm", "steel"), new("vambraces", "steel")],
        _ => [.. FightCalibration.Plated.Worn],
    };

    /// <summary>The class of a worn set: the representative whose protection index is nearest (ties to the lighter).</summary>
    public static ArmourClass ArmourOf(CombatTablesV2 t, IReadOnlyList<WornItem> worn, string plan = "human")
    {
        var cache = Cache.GetValue(t, _ => new ProtCache());
        int[] cls;
        lock (cache) cls = cache.ClassPm ??= [.. Enumerable.Range(0, ArmourClasses).Select(i => -1)];
        for (int i = 0; i < ArmourClasses; i++) if (cls[i] < 0) cls[i] = ProtectionPm(t, Kit((ArmourClass)i));
        int pm = ProtectionPm(t, worn, plan);
        int best = 0;
        for (int i = 1; i < ArmourClasses; i++) if (Math.Abs(cls[i] - pm) < Math.Abs(cls[best] - pm)) best = i;
        return (ArmourClass)best;
    }

    /// <summary>The weapons a melee class's rows are built from, round-robin, weighted as the kits carry them (the road groups' iron swords, axes and spears and oak clubs; a steel blade, a mace, a pike now and then).</summary>
    public static WeaponInstance[] Members(WeaponClass c) => c switch
    {
        WeaponClass.Blade => [new("arming_sword", "iron"), new("arming_sword", "iron"), new("arming_sword", "steel"), new("falchion", "iron"), new("arming_sword", "iron"), new("longsword", "steel")],
        WeaponClass.Dagger => [new("dagger", "iron")],
        WeaponClass.Axe => [new("axe", "iron"), new("axe", "iron"), new("hand_axe", "iron"), new("axe", "steel")],
        WeaponClass.Hafted => [new("mace", "iron"), new("mace", "steel"), new("war_hammer", "steel"), new("mace", "iron"), new("flail", "iron"), new("maul", "iron")],
        WeaponClass.Club => [new("club", "oak"), new("club", "oak"), new("club", "oak"), new("quarterstaff", "ash")],
        WeaponClass.Polearm => [new("spear", "iron"), new("spear", "iron"), new("spear", "steel"), new("spear", "iron"), new("spear", "iron"), new("pike", "steel")],
        WeaponClass.Thrown => [new("javelin", "iron")],
        WeaponClass.Lance => [new("lance", "steel")],
        _ => [new("fist", "bone")],
    };

    // ------------------------------------------------------------------ outcomes (3.2)

    /// <summary>
    /// The class of one blow's wounds on a plan (3.2): none landed is <see cref="OutcomeClass.Nothing"/> (armour held, a bruise is the same); instant: the heart or the brain, or a
    /// concussing head blow; severed: a limb or the head taken; grave: an artery or vein, an organ, a displaced or shattered bone, a nerve or the spine, a bleed over 30 ml/s or pain
    /// over 60; serious: torn or mangled muscle, a cracked or broken bone, a tendon or ligament, a bleed of 2 ml/s or more or pain of 20 or more; light: the rest.
    /// </summary>
    public static OutcomeClass Classify(BodyPlanV2 plan, ReadOnlySpan<WoundV2> wounds)
    {
        if (wounds.Length == 0) return OutcomeClass.Nothing;
        var worst = OutcomeClass.Nothing;
        long bleed = 0, pain = 0;
        foreach (var w in wounds)
        {
            var cat = WoundBook.Categorise(plan, w);
            bleed += w.BleedX10;
            pain += (long)WoundBook.PainOf(plan, w, cat) * (plan.FeelsPain ? plan.Parts[w.Part].PainPm : 0) / 1000;
            OutcomeClass c;
            if (w.Has(WoundFlags.Vital) || w.Has(WoundFlags.Concussion)) c = OutcomeClass.Instant;
            else if (w.Has(WoundFlags.Severed) && cat == WoundCat.Severed) c = OutcomeClass.Severed;
            else if (w.Has(WoundFlags.ArteryHit) || w.Has(WoundFlags.VeinHit) || w.Has(WoundFlags.OrganHit) || w.Organ >= 0 || w.Has(WoundFlags.Displaced) || w.Has(WoundFlags.Compound)
                || w.Has(WoundFlags.NerveHit) || w.Has(WoundFlags.MotorNerveHit) || w.Has(WoundFlags.SpineHit)
                || cat == WoundCat.Bone && w.Bone >= BoneGrade.Displaced) c = OutcomeClass.Grave;
            else if (cat is WoundCat.Bone or WoundCat.Tendon or WoundCat.Ligament || cat == WoundCat.Muscle && w.Muscle >= MuscleGrade.Torn || w.Has(WoundFlags.Severed)) c = OutcomeClass.Serious;
            else c = OutcomeClass.Light;
            if (c > worst) worst = c;
        }
        if (worst < OutcomeClass.Grave && (bleed > 300 || pain > 60)) worst = OutcomeClass.Grave;
        if (worst < OutcomeClass.Serious && (bleed >= 20 || pain >= 20)) worst = OutcomeClass.Serious;
        return worst;
    }

    /// <summary>The body region of a part for the limb mix (3.2): 0 arm, 1 leg, 2 torso, 3 head, 4 neck, 5 hand.</summary>
    public static int Region(BodyPlanV2 plan, int part)
    {
        var p = plan.Parts[part];
        if (p.IsHosted && p.Host >= 0) p = plan.Parts[p.Host];
        string b = p.BaseId;
        if (b.Contains("hand") || b.Contains("finger") || b.Contains("thumb")) return 5;
        if (b.Contains("arm") || b.Contains("shoulder")) return 0;
        if (b.Contains("leg") || b.Contains("foot") || b.Contains("knee")) return 1;
        if (b == "neck") return 4;
        if (b == "head") return 3;
        return 2;
    }

    /// <summary>The kind of a grave wound for the fx mix (3.2): 0 fracture, 1 nerve/tendon/ligament, 2 organ, 3 artery or vein; -1 not one of these.</summary>
    public static int Fx(BodyPlanV2 plan, ReadOnlySpan<WoundV2> wounds)
    {
        int best = -1;
        foreach (var w in wounds)
        {
            int f = w.Has(WoundFlags.ArteryHit) || w.Has(WoundFlags.VeinHit) ? 3 : w.Organ >= 0 || w.Has(WoundFlags.OrganHit) ? 2
                : w.Has(WoundFlags.NerveHit) || w.Has(WoundFlags.MotorNerveHit) || w.Has(WoundFlags.SpineHit) || w.Has(WoundFlags.TendonHit) || w.Has(WoundFlags.LigamentHit) ? 1
                : w.Has(WoundFlags.BoneBroken) || w.Has(WoundFlags.Displaced) || WoundBook.Categorise(plan, w) == WoundCat.Bone ? 0 : -1;
            best = Math.Max(best, f);
        }
        return best;
    }
}
