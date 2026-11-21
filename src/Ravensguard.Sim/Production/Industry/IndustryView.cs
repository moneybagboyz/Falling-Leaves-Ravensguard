using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>What holds a works back (industries 6): a stage that stopped, a link that cannot carry what the stage upstream makes, or the stage that runs furthest under what it could make.</summary>
public enum BottleneckKind : byte { None, Stopped, Hauling, Slowest }

/// <summary>One input of a stage: what its store holds against what a day asks for, and whether the chain itself supplies it.</summary>
public sealed record InputView(string Good, bool Fuel, long StockG, long PerDayG, int CoverDaysTenths, bool FromChain);

/// <summary>A good in a stage's store (grams, or pieces).</summary>
public sealed record StockView(string Good, long Qty, bool Piece);

/// <summary>
/// The upgrade row of a stage (industries 5.3, 6): the next tier's plan, what it costs, what the premises still lack, the crew band and how many more hands it needs, what it would add a day and what that repays at today's prices. The payback is an estimate:
/// the extra output priced at the gate's net price of the stage's main good, less the extra wages of the hands the band asks for (the books' per-unit repricing of the whole lot, not a flat price).
/// </summary>
public sealed record UpgradeView(int Tier, string Plan, long BillMites, IReadOnlyList<string> Missing, int CrewMin, int CrewMax, int HireMore, long ExtraGPerDay, long ExtraNetPerDayMites, int PaybackDays, bool InHand);

/// <summary>A good's way to market from a stage: the gate (net per kg after carting, the books' absorption, the days the stock needs to clear).</summary>
public sealed record SellGood(string Good, long StockG, string GateTown, long NetPerKgMilli, long AbsorbPerDay, long DaysToClear, bool Over);

/// <summary>The stage's own caravan quote beside the gate's for the same load (5.10): the parts, the margin, the days, the risk, and what the gate would pay for it.</summary>
public sealed record CaravanLine(bool Ok, string Why, string Dest, int Wagons, int Guards, int Days, long GrossMites, long CostMites, long RiskMites, long MarginMites, long GateNetMites, int PriceAgeDays, bool Away,
    IReadOnlyList<(string Key, long Mites)> Parts);

/// <summary>A stage as the tab shows it (<c>PlanX</c> and <c>PlanY</c>: where the one-click upgrade puts the next plan, two metres east of the premises, level with their north edge): the box of the flow, the detail and the sell lines.</summary>
public sealed record StageView(ulong Business, string KindId, string Name, int Tier, int TierCount, int Have, int Need, int CrewTarget, int CrewMin, int CrewMax, int WageOfferPm, long WagePerDayMites,
    long MadeG, long PotentialG, StageStop Stop, string StopGood, int StopDays, bool ShortHanded, bool Handling, bool Piece, Routing Route, bool HasOrder, bool HasSite, int PlanX, int PlanY, IReadOnlyList<StockView> Stock,
    IReadOnlyList<InputView> Inputs, UpgradeView? Upgrade, IReadOnlyList<SellGood> Sell, CaravanLine? Caravan);

/// <summary>The link into stage <see cref="To"/>: its length, what carries, what it can move in a day (<c>-1</c>: the crews' own hands, unlimited) and what moved on the last day.</summary>
public sealed record LinkView(int From, int To, int DistanceM, int Carts, int Wagons, long CapG, long MovedG, bool Short);

/// <summary>A balance row: the columns Income, Wages, Inputs, Upkeep, Royalties and rents (mites); the net is derived.</summary>
public sealed record BalanceRow(long Income, long Wages, long Inputs, long Upkeep, long Royalties)
{
    public long Net => Income - Wages - Inputs - Upkeep - Royalties;
    public static BalanceRow Of(WorkStage s, int today, int days) => new(s.Sum(WorkStage.Income, today, days), s.Sum(WorkStage.Wages, today, days), s.Sum(WorkStage.Inputs, today, days), s.Sum(WorkStage.Upkeep, today, days), s.Sum(WorkStage.Royalties, today, days));
    public static BalanceRow operator +(BalanceRow a, BalanceRow b) => new(a.Income + b.Income, a.Wages + b.Wages, a.Inputs + b.Inputs, a.Upkeep + b.Upkeep, a.Royalties + b.Royalties);
}

