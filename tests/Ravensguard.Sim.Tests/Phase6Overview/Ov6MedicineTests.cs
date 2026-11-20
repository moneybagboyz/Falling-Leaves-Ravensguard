using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-6 (overview 4, "the Phase 4 medicine items, first"): on a whole running game of seed 1234, after the ambush at a ford (an arterial cut to the left forearm and a torn tendon of the right thigh, the wounds
/// the fight leaves) nothing is dressed by itself; the hero ties a tourniquet and a bandage on the road through the treatment desk ("Dress all bleeding"), reaches a physician of the region within the day, is
/// quoted and pays for the tendon (coin through the ledger to the physician's business), keeps the leg and walks on a crutch (the make_crutch recipe) while it heals. Coin conserved, the save resumes byte for byte.
/// </summary>
public class Ov6MedicineTests(ITestOutputHelper output)
{
    private static WoundV2 Cut(BodyPlanV2 plan, string part, string structure, WoundFlags fl, int rate10)
    {
        int p = plan.IndexOf(part); var pt = plan.Parts[p];
        int si = Array.FindIndex(pt.Structures, s => s.Id == structure); Assert.True(si >= 0, structure);
        return new WoundV2 { Part = (short)p, Tissue = (short)pt.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rate10, Flags = fl };
    }

    [Fact]
    public void OV6_AfterTheFordAmbushTheHeroDressesOnTheRoad_ReachesThePhysicianWithinTheDayForTheTendon_PaysAndKeepsTheLeg_WalksOnACrutch_NothingIsDressedByItself()
    {
        var (w, s) = P6Game.Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new P6Play(rt, ctx);
        var e = s.Economy!; var pt = s.Population.People;
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24 + 24;
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 7));                                              // seven in the morning on the road
        var plan = MedicineWiring.PlanOf(w.Content, "human")!;
        ulong hero = ctx.Party.Hero.Value;

        // the road kit: what a traveller carries (the test's faucet: two bandages beside the rope's grams, a needle and thread is the physician's)
        ushort bandage = e.Goods.IdOf("bandage"), rope = e.Goods.IdOf("rope");
        ctx.Pack.Create(bandage, 4, CreateReason.Import); ctx.Pack.Create(rope, 400, CreateReason.Import);
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(10), SourceReason.Seed);                          // the purse that pays the physician
        long seeded = e.Ledger.Audit().Seeded.Mites;

        // 1. the ambush at the ford: an arterial cut to the left forearm, a torn tendon of the right thigh, in the hero's own wound book
        long ambush = rt.HourDone * 3600;
        var ledger = s.Wounds ?? throw new InvalidOperationException("the new game has no wound ledger");
        var book = ledger.For(hero, plan, ambush);
        book.Apply(plan, [Cut(plan, "lower_arm_l", "radial_artery", WoundFlags.ArteryHit, 250), Cut(plan, "lower_leg_r", "achilles_tendon", WoundFlags.TendonHit, 0)], ambush, ambush, 0x77, 0, false);
        Assert.True(Treatment.WorstBleeder(book, -1, ambush) >= 0, "the cut does not bleed");
        int tendon = book.Records.FindIndex(r => r.Cat == WoundCat.Tendon);
        Assert.True(tendon >= 0);
        var torn = book.Function(plan);
        output.WriteLine($"ambushed: walk {torn.WalkSpeedPm} per mille, lameness {torn.Lameness}, {book.Records.Count} wounds, bleeding {book.CurrentRateMilli()} mml/s");
        Assert.Equal(3, torn.Lameness);                                                            // the cut tendon: a lame leg

        // nothing dresses itself: a minute on, every wound is still undressed and the pack unchanged
        ledger.AdvanceTo(ambush + 60 * 20);
        Assert.All(book.Records, r => Assert.Equal(Dressing.None, r.Dressing));
        Assert.Equal(4, ctx.Pack.Qty(bandage));

        // 2. on the road: "Dress all bleeding" through the desk (a tourniquet first, for an artery of a limb), played out at the seconds each takes
        var dress = p.Do(HeroTreatDesk.DressAll(hero, 4, ambush));
        Assert.True(dress.Ok, dress.Text.ToString());
        int did = HeroTreatDesk.Advance(ctx, ambush + 5000);
        Assert.True(did >= 1);
        var tq = book.Records.Single(r => r.Cat is WoundCat.Vessel or WoundCat.Skin or WoundCat.Inset && r.Has(WoundFlags.ArteryHit));
        Assert.Equal(Dressing.Tourniquet, tq.Dressing);
        Assert.Equal(Dressing.None, book.Records[tendon].Dressing);                                 // nobody ties a tendon: it wants a surgeon
        Assert.True(book.CurrentRateMilli() < 2_000, $"still {book.CurrentRateMilli()} after the tourniquet");
        Assert.False(book.Dead);

        // 3. a physician who can mend a tendon (Surgery 50 and up), the nearest town's, reached within the day
        var meds = HeroTreatDesk.BookOf(ctx);
        int home = pt.HomeSettlement[s.Hero!.PersonIndex]; var region = s.Population.Region;
        var man = meds.Rows.Where(r => r.Kind == PhysicianKind.Physician && pt.IsAlive(pt.IndexOf(new EntityId(r.Person))) && pt.SkillLevelOf(pt.IndexOf(new EntityId(r.Person)), Skill.Surgery) >= 50)
            .OrderBy(r => (long)(region.Settlements[r.Settlement].CenterX - region.Settlements[home].CenterX) * (region.Settlements[r.Settlement].CenterX - region.Settlements[home].CenterX)
                        + (long)(region.Settlements[r.Settlement].CenterY - region.Settlements[home].CenterY) * (region.Settlements[r.Settlement].CenterY - region.Settlements[home].CenterY)).ThenBy(r => r.Person).First();
        long dx = region.Settlements[man.Settlement].CenterX - region.Settlements[home].CenterX, dy = region.Settlements[man.Settlement].CenterY - region.Settlements[home].CenterY;
        double metres = Math.Sqrt(dx * dx + dy * dy);
        output.WriteLine($"physician {pt.FullName(pt.IndexOf(new EntityId(man.Person)))} of {region.Settlements[man.Settlement].Name}, {metres:F0} m from {region.Settlements[home].Name}");
        // a tourniquet cannot stay on for the whole road (a game hour is three sim-minutes of a wound's life, the limb dies at 36 sim-minutes): an hour on, when the blood has clotted, the hero lets it go and binds the cut
        p.Hours(1);
        int armRoot = TreatAi.LimbRoot(plan, plan.IndexOf("lower_arm_l"));
        Assert.True(p.Do(HeroTreatDesk.Do(hero, TreatKind.ReleasePressure, armRoot, p.Sec)).Ok);
        Assert.True(p.Do(HeroTreatDesk.Do(hero, TreatKind.Bandage, plan.IndexOf("lower_arm_l"), p.Sec)).Ok);
        HeroTreatDesk.Advance(ctx, p.Sec + 2000);
        output.WriteLine("after release+bandage: " + string.Join("; ", book.Records.Select(r => $"{r.Cat} {r.Dressing} {r.State}")) + " queue " + HeroTreatDesk.BookOf(ctx).Queue.Count);
        Assert.Equal(Dressing.Bandage, book.Records.First(r => r.Cat == WoundCat.Vessel).Dressing);          // the cut is bound, the tourniquet is off
        Assert.True(Treatment.WorstBleeder(book, -1, book.LastSec) < 0, "the cut still bleeds");
        // the ford is four kilometres from the town: a man with a lame leg walks it at about a third of a walk, and is there inside the day
        double fordM = 4000;
        long walkHours = (long)Math.Ceiling((fordM + metres) / (1.4 * 0.33) / 3600.0);
        Assert.True(walkHours < 12, $"{walkHours} h to the physician");
        rt.AdvanceTo(GameTime.FromGameHours(Math.Max(rt.HourDone + 1, h0 + 7 + walkHours + 1)));
        Assert.False(book.Dead, "the hero did not last the road");
        Assert.True(rt.HourDone * 3600 - ambush < 86_400, "the physician is reached within the game day the tendon allows");

        // 4. his quote (a physician mends a tendon, a barber does not) and the payment, through the ledger to the physician's business
        long sec = p.Sec;
        long balance0 = e.Ledger.Balance(ctx.Purse).Mites;
        var row = meds.TryGet(man.Person, out var r0) ? r0 : throw new InvalidOperationException();
        int skill = pt.SkillLevelOf(pt.IndexOf(new EntityId(man.Person)), Skill.Surgery);
        var quote = TreatDesk.Quote(book, plan, row.Kind, skill, sec);
        var line = quote.Single(l => l.Kind == TreatKind.TendonRepair);
        Assert.Equal(1200, line.FeeMites);
        output.WriteLine($"quote: {string.Join(", ", quote.Select(l => $"{l.Kind} {l.FeeMites} mites {l.ChancePct}%"))}");
        var stitch = quote.Single(l => l.Kind == TreatKind.Stitch);                                // and the cut he had bound: stitched, it is far less likely to fester
        var pay = p.Do(HeroTreatDesk.Physician(man.Person, hero, [line, stitch], sec));
        Assert.True(pay.Ok, pay.Text.ToString());
        long paid = pay.Mites;
        for (int again = 0; again < 2 && !book.Records[tendon].Is(Aid.Repaired); again++)           // the surgeon's hand slips on the draw: one retry at -20 points, he pays again
        {
            var retry = TreatDesk.Quote(book, plan, row.Kind, skill, p.Sec).Single(l => l.Kind == TreatKind.TendonRepair);
            output.WriteLine($"the repair failed; again at {retry.ChancePct}%");
            var again2 = p.Do(HeroTreatDesk.Physician(man.Person, hero, [retry], p.Sec));
            Assert.True(again2.Ok, again2.Text.ToString()); paid += again2.Mites;
        }
        Assert.Equal(balance0 - paid, e.Ledger.Balance(ctx.Purse).Mites);
        Assert.True(paid >= 1500, $"paid {paid}");
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(seeded, e.Ledger.Audit().Seeded.Mites);

        // 5. the tendon is repaired within the game day it needs (else it never heals and the leg stays lame; the chance is the quote's, the draw 0x422's, the same on every run)
        output.WriteLine($"tendon: repaired {book.Records[tendon].Is(Aid.Repaired)}, chance {line.ChancePct}%, paid {Quotes.Money(paid)}");
        Assert.True(book.Records[tendon].Is(Aid.Repaired), $"the repair of the tendon failed on the draws (chance {line.ChancePct}%)");
        Assert.Equal(3, book.Function(plan).Lameness);                                              // repaired, not yet healed

        // 6. a crutch while it heals: the recipe any station or field kit can make, one hour, 2 kg of wood and a knife (the test's faucet: the pole)
        var rc = ctx.Recipes["make_crutch"];
        ushort crutchGood = e.Goods.IdOf("crutch");
        for (int attempt = 0; attempt < 6 && ctx.Pack.Qty(crutchGood) == 0; attempt++)              // a pole can split under the knife: he cuts another
        {
            foreach (var slot in rc.Slots) ctx.Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Import);
            var cstart = p.Do(new HeroCommand { Kind = HeroCommandKind.CraftStart, Recipe = rc.Index, Site = new CraftSiteSpec(255, true, ["knife"], [], []) });
            Assert.True(cstart.Ok, cstart.Text.ToString());
            var cjob = ctx.Craft.State.Jobs.Values.Single(j => j.Owner == ctx.Party.Hero && j.State != CraftJobState.Done);
            Assert.True(p.Do(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = cjob.Id, UntilSec = p.Sec + 2 * 3600 }).Ok);
            p.Hours(2);
        }
        Assert.Equal(1, ctx.Pack.Qty(crutchGood));
        Assert.Equal(550, Crutches.WalkPm(1, 1)); Assert.Equal(250, Crutches.WalkPm(1, 0));        // a crutch lifts a hop from 250 to 550 and takes a hand
        Assert.Equal(1, Crutches.HandsTaken(1)); Assert.False(Crutches.CanHold(1, 2));

        // 7. the leg heals within the weeks and he keeps it: the tendon wound resolves and the limp is gone
        long dayAtPay = p.Day;
        int cleanings = 0;
        for (int d = 0; d < 90 && !book.Records[tendon].Is(Aid.Resolved); d++)
        {
            p.Days(1);
            Assert.False(book.Dead, "the hero died of his wounds while they healed");
            if (book.Records.Any(r => r.InfectionLevel > 0 && r.State != WoundState.Scar))            // it festers: back to the physician to have it cleaned (he will not close over a dirty wound)
            {
                var clean = TreatDesk.Quote(book, plan, row.Kind, skill, p.Sec).Where(l => l.Kind == TreatKind.Clean).ToList();
                Assert.NotEmpty(clean);
                Assert.True(p.Do(HeroTreatDesk.Physician(man.Person, hero, clean, p.Sec)).Ok); cleanings++;
            }
        }
        output.WriteLine($"healed {p.Day - dayAtPay} days after the repair ({cleanings} cleanings): resolved {book.Records[tendon].Is(Aid.Resolved)}, lameness {book.Function(plan).Lameness}");
        output.WriteLine("records: " + string.Join("; ", book.Records.Select(r => $"{r.Cat} {r.State} heal {r.HealX10}/{r.NeedX10} infection {r.InfectionLevel} aids {r.Aids}")) + $" last {book.LastSec / 86400} day {book.DayDone}");
        Assert.True(book.Records[tendon].Is(Aid.Resolved), "the repaired tendon did not heal");
        Assert.Equal(0, book.Function(plan).Lameness);
        Assert.False(book.Dead);

        // 8. the world goes on and the save resumes byte for byte
        var bytes = SaveCodec.Encode(P6Game.SaveAt(rt));
        var st2 = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, P6Game.Hash, null, w.Continent, w.History);
        st2.Content = w.Content;
        using var back = new WorldRuntime(st2, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(back, w.Content);
        for (int hh = 0; hh < 48; hh++)
        {
            rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1)); back.AdvanceTo(GameTime.FromGameHours(back.HourDone + 1));
            var a = P6Game.Sections(rt); var b = P6Game.Sections(back);
            var bad = a.Keys.Where(k => !a[k].AsSpan().SequenceEqual(b[k])).ToList();
            if (bad.Count > 0)
            {
                output.WriteLine($"hour +{hh + 1} (hour of day {rt.HourDone % 24}) differs in {string.Join(",", bad)}");
                var sa = rt.State.Standing!; var sb = back.State.Standing!;
                output.WriteLine($"deeds {sa.Deeds.Count}/{sb.Deeds.Count} rows {sa.Rows.Count}/{sb.Rows.Count} renowns {sa.Renowns.Count}/{sb.Renowns.Count} lastDay {sa.LastDay}/{sb.LastDay}");
                foreach (var (id, d) in sa.Deeds) { if (!sb.Deeds.TryGetValue(id, out var d2)) { output.WriteLine($"deed {id} only in original: {d.DeedId} heard {d.HeardTotal}"); continue; } if (d.HeardTotal != d2.HeardTotal) output.WriteLine($"deed {id} {d.DeedId} heard {d.HeardTotal} vs {d2.HeardTotal}"); }
                foreach (var (id, d) in sb.Deeds) if (!sa.Deeds.ContainsKey(id)) output.WriteLine($"deed {id} only in resumed: {d.DeedId} heard {d.HeardTotal}");
                foreach (var kv in sa.Rows) if (!sb.Rows.TryGetValue(kv.Key, out var r2) || !r2.Equals(kv.Value)) output.WriteLine($"row {kv.Key}: {kv.Value.ValuePm}/{kv.Value.Heard} vs {(sb.Rows.TryGetValue(kv.Key, out var r3) ? r3.ValuePm + "/" + r3.Heard : "none")}");
                break;
            }
        }
        var want = P6Game.Sections(rt); var got = P6Game.Sections(back);
        foreach (var k in want.Keys) Assert.True(want[k].AsSpan().SequenceEqual(got[k]), $"section {k} differs after the resume");
    }
}
