using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// P8-BL-1 (WL-9, EA_F11): the war-load run carries an armoury. On day 1 of <see cref="Year"/> the richest fief treasury of the region orders a full levy's kits (960, four pieces each, 2.1 t of bar iron) from the makers in lots of
/// <c>LotKits</c>; two seasons later the pieces delivered are read, with the iron bar and linen cloth prices at the median book before and after. Nothing is seeded: the lord pays from his own treasury. Hung on
/// <see cref="WarLoadSoak.AtDayStart"/>; the result is the WL-9 row of <see cref="WarGates"/>.
/// </summary>
public sealed class KitProbe
{
    public int Year { get; init; } = 3;
    public int Kits { get; init; } = 960;
    public bool Placed { get; private set; }
    public bool Read { get; private set; }
    public int Pieces { get; private set; }
    public long Delivered { get; private set; }
    public int Lots { get; private set; }
    public long BudgetMites { get; private set; }
    public long FundedMites { get; private set; }
    public int BarPm0 { get; private set; }
    public int BarPm1 { get; private set; }
    public int ClothPm0 { get; private set; }
    public int ClothPm1 { get; private set; }
    public string Note { get; private set; } = "";
    public int LotsOpen { get; private set; }
    public int LotsTaken { get; private set; }
    public int InMaking { get; private set; }
    public int OnRoad { get; private set; }
    public long RestockedKg { get; private set; }
    private int _placedDay;

    private static int Median(WarLoadRun run, string good)
    {
        ushort g = run.Econ.Goods.IdOf(good); var all = new List<int>();
        foreach (var b in run.Economy.Markets.AllBooks()) if (b.Merchants.Count > 0) all.Add(b.MidRatioPermille(g));
        all.Sort(); return all.Count == 0 ? 0 : all[all.Count / 2];
    }

    public void AtDay(int year, int dayOfYear, WarLoadRun run)
    {
        if (!Placed && year == Year && dayOfYear == 1) Place(run);
        else if (Placed && !Read && run.DayNow >= _placedDay + 2 * TimeConst.DaysPerSeason) ReadOut(run);
    }

    private void Place(WarLoadRun run)
    {
        Placed = true; var st = run.State; var led = run.Econ.Ledger; var region = st.Population.Region;
        var supply = ArmsPlay.Build(st, st.Content, create: true); if (supply == null) { Note = "no arms service"; return; }
        ulong lord = 0; long best = -1;
        foreach (var f in region.Fiefs) if (led.TryFind(PurseKind.Treasury, f.Id, out var p) && led.Balance(p).Mites > best) { best = led.Balance(p).Mites; lord = f.Id.Value; }
        if (lord == 0) { Note = "no fief treasury"; return; }
        var depot = ArmsPlay.DepotFor(st, lord, 0); if (depot == null) { Note = "no depot"; return; }
        // the realm funds the order: the other fiefs' treasuries lend their share (four fifths of what they hold, richest first) until the lord can pay the list price; coin only moves, nothing is seeded
        long need = supply.ListPrice(Kits) - led.Balance(depot.Purse).Mites;
        foreach (var f in region.Fiefs.Where(x => x.Id.Value != lord).OrderByDescending(x => led.TryFind(PurseKind.Treasury, x.Id, out var tp) ? led.Balance(tp).Mites : 0))
        {
            if (need <= 0 || !led.TryFind(PurseKind.Treasury, f.Id, out var tr)) break;
            long give = Math.Min(need, led.Balance(tr).Mites * 8 / 10);
            if (give > 0 && led.Transfer(tr, depot.Purse, new Coin(give), TransferReason.Subsidy)) { need -= give; FundedMites += give; }
        }
        _placedDay = run.DayNow; BarPm0 = Median(run, "iron_bar"); ClothPm0 = Median(run, "cloth_linen");
        long budget = Math.Min(supply.ListPrice(Kits), led.Balance(depot.Purse).Mites * 8 / 10); BudgetMites = budget;
        var res = supply.PlaceOrderLots(depot, Kits, budget, _placedDay);
        Pieces = res.Ordered.Values.Sum(); Lots = st.WarSupply!.Arms.Jobs.Count;
        Note = Pieces == 0 ? "the treasury could not pay for any piece" : "";
    }

    private void ReadOut(WarLoadRun run)
    {
        Read = true; Delivered = run.State.WarSupply?.Arms.SupplyStats[ArmsSupply.StatDelivered] ?? 0;
        BarPm1 = Median(run, "iron_bar"); ClothPm1 = Median(run, "cloth_linen");
        var arms = run.State.WarSupply?.Arms;
        if (arms != null)
        {
            LotsOpen = arms.Jobs.Count; LotsTaken = arms.Jobs.Values.Count(j => j.Maker != 0);
            InMaking = arms.Transit.Values.Count(t => t.State == ArmsSupply.Making); OnRoad = arms.Transit.Values.Count(t => t.State == ArmsSupply.OnRoad);
        }
    }

    /// <summary>The WL-9 row: kits delivered within two seasons of the order, of the pieces ordered (4 a kit).</summary>
    public GateResult Result()
    {
        if (!Placed) return new GateResult("WL-9", AuditState.Info, "the run ended before the year of the kit order");
        if (Pieces == 0) return new GateResult("WL-9", AuditState.Info, "no kit order was placed: " + Note);
        if (!Read) return new GateResult("WL-9", AuditState.Info, "the run ended inside the two seasons after the order");
        long pm = Delivered * 1000 / Pieces;
        string line = $"{Kits} levy kits ({Pieces} pieces, {Lots} lots, budget {BudgetMites:N0} mites, {FundedMites:N0} of it lent by the other fiefs' treasuries) ordered on day 1 of year {Year}: {Delivered} pieces delivered after two seasons = {pm} permille; lots still open {LotsOpen} ({LotsTaken} taken by a maker), loads in the making {InMaking}, on the road {OnRoad}; iron bar at the median book {BarPm0} to {BarPm1} permille of its reference, linen cloth {ClothPm0} to {ClothPm1}";
        if (pm >= 1000) return new GateResult("WL-9", AuditState.Pass, line);
        return new GateResult("WL-9", pm >= WarGates.Wl9RatchetPm ? AuditState.Info : AuditState.Fail, $"RATCHET (D11, at {WarGates.Wl9RatchetPm} permille, it only rises): " + line);
    }
}
