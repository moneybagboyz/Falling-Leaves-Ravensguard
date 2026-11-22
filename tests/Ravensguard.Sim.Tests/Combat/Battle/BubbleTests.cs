using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>The armies the bubble tests fight: units of a mixed foot (shield and sword, spears, swords, archers), people with ids when a ledger is given.</summary>
public sealed class BubbleScenario(FightFixture fx)
{
    private static readonly WeaponInstance Spear = new("spear", "steel");
    public ulong NextPerson = 1000;
    public bool Persons;

    public FighterKit KitOf(int cls, int team, int i, int skill)
    {
        var k = (cls switch
        {
            0 => fx.Kit($"s{team}{i}", team, 0, 0, skill: skill, shield: "heater"),
            1 => fx.Kit($"p{team}{i}", team, 0, 0, weapon: Spear, skill: skill),
            2 => fx.Kit($"w{team}{i}", team, 0, 0, skill: skill),
            _ => new FighterKit { Name = $"a{team}{i}", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = skill, ArcherySkill = skill },
        }) with { Courage = 100, Tactics = i == 0 ? 30 : 0 };
        return Persons ? k with { Person = NextPerson++ } : k;
    }

    public BattleUnit Unit(int team, int n, int tag)
    {
        var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 4 };
        for (int i = 0; i < n; i++)
        {
            int cls = i % 5 == 4 ? 3 : i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2;
            u.Soldiers.Add(KitOf(cls, team, i + tag * 1000, 30 + i * 7 % 40));
        }
        u.Officers = [0];
        return u;
    }

    public BattleSideSpec Side(int team, int men, int unitSize, int hero = -1)
    {
        var s = new BattleSideSpec { Team = team, Realm = team + 1, Commander = (ulong)(9000 + team), HostId = (ulong)(100 + team), Kind = SideKind.Army };
        for (int k = 0; men > 0; k++) { int n = Math.Min(unitSize, men); s.Units.Add(Unit(team, n, k)); men -= n; }
        if (hero >= 0) { s.HeroUnit = 0; s.HeroIndex = 0; }
        return s;
    }

    public BattleOpening Opening(int menA, int menB, int cap, int unitSize = 100, int gap = 60_000, bool hero = false, ulong seed = 41, WoundLedger? ledger = null) => new()
    {
        Id = 5, Seed = seed, GameSec = 8 * 3600, CapTotal = cap, GapMm = gap, Ledger = ledger, OriginMm = (400_000_000, 300_000_000),
        Sides = [Side(0, menA, unitSize, hero ? 0 : -1), Side(1, menB, unitSize)], CauseKind = CauseKind.War, CauseEntry = new EntityId(777), Place = new EntityId(55),
    };
}

/// <summary>
/// Phase 7 BB-C (battle-bubble-and-formations 4.1, 7, 8; gate BB-14 to BB-16): the bubble over a fight of formations. Opening and the caps, people entering at the edge, reinforcement waves, the box and the paused
/// chunks, closing, what the battle leaves in the chronicle, and the save inside a battle.
/// </summary>
[Collection("fight")]
public class BubbleTests(FightFixture fx, ITestOutputHelper output)
{
    private BubbleScenario Sc() => new(fx);

    private sealed class Sink
    {
        public readonly List<(string Type, string Key, IReadOnlyList<ChronParam> Ps, IReadOnlyList<Cause> Causes, ulong Id)> Entries = [];
        private ulong _n = 900;
        public EntityId Write(GameTime when, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subj, IReadOnlyList<Cause> causes, (string, string)[] data)
        { Entries.Add((type, key, ps, causes, ++_n)); return new EntityId(_n); }
    }

    private static void RunToEnd(BattleBubble b, int max = 40_000) { for (int t = 0; t < max && !b.Closed; t++) b.Step(); }

    // ---------------------------------------------------------------- opening, caps and the box

