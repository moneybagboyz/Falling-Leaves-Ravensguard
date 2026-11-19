using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// The auto-dress stop-gap after a fight (approved after the Phase 4 review; the Phase 5 treatment screen replaces it): a won road fight with an arterial cut and bandages in the pack ends
/// with the bleed bound and the hero alive ten game minutes later; with no bandages he bleeds on; a replay binds the same wounds; the pack gives up what was used, and the save round-trips.
/// </summary>
public class AutoDressTests
{
    private static FightContent Fc => _fc ??= new FightContent(new CombatTables(EconFixture.Content), EconFixture.Content);
    private static FightContent? _fc;

    private static LiveWorld Field()
    {
        var g = new LevelGrid(40, 20, 0, 1);
        g.FillRect(0, 0, 39, 19, 0, true);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
    }

    /// <summary>The hero (fighter 0) with a cut to the radial artery of the left lower arm, and a beaten foe who yields: the fight is over and the hero won.</summary>
    private static FightWorld WonFightWithArterialCut(LiveWorld live, WoundLedger? ledger)
    {
        var w = new FightWorld(Fc, 41, 9001, live.Clock.Now.Seconds, ledger);
        FighterKit Kit(string n, int team, int x, ulong person) => new()
        {
            Name = n, Team = team, X = x, Y = 0, Person = person, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 50, Worn = [new WornItem("tunic", "linen")],
            FaceX = team == 0 ? FightMath.One : -FightMath.One,
        };
        var hero = w.Add(Kit("Hero", 0, 0, EconomyState.HeroOwner.Value), player: true);
        w.Add(Kit("Robber", 1, 6000, 0x5000_0001));
        var plan = hero.Plan;
        int part = Enumerable.Range(0, plan.Parts.Length).First(i => plan.Parts[i].BaseId == "lower_arm" && plan.Parts[i].Structures.Any(s => s.Id == "radial_artery"));
        short st = (short)Array.FindIndex(plan.Parts[part].Structures, s => s.Id == "radial_artery");
        var cut = new WoundV2 { Part = (short)part, Tissue = 0, Structure = st, Organ = -1, Kind = WoundKind.Cut, DepthPm = 900, BleedX10 = 250, Flags = WoundFlags.ArteryHit };
        hero.Book.Apply(plan, [cut], w.GameSec, w.Tick, 0x77, 0, false);
        Assert.True(Treatment.WorstBleeder(hero.Book, -1, w.GameSec) >= 0, "the cut does not bleed");
        Assert.True(w.Fighters[1].Act != FAct.Dead);
        w.Input(1, new FightInput(FightInputKind.Yield));
        return w;
    }

    private static (DressOutcome Outcome, WoundBook Book, long Sec) Run(int bandages, bool rope)
    {
        var live = Field();
        var rec = new SessionRecorder(live);
        var ledger = new WoundLedger(41, live.Clock.Now.Seconds);
        live.AttachWounds(ledger);
        var fight = WonFightWithArterialCut(live, ledger);
        Assert.True(fight.Fighters[0].LedgerBook, "the hero's book is not the ledger's");
        rec.StartFight(fight, 0);
        for (int t = 0; t < 5; t++) rec.Step();                                           // the game binds the wounds a step or two after the fight is decided
        Assert.Same(ledger.Find(EconomyState.HeroOwner.Value), fight.Fighters[0].Book);
        Assert.True(fight.IsOver);
        Assert.Equal(0, fight.DecidedWinner);
        var o = rec.Dress(bandages, rope, 20, 0);
        var book = fight.Fighters[0].Book;
        long sec = fight.GameSec;
        rec.EndFight();
        ledger.AdvanceTo(sec + 600);                                                        // ten game minutes later
        return (o, book, sec);
    }

    [Fact]
    public void A_won_road_fight_with_an_arterial_cut_and_bandages_ends_bound_and_the_hero_is_alive_ten_minutes_on()
    {
        var (o, book, sec) = Run(bandages: 3, rope: false);
        Assert.NotEmpty(o.Steps);
        Assert.Equal(TreatKind.Tourniquet, o.Steps[0].Kind);                             // arterial and on a limb: two bandages make the tourniquet (the treatment's own rule)
        Assert.Equal(2, o.BandagesUsed);
        Assert.True(o.Steps[0].DoneSec > sec && o.EndSec == o.Steps[^1].DoneSec);          // it took its time
        Assert.False(book.Dead, "the hero bled out despite the tourniquet");
        Assert.True(book.CurrentRateMilli() < 2_000, $"still {book.CurrentRateMilli()} ml/s x 1000 after the tourniquet");   // 25 ml/s before
        Assert.True(Treatment.WorstBleeder(book, -1, book.LastSec) < 0);
        // one bandage only: a bandage, not a tourniquet
        var one = Run(1, false);
        Assert.Equal(TreatKind.Bandage, one.Outcome.Steps.Single().Kind);
        Assert.Equal(1, one.Outcome.BandagesUsed);
    }

