using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Trade;

/// <summary>The hero starts dressed for the season from the class wardrobe, as worn item rows of the inventory, and a spring midday does not chill them.</summary>
public class HeroOutfitTests(ITestOutputHelper output)
{
    private static (HeroParty Party, EconomyState Econ, ContainerRules Rules) Dressed(OriginDef origin, long sec)
    {
        var e = EconomyState.FromContent(EconFixture.Content, 1, 1);
        var hero = HeroRecord.FromOrigin("Hero", false, origin);
        return (HeroParty.Create(e, EconFixture.Content, new IdAllocator(), hero, sec), e, new ContainerRules(EconFixture.Content, e.Goods));
    }

    private static List<string> Worn(HeroParty p, EconomyState e) => [.. p.Carried.Equip.Keys.Select(id => e.Items.Get(id).Shape)];

    [Fact]
    public void Every_origin_starts_in_tunic_hose_footwear_and_a_cloak_and_a_hood_in_the_cold()
    {
        var y = EconFixture.Pop.NowYear;
        long spring = GameTime.FromCalendar(y, Season.Spring, 1, 12).Seconds, winter = GameTime.FromCalendar(y, Season.Winter, 5, 12).Seconds, summer = GameTime.FromCalendar(y, Season.Summer, 5, 12).Seconds;
        foreach (var o in EconFixture.Content.All<OriginDef>())
        {
            var (p, e, _) = Dressed(o, spring);
            var w = Worn(p, e);
            output.WriteLine($"{o.Id}: " + string.Join(", ", p.Carried.Rows(e).Select(r => $"{e.Items.At(r).Shape}={p.Carried.Equip.GetValueOrDefault(e.Items.At(r).Id)}")));
            Assert.Contains("tunic", w); Assert.Contains("hose", w); Assert.Contains("cloak", w); Assert.Contains("hood", w);
            Assert.True(w.Contains("shoes") || w.Contains("boots"), $"{o.Id}: no footwear ({string.Join(", ", w)})");
            Assert.False(w.Contains("shoes") && w.Contains("boots"));
            Assert.Equal(w.Count, w.Distinct().Count());
            var (pw, ew, _) = Dressed(o, winter);
            Assert.Contains("hood", Worn(pw, ew)); Assert.Contains("cloak", Worn(pw, ew));
            var (ps, es, _) = Dressed(o, summer);
            Assert.DoesNotContain("hood", Worn(ps, es));
            // the clothes are things carried: their mass is in the load
            Assert.True(p.Carried.MassG(e) > 3_000);
        }
    }

    [Fact]
    public void A_spring_midday_at_8_degrees_does_not_chill_the_dressed_hero()
    {
        var origin = EconFixture.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        long sec = GameTime.FromCalendar(EconFixture.Pop.NowYear, Season.Spring, 8, 12).Seconds;
        var (p, e, _) = Dressed(origin, sec);
        var book = new ClothingBook(EconFixture.Content);
        var t = book.Track(EconomyState.HeroOwner, Ravensguard.Sim.People.Sex.Male, HeroOutfit.ClassOf(origin.Id), Ravensguard.Sim.People.Occupation.None, 50, 1, sec, dress: false);
        foreach (var id in p.Carried.Equip.Keys)
        {
            var row = e.Items.Get(id);
            if (!book.Garments.TryGetValue(row.Shape, out var g)) continue;
            var c = book.Create(g, e.Items.Component(row.ComponentStart).Material, t.Person, row.ConditionPermille, 0, t.SizeCm3);
            if (book.CannotWear(t, c) == null) book.Wear(t, c);
        }
        var env = ExposureEnv.Outdoors(8, windMps: 2);
        long m0 = sec / 60;
        for (long m = 1; m <= 6 * 60; m++) book.StepTo(t, m0 + m, env, Activity.Walk);
        output.WriteLine($"worn {string.Join(", ", t.Worn.Select(i => book.Items.Get(i).Shape))}: Ieff {t.Last.IEffCloX100}, Ireq {t.Last.IReqCloX100}, core {t.CoreCenti}, state {t.State}");
        Assert.Equal(BodyState.Normal, t.State);
        Assert.True(t.Last.IEffCloX100 >= t.Last.IReqCloX100, $"walking at 8 C: {t.Last.IEffCloX100} clo x100 against {t.Last.IReqCloX100}");
        Assert.True(t.Last.IEffCloX100 >= 100);
    }
}
