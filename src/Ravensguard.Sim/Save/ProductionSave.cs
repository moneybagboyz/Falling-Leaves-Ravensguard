using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "markets" section: the price state of every book and good, stock lots, merchants and import requests. Stores and coin are in the economy section; this
/// holds only the market records on top of them. Lists are in book then good then lot order, so bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class MarketsSave
{
    public const string SectionName = "markets";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public string GoodsHash { get; set; } = "";
    [Key(2)] public int[] BookSettlement { get; set; } = [];
    [Key(3)] public uint[] BookNextLot { get; set; } = [];
    [Key(4)] public int[] BookPopulation { get; set; } = [];
    // per (book, good) rows, books ascending then goods ascending; GoodCount rows per book
    [Key(5)] public long[] RefMicro { get; set; } = [];
    [Key(6)] public long[] DemandEma { get; set; } = [];
    [Key(7)] public long[] StructuralDemand { get; set; } = [];
    [Key(8)] public int[] LastTradedDay { get; set; } = [];
    [Key(9)] public long[] DemandToday { get; set; } = [];
    [Key(10)] public long[] TradedToday { get; set; } = [];
    // lots
    [Key(11)] public int[] LotBook { get; set; } = [];
    [Key(12)] public ushort[] LotGood { get; set; } = [];
    [Key(13)] public uint[] LotId { get; set; } = [];
    [Key(14)] public ulong[] LotOwner { get; set; } = [];
    [Key(15)] public byte[] LotOwnerKind { get; set; } = [];
    [Key(16)] public long[] LotQty { get; set; } = [];
    [Key(17)] public int[] LotFloor { get; set; } = [];
    [Key(18)] public int[] LotListedDay { get; set; } = [];
    [Key(19)] public bool[] LotMerchant { get; set; } = [];
    // merchants (book, owner id) and import requests
    [Key(20)] public int[] MerchantBook { get; set; } = [];
    [Key(21)] public ulong[] MerchantId { get; set; } = [];
    [Key(22)] public int[] ImportBook { get; set; } = [];
    [Key(23)] public ushort[] ImportGood { get; set; } = [];
    [Key(24)] public long[] ImportQty { get; set; } = [];
    [Key(25)] public int[] ImportDay { get; set; } = [];
    [Key(26)] public ulong[] ImportMerchant { get; set; } = [];
    [Key(27)] public long[] FloorDemand { get; set; } = [];

    public static MarketsSave Capture(MarketSystem ms)
    {
        var s = new MarketsSave { GoodsHash = ms.Econ.Goods.Hash() };
        var books = ms.AllBooks().ToList(); int goods = ms.Econ.Goods.Count;
        s.BookSettlement = [.. books.Select(b => b.SettlementIndex)]; s.BookNextLot = [.. books.Select(b => b.NextLotId)]; s.BookPopulation = [.. books.Select(b => b.Population)];
        int n = books.Count * goods;
        s.RefMicro = new long[n]; s.DemandEma = new long[n]; s.StructuralDemand = new long[n]; s.LastTradedDay = new int[n]; s.DemandToday = new long[n]; s.TradedToday = new long[n]; s.FloorDemand = new long[n];
        var lb = new List<int>(); var lg = new List<ushort>(); var li = new List<uint>(); var lo = new List<ulong>(); var lk = new List<byte>(); var lq = new List<long>();
        var lf = new List<int>(); var ld = new List<int>(); var lm = new List<bool>(); var mb = new List<int>(); var mi = new List<ulong>();
        var ib = new List<int>(); var ig = new List<ushort>(); var iq = new List<long>(); var id = new List<int>(); var im = new List<ulong>();
        for (int bi = 0; bi < books.Count; bi++)
        {
            var b = books[bi];
            for (int g = 0; g < goods; g++)
            {
                var gb = b.Goods[g]; int k = bi * goods + g;
                s.RefMicro[k] = gb.RefMicro; s.DemandEma[k] = gb.DemandEma; s.StructuralDemand[k] = gb.StructuralDemand; s.LastTradedDay[k] = gb.LastTradedDay;
                s.DemandToday[k] = gb.DemandToday; s.TradedToday[k] = gb.TradedToday; s.FloorDemand[k] = gb.FloorDemand;
                foreach (var l in gb.Lots) { lb.Add(bi); lg.Add((ushort)g); li.Add(l.Id); lo.Add(l.Owner.Id.Value); lk.Add((byte)l.Owner.Kind); lq.Add(l.Qty); lf.Add(l.FloorPermille); ld.Add(l.ListedDay); lm.Add(l.IsMerchant); }
            }
            foreach (var m in b.Merchants) { mb.Add(bi); mi.Add(m.Id.Value); }
            foreach (var r in b.ImportRequests) { ib.Add(bi); ig.Add(r.Good); iq.Add(r.Qty); id.Add(r.Day); im.Add(r.Merchant.Value); }
        }
        s.LotBook = [.. lb]; s.LotGood = [.. lg]; s.LotId = [.. li]; s.LotOwner = [.. lo]; s.LotOwnerKind = [.. lk]; s.LotQty = [.. lq]; s.LotFloor = [.. lf]; s.LotListedDay = [.. ld]; s.LotMerchant = [.. lm];
        s.MerchantBook = [.. mb]; s.MerchantId = [.. mi]; s.ImportBook = [.. ib]; s.ImportGood = [.. ig]; s.ImportQty = [.. iq]; s.ImportDay = [.. id]; s.ImportMerchant = [.. im];
        return s;
    }

    /// <summary>Overlays the saved market state on a freshly built <see cref="MarketSystem"/> (which makes the same books for the same region). <paramref name="prod"/> resolves business parties.</summary>
    public void Restore(MarketSystem ms, ProductionSystem? prod)
    {
        try
        {
            int goods = ms.Econ.Goods.Count;
            if (GoodsHash != ms.Econ.Goods.Hash()) throw new SaveException("The markets section was saved against a different goods table.");
            var books = ms.AllBooks().ToList();
            if (books.Count != BookSettlement.Length) throw new SaveException("The markets section does not match the region's settlements.");
            MarketParty Party(ulong owner, byte kind)
            {
                var id = new EntityId(owner);
                if (EconIds.IsBusiness(id)) return prod?.ById(id)?.Party ?? throw new SaveException("A market lot belongs to a business that does not exist.");
                int hh = ms.Pop.Households.IndexOf(id);
                return hh >= 0 ? ms.HouseholdParty(hh) : throw new SaveException("A market lot belongs to an unknown owner.");
            }
            for (int bi = 0; bi < books.Count; bi++)
            {
                var b = books[bi];
                if (b.SettlementIndex != BookSettlement[bi]) throw new SaveException("The markets section lists books in a different order.");
                b.NextLotId = BookNextLot[bi]; b.Population = BookPopulation[bi];
                for (int g = 0; g < goods; g++)
                {
                    var gb = b.Goods[g]; int k = bi * goods + g;
                    gb.RefMicro = RefMicro[k]; gb.DemandEma = DemandEma[k]; gb.StructuralDemand = StructuralDemand[k]; gb.LastTradedDay = LastTradedDay[k];
                    gb.DemandToday = DemandToday[k]; gb.TradedToday = TradedToday[k]; gb.FloorDemand = FloorDemand[k]; gb.Lots.Clear(); gb.StockUnits = 0;
                }
                b.Merchants.Clear(); b.ImportRequests.Clear();
            }
            for (int i = 0; i < LotId.Length; i++)
            {
                var gb = books[LotBook[i]].Goods[LotGood[i]];
                gb.Lots.Add(new StockLot { Id = LotId[i], Owner = Party(LotOwner[i], LotOwnerKind[i]), Good = LotGood[i], Qty = LotQty[i], FloorPermille = LotFloor[i], ListedDay = LotListedDay[i], IsMerchant = LotMerchant[i] });
                gb.StockUnits += LotQty[i];
            }
            for (int i = 0; i < MerchantId.Length; i++) books[MerchantBook[i]].Merchants.Add(Party(MerchantId[i], (byte)PartyKind.Merchant));
            for (int i = 0; i < ImportGood.Length; i++) books[ImportBook[i]].ImportRequests.Add(new ImportRequest(books[ImportBook[i]].SettlementIndex, ImportGood[i], ImportQty[i], ImportDay[i], new EntityId(ImportMerchant[i])));
            foreach (var b in books)
                for (int g = 0; g < goods; g++)
                    if (b.BookStore.Qty((ushort)g) != b.Goods[g].StockUnits) throw new SaveException("A market book's lots do not match its store.");
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            throw new SaveException("Corrupt 'markets' section in save.", ex);
        }
    }
}

