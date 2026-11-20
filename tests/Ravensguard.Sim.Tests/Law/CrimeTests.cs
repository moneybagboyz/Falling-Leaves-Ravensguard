using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Tests.Dialogue;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Law;

/// <summary>
/// The crime core (docs/specs/phase6/crime-and-justice.md, P6-B2): CJ-1 (a seen theft: five witnesses, the cry, the report, a 580-mite bounty escrowed, the cause chain, standing), CJ-2 (night, sneaking: unseen),
/// CJ-3 (described, then matched by clothes), CJ-4 (the hue and cry's reach), CJ-13 (a season of NPC crime in a town of 1,500) and CJ-15 (a seen grave robbing), plus the bounty's ways out and the save.
/// </summary>
public class CrimeTests(ITestOutputHelper output)
{
    private sealed class Scene(DayPhase phase, bool rain = false, Func<Cell3, bool>? lit = null, Func<Cell3, Cell3, bool>? los = null, bool indoor = false) : IWitnessScene
    {
        public DayPhase Phase => phase;
        public bool HeavyRain => rain;
        public bool Los(Cell3 a, Cell3 b) => los?.Invoke(a, b) ?? true;
        public bool Lit(Cell3 c) => lit?.Invoke(c) ?? false;
        public bool Indoor(Cell3 c) => indoor;
    }

    private static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Hero = EntityId.None;
    private static readonly Cell3 Spot = new(200, 200, 0);

    private sealed class Rig
    {
        public TalkWorld W = null!; public Chronicle C = null!; public LawWorld Law = null!; public CrimeBook Book = new(); public Ledger Ledger = new(); public JobTable Jobs = new(); public PurseId Treasury;
        public IdAllocator Ids = new();
        public PersonTable P => W.People;

        public Rig(int people = 1400, long treasury = 100_000, HouseholdTable? hh = null, Func<int, bool>? desperate = null, ulong seed = 7)
        {
            (W, C) = TalkKit.Town(people, seed);
            Treasury = Ledger.Open(PurseKind.Treasury, Fief);
            if (treasury > 0) Ledger.Mint(Treasury, new Coin(treasury), SourceReason.Seed);
            Law = new LawWorld
            {
                Chronicle = C, Ids = Ids, People = W.People, Households = hh, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [Fief], Seed = seed,
                Ledger = Ledger, Jobs = Jobs, JobEnv = new JobEnv { Ledger = Ledger }, TreasuryOf = _ => Treasury, Desperate = desperate ?? (_ => false),
            };
        }

        public EntityId Id(int row) => P.IdOf(row);
        public Observer At(int row, int dx, int dy = 0, bool awake = true, int fam = 0, int trust = 0, bool kin = false, bool guard = false, int melee = 0) =>
            new() { Person = Id(row), At = new Cell3(Spot.X + dx, Spot.Y + dy, 0), Awake = awake, Familiarity = fam, TrustPm = trust, Kin = kin, Guard = guard, Melee = melee };
    }

    private static WitnessActor Actor(bool sneak = false, int stealth = 0, int renown = 0, ActorLook? look = null, bool hooded = false) => new(Hero, Spot, sneak, stealth, hooded, false, renown, look ?? ActorLook.None);

    private static LineBook Gossip() => new()
    {
        Lines = LineBook.Default.Lines, Barks = LineBook.Default.Barks, Dialogues = LineBook.Default.Dialogues, Settings = LineBook.Default.Settings,
        RumourTypes = [.. LineBook.Default.RumourTypes, new RumourTypeDef { Id = "theft", Entry = "theft", Kind = "Crime", Key = "R_RIOT", InterestPm = 800, LifeDays = 10, Witnesses = "seen", Severity = 2, WhoKey = "doer" }],
    };

    private sealed class Map(PersonTable pt) : IGroupMap
    {
        public GroupRef FiefOf(int s) => GroupRef.Fief(0);
        public GroupRef RealmOf(int s) => GroupRef.Realm(0);
        public IReadOnlyList<int> SettlementsOf(GroupRef g) => g.Kind == GroupKind.Settlement ? [g.Index] : [0];
        public int Members(GroupRef g) { int n = 0; for (int i = 0; i < pt.Count; i++) if (pt.IsAlive(i)) n++; return n; }
        public int RegionAdults => Members(GroupRef.Realm(0));
        public int HomeOf(EntityId person) { int i = pt.IndexOf(person); return i >= 0 ? pt.HomeSettlement[i] : -1; }
    }

