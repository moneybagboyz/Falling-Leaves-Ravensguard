using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Medicine;

/// <summary>One line of a physician's quote: the treatment, the part, the fee and the chance he gives with his skill.</summary>
public readonly record struct QuoteLine(TreatKind Kind, int Part, int FeeMites, int ChancePct);

/// <summary>
/// What a physician or barber-surgeon charges and what he will do (medicine 3, 4.4): the service of each <see cref="TreatKind"/>, the fee from <c>medicine/prices.jsonc</c> (a barber's at
/// <c>barber_share_pct</c>), and the success chances of the Phase 4 formulas. Pure; the quote screen of the hero and the abstract physic use the same numbers.
/// </summary>
public static class PhysicianFees
{
    /// <summary>The price id of a treatment: a pressure hold or a bandage is a visit.</summary>
    public static string ServiceOf(TreatKind k) => k switch
    {
        TreatKind.Pressure or TreatKind.Bandage or TreatKind.ReleasePressure => "visit",
        TreatKind.Cauterise => "clean",
        _ => MedicineRules.IdOf(k),
    };

    /// <summary>True when this sort of man does the treatment: a barber-surgeon only the services marked so.</summary>
    public static bool Offers(PhysicianKind who, TreatKind k) => who == PhysicianKind.Physician || MedicineRules.Current.Prices.TryGetValue(ServiceOf(k), out var p) && p.Barber;

    /// <summary>The fee for a treatment, mites (a splint's 150 is the work; the splint is his own).</summary>
    public static int Fee(PhysicianKind who, TreatKind k)
    {
        var m = MedicineRules.Current;
        if (!m.Prices.TryGetValue(ServiceOf(k), out var p)) return 0;
        return who == PhysicianKind.Barber ? p.Mites * m.Rules.BarberSharePct / 100 : p.Mites;
    }

    /// <summary>The fee of the visit itself (the look at the patient).</summary>
    public static int VisitFee(PhysicianKind who) => Fee(who, TreatKind.Bandage);

    public static int BedDayFee => MedicineRules.Current.Rules.BedDayMites;

    /// <summary>
    /// The chance of success of a treatment at a surgery skill, percent (wounds 4.3, 4.6): set bone <c>clamp(30 + 0.6 x skill - 10 x grade, 5, 95)</c>, tendon, ligament and organ
    /// <c>clamp(40 + skill - floor, 40, 95)</c>, cleaning <c>clamp(30 + 0.4 x skill (+20 with herbs), 20, 85)</c>; the rest do not fail. <paramref name="grade"/> is the fracture's grade.
    /// </summary>
    public static int ChancePct(TreatKind k, int skill, int grade = 1, bool herbs = false) => k switch
    {
        TreatKind.SetBone => Math.Clamp(30 + skill * 6 / 10 - 10 * grade, 5, 95),
        TreatKind.TendonRepair or TreatKind.LigamentRepair or TreatKind.OrganRepair => Math.Clamp(40 + skill - Treatment.MinSkill(k), 40, 95),
        TreatKind.Clean => Math.Clamp(30 + skill * 4 / 10 + (herbs ? 20 : 0), 20, 85),
        _ => 100,
    };
}