/// <summary>
/// The "production" section: businesses (owner, workplace, stations, staff, wages owed, season books), mines, resource stocks per tile and the production
/// counters. Coin and goods are in the economy section. Staff and station rows are flat arrays with a count per business.
/// </summary>
[MessagePackObject]
public sealed class ProductionSave
{
    public const string SectionName = "production";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public string GoodsHash { get; set; } = "";
    [Key(2)] public ushort[] Kind { get; set; } = [];
    [Key(3)] public int[] Settlement { get; set; } = [];
    [Key(4)] public int[] Lot { get; set; } = [];
    [Key(5)] public int[] Owner { get; set; } = [];
    [Key(6)] public bool[] OwnerWorks { get; set; } = [];
    [Key(7)] public byte[] State { get; set; } = [];
    [Key(8)] public byte[] LossSeasons { get; set; } = [];
    [Key(9)] public int[] IdleDays { get; set; } = [];
    [Key(10)] public int[] UnfilledDays { get; set; } = [];
    [Key(11)] public short[] WageOffer { get; set; } = [];
    [Key(12)] public long[] WagesSeason { get; set; } = [];
    [Key(13)] public long[] InputsSeason { get; set; } = [];
    [Key(14)] public long[] DividendsSeason { get; set; } = [];
    [Key(15)] public long[] CashStart { get; set; } = [];
    [Key(16)] public long[] WageBillPerDay { get; set; } = [];
    [Key(17)] public long[] InputSpendPerDay { get; set; } = [];
    [Key(18)] public long[] Dividends { get; set; } = [];
    [Key(19)] public long[] ToolPiecesWorn { get; set; } = [];
    [Key(20)] public long[] Batches { get; set; } = [];
    [Key(21)] public int[] Quality { get; set; } = [];             // 5 per business
    [Key(22)] public int[] LastQuality { get; set; } = [];
    [Key(23)] public int[] Mine { get; set; } = [];
    [Key(24)] public int[] StaffCount { get; set; } = [];
    [Key(25)] public int[] StaffPerson { get; set; } = [];
    [Key(26)] public long[] StaffOwed { get; set; } = [];
    [Key(27)] public byte[] StaffShortDays { get; set; } = [];
    [Key(28)] public int[] StationCount { get; set; } = [];
    [Key(29)] public string[] StationKind { get; set; } = [];
    [Key(30)] public int[] StationRecipe { get; set; } = [];
    [Key(31)] public long[] StationProgress { get; set; } = [];
    [Key(32)] public long[] StationBusyUntil { get; set; } = [];
    [Key(33)] public int[] StationScale { get; set; } = [];
    [Key(34)] public long[] StationStarted { get; set; } = [];
    [Key(35)] public int[] StationMinSkill { get; set; } = [];
    [Key(36)] public int[] JobCount { get; set; } = [];
    [Key(37)] public ushort[] JobKind { get; set; } = [];
    [Key(38)] public int[] JobRow { get; set; } = [];
    [Key(39)] public int[] JobWorkers { get; set; } = [];
    // mines
    [Key(40)] public int[] MineSite { get; set; } = [];
    [Key(41)] public ulong[] MineSiteId { get; set; } = [];
    [Key(42)] public byte[] MineMineral { get; set; } = [];
    [Key(43)] public long[] MineRemaining { get; set; } = [];
    [Key(44)] public long[] MineInitial { get; set; } = [];
    [Key(45)] public int[] MineLevels { get; set; } = [];
    [Key(46)] public byte[] MineState { get; set; } = [];
    [Key(47)] public int[] MineClosedUntil { get; set; } = [];
    // counters
    [Key(48)] public long[] ProducedUnits { get; set; } = [];
    [Key(49)] public long[] ConsumedUnits { get; set; } = [];
    [Key(50)] public long[] Counters { get; set; } = [];            // mass violations, finished, started, forgiven arrears, quits
    // resources
    [Key(51)] public bool HasResources { get; set; }
    [Key(52)] public int[] ResTile { get; set; } = [];
    [Key(53)] public byte[] ResKind { get; set; } = [];
    [Key(54)] public long[] ResStock { get; set; } = [];
    [Key(55)] public long[] ResCap { get; set; } = [];
    [Key(56)] public short[] ResLastDay { get; set; } = [];
    [Key(57)] public long[] Capital { get; set; } = [];
    // resources as a difference from a freshly built layer (see Capture); the Res* arrays above are then empty
    [Key(58)] public bool ResDiffBase { get; set; }
    [Key(59)] public int[] ResDiffRow { get; set; } = [];
    [Key(60)] public long[] ResDiffStock { get; set; } = [];
    [Key(61)] public short[] ResDiffLast { get; set; } = [];
    /// <summary>
    /// The difference per row of the layer (note N24), when non-empty: 0 = as freshly built, 1 = stock at its cap (what regrowth does to rows that were not full),
    /// 2 = fresh stock plus the amount for the row's kind in <see cref="ResKindDelta"/> (wood and game regrow by a fixed amount), 4 = fresh stock plus a share of the gap to the cap, <see cref="ResKindFracPermille"/> (fish and the like regrow by a fraction of what is missing), 3 = explicit: the next entry of
    /// <see cref="ResDiffStock"/> (stock minus fresh) and <see cref="ResDiffLast"/>. The class bytes repeat with the tile pattern, so they compress to a few kilobytes.
    /// <see cref="ResDiffRow"/> is empty then (the older row list is still read).
    /// </summary>
    [Key(62)] public byte[] ResClass { get; set; } = [];
    /// <summary>Per resource kind (index = kind, 16 entries): the amount the rows of class 2 gained, and the share of the gap to the cap (per mille) the rows of class 4 gained.</summary>
    [Key(63)] public long[] ResKindDelta { get; set; } = [];
    [Key(64)] public int[] ResKindFracPermille { get; set; } = [];

