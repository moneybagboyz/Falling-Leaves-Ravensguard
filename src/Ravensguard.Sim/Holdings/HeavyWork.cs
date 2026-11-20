using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Holdings;

/// <summary>
/// Fatigue and food for heavy work (holdings 3.8, HW-9). Building and felling burn food at one and a half times the day's ordinary measure and wear a body out: a worker's heavy day is
/// <see cref="DayCapMilli"/> milli-hours (eight hours); he eats at noon, <see cref="FoodGPerHour"/> grams of food for each hour of the day, from his pack or the site's store, and if there is
/// nothing to eat he works on only until <see cref="HungryUntilHour"/> in the afternoon. The state is the day's heavy hours and whether the noon meal was eaten (saved in the "holdings" section).
/// </summary>
public static class HeavyWork
{
    /// <summary>The ordinary measure is 1,000 g of food a day, 42 g an hour; heavy work takes 1.5 times that, 63 g an hour of the 8-hour day.</summary>
    public const int FoodGPerHour = 63, DayHours = 8;
    public const long DayCapMilli = DayHours * 1000L;
    public const int NoonHour = 12, HungryUntilHour = 14;

    /// <summary>What the hero may work of the seconds asked, and why not when nothing (a text key).</summary>
    public readonly record struct Limit(long UntilSec, string? Why);

    /// <summary>
    /// The end of the stretch the hero can work from <paramref name="fromSec"/> to <paramref name="untilSec"/> today: at most the day's cap of heavy hours, and if the stretch reaches noon the meal
    /// is eaten first (<paramref name="siteStore"/> is the food the site keeps, if any); a hungry worker stops at two in the afternoon.
    /// </summary>
    public static Limit Begin(HeroContext ctx, HoldingsBook book, long fromSec, long untilSec, Store? siteStore)
    {
        int day = (int)(fromSec / 86400);
        if (book.HeavyDay != day) { book.HeavyDay = day; book.HeavyMilli = 0; book.Fed = false; book.Hungry = false; }
        long noon = day * 86400L + NoonHour * 3600L;
        if (untilSec > noon && !book.Fed && !book.Hungry)
        {
            if (Eat(ctx, DayHours * FoodGPerHour, siteStore)) book.Fed = true; else book.Hungry = true;
        }
        long until = untilSec;
        if (book.Hungry) until = Math.Min(until, day * 86400L + HungryUntilHour * 3600L);
        long left = Math.Max(0, DayCapMilli - book.HeavyMilli) * 3600 / 1000;
        until = Math.Min(until, fromSec + left);
        if (until <= fromSec) return new Limit(fromSec, book.Hungry && fromSec >= day * 86400L + HungryUntilHour * 3600L ? "hw_too_hungry" : "hw_too_tired");
        return new Limit(until, null);
    }

    /// <summary>Counts the seconds the hero worked into the day's heavy hours.</summary>
    public static void End(HoldingsBook book, long workedSec) => book.HeavyMilli += workedSec * 1000 / 3600;

    /// <summary>Eats <paramref name="grams"/> of food from the hero's pack, then the site's store (anything with food in it, the lowest good first); false when there is not enough (what was there is eaten).</summary>
    public static bool Eat(HeroContext ctx, long grams, Store? siteStore)
    {
        var e = ctx.Econ; long left = grams;
        foreach (var store in new[] { ctx.Pack, siteStore })
        {
            if (store == null) continue;
            foreach (var (g, q) in store.Rows().ToList())
            {
                if (left <= 0) break;
                if (e.Goods.Def(g).FoodMeasuresPerKg <= 0 || e.Goods.IsPiece(g)) continue;
                long take = Math.Min(left, q);
                if (take > 0 && store.Destroy(g, take, DestroyReason.Consumed)) left -= take;
            }
        }
        return left <= 0;
    }
}
