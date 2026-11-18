using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Abstract;

// The Phase 2 economy as abstract-tick systems (overview section 4, decision I1). Each one finds the world's EconomyWorld in the service bag and does nothing when
// there is none, so worlds that only run the population systems tick as before. Registered by AbstractSystemRegistry.CreateDefault.

/// <summary>H5, hour 0: farming's seasonal steps (S1-S4, when the day is a season start) and the day's weather, field events, labour and harvest (farming spec section 4).</summary>
public sealed class FarmingDailySystem : AbstractSystem
{
    public FarmingDailySystem() : base("farming", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsFarming) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        ew.Farming.StepDay(ctx.Time);
        var all = ew.FarmChronicle.All;
        for (; ew.FarmChronicleDrained < all.Count; ew.FarmChronicleDrained++)
        {
            var en = all[ew.FarmChronicleDrained];
            ctx.Outbox.Chronicle(en.When, en.TypeId, en.Subjects, en.Causes, [.. en.Data.Select(kv => (kv.Key, kv.Value))]);
        }
    }
}

/// <summary>H4 slice: a household with more grain than it needs until the next threshing (plus its seed) sells a sixth of the surplus a day: merchants first, the rest as a lot.</summary>
public sealed class FarmSalesSystem : AbstractSystem
{
    public const int KeepMarginDays = 12, ThreshedByDoy = 44, SpreadDays = 3;
    private ushort[]? _grains;

    public FarmSalesSystem() : base("farm-sales", TickPhase.H4DailySlice, SystemOrder.FarmSales) { }

    public override void Step(TickContext ctx)
    {
        var w = ctx.World; var ew = w.Get<EconomyWorld>(); if (ew == null) return;
        var e = w.Economy; var hht = w.Households; var pop = w.Population;
        _grains ??= [.. e.Food.Staples.Where(g => e.Food.IsGrain(g)), e.Goods.IdOf("peas"), e.Goods.IdOf("beans")];
        int day = (int)ctx.Day, doy = ctx.DayOfYear, t = w.Env.TMeanC(ctx.Day);
        int toHarvest = ((ThreshedByDoy - doy) % 60 + 60) % 60;
        for (int hh = ctx.HourOfDay; hh < hht.Count; hh += 24)
        {
            if (hht.MemberCount[hh] == 0) continue;
            if (hht.ParcelCount[hh] == 0 && hht.Class[hh] != SocialClass.Gentry) continue;
            var store = e.HouseholdStore[hh];
            SellProducts(ew, hh, store, day);
            long have = 0; foreach (ushort g in _grains) have += store.Qty(g);
            if (have <= 0) continue;
            var need = HouseholdNeeds.Basket(e, w.People, hht, hh, day, t);
            long keep = HouseholdNeeds.GrainEquivalentG(need.StapleX100 + need.RelishX100) * (toHarvest + KeepMarginDays) + e.SeedReserveG[hh];
            long surplus = have - keep;
            if (surplus <= 0) continue;
            long today = Math.Max(1, surplus / SpreadDays);
            var book = ew.Markets.BookForHousehold(hh); var party = ew.Markets.HouseholdParty(hh);
            long left = today;
            foreach (ushort g in _grains)
            {
                long q = Math.Min(store.Qty(g), Math.Max(1, today * store.Qty(g) / have));
                q = Math.Min(q, left);
                if (q <= 0) continue;
                long sold = book.SellOrList(party, g, q, 800, day);
                left -= sold; ew.Counters.FarmSoldG += sold;
            }
        }
    }

    private ushort[]? _products;

    /// <summary>The herds' products: wool and slaughtered animals go to market at once, cheese above a month's keep of the household sells a sixth a day.</summary>
    private void SellProducts(EconomyWorld ew, int hh, Store store, int day)
    {
        var cat = ew.World.Economy.Goods;
        _products ??= [cat.IdOf("wool_raw"), cat.IdOf("sheep"), cat.IdOf("cow"), cat.IdOf("cheese")];
        var book = ew.Markets.BookForHousehold(hh); ushort cheese = _products[3];
        for (int i = 0; i < _products.Length; i++)
        {
            ushort g = _products[i]; long have = store.Qty(g); if (have <= 0) continue;
            long q = have;
            if (g == cheese) { long keep = (long)ew.World.Households.MemberCount[hh] * 1500; q = Math.Max(0, have - keep) / 6; if (q <= 0) continue; }
            ew.Markets.BookForHousehold(hh).SellOrList(ew.Markets.HouseholdParty(hh), g, q, 850, day);
        }
    }
}

/// <summary>H3: one market session in every open book.</summary>
public sealed class MarketSessionSystem : AbstractSystem
{
    public MarketSessionSystem() : base("market-session", TickPhase.H3Market, SystemOrder.Markets) { }
    public override void Step(TickContext ctx) { var ew = ctx.World.Get<EconomyWorld>(); ew?.Markets.HourlySession(ctx.Time); }
}

/// <summary>H4 slice, after the needs and the production slice: each household posts its buy orders for the next session (households spec 3.4).</summary>
public sealed class HouseholdShoppingSystem : AbstractSystem
{
    public HouseholdShoppingSystem() : base("household-shopping", TickPhase.H4DailySlice, SystemOrder.MarketDaily) { }

