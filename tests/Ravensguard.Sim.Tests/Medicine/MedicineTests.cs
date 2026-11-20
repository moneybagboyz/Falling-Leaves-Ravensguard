using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Medicine;

/// <summary>
/// Medicine and treatment, the simulation side (docs/specs/phase6/medicine-and-treatment.md 7): MT-1 content parity, MT-3 the tourniquet on a spurting thigh, MT-4 the ischaemia steps,
/// MT-7 physician seeding, MT-8 the physician's quote and treatment, MT-9 the abstract physic, MT-10 the abstract first aid owed since Phase 4, MT-11 Treat scoring, MT-12 the crutch.
/// </summary>
[Collection("resolver2")]
public class MedicineTests(V2Fixture f)
{
    private BodyPlanV2 H => f.Human;
    private WoundBook Book(ulong seed = 1, ulong key = 7) => new(seed, key, 5000, 10, "human") { RegenPm = 0 };

    private WoundV2 Struct(string part, string sid, WoundFlags fl, int rate10)
    {
        int p = f.P(part); var pt = H.Parts[p];
        int si = Array.FindIndex(pt.Structures, s => s.Id == sid); Assert.True(si >= 0, sid);
        return new WoundV2 { Part = (short)p, Tissue = (short)pt.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rate10, Flags = fl };
    }
    private WoundV2 Skin(string part, int rate10 = 5)
    {
        int p = f.P(part); var l = H.Parts[p].Layers[0];
        return new WoundV2 { Part = (short)p, Tissue = (short)l.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 300, BleedX10 = rate10 };
    }
    private WoundV2 Muscle(string part, int rate10, byte grade = 1)
    {
        int p = f.P(part); var l = H.Parts[p].Layers.First(x => x.IsMuscle);
        return new WoundV2 { Part = (short)p, Tissue = (short)l.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Crush, DepthPm = 600, Grade = grade, BleedX10 = rate10 };
    }
    private void Hit(WoundBook b, long sec, params WoundV2[] w) => b.Apply(H, w, sec, sec, 0, 0, false);

    // ------------------------------------------------------------------------------------------------ MT-1

    /// <summary>The healing table as it stood in code at b5d9234 (wounds 3.4), kept here as the golden.</summary>
    private static int LegacyNeedX10(WoundCat cat, byte grade, int depthPm, bool treated, bool repaired) => cat switch
    {
        WoundCat.Skin or WoundCat.Inset or WoundCat.Vessel => depthPm < 500 ? (treated ? 60 : 100) : (treated ? 80 : 140),
        WoundCat.Muscle => (grade switch { 1 => 30, 2 => 150, _ => 400 }) * (treated ? 10 : 15) / 10,
        WoundCat.Bone => grade switch { 1 => treated ? 100 : 150, 2 => treated ? 200 : 350, 3 => treated ? 280 : 450, 4 => treated ? 400 : 600, _ => 30 },
        WoundCat.Ligament => (grade == 1 ? 120 : 300) * (treated ? 10 : 15) / 10,
        WoundCat.Tendon => 400,
        WoundCat.Organ => repaired ? 300 : 0,
        WoundCat.Severed => 100,
        _ => 0,
    };

    private static void AssertLegacyTables()
    {
        int[] secs = [2, 4, 6, 12, 20, 30, 20, 10, 90, 90, 180, 60];
        for (int i = 0; i < secs.Length; i++) Assert.Equal(secs[i], Treatment.SimSeconds((TreatKind)i));
        Assert.Equal(6, Treatment.SimSeconds(TreatKind.ReleasePressure));                       // the one new number: letting go took no time in Phase 4 and nobody asked
        Assert.Equal(10, Treatment.MinSkill(TreatKind.Stitch)); Assert.Equal(50, Treatment.MinSkill(TreatKind.TendonRepair)); Assert.Equal(60, Treatment.MinSkill(TreatKind.LigamentRepair));
        Assert.Equal(60, Treatment.MinSkill(TreatKind.OrganRepair)); Assert.Equal(30, Treatment.MinSkill(TreatKind.Amputate)); Assert.Equal(0, Treatment.MinSkill(TreatKind.SetBone));
        Assert.Equal([("bandage", 1)], Treatment.Needs(TreatKind.Bandage));
        Assert.Equal([("rope", 1), ("bandage", 2)], Treatment.Needs(TreatKind.Tourniquet));
        Assert.Equal([("splint", 1)], Treatment.Needs(TreatKind.Splint));
        foreach (var k in new[] { TreatKind.Stitch, TreatKind.TendonRepair, TreatKind.LigamentRepair, TreatKind.OrganRepair }) Assert.Equal([("needle_and_thread", 1)], Treatment.Needs(k));
        foreach (var k in new[] { TreatKind.Pressure, TreatKind.SetBone, TreatKind.Clean, TreatKind.Cauterise, TreatKind.Amputate, TreatKind.ReleasePressure }) Assert.Empty(Treatment.Needs(k));
        foreach (var cat in Enum.GetValues<WoundCat>())
            for (int g = 0; g <= 5; g++)
                foreach (int depth in new[] { 0, 300, 499, 500, 900 })
                    foreach (bool tr in new[] { false, true })
                        foreach (bool rep in new[] { false, true })
                            Assert.Equal(LegacyNeedX10(cat, (byte)g, depth, tr, rep), MedicineTables.NeedX10(cat, (byte)g, depth, tr, rep));
        for (byte g = 0; g <= 5; g++) Assert.Equal(g switch { 2 => 50, 3 => 80, 4 => 100, _ => 0 }, MedicineTables.MalunionPct(g));
    }