/// <summary>A works on the tab: the chain's stages and links, the bottleneck, the balance sheet by stage for the last works day and the 6-day week, the payback and the selling tally.</summary>
public sealed record WorksView(ulong Id, string Chain, string Place, int Settlement, int LastDay, IReadOnlyList<StageView> Stages, IReadOnlyList<LinkView> Links,
    BottleneckKind Bottleneck, int BottleneckStage, int BottleneckLink, IReadOnlyList<BalanceRow> StageDay, IReadOnlyList<BalanceRow> StageWeek, BalanceRow Day, BalanceRow Week,
    long BuildingsCostMites, long CumulativeNetMites, int PaidPm, int PaybackDays, bool Closed, bool Warning, int CaravanTrips, int CaravansLost, long CaravanNetMites)
{
    /// <summary>The works' working days that make a week (the 6-day working week of the sheets).</summary>
    public const int WeekDays = 6;
    public long NetDay => Day.Net;
}

/// <summary>The Industry tab as of a barrier: the hero's works (industries 6). Built by <see cref="IndustryViews.Build"/>; the screen only draws it and sends <c>in:</c> commands through the hero desk.</summary>
public sealed record IndustryView(int Day, IReadOnlyList<WorksView> Works)
{
    public bool IsEmpty => Works.Count == 0;
}

/// <summary>Builds <see cref="IndustryView"/> from the industry world (a read: nothing here changes the world, spends a stream or touches a store).</summary>
public static class IndustryViews
{
    /// <summary>The Industry tab's one unit (Q14): silver with one decimal ("67.9 sp"), now <see cref="MoneyText"/>'s <see cref="MoneyStyle.OneUnit"/> (P7-CN2).</summary>
    public static string Silver(long mites) => MoneyText.Format(mites, MoneyStyle.OneUnit);

    public static IndustryView Build(IndustryWorld iw, int owner, int day, PriceNotebook? notes = null)
    {
        var list = new List<WorksView>();
        foreach (var w in iw.Book.Works)
            if (w.Owner == owner && !w.Closed && w.Stages.Count > 0 && iw.BusinessOf(w.Stages[0]) != null) list.Add(Works(iw, w, day, notes));
        return new IndustryView(day, list);
    }

    private static WorksView Works(IndustryWorld iw, Works w, int day, PriceNotebook? notes)
    {
        int last = -1; foreach (var s in w.Stages) last = Math.Max(last, s.LastDay);
        int today = last >= 0 ? last : day;
        var stages = new List<StageView>(); var dayRows = new List<BalanceRow>(); var weekRows = new List<BalanceRow>();
        BalanceRow d = new(0, 0, 0, 0, 0), wk = d;
        for (int i = 0; i < w.Stages.Count; i++)
        {
            var s = w.Stages[i];
            stages.Add(Stage(iw, w, i, day, notes));
            var dr = last >= 0 ? BalanceRow.Of(s, today, 1) : new BalanceRow(0, 0, 0, 0, 0); var wr = last >= 0 ? BalanceRow.Of(s, today, WorksView.WeekDays) : dr;
            dayRows.Add(dr); weekRows.Add(wr); d += dr; wk += wr;
        }
        var links = new List<LinkView>();
        foreach (var l in w.Links)
        {
            long cap = StageMath.LinkCapacityG(iw.Prod.Content.Industries.Settings, l); if (cap == long.MaxValue) cap = -1;
            long made = l.From >= 0 && l.From < stages.Count ? stages[l.From].MadeG : 0;
            long moved = l.From >= 0 && l.From < w.Stages.Count ? iw.Status(w.Stages[l.From].Business)?.MovedG ?? 0 : 0;
            links.Add(new LinkView(l.From, l.To, l.DistanceM, l.Carts, l.Wagons, cap, moved, cap >= 0 && made > 0 && cap < made));
        }
        var (kind, bs, bl) = Bottleneck(stages, links);
        long net60 = 0; int span = 1;
        if (last >= 0) { foreach (var s in w.Stages) net60 += s.Net(today, WorkStage.RingDays); span = Math.Clamp(today - w.StartedDay + 1, 1, WorkStage.RingDays); }
        long mean = net60 / span;
        int payback = w.PaidBackDay >= 0 ? 0 : w.BuildingsCostMites > 0 && mean > 0 ? (int)Math.Min(int.MaxValue, w.BuildingsCostMites / mean) : -1;
        int paid = w.BuildingsCostMites > 0 ? (int)Math.Clamp(w.CumulativeNetMites * 1000 / w.BuildingsCostMites, 0, 1000) : 0;
        bool warn = stages.Any(s => s.Stop is not (StageStop.None or StageStop.Full) || s.ShortHanded);
        return new WorksView(w.Id, w.Chain, iw.Prod.Pop.Region.Settlements[w.Settlement].Name, w.Settlement, last, stages, links, kind, bs, bl, dayRows, weekRows, d, wk,
            w.BuildingsCostMites, w.CumulativeNetMites, paid, payback, w.Closed, warn, w.CaravanTrips, w.CaravansLost, w.CaravanNetMites);
    }

