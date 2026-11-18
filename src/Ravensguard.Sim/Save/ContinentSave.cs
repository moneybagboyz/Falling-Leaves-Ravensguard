using MessagePack;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class SummarySave
{
    [Key(0)] public int Pop { get; set; }
    [Key(1)] public int Households { get; set; }
    [Key(2)] public int Dwellings { get; set; }
    [Key(3)] public int Vacancies { get; set; }
    [Key(4)] public long[] StockG { get; set; } = [];
    [Key(5)] public long GrainCapacityNet { get; set; }
    [Key(6)] public long CoinTotal { get; set; }
    [Key(7)] public long TreasuryCoin { get; set; }
    [Key(8)] public int[] PriceIdx { get; set; } = [];
    [Key(9)] public int[] CoverX10 { get; set; } = [];
    [Key(10)] public int FamineDays { get; set; }
    [Key(11)] public int Unrest { get; set; }
    [Key(12)] public int Prosperity { get; set; }
    [Key(13)] public byte Realm { get; set; }
    [Key(14)] public byte WarFlags { get; set; }
    [Key(15)] public int PopRemainder { get; set; }
    [Key(16)] public int PriceDay { get; set; }
    [Key(17)] public int LastDetailDay { get; set; }
    [Key(18)] public bool Detailed { get; set; }
    [Key(19)] public byte[] NewsKind { get; set; } = [];
    [Key(20)] public byte[] NewsOrigin { get; set; } = [];
    [Key(21)] public int[] NewsMagnitude { get; set; } = [];
    [Key(22)] public int[] NewsDay { get; set; } = [];
    [Key(23)] public byte[] NewsHops { get; set; } = [];
}

[MessagePackObject]
public sealed class EdgeTradeSave
{
    [Key(0)] public int Edge { get; set; }
    [Key(1)] public int Neighbour { get; set; }
    [Key(2)] public long PayBudgetMites { get; set; }
    [Key(3)] public long[] SpareG { get; set; } = [];
    [Key(4)] public long CoinNetIn { get; set; }
    [Key(5)] public long[] StockNetOut { get; set; } = [];
    [Key(6)] public long TollMites { get; set; }
}

[MessagePackObject]
public sealed class LinkSave
{
    [Key(0)] public int Region { get; set; }
    [Key(1)] public EdgeTradeSave[] Edges { get; set; } = [];
    [Key(2)] public int EmigrantPersons { get; set; }
    [Key(3)] public int EmigrantHouseholds { get; set; }
    [Key(4)] public long EmigrantCoin { get; set; }
    [Key(5)] public long EmigrantMeasures { get; set; }
    [Key(6)] public int PendingPersons { get; set; }
    [Key(7)] public int PendingHouseholds { get; set; }
    [Key(8)] public long PendingCoin { get; set; }
    [Key(9)] public long PendingMeasures { get; set; }
    [Key(10)] public int PendingFromRegion { get; set; }
    [Key(11)] public int TakenPersons { get; set; }
    [Key(12)] public long TakenCoin { get; set; }
    [Key(13)] public long TakenMeasures { get; set; }
    [Key(14)] public long TotalLeft { get; set; }
    [Key(15)] public long TotalCame { get; set; }
    [Key(16)] public long TotalPosted { get; set; }
    [Key(17)] public long[] EmigrantG { get; set; } = [];
}

