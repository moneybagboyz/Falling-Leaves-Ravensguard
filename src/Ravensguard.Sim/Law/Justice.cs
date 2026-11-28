namespace Ravensguard.Sim.Law;

/// <summary>
/// The day of the law (crime-and-justice 4.9) after the crime core's own: hour 0 the prisoners are fed and those who broke out are written down; hour 6 (<c>CarryOutHour</c>) the sentences due are carried
/// out in the square; hour 8 (<c>CourtHour</c>) on a court day the held are tried; hour 10 the guards on the day shift take the known (stream 0x615). The runtime's <see cref="LawLink"/> calls it at each hour
/// of the day, and so can a test.
/// </summary>
public static class Justice
{
    /// <summary>The hour of the day the guards go out for the known.</summary>
    public const int ArrestHour = 10;

    /// <summary>Whether the law has anything to do at this hour of the day (the link skips the rest).</summary>
    public static bool Acts(LawWorld w, int hourOfDay) => hourOfDay == 0 || hourOfDay == w.S.CarryOutHour || hourOfDay == w.S.CourtHour || hourOfDay == ArrestHour;

    public static void OnHour(LawWorld w, CrimeBook cb, JusticeBook jb, long hour)
    {
        int hod = (int)(hour % 24), day = (int)(hour / 24); long sec = hour * 3600;
        if (hod == 0) { using (Ravensguard.Sim.Diagnostics.PfClock.Span("law.escapes")) Gaol.Escapes(w, cb, jb, sec); using (Ravensguard.Sim.Diagnostics.PfClock.Span("law.feed")) Gaol.Feed(w, cb, jb, day, sec); }
        if (hod == w.S.CarryOutHour) using (Ravensguard.Sim.Diagnostics.PfClock.Span("law.carryout")) Court.CarryOut(w, cb, jb, day, sec);
        if (hod == w.S.CourtHour && Court.IsCourtDay(w, day)) using (Ravensguard.Sim.Diagnostics.PfClock.Span("law.sit")) Court.Sit(w, cb, jb, day, sec);
        if (hod == ArrestHour) using (Ravensguard.Sim.Diagnostics.PfClock.Span("law.arrests")) Gaol.Daily(w, cb, jb, day, sec);
    }
}