    /// <summary>
    /// The bottleneck (industries 6): a link that cannot carry what the stage upstream made comes first; then the earliest stage in chain order that stopped for a reason of its own (anything but a full store, which blames what lies downstream; a shortage of an
    /// input is the stage's own only when nothing upstream stopped first, which chain order gives); then, with nothing stopped, the stage furthest under what it could make.
    /// </summary>
    public static (BottleneckKind Kind, int Stage, int Link) Bottleneck(IReadOnlyList<StageView> stages, IReadOnlyList<LinkView> links)
    {
        int firstStop = -1;
        for (int i = 0; i < stages.Count; i++) if (stages[i].Stop is not (StageStop.None or StageStop.Full)) { firstStop = i; break; }
        for (int k = 0; k < links.Count; k++)
            if (links[k].Short && (firstStop < 0 || links[k].From < firstStop)) return (BottleneckKind.Hauling, links[k].To, k);
        if (firstStop >= 0) return (BottleneckKind.Stopped, firstStop, -1);
        int slow = -1; long worst = 1000;
        for (int i = 0; i < stages.Count; i++)
        {
            var s = stages[i]; if (s.Handling || s.PotentialG <= 0) continue;
            long ratio = s.MadeG * 1000 / s.PotentialG;
            if (ratio < worst) { worst = ratio; slow = i; }
        }
        return slow >= 0 ? (BottleneckKind.Slowest, slow, -1) : (BottleneckKind.None, -1, -1);
    }

    private static StageView Stage(IndustryWorld iw, Works w, int idx, int day, PriceNotebook? notes)
    {
        var s = w.Stages[idx]; var k = iw.KindOf(s); var b = iw.BusinessOf(s)!; var st = iw.Status(s.Business); var cat = iw.Prod.Econ.Goods;
        var t = k.Tier(s.Tier);
        string Name(ushort g) => g < cat.Count ? cat.Def(g).Name : "?";
        var stop = st?.Stop ?? s.Stop; ushort stopGood = st?.StopGood ?? s.StopGood;
        long wage = (long)RecipeBook.WagePerDay(k.Wage) * s.WageOfferPm / 1000;
        long made = st?.MadeG ?? 0, pot = st?.PotentialG ?? 0;
        var stock = new List<StockView>();
        foreach (var g in k.Goods().Distinct()) { long q = b.Store.Qty(g); if (q > 0) stock.Add(new StockView(Name(g), q, cat.IsPiece(g))); }
        // the inputs: what a day of the stage's potential (or, idle, of its tier's capacity) asks for, against the store
        var inputs = new List<InputView>();
        long basis = pot > 0 ? pot : Math.Max(made, t.CapacityG > 0 ? t.CapacityG : t.RateG * Math.Max(1, t.CrewMin));
        foreach (var (good, pm, fuel) in k.Inputs)
        {
            long per = basis * pm / 1000, have = b.Store.Qty(good);
            bool chain = idx > 0 && w.Stages.Take(idx).Any(p => iw.KindOf(p).Outputs.Any(o => o.Good == good) || iw.KindOf(p).Main == good);
            inputs.Add(new InputView(Name(good), fuel, have, per, per <= 0 ? 0 : (int)Math.Min(9999, have * 10 / per), chain));
        }
        var routeOut = IndustryWorld.OutputsOf(k);
        var sell = new List<SellGood>();
        if (iw.Prod.Markets != null && (idx == w.Stages.Count - 1 || s.Route != Routing.Next))
            foreach (var g in routeOut)
            {
                var gv = iw.ViewGate(s.Business, g);
                sell.Add(new SellGood(Name(g), b.Store.Qty(g), gv.Settlement >= 0 && gv.Settlement < iw.Prod.Pop.Region.Settlements.Count ? iw.Prod.Pop.Region.Settlements[gv.Settlement].Name : "", gv.NetPerKgMilli, gv.AbsorbPerDay, gv.DaysToClear, gv.OverAbsorption));
            }
        CaravanLine? car = null;
        if (s.Order != null) car = Caravan(iw, w, s, b, day, notes);
        return new StageView(s.Business, k.Id, k.Name, s.Tier, k.TierCount, st?.Have ?? b.Staff.Count, st?.Need ?? t.CrewMin, Math.Clamp(s.CrewTarget, t.CrewMin, t.CrewMax), t.CrewMin, t.CrewMax, s.WageOfferPm, wage,
            made, pot, stop, stopGood != ushort.MaxValue ? Name(stopGood) : "", s.StopDays, st?.ShortHanded ?? false, k.IsHandling, cat.IsPiece(k.Main), s.Route, s.Order != null, s.HasSite, s.SiteX1 + 2, s.SiteY0, stock, inputs, Upgrade(iw, w, s, k, t, st, wage, b), sell, car);
    }

