using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Dialogue;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Law;

/// <summary>
/// The courts and the gaol (docs/specs/phase6/crime-and-justice.md, P6-C1): CJ-5 (arrest, the cell, bread), CJ-6 (the Valeholt ladder: the stocks, twenty lashes, the hand), CJ-7 (weregild and outlawry),
/// CJ-8 (mercy and clergy), CJ-9 (no witness), CJ-10 (the gaoler's price), CJ-11 (the rope, and a break before dawn), CJ-12 (the lord's own law), CJ-14 (the hero as judge), CJ-16 (the band feeds its captives)
/// and the save round trip of the courts' records.
/// </summary>
public class JusticeTests(ITestOutputHelper output)
{
    private static readonly Lazy<ContentDatabase> Db = new(V2Fixture.Load);
    private static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Hero = EntityId.None;

    /// <summary>The cultures of the test: Valeholt's table, Northmark's weregild for murder, a plain fine for the rest.</summary>
    private static string? Vale(int s, string kind) => kind switch { "theft" => "stocks_then_hand", "murder" => "hanging", "assault" => "fine", "heresy" => "penance", "bribery" => "fine", _ => null };
    private static string? North(int s, string kind) => kind switch { "theft" => "fine_or_hand", "murder" => "weregild_or_outlawry", "assault" => "weregild", _ => null };

    private sealed class Rig
    {
        public Chronicle C; public LawWorld W = null!; public CrimeBook Cb = new(); public JusticeBook Jb = new(); public Ledger L = new(); public JobTable Jobs = new(); public PurseId Treasury;
        public PurseId? Baker; public CaptiveBook Cap; public WoundLedger Wounds; public MarkBook Marks = new(); public PersonTable P; public IdAllocator Ids = new();
        public Dictionary<int, PurseId> Hh = [];
        public List<(EntityId Who, long Sec, ulong Entry)> Hanged = []; public List<(EntityId Who, int Realm)> Forfeits = []; public List<(EntityId Who, int Delta)> Grievances = [];
        public Func<EntityId, int, int> Standing = (_, _) => 0; public Func<EntityId, int> Literacy = _ => 0; public Func<EntityId, int> Honesty = _ => 45;
        public bool HeroLord; public PurseId HeroPurse;

        public Rig(Func<int, string, string?> culture, LawSettingsDef? s = null, ulong seed = 11, int people = 80)
        {
            var (tw, chron) = TalkKit.Town(people, seed); C = chron; P = tw.People;
            P.Occupation[0] = Occupation.Guard; P.Occupation[1] = Occupation.Guard; P.Occupation[2] = Occupation.Guard; P.Occupation[3] = Occupation.Guard;       // the watch: the gaoler is person 0
            Treasury = L.Open(PurseKind.Treasury, Fief); L.Mint(Treasury, new Coin(200_000), SourceReason.Seed);
            HeroPurse = L.Open(PurseKind.Person, EconomyState.HeroOwner); L.Mint(HeroPurse, new Coin(20_000), SourceReason.Seed);
            Cap = new CaptiveBook(seed); Wounds = new WoundLedger(seed);
            var db = Db.Value; var plan = new CombatTables(db).V2.Plan("human");
            var settings = s ?? new LawSettingsDef { Id = "law", ArrestPct = 0 };
            var content = new LawContent(db.All<CrimeDef>(), settings, db.All<PunishmentDef>());
            W = new LawWorld
            {
                Chronicle = C, Ids = Ids, People = P, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [Fief], FiefSeat = [0], Seed = seed,
                Ledger = L, Jobs = Jobs, JobEnv = new JobEnv { Ledger = L }, TreasuryOf = _ => Treasury, Law = content, Captives = Cap, Wounds = Wounds, Human = plan, Marks = () => Marks,
                CultureKey = culture, PurseOf = Purse, VictimPurse = v => v == 0 ? null : Purse(new EntityId(v)), LordOf = _ => P.IdOf(1), HeroLordOf = _ => HeroLord,
                StandingWithFief = (p, f) => Standing(p, f), LiteracyOf = p => Literacy(p), HonestyOf = p => Honesty(p),
                Execute = (p, sec, entry) => { Hanged.Add((p, sec, entry)); int r = P.IndexOf(p); if (r >= 0) P.Status[r] = PersonStatus.Dead; },
                Forfeit = (p, realm, sec) => Forfeits.Add((p, realm)), Grievance = (p, d) => Grievances.Add((p, d)), RationPayee = _ => Baker,
            };
        }

