namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>
/// The named ratchets of the balance pass (decision D11): a calibration or balance gate that the honest fix could not meet ships as a measured floor that may only ever be raised. Each constant says the gate it stands for, the target, what was
/// measured when it was set and where. A ratchet is never lowered; when a later part measures better, the new figure replaces the old one in the same commit.
/// </summary>
public static class BalanceRatchets
{
    /// <summary>BL-2 (EA_F18): of five seeds (1234, 42, 7, 1, 99), eight game years of the harness's L3 iron works, the number whose capital need is four times the bill or less. Target 3. Measured at P8-BL-1 with the 30-day terms on charcoal and ore: 1 of 5 (capital need 8.1, 2.1, 5.2, 10.2 and 9.9 times the bill on seeds 1234, 42, 7, 1 and 99; the same four seeds run without terms on the build before the last changes: 10.2, 4.1, 6.1 and 16.4). Barrels stay cash: on terms the wood works in IN-20 stopped earning in its last quarter.</summary>
    public const int Bl2SeedsWithinFourTimesTheBill = 1;

    /// <summary>BL-5 (EA_F11, WL-9): of 960 levy kits' pieces ordered in lots by a lord with the coin, the permille delivered inside two seasons. Target 1000 (every piece). Measured at P8-BL-1: 610 (2,344 of 3,840 pieces, in 48 lots of 20 kits, the hero as the lord with 12 million mites, seed 1234 after 20 days); the war-load world's treasuries could not fund the order and its books had no bar to sell, 0 (WarGates.Wl9RatchetPm).</summary>
    public const int Bl5KitsDeliveredPerMille = 600;

    /// <summary>
    /// BL-1 (EA_F25): of twelve settling runs of the L1 clay works (settling 52 to 74 days, every second day, seed 1234, the clay works alone), the number that repay inside IN-20's 100 to 200 days with the last 120 days still earning. Target 10.
    /// Before P8-BL-1 (bricks at 1.2 mites a kilogram, a bill of 76,800): 0 of 12, nine never. The brick price was tried at 1.5 (2 of 12), 1.6 (5) and 1.7 (2, ten paying back in under 100 days) and put back at 1.2: at 1.6 the clay works in IN-20's own
    /// three-works run repays in 49 days (its floor is 100), the founders' sheet (NpcSheet, 175 to 190 a day) and IN-22's iron sheet follow the base price, and one run's mean of -400 to +1,300 a day on a bill of 80,000 is wider than the band is. What was changed is the
    /// clay chain's building labour (800 to 450 hours: a bill of 66,300 at the book), which the shift of IN-20's world after the carry-overs asked for (clay 215 days, over the 200). The ratchet is the measure at the final code.
    /// </summary>
    // P8-EA-1 raised it to 1 (one of twelve in band; settle 74: 204 days was the next nearest): the moulding shop and the kiln pay the unskilled class their bricks are priced at (EA8_F12).
    public const int Bl1ClayInBandOfTwelve = 1;

    /// <summary>BL-3 (OV-6): the furthest of the seven steps of <see cref="Bl3OvSixTests.Steps"/> the hero's run has reached (works, claim, upgrade placed, builder commissioned, L2, caravan quote, payback). Target 7. Measured at P8-BL-1: 7 (the claim of 8 acres, the L2 smeltery 11 days after the builder was commissioned, a quote to the nearest town, the payback read).</summary>
    public const int Bl3StepsReached = 7;

    /// <summary>
    /// BL-6 (phase-6 G2, D13): the 100-year soak's bounds, asserted when the run reaches 70 game years. Soil jobs finished in each decade after year 60 (target and bound: some, 1), delivery contracts a carter finished over the run (target and bound: some, 1), a decade's famines against the
    /// decade before (target and bound: not rising, 100 percent, with two entries of slack), and households' growth over the population's in percentage points: target 10; the bound is 26, the figure the phase-6 close-out measured at year 100 (households +42 percent against population
    /// +16) because the cottage rule of P8-BL-1 did not move it by year 44 of the run (+28 percent against +4: 24 points; at year 25 +21 against +1, as before). The century was not run to its end at P8-BL-1 (a game year costs five minutes from year 40: the dead person rows of P8-PF-2): the first full run replaces the 26
    /// with what it measures and may only lower it.
    /// </summary>
    public const int Bl6SoilJobsFilledPerLateDecade = 1, Bl6HouseholdPointsOverPopulation = 26, Bl6DeliveriesTaken = 1, Bl6FamineDecadeGrowthPct = 100;
}