[MessagePackObject]
public sealed class CaravanSave
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int Home { get; set; }
    [Key(2)] public int Region { get; set; }
    [Key(3)] public int Settlement { get; set; }
    [Key(4)] public int OwnerBusiness { get; set; }
    [Key(5)] public int Master { get; set; }
    [Key(6)] public int OwnerHousehold { get; set; }
    [Key(7)] public int Account { get; set; }
    [Key(8)] public byte[] Vehicles { get; set; } = [];
    [Key(9)] public int[] Crew { get; set; } = [];
    [Key(10)] public int Guards { get; set; }
    [Key(11)] public byte State { get; set; }
    [Key(12)] public int[] PathX { get; set; } = [];
    [Key(13)] public int[] PathY { get; set; } = [];
    [Key(14)] public byte[] PathClass { get; set; } = [];
    [Key(15)] public long[] PathCumMm { get; set; } = [];
    [Key(16)] public long[] TollAt { get; set; } = [];
    [Key(17)] public byte[] TollKind { get; set; } = [];
    [Key(18)] public int Neighbour { get; set; }
    [Key(19)] public int GateIndex { get; set; }
    [Key(20)] public long Dist { get; set; }
    [Key(21)] public long PrevDist { get; set; }
    [Key(22)] public long AnchorSec { get; set; }
    [Key(23)] public long WaitUntilSec { get; set; }
    [Key(24)] public long DepartSec { get; set; }
    [Key(25)] public long FoundedSec { get; set; }
    [Key(26)] public long ArrearsMites { get; set; }
    [Key(27)] public long ExpectedProfitMites { get; set; }
    [Key(28)] public long TripCostMites { get; set; }
    [Key(29)] public long FloatMites { get; set; }
    [Key(30)] public long PurchaseMites { get; set; }
    [Key(31)] public ushort[] WishGood { get; set; } = [];
    [Key(32)] public long[] WishUnits { get; set; } = [];
    [Key(33)] public int Trips { get; set; }
    [Key(34)] public int LostDay { get; set; }
    [Key(35)] public bool TradedAtGate { get; set; }
    [Key(36)] public long ProfitTotalMites { get; set; }
    [Key(37)] public int DangerX { get; set; }
    [Key(38)] public int[] WishDest { get; set; } = [];
    [Key(39)] public long DebtMites { get; set; }
    [Key(40)] public ulong DebtTo { get; set; }
    [Key(41)] public int Used { get; set; }
}

