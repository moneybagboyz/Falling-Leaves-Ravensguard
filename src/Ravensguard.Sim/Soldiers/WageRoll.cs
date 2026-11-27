using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Soldiers;

/// <summary>One line of a lord's wage roll: the soldier, his role, his wage a day and what he is owed, in mites.</summary>
public sealed record WageLine(ulong Person, string Role, long WageMites, long ArrearsMites);

/// <summary>
/// P7-SC-B (soldiers-and-companies 5 and the Army tab's wage roll): the wage rows of a lord's soldiers, paid by ledger transfers from the lord's purse to each man's household purse (reason Wage); what the purse cannot pay a
/// day is the man's arrears, paid down when there is coin again. Coin only moves between purses. Men in a warband are paid by the armies' own day (<c>skip</c>), so nobody is paid twice.
/// </summary>
public static class WageRoll
{
    /// <summary>The wage in mites a day: the role's copper wage (1 cp = 10 mites); a levy within owed service has none.</summary>
    public static long WageMites(SoldierWorld sw, string role) => (long)sw.Roles.WageCp(role) * 10;

    public static List<WageLine> Rows(SoldierWorld sw, long lord) =>
        [.. sw.Book.Rows.Values.Where(r => r.Lord == lord).Select(r => new WageLine(r.Person, r.Role, WageMites(sw, r.Role), r.ArrearsMites))];

    public static long DailyMites(SoldierWorld sw, long lord) => Rows(sw, lord).Sum(l => l.WageMites);

    /// <summary>
    /// The day's pay. Each man (in person order) is paid his wage while the lord's purse holds it; a wage it cannot meet is added to his arrears. When the whole day was paid, arrears are paid down, a day's wage a man at most.
    /// Returns the mites paid.
    /// </summary>
    public static long PayDay(SoldierWorld sw, long lord, PurseId from, Ledger led, Func<ulong, bool>? skip = null, IReadOnlyList<SoldierRow>? lordRows = null)
    {
        long paid = 0; bool short_ = false;
        var rows = lordRows ?? [.. sw.Book.Rows.Values.Where(r => r.Lord == lord)];          // lordRows: the lord's men in person order, grouped once by the day's tick
        foreach (var r in rows.Where(r => skip == null || !skip(r.Person)))
        {
            long wage = WageMites(sw, r.Role); if (wage <= 0) continue;
            var to = sw.PurseOf(r.Person);
            if (to.IsNone) continue;                                                  // P7R-8: a man with no purse cannot be paid and is owed nothing: he blocks nobody's arrears
            if (!from.IsNone && to.Index == from.Index) continue;                     // a man of the lord's own household: his wage is his own purse
            if (!from.IsNone && led.Balance(from).Mites >= wage && led.Transfer(from, to, new Coin(wage), TransferReason.Wage)) paid += wage;
            else { r.ArrearsMites += wage; short_ = true; }
        }
        if (!short_ && !from.IsNone)
            foreach (var r in rows.Where(r => r.ArrearsMites > 0 && (skip == null || !skip(r.Person))))
            {
                long pay = Math.Min(Math.Min(WageMites(sw, r.Role), r.ArrearsMites), led.Balance(from).Mites); if (pay <= 0) break;
                var to = sw.PurseOf(r.Person);
                if (!to.IsNone && to.Index == from.Index) { r.ArrearsMites = 0; continue; }
                if (!to.IsNone && led.Transfer(from, to, new Coin(pay), TransferReason.Wage)) { r.ArrearsMites -= pay; paid += pay; }
            }
        return paid;
    }
}