    [Fact]
    public void The_heros_book_is_the_ledgers_although_his_id_is_0_and_comes_back_so_from_a_save_made_while_watched()
    {
        var live = Field();
        var ledger = new WoundLedger(41, live.Clock.Now.Seconds);
        var fight = WonFightWithArterialCut(live, ledger);
        Assert.Equal(0UL, EconomyState.HeroOwner.Value);                                    // the hero's id is the kit's "nobody" too: before the fix his book was never the ledger's
        Assert.True(fight.Fighters[0].LedgerBook);
        Assert.False(fight.Fighters[1].LedgerBook && fight.Fighters[1].Kit.Person == 0);
        fight.KeepBooksInLedger();
        Assert.Same(fight.Fighters[0].Book, ledger.Find(0));
        fight.Fighters[0].Player = false;                                                   // watch mode hands him to the AI
        var rec = FightRecord.Capture(fight, "", "x");
        Assert.Null(rec.Fighters[0].Book);                                                  // his book is in the "wounds" section, not the record
        var back = rec.Restore(Fc, WoundSave.Capture(ledger).Restore());
        Assert.True(back.Fighters[0].LedgerBook);
        Assert.Equal(fight.Hash(), back.Hash());
    }

    [Fact]
    public void With_no_bandages_nothing_is_done_and_the_hero_bleeds_on()
    {
        var (o, book, sec) = Run(bandages: 0, rope: false);
        Assert.Empty(o.Steps);
        var (_, bound, _) = Run(3, false);
        Assert.True(book.Dead || book.BloodUl < bound.BloodUl, $"an unbound arterial cut lost no more blood than a bound one: {book.BloodUl} (last {book.LastSec}, rate {book.CurrentRateMilli()}, state {book.State}) against {bound.BloodUl} (last {bound.LastSec}); started {sec}");
        Assert.True(book.Dead || book.CurrentRateMilli() > 0);
    }

    [Fact]
    public void A_replay_binds_the_same_wounds_and_gives_the_same_hash()
    {
        var live = Field();
        var rec = new SessionRecorder(live);
        var fight = WonFightWithArterialCut(live, null);                                   // the fight's own books: the start record carries the cut
        rec.StartFight(fight, 0);
        for (int t = 0; t < 5; t++) rec.Step();
        var o = rec.Dress(3, false, 20, 0);
        Assert.NotEmpty(o.Steps);
        for (int t = 0; t < 30; t++) rec.Step();
        var (trace, csv) = rec.Export();
        Assert.Contains(rec.Commands.Items, c => c.Kind == ReplayKind.Dress);
        var again = Field();
        again.FightFactory = id => FightRecord.FromBytes(rec.FightStarts[id]).Restore(Fc);
        var res = ReplayRunner.Run(again, CommandLog.FromBytes(trace), StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.Equal(live.Fight!.Hash(), again.Fight!.Hash());
        Assert.Equal(o.Steps, again.LastDressing.Steps);
        Assert.Equal((o.BandagesUsed, o.RopeUsed, o.EndSec), (again.LastDressing.BandagesUsed, again.LastDressing.RopeUsed, again.LastDressing.EndSec));
        Assert.Equal(live.Fight.Fighters[0].Book.Hash(0), again.Fight.Fighters[0].Book.Hash(0));
    }

    [Fact]
    public void The_pack_gives_up_what_was_used_and_the_world_saves_loads_and_saves_to_the_same_bytes()
    {
        var w = new TradeWorld();
        w.State.Markets = null;                                                             // as HeroRuntimeTests: no production section to restore markets against
        var bandage = w.G("bandage");
        w.Ctx.Pack.Create(bandage, 3, Ravensguard.Sim.Goods.CreateReason.Seed);
        long mass0 = w.Econ.Goods.MassG(bandage, w.Ctx.Pack.Qty(bandage));
        var ledger = new WoundLedger(w.State.Seed, w.Sec);
        w.State.Wounds = ledger;
        var live = Field();
        live.AttachWounds(ledger);
        var rec = new SessionRecorder(live);
        var fight = WonFightWithArterialCut(live, ledger);
        rec.StartFight(fight, 0);
        for (int t = 0; t < 5; t++) rec.Step();
        var o = rec.Dress((int)w.Ctx.Pack.Qty(bandage), false, 20, 0);
        rec.EndFight();
        var r = w.Do(AftermathDesk.DressCommand(o.BandagesUsed, o.RopeUsed * FieldDressing.TourniquetRopeG, w.Sec));
        Assert.True(r.Ok, r.Text);
        Assert.Equal(3 - o.BandagesUsed, w.Ctx.Pack.Qty(bandage));
        Assert.Equal(o.BandagesUsed, (int)r.Qty);
        Assert.True(mass0 > w.Econ.Goods.MassG(bandage, w.Ctx.Pack.Qty(bandage)));
        Assert.True(w.Econ.MassAuditHolds());
        Assert.NotNull(ledger.Find(EconomyState.HeroOwner.Value));                          // the bound wound is the hero's, in the ledger
        var bytes = SaveCodec.Encode(WorldSave.Capture(w.State, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        Assert.Equal(ledger.Find(EconomyState.HeroOwner.Value)!.Hash(0), back.Wounds!.Find(EconomyState.HeroOwner.Value)!.Hash(0));
        // a second dress command with nothing left takes nothing more
        Assert.Equal(3 - o.BandagesUsed, w.Do(AftermathDesk.DressCommand(5, 0, w.Sec)).Qty);
        Assert.Equal(0, w.Ctx.Pack.Qty(bandage));
    }
}
