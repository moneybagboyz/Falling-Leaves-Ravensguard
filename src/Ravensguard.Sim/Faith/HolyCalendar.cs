using Ravensguard.Content;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// The holy days as the people of the live tier read them (faith-and-church 4.1): a mask per continent culture index of the days of the 60-day year that the culture keeps as feasts (bit <c>doy - 1</c>), so a
/// farmer of the Vale does not work at Martinmas. The saints' days come from the history and the derived religion, which the live tier does not hold; they reach the abstract tier (services, the feast's
/// larder) through <see cref="Religion.HolyDays"/>.
/// </summary>
public static class HolyCalendar
{
    /// <summary>The feast mask of each of the 256 continent culture indexes (0 where the culture has none).</summary>
    public static ulong[] FeastMasks(ContentDatabase db)
    {
        var masks = new ulong[256];
        for (int c = 0; c < 256; c++)
        {
            var cu = ReligionBook.CultureOf(db, c); if (cu == null) continue;
            foreach (var f in cu.FeastDays) masks[c] |= 1UL << (ReligionBook.DayOfYear(f) - 1);
        }
        return masks;
    }
}
