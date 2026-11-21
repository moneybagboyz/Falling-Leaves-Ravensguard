using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Production;

/// <summary>One recipe's costs as the Costs section shows them (economies-of-scale 9): the variable cost per unit (inputs at the book's ask, the batch's wages), the day's fixed cost, the unit cost at today's output and at the absorption cap, and what the markets in reach take.</summary>
public sealed record CostRow(string Recipe, ushort Good, long InputsMicroPerUnit, long WagesMicroPerUnit, long UnitsPerDay, long UnitCostMicroToday, long UnitCostMicroAtCap, long CapPerDay, long Held, bool Stops);

/// <summary>A station coupled to a machine (4.5): the labour permille it works at now, the kind's own, and why it is not powered (a content key such as <c>lb_frozen</c>; empty while it runs).</summary>
public sealed record MachineRow(int Station, string Kind, int LabourPm, int KindPm, string Cause);

/// <summary>A supply contract the business is a party to.</summary>
public sealed record ContractRow(int Id, string Other, bool Seller, string Good, long QtyPerDelivery, int EveryDays, long PriceMicro, int Delivered, int Missed, int NextDue, string State, bool Carter);

/// <summary>The business panel's Costs section (P7-ES2, economies-of-scale 9): a pure read, never saved.</summary>
public sealed record CostView(IReadOnlyList<CostRow> Recipes, IReadOnlyList<MachineRow> Machines, IReadOnlyList<ContractRow> Contracts, long FixedPerDayMites);

public static class CostViews
{
    /// <summary>The Costs view of a business: the fixed costs of a day it carries itself (appointed foremen, a licence's rent), each recipe's variable and unit costs, its machines and its contracts.</summary>
    public static CostView Of(ProductionSystem prod, ScaleBook? book, Business b, int day, Func<ulong, string>? nameOf = null)
    {
        var sc = prod.Content.Scale; var kind = prod.KindOf(b);
        var book0 = prod.Markets?.BookForSettlement(b.Settlement);
        long fixedDay = 0;
        if (book != null)
        {
            if (book.Foremen.TryGetValue(b.Id.Value, out var fm)) fixedDay += fm.Count * (long)sc.ForemanWageMitesPerDay;
            if (book.LicenceOf(b.Id.Value) != null) fixedDay += sc.LicenceSeasonMites / TimeConst.DaysPerSeason;
        }
        var rows = new List<CostRow>();
        foreach (var r in kind.Recipes)
        {
            ushort g = r.Outputs[0].Good; long outQ = Math.Max(1, r.Outputs[0].Qty);
            Int128 inputs = 0; foreach (var (ig, iq) in r.Inputs) inputs += (Int128)iq * (book0 == null ? prod.Econ.Goods.BasePriceMilliMites(ig) * (prod.Econ.Goods.IsPiece(ig) ? 1000 : 1) : book0.AskMicro(ig));
            Int128 wages = (Int128)r.WorkerHours * RecipeBook.WagePerHour(r.Wage) * Ravensguard.Sim.Economy.Markets.PriceCurve.Micro;
            int stations = 0; foreach (var s in b.Stations) if (s.Kind == r.Station) stations++;
            long perDay = Math.Max(1, stations) * 10 * outQ / Math.Max(1, r.WorkerHours);                       // a station's ten-hour day, one worker to a station
            long varUnit = (long)((inputs + wages) / outQ), fixedMicro = fixedDay * Ravensguard.Sim.Economy.Markets.PriceCurve.Micro;
            long cap = 0, held = 0; bool stops = false;
            if (book0 != null) { var av = prod.AbsorbView(b, r, book0); cap = av.CapPerDay; held = av.Held; stops = av.Stops; }
            rows.Add(new CostRow(r.Id, g, (long)(inputs / outQ), (long)(wages / outQ), perDay, FixedCosts.UnitCostMicro(fixedMicro, varUnit, perDay), cap > 0 ? FixedCosts.UnitCostMicro(fixedMicro, varUnit, cap) : varUnit, cap, held, stops));
        }
        var machines = new List<MachineRow>();
        if (book != null)
            foreach (var k in book.Couplings)
            {
                if (k.Business != b.Id.Value || (uint)k.Station >= (uint)b.Stations.Count) continue;
                int now = prod.StationLabourPm?.Invoke(b, k.Station) ?? kind.LabourPm;
                machines.Add(new MachineRow(k.Station, b.Stations[k.Station].Kind, now, kind.LabourPm, now >= kind.LabourPm ? prod.StationMachineWhy?.Invoke(b, k.Station) ?? "sc_machine_idle" : ""));
            }
        var contracts = new List<ContractRow>();
        if (book != null)
            foreach (var c in book.Contracts)
            {
                bool seller = c.Seller == b.Id.Value; if (!seller && c.Buyer != b.Id.Value) continue;
                ulong other = seller ? c.Buyer : c.Seller;
                contracts.Add(new ContractRow(c.Id, nameOf?.Invoke(other) ?? other.ToString(), seller, c.Good, c.QtyPerDelivery, c.EveryDays, c.PriceMicro, c.Delivered, c.Missed, c.NextDue, c.State.ToString(), seller == c.CarterIsSeller));
            }
        return new CostView(rows, machines, contracts, fixedDay);
    }
}
