using System.Diagnostics;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Abstract;

/// <summary>One game year of the headless soak (Phase 2 done-when, plan section 5).</summary>
public sealed class SoakRow
{
    public int Year;                       // 1-based
    public long Population;
    public double Under15Pct, Over50Pct;
    public long Births, Deaths, Emigrants, Marriages, HungerDeaths;
    public double FoodSurplusPct;          // (harvest - seed) / (people x 60 days x 1 measure) - 1
    public long HungryHouseholdDays;       // household-days with the hunger grievance (under one day of food at the daily slice)
    public double HungryPct;               // of household-days
    public int Famines;                    // settlement famines declared this year
    public double[] PriceIndex = [];       // per tracked staple: mean mid / base price over eligible book-days
    public double[] InBandPct = [];        // per staple: share of eligible book-days with mid / base in the band
    public double AtCapPct;                // all staples: share of eligible book-days with mid at 0.3 x or 4 x Ref
    public double BandPct;                 // all staples together
    public double WorkshopCompletionPct;  // open workshops (stations, not traders) that finished a batch this year
    public double MerchantsSolventPct;
    public long BreadT, AleKl;             // bread (tonnes) and ale (kilolitres) made this year
    public int Businesses, Closed;
    public int Employed, Idle;             // staff of businesses; working-age adults (16-59) with no occupation and not farming
    public long CoinTotal, CoinSources, CoinSinks, CoinInHouseholds, CoinInBusinesses;
    public bool CoinConserved, MassOk;
    public long MassViolations;
    public long Hired, Succeeded, ClosedNoOwner;
    public int Sheep, Oxen, Cows;
    public double MeanFertility;
}

/// <summary>
/// Runs a <see cref="HeadlessWorld"/> for years on a <see cref="WorldTicker"/> and takes the yearly table: the numbers the Phase 2 done-when asks for. Price
/// band: a book-day is in band when the staple's mid price is within <see cref="BandLow"/> and <see cref="BandHigh"/> permille of the content base price; the
/// eligible book-days of a staple are those of the books that have a merchant (the markets that are actually supplied: a village without a trader has no
/// stock and is outside the market the spec's band speaks about, it shows in the "no stock" count instead).
/// </summary>
public sealed class EconomySoak
{
    public const int BandLow = 600, BandHigh = 1800;
    public static readonly string[] Staples = ["wheat", "rye", "flour", "bread", "ale", "firewood", "salt"];

    public HeadlessWorld World { get; }
    public WorldTicker Ticker { get; }
    public List<SoakRow> Rows { get; } = [];
    public SimWorld Sim => World.World;
    private readonly ushort[] _staple;
    private readonly long[] _batchStart;
    private long _births0, _deaths0, _emig0, _marr0, _famine0, _hunger0;
    private long _hhDays, _hungryDays;
    private readonly long[] _bandN, _bandIn, _bandSum;
    private long _bandCap, _bandAll;
    private long _bread0, _ale0;
    private long _hired0, _succ0, _closedNo0;
    public long TotalHungryDays, TotalHouseholdDays, TotalHungryDaysAll, TotalHouseholdDaysAll;
    public readonly Stopwatch Clock = new();
    /// <summary>Hungry households at the end of each simulated day (a window of the whole run).</summary>
    public List<int> HungryByDay { get; } = [];

    public EconomySoak(HeadlessWorld w, AbstractSystemRegistry? registry = null, TickerOptions? options = null)
    {
        World = w; Ticker = w.NewTicker(registry, options);
        var cat = Sim.Economy.Goods;
        _staple = [.. Staples.Select(cat.IdOf)];
        _bandN = new long[_staple.Length]; _bandIn = new long[_staple.Length]; _bandSum = new long[_staple.Length];
        _batchStart = new long[w.Economy.Production.Businesses.Count];
        StartYear();
    }

    private void StartYear()
    {
        var v = Sim.Vital; var ew = World.Economy;
        _births0 = v.Births; _deaths0 = v.Deaths; _emig0 = v.Emigrants; _marr0 = v.Marriages; _hunger0 = v.DeathsByCause[(int)DeathCause.Hunger];
        _famine0 = Ticker.Counters.GetValueOrDefault("famines");
        _hhDays = _hungryDays = 0; _bandCap = _bandAll = 0;
        Array.Clear(_bandN); Array.Clear(_bandIn); Array.Clear(_bandSum);
        var bs = ew.Production.Businesses;
        if (_batchStart.Length < bs.Count) { }
        for (int i = 0; i < bs.Count && i < _batchStart.Length; i++) _batchStart[i] = bs[i].Batches;
        var cat = Sim.Economy.Goods; var prod = ew.Production.ProducedUnits;
        _bread0 = prod[cat.IdOf("bread")]; _ale0 = prod[cat.IdOf("ale")];
        _hired0 = ew.Labour.Hired; _succ0 = ew.Labour.Succeeded; _closedNo0 = ew.Labour.ClosedNoOwner;
    }