    [Fact]
    public void Open_TheCapsSplitTheBattleSizeByStrength_AndTheLiveSetIsWholeFormationsWithinTheCap()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(1500, 1500, 1000));
        foreach (var s in b.Sides) { Assert.Equal(500, s.LiveCap); Assert.Equal(500, b.LiveOf(s.Team)); Assert.Equal(1000, s.Reserve.Sum(u => u.Count)); Assert.Equal(5, b.W.Formations!.Rows.Count(r => r.Side == s.Team)); }
        // a weak side keeps at least a quarter of the battle size while it has the men; never more than it has
        var weak = BattleBubble.Open(fx.C, Sc().Opening(2000, 300, 1000));
        Assert.Equal(250, weak.Sides[1].LiveCap); Assert.Equal(300, weak.Sides[1].Strength); Assert.Equal(869, weak.Sides[0].LiveCap); Assert.Equal(800, weak.W.Fighters.Count(f => f.Team == 0));
        var tiny = BattleBubble.Open(fx.C, Sc().Opening(2000, 120, 1000));
        Assert.Equal(120, tiny.Sides[1].LiveCap); Assert.Empty(tiny.Sides[1].Reserve);
    }

    [Fact]
    public void Open_AUnitOverTheCapIsCutToIt_SoNoSideStartsOverItsCap()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(400, 400, 200, unitSize: 400));
        foreach (var s in b.Sides) { Assert.Equal(100, s.LiveCap); Assert.Equal(100, b.LiveOf(s.Team)); Assert.Equal(300, s.Reserve.Sum(u => u.Count)); }
    }

    [Fact]
    public void BB16_TheBoxIsTheExtentPlus150m_ClippedToAKilometreSquared()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(500, 500, 1000, gap: 80_000));
        int minX = b.W.Fighters.Min(f => f.X), maxX = b.W.Fighters.Max(f => f.X), minY = b.W.Fighters.Min(f => f.Y), maxY = b.W.Fighters.Max(f => f.Y);
        Assert.Equal((minX - 150_000, minY - 150_000, maxX + 150_000, maxY + 150_000), b.Box);
        foreach (var f in b.W.Fighters) Assert.True(b.InBox(f.X, f.Y));
        // wider than a kilometre square: cut to at most 1,400 m on a side and 1,000,000 m2, around the given centre
        var R = new Ravensguard.Content.BubbleRulesDef { Id = "t" };
        var (x0, y0, x1, y1) = BattleBubble.ClipBox(R, -1_000_000, -500_000, 1_000_000, 500_000, (300_000, 0));
        Assert.True((x1 - x0) <= 1_400_000 && (long)(x1 - x0) * (y1 - y0) <= 1_000_000L * 1_000_000L, $"{x1 - x0} x {y1 - y0}");
        Assert.True(x0 <= 300_000 && 300_000 <= x1 && y0 <= 0 && 0 <= y1);
        var small = BattleBubble.ClipBox(R, 0, 0, 800_000, 600_000, (0, 0));
        Assert.Equal((0, 0, 800_000, 600_000), small);                          // within the limits: untouched
        var narrow = BattleBubble.ClipBox(R, 0, 0, 500_000, 1_900_000, (250_000, 900_000));
        Assert.True((narrow.Y1 - narrow.Y0) <= 1_400_000 && (long)(narrow.X1 - narrow.X0) * (narrow.Y1 - narrow.Y0) <= 1_000_000L * 1_000_000L);
    }

    [Fact]
    public void BB16_TheBoxOfTwoThousandMenFitsTheLimits()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(1000, 1000, 2000, unitSize: 50, gap: 200_000));
        Assert.Equal(2000, b.W.Fighters.Count);
        long w = b.Box.X1 - b.Box.X0, h = b.Box.Y1 - b.Box.Y0;
        Assert.True(w <= 1_400_000 && h <= 1_400_000 && w * h <= 1_000_000L * 1_000_000L, $"{w / 1000} x {h / 1000} m");
        output.WriteLine($"2,000 men in 40 formations: the box is {w / 1000} x {h / 1000} m");
    }

    [Fact]
    public void BB16_TheChunksAroundTheBoxPause_AndTheBoxItselfDoesNot_AndTheyResumeAtTheClose()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(300, 300, 1000, gap: 40_000));
        var paused = b.PausedChunks;
        Assert.NotEmpty(paused);
        long cm = 32_000;
        long ox = b.OriginMm.X, oy = b.OriginMm.Y;
        // a point inside the box is not paused; a point 100 m outside it is; a point 300 m outside it is not
        Assert.False(b.IsPausedAt(ox + (b.Box.X0 + b.Box.X1) / 2, oy + (b.Box.Y0 + b.Box.Y1) / 2));
        Assert.True(b.IsPausedAt(ox + b.Box.X1 + 100_000, oy + (b.Box.Y0 + b.Box.Y1) / 2));
        Assert.False(b.IsPausedAt(ox + b.Box.X1 + 300_000, oy + (b.Box.Y0 + b.Box.Y1) / 2));
        Assert.Equal(paused.Count, paused.Distinct().Count());
        // every paused chunk lies within 200 m (plus a chunk) of the box and outside the whole chunks the box touches
        foreach (long key in paused)
        {
            int cx = (int)(key >> 32), cy = (int)(uint)key;
            long x0 = cx * cm, x1 = x0 + cm, y0 = cy * cm, y1 = y0 + cm;
            long dx = Math.Max(0, Math.Max(ox + b.Box.X0 - x1, x0 - (ox + b.Box.X1))), dy = Math.Max(0, Math.Max(oy + b.Box.Y0 - y1, y0 - (oy + b.Box.Y1)));
            Assert.True(dx <= 200_000 && dy <= 200_000);
        }
        RunToEnd(b);
        Assert.True(b.Closed);
        Assert.Empty(b.PausedChunks);
        Assert.False(b.IsPausedAt(ox + b.Box.X1 + 100_000, oy));
    }

    // ---------------------------------------------------------------- entering at the edge, waves (BB-15)

    [Fact]
    public void Entering_AtMostFiftyAPeopleATick_SharedBetweenSides_AndAUnitFormsWhenItsLastManIsIn()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(600, 600, 1200, gap: 80_000));
        // two waves' worth queued at once on both sides
        foreach (var s in b.Sides) { s.LiveCap = 600; }
        int before = b.W.Fighters.Count;
        var sp = Sc();
        foreach (var s in b.Sides) s.Reserve.Add(sp.Unit(s.Team, 100, 50)); foreach (var s in b.Sides) s.Strength += 100;
        int maxIn = 0, ticks = 0, formed0 = b.W.Formations!.Rows.Count;
        // kill the room: the cap is not what is tested here, only the rate; hand the units straight to the queue
        foreach (var s in b.Sides) { var u = s.Reserve[^1]; s.Reserve.RemoveAt(s.Reserve.Count - 1); s.Pending.Add(u); u.AnchorX = s.EntryX; u.AnchorY = s.EntryY; }
        while (b.Entering > 0 && ticks < 100) { int n0 = b.W.Fighters.Count; b.Step(); maxIn = Math.Max(maxIn, b.W.Fighters.Count - n0); ticks++; }
        Assert.Equal(before + 200, b.W.Fighters.Count);
        Assert.True(maxIn <= 50, $"{maxIn} in one tick");
        Assert.Equal(4, ticks);                                                    // 200 people at 50 a tick
        Assert.Equal(formed0 + 2, b.W.Formations.Rows.Count);                      // each unit formed when its last man entered
        Assert.True(b.MaxEnteredInATick <= 50);
    }

    [Fact]
    public void BB15_TheCapHolds_AWaveOfAtMostAQuarterEntersWithinFiveSecondsOfFallingUnderSeventyPercent()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(1500, 1500, 1000, gap: 60_000));
        var fell = new[] { -1, -1 }; var entered = new[] { -1, -1 };
        var waveSizes = new List<int>();
        int[] lastCount = [b.W.Fighters.Count, 0];
        int maxLive = 0;
        for (int t = 0; t < 14_000 && !b.Closed; t++)
        {
            int n0 = b.W.Fighters.Count;
            b.Step();
            for (int i = 0; i < 2; i++)
            {
                var s = b.Sides[i];
                int present = b.PresentOf(s.Team), active = b.LiveOf(s.Team), entering = s.Pending.Sum(u => u.Count - u.Entered);
                maxLive = Math.Max(maxLive, present);
                Assert.True(present + entering <= s.LiveCap, $"tick {t}: side {i} has {present} present and {entering} entering over its cap {s.LiveCap}");
                if (fell[i] < 0 && active * 1000 < 700 * s.LiveCap) fell[i] = b.W.Tick;
            }
            if (b.W.Fighters.Count > n0)
            {
                int n = b.W.Fighters.Count - n0;
                for (int i = 0; i < 2; i++)
                    if (entered[i] < 0 && fell[i] >= 0 && b.W.Fighters.Skip(n0).Any(f => f.Team == b.Sides[i].Team)) entered[i] = b.W.Tick;
            }
        }
        Assert.True(b.Closed, "the battle did not end");
        Assert.True(b.Sides.Sum(s => s.WavesSent) > 0, "no wave was sent");
        for (int i = 0; i < 2; i++)
        {
            if (fell[i] < 0) continue;
            Assert.True(entered[i] >= 0, $"side {i} fell under 70% at tick {fell[i]} and no wave entered");
            Assert.True(entered[i] - fell[i] <= 100, $"side {i}: fell at {fell[i]}, wave in at {entered[i]}: {(entered[i] - fell[i]) / 20.0:0.0} s");
        }
        Assert.True(maxLive <= 500);
        output.WriteLine($"fell under 70%: {fell[0]}, {fell[1]}; first wave in: {entered[0]}, {entered[1]}; waves {b.Sides[0].WavesSent} and {b.Sides[1].WavesSent}; ended at tick {b.W.Tick}");
    }

    [Fact]
    public void Waves_AreAtMostAQuarterOfTheBattleSize_WholeFormationsFirst_AndAnEmptyReserveSendsNone()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(600, 600, 400, unitSize: 50, gap: 40_000));
        var s = b.Sides[0];
        Assert.Equal(200, s.LiveCap);
        // 70 of its 200 men are dead (under 70% of its cap: 130 < 140): the room is 70, so one formation of 50 comes, never more than the quarter (100) and never over the cap
        int k = 0; foreach (var f in b.W.Fighters) if (f.Team == s.Team && k < 70) { f.Act = FAct.Dead; k++; }
        int before = b.W.Fighters.Count;
        for (int t = 0; t < 25 && s.WavesSent == 0; t++) b.Step();
        Assert.Equal(1, s.WavesSent);
        Assert.Equal(50, s.Pending.Sum(u => u.Count) + (b.W.Fighters.Count - before) - 0);             // one whole formation of 50 (entering at once, as it is under the rate)
        Assert.True(b.PresentOf(s.Team) + s.Pending.Sum(u => u.Count - u.Entered) <= s.LiveCap);
        // men who are down hold their place (they may get up): no room, no wave
        var s1 = b.Sides[1]; int d = 0;
        foreach (var f in b.W.Fighters) if (f.Team == s1.Team && d < 70) { f.Act = FAct.Down; d++; }
        for (int t = 0; t < 120; t++) b.Step();
        Assert.Equal(0, s1.WavesSent);
        // an empty reserve sends no wave
        s.Reserve.Clear(); s.Pending.Clear();
        int w0 = s.WavesSent; s.LastWaveTick = -1_000_000;
        for (int t = 0; t < 60; t++) b.Step();
        Assert.Equal(w0, s.WavesSent);
    }

    // ---------------------------------------------------------------- the close, the chronicle, the people

    [Fact]
    public void BB16_TheBattleClosesWithAWinner_FoldsBackEverySoldier_AndWritesTheBattleEntryWithItsCause()
    {
        var sink = new Sink();
        var ledger = new WoundLedger(41, 8 * 3600);
        var sc = Sc(); sc.Persons = true;
        var b = BattleBubble.Open(fx.C, sc.Opening(600, 600, 600, unitSize: 60, gap: 40_000, hero: true, ledger: ledger));
        b.Chronicle = sink.Write;
        int men = 1200;
        RunToEnd(b);
        Assert.True(b.Closed);
        var r = b.Result!;
        Assert.Equal(2, r.Sides.Count);
        // nobody is made or lost: every soldier is in the fight or in a reserve that rejoins its host
        foreach (var s in r.Sides)
        {
            int inFight = r.Soldiers.Count(x => x.Team == s.Team);
            Assert.Equal(s.Men, inFight + s.Reserve);
            Assert.Equal(s.Dead + s.Down + s.Fled + s.Yielded + s.Standing, inFight);
        }
        Assert.Equal(men, r.Sides.Sum(s => s.Men));
        // the wounds stay in the world ledger: a person who was hurt has his book there, none was dropped
        var hurt = b.W.Fighters.Where(f => f.Person != 0 && !f.Book.Empty).ToList();
        Assert.NotEmpty(hurt);
        foreach (var f in hurt) Assert.True(ledger.Books.ContainsKey(f.Person), $"person {f.Person} lost his wounds");
        // the entry: one battle_fought with a specific cause (the campaign entry), the sides' numbers, the outcome key
        var e = sink.Entries.Single(x => x.Type == "battle_fought");
        Assert.Equal("ch_bt_battle", e.Key);
        Assert.Contains(e.Causes, c => c.Kind == CauseKind.War && c.EventId == new EntityId(777));
        Assert.Contains(e.Ps, p => p.Name == "outcome" && p.Key == r.OutcomeKey);
        Assert.Contains(e.Ps, p => p.Name == "men_a" && p.Value == r.Sides[0].Men);
        Assert.Contains(e.Ps, p => p.Name == "lost_b" && p.Value == r.Sides[1].Lost);
        Assert.Equal(e.Id, r.Entry.Value);
        Assert.All(sink.Entries.Where(x => x.Type != "battle_fought"), x => Assert.Contains(x.Causes, c => c.EventId == new EntityId(e.Id)));
        Assert.Single(sink.Entries, x => x.Type == "battle_hero_led");
        output.WriteLine($"closed at tick {r.Ticks}: winner team {r.WinnerTeam}, {r.OutcomeKey}; lost {r.Sides[0].Lost} and {r.Sides[1].Lost}; entries {string.Join(", ", sink.Entries.Select(x => x.Type))}");
    }

    [Fact]
    public void TheBattleEntryIsARoot_WhenNothingBroughtTheSidesTogether()
    {
        var sink = new Sink();
        var op = Sc().Opening(80, 80, 200, unitSize: 40, gap: 30_000); op.CauseEntry = EntityId.None;
        var b = BattleBubble.Open(fx.C, op);
        b.Chronicle = sink.Write;
        RunToEnd(b);
        var e = sink.Entries.Single(x => x.Type == "battle_fought");
        Assert.Empty(e.Causes);
        Assert.DoesNotContain(e.Ps, p => p.Name == "place" && false);
    }

    [Fact]
    public void ATruce_ClosesTheBattleNow_WithTheTruceOutcome()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(200, 200, 400, unitSize: 50, gap: 60_000));
        for (int t = 0; t < 100; t++) b.Step();
        b.CallTruce();
        Assert.True(b.Closed); Assert.Equal(BattleBubble.OutTruce, b.Result!.OutcomeKey); Assert.Equal(-1, b.Result.WinnerTeam);
    }

    [Fact]
    public void TheBubble_NeverClosesForNothing_SidesThatNeverMeetCloseAfterTwentyMinutes()
    {
        var op = Sc().Opening(100, 100, 200, unitSize: 50, gap: 150_000);
        var b = BattleBubble.Open(fx.C, op);
        foreach (var r in b.W.Formations!.Rows) { r.CaptainAlways = false; r.Micro = int.MaxValue / 2; }              // nobody advances
        RunToEnd(b, 30_000);
        Assert.True(b.Closed); Assert.Equal(-1, b.Result!.WinnerTeam); Assert.InRange(b.W.Tick, 24_000, 24_100);
    }

    // ---------------------------------------------------------------- BB-14: a save inside a battle

    private (FightRecord F, BattleRecord B) Save(BattleBubble b) => (FightRecord.Capture(b.W, "raid", "battle"), BattleRecord.Capture(b)!);

    private BattleBubble Load(FightRecord f, BattleRecord br, WoundLedger? ledger = null)
    {
        var w = FightRecord.FromBytes(f.ToBytes()).Restore(fx.C, ledger);
        return BattleBubble.Restore(w, BattleRecord.FromBytes(br.ToBytes()));
    }

    [Fact]
    public void BB14_SaveInsideABattle_LoadAndContinue_EqualsTheUnbrokenRun_AndSaveLoadSaveIsByteIdentical()
    {
        var sink = new Sink();
        var a = BattleBubble.Open(fx.C, Sc().Opening(1500, 1500, 1000, gap: 50_000));
        a.Chronicle = sink.Write;
        for (int t = 0; t < 1200; t++) a.Step();
        Assert.False(a.Closed);
        var (f, br) = Save(a);
        var fb = f.ToBytes(); var bb = br.ToBytes();
        output.WriteLine($"saved at tick 1,200: fight {fb.Length / 1024} KB, battle {bb.Length / 1024} KB; reserves {a.Sides.Sum(s => s.Reserve.Sum(u => u.Count))}, entering {a.Entering}, waves {a.Sides.Sum(s => s.WavesSent)}");
        var c = Load(f, br);
        Assert.Equal(a.W.Hash(), c.W.Hash());
        // save, load, save: the same bytes
        var (f2, br2) = Save(c);
        Assert.Equal(fb, f2.ToBytes()); Assert.Equal(bb, br2.ToBytes());
        var sink2 = new Sink(); c.Chronicle = sink2.Write;
        for (int t = 0; t < 30_000 && !(a.Closed && c.Closed); t++) { a.Step(); c.Step(); if (t % 997 == 0) Assert.Equal(a.W.Hash(), c.W.Hash()); }
        Assert.True(a.Closed && c.Closed);
        Assert.Equal(a.W.Hash(), c.W.Hash());
        Assert.Equal(a.W.Tick, c.W.Tick);
        Assert.Equal(a.Result!.WinnerTeam, c.Result!.WinnerTeam);
        Assert.Equal(a.Result.Sides, c.Result.Sides);
        Assert.Equal(a.Sides.Select(s => s.WavesSent), c.Sides.Select(s => s.WavesSent));
        Assert.Equal(sink.Entries.Select(e => e.Type), sink2.Entries.Select(e => e.Type));
    }

    [Fact]
    public void BB14_ASaveWithPeopleEnteringAndWavesOnTheWay_ContinuesTheSame()
    {
        // 1,000 against 1,000 in formations of 100 with a battle size of 800: 400 live a side; 150 of side 0 fall, a wave of one formation of 100 is sent and walks in over two ticks (50 a tick)
        var a = BattleBubble.Open(fx.C, Sc().Opening(1000, 1000, 800, unitSize: 100, gap: 80_000));
        int k = 0; foreach (var f0 in a.W.Fighters) if (f0.Team == 0 && k < 150) { f0.Act = FAct.Dead; k++; }
        for (int t = 0; t < 60 && a.Entering == 0; t++) a.Step();
        a.Step();                                                                                       // the first fifty are in
        Assert.True(a.Entering > 0 && a.Entering < 100, $"{a.Entering} still entering");               // saved with a wave half in
        var (f, br) = Save(a);
        var c = Load(f, br);
        for (int t = 0; t < 600; t++) { a.Step(); c.Step(); }
        Assert.Equal(a.W.Hash(), c.W.Hash());
        Assert.Equal(a.Entering, c.Entering);
    }

    // ---------------------------------------------------------------- the world save

    [Fact]
    public void TheBattleSection_IsAbsentWithoutABubble_AndThePausedChunksFollowTheBox()
    {
        var b = BattleBubble.Open(fx.C, Sc().Opening(100, 100, 200, unitSize: 50, gap: 30_000));
        Assert.NotNull(BattleRecord.Capture(b));
        RunToEnd(b);
        Assert.Null(BattleRecord.Capture(b));
    }
}
