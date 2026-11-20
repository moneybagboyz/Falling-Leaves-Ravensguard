using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Medicine;

/// <summary>
/// The treatment screen's sim side (medicine 4.1, 4.2; P6-B1), where the Phase 4 auto-dress tests live on: MT-2 the hero binds his own forearm, MT-5 a moved hero cancels the rest of his queue and
/// the cancelled items are not used, MT-6 nothing is dressed by itself after a fight and "Dress all bleeding" gives the steps the auto-dress gave, MT-13 a save mid-queue and mid-tourniquet
/// comes back the same, an old trace's Dress command is skipped by a replay, and the hero's book is the ledger's although his id is 0.
/// </summary>
public class TreatmentScreenTests
{
    private static FightContent Fc => _fc ??= new FightContent(new CombatTables(EconFixture.Content), EconFixture.Content);
    private static FightContent? _fc;

    // ------------------------------------------------------------------------------------------------ the hero's world

    private sealed class Rig
    {
        public TradeWorld W { get; } = new();
        public WoundLedger Ledger { get; }
        public BodyPlanV2 H { get; }
        public ulong Hero => W.Ctx.Party.Hero.Value;
        public Rig(int bandages = 3, int ropeG = 0)
        {
            W.State.Markets = null;                                                       // as HeroRuntimeTests: no production section to restore markets against
            Ledger = new WoundLedger(W.State.Seed, W.Sec); W.State.Wounds = Ledger;
            H = MedicineWiring.PlanOf(EconFixture.Content, "human")!;
            if (bandages > 0) W.Ctx.Pack.Create(W.G("bandage"), bandages, CreateReason.Seed);
            if (ropeG > 0) W.Ctx.Pack.Create(W.G("rope"), ropeG, CreateReason.Seed);
        }
        public int P(string id) => H.IndexOf(id);
        public WoundBook BookOf(ulong who) => Ledger.For(who, H, W.Sec);

        public WoundV2 Cut(string part, string structure, WoundFlags fl, int rate10)
        {
            int p = P(part); var pt = H.Parts[p];
            int si = Array.FindIndex(pt.Structures, s => s.Id == structure); Assert.True(si >= 0, structure);
            return new WoundV2 { Part = (short)p, Tissue = (short)pt.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rate10, Flags = fl };
        }
        public void Hurt(WoundBook b, params WoundV2[] w) => b.Apply(H, w, W.Sec, W.Sec, 0x77, 0, false);
    }

    // ------------------------------------------------------------------------------------------------ MT-2

    [Fact]
    public void MT2_TheHeroBandagesHisOwnForearmVeinCut_TwoHandedVerbsAreRefused_BandageTakesFourSimSeconds_RateDropsToThree()
    {
        var r = new Rig(bandages: 3);
        var b = r.BookOf(r.Hero);
        r.Hurt(b, r.Cut("lower_arm_l", "radial_vein", WoundFlags.VeinHit, 150));
        Assert.Equal(15_000, b.RateMilliAt(r.W.Sec / 20));
        int arm = r.P("lower_arm_l");
        // a stitch, a tourniquet and a splint need both hands: refused on oneself, nothing queued
        foreach (var k in new[] { TreatKind.Stitch, TreatKind.Tourniquet, TreatKind.Splint, TreatKind.SetBone })
        {
            var no = r.W.Do(HeroTreatDesk.Do(r.Hero, k, arm, r.W.Sec));
            Assert.Equal(HeroResultCode.Refused, no.Code);
        }
        Assert.True(r.W.State.Medicine!.Queue.IsEmpty);
        var ok = r.W.Do(HeroTreatDesk.Do(r.Hero, TreatKind.Bandage, arm, r.W.Sec));
        Assert.True(ok.Ok, ok.Text);
        long start = r.W.Sec, finish = start + 4 * 20;
        Assert.Equal(0, HeroTreatDesk.Advance(r.W.Ctx, finish - 1));                      // the work is not done yet: the blood still runs and the pack still holds the bandage
        Assert.Equal(3, r.W.Packed("bandage"));
        Assert.Equal(1, HeroTreatDesk.Advance(r.W.Ctx, finish));                          // at second 4 it is done
        Assert.Equal(2, r.W.Packed("bandage"));                                           // one bandage left the pack
        Assert.Equal(3_000, b.RateMilliAt(finish / 20));                                  // x0.20 of 15 ml/s
        Assert.Equal(Dressing.Bandage, b.Records[0].Dressing);
        Assert.True(r.W.State.Medicine.Queue.IsEmpty);
        Assert.True(r.W.Econ.MassAuditHolds());
    }