    /// <summary>Advances <paramref name="years"/> game years, sampling every day, and returns the rows added; <paramref name="onRow"/> is called as each year closes.</summary>
    public void Run(int years, Action<SoakRow>? onRow = null)
    {
        Clock.Start();
        for (int y = 0; y < years; y++)
        {
            for (int d = 0; d < TimeConst.DaysPerYear; d++)
            {
                Ticker.AdvanceHours(24);
                Sample();
            }
            TotalHungryDaysAll += _hungryDays; TotalHouseholdDaysAll += _hhDays;
            var row = CloseYear();
            Rows.Add(row); onRow?.Invoke(row);
            StartYear();
        }
        Clock.Stop();
    }

    /// <summary>End-of-day samples: hungry households and the staple prices of the supplied books.</summary>
    private void Sample()
    {
        var hht = Sim.Households; var e = Sim.Economy; var ew = World.Economy; int today = 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            _hhDays++;
            if ((e.Grievances[h] & Grievance.Hunger) != 0) { _hungryDays++; today++; }
        }
        HungryByDay.Add(today);
        foreach (var b in ew.Markets.AllBooks())
        {
            if (b.Merchants.Count == 0) continue;
            for (int i = 0; i < _staple.Length; i++)
            {
                var gb = b.Goods[_staple[i]];
                long mid = b.MidMicro(_staple[i]);
                int ratio = (int)(mid * 1000 / Math.Max(1, gb.BaseRefMicro));
                _bandN[i]++; _bandSum[i] += ratio; _bandAll++;
                if (ratio >= BandLow && ratio <= BandHigh) _bandIn[i]++;
                int m = b.MidRatioPermille(_staple[i]);
                if (m <= e.Money.PriceClampMinPermille + 5 || m >= e.Money.PriceClampMaxPermille - 5) _bandCap++;
            }
        }
        TotalHungryDays = _hungryDays; TotalHouseholdDays = _hhDays;
    }

    public SoakRow CloseYear()
    {
        var v = Sim.Vital; var ew = World.Economy; var e = Sim.Economy; var p = Sim.People; var cat = e.Goods;
        long day = Sim.State.HourDone / 24;
        var r = new SoakRow { Year = Rows.Count + 1 };
        long alive = 0, kids = 0, old = 0, idle = 0, sheep = 0;
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            alive++; int age = Sim.AgeOn(i, day);
            if (age < 15) kids++; else if (age >= 50) old++;
            if (age is >= 16 and < 60 && p.Occupation[i] == Occupation.None && !(Sim.Households.ParcelCount[Math.Max(0, p.Household[i])] > 0)) idle++;
        }
        r.Population = alive; r.Under15Pct = 100.0 * kids / Math.Max(1, alive); r.Over50Pct = 100.0 * old / Math.Max(1, alive); r.Idle = (int)idle;
        r.Births = v.Births - _births0; r.Deaths = v.Deaths - _deaths0; r.Emigrants = v.Emigrants - _emig0; r.Marriages = v.Marriages - _marr0;
        r.HungerDeaths = v.DeathsByCause[(int)DeathCause.Hunger] - _hunger0;
        r.Famines = (int)(Ticker.Counters.GetValueOrDefault("famines") - _famine0);
        var fs = ew.Farming.State;
        long need = alive * TimeConst.DaysPerYear * 1100L;
        r.FoodSurplusPct = need == 0 ? 0 : 100.0 * ((fs.HarvestG - fs.SeedSownG) - need) / need;
        r.HungryHouseholdDays = _hungryDays; r.HungryPct = 100.0 * _hungryDays / Math.Max(1, _hhDays);
        r.PriceIndex = new double[_staple.Length]; r.InBandPct = new double[_staple.Length];
        long inAll = 0;
        for (int i = 0; i < _staple.Length; i++)
        {
            r.PriceIndex[i] = _bandN[i] == 0 ? 0 : _bandSum[i] / 1000.0 / _bandN[i];
            r.InBandPct[i] = _bandN[i] == 0 ? 0 : 100.0 * _bandIn[i] / _bandN[i]; inAll += _bandIn[i];
        }
        r.BandPct = _bandAll == 0 ? 0 : 100.0 * inAll / _bandAll; r.AtCapPct = _bandAll == 0 ? 0 : 100.0 * _bandCap / _bandAll;
        int workshops = 0, done = 0, merchants = 0, solvent = 0, open = 0, closed = 0, staff = 0;
        var led = e.Ledger; long bizCoin = 0;
        var bs = ew.Production.Businesses;
        for (int i = 0; i < bs.Count; i++)
        {
            var b = bs[i]; var k = ew.Production.KindOf(b);
            if (b.State == BusinessState.Closed) { closed++; continue; }
            open++; staff += b.Staff.Count; long cash = led.Balance(b.Purse).Mites; bizCoin += cash;
            if (k.Trader) { merchants++; if (cash >= 1000) solvent++; }
            else if (k.Stations.Length > 0)
            {
                workshops++;
                long before = i < _batchStart.Length ? _batchStart[i] : 0;
                if (b.Batches > before) done++;
            }
        }
        r.WorkshopCompletionPct = workshops == 0 ? 100 : 100.0 * done / workshops; r.MerchantsSolventPct = merchants == 0 ? 100 : 100.0 * solvent / merchants;
        r.Businesses = open; r.Closed = closed; r.Employed = staff;
        r.BreadT = (ew.Production.ProducedUnits[cat.IdOf("bread")] - _bread0) / 1_000_000;
        r.AleKl = (ew.Production.ProducedUnits[cat.IdOf("ale")] - _ale0) / 1_000_000;
        var au = led.Audit();
        r.CoinTotal = au.InPurses.Mites; r.CoinSources = au.Sourced.Mites; r.CoinSinks = au.Sunk.Mites; r.CoinConserved = au.IsConserved; r.CoinInBusinesses = bizCoin;
        r.CoinInHouseholds = led.TotalOf(PurseKind.Household).Mites;
        r.MassOk = e.MassAuditHolds(); r.MassViolations = ew.Production.MassViolations;
        r.Hired = ew.Labour.Hired - _hired0; r.Succeeded = ew.Labour.Succeeded - _succ0; r.ClosedNoOwner = ew.Labour.ClosedNoOwner - _closedNo0;
        { long ft = 0; var ff = fs.Fields; for (int i = 0; i < ff.Count; i++) ft += ff.Fertility[i]; r.MeanFertility = ff.Count == 0 ? 0 : (double)ft / ff.Count; }
        var lv = fs.Livestock; foreach (int x in lv.Sheep) sheep += x; r.Sheep = (int)sheep; r.Oxen = lv.Oxen.Sum(); r.Cows = lv.Cows.Sum();
        return r;
    }

    // ---------- the table ----------

    public static string Header()
    {
        var sb = new StringBuilder();
        sb.Append("year   pop  u15%  over50 birth death emig hungD  surplus% hungry% fam");
        foreach (var s in Staples.Take(5)) sb.Append($" {s[..Math.Min(5, s.Length)],5}");
        sb.Append(" band% cap% shop% merch% bread-t ale-kl biz/closed employed idle fert sheep/ox/cow      coin(crowns)  src/sink  audit");
        return sb.ToString();
    }

    public static string Format(SoakRow r)
    {
        var sb = new StringBuilder();
        sb.Append($"{r.Year,4} {r.Population,6} {r.Under15Pct,5:0.0} {r.Over50Pct,6:0.0} {r.Births,5} {r.Deaths,5} {r.Emigrants,4} {r.HungerDeaths,5} {r.FoodSurplusPct,8:0.0} {r.HungryPct,7:0.00} {r.Famines,3}");
        for (int i = 0; i < 5; i++) sb.Append($" {r.PriceIndex[i],5:0.00}");
        sb.Append($" {r.BandPct,5:0.0} {r.AtCapPct,4:0.0} {r.WorkshopCompletionPct,5:0} {r.MerchantsSolventPct,6:0} {r.BreadT,7} {r.AleKl,6} {r.Businesses,4}/{r.Closed,-3} {r.Employed,8} {r.Idle,4} {r.MeanFertility,4:0} {r.Sheep}/{r.Oxen}/{r.Cows} {r.CoinTotal / 100,12:N0} {r.CoinSources / 100:N0}/{r.CoinSinks / 100:N0} {(r.CoinConserved && r.MassOk ? "ok" : "FAIL")}");
        return sb.ToString();
    }

    /// <summary>Closing lines of a soak: whole-run percentages and per-staple band shares, to quote in reports.</summary>
    public static string Summary(EconomySoak soak)
    {
        var sb = new StringBuilder();
        var rows = soak.Rows; if (rows.Count == 0) return "";
        sb.AppendLine($"hungry household-days over the run: {100.0 * soak.TotalHungryDaysAll / Math.Max(1, soak.TotalHouseholdDaysAll):0.000}% ({soak.TotalHungryDaysAll:N0} of {soak.TotalHouseholdDaysAll:N0})");
        sb.AppendLine($"price band share of book-days (mean over years): {rows.Average(r => r.BandPct):0.0}%; at the 0.3x/4x caps {rows.Average(r => r.AtCapPct):0.0}%; staples " +
                      string.Join(", ", Staples.Select((s, i) => $"{s} {rows.Average(r => r.InBandPct[i]):0.0}%")));
        sb.AppendLine($"workshops completing a batch: mean {rows.Average(r => r.WorkshopCompletionPct):0}% (min {rows.Min(r => r.WorkshopCompletionPct):0}%); merchants solvent: mean {rows.Average(r => r.MerchantsSolventPct):0}% (min {rows.Min(r => r.MerchantsSolventPct):0}%)");
        { var v = soak.Sim.Vital; long net = v.Emigrants - v.Immigrants; sb.AppendLine($"net migration over the run: {net:N0} left; population excluding it {rows[^1].Population + net:N0} (start {rows[0].Population:N0}): the continent tick owns the inflow, so judge births against deaths here"); }
        sb.AppendLine($"population {rows[0].Population:N0} -> {rows[^1].Population:N0} (min {rows.Min(r => r.Population):N0}, max {rows.Max(r => r.Population):N0}); under 15 mean {rows.Average(r => r.Under15Pct):0.0}%; coin {rows[0].CoinTotal / 100:N0} -> {rows[^1].CoinTotal / 100:N0} crowns, sources {rows[^1].CoinSources / 100:N0}, sinks {rows[^1].CoinSinks / 100:N0}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>A text dump of where the economy stands (businesses by kind, flows of the key goods, book state, purses) for tuning; <c>soak --diag</c>.</summary>
    public string Diagnostics()
    {
        var sb = new StringBuilder(); var ew = World.Economy; var e = Sim.Economy; var cat = e.Goods; var prod = ew.Production; var led = e.Ledger;
        sb.AppendLine("-- businesses by kind: n open | staff slots | batches | cash(crowns) avg | idle days avg | arrears(mites) | dividends");
        foreach (var g in prod.Businesses.GroupBy(b => prod.KindOf(b).Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var open = g.Where(b => b.State == BusinessState.Open).ToList(); var k = prod.KindOf(g.First());
            sb.AppendLine($"  {g.Key,-15} {g.Count(),4} {open.Count,4} | staff {open.Sum(b => b.Staff.Count),4}/{open.Count * k.StaffCapacity,-4} | batches {g.Sum(b => b.Batches),7} | cash {(open.Count == 0 ? 0 : open.Sum(b => led.Balance(b.Purse).Mites) / open.Count / 100.0),9:N0} | idle {(open.Count == 0 ? 0 : open.Average(b => b.IdleDays)),5:0.0} | arrears {g.Sum(b => b.ArrearsMites),8} | div {g.Sum(b => b.Dividends) / 100.0,9:N0}");
        }
        sb.AppendLine("-- goods: produced t | used by recipes t | on books t | book demand t/day | mean mid/base (all books | merchant books)");
        var goods = new[] { "wheat", "rye", "barley", "oats", "peas", "beans", "flour", "bread", "malt", "ale", "firewood", "logs", "salt", "fish_fresh", "meat_fresh", "cheese", "vegetables", "eggs", "wool_raw", "yarn", "cloth_wool", "hay", "straw", "sheep", "cow", "charcoal", "tools", "leather", "hide_raw" };
        foreach (var gn in goods)
        {
            if (!cat.TryIdOf(gn, out ushort g)) continue;
            long onBooks = 0, demand = 0; double all = 0, mer = 0; int nAll = 0, nMer = 0;
            foreach (var b in ew.Markets.AllBooks())
            {
                var gb = b.Goods[g]; onBooks += gb.StockUnits; demand += gb.Demand;
                double r = b.MidMicro(g) / (double)Math.Max(1, gb.BaseRefMicro); all += r; nAll++;
                if (b.Merchants.Count > 0) { mer += r; nMer++; }
            }
            long inStores = ew.Farming.TotalGrams(g);
            sb.AppendLine($"  {gn,-11} made {prod.ProducedUnits[g] / 1e6,9:N1} | used {prod.ConsumedUnits[g] / 1e6,9:N1} | books {onBooks / 1e6,8:N1} | all stores {inStores / 1e6,9:N1} | demand/day {demand / 1e6,7:N2} | mid/base {all / Math.Max(1, nAll),5:0.00} | {mer / Math.Max(1, nMer),5:0.00}");
        }
        var reg = Sim.Population.Region;
        sb.AppendLine("-- books by class: n | with merchants | pop | workshops (open) | workshops in books without a merchant");
        foreach (var cl in Enum.GetValues<Ravensguard.Sim.WorldGen.Region.SettlementClass>())
        {
            var bks = ew.Markets.AllBooks().Where(b => reg.Settlements[b.SettlementIndex].Class == cl).ToList(); if (bks.Count == 0) continue;
            int ws = prod.Businesses.Count(b => b.State == BusinessState.Open && prod.KindOf(b).Stations.Length > 0 && reg.Settlements[ew.Markets.BookOf[b.Settlement]].Class == cl);
            int wsNo = prod.Businesses.Count(b => b.State == BusinessState.Open && prod.KindOf(b).Stations.Length > 0 && reg.Settlements[ew.Markets.BookOf[b.Settlement]].Class == cl && ew.Markets.BookForSettlement(b.Settlement).Merchants.Count == 0);
            sb.AppendLine($"  {cl,-9} {bks.Count,3} | {bks.Count(b => b.Merchants.Count > 0),3} | {bks.Sum(b => b.Population),6} | {ws,4} | {wsNo,4}");
        }
        {
            var pe = Sim.People; var census = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < pe.Count; i++)
            {
                if (pe.Status[i] != PersonStatus.Alive) continue;
                int age = Sim.AgeOn(i, Sim.State.HourDone / 24); if (age < 12) continue;
                var occ = pe.Occupation[i]; string emp = pe.Employer[i] < 0 ? "-" : pe.Occupation[pe.Employer[i]].ToString();
                string where = ew.Labour.IsStaff(i) ? "staff" : Sim.Households.ParcelCount[Math.Max(0, pe.Household[i])] > 0 ? "landed" : "landless";
                string key = $"{occ,-14} emp:{emp,-10} {where}";
                census[key] = census.GetValueOrDefault(key) + 1;
            }
            sb.AppendLine("-- occupations aged 12+ (occupation, employer's occupation, staff / landed household / landless): " + string.Join("; ", census.Where(kv => kv.Value >= 40).OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key.Trim()}={kv.Value}")));
        }
        long hhCoin = led.TotalOf(PurseKind.Household).Mites; int hhn = Sim.Households.Count;
        var bal = Enumerable.Range(0, hhn).Where(h => Sim.Households.MemberCount[h] > 0).Select(h => led.Balance(e.HouseholdPurse[h]).Mites).OrderBy(x => x).ToArray();
        sb.AppendLine($"-- coin: households {hhCoin / 100:N0} crowns (median {bal[bal.Length / 2] / 100.0:N1}, p10 {bal[bal.Length / 10] / 100.0:N1}, p90 {bal[bal.Length * 9 / 10] / 100.0:N1} per household), businesses {Rows.LastOrDefault()?.CoinInBusinesses / 100:N0}, treasuries {led.TotalOf(PurseKind.Treasury).Mites / 100:N0}");
        sb.AppendLine("-- coin flows since start (crowns): " + string.Join(", ", Enum.GetValues<TransferReason>().Select(r => $"{r} {led.Flow(r).Mites / 100:N0}")));
        {
            long gentry = 0, burgher = 0, peasant = 0; var hq = Sim.Households;
            for (int h = 0; h < hq.Count; h++) { if (hq.MemberCount[h] == 0) continue; long c = led.Balance(e.HouseholdPurse[h]).Mites; if (hq.Class[h] == SocialClass.Gentry) gentry += c; else if (hq.Class[h] == SocialClass.Burgher) burgher += c; else peasant += c; }
            sb.AppendLine($"-- household coin by class (crowns): gentry {gentry / 100:N0}, burgher {burgher / 100:N0}, peasant {peasant / 100:N0}; manor cash {prod.Businesses.Where(b => prod.KindOf(b).Id == "manor").Sum(b => led.Balance(b.Purse).Mites) / 100:N0}; treasury paid to payroll {prod.TreasuryPaidMites / 100:N0}");
        }
        {
            long gran = 0; int ng = 0; var reg3 = Sim.Population.Region;
            for (int st = 0; st < reg3.Settlements.Count; st++) if (e.OtherStores.TryGetValue(Ravensguard.Sim.Farming.FarmIds.ParishGranary(st), out var gs)) { gran += gs.TotalMassG(); ng++; }
            int noFief = reg3.Settlements.Count(x => x.FiefIndex < 0), townsNoFief = reg3.Settlements.Count(x => x.FiefIndex < 0 && x.Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town);
            sb.AppendLine($"-- parish granaries hold {gran / 1e6:N0} t in {ng} settlements; settlements without a fief {noFief} (towns {townsNoFief}); charity given {ew.Parish.CharityG / 1e6:N0} t, tithe sold {ew.Parish.SoldG / 1e6:N0} t");
        }
        {
            var bins = new int[7]; var pq = Sim.People;
            for (int i = 0; i < pq.Count; i++) if (pq.Status[i] == PersonStatus.Alive) bins[Math.Min(6, e.NutritionCd[i] / 500)]++;
            sb.AppendLine("-- living by nutrition reserve (centidays /500 bins, 6 = full): " + string.Join(" ", bins));
            sb.AppendLine("-- deaths by cause: " + string.Join(", ", Enum.GetValues<DeathCause>().Select(c => $"{c} {Sim.Vital.DeathsByCause[(int)c]}")));
        }
        {
            var pq = Sim.People; var hq = Sim.Households; int[] c = new int[8]; long cover = 0, grams = 0; int n = 0; long seed = 0;
            for (int i = 0; i < pq.Count; i++)
            {
                if (pq.Status[i] != PersonStatus.Alive || e.NutritionCd[i] >= 1000) continue;
                int h = pq.Household[i]; if (h < 0) continue; n++;
                c[(hq.ParcelCount[h] > 0 ? 0 : 1) + (hq.Class[h] == SocialClass.Gentry ? 2 : hq.Class[h] == SocialClass.Burgher ? 4 : 0)]++;
                cover += e.FoodCoverDaysX10[h]; grams += e.HouseholdStore[h].TotalMassG(); seed += e.SeedReserveG[h];
            }
            sb.AppendLine($"-- {n} people under 1,000 cd: peasant holder {c[0]}, peasant landless {c[1]}, gentry holder {c[2]}, gentry landless {c[3]}, burgher holder {c[4]}, burgher landless {c[5]}; mean household cover {cover / Math.Max(1, n) / 10.0:0.0} days, stock {grams / Math.Max(1, n) / 1000.0:0.0} kg, seed reserve {seed / Math.Max(1, n) / 1000.0:0.0} kg per person");
        }
        {
            var pq = Sim.People; var hq = Sim.Households; int shown = 0; var seen = new HashSet<int>();
            for (int i = 0; i < pq.Count && shown < 4; i++)
            {
                if (pq.Status[i] != PersonStatus.Alive || e.NutritionCd[i] >= 500) continue;
                int h = pq.Household[i]; if (h < 0 || hq.ParcelCount[h] == 0 || !seen.Add(h)) continue; shown++;
                string barn = e.OtherStores.TryGetValue(Ravensguard.Sim.Farming.FarmIds.Barn(h), out var bs2) ? string.Join(",", bs2.Rows().Select(r => $"{cat.StringId(r.Good)} {r.Qty / 1000}")) : "-";
                sb.AppendLine($"   barn [{barn}] fields {ew.Farming.State.Parcels.Holder.Select((x, k) => x == h ? Sim.Population.Region.Parcels[k].FieldCount : 0).Sum()}");
                sb.AppendLine($"   hh {h} {hq.Class[h]} members {hq.MemberCount[h]} cover {e.FoodCoverDaysX10[h] / 10.0} d seedReserve {e.SeedReserveG[h] / 1000} kg tier {e.Tiers[h]} coin {led.Balance(e.HouseholdPurse[h]).Mites} stock: " + string.Join(", ", e.HouseholdStore[h].Rows().Select(r => $"{cat.StringId(r.Good)} {r.Qty / 1000.0:0}")));
            }
        }
        {
            var (ws, wc) = ew.Resources.Totals(Ravensguard.Sim.Production.ResourceKind.Wood); var (fsh, fcap) = ew.Resources.Totals(Ravensguard.Sim.Production.ResourceKind.Fish);
            var (gs, gc) = ew.Resources.Totals(Ravensguard.Sim.Production.ResourceKind.Game);
            sb.AppendLine($"-- resources: wood {100.0 * ws / Math.Max(1, wc):0.0}% of cap, fish {100.0 * fsh / Math.Max(1, fcap):0.0}%, game {100.0 * gs / Math.Max(1, gc):0.0}%; trade: imported {ew.Trade.ImportedMites / 100:N0} crowns, exported {ew.Trade.ExportedMites / 100:N0} crowns, continent purse {Sim.Economy.Ledger.Balance(ew.Trade.Purse).Mites / 100:N0}");
        }
        {
            string Top(long[] units) => string.Join(", ", Enumerable.Range(0, cat.Count).Select(g => (g, v: units[g] * (cat.IsPiece((ushort)g) ? cat.BasePriceMilliMites((ushort)g) / 1000.0 : cat.BasePriceMilliMites((ushort)g) / 1e6) / 100)).Where(x => x.v > 0).OrderByDescending(x => x.v).Take(8).Select(x => $"{cat.StringId((ushort)x.g)} {x.v:N0}"));
            sb.AppendLine("-- imports by value (crowns at base price): " + Top(ew.Trade.ImportedUnits));
            sb.AppendLine("-- exports by value (crowns at base price): " + Top(ew.Trade.ExportedUnits));
        }
        {
            var par = ew.Farming.State.Parcels; var fieldsOf = new Dictionary<int, int>();
            for (int pi = 0; pi < par.Count; pi++) if (par.Holder[pi] >= 0) fieldsOf[par.Holder[pi]] = fieldsOf.GetValueOrDefault(par.Holder[pi]) + Sim.Population.Region.Parcels[pi].FieldCount;
            var hungF = new List<int>(); var allF = new List<int>();
            foreach (var (h, f) in fieldsOf) { if (Sim.Households.MemberCount[h] == 0 || Sim.Households.Class[h] == SocialClass.Gentry) continue; allF.Add(f); if ((e.Grievances[h] & Grievance.Hunger) != 0) hungF.Add(f); }
            allF.Sort(); hungF.Sort();
            string Q(List<int> l) => l.Count == 0 ? "-" : $"n {l.Count} p10 {l[l.Count / 10]} p50 {l[l.Count / 2]} p90 {l[l.Count * 9 / 10]}";
            sb.AppendLine($"-- fields held (non-gentry holders): all {Q(allF)}; hungry {Q(hungF)}; farm seed eaten {e.SeedEaten / 1e6:N1} t");
        }
        var lab = ew.Labour;
        sb.AppendLine($"-- labour: hired {lab.Hired} (apprentices {lab.Apprenticed}), succeeded {lab.Succeeded}, closed for want of an owner {lab.ClosedNoOwner}, staff gone {lab.StaffGone}; quits {prod.QuitCount}, forgiven arrears {prod.ForgivenArrearsMites} mites; batches {prod.BatchesFinished}/{prod.BatchesStarted}");
        var fl = ew.Farming.LastLabour;
        sb.AppendLine($"-- farming labour last day: demand {fl.DemandHours} h, supply {fl.SupplyHours} h, own {fl.OwnUsedHours} h, hired {fl.HiredUsedHours} h, unmet {fl.UnmetHours} h, wages {fl.WagesMites} mites");
        sb.AppendLine($"-- farm sales {ew.Counters.FarmSoldG / 1e6:N0} t, parish sold {ew.Parish.SoldG / 1e6:N0} t, charity {ew.Parish.CharityG / 1e6:N0} t");
        var fs = ew.Farming.State; var lv = fs.Livestock;
        sb.AppendLine($"-- herds: sheep {lv.Sheep.Sum()}, oxen {lv.Oxen.Sum()}, cows {lv.Cows.Sum()}; grazed settlements {lv.Grazed.Count(x => x)}; farm sow refused: seed {ew.Farming.Diagnostics.SowRefusedSeed}, fertility {ew.Farming.Diagnostics.SowRefusedFertility}");
        sb.AppendLine("-- hungry households at the end of every 5th day: " + string.Join(" ", HungryByDay.Where((_, i) => i % 5 == 4).Select(x => x.ToString())));
        {
            int hh1 = 0, hh2 = 0, hh3 = 0, hh4 = 0, hhTown = 0, hhNoCoin = 0, hhNoStock = 0; var hhb = Sim.Households; var reg2 = Sim.Population.Region;
            for (int h = 0; h < hhb.Count; h++)
            {
                if (hhb.MemberCount[h] == 0 || (e.Grievances[h] & Grievance.Hunger) == 0) continue;
                if (hhb.ParcelCount[h] > 0) hh1++; else hh2++;
                if (reg2.Settlements[hhb.Settlement[h]].Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town) hhTown++;
                if (led.Balance(e.HouseholdPurse[h]).Mites < 100) hhNoCoin++;
                if (hhb.Class[h] == SocialClass.Burgher) hh3++; else hh4++;
                if (e.HouseholdStore[h].TotalMassG() < 5000) hhNoStock++;
            }
            var occH = new SortedDictionary<string, int>(StringComparer.Ordinal); var pp = Sim.People;
            for (int i = 0; i < pp.Count; i++)
            {
                int hq = pp.Household[i]; if (hq < 0 || pp.Status[i] != PersonStatus.Alive || hhb.MemberCount[hq] == 0 || (e.Grievances[hq] & Grievance.Hunger) == 0 || Sim.AgeOn(i, Sim.State.HourDone / 24) < 12) continue;
                string k = $"{pp.Occupation[i]}/{(pp.Employer[i] < 0 ? "-" : pp.Occupation[pp.Employer[i]].ToString())}"; occH[k] = occH.GetValueOrDefault(k) + 1;
            }
            sb.AppendLine("-- adults of hungry households by occupation/employer: " + string.Join(", ", occH.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key}={kv.Value}")));
            sb.AppendLine($"-- hungry now: holders {hh1}, landless {hh2}; town {hhTown}; burgher {hh3}; with under 1 crown {hhNoCoin}; with under 5 kg of anything {hhNoStock}");
        }
        int hung = 0, tot = 0; var byClass = new int[3]; var byClassTot = new int[3];
        for (int h = 0; h < hhn; h++) { if (Sim.Households.MemberCount[h] == 0) continue; tot++; byClassTot[(int)Sim.Households.Class[h]]++; if ((e.Grievances[h] & Grievance.Hunger) != 0) { hung++; byClass[(int)Sim.Households.Class[h]]++; } }
        sb.AppendLine($"-- hungry households now: {hung}/{tot} (peasant {byClass[0]}/{byClassTot[0]}, burgher {byClass[1]}/{byClassTot[1]}, gentry {byClass[2]}/{byClassTot[2]})");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Steps <paramref name="days"/> days printing one line per day for the first businesses of a kind (cash, staff, input and output stock, station state, expected margins).</summary>
    public void Probe(string kind, int days, TextWriter o, int count = 2)
    {
        var ew = World.Economy; var prod = ew.Production; var cat = Sim.Economy.Goods;
        var bs = prod.Businesses.Where(b => prod.KindOf(b).Id == kind && b.State == BusinessState.Open).Take(count).ToList();
        for (int d = 0; d < days; d++)
        {
            Ticker.AdvanceHours(24);
            foreach (var b in bs)
            {
                var k = prod.KindOf(b); var book = ew.Markets.BookForSettlement(b.Settlement);
                var sb = new StringBuilder($"day {Sim.State.HourDone / 24 % 60 + 1,2} b{b.Index} cash {Sim.Economy.Ledger.Balance(b.Purse).Mites,7} staff {b.Staff.Count}+{(b.OwnerWorks ? 1 : 0)} batches {b.Batches,4} stations [");
                foreach (var st in b.Stations) sb.Append(st.Busy ? $"{prod.Content.Recipes.Recipes[st.Recipe].Id}:{st.ProgressPpm / 10000}% " : "idle ");
                sb.Append("] store {");
                foreach (var (g, q) in b.Store.Rows()) sb.Append($"{cat.StringId(g)}={q / 1000.0:0.#}kg ");
                sb.Append("} margins {");
                foreach (var r in k.Recipes) sb.Append($"{r.Id}={prod.ExpectedMargin(b, r):0} ");
                sb.Append("} book {");
                foreach (var r in k.Recipes.Take(2)) foreach (var (g, _) in r.Inputs.Concat(r.Outputs)) { var gb = book.Goods[g]; sb.Append($"{cat.StringId(g)}: s={gb.StockUnits / 1000.0:0.#} d={gb.Demand / 1000.0:0.#} x{book.MidRatioPermille(g)} "); }
                sb.Append('}');
                o.WriteLine(sb.ToString());
            }
        }
    }
}
