using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Politics;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Family;

/// <summary>The estates of NPCs (P6-E3, family-and-lifespan 4.7, FL-13): wills at the church, a steward for a minor heir's business until he is sixteen, a creditor who sells a claimed business after fifteen unpaid days.</summary>
public class FamilyEstateTests(ITestOutputHelper output)
{
    private sealed class Rig : IDisposable
    {
        public PoliticsRig P = new();
        public WorldState St => P.State;
        public PersonTable Pt => St.Population.People;
        public HouseholdTable Hht => St.Population.Households;
        public int Day => P.Day;
        public FamilyWorld Fw => FamilyWorld.From(St, P.R.Content);
        public Ledger Led => St.Economy!.Ledger;
        public long Coin(int hh) => Led.Balance(St.Economy!.HouseholdPurse[hh]).Mites;
        public void Fund(int hh, long mites) { var p = St.Economy!.HouseholdPurse[hh]; long have = Led.Balance(p).Mites; if (have < mites) Led.Mint(p, new Coin(mites - have), SourceReason.Seed); }
        public void Dispose() => P.Dispose();
        public int BirthdayIn(int row, int year) => Enumerable.Range(year * TimeConst.DaysPerYear, TimeConst.DaysPerYear + 1).First(d => Pt.AgeYears(row, d) > Pt.AgeYears(row, d - 1));
    }

    [Fact]
    public void FL13_AHeadWithMeansAndADislikedHeirMakesHisWillAtTheChurch_ATwentiethAYear_TheFeeToThePriest_NothingIsMadeOrLost()
    {
        using var r = new Rig(); var pt = r.Pt; var fw0 = r.Fw; int day = r.Day;
        var s = fw0.S;
        // a head of a house with kin the law would give it to, in a place with a priest
        int head = -1; List<PropertyHeir> heirs = [];
        for (int hh = 0; hh < r.Hht.Count && head < 0; hh++)
        {
            int h = r.Hht.Head[hh]; if (h < 0 || h == fw0.HeroRow || pt.Status[h] != PersonStatus.Alive || pt.AgeYears(h, day) < 30) continue;
            var hs = EstateSettler.Heirs(pt, h, day, kin: r.St.Population.Kin); int priest = NpcEstates.PriestOf(fw0, pt.HomeSettlement[h]);
            if (hs.Count >= 2 && priest >= 0 && priest != h && pt.Household[priest] != hh) { head = h; heirs = hs; }
        }
        Assert.True(head >= 0, "no head with kin and a priest in the test town");
        int hhead = pt.Household[head]; var id = pt.IdOf(head); r.Fund(hhead, 9_000);
        // the pure draw is the 20 percent of the spec
        int yes = 0; for (int y = 0; y < 10_000; y++) if (NpcEstates.MakesWill(s, 1234, id.Value, y)) yes++;
        Assert.InRange(yes, 1_850, 2_150);
        // an heir who trusts him as kin do (400) is no reason: no will, whatever the draw says
        int year = Enumerable.Range(day / TimeConst.DaysPerYear, 400).First(y => NpcEstates.MakesWill(s, r.St.Seed, id.Value, y));
        int bday = r.BirthdayIn(head, year);
        NpcEstates.Wills(r.Fw, bday);
        Assert.Null(r.St.Wills?.Of(id));
        // the eldest heir has come to loathe him (trust -400 in the relation table): the will is made, to the next of his kin
        var talk = r.St.Talk ??= new Ravensguard.Sim.Rumours.TalkState();
        talk.Relations.Met(head, heirs[0].Row, bday, new TalkSettingsDef { Id = "talk" }, -800, kin: true);
        Assert.True(talk.Relations.Of(head, heirs[0].Row, true).TrustPm < s.NpcWillDislikePm);
        int priestRow = NpcEstates.PriestOf(r.Fw, pt.HomeSettlement[head]); var priestPurse = r.St.Economy!.HouseholdPurse[pt.Household[priestRow]];
        long coin0 = r.Coin(hhead), pr0 = r.Led.Balance(priestPurse).Mites, total0 = r.Led.Audit().Circulating.Mites;
        Assert.True(NpcEstates.Wills(r.Fw, bday) >= 1);
        var will = r.St.Wills!.Of(id)!; Assert.NotNull(will);
        Assert.Equal(heirs[1].Person, will.AllTo); Assert.Equal(pt.IdOf(priestRow), will.Witness);
        Assert.Equal(coin0 - WillBook.FeeMites, r.Coin(hhead)); Assert.Equal(pr0 + WillBook.FeeMites, r.Led.Balance(priestPurse).Mites);
        Assert.Equal(total0, r.Led.Audit().Circulating.Mites);
        NpcEstates.Wills(r.Fw, bday);                                                                 // a second call the same day writes no second will
        Assert.Same(will, r.St.Wills.Of(id));
    }