    [Fact]
    public void MT1_Content_LoadsToTheSameValuesAsTheConstantsOfPhase4()
    {
        MedicineRules.Install(null);
        AssertLegacyTables();
        var loaded = MedicineRules.From(f.Db);
        var def = MedicineRules.Defaults();
        Assert.Equal(def.Treat.Length, loaded.Treat.Length);
        for (int i = 0; i < def.Treat.Length; i++)
        {
            Assert.Equal(def.Treat[i].SimS, loaded.Treat[i].SimS); Assert.Equal(def.Treat[i].MinSkill, loaded.Treat[i].MinSkill);
            Assert.Equal(def.Treat[i].Needs, loaded.Treat[i].Needs); Assert.Equal(def.Treat[i].ToolQuality, loaded.Treat[i].ToolQuality);
            Assert.Equal(def.Treat[i].ToolLevel, loaded.Treat[i].ToolLevel); Assert.Equal(def.Treat[i].Light, loaded.Treat[i].Light);
        }
        Assert.Equal(def.Healing.Muscle, loaded.Healing.Muscle); Assert.Equal(def.Healing.Ligament, loaded.Healing.Ligament); Assert.Equal(def.Healing.BoneTreated, loaded.Healing.BoneTreated);
        Assert.Equal(def.Healing.Bone, loaded.Healing.Bone); Assert.Equal(def.Healing.Malunion, loaded.Healing.Malunion);
        Assert.Equal((def.Healing.ShallowTreated, def.Healing.Shallow, def.Healing.DeepTreated, def.Healing.Deep, def.Healing.DeepFromPm, def.Healing.Treated, def.Healing.Untreated, def.Healing.BoneOther, def.Healing.Tendon, def.Healing.OrganRepaired, def.Healing.Severed),
            (loaded.Healing.ShallowTreated, loaded.Healing.Shallow, loaded.Healing.DeepTreated, loaded.Healing.Deep, loaded.Healing.DeepFromPm, loaded.Healing.Treated, loaded.Healing.Untreated, loaded.Healing.BoneOther, loaded.Healing.Tendon, loaded.Healing.OrganRepaired, loaded.Healing.Severed));
        Assert.Equal((8, 4), loaded.Relief["ale"]); Assert.Equal((15, 4), loaded.Relief["poultice"]); Assert.Equal((40, 4), loaded.Relief["poppy_milk"]);
        Assert.Equal(550, loaded.Aids["crutch"].OneLegPm); Assert.Equal(1, loaded.Aids["crutch"].Hands); Assert.Equal(2, loaded.Aids["litter"].Carriers); Assert.Equal(600, loaded.Aids["litter"].CarrySpeedPm);
        foreach (var (id, mites) in new[] { ("visit", 50), ("clean", 100), ("stitch", 300), ("set_bone", 400), ("splint", 150), ("tourniquet", 50), ("tendon_repair", 1200), ("ligament_repair", 1500), ("organ_repair", 2000), ("amputate", 800), ("poppy_dose", 600), ("bed_day", 60) })
            Assert.Equal(mites, loaded.Prices[id].Mites);
        Assert.Equal(60, loaded.Rules.BarberSharePct);
        MedicineRules.Install(f.Db);
        try { AssertLegacyTables(); } finally { MedicineRules.Install(null); }
    }