        public PurseId? Purse(EntityId p)
        {
            if (p == Hero) return HeroPurse;
            int row = P.IndexOf(p); if (row < 0) return null;
            int h = P.Household[row];
            if (!Hh.TryGetValue(h, out var pu)) { pu = L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, (ulong)h + 1)); L.Mint(pu, new Coin(2_000), SourceReason.Seed); Hh[h] = pu; }
            return pu;
        }

        public EntityId Id(int row) => P.IdOf(row);
        public static long Sec(int day, int hour = 10) => day * 86_400L + hour * 3600;

        /// <summary>A crime done by <paramref name="culprit"/> on <paramref name="day"/> that <paramref name="witness"/> saw and knew him in, reported the next day (the chain and the bounty are real).</summary>
        public CrimeRecord Crime(EntityId culprit, string kind, int day, long value = 240, int witnessRow = 20, int victimRow = 21)
        {
            var rec = CrimeSystem.Commit(W, Cb, new ActSpec(kind, culprit, Id(victimRow), 0, Sec(day), value, 0, "ch_note_crime_greed"), [new WitnessRow(Id(witnessRow).Value, WitnessRow.KnewIt)], ActorLook.None);
            CrimeSystem.Report(W, rec, Id(witnessRow), Sec(day + 1, 0));
            return rec;
        }

        /// <summary>The law's hours from the first hour of <paramref name="from"/> to the last of <paramref name="to"/>.</summary>
        public void Run(int from, int to) { for (long h = from * 24L; h < (to + 1) * 24L; h++) Justice.OnHour(W, Cb, Jb, h); }

        public bool Held(EntityId p) => Cap.Find(p) is { Held: true };
        public IEnumerable<ChronicleEntry> Of(string type) => C.All.Where(e => e.TypeId == type);
    }

    // ------------------------------------------------------------------ CJ-5

    [Fact]
    public void CJ5_AnArrestPutsTheCulpritInTheFiefGaolBound_AndThePrisonerIsFedABreadADayFromTheTreasury()
    {
        var r = new Rig(Vale);
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1);
        var taken = Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), inAct: false, r.Id(0));
        Assert.Single(taken); Assert.Equal(CrimeState.Held, rec.State);
        var cap = r.Cap.Find(thief)!;
        Assert.True(cap.Held); Assert.Equal(CaptorKind.Gaol, cap.Captor); Assert.Equal(Restraint.Bound, cap.Restraint); Assert.Equal(Fief, cap.CaptorId);
        Assert.True(r.Jb.Gaol.ContainsKey(thief.Value)); Assert.Equal(r.Id(0).Value, r.Jb.Gaol[thief.Value].Gaoler);
        // the chain: crime_arrested hangs on the report
        var arrested = r.Of("crime_arrested").Single();
        Assert.Equal(rec.Reported, arrested.Causes[0].EventId!.Value.Value);
        // fed one measure a day (3 mites) from the fief treasury for three days
        r.Baker = r.L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 900));      // S10: the ration is bought from a baker's purse
        long t0 = r.L.Balance(r.Treasury).Mites;
        for (int d = 3; d <= 5; d++) Gaol.Feed(r.W, r.Cb, r.Jb, d, Rig.Sec(d, 0));
        Assert.Equal(t0 - 3 * r.W.S.RationMites, r.L.Balance(r.Treasury).Mites);
        Assert.Equal(3, r.Jb.Gaol[thief.Value].FedDays);
        Assert.True(r.L.Audit().IsConserved);
        // a murderer is chained
        var killer = r.Id(31); var m = r.Crime(killer, "murder", 1, 0);
        Gaol.Arrest(r.W, r.Cb, r.Jb, killer, 0, Rig.Sec(2, 10), false);
        Assert.Equal(Restraint.Chained, r.Cap.Find(killer)!.Restraint);
        // a prisoner nobody can feed is let out after two days: nobody starves in a cell
        r.L.Sink(r.Treasury, r.L.Balance(r.Treasury), SinkReason.Burn);
        Gaol.Feed(r.W, r.Cb, r.Jb, 6, Rig.Sec(6, 0)); Assert.True(r.Held(thief));
        Gaol.Feed(r.W, r.Cb, r.Jb, 7, Rig.Sec(7, 0)); Assert.False(r.Held(thief));
        Assert.Equal(CrimeState.Reported, rec.State);
        Assert.Contains(r.Of("gaol_released"), e => e.Params!.Any(p => p.Key == "jl_why_unfed"));
    }

    [Fact]
    public void EA_F3_S10_ARationWithNoBakerInTheRegionIsNeverBurned_TheTreasuryKeepsItsCoin()
    {
        var r = new Rig(Vale);
        var thief = r.Id(30); r.Crime(thief, "theft", 1);
        Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), inAct: false, r.Id(0));
        long t0 = r.L.Balance(r.Treasury).Mites, burnt0 = r.L.Sunk(SinkReason.Burn).Mites;
        for (int d = 3; d <= 5; d++) Gaol.Feed(r.W, r.Cb, r.Jb, d, Rig.Sec(d, 0));
        Assert.Equal(3, r.Jb.Gaol[thief.Value].FedDays);
        Assert.Equal(burnt0, r.L.Sunk(SinkReason.Burn).Mites);                          // fails before P7-0B: three rations were burned
        Assert.Equal(t0, r.L.Balance(r.Treasury).Mites);                                // the seat's own treasury bought the bread from its own stores
        Assert.True(r.L.Audit().IsConserved);
    }

    [Fact]
    public void CJ5_TheGuardsTakeTheKnownOnTheirWatch_AndTheHourOfTheDayShiftDecidesWhoIsAwake()
    {
        var r = new Rig(Vale, new LawSettingsDef { Id = "law", ArrestPct = 100 });
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1);
        Assert.Equal(4, Guards.Of(r.P, 0).Count);
        Assert.Equal(2, Guards.OnDuty(r.P, 0, 10).Count);                       // the day men
        Assert.Equal(2, Guards.OnDuty(r.P, 0, 22).Count);                       // the night men
        Assert.Equal(2, Guards.OnDuty(r.P, 0, 3).Count);                        // two night men: every fourth sleeps, one of two is the fourth only with four of them
        Justice.OnHour(r.W, r.Cb, r.Jb, 2 * 24 + 10);                           // day 2, ten o'clock: the guards go out
        Assert.Equal(CrimeState.Held, rec.State); Assert.True(r.Held(thief));
        Assert.Contains(r.Of("crime_arrested"), e => e.Subjects.Contains(thief));
    }

    // ------------------------------------------------------------------ CJ-6

    [Fact]
    public void CJ6_ValeholtTheft_TheStocksThenTwentyLashesThenTheHand_EachThroughTheWoundBook_AndTheChainNamesCourtCrimeAndWitness()
    {
        var r = new Rig(Vale);
        var thief = r.Id(30);
        // first conviction: a day in the stocks
        var c1 = r.Crime(thief, "theft", 1); Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        r.Run(2, 7);
        Assert.Equal(1, r.Cb.ConvictionsOf(thief, "theft"));
        var s1 = r.Jb.Sentences[c1.Id]; Assert.Equal("stocks", s1.Kind); Assert.Equal(SentenceState.Done, s1.State);
        Assert.Single(r.Of("sentence_stocks")); Assert.False(r.Held(thief)); Assert.Equal(CrimeState.Served, c1.State);
        Assert.Empty(r.Wounds.Find(thief.Value)!.Records);               // the stocks leave no wound, only pain and a standing
        // second: twenty lashes = two cuts across the back, grade one, bleeding and able to fester like any wound
        var c2 = r.Crime(thief, "theft", 8, 300, 22); Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(9, 10), false);
        r.Run(9, 14);
        var s2 = r.Jb.Sentences[c2.Id]; Assert.Equal("flogging", s2.Kind); Assert.Equal(20, s2.Lashes); Assert.Equal(1, s2.Step);
        var book = r.Wounds.Find(thief.Value)!;
        Assert.Equal(2, book.Records.Count(w => w.Cat == WoundCat.Skin && w.Kind == WoundKind.Cut));
        Assert.All(book.Records, w => Assert.Equal(r.Of("sentence_flogged").Single().Id.Value, w.Cause));     // the wound's cause is the court's entry
        Assert.True(book.PainX100 > 0);
        // third: the right hand, by tourniquet and amputation with the stump cauterised
        var c3 = r.Crime(thief, "theft", 15, 500, 23); Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(16, 10), false);
        r.Run(16, 21);
        var s3 = r.Jb.Sentences[c3.Id]; Assert.Equal("hand", s3.Kind); Assert.Equal(2, s3.Step);
        book = r.Wounds.Find(thief.Value)!;
        var plan = new CombatTables(Db.Value).V2.Plan("human"); int hand = plan.IndexOf("hand_r"); Assert.True(hand >= 0);
        Assert.NotEqual(0UL, book.Permanent.AmputatedMask & (1UL << hand));
        Assert.Contains(book.Records, w => w.Cat == WoundCat.Severed && w.Is(Aid.Cauterised));
        var mark = r.Jb.MarksOf(thief.Value).Single(m => m.Kind == "hand"); Assert.Equal("hand_r", mark.Part); Assert.Equal(c3.Id, mark.Crime);
        // the cause chain: hand <- sentence <- arrest <- report <- witnessed (Wynn is the witness) <- the theft
        var hands = r.C.Get(new EntityId(mark.Entry))!;
        var chain = new List<string>(); var e = hands;
        while (e != null) { chain.Add(e.TypeId); e = e.Causes.Count > 0 && e.Causes[0].EventId is { } id ? r.C.Get(id) : null; }
        Assert.Equal(["sentence_hand", "sentence_pronounced", "crime_arrested", "crime_reported", "crime_witnessed", "theft"], chain);
        Assert.Contains(r.C.Get(new EntityId(c3.Witnessed))!.Subjects, x => x == r.Id(23));
        output.WriteLine(string.Join(" <- ", chain));
        Assert.True(r.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CJ-7

    [Fact]
    public void CJ7_NorthmarkWeregild_ThePriceGoesToTheVictimsKin_AndAnUnpaidOneIsOutlawryWithDeedsForfeitAndAPermanentBounty()
    {
        // paid: the kin of the victim are paid six thousand
        var r = new Rig(North);
        var killer = r.Id(30); var victim = r.Id(40);
        r.L.Mint(r.Purse(killer)!.Value, new Coin(7_000), SourceReason.Seed);
        var m = r.Crime(killer, "murder", 1, 0, 20, 40);
        Gaol.Arrest(r.W, r.Cb, r.Jb, killer, 0, Rig.Sec(2, 10), false);
        long kin0 = r.L.Balance(r.Purse(victim)!.Value).Mites;
        r.Run(2, 8);
        Assert.Equal("weregild", r.Jb.Sentences[m.Id].Kind); Assert.Equal(SentenceState.Done, r.Jb.Sentences[m.Id].State);
        Assert.Equal(kin0 + 6_000, r.L.Balance(r.Purse(victim)!.Value).Mites);
        Assert.Single(r.Of("restitution_paid")); Assert.False(r.Marks.IsOutlaw(killer, 0));
        // unpaid after fifteen days: outlawry
        var r2 = new Rig(North);
        var k2 = r2.Id(31); var m2 = r2.Crime(k2, "murder", 1, 0, 20, 41);
        Gaol.Arrest(r2.W, r2.Cb, r2.Jb, k2, 0, Rig.Sec(2, 10), false);
        r2.Run(2, 7);
        var s = r2.Jb.Sentences[m2.Id];
        Assert.Equal(SentenceState.Pending, s.State); Assert.True(s.Started); Assert.False(r2.Held(k2));          // he is let go to raise it
        Assert.False(r2.Marks.IsOutlaw(k2, 0));
        r2.Run(8, 25);
        Assert.Equal(SentenceState.Done, s.State); Assert.True(r2.Marks.IsOutlaw(k2, 0));
        Assert.Contains(r2.Forfeits, f => f.Who == k2);
        var sb = new StandingBook(); sb.Marks.Outlaw(k2, 0); Assert.Equal(-1000, Standing.Of(sb, k2, GroupRef.Realm(0)));      // the standing function reads the same marks
        var job = r2.Jobs.Get(new EntityId(m2.BountyJob))!;
        Assert.Equal(JobState.Open, job.State); Assert.NotEqual(0, job.P2 & Bounties.FlagPermanent); Assert.NotEqual(0, job.P2 & Bounties.FlagDeadPaysAll);
        Assert.Equal(CrimeState.Served, m2.State);
        Assert.Contains(r2.Of("outlawed"), e => e.Subjects.Contains(k2));
        Assert.True(r2.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CJ-8

    [Fact]
    public void CJ8_Mercy_StandingOfThreeHundredLowersTheStep_AndBenefitOfClergyTurnsATheftIntoPenance()
    {
        var r = new Rig(Vale);
        var thief = r.Id(30);
        var first = r.Crime(thief, "theft", 1); Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        r.Run(2, 7);                                                              // a first conviction: the stocks
        var c2 = r.Crime(thief, "theft", 8, 300, 22);
        Assert.Equal(1, Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0).Step);   // second conviction: flogging...
        Assert.Equal("flogging", Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0).Kind);
        r.Standing = (p, f) => p == thief ? 300 : 0;                              // ...unless the fief thinks well of him: down a step
        var mercy = Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0);
        Assert.Equal(0, mercy.Step); Assert.Equal(-1, mercy.Mercy); Assert.Equal("stocks", mercy.Kind);
        r.Standing = (_, _) => 0;
        r.Jb.Restituted.Add(c2.Id);                                               // full restitution before sentence does the same
        Assert.Equal(-1, Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0).Mercy);
        r.Jb.Restituted.Clear();
        // benefit of clergy: literate, the church's court: penance whatever the count
        r.Jb.Clergy.Add(c2.Id); r.Literacy = _ => 20;
        Assert.Equal("flogging", Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0).Kind);   // too little letters
        r.Literacy = _ => 30;
        var clergy = Sentences.Expect(r.W, r.Cb, r.Jb, c2, thief, 0);
        Assert.Equal("penance", clergy.Kind); Assert.True(clergy.Clergy);
        // both mercy and an ill-wishing lord: a draw decides at twenty percent each way (stream 0x612, key the crime), so over many crimes it is mostly the middle
        var r3 = new Rig(Vale);
        r3.Standing = (_, _) => 400;
        int down = 0, up = 0, mid = 0;
        for (int i = 0; i < 400; i++)
        {
            var rec = new CrimeRecord { Id = 1000 + i, Kind = "theft", Settlement = 0, ValueMites = 100 };
            var w3 = new LawWorld
            {
                Chronicle = r3.C, Ids = r3.Ids, People = r3.P, SettlementNames = ["Ashby"], SettlementFief = [0], FiefIds = [Fief], Seed = 11, Law = r3.W.Law, CultureKey = Vale, LordOf = _ => r3.Id(1),
                StandingWithFief = (_, _) => 400, OpinionPm = (_, _) => -100,
            };
            int s = Sentences.Mercy(w3, r3.Jb, rec, r3.Id(30), 0, npcLord: true);
            if (s < 0) down++; else if (s > 0) up++; else mid++;
        }
        output.WriteLine($"mercy tie over 400 crimes: down {down}, up {up}, middle {mid}");
        Assert.InRange(down, 50, 110); Assert.InRange(up, 50, 110); Assert.InRange(mid, 200, 300);
    }

    // ------------------------------------------------------------------ CJ-9

    [Fact]
    public void CJ9_TheOnlyWitnessDied_TheAccusedIsAcquitted_AndWitnessesBeyondFifteenKilometresDoNotCome()
    {
        var r = new Rig(Vale);
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1);
        Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        r.P.Status[20] = PersonStatus.Dead;                                       // the only witness is dead
        r.Run(2, 7);
        Assert.Equal(CrimeState.Acquitted, rec.State); Assert.False(r.Held(thief)); Assert.False(r.Jb.Sentences.ContainsKey(rec.Id));
        var acq = r.Of("crime_acquitted").Single(); Assert.Equal(r.Of("crime_arrested").Single().Id.Value, acq.Causes[0].EventId!.Value.Value);
        Assert.Equal(0, r.Cb.ConvictionsOf(thief, "theft"));
        Assert.Equal(JobState.Cancelled, r.Jobs.Get(new EntityId(rec.BountyJob))!.State);     // the bounty goes home
        // a witness who lives 40 km away does not come
        var far = new Rig(Vale);
        far.P.HomeSettlement[20] = 1;
        var far2 = new LawWorld
        {
            Chronicle = far.C, Ids = far.Ids, People = far.P, SettlementNames = ["Ashby", "Faraway"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1), EntityId.Make(IdKind.Settlement, 2)], SettlementFief = [0, 0], FiefIds = [Fief], FiefSeat = [0],
            Seed = 11, Law = far.W.Law, Ledger = far.L, Jobs = far.Jobs, JobEnv = new JobEnv { Ledger = far.L }, TreasuryOf = _ => far.Treasury, Captives = far.Cap, DistanceM = (a, b) => a == b ? 0 : 40_000,
        };
        var rec2 = CrimeSystem.Commit(far2, far.Cb, new ActSpec("theft", far.Id(30), far.Id(21), 0, Rig.Sec(1), 240, 0, "ch_note_crime_greed"), [new WitnessRow(far.Id(20).Value, WitnessRow.KnewIt)], ActorLook.None);
        Assert.Empty(Court.Attending(far2, rec2, 0));
        far.P.HomeSettlement[20] = 0; Assert.Single(Court.Attending(far.W, rec2, 0));
        // being taken in the act, or confessing, is proof without a witness
        var r3 = new Rig(Vale);
        var t3 = r3.Id(31); var c3 = r3.Crime(t3, "theft", 1); r3.P.Status[20] = PersonStatus.Dead;
        var row = Gaol.Hold(r3.W, r3.Jb, t3, 0, 0, Rig.Sec(2), false, false, c3.Reported, c3.Id); c3.State = CrimeState.Held; row.Crimes.Add(c3.Id);
        Assert.False(Court.Proved(r3.W, r3.Jb, c3, row)); r3.Jb.Confessed.Add(c3.Id); Assert.True(Court.Proved(r3.W, r3.Jb, c3, row));
    }

    // ------------------------------------------------------------------ CJ-10

    [Fact]
    public void CJ10_TheGaolersPrice_HonestyTwentyWageOneFifty_NineHundredIsTakenWithFortyPercentAndSixHundredRefused_ARefusedBribeIsACrime()
    {
        var r = new Rig(Vale);
        r.Honesty = _ => 20;
        Assert.Equal(900, Gaol.BribeFloor(r.W, 20)); Assert.Equal(40, Gaol.BribePct(r.W, 20)); Assert.Null(Gaol.BribeFloor(r.W, 60));
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1);
        Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        var gaoler = r.Id(0);
        // 600 is under the floor: refused, and a bribery charge is laid
        Assert.Equal(BribeOutcome.TooLittle, Gaol.Bribe(r.W, r.Cb, r.Jb, thief, 600, Rig.Sec(3, 12)));
        var bribery = r.Cb.Crimes.Values.Single(c => c.Kind == "bribery");
        Assert.Equal(CrimeState.Held, bribery.State); Assert.Contains(bribery.Id, r.Jb.Gaol[thief.Value].Crimes);
        Assert.True(r.Held(thief)); Assert.Single(r.Of("bribe_refused"));
        // 900 on days where the draw (0x613, key the gaoler and the day) says yes or no: P is forty percent
        int taken = 0, tried = 0;
        for (int day = 3; day < 203; day++)
        {
            var rr = new Rig(Vale) { Honesty = _ => 20 };
            var t = rr.Id(30); rr.Crime(t, "theft", 1); Gaol.Arrest(rr.W, rr.Cb, rr.Jb, t, 0, Rig.Sec(2, 10), false);
            bool roll = DetRng.Range(rr.W.Seed, day, rr.Id(0).Value, RngStreams.BribeRoll, 0, 0, 100) < 40;
            var res = Gaol.Bribe(rr.W, rr.Cb, rr.Jb, t, 900, Rig.Sec(day, 12));
            Assert.Equal(roll ? BribeOutcome.Taken : BribeOutcome.Failed, res);
            tried++; if (res == BribeOutcome.Taken) { taken++; Assert.False(rr.Held(t)); Assert.Equal(900, rr.L.Balance(rr.Purse(rr.Id(0))!.Value).Mites - 2_000); Assert.Single(rr.Of("bribe_taken")); Assert.Equal(CrimeState.Fled, rr.Cb.Get(1)!.State); }
        }
        output.WriteLine($"900 mites at honesty 20: taken {taken} of {tried}");
        Assert.InRange(taken, 60, 100);
        // an honest gaoler (60) never does, whatever the offer
        var h = new Rig(Vale) { Honesty = _ => 60 };
        var ht = h.Id(30); h.Crime(ht, "theft", 1); Gaol.Arrest(h.W, h.Cb, h.Jb, ht, 0, Rig.Sec(2, 10), false);
        Assert.Equal(BribeOutcome.Honest, Gaol.Bribe(h.W, h.Cb, h.Jb, ht, 1_500, Rig.Sec(3)));
        Assert.True(r.L.Audit().IsConserved);
        _ = gaoler;
    }

    // ------------------------------------------------------------------ CJ-11

    [Fact]
    public void CJ11_AMurdererHangsAtDawnAfterTheCourtDay_AndABreakBeforeDawnPreventsIt()
    {
        var r = new Rig(Vale);
        var killer = r.Id(30); var m = r.Crime(killer, "murder", 1, 0, 20, 40);
        Gaol.Arrest(r.W, r.Cb, r.Jb, killer, 0, Rig.Sec(2, 10), false);
        r.Run(2, 4);                                                               // the court day is day 4: hour 8
        Assert.Equal("hanging", r.Jb.Sentences[m.Id].Kind); Assert.Equal(SentenceState.Pending, r.Jb.Sentences[m.Id].State); Assert.True(r.Held(killer));
        Assert.Empty(r.Hanged);
        r.Run(5, 5);                                                               // dawn of day 5: hour 6
        Assert.Single(r.Hanged); Assert.Equal(killer, r.Hanged[0].Who); Assert.Equal(5 * 86_400L + 6 * 3600, r.Hanged[0].Sec);
        var entry = r.Of("sentence_hanged").Single(); Assert.Equal(entry.Id.Value, r.Hanged[0].Entry);
        // the same for a man who breaks out the night before (the lock, or a rescue): the sentence is avoided and he is wanted for the escape
        var r2 = new Rig(Vale);
        var k2 = r2.Id(31); var m2 = r2.Crime(k2, "murder", 1, 0, 20, 40);
        Gaol.Arrest(r2.W, r2.Cb, r2.Jb, k2, 0, Rig.Sec(2, 10), false);
        r2.Run(2, 4);
        Assert.True(r2.Cap.Find(k2)!.Held);
        var cap = r2.Cap.Find(k2)!; cap.State = CaptiveState.Escaped; cap.EndedSec = 4 * 86_400L + 23 * 3600; cap.Restraint = Restraint.None;       // out by the lock in the night
        r2.Run(5, 5);
        Assert.Empty(r2.Hanged); Assert.Equal(SentenceState.Avoided, r2.Jb.Sentences[m2.Id].State); Assert.Equal(CrimeState.Fled, m2.State);
        Assert.Contains(r2.Cb.Crimes.Values, c => c.Kind == "jailbreak" && c.Doer == k2.Value);
        Assert.Contains(r2.Cb.Open(k2), c => c.Id == m2.Id);                        // still wanted for the murder
    }

    [Fact]
    public void CJ11_TheHeroHangedByTheCourt_HisBreathStopsInTheWoundBook_AndTheEntryIsKept()
    {
        var r = new Rig(Vale);
        var m = r.Crime(Hero, "murder", 1, 0, 20, 40);
        Gaol.Arrest(r.W, r.Cb, r.Jb, Hero, 0, Rig.Sec(2, 10), false);
        r.Run(2, 5);
        var book = r.Wounds.Find(EconomyState.HeroOwner.Value)!;
        Assert.True(book.Dead); Assert.Equal(DeathKind.Breath, book.Death);
        Assert.NotEqual(0UL, r.Jb.HeroExecuted); Assert.Equal(r.Of("sentence_hanged").Single().Id.Value, r.Jb.HeroExecuted);
        Assert.Empty(r.Hanged);                                                    // no NPC kill: the game's succession takes over from the book
        Assert.Equal(CaptiveState.Dead, r.Cap.Find(Hero)!.State);
    }

    // ------------------------------------------------------------------ CJ-12 and CJ-14

    [Fact]
    public void CJ12_ALordsOwnLaw_FineForTheftReplacesTheStocksThenHand_AndTheAllowedListIsTheCulturesOwnOrMilder()
    {
        var r = new Rig(Vale);
        var allowed = Sentences.Allowed(r.W, "stocks_then_hand");
        Assert.Contains("fine", allowed); Assert.Contains("stocks_then_hand", allowed); Assert.DoesNotContain("hanging", allowed);
        r.Jb.Laws[(0, "theft")] = "fine";
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1, 240);
        Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        var e = Sentences.Expect(r.W, r.Cb, r.Jb, rec, thief, 0);
        Assert.Equal("fine", e.Key); Assert.True(e.LordsLaw); Assert.Equal(580, e.Mites);       // 2 x 240 + 100
        long hh0 = r.L.Balance(r.Purse(thief)!.Value).Mites, tr0 = r.L.Balance(r.Treasury).Mites;
        r.Run(2, 7);
        Assert.Equal(SentenceState.Done, r.Jb.Sentences[rec.Id].State);
        Assert.Equal(hh0 - 580, r.L.Balance(r.Purse(thief)!.Value).Mites);
        Assert.True(r.L.Balance(r.Treasury).Mites >= tr0 + 580 - 3 * 3);        // the fine went to the treasury (less the bread)
        Assert.Single(r.Of("fine_paid")); Assert.Empty(r.Of("sentence_hand"));
        Assert.Empty(r.Grievances);                                                // milder than the culture's table: no new grievance
        // a fine he cannot pay is gaol for the debt (one day for each hundred, at most thirty)
        var broke = r.Id(32); var rb = r.Crime(broke, "theft", 8, 240, 22); r.L.Sink(r.Purse(broke)!.Value, r.L.Balance(r.Purse(broke)!.Value), SinkReason.Burn);
        Gaol.Arrest(r.W, r.Cb, r.Jb, broke, 0, Rig.Sec(9, 10), false);
        r.Run(9, 14);
        var s = r.Jb.Sentences[rb.Id]; Assert.Equal("gaol", s.Kind); Assert.Equal(5, s.Days); Assert.True(r.Held(broke));
        r.Run(15, 21);
        Assert.False(r.Held(broke)); Assert.Equal(CrimeState.Served, rb.State);
        Assert.Contains(r.Of("sentence_served"), x => x.Subjects.Contains(broke));
    }

    [Fact]
    public void CJ14_TheHeroAsJudge_TheDocketHoldsTheCasesWithTheLawAndItsAlternatives_TheChosenSentenceIsCarriedOut_AndHarsherThanTheCultureIsAGrievance()
    {
        var r = new Rig(Vale) { HeroLord = true };
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1, 240);
        var thief2 = r.Id(31); var rec2 = r.Crime(thief2, "theft", 1, 100, 22);
        Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false); Gaol.Arrest(r.W, r.Cb, r.Jb, thief2, 0, Rig.Sec(2, 10), false);
        r.Run(2, 4);                                                               // the court day: the hero's cases wait on his docket
        Assert.Equal([rec.Id, rec2.Id], r.Jb.Docket.ToArray()); Assert.Empty(r.Jb.Sentences);
        var alts = Sentences.Alternatives(r.W, r.Cb, r.Jb, rec, thief);
        Assert.Contains(alts, a => a.Key == "stocks_then_hand" && a.Kind == "stocks" && !a.LordsLaw);      // the culture's: the first step
        Assert.Contains(alts, a => a.Key == "fine" && a.Mites == 580);
        Assert.Contains(alts, a => a.Key == "hanging" && a.LordsLaw);                                       // a harsher one is there, and flagged
        // he fines the first, and hangs the second for a theft of a hundred mites (harsher than Valeholt's table)
        Assert.True(Court.Judge(r.W, r.Cb, r.Jb, rec.Id, "fine", Rig.Sec(4, 12)));
        Assert.True(Court.Judge(r.W, r.Cb, r.Jb, rec2.Id, "hanging", Rig.Sec(4, 12)));
        Assert.Equal("fine", r.Jb.Sentences[rec.Id].Kind); Assert.True(r.Jb.Sentences[rec.Id].ByHero);
        Assert.Equal("hanging", r.Jb.Sentences[rec2.Id].Kind);
        Assert.Equal([(thief2, 1)], r.Grievances);                                // a harsher sentence: the convicted's kin
        r.Run(5, 5);
        Assert.Single(r.Of("fine_paid")); Assert.Single(r.Hanged); Assert.Equal(thief2, r.Hanged[0].Who);
        Assert.Empty(r.Jb.Docket);
        // what he leaves undecided at the next sitting is the steward's
        var t3 = r.Id(32); var rec3 = r.Crime(t3, "theft", 6, 240, 23); Gaol.Arrest(r.W, r.Cb, r.Jb, t3, 0, Rig.Sec(7, 10), false);
        r.Run(7, 9);
        Assert.Equal([rec3.Id], r.Jb.Docket.ToArray());
        var t4 = r.Id(33); var rec4 = r.Crime(t4, "theft", 10, 240, 24); Gaol.Arrest(r.W, r.Cb, r.Jb, t4, 0, Rig.Sec(11, 10), false);
        r.Run(10, 14);                                                             // day 14 is the next court day: rec3 goes to the steward, rec4 on to the docket
        Assert.True(r.Jb.Sentences.ContainsKey(rec3.Id) && !r.Jb.Sentences[rec3.Id].ByHero);
        Assert.Equal([rec4.Id], r.Jb.Docket.ToArray());
        Assert.Equal(1, Court.Adjourn(r.W, r.Cb, r.Jb, Rig.Sec(14, 20)));
        Assert.Empty(r.Jb.Docket); Assert.True(r.Jb.Sentences.ContainsKey(rec4.Id));
        // acquitting the plainly guilty angers the victim's household
        var r5 = new Rig(Vale) { HeroLord = true };
        var t5 = r5.Id(30); var rec5 = r5.Crime(t5, "theft", 1); Gaol.Arrest(r5.W, r5.Cb, r5.Jb, t5, 0, Rig.Sec(2, 10), false); r5.Run(2, 4);
        Assert.True(Court.Acquit(r5.W, r5.Cb, r5.Jb, rec5, true, Rig.Sec(4, 12)));
        Assert.Contains(r5.Grievances, g => g.Delta == -1);
    }

    // ------------------------------------------------------------------ CJ-16

    [Fact]
    public void CJ16_ABandsCaptivesAreFedFromItsStash_AndWithAnEmptyStashTheyAreLetGoAfterTwoDays()
    {
        var r = new Rig(Vale);
        var band = AftermathCoin.BandId(7); var bandPurse = r.L.Open(PurseKind.Household, band); r.L.Mint(bandPurse, new Coin(100), SourceReason.Seed);
        var captive = r.Id(50);
        r.Cap.Capture(AftermathRules.Default, CaptorKind.Bandits, captive, false, band, new PurseRef(PurseKind.Household, band), 10L * 86_400, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (0, 0, 0, 50, 0));
        r.Cap.Find(captive)!.NextTransferDay = 100_000;
        Gaol.Feed(r.W, r.Cb, r.Jb, 11, 11 * 86_400L); Gaol.Feed(r.W, r.Cb, r.Jb, 12, 12 * 86_400L);
        Assert.Equal(100 - 2 * r.W.S.RationMites, r.L.Balance(bandPurse).Mites); Assert.True(r.Held(captive));
        r.L.Sink(bandPurse, r.L.Balance(bandPurse), SinkReason.Burn);              // the stash is empty
        Gaol.Feed(r.W, r.Cb, r.Jb, 13, 13 * 86_400L); Assert.True(r.Held(captive));
        Gaol.Feed(r.W, r.Cb, r.Jb, 14, 14 * 86_400L); Assert.False(r.Held(captive));
        Assert.Equal(CaptiveState.Released, r.Cap.Find(captive)!.State); Assert.Equal("unfed", r.Cap.Find(captive)!.Note);
        Assert.True(r.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ the save

    [Fact]
    public void TheCourtsRecords_RoundTripThroughTheJusticeSection_AndAWorldWithNoneWritesNoSection()
    {
        var r = new Rig(Vale) { HeroLord = false };
        Assert.True(r.Jb.IsEmpty);
        r.Jb.Laws[(0, "theft")] = "fine";
        var thief = r.Id(30); var rec = r.Crime(thief, "theft", 1); Gaol.Arrest(r.W, r.Cb, r.Jb, thief, 0, Rig.Sec(2, 10), false);
        var m = r.Id(31); var mr = r.Crime(m, "murder", 1, 0, 22, 41); Gaol.Arrest(r.W, r.Cb, r.Jb, m, 0, Rig.Sec(2, 10), false);
        r.Jb.Clergy.Add(mr.Id); r.Jb.BandUnfed[99] = 1;
        r.Run(2, 4);
        r.Jb.Marks.Add(new BodyMark { Person = thief.Value, Kind = "hand", Part = "hand_r", Day = 5, Crime = rec.Id, Entry = 77 });
        r.Jb.Feuds.Add(new FeudRow { Culprit = 5, Kin = 6, Until = 90, Crime = 3 });
        var copy = JusticeSave.Capture(r.Jb).Restore();
        Assert.Equal(r.Jb.Hash(), copy.Hash());
        Assert.Equal(r.Jb.Sentences.Count, copy.Sentences.Count); Assert.Equal(r.Jb.Gaol.Count, copy.Gaol.Count); Assert.Equal("fine", copy.Laws[(0, "theft")]);
        var bytes = SaveCodec.SerializeCompressed(JusticeSave.Capture(copy));
        Assert.True(bytes.AsSpan().SequenceEqual(SaveCodec.SerializeCompressed(JusticeSave.Capture(r.Jb))));
        Assert.True(new JusticeBook().IsEmpty);
    }

    [Fact]
    public void EverySentenceStepOfTheContentIsAKnownKind_AndEveryCultureKeyHasATable()
    {
        var law = LawContent.From(Db.Value);
        var kinds = new HashSet<string> { "fine", "stocks", "flogging", "branding", "hand", "hanging", "weregild", "outlawry", "restitution", "gaol", "bond_labour", "penance", "exile", "blood_price", "confiscation", "kin_pays" };
        foreach (var p in law.Punishments) foreach (var s in p.Steps) Assert.True(kinds.Contains(s.Kind), $"{p.Id}: {s.Kind}");
        foreach (var cu in Db.Value.All<CultureDef>()) foreach (var (crime, key) in cu.Crimes) Assert.NotNull(law.Punishment(key));
        Assert.Equal(LawContent.DefaultPunishments().Select(d => d.Id).Order(), law.Punishments.Select(d => d.Id).Order());
    }
}