    private static readonly DeedTable Deeds = new([new DeedDef { Id = "theft_witnessed", Entry = "theft", SettlementPm = -60, FiefPm = -30 }]);

    // ------------------------------------------------------------------ CJ-1

    [Fact]
    public void CJ1_ATheftInFrontOfFiveWitnesses_IsSeenBefore_RaisesTheCry_IsReported_AndPostsA580MiteBountyWhoseCauseChainResolves()
    {
        var r = new Rig();
        // day 3, 10:00, daylight; Wynn (row 4) at 2 m, the others at 8 to 25 m; a guard far off hears nothing of it yet
        var obs = new List<Observer> { r.At(4, 2), r.At(5, 8), r.At(6, 12), r.At(7, 20, 5), r.At(8, 0, 25), r.At(9, 0, 60) };
        var scene = new Scene(DayPhase.Day); var actor = Actor();
        Assert.Equal(5, Witnessing.SeenCount(r.Law.S, scene, actor, obs));                 // the HUD said Seen (5) before the act
        long sec = 3 * 86_400L + 10 * 3600;
        long treasuryBefore = r.Ledger.Balance(r.Treasury).Mites;
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, sec, 240, 0, "ch_note_crime_hero"), scene, actor, obs);
        var rec = res.Record!;
        Assert.Equal(5, rec.Witnesses.Count);
        Assert.True(rec.Witnesses.First(w => w.Person == r.Id(4).Value).Recognised);        // Wynn, 2 m, light
        Assert.True(rec.Known); Assert.True(res.Cry != null); Assert.True(rec.Cried);
        Assert.Equal(CrimeState.Seen, rec.State);
        // standing by RP-1's rule: only those who knew him hear of it; in a town of 1,400 two hearers are nothing yet
        var ts = new TalkState(); ts.Rumours.EnsureCounts(p => r.W.People.HomeSettlement[p]); RumourSeeder.Seed(r.W, ts, Gossip(), 3 * 24 + 10);
        var stand = new StandingBook?[] { null }; StandingSystem.OnHour(() => stand[0] ??= new StandingBook(), stand[0], Deeds, new Map(r.P), r.W, ts, 3 * 24 + 11);
        Assert.Equal(0, Standing.Of(stand[0], Hero, GroupRef.Settlement(0)));
        Assert.Equal(2, stand[0]!.Deeds.Values.Single().HeardTotal);                         // Wynn and the one at 8 m
        // not reported on the day; at the next day's first hour a witness reports and the lord posts the bounty
        Assert.Empty(CrimeSystem.Daily(r.Law, r.Book, 3));
        Assert.Single(CrimeSystem.Daily(r.Law, r.Book, 4));
        Assert.Equal(CrimeState.Reported, rec.State);
        Assert.Equal(580, r.Jobs.Get(new EntityId(rec.BountyJob))!.WageMites);
        Assert.Equal(treasuryBefore - 580, r.Ledger.Balance(r.Treasury).Mites);
        Assert.Equal(580, r.Ledger.Balance(new PurseId(r.Jobs.Get(new EntityId(rec.BountyJob))!.Escrow)).Mites);        // escrowed, not spent
        // the chain: bounty_posted <- crime_reported <- crime_witnessed (Wynn is the lead) <- theft, each an earlier entry
        var posted = r.C.Get(new EntityId(rec.BountyEntry))!; var reported = r.C.Get(posted.Causes[0].EventId!.Value)!; var witnessed = r.C.Get(reported.Causes[0].EventId!.Value)!; var act = r.C.Get(witnessed.Causes[0].EventId!.Value)!;
        Assert.Equal(["bounty_posted", "crime_reported", "crime_witnessed", "theft"], new[] { posted.TypeId, reported.TypeId, witnessed.TypeId, act.TypeId });
        Assert.Equal(r.Id(4), witnessed.Subjects[0]);                                         // the witness is named
        Assert.Contains(posted.Params!, p => p.Name == "witness" && p.Value == (long)reported.Subjects[0].Value);
        Assert.True(act.When.Seconds <= witnessed.When.Seconds && witnessed.When.Seconds <= reported.When.Seconds && reported.When.Seconds <= posted.When.Seconds);
        Assert.Equal(580, r.Jobs.Jobs.Values.Single().WageMites);
        // the coin is all still there (the audit holds) and the job is not work anybody can take
        Assert.True(r.Ledger.Audit().IsConserved);
        Assert.Empty(r.Jobs.Open(0, 0));
    }

    [Fact]
    public void CJ1_TheBountyIsCappedAtAQuarterOfTheTreasury_AndPaysAllAliveOrHalfDead_TheRestGoingBack()
    {
        var r = new Rig(treasury: 1_000);                                                     // a quarter is 250
        var obs = new List<Observer> { r.At(4, 2) };
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, 100, 240, 0, "ch_note_crime_hero"), new Scene(DayPhase.Day), Actor(), obs);
        CrimeSystem.Report(r.Law, res.Record!, r.Id(4), 86_400);
        var job = r.Jobs.Get(new EntityId(res.Record!.BountyJob))!;
        Assert.Equal(250, job.WageMites);
        var hunter = r.Ledger.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 777));
        Assert.Equal(125, Bounties.Collect(r.Law, r.Book, job.Id, EntityId.Make(IdKind.Person, 777), hunter, alive: false, 2 * 86_400));        // dead: half
        Assert.Equal(125, r.Ledger.Balance(hunter).Mites); Assert.Equal(875, r.Ledger.Balance(r.Treasury).Mites);                                 // the other half went home
        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("bounty_collected", r.C.All[^1].TypeId);
        Assert.Equal(res.Record.BountyEntry, r.C.All[^1].Causes[0].EventId!.Value.Value);
        Assert.True(r.Ledger.Audit().IsConserved);
        // a murderer's bounty pays all dead or alive; an unclaimed one expires and is refunded
        var m = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("murder", Hero, r.Id(4), 0, 200, 0, 0, "ch_note_crime_hero"), new Scene(DayPhase.Day), Actor(), obs).Record!;
        CrimeSystem.Report(r.Law, m, r.Id(4), 3 * 86_400);
        var mj = r.Jobs.Get(new EntityId(m.BountyJob))!;
        Assert.True(mj.DeadlineSec > 1_000_000_000);                                          // a murder's price never expires
        Assert.Equal(mj.WageMites, Bounties.Collect(r.Law, r.Book, mj.Id, EntityId.Make(IdKind.Person, 777), hunter, alive: false, 4 * 86_400));
        var t = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, 5 * 86_400, 10, 0, "ch_note_crime_hero"), new Scene(DayPhase.Day), Actor(), obs).Record!;
        CrimeSystem.Report(r.Law, t, r.Id(4), 6 * 86_400);
        long left = r.Ledger.Balance(r.Treasury).Mites; var tj = r.Jobs.Get(new EntityId(t.BountyJob))!;
        Assert.Equal(left, r.Ledger.Balance(r.Treasury).Mites);
        CrimeSystem.Daily(r.Law, r.Book, 6 + 61);                                              // sixty days on
        Assert.Equal(JobState.Expired, tj.State); Assert.Equal(left + tj.WageMites, r.Ledger.Balance(r.Treasury).Mites);
        Assert.True(r.Ledger.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ CJ-2

    [Fact]
    public void CJ2_AtNightWithNoLightSneakingAtStealth60_TheRangeIs4Point2Metres_NineMetresIsUnseen_NoRecordNoRumour()
    {
        var r = new Rig();
        var night = new Scene(DayPhase.Night); var sneak = Actor(sneak: true, stealth: 60);
        Assert.Equal(420, Witnessing.SightCm(r.Law.S, night, sneak));
        var obs = new List<Observer> { r.At(4, 9) };
        Assert.Equal(0, Witnessing.SeenCount(r.Law.S, night, sneak, obs));
        int before = r.C.Count;
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, 23 * 3600, 240, 0, "ch_note_crime_hero"), night, sneak, obs);
        Assert.Null(res.Record); Assert.Equal(before, r.C.Count); Assert.True(r.Book.IsEmpty);
        var ts = new TalkState(); Assert.Equal(0, RumourSeeder.Seed(r.W, ts, Gossip(), 24));
        // the edges: 4 m is seen, 5 m is not; carrying a torch adds 6 m; rain takes 30% off; a roof makes it 4 m dark, 12 m lit
        Assert.Equal(1, Witnessing.SeenCount(r.Law.S, night, sneak, [r.At(4, 4)])); Assert.Equal(0, Witnessing.SeenCount(r.Law.S, night, sneak, [r.At(4, 5)]));
        Assert.Equal(1200, Witnessing.SightCm(r.Law.S, new Scene(DayPhase.Night, lit: _ => true), Actor()));
        Assert.Equal(2100, Witnessing.SightCm(r.Law.S, new Scene(DayPhase.Day, rain: true), Actor()));
        Assert.Equal(400, Witnessing.SightCm(r.Law.S, new Scene(DayPhase.Day, indoor: true), Actor()));
        Assert.Equal(1200, Witnessing.SightCm(r.Law.S, new Scene(DayPhase.Day, lit: _ => true, indoor: true), Actor()));
        Assert.Equal(1500, Witnessing.SightCm(r.Law.S, new Scene(DayPhase.Dusk), Actor()));
        // asleep, a wall between, or far: nobody sees
        var day = new Scene(DayPhase.Day, los: (a, b) => false);
        Assert.Equal(0, Witnessing.SeenCount(r.Law.S, day, Actor(), [r.At(4, 2)]));
        Assert.Equal(0, Witnessing.SeenCount(r.Law.S, new Scene(DayPhase.Day), Actor(), [r.At(4, 2, awake: false)]));
    }

    // ------------------------------------------------------------------ CJ-3

    [Fact]
    public void CJ3_UnrecognisedInLightAt15m_IsDescribed_AGuardMatchesTheOldCloakAbout70Percent_NeverTheNewOne()
    {
        var r = new Rig();
        var oldLook = new ActorLook([0xA03030u], 2); var newLook = new ActorLook([0x3050A0u], 2);
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, 36_000, 240, 0, "ch_note_crime_hero"), new Scene(DayPhase.Day), Actor(look: oldLook), [r.At(4, 15)]);
        var rec = res.Record!;
        Assert.False(rec.Witnesses.Single().Recognised); Assert.False(rec.Known); Assert.Equal(-1, rec.Perpetrator);
        Assert.Equal(oldLook, rec.Look);
        int old = 0, fresh = 0;
        for (int g = 0; g < 400; g++)
        {
            var guard = EntityId.Make(IdKind.Person, 5000 + (ulong)g);
            if (CrimeSystem.GuardMatches(r.Law, rec, guard, Hero, 5, oldLook)) old++;
            if (CrimeSystem.GuardMatches(r.Law, rec, guard, Hero, 5, newLook)) fresh++;
        }
        output.WriteLine($"old cloak matched {old}/400, the new one {fresh}/400");
        Assert.InRange(old, 250, 310); Assert.Equal(0, fresh);
        Assert.Equal(CrimeSystem.GuardMatches(r.Law, rec, EntityId.Make(IdKind.Person, 5001), Hero, 5, oldLook), CrimeSystem.GuardMatches(r.Law, rec, EntityId.Make(IdKind.Person, 5001), Hero, 5, oldLook));   // no tick: the same answer every time
        // a hood hides the face: recognition needs 5 m
        Assert.False(Witnessing.Evaluate(r.Law.S, new Scene(DayPhase.Day), Actor(hooded: true), [r.At(4, 8)]).Single().Recognises);
        Assert.True(Witnessing.Evaluate(r.Law.S, new Scene(DayPhase.Day), Actor(hooded: true), [r.At(4, 4)]).Single().Recognises);
        Assert.True(Witnessing.Evaluate(r.Law.S, new Scene(DayPhase.Day), Actor(), [r.At(4, 15, fam: 1)]).Single().Recognises);          // someone who knows him
        // a described thief still earns a bounty, on the description
        CrimeSystem.Report(r.Law, rec, r.Id(4), 86_400);
        Assert.NotEqual(0UL, rec.BountyJob);
    }

    // ------------------------------------------------------------------ CJ-4

    [Fact]
    public void CJ4_TheHueAndCry_GuardsWithin80mPursue_At90mTheyDoNot_AKinWitnessRaisesNothing()
    {
        var r = new Rig(); var scene = new Scene(DayPhase.Day); var actor = Actor();
        var witness = r.At(4, 3);
        var everyone = new List<Observer>
        {
            witness, r.At(10, 25, guard: true), r.At(11, 3, 80, guard: true), r.At(12, 0, 90, guard: true), r.At(13, 40, 0, melee: 50), r.At(14, 10, 0, melee: 50), r.At(15, 12, 0, melee: 40), r.At(16, -6, 0, melee: 60),
            r.At(17, 2, 2, melee: 35), r.At(18, 14, 0, melee: 20), r.At(19, 60, guard: true, awake: false),
        };
        var sights = Witnessing.Evaluate(r.Law.S, scene, actor, everyone);
        var cry = HueAndCry.Raise(r.Law.S, scene, actor, sights, everyone)!;
        Assert.Equal(r.Id(4), cry.Raiser.Person);
        var pursuers = cry.Pursuers.Select(o => o.Person).ToList();
        Assert.Contains(r.Id(10), pursuers); Assert.Contains(r.Id(11), pursuers);          // 25 m and 80 m
        Assert.DoesNotContain(r.Id(12), pursuers);                                          // 90 m
        Assert.DoesNotContain(r.Id(19), pursuers);                                          // asleep
        Assert.DoesNotContain(r.Id(13), cry.Hearers.Select(o => o.Person));                 // 40 m is out of earshot
        Assert.Equal(3, cry.Joiners.Count);                                                 // at most three townsfolk with melee 30 or more, nearest first
        Assert.DoesNotContain(r.Id(18), cry.Joiners.Select(o => o.Person));                 // melee 20
        // a wall halves the reach: 20 m behind a wall is not heard
        var walled = new Scene(DayPhase.Day, los: (a, b) => Math.Abs(a.X - b.X) < 10 && Math.Abs(a.Y - b.Y) < 10);
        var cry2 = HueAndCry.Raise(r.Law.S, walled, actor, Witnessing.Evaluate(r.Law.S, walled, actor, [witness]), [witness, r.At(20, 20, 0), r.At(21, 14, 0)])!;
        Assert.DoesNotContain(r.Id(20), cry2.Hearers.Select(o => o.Person)); Assert.Contains(r.Id(21), cry2.Hearers.Select(o => o.Person));
        // kin or a friend raises nothing; one who fears a renowned actor raises nothing alone but does among three
        var kin = r.At(4, 3, kin: true);
        Assert.Null(HueAndCry.Raise(r.Law.S, scene, actor, Witnessing.Evaluate(r.Law.S, scene, actor, [kin]), [kin, r.At(10, 25, guard: true)]));
        var friend = r.At(4, 3, trust: 600);
        Assert.Null(HueAndCry.Raise(r.Law.S, scene, actor, Witnessing.Evaluate(r.Law.S, scene, actor, [friend]), [friend]));
        var feared = Actor(renown: 700);
        Assert.Null(HueAndCry.Raise(r.Law.S, scene, feared, Witnessing.Evaluate(r.Law.S, scene, feared, [witness]), [witness]));
        var three = new List<Observer> { r.At(4, 3), r.At(5, 4), r.At(6, 5) };
        Assert.NotNull(HueAndCry.Raise(r.Law.S, scene, feared, Witnessing.Evaluate(r.Law.S, scene, feared, three), three));
        // a kin witness's act makes a record but the cry is not raised; the committed act writes hue_and_cry otherwise
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(9), 0, 1000, 100, 0, "ch_note_crime_hero"), scene, actor, [kin]);
        Assert.False(res.Record!.Cried); Assert.DoesNotContain(r.C.All, e => e.TypeId == "hue_and_cry");
        var res2 = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(9), 0, 2000, 100, 0, "ch_note_crime_hero"), scene, actor, everyone);
        Assert.True(res2.Record!.Cried); Assert.Contains(r.C.All, e => e.TypeId == "hue_and_cry");
    }

    // ------------------------------------------------------------------ CJ-13

    private static (Rig R, int Thefts, int Assaults, int Reported) Season(ulong seed)
    {
        var pt = new PersonTable(1500, 1000); var hh = new HouseholdTable(400, 5000);
        for (int h = 0; h < 375; h++)
        {
            hh.Add(0, h, DwellingType.Cottage, 5);
            for (int k = 0; k < 4; k++)
            {
                int i = pt.Add();
                pt.Given[i] = "P" + i; pt.Family[i] = "F" + h; pt.Byname[i] = ""; pt.BirthDay[i] = -30 * TimeConst.DaysPerYear; pt.Household[i] = h; pt.HomeSettlement[i] = 0; pt.WorkSettlement[i] = 0;
                pt.Status[i] = PersonStatus.Alive; pt.Occupation[i] = Occupation.Farmer; pt.Father[i] = pt.Mother[i] = pt.Spouse[i] = -1; pt.DeathDay[i] = -1;
                hh.AddMember(h, i);
            }
        }
        var chron = new Chronicle(); var ledger = new Ledger(); var treasury = ledger.Open(PurseKind.Treasury, Fief); ledger.Mint(treasury, new Coin(2_000_000), SourceReason.Seed);
        var jobs = new JobTable();
        var law = new LawWorld
        {
            Chronicle = chron, Ids = new IdAllocator(), People = pt, Households = hh, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [Fief], Seed = seed,
            Ledger = ledger, Jobs = jobs, JobEnv = new JobEnv { Ledger = ledger }, TreasuryOf = _ => treasury, Desperate = h => h % 3 == 0,           // a third of the town is hungry or in arrears
        };
        var rig = new Rig(1, 0) { Law = law, C = chron, Ledger = ledger, Jobs = jobs, Treasury = treasury };
        var book = rig.Book;
        for (int day = 0; day < 15; day++) { NpcCrime.Daily(law, book, day); CrimeSystem.Daily(law, book, day + 1); }
        return (rig, book.Crimes.Values.Count(c => c.Kind == "theft"), book.Crimes.Values.Count(c => c.Kind == "assault"), book.Crimes.Values.Count(c => c.State >= CrimeState.Reported));
    }

    [Fact]
    public void CJ13_ASeasonOfNpcCrimeInATownOf1500_About25Thefts_FewAssaults_AboutSixtyPercentReported_EveryReportWithASpecificCause()
    {
        var (r, thefts, assaults, reported) = Season(1234);
        int total = thefts + assaults; double share = total == 0 ? 0 : 100.0 * reported / total;
        output.WriteLine($"{thefts} thefts, {assaults} assaults, {reported} reported ({share:0}%)");
        Assert.InRange(thefts, 20, 30); Assert.InRange(assaults, 2, 5); Assert.InRange(share, 55, 65);
        var types = ChronicleTypes.From(EconFixture.Content);
        var crimeTypes = new HashSet<string> { "theft", "assault", "crime_witnessed", "crime_discovered", "crime_reported", "bounty_posted", "hue_and_cry" };
        var cov = r.C.SpecificCoverage(types, e => crimeTypes.Contains(e.TypeId));
        output.WriteLine($"{cov.Entries} crime entries, {cov.Specific} with a specific cause");
        Assert.Equal(cov.Entries, cov.Specific);
        foreach (var e in r.C.All.Where(e => e.TypeId == "crime_reported")) Assert.NotNull(r.C.Get(e.Causes[0].EventId!.Value));
        Assert.True(r.Ledger.Audit().IsConserved);
        // the same season again is the same season
        var (r2, t2, a2, rep2) = Season(1234);
        Assert.Equal((thefts, assaults, reported), (t2, a2, rep2)); Assert.Equal(r.Book.Hash(), r2.Book.Hash()); Assert.Equal(r.C.Hash(), r2.C.Hash());
    }

    // ------------------------------------------------------------------ CJ-15

    [Fact]
    public void CJ15_AGraveRobbedWhileSeen_IsAGraveRobbingCrime_ATabooWithTheReligionsShare_AndTheBarrowRumourIsKept()
    {
        var r = new Rig(); var db = EconFixture.Content;
        var def = LawContent.From(db).Get("grave_robbing");
        Assert.True(def.Taboo); Assert.True(def.Cry);
        var row = DeedTable.From(db).Get("grave_robbing")!;
        Assert.Equal(-100, DeedTable.DeltaFor(row, GroupKind.Religion)); Assert.True(DeedTable.DeltaFor(row, GroupKind.Settlement) < 0);
        // a pilgrim at the barrow mouth saw a grave good taken
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("grave_robbing", Hero, EntityId.None, 0, 40_000, 800, 0, "ch_note_crime_taboo"), new Scene(DayPhase.Dusk), Actor(), [r.At(4, 9)]);
        Assert.Equal("grave_robbing", res.Record!.Kind); Assert.True(res.Record.Known);
        Assert.Equal("grave_robbing", r.C.Get(new EntityId(res.Record.Entry))!.TypeId);
        // the Phase 4 barrow rumour is still there: the content row for taboo_robbed stands, and the two chronicle entries each seed a rumour
        Assert.Contains(db.All<RumourTypeDef>(), t => t.Id == "taboo_robbed");
        Assert.Contains(db.All<RumourTypeDef>(), t => t.Id == "grave_robbing" && t.Witnesses == "seen");
        // the bounty of a robbed grave: 1,000 + the value
        CrimeSystem.Report(r.Law, res.Record, r.Id(4), 2 * 86_400);
        Assert.Equal(1800, r.Jobs.Get(new EntityId(res.Record.BountyJob))!.WageMites);
    }

    // ------------------------------------------------------------------ the save, the hero's command, the hash

    [Fact]
    public void TheLawBookSavesAndLoadsByteForByte_AndTheHashMovesWithTheRecords()
    {
        var r = new Rig(); var empty = r.Book.Hash();
        var obs = new List<Observer> { r.At(4, 2), r.At(5, 8) };
        var res = CrimeSystem.Commit(r.Law, r.Book, new ActSpec("theft", Hero, r.Id(4), 0, 100, 240, 0, "ch_note_crime_hero"), new Scene(DayPhase.Day), Actor(look: new ActorLook([1u, 2u], 3)), obs);
        CrimeSystem.Report(r.Law, res.Record!, r.Id(4), 86_400);
        r.Book.Convictions[(Hero.Value, "theft")] = 2;
        Assert.NotEqual(empty, r.Book.Hash());
        var save = CrimeSave.Capture(r.Book);
        var bytes = SaveCodec.SerializeCompressed(save);
        var back = SaveCodec.DeserializeCompressed<CrimeSave>(bytes).Restore();
        Assert.Equal(r.Book.Hash(), back.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(CrimeSave.Capture(back)));
        Assert.Equal(2, back.ConvictionsOf(Hero, "theft"));
        Assert.True(new CrimeBook().IsEmpty);
    }

    [Fact]
    public void TheHeroCommandRoundTripsItsTag_AndAnUnseenActWritesNothing()
    {
        var cmd = CrimeDesk.Act("theft", EntityId.Make(IdKind.Person, 1004), 0, 240, 0, "ch_note_crime_hero", new ActorLook([7u, 9u], 2), [new WitnessRow(1004, 1), new WitnessRow(1005, 0)], new CryInfo(EntityId.Make(IdKind.Person, 1004), 3, 1), 100);
        Assert.Equal(Ravensguard.Sim.Trade.HeroCommandKind.Crime, cmd.Kind);
        Assert.StartsWith("cj:act|theft|", cmd.Tag);
        Assert.Equal(10, cmd.Tag[(CrimeDesk.Prefix.Length + 4)..].Split('|').Length);
        var unseen = CrimeDesk.Act("theft", EntityId.None, 0, 240, 0, "ch_note_crime_hero", ActorLook.None, [], null, 100);
        Assert.Equal("", unseen.Tag[(CrimeDesk.Prefix.Length + 4)..].Split('|')[8]);
    }
}