    // ------------------------------------------------------------------------------------------------ MT-5

    [Fact]
    public void MT5_ThreeVerbsQueued_TheHeroWalksTwoMetresAfterTheFirst_TwoAreCancelled_TheirItemsAreNotUsed()
    {
        var r = new Rig(bandages: 3);
        ulong stranger = 0x7700_0001;
        var b = r.BookOf(stranger);
        r.Hurt(b, r.Cut("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 50), r.Cut("upper_leg_r", "femoral_vein", WoundFlags.VeinHit, 50), r.Cut("lower_leg_l", "tibial_vein", WoundFlags.VeinHit, 50));
        long t0 = r.W.Sec;
        foreach (var part in new[] { "upper_arm_l", "upper_leg_r", "lower_leg_l" })
            Assert.True(r.W.Do(HeroTreatDesk.Do(stranger, TreatKind.Bandage, r.P(part), t0)).Ok);
        Assert.Equal(3, r.W.State.Medicine!.Queue.Count);
        Assert.Equal(t0 + 3 * 80, r.W.State.Medicine.Queue.EndSec);                       // each begins when the one before ends
        Assert.Equal(1, HeroTreatDesk.Advance(r.W.Ctx, t0 + 80));
        Assert.Equal(2, r.W.Packed("bandage"));
        r.W.Sec = t0 + 90;                                                                  // he has walked two metres: the game sends the cancel
        var c = r.W.Do(HeroTreatDesk.Cancel(r.W.Sec));
        Assert.True(c.Ok); Assert.Equal(2, c.Qty);
        Assert.Equal(0, HeroTreatDesk.Advance(r.W.Ctx, t0 + 1000));
        Assert.Equal(2, r.W.Packed("bandage"));                                           // the items of the cancelled steps were never taken
        Assert.Equal(Dressing.Bandage, b.Records.First(w => w.Part == r.P("upper_arm_l")).Dressing);
        Assert.All(b.Records.Where(w => w.Part != r.P("upper_arm_l")), w => Assert.Equal(Dressing.None, w.Dressing));
        Assert.True(r.W.State.Medicine.Queue.IsEmpty);
    }

    [Fact]
    public void ADressingWithNothingToDressWithEndsTheQueue_AndNothingIsInventedForAMissingBandage()
    {
        var r = new Rig(bandages: 1);
        ulong stranger = 0x7700_0002;
        var b = r.BookOf(stranger);
        r.Hurt(b, r.Cut("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 50), r.Cut("upper_leg_r", "femoral_vein", WoundFlags.VeinHit, 50));
        r.W.Do(HeroTreatDesk.Do(stranger, TreatKind.Bandage, r.P("upper_arm_l"), r.W.Sec));
        r.W.Do(HeroTreatDesk.Do(stranger, TreatKind.Bandage, r.P("upper_leg_r"), r.W.Sec));
        Assert.Equal(1, HeroTreatDesk.Advance(r.W.Ctx, r.W.Sec + 1000));                  // the second has no bandage: it is not done and the queue ends
        Assert.Equal(0, r.W.Packed("bandage"));
        Assert.True(r.W.State.Medicine!.Queue.IsEmpty);
        Assert.Contains(r.W.State.Medicine.Queue.Log, o => !o.Done);
    }

    // ------------------------------------------------------------------------------------------------ MT-6

    private static LiveWorld Field()
    {
        var g = new LevelGrid(40, 20, 0, 1);
        g.FillRect(0, 0, 39, 19, 0, true);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
    }

    /// <summary>The hero (fighter 0) with a cut to the radial artery of the left lower arm, and a beaten foe who yields: the fight is over and the hero won.</summary>
    private static FightWorld WonFightWithArterialCut(LiveWorld live, WoundLedger? ledger, ulong heroPerson)
    {
        var w = new FightWorld(Fc, 41, 9001, live.Clock.Now.Seconds, ledger);
        FighterKit Kit(string n, int team, int x, ulong person) => new()
        {
            Name = n, Team = team, X = x, Y = 0, Person = person, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 50, Worn = [new WornItem("tunic", "linen")],
            FaceX = team == 0 ? FightMath.One : -FightMath.One,
        };
        var hero = w.Add(Kit("Hero", 0, 0, heroPerson), player: true);
        w.Add(Kit("Robber", 1, 6000, 0x5000_0001));
        var plan = hero.Plan;
        int part = Enumerable.Range(0, plan.Parts.Length).First(i => plan.Parts[i].BaseId == "lower_arm" && plan.Parts[i].Structures.Any(s => s.Id == "radial_artery"));
        short st = (short)Array.FindIndex(plan.Parts[part].Structures, s => s.Id == "radial_artery");
        var cut = new WoundV2 { Part = (short)part, Tissue = 0, Structure = st, Organ = -1, Kind = WoundKind.Cut, DepthPm = 900, BleedX10 = 250, Flags = WoundFlags.ArteryHit };
        hero.Book.Apply(plan, [cut], w.GameSec, w.Tick, 0x77, 0, false);
        Assert.True(Treatment.WorstBleeder(hero.Book, -1, w.GameSec) >= 0, "the cut does not bleed");
        w.Input(1, new FightInput(FightInputKind.Yield));
        return w;
    }

    /// <summary>A road fight the hero won, over, with his book in the ledger of a hero's world whose clock stands where the fight ended; the pack holds <paramref name="bandages"/>.</summary>
    private static (Rig R, FightWorld Fight, LiveWorld Live, SessionRecorder Rec) AfterFight(int bandages)
    {
        var live = Field();
        var rec = new SessionRecorder(live);
        var r = new Rig(bandages);
        Assert.Equal(0UL, r.Hero);                                                         // the hero's id is the kit's "nobody" too
        live.AttachWounds(r.Ledger);
        var fight = WonFightWithArterialCut(live, r.Ledger, r.Hero);
        Assert.True(fight.Fighters[0].LedgerBook, "the hero's book is not the ledger's");
        rec.StartFight(fight, 0);
        for (int t = 0; t < 5; t++) rec.Step();
        Assert.Same(r.Ledger.Find(r.Hero), fight.Fighters[0].Book);
        Assert.True(fight.IsOver); Assert.Equal(0, fight.DecidedWinner);
        rec.EndFight();
        r.W.Sec = fight.GameSec;                                                           // the world's second is the fight's
        return (r, fight, live, rec);
    }

    [Fact]
    public void MT6_AfterAFightNothingIsDressedByItself_ThePlannerStillSaysWhatWouldBeDone()
    {
        var (r, fight, _, _) = AfterFight(bandages: 3);
        var book = r.Ledger.Find(r.Hero)!;
        r.Ledger.AdvanceTo(fight.GameSec + 60);
        Assert.True(Treatment.WorstBleeder(book, -1, book.LastSec) >= 0, "something dressed the wound by itself");
        Assert.All(book.Records, w => Assert.Equal(Dressing.None, w.Dressing));
        Assert.Equal(3, r.W.Packed("bandage"));
        Assert.True(r.W.State.Medicine?.Queue.IsEmpty ?? true);
    }

    [Fact]
    public void MT6_DressAllBleeding_GivesTheStepsTheAutoDressGave_ATourniquetOfTwoBandagesThenNothingMore()
    {
        var (r, fight, _, _) = AfterFight(bandages: 3);
        // what the planner alone says on a copy of the case: a tourniquet with two bandages first (the treatment's own rule), the hero alive and bound ten minutes later
        var expect = AfterFight(3).Fight.Fighters[0];
        var o = FieldDressing.Dress(expect.Book, expect.Plan, 3, false, 20, 0, fight.GameSec);
        Assert.Equal(TreatKind.Tourniquet, o.Steps[0].Kind); Assert.Equal(2, o.BandagesUsed);
        // the same through the desk: "Dress all bleeding" queued as one command, each dressing done at the second its work ends
        var start = fight.GameSec;
        var res = r.W.Do(HeroTreatDesk.DressAll(r.Hero, FieldDressing.MaxSteps, start));
        Assert.True(res.Ok, res.Text);
        Assert.Equal(0, HeroTreatDesk.Advance(r.W.Ctx, start + 119));
        Assert.Equal(1, HeroTreatDesk.Advance(r.W.Ctx, start + 120));                      // a tourniquet takes six sim-seconds
        var q = r.W.State.Medicine!.Queue;
        Assert.Equal(TreatKind.Tourniquet, q.Log[0].Kind);
        Assert.Equal(o.Steps[0].DoneSec, q.Log[0].Sec);
        Assert.Equal(1, r.W.Packed("bandage"));                                            // two bandages made the tourniquet
        HeroTreatDesk.Advance(r.W.Ctx, start + 5000);
        Assert.Equal(o.Steps.Count, q.Log.Count(x => x.Done));
        Assert.True(q.IsEmpty);
        var book = r.Ledger.Find(r.Hero)!;
        r.Ledger.AdvanceTo(start + 600 * 20);                                              // ten sim-minutes on
        Assert.False(book.Dead, "the hero bled out despite the tourniquet");
        Assert.True(book.CurrentRateMilli() < 2_000, $"still {book.CurrentRateMilli()} ml/s x 1000 after the tourniquet");
        Assert.True(Treatment.WorstBleeder(book, -1, book.LastSec) < 0);
    }

    [Fact]
    public void MT6_WithNoBandagesNothingIsDone_AndTheHeroBleedsOn()
    {
        var (r, fight, _, _) = AfterFight(bandages: 0);
        Assert.True(r.W.Do(HeroTreatDesk.DressAll(r.Hero, 4, fight.GameSec)).Ok);
        Assert.Equal(0, HeroTreatDesk.Advance(r.W.Ctx, fight.GameSec + 5000));
        Assert.True(r.W.State.Medicine!.Queue.IsEmpty);                                    // nothing to dress with: the chain ends
        var book = r.Ledger.Find(r.Hero)!;
        Assert.True(Treatment.WorstBleeder(book, -1, book.LastSec) >= 0);
    }

    // ------------------------------------------------------------------------------------------------ the replay of an old trace

    [Fact]
    public void AnOldTraceWithADressCommandIsReadAndSkipped_TheReplayStillMatches()
    {
        var live = Field();
        var rec = new SessionRecorder(live);
        var fight = WonFightWithArterialCut(live, null, 0);
        rec.StartFight(fight, 0);
        for (int t = 0; t < 5; t++) rec.Step();
        rec.Commands.Append(new ReplayCommand(live.Tick, ReplayKind.Dress, 3, 20, 0));      // what a Phase 4 session wrote after a won fight
        for (int t = 0; t < 30; t++) rec.Step();
        var (trace, csv) = rec.Export();
        Assert.Contains(rec.Commands.Items, c => c.Kind == ReplayKind.Dress);
        var again = Field();
        again.FightFactory = id => FightRecord.FromBytes(rec.FightStarts[id]).Restore(Fc);
        var res = ReplayRunner.Run(again, CommandLog.FromBytes(trace), StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.Equal(live.Fight!.Hash(), again.Fight!.Hash());
    }

    [Fact]
    public void The_heros_book_is_the_ledgers_although_his_id_is_0_and_comes_back_so_from_a_save_made_while_watched()
    {
        var live = Field();
        var ledger = new WoundLedger(41, live.Clock.Now.Seconds);
        var fight = WonFightWithArterialCut(live, ledger, EconomyState.HeroOwner.Value);
        Assert.Equal(0UL, EconomyState.HeroOwner.Value);
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

    // ------------------------------------------------------------------------------------------------ MT-13

    private static SaveFile SaveOf(Rig r) => WorldSave.Capture(r.W.State, EconFixture.ContentHash, "0.1.0");

    [Fact]
    public void MT13_ASaveMidQueueAndMidTourniquetComesBackTheSame_AndSavesToTheSameBytes()
    {
        var r = new Rig(bandages: 4, ropeG: 400);
        var b = r.BookOf(r.Hero);
        r.Hurt(b, r.Cut("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 50), r.Cut("upper_leg_r", "femoral_vein", WoundFlags.VeinHit, 50));
        int arm = TreatAi.LimbRoot(r.H, r.P("upper_arm_l"));
        Assert.True(Treatment.Perform(b, r.H, new(TreatKind.Tourniquet, arm, r.W.Sec, 0, r.Hero), new ItemBag().Add("rope", 1)).Ok);
        r.Ledger.AdvanceTo(r.W.Sec + 400 * 20);                                            // 400 sim-seconds with the tourniquet on: past the first ischaemia step
        long tq = b.Records.Max(w => w.TourniquetOnS);
        Assert.InRange(tq, 360, 440);
        r.W.Sec += 400 * 20;
        // a queue of two: a bandage on the leg (planned, its time not yet come) and a dress-all behind it
        Assert.True(r.W.Do(HeroTreatDesk.Do(r.Hero, TreatKind.Bandage, r.P("upper_leg_r"), r.W.Sec)).Ok);
        Assert.True(r.W.Do(HeroTreatDesk.DressAll(r.Hero, 3, r.W.Sec)).Ok);
        Assert.Equal(0, HeroTreatDesk.Advance(r.W.Ctx, r.W.Sec + 10));
        var q = r.W.State.Medicine!.Queue;
        Assert.Equal(2, q.Count);
        var bytes = SaveCodec.Encode(SaveOf(r));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Medicine);
        Assert.Equal(q.Hash(), back.Medicine!.Queue.Hash());
        Assert.Equal(2, back.Medicine.Queue.Count);
        Assert.Equal(r.Ledger.Hash(), back.Wounds!.Hash());                                // WoundHash holds TourniquetOnS
        Assert.Equal(tq, back.Wounds.Find(r.Hero)!.Records.Max(w => w.TourniquetOnS));
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }

    [Fact]
    public void AWorldThatNeverTreatedAnyoneHasNoMedicineSection_SoItsBytesAreUnchanged()
    {
        var r = new Rig();
        var file = SaveOf(r);
        Assert.DoesNotContain(MedicineSave.SectionName, file.Sections.Keys);
    }

    [Fact]
    public void ThePhysiciansStepIsRegisteredInThePhase6SeedingPass()
    {
        Assert.Contains(Ravensguard.Sim.Worlds.Phase6Seeding.DefaultSteps, s => s.Section == MedicineSave.SectionName);
    }

    // ------------------------------------------------------------------------------------------------ the view

    [Fact]
    public void TheViewListsEachPartsWoundsAndVerbs_WithTimesChancesAndWhatThePackHolds()
    {
        var r = new Rig(bandages: 2);
        var b = r.BookOf(r.Hero);
        r.Hurt(b, r.Cut("lower_arm_l", "radial_vein", WoundFlags.VeinHit, 150));
        var v = TreatViews.ForHero(r.W.Ctx, r.Hero, r.W.Sec)!;
        Assert.True(v.Self); Assert.Equal(1, v.BleedingWounds);
        Assert.Equal(r.P("lower_arm_l"), v.WorstBleederPart);
        var part = v.Parts.Single(p => p.Part == r.P("lower_arm_l"));
        var bandage = part.Verbs.Single(x => x.Kind == TreatKind.Bandage);
        Assert.Equal(4, bandage.SimS); Assert.True(bandage.HasItems); Assert.True(bandage.Allowed);
        var tourniquet = part.Verbs.Single(x => x.Kind == TreatKind.Tourniquet);
        Assert.False(tourniquet.Allowed); Assert.Equal("tr_two_hands", tourniquet.Refusal);          // on oneself
        Assert.True(tourniquet.HasItems);                                                            // no rope, but two bandages do
        Assert.Equal(15_00, part.BleedCentiMlPerS);
    }
}