    private static UpgradeView? Upgrade(IndustryWorld iw, Works w, WorkStage s, StageKind k, StageTier t, StageStatus? st, long wage, Business b)
    {
        if (iw.NextUpgrade(s) is not { } next) return null;
        var nt = k.Tier(next.Tier);
        int pct = iw.SliderPct();
        long Cap(StageTier x) => (x.CapacityG > 0 ? x.CapacityG : x.RateG * x.CrewMax) * pct / 100;
        long extra = Math.Max(0, Cap(nt) - Cap(t));
        int have = st?.Have ?? b.Staff.Count;
        long perKg = k.IsHandling || iw.Prod.Markets == null ? 0 : iw.ViewGate(s.Business, k.Outputs.Length > 0 ? k.Outputs[0].Good : k.Main).NetPerKgMilli;
        long extraIncome = extra * perKg / 1_000_000;                                    // grams x milli-mites a kilogram -> mites
        long extraWages = Math.Max(0, nt.CrewMin - Math.Max(have, t.CrewMax)) * wage;
        long extraNet = extraIncome - extraWages;
        long bill = iw.PlanBill(next.Plan);
        int payback = extraNet > 0 && bill > 0 ? (int)Math.Min(int.MaxValue, (bill + extraNet - 1) / extraNet) : -1;
        return new UpgradeView(next.Tier, next.Plan, bill, iw.Missing(s, next.Tier), nt.CrewMin, nt.CrewMax, Math.Max(0, nt.CrewMin - have), extra, extraNet, payback, s.UpgradeTo == next.Tier);
    }

    private static CaravanLine Caravan(IndustryWorld iw, Works w, WorkStage s, Business b, int day, PriceNotebook? notes)
    {
        bool away = iw.Caravans.Away(s.Business);
        var q = iw.Caravans.Quote(s.Business, s.Order, day, notes);
        var o = s.Order!;
        var reg = iw.Prod.Pop.Region;
        string dest = o.Kind == DestKind.Town && o.Dest >= 0 && o.Dest < reg.Settlements.Count ? reg.Settlements[o.Dest].Name : "#" + o.Dest.ToString(CultureInfo.InvariantCulture);
        if (!q.Ok) return new CaravanLine(false, q.Why, dest, o.Wagons, o.Guards, 0, 0, 0, 0, 0, 0, 0, away, []);
        long gate = 0;
        foreach (var (g, qty) in q.Manifest) gate += iw.QuoteGate(w, s, g, qty).NetMites;
        return new CaravanLine(true, "", dest, q.Wagons, q.Guards, q.Days, q.GrossMites, q.CostsMites, q.RiskMites, q.MarginMites, gate, q.PriceAgeDays, away, [.. q.Parts()]);
    }
}
