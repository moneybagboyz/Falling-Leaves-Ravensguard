using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.People;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>P7-EA3c (overview 10c): children of a farming household help with the harvest and with nothing else.</summary>
public class FarmingChildrenTests(ITestOutputHelper output)
{
    /// <summary>One holder household (a man, levied) with children of the given ages; every parcel of the village is his.</summary>
    private static (Synth W, int Holder) World(params int[] childAges)
    {
        var w = Synth.Build(new SynthSpec(ParcelsPerSlot: 1, WorkersPerHolder: 1));
        int holder = 1; var hh = w.Pop.Households; var pt = w.Pop.People;
        int man = hh.Members(holder).First();
        foreach (int age in childAges)
        {
            int c = pt.Add(); pt.BirthDay[c] = w.Pop.NowDay - age * TimeConst.DaysPerYear - 5; pt.Household[c] = holder; pt.Occupation[c] = Occupation.None; hh.AddMember(holder, c);
        }
        w.Sys.IsEmployedElsewhere = p => p == man;                                // the man is on campaign for the whole year
        return (w, holder);
    }

    private static long Reaped(Synth w) { long n = 0; for (int p = 0; p < w.State.Parcels.Count; p++) n += w.State.Parcels.ReapDone[p]; return n; }

    [Fact]
    public void Section10c_AHouseholdOfOneLeviedManAndThreeChildren_ReapsMoreThanTheSameHouseholdWithout()
    {
        var (with, _) = World(8, 10, 13); var (without, _) = World();
        int y = with.Year;
        with.Run(y, 1, 40); without.Run(y, 1, 40);
        output.WriteLine($"reaped fields: with children {Reaped(with)}, without {Reaped(without)}; harvest {with.State.HarvestG} g against {without.State.HarvestG} g");
        Assert.True(Reaped(with) > Reaped(without), "the children's hands bring part of the harvest in");
        Assert.True(with.State.HarvestG > without.State.HarvestG);
        Assert.True(with.MassAudit() && without.MassAudit());
    }

    [Fact]
    public void Section10c_ChildrenOfSevenToEleven_NeverWorkAPloughOrSowTask_ButReapOnceTheGrainIsRipe()
    {
        var (w, _) = World(8, 10);                                                // no one old enough for any other task
        int y = w.Year; var P = w.State.Parcels;
        w.Run(y, 1, 24);                                                          // spring work, weeding, hay: nothing the children may do
        for (int p = 0; p < P.Count; p++)
            foreach (var t in new[] { FarmTask.PloughSpring, FarmTask.SowSpring, FarmTask.PloughFallow, FarmTask.SowWinter, FarmTask.Weed })
                Assert.Equal(0, P.Progress(p, t));
        Assert.Equal(0, Reaped(w));
        w.Run(y, 25, 15);                                                         // the winter grain is ripe from day 26
        Assert.True(Reaped(w) > 0, "the children reap their household's grain");
        for (int p = 0; p < P.Count; p++)
            foreach (var t in new[] { FarmTask.PloughSpring, FarmTask.SowSpring, FarmTask.PloughFallow, FarmTask.SowWinter })
                Assert.Equal(0, P.Progress(p, t));
    }

    [Fact]
    public void Section10c_ATwelveYearOldStaysAtHalfADay_ForEveryTask()
    {
        var (w, _) = World(13);
        int y = w.Year; var P = w.State.Parcels;
        w.Run(y, 1, 20);
        int ploughed = 0; for (int p = 0; p < P.Count; p++) ploughed += P.Progress(p, FarmTask.PloughSpring);
        Assert.True(ploughed > 0, "a thirteen-year-old ploughs and sows at half a day");
    }
}
