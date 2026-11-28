using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.Save;
using Xunit;

namespace Ravensguard.Sim.Tests.Holdings;

/// <summary>P8-BL-1 (D13, balance pass 3.2, BL-6): soil work late in a long game. The holders' own households do it first, in idle hours; what they have not done in thirty days goes to the day-labour pool at the posted wage.</summary>
public partial class SoilWorksTests
{
    [Fact]
    public void BL6_AHoldersOwnHouseholdWorksItsPoorField_InIdleHours_WithNoContractAndNoEscrow_AndTheFieldGainsItsPoints()
    {
        var r = Start(ownHands: true); using var _ = r.Rt;
        var (s, p, sw, farm) = (r.S, r.P, r.Sw, r.Farm);
        var e = s.Economy!; var F = farm.State.Fields;
        sw.HasMarlPit = _ => false;
        var (parcel, field, hh, place) = NpcField(r, pl => sw.DungLeftKg(pl, p.Day) >= r.Lw.Rules.ManureKg);
        OnlyThisPoor(r, place, field, 40);
        var holder = s.Population.Households.IdOf(hh);
        p.ToSeasonDay();
        var work = sw.Book.Works.First(x => x.Field == field);
        Assert.Equal(0, work.Contract); Assert.Equal(holder, work.Issuer);                              // no contract, nobody is paid
        Assert.DoesNotContain(s.Contracts == null ? [] : s.Contracts.Contracts.Values.ToList(), c => c.Kind == "soil_improvement" && c.Issuer == holder);
        int fert0 = F.Fertility[field];
        for (int d = 0; d < 60 && work.State != SoilState.Done; d++) p.Days(1);
        output.WriteLine($"own hours {sw.OwnHoursDone}, jobs {sw.OwnJobsDone}, state {work.State}, fertility {fert0} -> {F.Fertility[field]}");
        Assert.Equal(SoilState.Done, work.State);
        Assert.Equal(fert0 + sw.AmountOf(SoilKind.Manure), F.Fertility[field]);
        Assert.True(sw.OwnHoursDone >= sw.HoursOf(SoilKind.Manure)); Assert.True(sw.OwnJobsDone >= 1);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.True(e.MassAuditHolds());
        Assert.Contains(s.Chronicle.All, en => en.TypeId == "soil_improved");
    }

    [Fact]
    public void BL6_TheHouseholdsOwnJob_IsSavedAsItIs_AndNothingOfTheIdleHoursIsSaved()
    {
        var r = Start(ownHands: true); using var _ = r.Rt;
        r.Sw.HasMarlPit = _ => false;
        var (_, field, _, place) = NpcField(r, pl => r.Sw.DungLeftKg(pl, r.P.Day) >= r.Lw.Rules.ManureKg);
        OnlyThisPoor(r, place, field, 40);
        r.P.ToSeasonDay();
        Assert.Contains(r.Sw.Book.Works, x => x.Field == field && x.Contract == 0);
        var saved = SaveCodec.Encode(WorldSave.Capture(r.S, "h", "0.4.0-dev"));
        var back = WorldSave.Restore(SaveCodec.Decode(saved), r.W.Content, "h", null, r.W.Continent, r.W.History);
        var bw = back.Soil!.Works.First(x => x.Field == field);
        Assert.Equal(0, bw.Contract); Assert.Equal(r.Sw.Book.Works.First(x => x.Field == field).Issuer, bw.Issuer);
    }
}
