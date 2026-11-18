using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// A private, mutable copy of the seed-1234 world (population cloned, economy created fresh) for the abstract-tick tests, plus the stand-in food supply the
/// tests use until the farming and markets systems register: <see cref="HarvestStub"/> gives each household its day's needs, scaled by a yield function.
/// </summary>
public static class AbstractFixture
{
    public const ulong Seed = EconFixture.Seed;

    public static NamePools Names(Population pop) => new(EconFixture.Content.All<NamePoolDef>(), pop.Region.Seed ^ 0x50455031UL);

    /// <summary>A fresh world of the shared seed-1234 region at spring day 1 of the generation year. Cheap enough to build per test (about 150 ms).</summary>
    public static SimWorld NewWorld(VitalRates? rates = null, bool seedGoods = true)
    {
        var pop = EconFixture.Pop.Clone();
        var econ = EconomyState.Create(pop, EconFixture.Content, Seed, seedGoods);
        var w = new SimWorld(Seed, pop, econ, names: Names(pop), state: new AbstractState { HourDone = GameTime.FromCalendar(pop.NowYear, Season.Spring, 1).TotalHours });
        if (rates != null) w.Rates = rates;
        VitalSeeding.SeedPregnancies(w, GameTime.FromGameHours(w.State.HourDone));
        return w;
    }

    /// <summary>The default systems of the abstract tick plus the harvest stub (yield 1000 permille unless a function is given).</summary>
    public static AbstractSystemRegistry Registry(Func<long, int>? yieldPermille = null)
    {
        var r = AbstractSystemRegistry.CreateDefault(discover: false);
        r.Add(new HarvestStub(yieldPermille ?? (_ => 1000)));
        return r;
    }

    public static WorldState AsWorldState(SimWorld w, GameTime now)
    {
        var b = EconFixture.Shared;
        var ws = new WorldState
        {
            ContinentSettings = b.Cs, HistorySettings = b.Hs, RegionSettings = b.Rs, EmbarkCellX = b.CellX, EmbarkCellY = b.CellY, Continent = b.Continent, History = b.History,
            Population = w.Population, Clock = new SimClock(now), Ids = w.Ids, Chunks = new ChunkStore(), Chronicle = w.Chronicle, Economy = w.Economy, Abstract = w.State,
        };
        return ws;
    }

    public static (long Alive, int Households, double KidsPct, double ElderPct) Census(SimWorld w)
    {
        var p = w.People; long day = w.State.HourDone / 24; long alive = 0, kids = 0, elders = 0;
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            alive++; int age = w.AgeOn(i, day);
            if (age < 15) kids++; else if (age >= 50) elders++;
        }
        int hh = 0; for (int h = 0; h < w.Households.Count; h++) if (w.Households.MemberCount[h] > 0) hh++;
        return (alive, hh, 100.0 * kids / alive, 100.0 * elders / alive);
    }
}

/// <summary>
/// Test-only stand-in for farming and markets: each household whose slice it is receives the staples, fuel, salt and ale of its day's needs, times the yield of
/// the day (permille). It runs just before the household needs (<see cref="SystemOrder.Provisioning"/>), so a yield of 1000 keeps everyone fed and 0 starves them.
/// </summary>
public sealed class HarvestStub(Func<long, int> yieldPermille) : AbstractSystem("test-harvest-stub", TickPhase.H4DailySlice, SystemOrder.Provisioning)
{
    public override void Step(TickContext ctx)
    {
        var w = ctx.World; var e = w.Economy; var hht = w.Households;
        long day = ctx.Day; int y = yieldPermille(day);
        if (y <= 0) return;
        int t = w.Env.TMeanC(day);
        ushort wheat = e.Goods.IdOf("wheat"), wood = e.Food.Firewood, salt = e.Food.Salt, ale = e.Food.Ale;
        for (int hh = ctx.HourOfDay; hh < hht.Count; hh += 24)
        {
            if (hht.MemberCount[hh] == 0) continue;
            var b = HouseholdNeeds.Basket(e, w.People, hht, hh, (int)day, t);
            var s = e.HouseholdStore[hh];
            s.Create(wheat, HouseholdNeeds.GrainEquivalentG(b.StapleX100 + b.RelishX100) * y / 1000, CreateReason.Harvest);
            s.Create(wood, b.FuelG * y / 1000, CreateReason.Cut);
            s.Create(salt, b.SaltG * y / 1000, CreateReason.Other);
            s.Create(ale, b.AleMl * e.Goods.Def(ale).LitreMassG / 1000 * y / 1000, CreateReason.Brewed);
        }
    }
}
