using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Trade;
using Xunit;

namespace Ravensguard.Sim.Tests.Bondage;

/// <summary>
/// Slaves in the hero's party (overview 10a, decided with the user): a slave told to follow is a squad member, fights only when armed, and bolts below morale 40 unless chained; one who bolts is a runaway by the daily path.
/// SP-1 the rule table, SP-2 the desk (follow, arm, disarm, send back), SP-3 the muster and the flight, SP-4 the daily pass and the save.
/// </summary>
public class SlavePartyTests
{
    private static EntityId Someone(FaithRig r, int skip)
    {
        var pt = r.People; int n = 0;
        for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i) && pt.IdOf(i) != r.Hero && pt.AgeYears(i, 1_000_000) >= 16 && n++ == skip) return pt.IdOf(i);
        throw new InvalidOperationException("nobody");
    }

    private static Bond Own(FaithRig r, EntityId slave, int morale = 50)
    {
        var st = r.State; var book = st.Bonds ??= new BondBook();
        var b = Slavery.Make(BondWiring.World(st, r.Content), book, slave, r.Hero, BondKind.Chattel, BondOrigin.Sold, 0, 0, 9_000, 0, 0);
        b.Morale = morale; b.Chain = Restraint.Bound; b.Place = 0;
        return b;
    }

    /// <summary>A spare spear in the hero's pack (not equipped).</summary>
    private static EntityId SpareSpear(FaithRig r) => r.Ctx.Party.Carried.MakeItem(r.Ctx.Econ, r.Content, r.Ctx.Rules, r.Ctx.Factory, r.State.Ids, "spear", "iron");

    // ------------------------------------------------------------------ SP-1

    [Theory]
    [InlineData(80, Restraint.Bound, true, SlaveRole.Fights)]
    [InlineData(80, Restraint.Bound, false, SlaveRole.Stays)]
    [InlineData(40, Restraint.None, true, SlaveRole.Fights)]            // 40 is not below 40
    [InlineData(39, Restraint.None, true, SlaveRole.Flees)]             // an armed slave bolts too
    [InlineData(39, Restraint.None, false, SlaveRole.Flees)]
    [InlineData(0, Restraint.Chained, true, SlaveRole.Fights)]           // chained: he cannot run, so he fights if armed
    [InlineData(0, Restraint.Chained, false, SlaveRole.Stays)]
    [InlineData(10, Restraint.Bound, false, SlaveRole.Stays)]            // bound is held, not free to bolt (the daily roll's own test is Chain None)
    public void SP1_TheRuleTable(int morale, Restraint chain, bool armed, SlaveRole want)
        => Assert.Equal(want, SlaveParty.RoleOf(new Bond { Morale = morale, Chain = chain }, armed, 40));

    // ------------------------------------------------------------------ SP-2

    [Fact]
    public void SP2_FollowIsASquadSeat_NoWage_NotTheCap_AndTheSpareSwordMovesToHim_AndBack()
    {
        using var r = new FaithRig();
        var slave = Someone(r, 0); var b = Own(r, slave); long sec = 100L * 86_400 + 3600;
        var squad = r.State.Party!.Squad;
        Assert.Empty(squad.Members);
        Assert.True(r.Do(BondDesk.Work(slave, BondWork.Follow, sec), sec).Ok);
        var m = Assert.Single(squad.Members); Assert.Equal(FollowerOrigin.Slave, m.Origin); Assert.Equal(slave, m.Person); Assert.Equal(0, m.WageMitesPerDay); Assert.Equal(SquadOrder.Follow, m.Order);
        Assert.True(m.Active);
        Assert.False(r.Do(BondDesk.Arm(slave, sec), sec).Ok);                                  // no spare weapon yet
        var items = r.Ctx.Econ.Items.Count; var spear = SpareSpear(r);
        Assert.True(r.Do(BondDesk.Arm(slave, sec), sec).Ok);
        Assert.Equal(slave, r.Ctx.Econ.Items.Get(spear).Owner);
        Assert.False(r.Do(BondDesk.Arm(slave, sec), sec).Ok);                                  // one weapon each
        Assert.Equal(items + 1, r.Ctx.Econ.Items.Count);                                       // nothing made or lost by the hand-over
        var view = BondView.Of(r.State, r.Content, 100)!; Assert.True(view.Owned.Single().Armed);
        Assert.True(r.Do(BondDesk.Disarm(slave, sec), sec).Ok);
        Assert.Equal(r.Hero, r.Ctx.Econ.Items.Get(spear).Owner);
        Assert.False(r.Do(BondDesk.Arm(Someone(r, 5), sec), sec).Ok);                          // not his slave
        // sent back to the house through the party's dismiss: no severance, the bond stays
        Assert.True(r.Do(new HeroCommand { Kind = HeroCommandKind.Dismiss, Person = slave, IssuedSec = sec, Origin = CommandOrigin.Human }, sec).Ok);
        Assert.Empty(squad.Members); Assert.Equal(BondWork.House, b.Work); Assert.True(r.State.Bonds!.Has(slave));
        // follow again, then other work: he leaves the party
        Assert.True(r.Do(BondDesk.Work(slave, BondWork.Follow, sec), sec).Ok); Assert.Single(squad.Members);
        Assert.True(r.Do(BondDesk.Work(slave, BondWork.Fields, sec), sec).Ok); Assert.Empty(squad.Members);
    }

    // ------------------------------------------------------------------ SP-3

    [Fact]
    public void SP3_TheMusterSaysWhoFightsWhoStandsBackAndWhoBolts_AFleeingSlaveIsARunaway_AChainedOneCannotRun()
    {
        using var r = new FaithRig();
        var armed = Someone(r, 0); var bare = Someone(r, 1); var shaky = Someone(r, 2); var chained = Someone(r, 3);
        long sec = 100L * 86_400 + 3600; var rules = BondRules.From(r.Content);
        foreach (var s in new[] { armed, bare, shaky, chained }) { Own(r, s); Assert.True(r.Do(BondDesk.Work(s, BondWork.Follow, sec), sec).Ok); }
        SpareSpear(r); Assert.True(r.Do(BondDesk.Arm(armed, sec), sec).Ok);
        var book = r.State.Bonds!;
        book.Of(shaky)!.Morale = 30; book.Of(shaky)!.Chain = Restraint.None;
        book.Of(chained)!.Morale = 5; book.Of(chained)!.Chain = Restraint.Chained;
        var stands = SlaveParty.Muster(book, r.State.Party!.Squad, r.Ctx.Econ, r.Content, rules);
        Assert.Equal(4, stands.Count);
        Assert.Equal(SlaveRole.Fights, stands.Single(s => s.Person == armed).Role);
        Assert.Equal(SlaveRole.Stays, stands.Single(s => s.Person == bare).Role);
        Assert.Equal(SlaveRole.Flees, stands.Single(s => s.Person == shaky).Role);
        Assert.Equal(SlaveRole.Stays, stands.Single(s => s.Person == chained).Role);
        var kits = SlaveParty.Kits(stands);
        var kit = Assert.Single(kits); Assert.Equal(armed.Value, kit.Person); Assert.Equal("spear", kit.Weapon!.ShapeId); Assert.Equal(50, kit.Courage);
        // the bolt: the rule must hold; a steady slave or a chained one is refused
        Assert.False(r.Do(BondDesk.Flee(bare, false, sec), sec).Ok);
        Assert.False(r.Do(BondDesk.Flee(chained, true, sec), sec).Ok);
        Assert.True(r.Do(BondDesk.Flee(shaky, false, sec), sec).Ok);
        var rb = book.Of(shaky)!; Assert.True(rb.OnRun); Assert.NotEqual(0, rb.RunawayCrime); Assert.Equal(BondWork.Idle, rb.Work);
        Assert.DoesNotContain(r.State.Party.Squad.Members, m => m.Person == shaky);
        Assert.Contains(r.State.Chronicle.All, e => e.TypeId == "slave_runaway");
        // routed in the fight: a steady unchained slave who broke runs; the rest are shaken by a lost field and steadied by a won one
        book.Of(bare)!.Chain = Restraint.None;
        Assert.True(r.Do(BondDesk.Flee(bare, true, sec), sec).Ok); Assert.True(book.Of(bare)!.OnRun);
        int m0 = book.Of(armed)!.Morale;
        Assert.True(r.Do(BondDesk.Fought(false, sec), sec).Ok); Assert.Equal(m0 - 10, book.Of(armed)!.Morale);
        Assert.True(r.Do(BondDesk.Fought(true, sec), sec).Ok); Assert.Equal(m0 - 7, book.Of(armed)!.Morale);
    }

    // ------------------------------------------------------------------ SP-4

    [Fact]
    public void SP4_TheDailyRunawayRollTakesAnUnchainedUnhappySlaveOutOfTheParty_AChainedOneStays_AndTheSaveKeepsTheSeat()
    {
        using var r = new FaithRig();
        var free = Someone(r, 0); var held = Someone(r, 1);
        Own(r, free, 0).Chain = Restraint.None; Own(r, held, 0).Chain = Restraint.Chained;
        long sec = 100L * 86_400 + 3600;
        foreach (var s in new[] { free, held }) Assert.True(r.Do(BondDesk.Work(s, BondWork.Follow, sec), sec).Ok);
        var book = r.State.Bonds!; Assert.Equal(2, r.State.Party!.Squad.ActiveCount);
        int ran = -1;
        for (int d = 100; d < 400 && ran < 0; d++)
        {
            foreach (var b in book.Bonds.Values) { b.Morale = 0; b.Place = 0; }                    // kept unhappy; the owner's feeding would steady him
            BondLink.Daily(r.State, r.Content, d);
            if (book.Of(free)!.OnRun) ran = d;
        }
        Assert.True(ran >= 0, "the unhappy unchained slave never ran");
        Assert.DoesNotContain(r.State.Party.Squad.Members, m => m.Person == free);                // the daily pass took him out of the party
        Assert.Contains(r.State.Party.Squad.Members, m => m.Person == held && m.Origin == FollowerOrigin.Slave);
        Assert.False(book.Of(held)!.OnRun);
        // save and load: the seat, its origin, and the byte-for-byte resave
        var save = WorldSave.Capture(r.State, "b", "0.1");
        var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(save)), r.Content, "b", null, r.W.Continent, r.W.History);
        Assert.Contains(st2.Party!.Squad.Members, m => m.Person == held && m.Origin == FollowerOrigin.Slave);
        var save2 = WorldSave.Capture(st2, "b", "0.1");
        Assert.Equal(save.Sections[WorldSave.PartySection].ToArray(), save2.Sections[WorldSave.PartySection].ToArray());
    }
}
