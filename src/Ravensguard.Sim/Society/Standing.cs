using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Society;

/// <summary>The word a standing wears (reputation 6): Hated, Distrusted, Unknown, Known, Respected, Honoured.</summary>
public enum StandingBand : byte { Hated, Distrusted, Unknown, Known, Respected, Honoured }

/// <summary>
/// The one function everything reads (reputation 4.2, overview 8.2): <see cref="Of"/> gives a person's standing with a group in permille, and the readers below turn it into what prices, hiring, guards,
/// courts, petitions, marriages and the church do with it (linear, clamped). Nobody keeps a private reputation. Pure integer arithmetic, no draw.
/// </summary>
public static class Standing
{
    /// <summary>Prices move this many permille at -+1000 with the settlement: +-10% (4.2).</summary>
    public const int PriceSwingPm = 100;
    /// <summary>Hiring and recruiting are refused below this with the settlement; recruits' starting loyalty adds standing/20 (4.2).</summary>
    public const int HireRefusedBelow = -300, LoyaltyDivisor = 20;
    /// <summary>A person below this with the fief is watched in town (followed 20 m); the church refuses weddings, burial and absolution below the church floor and excommunicates at -800 if sins go on (4.2).</summary>
    public const int WatchedBelow = -500, ChurchRefusesBelow = -500, ExcommunicationAt = -800;
    /// <summary>A standing of this much with the fief lowers a sentence one step (4.2, the mercy step).</summary>
    public const int MercyAtLeast = 300;
    public const int WatchedFollowM = 20;

    /// <summary>A person's standing with a group, -1000..1000. An outlaw's standing with the realm is -1000 for as long as the mark lasts (4.3).</summary>
    public static int Of(StandingBook? book, EntityId person, GroupRef group)
    {
        if (book == null) return 0;
        if (group.Kind == GroupKind.Realm && book.Marks.IsOutlaw(person, group.Index)) return -1000;
        return book.Value(person, group);
    }

    public static StandingBand BandOf(int pm) => pm switch
    {
        <= -500 => StandingBand.Hated, <= -150 => StandingBand.Distrusted, < 150 => StandingBand.Unknown, < 400 => StandingBand.Known, < 700 => StandingBand.Respected, _ => StandingBand.Honoured,
    };

    /// <summary>The loc key of a band's label.</summary>
    public static string BandKey(StandingBand b) => "st_band_" + b.ToString().ToLowerInvariant();

    /// <summary>Market prices (4.2): the hero pays <c>base x (1 - standing/10000)</c> buying (10% less at +1000) and is paid <c>base x (1 + standing/10000)</c> selling.</summary>
    public static long PriceMites(long baseMites, int standingWithSettlementPm, bool buying)
    {
        int s = Math.Clamp(standingWithSettlementPm, -1000, 1000) * PriceSwingPm / 1000;     // permille of the price, -100..100
        return baseMites * (1000 + (buying ? -s : s)) / 1000;
    }

    /// <summary>Hiring and recruiting are refused below -300 with the settlement (4.2).</summary>
    public static bool HiringRefused(int standingWithSettlementPm) => standingWithSettlementPm < HireRefusedBelow;

    /// <summary>A recruit's starting loyalty gains standing/20 (4.2).</summary>
    public static int LoyaltyBonus(int standingPm) => standingPm / LoyaltyDivisor;

    /// <summary>The guards follow a person below -500 with the fief in town (4.2).</summary>
    public static bool Watched(int standingWithFiefPm) => standingWithFiefPm < WatchedBelow;

    /// <summary>An outlaw of the realm is attacked on sight (4.2, 4.3).</summary>
    public static bool AttackedOnSight(StandingBook? book, EntityId person, int realm) => book != null && book.Marks.IsOutlaw(person, realm);

    /// <summary>The court's mercy step (crime 4.6): a standing of 300 or more with the fief lowers the sentence one step.</summary>
    public static int MercySteps(int standingWithFiefPm) => standingWithFiefPm >= MercyAtLeast ? 1 : 0;

    /// <summary>The utility term petitions, councils, marriages and fealty add (4.2): standing / 10.</summary>
    public static int UtilityTerm(int standingPm) => standingPm / 10;

    /// <summary>The church refuses weddings, burial and absolution below -500 with the religion (4.2).</summary>
    public static bool ChurchRefuses(int standingWithReligionPm) => standingWithReligionPm < ChurchRefusesBelow;

    /// <summary>The church excommunicates at -800 if sins continue (4.2); the faith step calls this when a new sin lands.</summary>
    public static bool ExcommunicationDue(int standingWithReligionPm) => standingWithReligionPm <= ExcommunicationAt;
}
