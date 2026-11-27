using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-5 (overview 4, "raiders land from a ship and you fight them on the beach"), as the build stands: two longships beach at dawn with seventy raiders made as real persons of the north (guards stay with the ships), the village a
/// kilometre and a half inland is raided (grain into the ships' holds as real mass, coin by ledger <c>Plunder</c> into a strongbox, captives by the culture's law), the bell rings, the lords hear it, the warband is sent and the beach is fought
/// with the abstract band fight; then the ships push off and the men leave through the emigration paths, or the raid is caught with the ships on the sand. The chronicle: <c>sea_raid</c> caused by the war, <c>raiders_caught</c> by the raid.
/// A save in the middle of the raid re-saves byte for byte, naval section included.
/// <b>Not built, not asserted:</b> the battle bubble on the beach (the beach fight is the abstract band fight), the hero's bell interrupt, the hero boarding a longship pushing off, and a man in mail drowning by the breath rule at that boarding (the
/// deck fight and the drowning are SH-12's own tests on a deck, not wired into the raid).
/// </summary>
public class Ov5SeaRaidTests(ITestOutputHelper output)
{
    private static readonly SeaRules Rules = SeaRules.From(EconFixture.Content);

    [Fact]
    public void OV5_TwoLongshipsBeachAtDawn_SeventyRealRaiders_TheVillageIsRaided_TheBellRings_TheWarbandFightsOnTheBeach_PlunderByLedger_OneChain()
    {
        using var k = new ArmyRig(); k.Raised();
        var aw = k.Aw; var econ = k.State.Economy!; var ledger = econ.Ledger; var region = k.State.Population.Region; var rides = VehicleDesk.BookOf(k.State, EconFixture.Content); var pt = k.State.Population.People;
        int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm;
        var decl = RuleChron.Of(k.State.Chronicle, k.State.Ids).Chron(GameTime.FromGameHours(k.Now), "h_war_declared", "ch_h_war_declared", [], [], [new Cause(CauseKind.Diplomacy, null, "", "ch_note_h_cb_insult")]);
        k.Pol.Wars.Add(new War { Serial = k.Pol.NextWar++, Attacker = raider, Defender = victim, StartDay = k.Day, Cb = CbKind.Insult, DeclEntry = decl.Value });
        int village = -1;
        foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == victim).OrderBy(f => f.Fief))
            foreach (int s in region.Fiefs[f.Fief].Settlements)
                if (village < 0 && region.Settlements[s].Class != SettlementClass.Town && k.Book.WarbandOfFief(f.Fief) is { Members.Count: > 0 } && aw.Households.MemberCount.Select((m, h) => (m, h)).Count(x => x.m > 0 && aw.Households.Settlement[x.h] == s) >= 3) village = s;
        Assert.True(village >= 0); var st = region.Settlements[village];
        int north = Enumerable.Range(0, 64).First(i => Ravensguard.Sim.Faith.ReligionBook.CultureOf(EconFixture.Content, i)?.Id == "north");
        long landHour = (k.Now / 24 + 1) * 24 + Rules.D.LandHourOfDay;
        var raid = new SeaRaid { Realm = raider, Victim = victim, Region = k.State.HomeRegion.Id, Settlement = village, Culture = north, War = true, PlanShips = 2, PlanMen = 70, PlannedHour = k.Now, BeachXmm = (st.CenterX + 1500L) * 1000, BeachYmm = st.CenterY * 1000L, LandHour = landHour };
        var book = k.State.Naval ??= new NavalBook();
        var pc = PoliticsEngine.Context(k.R.Sim, k.Pol, k.Rule, RuleChron.Of(k.State.Chronicle, k.State.Ids), k.Day);
        aw.Enlist(k.Env(), book, raid, pc);
        long alive0 = aw.W.Vital.Alive; long coin0 = ledger.Audit().InPurses.Mites, sunk0 = ledger.Sunk(SinkReason.Emigrate).Mites;

        k.To(landHour - 1); Assert.Equal(RaidState.Sailing, raid.State);                                                     // nothing on the beach before dawn
        k.To(landHour); Assert.Equal(RaidState.Landed, raid.State);
        Assert.Equal(2, raid.Ships.Count);
        foreach (ulong s in raid.Ships) { var v = rides.Vehicles.Find(new EntityId(s))!; Assert.Equal("longship", rides.Templates.At(v.Template).Id); Assert.Equal(WaterState.Aground, v.Water); }
        Assert.Equal(70, raid.Raiders.Count + raid.Guards.Count);
        foreach (ulong m in raid.Raiders.Concat(raid.Guards)) { int r = pt.IndexOf(new EntityId(m)); Assert.True(r >= 0 && pt.Status[r] == PersonStatus.Alive); Assert.Equal(north, pt.Culture[r]); Assert.Equal(-1, pt.Household[r]); }
        Assert.InRange(aw.W.Vital.Alive, alive0 + 60, alive0 + 90);                                                           // seventy more people, none made twice

        k.To(landHour + Rules.D.RunHours);
        Assert.Equal(RaidState.Raiding, raid.State);
        Assert.True(raid.Households >= 1 && (raid.TakenG > 0 || raid.TakenCoin > 0));
        Assert.True(raid.Ships.Sum(s => rides.Vehicles.Find(new EntityId(s))!.Cargo.TotalMassG()) > 0, "the grain lies in the holds");
        Assert.True(ledger.TryFind(PurseKind.Chest, new EntityId(raid.Ships[0]), out var box)); Assert.Equal(raid.TakenCoin, ledger.Balance(box).Mites);   // plunder by ledger, in the strongbox
        Assert.True(raid.TakenCoin > 0); Assert.True(ledger.Audit().IsConserved); Assert.True(econ.MassAuditHolds());
        Assert.Equal(raid.LootHour, raid.BellHour);
        Assert.Contains(k.Book.Reports, n => n.Realm == raider && n.Place == village);                                      // the bell: the lords hear it
        Assert.True(raid.Defender >= 0 && raid.FightHour > raid.LootHour);                                                  // the warband is sent

        // saved in the middle of the raid, naval section and all
        var (back, bytes) = P7Game.RoundTrip(k.State, k.R.W, "OV-5 mid-raid");
        Assert.Contains(NavalSave.SectionName, SaveCodec.Decode(bytes).Sections.Keys); Assert.Equal(book.Hash(), back.Naval!.Hash());

        for (long h = landHour + 2; h <= landHour + 12 && !raid.Over; h++) k.To(h);
        Assert.True(raid.Over); Assert.True(raid.Fought, "the warband met them on the beach before the ships pushed off");
        var entry = k.Entries("sea_raid").Single(); Assert.Contains(entry.Causes, c => c.Kind == CauseKind.War && c.EventId == decl); Assert.Equal(entry.Id.Value, raid.Entry);
        Assert.Equal(0, ledger.Balance(box).Mites);                                                                          // the strongbox is emptied either way
        if (raid.State == RaidState.Caught)
        {
            var caught = k.Entries("raiders_caught").Single(); Assert.Contains(P7Game.Chain(k.State, caught), c => c.TypeId == "sea_raid");
            Assert.Equal(coin0, ledger.Audit().InPurses.Mites);                                                              // the box went to the captor: nothing left the books
        }
        else
        {
            Assert.Equal(RaidState.Gone, raid.State); Assert.True(ledger.Sunk(SinkReason.Emigrate).Mites - sunk0 >= raid.TakenCoin);   // the loot left as emigrated coin
            Assert.All(raid.Raiders.Concat(raid.Guards), m => Assert.Equal(PersonStatus.Departed, pt.Status[pt.IndexOf(new EntityId(m))]));
        }
        Assert.True(econ.MassAuditHolds());
        output.WriteLine($"raid {raid.State}: {raid.Households} households, {raid.TakenG} g, {raid.TakenCoin} mites, {raid.Captives.Count} captives, fought {raid.Fought}, raiders left {raid.Raiders.Count}, dead {raid.Dead}");
        var (_, after) = P7Game.RoundTrip(k.State, k.R.W, "OV-5 after the raid"); Assert.True(after.Length > 0);
    }
}