    /// <summary>The per mille share of <paramref name="gap"/> that <paramref name="delta"/> is exactly (<c>gap * f / 1000 == delta</c>), null when none of the 1..1000 does.</summary>
    private static int? FracOf(long delta, long gap)
    {
        long f = (delta * 1000 + gap - 1) / gap; // the smallest share that reaches delta
        return f is >= 1 and <= 1000 && gap * f / 1000 == delta ? (int)f : null;
    }

    public static ProductionSave Capture(ProductionSystem p)
    {
        var bs = p.Businesses; int n = bs.Count;
        var s = new ProductionSave
        {
            GoodsHash = p.Econ.Goods.Hash(),
            Kind = [.. bs.Select(b => b.Kind)], Settlement = [.. bs.Select(b => b.Settlement)], Lot = [.. bs.Select(b => b.Lot)], Owner = [.. bs.Select(b => b.Owner)],
            OwnerWorks = [.. bs.Select(b => b.OwnerWorks)], State = [.. bs.Select(b => (byte)b.State)], LossSeasons = [.. bs.Select(b => b.LossSeasons)],
            IdleDays = [.. bs.Select(b => b.IdleDays)], UnfilledDays = [.. bs.Select(b => b.UnfilledDays)], WageOffer = [.. bs.Select(b => b.WageOfferMites)],
            WagesSeason = [.. bs.Select(b => b.WagesSeason)], InputsSeason = [.. bs.Select(b => b.InputsSeason)], DividendsSeason = [.. bs.Select(b => b.DividendsSeason)],
            CashStart = [.. bs.Select(b => b.CashStart)], WageBillPerDay = [.. bs.Select(b => b.WageBillPerDay)], InputSpendPerDay = [.. bs.Select(b => b.InputSpendPerDay)],
            Capital = [.. bs.Select(b => b.Capital)], Dividends = [.. bs.Select(b => b.Dividends)], ToolPiecesWorn = [.. bs.Select(b => b.ToolPiecesWorn)], Batches = [.. bs.Select(b => b.Batches)],
            Quality = [.. bs.SelectMany(b => b.Quality)], LastQuality = [.. bs.Select(b => b.LastQuality)], Mine = [.. bs.Select(b => b.Mine)],
            StaffCount = [.. bs.Select(b => b.Staff.Count)], StaffPerson = [.. bs.SelectMany(b => b.Staff)], StaffOwed = [.. bs.SelectMany(b => b.Owed)], StaffShortDays = [.. bs.SelectMany(b => b.ShortDays)],
            StationCount = [.. bs.Select(b => b.Stations.Count)], StationKind = [.. bs.SelectMany(b => b.Stations.Select(x => x.Kind))], StationRecipe = [.. bs.SelectMany(b => b.Stations.Select(x => x.Recipe))],
            StationProgress = [.. bs.SelectMany(b => b.Stations.Select(x => x.ProgressMicro))], StationBusyUntil = [.. bs.SelectMany(b => b.Stations.Select(x => x.BusyUntilHour))],
            StationScale = [.. bs.SelectMany(b => b.Stations.Select(x => x.ScalePermille))], StationStarted = [.. bs.SelectMany(b => b.Stations.Select(x => x.StartedHour))],
            StationMinSkill = [.. bs.SelectMany(b => b.Stations.Select(x => x.MinSkill))],
            JobCount = [.. bs.Select(b => b.Jobs.Count)], JobKind = [.. bs.SelectMany(b => b.Jobs.Select(j => j.Kind))], JobRow = [.. bs.SelectMany(b => b.Jobs.Select(j => j.Row))],
            JobWorkers = [.. bs.SelectMany(b => b.Jobs.Select(j => j.Workers))],
            MineSite = [.. p.Mines.Select(m => m.SiteIndex)], MineSiteId = [.. p.Mines.Select(m => m.SiteId.Value)], MineMineral = [.. p.Mines.Select(m => (byte)m.Mineral)],
            MineRemaining = [.. p.Mines.Select(m => m.RemainingG)], MineInitial = [.. p.Mines.Select(m => m.InitialG)], MineLevels = [.. p.Mines.Select(m => m.Levels)],
            MineState = [.. p.Mines.Select(m => (byte)m.State)], MineClosedUntil = [.. p.Mines.Select(m => m.ClosedUntilDay)],
            ProducedUnits = (long[])p.ProducedUnits.Clone(), ConsumedUnits = (long[])p.ConsumedUnits.Clone(),
            Counters = [p.MassViolations, p.BatchesFinished, p.BatchesStarted, p.ForgivenArrearsMites, p.QuitCount],
        };
        _ = n;
        if (p.Resources != null)
        {
            var e = p.Resources.Export();
            s.HasResources = true;
            // The layer is ~450,000 rows and a pure function of the region: store only the rows that differ from a freshly built one (stock, last visit), not the layer
            var pristine = ResourceLayer.Build(p.Pop.Region, p.Content.Settings, p.Seed).Export();
            if (pristine.Tile.AsSpan().SequenceEqual(e.Tile) && pristine.Kind.AsSpan().SequenceEqual(e.Kind) && pristine.Cap.AsSpan().SequenceEqual(e.Cap))
            {
                s.ResDiffBase = true;
                var stocks = new List<long>(); var lasts = new List<short>();
                // the usual regrowth of each kind: a fixed amount (wood) or a share of the gap to the cap (fish): the most common of the rows that moved and did not just fill up
                var cDelta = new Dictionary<(byte Kind, long Delta), int>(); var cFrac = new Dictionary<(byte Kind, int Frac), int>();
                for (int i = 0; i < e.Tile.Length; i++)
                {
                    long d = e.Stock[i] - pristine.Stock[i], gap = e.Cap[i] - pristine.Stock[i];
                    if (d == 0 || e.Stock[i] == e.Cap[i] || e.LastDay[i] != pristine.LastDay[i]) continue;
                    cDelta[(e.Kind[i], d)] = cDelta.GetValueOrDefault((e.Kind[i], d)) + 1;
                    if (gap > 0 && FracOf(d, gap) is int f) cFrac[(e.Kind[i], f)] = cFrac.GetValueOrDefault((e.Kind[i], f)) + 1;
                }
                var kindDelta = new long[16]; var kindFrac = new int[16];
                foreach (var g in cDelta.GroupBy(kv => kv.Key.Kind)) { var best = g.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Delta).First(); kindDelta[g.Key] = best.Key.Delta; }
                foreach (var g in cFrac.GroupBy(kv => kv.Key.Kind)) { var best = g.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Frac).First(); kindFrac[g.Key] = best.Key.Frac; }
                // a kind uses whichever law explains more of its rows
                for (int k = 0; k < 16; k++)
                {
                    int nd = kindDelta[k] == 0 ? 0 : cDelta.GetValueOrDefault(((byte)k, kindDelta[k])), nf = kindFrac[k] == 0 ? 0 : cFrac.GetValueOrDefault(((byte)k, kindFrac[k]));
                    if (nf > nd) kindDelta[k] = 0; else kindFrac[k] = 0;
                }
                var cls = new byte[e.Tile.Length];
                for (int i = 0; i < e.Tile.Length; i++)
                {
                    long d = e.Stock[i] - pristine.Stock[i], gap = e.Cap[i] - pristine.Stock[i];
                    bool sameLast = e.LastDay[i] == pristine.LastDay[i];
                    byte kd = e.Kind[i];
                    byte c = d == 0 && sameLast ? (byte)0 : e.Stock[i] == e.Cap[i] && sameLast ? (byte)1
                        : sameLast && kindDelta[kd] != 0 && d == kindDelta[kd] ? (byte)2 : sameLast && kindFrac[kd] != 0 && gap > 0 && d == gap * kindFrac[kd] / 1000 ? (byte)4 : (byte)3;
                    cls[i] = c;
                    if (c == 3) { stocks.Add(d); lasts.Add(e.LastDay[i]); }
                }
                s.ResClass = cls; s.ResKindDelta = kindDelta; s.ResKindFracPermille = kindFrac; s.ResDiffStock = [.. stocks]; s.ResDiffLast = [.. lasts];
            }
            else { s.ResTile = e.Tile; s.ResKind = e.Kind; s.ResStock = e.Stock; s.ResCap = e.Cap; s.ResLastDay = e.LastDay; }
        }
        return s;
    }

    /// <summary>Rebuilds the production system for a world whose economy has been restored: purses and stores are looked up by business id, never created.</summary>
    public ProductionSystem Restore(EconomyState econ, Population pop, ProductionContent content, ulong seed, MarketSystem? markets)
    {
        try
        {
            if (GoodsHash != econ.Goods.Hash()) throw new SaveException("The production section was saved against a different goods table.");
            ResourceLayer? layer = null;
            if (HasResources)
            {
                if (ResDiffBase)
                {
                    var b = ResourceLayer.Build(pop.Region, content.Settings, seed).Export();
                    if (ResClass.Length > 0)
                    {
                        if (ResClass.Length != b.Tile.Length) throw new SaveException("The resource layer does not match the region.");
                        int ex = 0;
                        for (int row = 0; row < ResClass.Length; row++)
                        {
                            switch (ResClass[row])
                            {
                                case 1: b.Stock[row] = b.Cap[row]; break;
                                case 2: b.Stock[row] += ResKindDelta[b.Kind[row]]; break;
                                case 4: b.Stock[row] += (b.Cap[row] - b.Stock[row]) * ResKindFracPermille[b.Kind[row]] / 1000; break;
                                case 3: b.Stock[row] += ResDiffStock[ex]; b.LastDay[row] = ResDiffLast[ex]; ex++; break;
                            }
                        }
                    }
                    else for (int k = 0; k < ResDiffRow.Length; k++) { b.Stock[ResDiffRow[k]] = ResDiffStock[k]; b.LastDay[ResDiffRow[k]] = ResDiffLast[k]; }
                    layer = ResourceLayer.Import(b.Tile, b.Kind, b.Stock, b.Cap, b.LastDay);
                }
                else layer = ResourceLayer.Import(ResTile, ResKind, ResStock, ResCap, ResLastDay);
            }
            var sys = new ProductionSystem(econ, pop, content, seed, markets, layer);
            int sp = 0, st = 0, jb = 0;
            for (int i = 0; i < Kind.Length; i++)
            {
                var b = sys.NewBusiness(content.Kinds[Kind[i]], Settlement[i], Lot[i], Owner[i]);
                b.OwnerWorks = OwnerWorks[i]; b.State = (BusinessState)State[i]; b.LossSeasons = LossSeasons[i]; b.IdleDays = IdleDays[i]; b.UnfilledDays = UnfilledDays[i];
                b.WageOfferMites = WageOffer[i]; b.WagesSeason = WagesSeason[i]; b.InputsSeason = InputsSeason[i]; b.DividendsSeason = DividendsSeason[i]; b.CashStart = CashStart[i]; b.Capital = i < Capital.Length ? Capital[i] : CashStart[i];
                b.WageBillPerDay = WageBillPerDay[i]; b.InputSpendPerDay = InputSpendPerDay[i]; b.Dividends = Dividends[i]; b.ToolPiecesWorn = ToolPiecesWorn[i]; b.Batches = Batches[i];
                for (int q = 0; q < 5; q++) b.Quality[q] = Quality[i * 5 + q];
                b.LastQuality = LastQuality[i]; b.Mine = Mine[i];
                for (int k = 0; k < StaffCount[i]; k++, sp++) { b.Staff.Add(StaffPerson[sp]); b.Owed.Add(StaffOwed[sp]); b.ShortDays.Add(StaffShortDays[sp]); }
                for (int k = 0; k < StationCount[i]; k++, st++)
                    b.Stations.Add(new Station { Kind = StationKind[st], Recipe = StationRecipe[st], ProgressMicro = StationProgress[st], BusyUntilHour = StationBusyUntil[st], ScalePermille = StationScale[st], StartedHour = StationStarted[st], MinSkill = StationMinSkill[st] });
                for (int k = 0; k < JobCount[i]; k++, jb++) b.Jobs.Add(new CrewJob { Kind = JobKind[jb], Row = JobRow[jb], Workers = JobWorkers[jb] });
            }
            for (int m = 0; m < MineSite.Length; m++)
                sys.AddMine(new MineWorks { SiteIndex = MineSite[m], SiteId = new EntityId(MineSiteId[m]), Mineral = (WorldGen.MineralType)MineMineral[m], RemainingG = MineRemaining[m], InitialG = MineInitial[m], Levels = MineLevels[m], State = (MineStateKind)MineState[m], ClosedUntilDay = MineClosedUntil[m] });
            Array.Copy(ProducedUnits, sys.ProducedUnits, ProducedUnits.Length); Array.Copy(ConsumedUnits, sys.ConsumedUnits, ConsumedUnits.Length);
            sys.MassViolations = Counters[0]; sys.BatchesFinished = Counters[1]; sys.BatchesStarted = Counters[2]; sys.ForgivenArrearsMites = Counters[3]; sys.QuitCount = Counters[4];
            return sys;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException or KeyNotFoundException)
        {
            throw new SaveException("Corrupt 'production' section in save.", ex);
        }
    }
}