    [Fact]
    public void FL13_AnNpcHeirOfThirteenWhoOwnsAShop_HasHisNearestAdultKinAsSteward_PaidADaysWageFromTheCashBox_UntilHeIsSixteen()
    {
        using var r = new Rig(); var pt = r.Pt; var fw = r.Fw; int day = r.Day;
        var prod = r.St.Production!;
        var b = prod.Businesses.First(x => x.State == BusinessState.Open && x.Owner >= 0 && x.Owner != fw.HeroRow && pt.Status[x.Owner] == PersonStatus.Alive && prod.KindOf(x).Stations.Length > 0 && !prod.KindOf(x).Trader);
        int heir = b.Owner;
        int uncle = Enumerable.Range(0, pt.Count).First(i => i != heir && i != fw.HeroRow && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, day) >= 30 && pt.Household[i] >= 0 && pt.Household[i] != pt.Household[heir]);
        pt.BirthDay[heir] = PersonTable.BirthDayFor(day, 13, 0, pt.AgingPace); pt.Mother[heir] = -1; pt.Father[heir] = uncle;       // his nearest adult kin: the guardian rule of Phase 5
        r.St.Population.ResetKin();
        Assert.Equal(1, NpcEstates.Stewards(r.Fw, day));
        var st = r.St.Family!.Stewards.Values.Single(); Assert.Equal(pt.IdOf(uncle).Value, st.Steward); Assert.Equal(pt.IdOf(heir).Value, st.Heir);
        Assert.False(b.OwnerWorks); Assert.Equal(heir, b.Owner);                                      // the boy owns it and does not work it
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "minor_steward");
        // the cash box pays the steward's day
        var led = r.Led; led.Mint(b.Purse, new Coin(2_000), SourceReason.Seed);
        long box0 = led.Balance(b.Purse).Mites, uncle0 = r.Coin(pt.Household[uncle]);
        Assert.Equal(0, NpcEstates.Stewards(r.Fw, day + 1));
        Assert.Equal(box0 - r.Fw.S.StewardWageMitesPerDay, led.Balance(b.Purse).Mites); Assert.Equal(uncle0 + r.Fw.S.StewardWageMitesPerDay, r.Coin(pt.Household[uncle]));
        // sixteen: the shop is his to work again
        pt.BirthDay[heir] = PersonTable.BirthDayFor(day + 2, 16, 0, pt.AgingPace);
        Assert.Equal(1, NpcEstates.Stewards(r.Fw, day + 2));
        Assert.Empty(r.St.Family?.Stewards.Values ?? (IEnumerable<Stewardship>)[]); Assert.True(b.OwnerWorks);
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "minor_of_age");
    }

    [Fact]
    public void FL13_ACreditorWhoClaimedAShopSellsItWhenItsStaffAreUnpaidFifteenDays_ThePriceToTheCreditor_TheShopAndItsPremisesToTheBuyer()
    {
        using var r = new Rig(); var pt = r.Pt; var hht = r.Hht; var fw = r.Fw; int day = r.Day;
        var prod = r.St.Production!;
        var b = prod.Businesses.First(x => x.State == BusinessState.Open && x.Owner >= 0 && x.Owner != fw.HeroRow && x.OwnerHousehold >= 0 && x.Staff.Count > 0 && hht.Head[x.OwnerHousehold] >= 0);
        int seller = b.OwnerHousehold;
        // the shop was claimed by the owner's house for a debt of an estate; its staff are owed
        var book = r.St.Succession ??= new SuccessionBook();
        var est = new Estate { Id = 9001, Dead = pt.IdOf(b.Owner), Day = day };
        est.Holdings.Add(new EstateEntry { Kind = EstateThing.Business, Id = b.Id, ClaimedBy = hht.IdOf(seller), To = hht.IdOf(seller), ToKind = PurseKind.Household, ValueMites = 1000 });
        book.Estates.Add(est);
        b.Owed[0] = 400;
        // a buyer: the best-off house of the place, with the coin to pay
        long price = Ravensguard.Sim.Production.OwnershipWiring.For(r.St, r.P.R.Content).ValueOf(b);
        int buyerHh = Enumerable.Range(0, hht.Count).First(h => h != seller && hht.MemberCount[h] > 0 && hht.Settlement[h] == b.Settlement && hht.Head[h] >= 0 && hht.Head[h] != fw.HeroRow && pt.Status[hht.Head[h]] == PersonStatus.Alive && pt.AgeYears(hht.Head[h], day) >= 16);
        r.Fund(buyerHh, price + 5_000);
        long sellerCoin0 = r.Coin(seller), buyerCoin0 = r.Coin(buyerHh), total0 = r.Led.Audit().Circulating.Mites;
        for (int d = day; d < day + 14; d++) { Assert.Equal(0, NpcEstates.Creditors(r.Fw, d)); b.Owed[0] = 400; }
        Assert.Equal(14, r.St.Family!.Distress[b.Id.Value]);                                         // fourteen days: not yet
        Assert.Equal(b.OwnerHousehold, seller);
        Assert.Equal(1, NpcEstates.Creditors(r.Fw, day + 14));                                          // the fifteenth
        Assert.Equal(buyerHh, b.OwnerHousehold); Assert.Equal(hht.Head[buyerHh], b.Owner);
        Assert.Equal(sellerCoin0 + price, r.Coin(seller)); Assert.Equal(buyerCoin0 - price, r.Coin(buyerHh));
        Assert.Equal(total0, r.Led.Audit().Circulating.Mites);
        Assert.Empty(r.St.Family?.Distress ?? new SortedDictionary<ulong, int>());
        var e = r.St.Chronicle.All.Single(x => x.TypeId == "business_sold_unpaid"); Assert.Equal("ch_note_staff_unpaid", e.Causes[0].NoteKey);
        // paid staff reset the count
        Assert.Equal(0, NpcEstates.Creditors(r.Fw, day + 15));
        _ = output;
    }

    [Fact]
    public void FL13_ADistressCountResets_WhenTheStaffAreNoLongerOwed_AndTheNewBookKeepsItsHashAndItsBytes()
    {
        using var r = new Rig(); var pt = r.Pt; var hht = r.Hht; int day = r.Day;
        var b = r.St.Production!.Businesses.First(x => x.State == BusinessState.Open && x.Owner >= 0 && x.OwnerHousehold >= 0 && x.Staff.Count > 0);
        var book = r.St.Succession ??= new SuccessionBook(); var est = new Estate { Id = 9002, Dead = pt.IdOf(b.Owner), Day = day };
        est.Holdings.Add(new EstateEntry { Kind = EstateThing.Business, Id = b.Id, ClaimedBy = hht.IdOf(b.OwnerHousehold) }); book.Estates.Add(est);
        b.Owed[0] = 100; NpcEstates.Creditors(r.Fw, day); NpcEstates.Creditors(r.Fw, day + 1);
        Assert.Equal(2, r.St.Family!.Distress[b.Id.Value]);
        b.Owed[0] = 0; NpcEstates.Creditors(r.Fw, day + 2);
        Assert.False(r.St.Family.Distress.ContainsKey(b.Id.Value));
        // the optional section round trip with a steward and a distress count
        r.St.Family.Stewards[7] = new Stewardship { Business = 7, Heir = 8, Steward = 9, Since = 10 }; r.St.Family.Distress[7] = 3;
        ulong hash = r.St.Family.Hash();
        var file = WorldSave.Capture(r.St, "b", "0.1");
        var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), r.P.R.Content, "b", null, r.P.R.W.Continent, r.P.R.W.History);
        Assert.Equal(hash, st2.Family!.Hash()); Assert.Equal(3, st2.Family.Distress[7]); Assert.Equal(9UL, st2.Family.Stewards[7].Steward);
        Assert.Equal(file.Sections[FamilySave.SectionName].ToArray(), WorldSave.Capture(st2, "b", "0.1").Sections[FamilySave.SectionName].ToArray());
    }
}