    [Fact]
    public void MT1_AWorldWithoutTheMedicineFiles_PlaysAsPhase4Did()
    {
        var db = new ContentDatabase();
        var rules = MedicineRules.From(db);
        Assert.Equal(MedicineRules.Defaults().Treat[(int)TreatKind.Stitch].MinSkill, rules.Treat[(int)TreatKind.Stitch].MinSkill);
        Assert.Equal(300, rules.Prices["stitch"].Mites);
    }

    // ------------------------------------------------------------------------------------------------ MT-3

    [Fact]
    public void MT3_Tourniquet_OnAThighArteryWithRope_DoneAtSixSeconds_Loses600ml_ThenRateTimes002()
    {
        var b = Book(); Hit(b, 0, Struct("upper_leg_l", "femoral_artery", WoundFlags.ArteryHit, 1000));
        Assert.Equal(100_000, b.RateMilliAt(0));
        int root = TreatAi.LimbRoot(H, f.P("upper_leg_l"));
        var bag = new ItemBag().Add("rope", 1);
        long done = (long)Treatment.SimSeconds(TreatKind.Tourniquet) * 20;
        Assert.Equal(120, done);
        var r = Treatment.Perform(b, H, new(TreatKind.Tourniquet, root, done, 0, 5), bag);
        Assert.True(r.Ok); Assert.Equal(0, bag.Count("rope"));
        long lostMl = (b.BloodMaxUl - b.BloodUl) / 1000;
        Assert.InRange(lostMl, 595, 605);                                                            // 100 ml/s for 6 s
        Assert.Equal(2_000, b.RateMilliAt(7));                                                        // x0.02
        Assert.Equal(Dressing.Tourniquet, b.Records[0].Dressing);
    }

    // ------------------------------------------------------------------------------------------------ MT-4

