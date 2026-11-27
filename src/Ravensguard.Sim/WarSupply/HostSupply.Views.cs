namespace Ravensguard.Sim.WarSupply;

public sealed partial class HostSupply
{
    /// <summary>
    /// P8-WS-1 (the Army tab): thousandths of a day of staple the host's baggage holds against the day's need (the same sum <see cref="Supply"/> writes as <c>DaysOfFoodMilli</c>, read from the stores, so a loaded game shows what a
    /// live one does). A read: nothing moves.
    /// </summary>
    public long BaggageDaysMilli(HostId h, int day)
    {
        var a = Acc(h); var rt = Resolve(a); var need = NeedsOf(a, day);
        long staples = 0; foreach (var g in _stapleGoods) staples += EqG(g, rt.Baggage.Qty(g));
        return need.StapleG > 0 ? staples * 1000 / need.StapleG : 0;
    }
}
