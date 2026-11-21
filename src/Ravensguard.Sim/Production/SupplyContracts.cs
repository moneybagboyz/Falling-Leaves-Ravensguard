using Ravensguard.Content;

namespace Ravensguard.Sim.Production;

/// <summary>What a delivery's carting costs (economies-of-scale 4.4): the vehicle chosen, how many, the crew and team's pay, the tolls, the whole, and the load.</summary>
public readonly record struct TripCost(string Vehicle, int Vehicles, long CrewAndTeamMites, long TollMites, long LoadG, bool HalfEmpty)
{
    public long TotalMites => CrewAndTeamMites + TollMites;
    /// <summary>Milli-mites per kilogram carried (1,044 is 1.04 a kg).</summary>
    public long MilliMitesPerKg => LoadG <= 0 ? 0 : TotalMites * 1_000_000 / LoadG;
}

/// <summary>Bulk carting, pure rules (P7-ES2, economies-of-scale 4.4): <c>trip_cost = days x (crew wages + draught fodder at book price) + tolls x vehicles + wear</c>, <c>cost_per_kg = trip_cost / load</c>. Integer arithmetic only.</summary>
public static class Carting
{
    /// <summary>The minutes of a round trip over <paramref name="distanceM"/>: two legs at the vehicle's speed, each leg rounded up to the minute.</summary>
    public static long RoundTripMinutes(long distanceM, int speedMPerH) => distanceM <= 0 ? 0 : 2 * ((distanceM * 60 + speedMPerH - 1) / speedMPerH);

    /// <summary>A vehicle's day: the crew's wage plus the team's hay and oats at the given prices (mites a kg). ES-10: a cart with one horse, 150 + 8 kg of hay x 12 + 2.5 kg of oats x 30 = 321.</summary>
    public static long DayMites(ScaleVehicleRow v, long hayMitesPerKg, long oatsMitesPerKg)
        => v.WageMitesPerDay + ((long)Math.Round(v.HayKgPerDay * 1000) * hayMitesPerKg + (long)Math.Round(v.OatsKgPerDay * 1000) * oatsMitesPerKg + 500) / 1000;

    /// <summary>One vehicle's round trip in mites: <c>minutes / 1,440 x the day's cost</c>, rounded half up (a game day is 24 hours of 60 minutes).</summary>
    public static long TripMites(long roundTripMinutes, long dayMites) => (roundTripMinutes * dayMites + 720) / 1440;

    /// <summary>Milli-mites a kilogram of a full load: <c>(trip + tolls) x 1,000 / load kg</c> (ES-10: 1,044, 937, 645 for the cart, the ox wagon and the four-horse wagon over 8 km).</summary>
    public static long MilliPerKg(long tripMites, long tollMites, int loadKg) => (tripMites + tollMites) * 1000 / loadKg;

    /// <summary>The cheapest way to move <paramref name="loadG"/> over <paramref name="distanceM"/>: for each vehicle the convoy of <c>ceil(load / its load)</c> of them, at the book's feed prices and a toll a pass (two a round trip); ties go to the lower row. Same place or no load: nothing to pay.</summary>
    public static TripCost Plan(ScaleSettingsDef s, long distanceM, long loadG, long hayMitesPerKg, long oatsMitesPerKg, long tollPerPass, bool crossesGate)
    {
        if (distanceM <= 0 || loadG <= 0 || s.Vehicles.Count == 0) return new TripCost("", 0, 0, 0, loadG, false);
        TripCost best = default; long bestTotal = long.MaxValue;
        foreach (var v in s.Vehicles)
        {
            long cap = (long)v.LoadKg * 1000; int n = (int)((loadG + cap - 1) / cap);
            long minutes = RoundTripMinutes(distanceM, Math.Max(1, v.SpeedMPerH));
            long crew = n * TripMites(minutes, DayMites(v, hayMitesPerKg, oatsMitesPerKg)), toll = crossesGate ? n * 2 * tollPerPass : 0;
            if (crew + toll >= bestTotal) continue;
            bestTotal = crew + toll;
            best = new TripCost(v.Id, n, crew, toll, loadG, loadG * 1000 < (long)n * cap * s.HalfEmptyPm);
        }
        return best;
    }

    /// <summary>Whether a load is under the half-empty line of its vehicle (4.4: under 60% of capacity is flagged).</summary>
    public static bool HalfEmpty(ScaleSettingsDef s, long loadG, int loadKg) => loadG * 1000 < (long)loadKg * 1000 * s.HalfEmptyPm;
}

/// <summary>Supply contracts, pure rules (4.3): the price fixed at signing and the payment on delivery.</summary>
public static class Contracts
{
    /// <summary>The signing price: the seller's book mid times <c>1,000 + (Trading_seller - Trading_buyer) / 2</c> permille, within the band (50) of the mid. ES-8: equal Trading is the mid itself.</summary>
    public static long PriceMicro(long midMicro, int tradingSeller, int tradingBuyer, int bandPm)
        => midMicro * (1000 + Math.Clamp((tradingSeller - tradingBuyer) / 2, -bandPm, bandPm)) / 1000;

    /// <summary>What a delivery of <paramref name="qty"/> at <paramref name="priceMicro"/> a unit pays, in mites (half up). ES-8: 1,000 kg of charcoal at a mid of 12 a kg is 12,000.</summary>
    public static long PayMites(long qty, long priceMicro) => (qty * priceMicro + 500_000) / 1_000_000;

    /// <summary>What a delivery's price is worth a season in mites (the test a contract in piece goods must pass: 10 gp).</summary>
    public static long SeasonMites(long qty, long priceMicro, int everyDays, int daysPerSeason) => PayMites(qty, priceMicro) * Math.Max(1, daysPerSeason / Math.Max(1, everyDays));
}