    private (WoundBook Book, int Root, int Muscle) LimbWithTourniquet(long tieAtSec = 0)
    {
        var b = Book();
        Hit(b, 0, Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 50), Muscle("lower_arm_l", 0, 1));
        int root = TreatAi.LimbRoot(H, f.P("upper_arm_l"));
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Tourniquet, root, tieAtSec), new ItemBag().Add("rope", 1)).Ok);
        int m = b.Records.FindIndex(w => w.Cat == WoundCat.Muscle);
        Assert.True(m >= 0);
        return (b, root, m);
    }

    [Fact]
    public void MT4_TourniquetSteps_MuscleDropsAt360And1080_LimbLostAt2160()
    {
        var (b, root, m) = LimbWithTourniquet();
        long Sec(long simS) => simS * 20;
        b.AdvanceTo(Sec(359)); Assert.Equal(1, b.Records[m].Grade);
        b.AdvanceTo(Sec(361)); Assert.Equal(2, b.Records[m].Grade);                                  // bruised -> torn at 360
        Assert.Contains(b.Events, e => e.Kind == BookEventKind.Ischaemia && e.Note == "step:1");
        b.AdvanceTo(Sec(1079)); Assert.Equal(2, b.Records[m].Grade);
        b.AdvanceTo(Sec(1081)); Assert.Equal(3, b.Records[m].Grade);                                 // torn -> mangled at 1,080
        Assert.Equal(0UL, b.Permanent.AmputatedMask);
        b.AdvanceTo(Sec(2159)); Assert.Equal(0UL, b.Permanent.AmputatedMask);
        b.AdvanceTo(Sec(2161));
        Assert.NotEqual(0UL, b.Permanent.AmputatedMask & (1UL << root));                            // the limb is lost at 2,160
        Assert.Contains(b.Events, e => e.Kind == BookEventKind.Amputated);
        Assert.False(b.Dead);
    }

    [Fact]
    public void MT4_Warnings_ComeAt300And1020()
    {
        var (b, _, _) = LimbWithTourniquet();
        b.AdvanceTo(299 * 20); Assert.DoesNotContain(b.Events, e => e.Kind == BookEventKind.TourniquetWarning);
        b.AdvanceTo(301 * 20); Assert.Contains(b.Events, e => e.Kind == BookEventKind.TourniquetWarning && e.Note == "step:1");
        b.AdvanceTo(1021 * 20); Assert.Contains(b.Events, e => e.Kind == BookEventKind.TourniquetWarning && e.Note == "step:2");
    }

    [Fact]
    public void MT4_LooseningAt1000_AndRetyingAt1100_KeepsTheClockAt1000()
    {
        var (b, root, m) = LimbWithTourniquet();
        b.AdvanceTo(1000 * 20 - 1);
        Assert.Equal(2, b.Records[m].Grade);
        Assert.True(Treatment.Perform(b, H, new(TreatKind.ReleasePressure, root, 1000 * 20)).Ok);
        Assert.All(b.Records.Where(w => w.Cat != WoundCat.Muscle), w => Assert.Equal(Dressing.None, w.Dressing));
        b.AdvanceTo(1100 * 20);                                                                      // it bleeds again, and the clock does not run
        Assert.Equal(1000u, b.Records.Max(w => w.TourniquetOnS));
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Tourniquet, root, 1100 * 20), new ItemBag().Add("rope", 1)).Ok);
        Assert.Equal(1000u, b.Records.Max(w => w.TourniquetOnS));                                    // tied again: carries on, no reset, no free hour
        b.AdvanceTo(1170 * 20); Assert.Equal(2, b.Records[m].Grade);                                 // 1,070 on the limb
        b.AdvanceTo(1185 * 20); Assert.Equal(3, b.Records[m].Grade);                                 // 1,085: the second step
    }

    [Fact]
    public void MT4_TheClockIsInTheHash_AndTheSave_AndAnOldBookHashesAsBefore()
    {
        var (b, _, _) = LimbWithTourniquet();
        b.AdvanceTo(400 * 20);
        var s = WoundSave.Of(7, b);
        var back = WoundSave.ToBook(s, 1);
        Assert.Equal(b.Hash(), back.Hash());
        Assert.Contains(back.Records, w => w.TourniquetOnS >= 399 && w.DistalMask != 0);
        var plain = Book(); Hit(plain, 0, Skin("upper_body"));
        var h = plain.Hash();
        Assert.Equal(h, WoundSave.ToBook(WoundSave.Of(7, plain), 1).Hash());
    }

    // ------------------------------------------------------------------------------------------------ MT-7

    private static SimWorld World() => new(EconFixture.Seed, EconFixture.Pop.Clone(), EconFixture.NewEconomy());

    private static int[] AliveBySettlement(SimWorld w)
    {
        var n = new int[w.Population.Region.Settlements.Count];
        for (int i = 0; i < w.People.Count; i++) if (w.People.IsAlive(i) && w.People.HomeSettlement[i] is >= 0 and var s && s < n.Length) n[s]++;
        return n;
    }

    [Fact]
    public void MT7_Seeding_OnePhysicianPerThousandInATown_ABarberInEveryVillageOf150()
    {
        var w = World(); var book = PhysicianSeeding.Run(w);
        var alive = AliveBySettlement(w); var region = w.Population.Region;
        int towns = 0, barbers = 0;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            var cls = region.Settlements[s].Class;
            if (cls == SettlementClass.Town)
            {
                towns++;
                Assert.Equal(Math.Max(1, alive[s] / 1000), book.Physicians(s));
                Assert.Equal(0, book.Barbers(s));
            }
            else if (cls == SettlementClass.Village && alive[s] >= 150) { Assert.Equal(1, book.Barbers(s)); Assert.Equal(0, book.Physicians(s)); barbers++; }
            else Assert.Empty(book.In(s));
        }
        Assert.True(towns >= 1);
        foreach (var row in book.Rows)
        {
            int p = w.People.IndexOf(new EntityId(row.Person)); Assert.True(p >= 0);
            int surgery = w.People.SkillLevelOf(p, Skill.Surgery);
            if (row.Kind == PhysicianKind.Physician) Assert.InRange(surgery, 45, 70);
            else Assert.InRange(surgery, 25, 40);
            Assert.Equal(row.Settlement, w.People.HomeSettlement[p]);
            Assert.True(w.People.AgeYears(p, w.Population.NowDay) >= 18);
        }
        Assert.True(book.Count >= towns);
        _ = barbers;
    }

    [Fact]
    public void MT7_Seeding_IsTheSameOnEveryRun_AndSkillsAreNeverLowered()
    {
        var a = PhysicianSeeding.Run(World()); var b = PhysicianSeeding.Run(World());
        Assert.Equal(a.Rows, b.Rows);
        var w = World();
        var before = w.People.Clone();
        var book = PhysicianSeeding.Run(w);
        foreach (var row in book.Rows)
        {
            int p = w.People.IndexOf(new EntityId(row.Person));
            Assert.True(w.People.SkillLevelOf(p, Skill.Surgery) >= before.SkillLevelOf(p, Skill.Surgery));
        }
        Assert.Same(book, PhysicianSeeding.Run(w));                                                   // a second pass changes nothing
        Assert.Equal(book.Count, a.Count);
    }

    [Fact]
    public void MT7_ThePhysicianBook_RoundTripsThroughItsSection_ByteIdentical()
    {
        var w = World(); var book = PhysicianSeeding.Run(w);
        var bytes = SaveCodec.SerializeCompressed(book.Capture());
        var back = PhysicianBook.Restore(SaveCodec.DeserializeCompressed<MedicineSave>(bytes));
        Assert.Equal(book.Rows, back.Rows);
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(back.Capture()));
    }

    // ------------------------------------------------------------------------------------------------ MT-8

    [Fact]
    public void MT8_PhysicianQuote_ATornTendonTenHoursOld_Costs1200_PaysTheBox_CoinConserved()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "biceps_tendon", WoundFlags.TendonHit, 0));
        long now = 10 * 3600;
        const int skill = 60;
        var quote = TreatDesk.Quote(b, H, PhysicianKind.Physician, skill, now);
        var line = Assert.Single(quote, l => l.Kind == TreatKind.TendonRepair);
        Assert.Equal(1200, line.FeeMites);
        Assert.Equal(Math.Clamp(40 + skill - 50, 40, 95), line.ChancePct);
        Assert.DoesNotContain(TreatDesk.Quote(b, H, PhysicianKind.Barber, skill, now), l => l.Kind == TreatKind.TendonRepair);          // a barber does not
        Assert.DoesNotContain(TreatDesk.Quote(b, H, PhysicianKind.Physician, 49, now), l => l.Kind == TreatKind.TendonRepair);           // below skill 50 nobody offers it

        var ledger = new Ledger();
        var patient = ledger.Open(PurseKind.Household, EntityId.Make(IdKind.Person, 1)); var box = ledger.Open(PurseKind.Business, EntityId.Make(IdKind.Person, 2));
        ledger.Mint(patient, new Coin(5000), SourceReason.Seed);
        var done = TreatDesk.Perform(ledger, b, H, [line], PhysicianKind.Physician, skill, 99, patient, box, now);
        var o = Assert.Single(done);
        Assert.True(o.Done); Assert.Equal(1200, o.PaidMites);
        Assert.Equal(3800, ledger.Balance(patient).Mites); Assert.Equal(1200, ledger.Balance(box).Mites);
        Assert.True(ledger.Audit().IsConserved);
        long finish = now + 90 * 20;
        bool expected = DetRng.Range(b.Seed, finish / 20, 99, RngStreams.SurgeryResult, b.Records[0].Id, 0, 100) < line.ChancePct;      // the result uses stream 0x422
        Assert.Equal(expected, o.Success);
        Assert.Equal(expected, b.Records[0].Is(Aid.Repaired));
        Assert.Equal(RngStreams.SurgeryResult, 0x422u);
    }

    [Fact]
    public void MT8_APatientWhoCannotPay_IsNotTreated()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 150));
        var quote = TreatDesk.Quote(b, H, PhysicianKind.Physician, 50, 100);
        Assert.NotEmpty(quote);
        var ledger = new Ledger();
        var patient = ledger.Open(PurseKind.Household, EntityId.Make(IdKind.Person, 1)); var box = ledger.Open(PurseKind.Business, EntityId.Make(IdKind.Person, 2));
        ledger.Mint(patient, new Coin(10), SourceReason.Seed);
        var done = TreatDesk.Perform(ledger, b, H, quote, PhysicianKind.Physician, 50, 99, patient, box, 100);
        Assert.False(done[0].Done); Assert.Equal(0, ledger.Balance(box).Mites); Assert.Equal(Dressing.None, b.Records[0].Dressing);
    }

    // ------------------------------------------------------------------------------------------------ MT-9 / MT-10

    private static void TopUp(SimWorld w, int hh, long mites)
    {
        var p = w.Economy.HouseholdPurse[hh]; long have = w.Economy.Ledger.Balance(p).Mites;
        if (have < mites) w.Economy.Ledger.Mint(p, new Coin(mites - have), SourceReason.Seed);
    }

    private WoundLedger LedgerOf(SimWorld w, IEnumerable<int> rows, long hourStartSec)
    {
        var l = new WoundLedger(w.Seed, hourStartSec);
        foreach (int r in rows)
        {
            ulong key = w.People.IdOf(r).Value;
            var b = new WoundBook(w.Seed, key, 5000, 10, "human") { RegenPm = 0, LastSec = hourStartSec, DayDone = hourStartSec / 86_400 };
            b.Apply(H, [Skin("upper_body", 20)], hourStartSec, hourStartSec, 0, 0, false);
            l.Books[key] = b;
        }
        return l;
    }

    [Fact]
    public void MT9_AbstractPhysic_TreatsEightyPercentWithAPhysician_SixtyWithABarber()
    {
        var w = World(); var book = PhysicianSeeding.Run(w);
        var region = w.Population.Region;
        (int Seen, int N) phys = (0, 0), barb = (0, 0);
        foreach (var (kind, accept) in new[] { (PhysicianKind.Physician, 0), (PhysicianKind.Barber, 1) })
        {
            var rows = new List<int>();
            for (int i = 0; i < w.People.Count && rows.Count < 400; i++)
            {
                if (!w.People.IsAlive(i) || w.People.Household[i] < 0 || w.People.HomeSettlement[i] < 0) continue;
                var men = book.In(w.People.HomeSettlement[i]); if (men.Count == 0 || men[0].Kind != kind) continue;
                if (men.Any(m => m.Person == w.People.IdOf(i).Value)) continue;
                rows.Add(i);
            }
            Assert.True(rows.Count >= 100, $"{kind}: {rows.Count} people");
            foreach (int r in rows) TopUp(w, w.People.Household[r], 1_000_000);
            int seen = 0, n = 0;
            for (int day = 1; n < 2400; day++)
            {
                long hs = day * 86_400L + 9 * 3600;
                var l = LedgerOf(w, rows, hs);
                var s = AbstractPhysic.Run(w, l, hs);
                seen += s?.Count ?? 0; n += rows.Count;
            }
            double pct = seen * 100.0 / n;
            Assert.InRange(pct, (accept == 0 ? 80 : 60) - 3.0, (accept == 0 ? 80 : 60) + 3.0);
            if (accept == 0) phys = (seen, n); else barb = (seen, n);
        }
        Assert.True(phys.N > 0 && barb.N > 0);
        _ = region;
    }

    [Fact]
    public void MT9_AbstractPhysic_TheFeeMoves_TheWoundIsDressed_CoinIsConserved_NoFeeNoTreatment()
    {
        var w = World(); var book = PhysicianSeeding.Run(w);
        var row = Enumerable.Range(0, w.People.Count).First(i => w.People.IsAlive(i) && w.People.Household[i] >= 0 && w.People.HomeSettlement[i] >= 0 && book.In(w.People.HomeSettlement[i]).Count > 0
            && !book.In(w.People.HomeSettlement[i]).Any(m => m.Person == w.People.IdOf(i).Value));
        int hh = w.People.Household[row]; TopUp(w, hh, 100_000);
        var man = book.In(w.People.HomeSettlement[row])[0];
        int mrow = w.People.IndexOf(new EntityId(man.Person)); var manPurse = w.Economy.HouseholdPurse[w.People.Household[mrow]];
        long before = w.Economy.Ledger.Balance(w.Economy.HouseholdPurse[hh]).Mites, manBefore = w.Economy.Ledger.Balance(manPurse).Mites;
        ulong key = w.People.IdOf(row).Value;
        long hs = 0; HashSet<ulong>? seen = null; WoundLedger? l = null;
        for (int day = 1; seen == null || !seen.Contains(key); day++) { hs = day * 86_400L + 3600; l = LedgerOf(w, [row], hs); seen = AbstractPhysic.Run(w, l, hs); Assert.True(day < 60); }
        Assert.NotEqual(Dressing.None, l!.Books[key].Records[0].Dressing);
        long paid = before - w.Economy.Ledger.Balance(w.Economy.HouseholdPurse[hh]).Mites;
        Assert.True(paid > 0);
        Assert.Equal(paid, w.Economy.Ledger.Balance(manPurse).Mites - manBefore);
        Assert.True(w.Economy.Ledger.Audit().IsConserved);

        // a household with no coin is not seen by the physician, whatever the draw says
        var poor = Enumerable.Range(0, w.People.Count).First(i => i != row && w.People.IsAlive(i) && w.People.Household[i] >= 0 && w.People.Household[i] != hh && w.People.HomeSettlement[i] == w.People.HomeSettlement[row]
            && w.People.Household[i] != w.People.Household[mrow]);
        var poorPurse = w.Economy.HouseholdPurse[w.People.Household[poor]];
        w.Economy.Ledger.Sink(poorPurse, w.Economy.Ledger.Balance(poorPurse), SinkReason.Burn);
        for (int day = 1; day <= 40; day++) { hs = day * 86_400L; var lp = LedgerOf(w, [poor], hs); Assert.Null(AbstractPhysic.Run(w, lp, hs)); }
    }

    [Fact]
    public void MT9_WithNoPhysician_NothingChanges_AndTheWoundSystemStillRunsFirstAid()
    {
        var w = World();                                                                               // no physician book in the world
        var rows = Enumerable.Range(0, w.People.Count).Where(i => w.People.IsAlive(i) && w.People.Household[i] >= 0).Take(20).ToList();
        var l = LedgerOf(w, rows, 3600);
        Assert.Null(AbstractPhysic.Run(w, l, 3600));
    }

    [Fact]
    public void MT10_AbstractFirstAid_Dresses90WithTheHouseholdHome_40WithNeighboursOnly_NoneAlone_AtMostFourWounds()
    {
        var w = World(); var pt = w.People;
        // three disjoint groups, by household: the household at home (more than one member), neighbours only (the house holds just him, he lives in a settlement), alone (the road: no settlement)
        var home = new List<int>(); var neigh = new List<int>(); var alone = new List<int>();
        var usedHh = new HashSet<int>();
        for (int i = 0; i < pt.Count && (home.Count < 250 || neigh.Count < 250 || alone.Count < 250); i++)
        {
            if (!pt.IsAlive(i) || pt.Household[i] < 0 || pt.HomeSettlement[i] < 0) continue;
            int hh = pt.Household[i];
            if (!usedHh.Add(hh)) continue;
            if (w.Households.MemberCount[hh] > 1 && home.Count < 250) home.Add(i);
            else if (neigh.Count < 250) { neigh.Add(i); w.Households.MemberCount[hh] = 1; }
            else if (alone.Count < 250) { alone.Add(i); w.Households.MemberCount[hh] = 1; pt.HomeSettlement[i] = -1; }
        }
        Assert.True(home.Count >= 100, $"{home.Count} with a household at home");
        Assert.True(neigh.Count >= 50, $"{neigh.Count} with neighbours only");
        Assert.True(alone.Count >= 50, $"{alone.Count} alone");
        double Rate(List<int> rows)
        {
            int dressed = 0, n = 0;
            for (int h = 1; n < 10_000; h++)
            {
                long hs = h * 3600L;
                var l = LedgerOf(w, rows, hs);
                AbstractFirstAid.Run(w, l, hs);
                foreach (var b in l.Books.Values) { if (b.Records.Any(r => r.Dressing != Dressing.None)) dressed++; n++; }
            }
            return dressed * 100.0 / n;
        }
        Assert.InRange(Rate(home), 88.5, 91.5);
        Assert.InRange(Rate(neigh), 38.5, 41.5);
        Assert.Equal(0, Rate(alone));

        // at most four wounds an hour
        var hs5 = 5 * 3600L; var many5 = LedgerOf(w, [home[0]], hs5); var bk = many5.Books.Values.First();
        bk.Apply(H, [Skin("upper_arm_l", 20), Skin("lower_arm_l", 20), Skin("upper_leg_l", 20), Skin("lower_leg_l", 20), Skin("head", 20), Skin("hand_l", 20)], hs5, hs5, 0, 0, false);
        for (int h = 6; bk.Records.All(r => r.Dressing == Dressing.None); h++) { many5 = LedgerOf(w, [home[0]], h * 3600L); bk = many5.Books.Values.First(); bk.Apply(H, [Skin("upper_arm_l", 20), Skin("lower_arm_l", 20), Skin("upper_leg_l", 20), Skin("lower_leg_l", 20), Skin("head", 20), Skin("hand_l", 20)], h * 3600L, h * 3600L, 0, 0, false); AbstractFirstAid.Run(w, many5, h * 3600L); Assert.True(h < 400); }
        Assert.True(bk.Records.Count(r => r.Dressing != Dressing.None) <= 4);
    }

    // ------------------------------------------------------------------------------------------------ MT-11

    [Fact]
    public void MT11_TreatScoring_AVillagerWithBandagesFourMetresFromHisBleedingBrother_Scores650_AStranger325_ABadlyHurtManNone()
    {
        var brain = UtilityBrain.Default;
        var c = new ChoiceContext { Scheduled = ActionKind.Work, Current = ActionKind.Work, TreatAlliesNear = 1 };
        Assert.Equal(650, UtilityBrain.TreatScore(c));
        Assert.Equal(325, UtilityBrain.TreatScore(c with { TreatAlliesNear = 0, TreatStrangersNear = 1 }));
        Assert.Equal(0, UtilityBrain.TreatScore(c with { OwnPainPct = 61 }));
        Assert.Equal(650, UtilityBrain.TreatScore(c with { OwnPainPct = 60 }));
        Assert.Equal(0, UtilityBrain.TreatScore(new ChoiceContext()));
        Assert.Equal(Math.Min(1000, 500 + 150 * 3 + 10), UtilityBrain.TreatScore(c with { TreatAlliesNear = 5, FirstAidPm = 100 }));
        var calm = new NeedInputs();
        var ch = brain.Choose(calm, c);
        Assert.Equal(ActionKind.Treat, ch.Kind); Assert.Equal(650, ch.Score);
        Assert.NotEqual(ActionKind.Treat, brain.Choose(calm, c with { OwnPainPct = 80 }).Kind);
        Assert.NotEqual(ActionKind.Treat, brain.Choose(calm, c with { TreatAlliesNear = 0 }).Kind);
        // Flee still wins
        var scared = new NeedInputs { ThreatPm = 1000 };
        Assert.Equal(ActionKind.Flee, brain.Choose(scared, c with { CanFlee = true }).Kind);

        // and he dresses him: the next treatment with two bandages is a bandage on the worst bleeder
        var brother = Book(2, 8); Hit(brother, 0, Struct("lower_arm_l", "radial_vein", WoundFlags.VeinHit, 50));
        var bag = new ItemBag().Add("bandage", 2);
        var req = TreatAi.Next(brother, H, bag, 40, 20, 5)!.Value;
        Assert.Equal(TreatKind.Bandage, req.Kind);
        Assert.True(Treatment.Perform(brother, H, req with { GameSec = 40 + 80 }, bag).Ok);
        Assert.Equal(1, bag.Count("bandage")); Assert.Equal(Dressing.Bandage, brother.Records[0].Dressing);
    }

    // ------------------------------------------------------------------------------------------------ MT-12

    [Fact]
    public void MT12_MakeCrutch_OnePole_OneCrutch_InAnHour_NoFailureBelowSkill5()
    {
        var recipe = f.Db.All<CraftRecipeDef>().Single(r => r.Id == "make_crutch");
        Assert.Equal(5, recipe.Difficulty);
        Assert.Equal("crutch", recipe.Results.Single().Good); Assert.Equal(1, recipe.Results.Single().Pieces);
        Assert.Equal(2.0, recipe.Components.Single().Single().Kg);                                        // 2 kg of wood: a pole
        Assert.Equal(1.0, recipe.Steps.Sum(s => s.Hours + s.Minutes / 60.0));                             // 1.0 h
        Assert.Contains(recipe.Tools, t => t.Quality == "cutting" && t.Level == 1);
        Assert.Null(recipe.Workstation);                                                                  // any station, or a field kit
        Assert.True(recipe.Learn.KnownByAll);
    }

    [Fact]
    public void MT12_ACrutchLiftsTheHopFrom250To550_AndTakesAHand()
    {
        var b = Book(); Hit(b, 0, new WoundV2 { Part = (short)f.P("upper_leg_l"), Tissue = (short)H.Parts[f.P("upper_leg_l")].Layers.First(x => x.IsBone).Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000, Grade = 2, Flags = WoundFlags.BoneBroken });
        Assert.Equal(250, b.Function(H, crutch: Crutches.Aids(0).Crutch).WalkSpeedPm);
        Assert.Equal(550, b.Function(H, crutch: Crutches.Aids(1).Crutch).WalkSpeedPm);
        Assert.Equal(250, Crutches.WalkPm(1, 0)); Assert.Equal(550, Crutches.WalkPm(1, 1)); Assert.Equal(550, Crutches.WalkPm(1, 2));
        Assert.Equal(1000, Crutches.WalkPm(2, 2)); Assert.Equal(0, Crutches.WalkPm(0, 2)); Assert.Equal(0, Crutches.WalkPm(0, 1)); Assert.True(Crutches.Seated(0, 2));
        Assert.Equal(1, Crutches.HandsTaken(1)); Assert.Equal(2, Crutches.HandsTaken(2));
        Assert.True(Crutches.CanHold(1, 1)); Assert.False(Crutches.CanHold(1, 2));                      // one crutch: a one-handed weapon, no shield beside it, no two-hander
        Assert.False(Crutches.CanHold(2, 1));
        Assert.Equal(600 * 1000 / 1000, Crutches.LitterSpeedPm(2, 1000)); Assert.Equal(0, Crutches.LitterSpeedPm(1, 1000));
    }
}