/// <summary>
/// The "continent" section: the 256 summaries, which roads are closed, what each detailed region and its neighbours still owe each other (<see cref="RegionLink"/>) and the caravans
/// on the road with their paths. The edges themselves are derived from the history and are not stored. Absent in saves from before the continent tick: such a world gets a
/// fresh continent on load (summaries rebuilt from the history and the region's head count).
/// </summary>
[MessagePackObject]
public sealed class ContinentSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public int Home { get; set; }
    [Key(2)] public long LastSeason { get; set; }
    [Key(3)] public SummarySave[] Summaries { get; set; } = [];
    [Key(4)] public bool[] EdgeClosed { get; set; } = [];
    [Key(5)] public LinkSave[] Links { get; set; } = [];
    [Key(6)] public CaravanSave[] Caravans { get; set; } = [];
    [Key(7)] public ulong[] NextSerials { get; set; } = [];

    public static ContinentSave Capture(ContinentState st)
    {
        var s = new ContinentSave
        {
            NextSerials = st.SerialState(), Home = st.Home, LastSeason = st.LastSeason, EdgeClosed = st.Edges.Select(e => e.Closed).ToArray(),
            Summaries = st.Summaries.Select(x => new SummarySave
            {
                Pop = x.Pop, Households = x.Households, Dwellings = x.Dwellings, Vacancies = x.Vacancies, StockG = (long[])x.StockG.Clone(), GrainCapacityNet = x.GrainCapacityNet,
                CoinTotal = x.CoinTotal, TreasuryCoin = x.TreasuryCoin, PriceIdx = (int[])x.PriceIdx.Clone(), CoverX10 = (int[])x.CoverX10.Clone(), FamineDays = x.FamineDays, Unrest = x.Unrest,
                Prosperity = x.Prosperity, Realm = x.Realm, WarFlags = x.WarFlags, PopRemainder = x.PopRemainder, PriceDay = x.PriceDay, LastDetailDay = x.LastDetailDay, Detailed = x.Detailed,
                NewsKind = x.News.Select(n => (byte)n.Kind).ToArray(), NewsOrigin = x.News.Select(n => n.Origin).ToArray(), NewsMagnitude = x.News.Select(n => n.Magnitude).ToArray(),
                NewsDay = x.News.Select(n => n.Day).ToArray(), NewsHops = x.News.Select(n => n.Hops).ToArray(),
            }).ToArray(),
            Links = st.Links.Select(kv => new LinkSave
            {
                Region = kv.Key, EmigrantPersons = kv.Value.EmigrantPersons, EmigrantHouseholds = kv.Value.EmigrantHouseholds, EmigrantCoin = kv.Value.EmigrantCoin, EmigrantMeasures = kv.Value.EmigrantMeasures,
                PendingPersons = kv.Value.PendingPersons, PendingHouseholds = kv.Value.PendingHouseholds, PendingCoin = kv.Value.PendingCoin, PendingMeasures = kv.Value.PendingMeasures,
                PendingFromRegion = kv.Value.PendingFromRegion, TakenPersons = kv.Value.TakenPersons, TakenCoin = kv.Value.TakenCoin, TakenMeasures = kv.Value.TakenMeasures, TotalLeft = kv.Value.TotalLeft, TotalCame = kv.Value.TotalCame, TotalPosted = kv.Value.TotalPosted, EmigrantG = (long[])kv.Value.EmigrantG.Clone(),
                Edges = kv.Value.Edges.Select(e => new EdgeTradeSave { Edge = e.Edge, Neighbour = e.Neighbour, PayBudgetMites = e.PayBudgetMites, SpareG = (long[])e.SpareG.Clone(), CoinNetIn = e.CoinNetIn, StockNetOut = (long[])e.StockNetOut.Clone(), TollMites = e.TollMites }).ToArray(),
            }).ToArray(),
            Caravans = st.Caravans.All.Select(c => new CaravanSave
            {
                Id = c.Id.Value, Home = c.Home, Region = c.Region, Settlement = c.Settlement, OwnerBusiness = c.OwnerBusiness, Master = c.Master, OwnerHousehold = c.OwnerHousehold, Account = c.Account.Index,
                Vehicles = c.Vehicles.Select(v => (byte)v).ToArray(), Crew = (int[])c.Crew.Clone(), Guards = c.Guards, State = (byte)c.State,
                PathX = (int[])c.Path.X.Clone(), PathY = (int[])c.Path.Y.Clone(), PathClass = (byte[])c.Path.Class.Clone(), PathCumMm = (long[])c.Path.CumMm.Clone(),
                TollAt = c.Path.Tolls.Select(t => t.AtMm).ToArray(), TollKind = c.Path.Tolls.Select(t => (byte)t.Kind).ToArray(),
                Neighbour = c.Neighbour, GateIndex = c.GateIndex, Dist = c.Dist, PrevDist = c.PrevDist, AnchorSec = c.AnchorSec, WaitUntilSec = c.WaitUntilSec, DepartSec = c.DepartSec, FoundedSec = c.FoundedSec,
                ArrearsMites = c.ArrearsMites, ExpectedProfitMites = c.ExpectedProfitMites, TripCostMites = c.TripCostMites, FloatMites = c.FloatMites, PurchaseMites = c.PurchaseMites,
                WishGood = c.Cargo.Select(l => l.Good).ToArray(), WishUnits = c.Cargo.Select(l => l.Units).ToArray(), WishDest = c.Cargo.Select(l => l.Dest).ToArray(), Trips = c.Trips, LostDay = c.LostDay, TradedAtGate = c.TradedAtGate,
                ProfitTotalMites = c.ProfitTotalMites, DangerX = c.DangerX, DebtMites = c.DebtMites, DebtTo = c.DebtTo, Used = c.Used,
            }).ToArray(),
        };
        return s;
    }

    /// <summary>Rebuilds the continent over the history's roads and puts the saved numbers into it.</summary>
    public ContinentState Restore(ulong seed, WorldHistory history)
    {
        var st = ContinentState.ForLoad(seed, history, Home);
        if (Summaries.Length != ContinentState.Regions) throw new SaveException("The continent section does not hold 256 regions.");
        st.LastSeason = LastSeason; st.SetSerialState(NextSerials);
        for (int i = 0; i < Summaries.Length; i++)
        {
            var x = Summaries[i]; var t = st.Summaries[i];
            t.Pop = x.Pop; t.Households = x.Households; t.Dwellings = x.Dwellings; t.Vacancies = x.Vacancies; t.StockG = (long[])x.StockG.Clone(); t.GrainCapacityNet = x.GrainCapacityNet;
            t.CoinTotal = x.CoinTotal; t.TreasuryCoin = x.TreasuryCoin; t.PriceIdx = (int[])x.PriceIdx.Clone(); t.CoverX10 = (int[])x.CoverX10.Clone(); t.FamineDays = x.FamineDays; t.Unrest = x.Unrest;
            t.Prosperity = x.Prosperity; t.Realm = x.Realm; t.WarFlags = x.WarFlags; t.PopRemainder = x.PopRemainder; t.PriceDay = x.PriceDay; t.LastDetailDay = x.LastDetailDay; t.Detailed = x.Detailed;
            t.News = [];
            for (int k = 0; k < x.NewsKind.Length; k++) t.News.Add(new NewsItem((NewsKind)x.NewsKind[k], x.NewsOrigin[k], x.NewsMagnitude[k], x.NewsDay[k], x.NewsHops[k]));
        }
        for (int i = 0; i < EdgeClosed.Length && i < st.Edges.Length; i++) st.Edges[i].Closed = EdgeClosed[i];
        foreach (var l in Links)
        {
            var r = st.Link(l.Region);
            r.EmigrantPersons = l.EmigrantPersons; r.EmigrantHouseholds = l.EmigrantHouseholds; r.EmigrantCoin = l.EmigrantCoin; r.EmigrantMeasures = l.EmigrantMeasures;
            r.PendingPersons = l.PendingPersons; r.PendingHouseholds = l.PendingHouseholds; r.PendingCoin = l.PendingCoin; r.PendingMeasures = l.PendingMeasures;
            r.PendingFromRegion = l.PendingFromRegion; r.TakenPersons = l.TakenPersons; r.TakenCoin = l.TakenCoin; r.TakenMeasures = l.TakenMeasures; r.TotalLeft = l.TotalLeft; r.TotalCame = l.TotalCame; r.TotalPosted = l.TotalPosted; if (l.EmigrantG.Length == r.EmigrantG.Length) r.EmigrantG = (long[])l.EmigrantG.Clone();
            foreach (var e in l.Edges)
            {
                var t = r.Edges.FirstOrDefault(q => q.Edge == e.Edge) ?? throw new SaveException("The continent section names a road that the history does not have.");
                t.PayBudgetMites = e.PayBudgetMites; t.SpareG = (long[])e.SpareG.Clone(); t.CoinNetIn = e.CoinNetIn; t.StockNetOut = (long[])e.StockNetOut.Clone(); t.TollMites = e.TollMites;
            }
        }
        foreach (var c in Caravans)
        {
            var path = new CaravanPath { X = (int[])c.PathX.Clone(), Y = (int[])c.PathY.Clone(), Class = (byte[])c.PathClass.Clone(), CumMm = (long[])c.PathCumMm.Clone() };
            path.Tolls = c.TollAt.Select((a, i) => new TollMark(a, (TollKind)c.TollKind[i])).ToArray();
            st.Caravans.Add(new Caravan
            {
                Id = new EntityId(c.Id), Home = c.Home, Region = c.Region, Settlement = c.Settlement, OwnerBusiness = c.OwnerBusiness, Master = c.Master, OwnerHousehold = c.OwnerHousehold,
                Account = new PurseId(c.Account), Vehicles = c.Vehicles.Select(v => (VehicleKind)v).ToArray(), Crew = (int[])c.Crew.Clone(), Guards = c.Guards, State = (CaravanState)c.State, Path = path,
                Neighbour = c.Neighbour, GateIndex = c.GateIndex, Dist = c.Dist, PrevDist = c.PrevDist, AnchorSec = c.AnchorSec, WaitUntilSec = c.WaitUntilSec, DepartSec = c.DepartSec, FoundedSec = c.FoundedSec,
                ArrearsMites = c.ArrearsMites, ExpectedProfitMites = c.ExpectedProfitMites, TripCostMites = c.TripCostMites, FloatMites = c.FloatMites, PurchaseMites = c.PurchaseMites,
                Cargo = c.WishGood.Select((g, i) => new CargoLine { Good = g, Units = c.WishUnits[i], Dest = c.WishDest.Length > i ? c.WishDest[i] : -1 }).ToList(), Trips = c.Trips, LostDay = c.LostDay, TradedAtGate = c.TradedAtGate,
                ProfitTotalMites = c.ProfitTotalMites, DangerX = c.DangerX, DebtMites = c.DebtMites, DebtTo = c.DebtTo, Used = c.Used,
            });
        }
        return st;
    }
}