    public override void Step(TickContext ctx)
    {
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        var hht = ctx.World.Households; int t = ctx.World.Env.TMeanC(ctx.Day), day = (int)ctx.Day;
        for (int hh = ctx.HourOfDay; hh < hht.Count; hh += 24) if (hht.MemberCount[hh] > 0) ew.Markets.PostHouseholdOrders(hh, day, t);
    }
}

/// <summary>H5: a day of spoilage in every store that is not a household's (barns, granaries, fodder, business and market stores), then the books re-sync their lots.</summary>
public sealed class StoreSpoilageSystem : AbstractSystem
{
    public StoreSpoilageSystem() : base("store-spoilage", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsSpoilage) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        int t = ctx.World.Env.TMeanC(ctx.Day);
        foreach (var s in ctx.World.Economy.OtherStores.Values) s.ApplySpoilage(ctx.World.Seed, ctx.Hour, t);
        foreach (var b in ew.Markets.AllBooks()) b.SyncAll();
    }
}

/// <summary>H5: the parish feeds the hungry from its granary and sells the rest of the tithe.</summary>
public sealed class ParishSystem : AbstractSystem
{
    public ParishSystem() : base("parish", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsParish) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        ew.Parish.Daily(ctx.Time);
        ew.Counters.CharityG = ew.Parish.CharityG; ew.Counters.ParishSoldG = ew.Parish.SoldG;
    }
}

/// <summary>H5: the labour market: dead staff leave, owners succeed, businesses hire the idle (households spec 3.5, production spec 3.3).</summary>
public sealed class LabourSystem : AbstractSystem
{
    public LabourSystem() : base("labour-market", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsLabour) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        ctx.World.Get<EconomyWorld>()?.Labour.Daily(ctx.Time);
    }
}

/// <summary>H5: structural demand, the demand EMA, lot expiry and import requests of every book.</summary>
public sealed class MarketDailySystem : AbstractSystem
{
    public MarketDailySystem() : base("market-daily", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsMarkets) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        ctx.World.Get<EconomyWorld>()?.Markets.DailyGlobals(ctx.Time, ctx.World.Env.TMeanC(ctx.Day));
    }
}

/// <summary>H5: the day's trade with the continent (imports of what a book lacks, exports of what it holds beyond its cover).</summary>
public sealed class ContinentTradeSystem : AbstractSystem
{
    public ContinentTradeSystem() : base("continent-trade", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsMarkets + 10) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        ctx.World.Get<EconomyWorld>()?.Trade.Daily(ctx.Time);
    }
}

/// <summary>H2: one hour of work in every business (production spec 3.1).</summary>
public sealed class ProductionWorkSystem : AbstractSystem
{
    public ProductionWorkSystem() : base("production-work", TickPhase.H2Work, SystemOrder.Production) { }
    public override void Step(TickContext ctx) => ctx.World.Get<EconomyWorld>()?.Production.WorkHour(ctx.Time);
}

/// <summary>H4 slice: wages, sales and planning of the businesses of the hour.</summary>
public sealed class ProductionSliceSystem : AbstractSystem
{
    public ProductionSliceSystem() : base("production-slice", TickPhase.H4DailySlice, SystemOrder.ProductionDaily) { }
    public override void Step(TickContext ctx) => ctx.World.Get<EconomyWorld>()?.Production.DailySlice(ctx.Time);
}

/// <summary>H5: vacancy counters and wage offers.</summary>
public sealed class ProductionGlobalsSystem : AbstractSystem
{
    public ProductionGlobalsSystem() : base("production-daily", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsProduction) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        ctx.World.Get<EconomyWorld>()?.Production.DailyGlobals(ctx.Time);
    }
}

/// <summary>H6 / S3 at hour 0: regrowth of the tile stocks.</summary>
public sealed class ResourceRegrowthSystem : AbstractSystem
{
    public ResourceRegrowthSystem() : base("resource-regrowth", TickPhase.H6Seasonal, SystemOrder.S3Extraction) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        ew.Resources.Regrow(ctx.Time, ew.Content.Settings);
    }
}

/// <summary>H6 / S5 at hour 0: dividends, closures and training (production), before the price drift reads the coin the businesses hold.</summary>
public sealed class BusinessSeasonalSystem : AbstractSystem
{
    public BusinessSeasonalSystem() : base("business-seasonal", TickPhase.H6Seasonal, SystemOrder.S5Businesses) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        ew.Production.Seasonal(ctx.Time);
        ew.Labour.SeasonalTraining();
    }
}

/// <summary>H6 / S5 at hour 0: every book's reference prices drift toward the local coin per person.</summary>
public sealed class PriceDriftSystem : AbstractSystem
{
    public PriceDriftSystem() : base("price-drift", TickPhase.H6Seasonal, SystemOrder.S5MintPrices) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0 || !ctx.IsSeasonStartDay) return;
        ctx.World.Get<EconomyWorld>()?.Markets.SeasonalDrift(ctx.Time);
    }
}
