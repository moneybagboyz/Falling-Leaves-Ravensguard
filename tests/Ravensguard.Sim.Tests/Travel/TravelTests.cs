using System.Text.Json.Nodes;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Tests.Travel;

/// <summary>The golden numbers of docs/specs/phase3/travel-encounters-camp.md section 7 (V-1 to V-16) and butchery (clothing-warmth-exposure.md C-11 to C-13).</summary>
public class TravelTests
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly GameTime Day5 = GameTime.FromCalendar(1, Season.Summer, 5, 8);        // well past the safe first days

    private static TravelState NewState(ulong seed = 11, long startSec = 0)
    {
        var goods = EconFixture.Catalog();
        return new TravelState(Db, seed, goods, new MassLedger(goods.Count)) { GameStartSec = startSec, HeroId = EntityId.Make(IdKind.Person, 1) };
    }

    private static void LetEverythingThrough(TravelState t)
    {
        foreach (var k in new[] { InterruptKind.Hostile, InterruptKind.Contract, InterruptKind.BlockedPath, InterruptKind.Found }) t.Bus.Set(k, InterruptPolicy.Notify);
    }

    private static RoutePath Straight(long metres) => new([0, metres * 1000], [0, 0]);

    // ------------------------------------------------------------------ data

    [Fact]
    public void V1_TheEncounterTableHas24ValidRows_AndABadConditionIsRejectedWithTheFileAndId()
    {
        var t = NewState();
        Assert.Equal(24, t.Table.Defs.Count);
        Assert.Empty(t.Table.Problems(BaseVocabulary.Create()));
        Assert.Equal(24, t.Table.Defs.Select(d => d.Id).Distinct().Count());
        var bad = JsonNode.Parse("""{ "type": "encounter", "id": "x", "conditions": [ { "id": "no_such_condition" } ] }""")!;
        var report = new ContentReport();
        BaseVocabulary.Create().Validate(bad, new SourceLocation("travel/encounters.jsonc", 1, 1), "encounter 'x'", report);
        Assert.True(report.HasErrors); Assert.Contains("no_such_condition", report.ToString()); Assert.Contains("travel/encounters.jsonc", report.ToString());
    }

    [Fact]
    public void TheDangerFormula_FollowsTheSpec()
    {
        var c = NewState().Cfg;
        Assert.Equal(100, RegionDanger.Compute(c, 0, false, false, false, false));
        Assert.Equal(100 + 40 * 5 + 300 + 100 + 50, RegionDanger.Compute(c, 5, true, false, true, true));
        Assert.Equal(1000, RegionDanger.Compute(c, 30, true, true, true, true));
    }

    // ------------------------------------------------------------------ rates

    private static (double Mean, double AmbushShare, int Total) Legs(int legs, int danger, long baseSec, bool early = false, Func<EncounterEvent, bool>? bad = null, int badLimit = 0)
    {
        var t = NewState(5, early ? baseSec : 0);
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = danger; tmpl.Terrain |= TerrainFlags.Pasture; tmpl.RainDaysAgo = 1; tmpl.HolyDayInDays = 3; w.Template = tmpl;      // a typical farmland road, a wet week, a feast coming
        if (!early) w.PerSecond = (_, x) => { x.Night = false; x.Daytime = true; x.Season = Season.Summer; return x; };      // the spec rate is a daytime road (night doubles it)
        int total = 0, ambush = 0, violations = 0;
        for (int leg = 0; leg < legs; leg++)
        {
            t.HeroId = EntityId.Make(IdKind.Person, (ulong)(leg + 1)); t.LastById.Clear(); t.LastEncounterSec = long.MinValue; t.LegFired.Clear();
            for (int slot = 0; slot < 42; slot++)
            {
                long sec = (early ? (leg * 1800L) % (41 * 3600L) : baseSec + leg * 90_000L) + slot * 600L;
                var s = w.Sample(sec, 0, 0); var d = EncounterDirector.Roll(t, s, w.Hero, sec);
                if (d == null) continue;
                total++; if (d.Kind == "Ambush") ambush++;
                if (bad != null && bad(new EncounterEvent(sec, d.Id, d.Kind, 0, 0, 0, 0, 0, [], ""))) violations++;
                t.LastById[d.Id] = sec; t.LastEncounterSec = sec;
            }
        }
        Assert.True(violations <= badLimit, $"{violations} forbidden encounters");
        return ((double)total / legs, total == 0 ? 0 : (double)ambush / total, total);
    }

    [Fact]
    public void V4_Frequency_IsAboutOneScriptedEncounterPerSevenHours_AndAmbushesAreRare()
    {
        var (mean, ambushShare, _) = Legs(10_000, 300, Day5.Seconds);
        Assert.InRange(mean, 0.9, 1.1);
        Assert.True(ambushShare < 0.10, $"ambush share {ambushShare:P1}");
    }

    [Fact]
    public void V5_SafeStart_NoAmbushOrHighwaymanInTheFirstTwoDays()
    {
        var (_, _, total) = Legs(1000, 800, 0, early: true, bad: e => e.Kind is "Ambush" or "Demand");   // 7 h legs of day 0 to 1, danger 800 (the scale is 300 pm)
        Assert.True(total > 0);
        var (meanLater, _, _) = Legs(300, 800, Day5.Seconds);
        Assert.True(meanLater > 1.5, "the same road is busier once the safe days are over");
    }

    [Fact]
    public void ChanceFollowsTheFormula()
    {
        var t = NewState(); var c = t.Cfg; var s = FlatWorld.Default();
        Assert.Equal(24_000, EncounterDirector.ChancePpm(c, s, Day5.Seconds, 0));                       // 40 pm x 300/500
        s.Night = true; Assert.Equal(48_000, EncounterDirector.ChancePpm(c, s, Day5.Seconds, 0));
        s.Night = false; s.NearestSettlementM = 300; Assert.Equal(12_000, EncounterDirector.ChancePpm(c, s, Day5.Seconds, 0));
        s.NearestSettlementM = 5000; s.Tier = RoadTier.OffRoad; Assert.Equal(42_000, EncounterDirector.ChancePpm(c, s, Day5.Seconds, 0));
        s.Tier = RoadTier.Road; Assert.Equal(7_200, EncounterDirector.ChancePpm(c, s, 3600, 0));        // first two days x0.3
    }

    // ------------------------------------------------------------------ auto-travel

    private static string Run(int chunk, out TravelState t)
    {
        t = NewState(21, Day5.Seconds); LetEverythingThrough(t);
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 700; w.Template = tmpl;
        w.Hero = TravelStatus.Calm(150);
        t.Begin("Oakford", Straight(8000), Day5.Seconds);
        long now = Day5.Seconds;
        for (int i = 0; i < 400_000 && t.Journey!.State == JourneyState.Travelling; i++) { now += chunk; t.Advance(w, now); }
        var j = t.Journey!;
        return $"{j.State} {j.Sec} {j.ProgressMm} | " + string.Join(";", t.Encounters.Select(e => $"{e.Id}@{e.Sec}:{e.XMm / 1000},{e.YMm / 1000}>{e.SpawnXMm / 1000},{e.SpawnYMm / 1000}#{string.Join(",", e.Cast)}"));
    }

    [Fact]
    public void V2_TheSameTripAt1xAnd8x_GivesTheSameEncounters()
    {
        string a = Run(1, out var t1), b = Run(8, out var t8), c = Run(160, out _);
        Assert.Equal(a, b); Assert.Equal(a, c);
        Assert.StartsWith("Arrived", a); Assert.NotEmpty(t1.Encounters); Assert.Equal(t1.Travellers.Count, t8.Travellers.Count);
    }

    [Fact]
    public void V3_Eta_8000mAt3mps()
    {
        var e = Eta.Of(8000, 300);
        Assert.Equal(148, e.GameHoursTenths); Assert.Equal(2667, e.RealSeconds1x); Assert.Equal(333, e.RealSeconds8x);
    }

    [Fact]
    public void TheHeroWalks8kmIn14_8GameHours()
    {
        var t = NewState(3, Day5.Seconds); LetEverythingThrough(t); t.Bus.Set(InterruptKind.Dusk, InterruptPolicy.Notify, quiet: true);
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 0; w.Template = tmpl; w.Hero = TravelStatus.Calm(150);
        var j = t.Begin("Far", Straight(8000), Day5.Seconds);
        t.Advance(w, Day5.Seconds + 40 * 3600);
        Assert.Equal(JourneyState.Arrived, j.State);
        Assert.InRange((j.Sec - Day5.Seconds) / 3600.0, 14.7, 14.9);
        Assert.Contains(t.Bus.Log, r => r.Kind == InterruptKind.Arrived);
        Assert.Single(t.Logs); Assert.True(t.Logs[0].Completed);
    }

    [Fact]
    public void WeatherAndGroundSlowTheHero()
    {
        var t = NewState(); var s = FlatWorld.Default(); var h = TravelStatus.Calm(150);
        int dry = t.SpeedMmPerGs(s, h); Assert.Equal(150, dry);
        s.Weather = WeatherState.HeavyRain; Assert.Equal(120, t.SpeedMmPerGs(s, h));
        s.Terrain |= TerrainFlags.Mud; Assert.True(t.SpeedMmPerGs(s, h) < 120);
        s = FlatWorld.Default(); s.Ground = "woodland"; Assert.Equal(90, t.SpeedMmPerGs(s, h));
        h.BaseMmPerGs = 250; s = FlatWorld.Default(); Assert.Equal(250, t.SpeedMmPerGs(s, h));         // trotting
    }

    // ------------------------------------------------------------------ interrupts

    [Fact]
    public void V6_AHostileAt90m_DropsTheClockTo1x_AndPauses()
    {
        var t = NewState(); t.Begin("X", Straight(5000), 1000);
        var s = FlatWorld.Default(); var h = TravelStatus.Calm(); h.HostileM = 90; h.HostileName = "Bandits";
        Assert.Equal(SimSpeed.X8, t.Bus.AllowedSpeed(SimSpeed.X8, 1000));
        Assert.True(t.Evaluate(s, h, 1000, 12_000, 0));
        Assert.Equal(SimSpeed.Paused, t.Bus.AllowedSpeed(SimSpeed.X8, 1000));
        t.Resume(); Assert.Equal(SimSpeed.X1, t.Bus.AllowedSpeed(SimSpeed.X8, 1000));
        var rec = t.Bus.Log[^1]; Assert.Equal(InterruptKind.Hostile, rec.Kind); Assert.Equal(12_000, rec.XMm); Assert.Equal(1000, rec.Sec);
        h.HostileM = int.MaxValue; t.Evaluate(s, h, 1100, 0, 0); Assert.Equal(SimSpeed.X8, t.Bus.AllowedSpeed(SimSpeed.X8, 1100));
    }

    [Fact]
    public void V7_TheSpeedGate_RefusesWithAReason_AndAllowsAfterTheWolfLeaves()
    {
        var t = NewState(); var h = TravelStatus.Calm(); h.HostileM = 120; h.HostileName = "Wolves";
        var (ok, why) = t.Gate8x(h); Assert.False(ok); Assert.Equal("Wolves in sight", why);
        h.HostileM = int.MaxValue; Assert.True(t.Gate8x(h).Allowed);
        h.InCombatStance = true; Assert.False(t.Gate8x(h).Allowed);
    }

    [Fact]
    public void AP7_EveryKindRaisesOnce_AndHonoursPauseNotifyAndAi()
    {
        Assert.Equal(23, Enum.GetValues<InterruptKind>().Length);
        Assert.Equal(InterruptBus.KindCount, Enum.GetValues<InterruptKind>().Length);
        foreach (var kind in Enum.GetValues<InterruptKind>())
            foreach (var pol in Enum.GetValues<InterruptPolicy>())
            {
                var b = new InterruptBus { Autopilot = true };
                Assert.True(b.Set(kind, pol));
                Assert.True(b.Edge(kind, true, 50, 1, 2, "x")); Assert.False(b.Edge(kind, true, 51, 1, 2, "x"));      // once
                Assert.Single(b.Log);
                Assert.Equal(pol == InterruptPolicy.Pause, b.Paused);
                Assert.Equal(pol == InterruptPolicy.Notify ? 1 : 0, b.ToastsPending);
            }
        var manual = new InterruptBus();
        Assert.False(manual.Set(InterruptKind.Cold, InterruptPolicy.Ai));                       // Ai only for autopilot persons
        Assert.False(manual.Set(InterruptKind.Attacked, InterruptPolicy.Notify, quiet: true));  // never quiet
        Assert.True(manual.Set(InterruptKind.Dusk, InterruptPolicy.Notify, quiet: true));
        manual.Raise(InterruptKind.Dusk, 5, 0, 0); Assert.Equal(0, manual.ToastsPending);
        Assert.Equal(InterruptPolicy.Pause, manual.PolicyOf((InterruptKind)99));                 // unknown kinds pause
    }

    [Fact]
    public void ADuskNotification_DoesNotStopTheJourney_ButAPauseKindDoes()
    {
        var t = NewState(8, Day5.Seconds); LetEverythingThrough(t);
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 0; w.Template = tmpl; w.Hero = TravelStatus.Calm(150);
        t.Begin("Far", Straight(30_000), Day5.Seconds);
        t.Advance(w, Day5.Seconds + 14 * 3600);                                   // 08:00 + 14 h: past sunset
        Assert.Contains(t.Bus.Log, r => r.Kind == InterruptKind.Dusk && r.Policy == InterruptPolicy.Notify);
        Assert.Equal(JourneyState.Travelling, t.Journey!.State);
        w.Hero.FatiguePm = 900; t.Advance(w, Day5.Seconds + 15 * 3600);
        Assert.Equal(JourneyState.Paused, t.Journey.State); Assert.True(t.Bus.Paused);
        long at = t.Journey.ProgressMm; t.Advance(w, Day5.Seconds + 16 * 3600); Assert.Equal(at, t.Journey.ProgressMm);
        t.Resume(); t.Advance(w, Day5.Seconds + 16 * 3600); Assert.True(t.Journey.ProgressMm > at);
    }

    [Fact]
    public void HungerAutoEats_FromThePack_WithATenMinuteBreak()
    {
        var t = NewState(9, Day5.Seconds); LetEverythingThrough(t);
        var pack = t.StoreOf(EntityId.Make(IdKind.Person, 77)); pack.Create(t.Goods!.IdOf("bread"), 3000, CreateReason.Other);
        t.PackStore = () => pack;
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 0; w.Template = tmpl; w.Hero = TravelStatus.Calm(150); w.Hero.HoursSinceMeal = 13;
        var j = t.Begin("Far", Straight(30_000), Day5.Seconds);
        t.Advance(w, Day5.Seconds + 1);
        Assert.True(pack.Qty("bread") < 3000); Assert.True(j.FoodUsedG > 0);
        long p0 = j.ProgressMm; t.Advance(w, Day5.Seconds + 590); Assert.Equal(p0, j.ProgressMm);   // resting at the meal
    }

    // ------------------------------------------------------------------ camp

    private static Camp NewCamp(TravelState t, long sec = 0, params EntityId[] who) => t.MakeCamp(5000, 7000, sec, who);

    [Fact]
    public void CampRules_NotNearASettlement_NotOnSteepOrWetGround()
    {
        var t = NewState(); var s = FlatWorld.Default();
        Assert.True(t.CanMakeCamp(s, 5).Ok);
        s.NearestSettlementM = 150; Assert.False(t.CanMakeCamp(s, 5).Ok);
        s = FlatWorld.Default(); s.SlopePm = 120; Assert.False(t.CanMakeCamp(s, 5).Ok);
        s = FlatWorld.Default(); s.Terrain |= TerrainFlags.Mud; Assert.False(t.CanMakeCamp(s, 5).Ok);
        s = FlatWorld.Default(); Assert.False(t.CanMakeCamp(s, 40).Ok);
    }

    [Fact]
    public void V8_TheFireBurns5kgAnHour_AndDiesWhenTheFuelIsGone()
    {
        var t = NewState(); var c = NewCamp(t);
        t.StoreOf(c.Id).Create(t.Goods!.IdOf("firewood"), 65_000, CreateReason.Other);
        Assert.Equal(40_000, t.AddFuel(c, 40_000));
        Assert.True(t.TryLightFire(c, EntityId.Make(IdKind.Person, 1), 100, WeatherState.Clear, true, 0));      // skill 100, dry: 950 cap, seeded draw
        c.FireLit = true;
        for (long s = 600; s <= 7 * 3600; s += 600) t.StepFire(c, s);
        Assert.True(c.FireLit); Assert.Equal(5_000, c.FuelG); Assert.Equal(15, t.FireWarmthC(c, 3)); Assert.Equal(0, t.FireWarmthC(c, 6));
        t.StepFire(c, 8 * 3600); Assert.False(c.FireLit); Assert.Equal(0, c.FuelG); Assert.Equal(0, t.FireWarmthC(c, 2));
        // 25 kg: out after 5 hours, however the time is chopped up
        var c2 = NewCamp(t, 0); t.StoreOf(c2.Id).Create(t.Goods!.IdOf("firewood"), 25_000, CreateReason.Other); c2.FireLit = true; Assert.Equal(25_000, t.AddFuel(c2, 25_000));
        for (long s = 7; s <= 5 * 3600 - 7; s += 7) t.StepFire(c2, s);
        Assert.True(c2.FireLit); t.StepFire(c2, 5 * 3600); Assert.False(c2.FireLit);
        Assert.Equal(40_000 + 25_000, t.Ledger!.Destroyed(t.Goods!.IdOf("firewood"), DestroyReason.Burned));      // every gram on the fire left the stock as burned
    }

    [Fact]
    public void TheFireChance_FollowsTheSpec()
    {
        var t = NewState();
        Assert.Equal(850, t.FireChancePm(50, WeatherState.Clear, false));
        Assert.Equal(550, t.FireChancePm(50, WeatherState.LightRain, false));
        Assert.Equal(750, t.FireChancePm(50, WeatherState.LightRain, true));
        var c = NewCamp(t); Assert.False(t.TryLightFire(c, EntityId.Make(IdKind.Person, 1), 50, WeatherState.Clear, true, 0));   // no fuel: no fire
    }

    [Fact]
    public void Shelter_ChangesRestAndWarmth()
    {
        var t = NewState(); var c = NewCamp(t);
        Assert.Equal(75, t.RestPerHour(c)); c.Shelter = ShelterKind.Bedroll; Assert.Equal(100, t.RestPerHour(c));
        c.Shelter = ShelterKind.Tent; c.FireLit = true; Assert.Equal(125, t.RestPerHour(c)); Assert.Equal(5, t.ShelterOffsetC(c));
        c.Shelter = ShelterKind.LeanTo; Assert.Equal(3, t.ShelterOffsetC(c));
    }

    [Fact]
    public void V9_Sentry_NoticeChances()
    {
        var t = NewState();
        Assert.Equal(640, t.NoticePm(40, 0)); Assert.Equal(490, t.NoticePm(40, 700));
        Assert.Equal(30, t.VisionM(false, true, false)); Assert.Equal(18, t.VisionM(false, false, true)); Assert.Equal(8, t.VisionM(false, false, false)); Assert.Equal(120, t.VisionM(true, false, false));
        Assert.InRange(1 - Math.Pow(0.36, 6), 0.9978, 0.9979);
        var sentry = EntityId.Make(IdKind.Person, 3);
        int hits = 0, n = 2000;                                                 // an approacher from 30 m at 3.5 m/s in the moonlight: 6 windows before 8 m
        for (int i = 0; i < n; i++)
        {
            bool seen = false;
            for (int w = 0; w < 6 && !seen; w++) seen = t.SentryNotices(sentry, i, 30 - (int)(3.5 * w * 1), 30, 640, 100_000L + i * 1000L + w * 20L);
            if (seen) hits++;
        }
        Assert.InRange(hits / (double)n, 0.985, 1.0);
        Assert.False(t.SentryNotices(sentry, 0, 30, t.VisionM(false, false, false), 1000, 500));      // in the dark nothing is seen at 30 m
        Assert.True(t.SentryNotices(sentry, 0, 8, 8, 1000, 500));
        Assert.Equal(t.SentryNotices(sentry, 5, 20, 30, 640, 1000), t.SentryNotices(sentry, 5, 20, 30, 640, 1019));    // same 20-second window, same answer
    }

    [Fact]
    public void V10_CookingAndFood_FourPersonsEatFrom_TheCampStock()
    {
        var t = NewState(); var c = NewCamp(t, 0);
        var g = t.Goods!; var stock = t.StoreOf(c.Id); stock.Create(g.IdOf("wheat"), 20_000, CreateReason.Other);
        var people = Enumerable.Range(1, 4).Select(i => new CampMember { Person = EntityId.Make(IdKind.Person, (ulong)i), Loyalty = 300 }).ToList();
        long sec = 20 * 3600;
        var r = t.CampMeal(c, people, sec);
        Assert.Equal(4, r.Fed); Assert.Equal(4 * 1100, r.GramsEaten); Assert.Equal(20_000 - 4400, stock.Qty("wheat"));
        Assert.All(people, m => Assert.Equal(320, m.Loyalty)); Assert.All(people, m => Assert.Equal(2, m.Familiarity));
        Assert.Equal(sec, t.LastMealSec); Assert.Equal(0, t.HoursSinceMeal(sec));
        t.CampMeal(c, people, sec + 3600); Assert.All(people, m => Assert.Equal(320, m.Loyalty));           // once a day
        t.CampMeal(c, people, sec + 86400); Assert.All(people, m => Assert.Equal(340, m.Loyalty));
        Assert.Equal(12 * 1100, t.Ledger!.Destroyed(g.IdOf("wheat"), DestroyReason.Eaten));
    }

    [Fact]
    public void V11_CompanionTalk_RevealsTheAmbitionByNightThree_AndPaysOncePerNight()
    {
        var t = NewState(); var c = NewCamp(t);
        var m = new CampMember { Person = EntityId.Make(IdKind.Person, 1), Loyalty = 400 }; var all = new[] { m };
        long night1 = 20 * 3600;
        var r1 = t.TalkAroundFire(c, all, night1, false);
        Assert.Equal(TalkKind.Ambition, r1[0].Kind); Assert.Equal(450, m.Loyalty); Assert.Equal(6, m.Familiarity); Assert.Equal(5, m.Mood); Assert.Equal(-150, m.SocialPm);
        var again = t.TalkAroundFire(c, all, night1 + 3600, false);
        Assert.Equal(TalkKind.None, again[0].Kind); Assert.Equal(450, m.Loyalty);                                  // capped for the night
        t.TalkAroundFire(c, all, night1 + 86400, false); Assert.False(m.AmbitionKnown);
        t.TalkAroundFire(c, all, night1 + 2 * 86400, false); Assert.True(m.AmbitionKnown);
        Assert.Equal(TalkKind.Backstory, t.TalkAroundFire(c, all, night1 + 3 * 86400, false)[0].Kind);
        Assert.Equal(TalkKind.Opinion, t.TalkAroundFire(c, all, night1 + 4 * 86400, true)[0].Kind);
        Assert.Equal(TalkKind.Banter, t.TalkAroundFire(c, all, night1 + 5 * 86400, false)[0].Kind);
        int loyal = m.Loyalty; t.TalkAroundFire(c, all, night1 + 6 * 86400, false, skip: true); Assert.Equal(loyal, m.Loyalty);   // Esc: no gain
    }

    [Fact]
    public void NightEvents_AreDeterministic_AndTheFoxOnlyStealsFromAnUnwatchedCamp()
    {
        var t = NewState(); var c = NewCamp(t); t.StoreOf(c.Id).Create(t.Goods!.IdOf("wheat"), 5000, CreateReason.Other);
        var seen = new HashSet<string>(); long lost = 0;
        for (int h = 0; h < 600; h++)
        {
            var a = t.RollNightEvent(c, h * 3600L, 300, false, Season.Autumn); var b = t.RollNightEvent(c, h * 3600L, 300, false, Season.Autumn);
            Assert.Equal(a.Id, b.Id); seen.Add(a.Id);
            if (a.Effect == "steal_food") { lost += a.GramsLost; }
        }
        Assert.Contains("fox_steals_food", seen); Assert.True(lost > 0);
        for (int h = 0; h < 600; h++) Assert.NotEqual("fox_steals_food", t.RollNightEvent(c, h * 3600L, 300, true, Season.Autumn).Id);
        Assert.DoesNotContain("wolf_visit", seen);                                                              // winter only
    }

    [Fact]
    public void BreakingCamp_ReturnsThePack_AndOldCampsDecayAfterThreeDays()
    {
        var t = NewState(); var hero = EntityId.Make(IdKind.Person, 77); var pack = t.StoreOf(hero); t.PackStore = () => pack;
        var c = NewCamp(t, 1000); t.StoreOf(c.Id).Create(t.Goods!.IdOf("bread"), 2000, CreateReason.Other);
        long back = t.BreakCamp(5000);
        Assert.Equal(2000, back); Assert.Equal(2000, pack.Qty("bread")); Assert.Null(t.Camp); Assert.Single(t.OldCamps);
        Assert.Equal(0, t.DecayCamps(2 * 86400)); Assert.Equal(1, t.DecayCamps(4 * 86400)); Assert.Empty(t.OldCamps);
    }

    // ------------------------------------------------------------------ forage

    private static (ResourceLayer L, int Tile) ForageTile(long stock = 1600)
    {
        var l = new ResourceLayer(); l.Add(1234, ResourceKind.Forage, stock, 1600); return (l, 1234);
    }

    [Fact]
    public void V12_Forage_Sweeps_TakeEightPercent_AndConserveMass()
    {
        var t = NewState(); var (layer, tile) = ForageTile();
        var pack = t.StoreOf(EntityId.Make(IdKind.Person, 5));
        var got = new List<long>();
        for (int i = 0; i < 6; i++) got.Add(t.Sweep(layer, tile, 50, Season.Summer, 100, pack).Grams);
        Assert.Equal([128L, 117, 108, 99, 91, 84], got); Assert.Equal(627, got.Sum());
        Assert.Equal(1600 - 627, layer.StockG(layer.Find(tile, ResourceKind.Forage)));
        long made = pack.Qty("vegetables"); Assert.Equal(627, made);                                          // the same grams: created in the pack = taken from the tile
        Assert.Equal(627, t.Ledger!.Created(t.Goods!.IdOf("vegetables"), CreateReason.Harvest));
        for (int i = 0; i < 300; i++) t.Sweep(layer, tile, 0, Season.Summer, 100, pack);
        Assert.True(layer.StockG(layer.Find(tile, ResourceKind.Forage)) >= 0);
        Assert.Equal(1600 - layer.StockG(layer.Find(tile, ResourceKind.Forage)), pack.Qty("vegetables"));
    }

    [Fact]
    public void Forage_Depletes_AndRegrowsOnDay1_AndTheSeasonsChangeTheItems()
    {
        var t = NewState(); var (layer, tile) = ForageTile(); var pack = t.StoreOf(EntityId.Make(IdKind.Person, 5));
        var summer = t.Sweep(layer, tile, 50, Season.Summer, 100, pack);
        Assert.Contains(summer.Items, i => i.Item == "berries"); Assert.DoesNotContain(summer.Items, i => i.Item == "nuts");
        Assert.Contains(t.Sweep(layer, tile, 50, Season.Autumn, 100, pack).Items, i => i.Item == "mushrooms" && i.Risky);
        long winter = t.Sweep(layer, tile, 50, Season.Winter, 100, pack).Grams;
        Assert.InRange(winter, 1, 40);                                                                           // x0.3
        for (int i = 0; i < 100; i++) t.Sweep(layer, tile, 80, Season.Summer, 100, pack);
        Assert.True(layer.StockG(0) < 400);
        layer.Regrow(GameTime.FromCalendar(2, Season.Spring, 1), new ResourceSettingsDef { Id = "r" });
        Assert.Equal(1600, layer.StockG(0));
        Assert.Equal(1600, t.Sweep(layer, tile, 50, Season.Summer, 101, pack).Grams + layer.StockG(0));
        Assert.Equal(128, t.SweepGrams(1600, 100, Season.Summer));                                               // 8% of 1,600 against a cap of 375 g
    }

    [Fact]
    public void V13_Poisoning_SevenPercentPer100g_AtForestry0()
    {
        var t = NewState();
        Assert.Equal(70, t.PoisonPmPer100g(0)); Assert.Equal(300, t.IdentifyPm(0)); Assert.Equal(650, t.IdentifyPm(50)); Assert.Equal(1000, t.IdentifyPm(100));
        int bad = 0, n = 5000; var p = EntityId.Make(IdKind.Person, 1);
        for (int i = 0; i < n; i++) if (t.Poisoned(p, 0, 100, i * 200L)) bad++;
        Assert.InRange(bad / (double)n, 0.05, 0.09);
        Assert.False(t.Poisoned(p, 100, 100, 0));
    }

    // ------------------------------------------------------------------ hunting and butchery

    private static (ResourceLayer L, int Tile) GameLand()
    {
        var l = new ResourceLayer(); int centre = 250 * 500 + 250;
        for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++) l.Add(centre + dy * 500 + dx, ResourceKind.Game, 80_000, 80_000);
        return (l, centre);
    }

    [Fact]
    public void V14_AKill_TakesItsMassOffTheLand_AndButcheryCreatesNoMass()
    {
        var t = NewState(14); var (layer, tile) = GameLand();
        var deer = t.Species("red_deer")!; long before = layer.Totals(ResourceKind.Game).Stock;
        var car = t.Kill(layer, tile, deer, 50_000, 1, 2)!;
        Assert.InRange(car.MassG, 72_000, 88_000);
        Assert.Equal(car.MassG * 25 / 2, before - layer.Totals(ResourceKind.Game).Stock);                          // micro-animals: 1,000,000 per 80 kg
        var pack = t.StoreOf(EntityId.Make(IdKind.Person, 5));
        var res = t.Butcher(car, pack, 50, 50_000 + 3600)!;
        Assert.Equal(car.MassG, res.MadeG + res.WasteG); Assert.True(res.MadeG <= car.MassG); Assert.Empty(t.Carcasses);
        long inLedger = new[] { "meat_fresh", "hide_raw", "tallow", "bone", "offal" }.Sum(gd => t.Ledger!.Created(t.Goods!.IdOf(gd), CreateReason.Hunted));
        Assert.Equal(res.MadeG, inLedger); Assert.Equal(res.MadeG, new[] { "meat_fresh", "hide_raw", "tallow", "bone", "offal" }.Sum(gd => pack.Qty(gd)));
        Assert.InRange(res.Made.Single(m => m.Tissue == "muscle").Grams / (double)car.MassG, 0.42, 0.46);
        // a land with too little game cannot be hunted
        var (poor, ptile) = (new ResourceLayer(), 100); poor.Add(100, ResourceKind.Game, 80_000, 80_000);
        Assert.Null(t.Kill(poor, ptile, deer, 1, 0, 0));
    }

    [Fact]
    public void C11_ButcherADeer_80kg()
    {
        var t = NewState(); var deer = new Carcass { Id = EntityId.Make(IdKind.Other, 9), Species = "red_deer", MassG = 80_000, DeathSec = 0 };
        var p = t.PlanButchery(deer, 50, 3600);
        Assert.Equal(1000, p.YieldPm);
        Assert.Equal(35_200, p.Yields.Single(y => y.Tissue == "muscle").Grams); Assert.Equal(5_600, p.Yields.Single(y => y.Tissue == "skin").Grams);
        Assert.Equal(3_360, p.Yields.Single(y => y.Tissue == "fat").Grams); Assert.Equal(7_200, p.Yields.Single(y => y.Tissue == "bone").Grams); Assert.Equal(4_800, p.Yields.Single(y => y.Tissue == "organs").Grams);
        Assert.Equal(13_320, p.Seconds);                                                                            // 3.7 game hours
        var p20 = t.PlanButchery(deer, 20, 3600); Assert.Equal(820, p20.YieldPm); Assert.Equal(28_864, p20.Yields.Single(y => y.Tissue == "muscle").Grams);
        Assert.Equal(13_320 / 2, t.PlanButchery(deer, 50, 3600, people: 2).Seconds); deer.Hung = true; Assert.Equal(13_320 * 3 / 4, t.PlanButchery(deer, 50, 3600).Seconds);
        var exact = t.Butcher(deer, t.StoreOf(EntityId.Make(IdKind.Person, 5)), 50, 3600, vary: false)!;
        Assert.Equal(35_200, exact.Made.Single(m => m.Tissue == "muscle").Grams); Assert.Equal(80_000, exact.MadeG + exact.WasteG);
    }

    [Fact]
    public void C12_ASheep_MatchesThePhase2Recipe_Within10Percent()
    {
        var t = NewState(); var sheep = new Carcass { Id = EntityId.Make(IdKind.Other, 10), Species = "sheep", MassG = 45_000, DeathSec = 0 };
        var y = t.PlanButchery(sheep, 50, 100).Yields.ToDictionary(a => a.Tissue, a => a.Grams);
        void Near(long got, long want) => Assert.InRange(got, want * 0.9, want * 1.1);
        Near(y["muscle"], 20_000); Near(y["skin"], 3_000); Near(y["fat"], 2_000); Near(y["bone"], 4_000); Near(y["organs"], 3_000);
        Assert.Equal(19_800, y["muscle"]); Assert.Equal(3_150, y["skin"]); Assert.Equal(4_050, y["bone"]);
    }

    [Fact]
    public void C13_AnOldCarcass_IsWorseThenGone_AndAHotDayHalvesTheFreshHours()
    {
        var t = NewState(); var deer = new Carcass { Id = EntityId.Make(IdKind.Other, 9), Species = "red_deer", MassG = 80_000, DeathSec = 0 };
        var fresh = t.PlanButchery(deer, 50, 20 * 3600); Assert.Equal(CarcassState.Fresh, fresh.State); Assert.Equal(0, fresh.QualityTierDelta); Assert.Equal(1, fresh.SpoilMult);
        var old = t.PlanButchery(deer, 50, 30 * 3600); Assert.Equal(CarcassState.Old, old.State); Assert.Equal(-1, old.QualityTierDelta); Assert.Equal(2, old.SpoilMult);
        Assert.Equal(CarcassState.Gone, t.PlanButchery(deer, 50, 80 * 3600).State);
        Assert.Null(t.Butcher(deer, t.StoreOf(EntityId.Make(IdKind.Person, 5)), 50, 80 * 3600));
        Assert.Equal(CarcassState.Old, t.PlanButchery(deer, 50, 14 * 3600, tempC: 30).State);
        Assert.Equal(CarcassState.Gone, t.PlanButchery(new Carcass { Species = "horse", MassG = 450_000 }, 50, 0, tabooCulture: true).State);
    }

    [Fact]
    public void Snares_CatchHares_AndLateCollectionFindsThemEmpty()
    {
        var t = NewState(3); var (layer, tile) = GameLand();
        var s = t.SetSnare(tile, 0)!;
        for (int i = 1; i < 6; i++) t.SetSnare(tile, 0); Assert.Null(t.SetSnare(tile, 0));                         // at most six
        for (long h = 1; h <= 40; h++) t.StepSnares(layer, h * 3600, 80);
        Assert.Contains(t.Snares, x => x.Caught);
        var caught = t.Snares.First(x => x.Caught);
        long stock = layer.Totals(ResourceKind.Game).Stock;
        var car = t.CollectSnare(caught, 30 * 3600); Assert.NotNull(car); Assert.Equal("hare", car!.Species);
        var late = t.Snares.FirstOrDefault(x => x.Caught); if (late != null) Assert.Null(t.CollectSnare(late, 49 * 3600));
        Assert.True(stock < 81 * 169 * 1000L);
    }

    [Fact]
    public void Animals_NoticeFasterWhenRunning_AndSlowerWhenSneakingDownwind()
    {
        var t = NewState();
        Assert.Equal(30, t.AwarenessM(false, false, false, false)); Assert.Equal(15, t.AwarenessM(true, false, false, false)); Assert.Equal(45, t.AwarenessM(false, true, false, false));
        Assert.Equal(9, t.AwarenessM(true, false, true, false)); Assert.Equal(21, t.AwarenessM(false, false, false, true));
        Assert.NotNull(t.PickSpecies(Biome.TemperateForest, Season.Summer, 5, 10)); Assert.Null(t.PickSpecies(Biome.Desert, Season.Summer, 5, 10));
    }

    // ------------------------------------------------------------------ fairs

    [Fact]
    public void V15_TheSpringFair_RunsDays8To10_AndOnlyThen()
    {
        var b = FairBook.From(Db); var h = FairHolder.CharterTown;
        foreach (int d in new[] { 8, 9, 10 })
        {
            Assert.Equal(12, b.StallBonus(d, h)); Assert.Equal(1200, b.DemandMultPm("ale", d, h)); Assert.Equal(3, b.JobBoardMult(d, h)); Assert.Equal(13, b.WorkEndHour(d, h));
            Assert.True(Ravensguard.Sim.Travel.FairCalendar.IsFairDay(d));
        }
        Assert.Equal(0, b.StallBonus(11, h)); Assert.Equal(1000, b.DemandMultPm("ale", 11, h)); Assert.Equal(1, b.JobBoardMult(11, h)); Assert.Equal(-1, b.WorkEndHour(11, h)); Assert.False(FairCalendar.IsFairDay(11));
        Assert.Equal(0, b.StallBonus(8, FairHolder.Village));                                                    // a village has no charter
        Assert.Equal("Spring Fair at Oakford in 3 days", b.Announcement("Oakford", 5, h)); Assert.Null(b.Announcement("Oakford", 1, h));
        Assert.Equal(150, b.TripBonusPm(6, h)); Assert.Equal(0, b.TripBonusPm(20, h));
        Assert.Equal(1400, b.DemandMultPm("ale", 38, FairHolder.Village)); Assert.Equal(13, b.WorkEndHour(54, FairHolder.Other)); Assert.Equal(1300, b.DemandMultPm("firewood", 53, h));
        int purse = b.ContestPurse(1, 9, 8, 8, "archery"); Assert.InRange(purse, 5, 20); Assert.Equal(0, b.ContestPurse(1, 9, 11, 11, "archery"));
    }

    // ------------------------------------------------------------------ routes

    [Fact]
    public void Routes_PreferRoads_AvoidWater_AndReturnNullWhenBlocked()
    {
        var open = RouteFinder.Plan(new OpenGrid(50), 32_000, 32_000, 3_000_000, 32_000)!;
        Assert.InRange(open.LengthMm, 2_900_000, 3_100_000);
        var road = new OpenGrid(50, 64, (x, y) => y == 10 ? 1000 : 2000);                                         // a road along row 10 beats the straight way across rough ground
        var viaRoad = RouteFinder.Plan(road, 32_000 + 64_000 * 0, 10 * 64_000 + 32_000, 40 * 64_000 + 32_000, 10 * 64_000 + 32_000)!;
        Assert.InRange(viaRoad.LengthMm, 2_500_000, 2_700_000);
        var wall = new OpenGrid(50, 64, (x, y) => x == 25 ? 0 : 1000);
        Assert.Null(RouteFinder.Plan(wall, 32_000, 32_000, 40 * 64_000, 32_000));
        var gap = new OpenGrid(50, 64, (x, y) => x == 25 && y != 30 ? 0 : 1000);
        var detour = RouteFinder.Plan(gap, 5 * 64_000, 5 * 64_000, 40 * 64_000, 5 * 64_000)!; Assert.True(detour.LengthMm > 35 * 64_000 + 10 * 64_000);
        var (x1, y1, _, _) = open.At(open.LengthMm / 2); Assert.InRange(x1, 1_400_000, 1_700_000); Assert.Equal(32_000, y1);
    }

    // ------------------------------------------------------------------ save

    private static string Hex(byte[] b) => Convert.ToHexString(b);

    [Fact]
    public void V16_SaveLoadSave_IsByteIdentical_MidJourney_AndInCamp()
    {
        var econ = EconFixture.NewEconomy(true);
        var t = new TravelState(Db, EconFixture.Shared.Cs.Seed, econ.Goods, econ.Mass) { Hosted = true, OpenStore = id => econ.OpenStore(StoreKind.Open, id), GameStartSec = 0, HeroId = EntityId.Make(IdKind.Person, 1) };
        LetEverythingThrough(t);
        var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 700; w.Template = tmpl; w.Hero = TravelStatus.Calm(150);
        t.Begin("Oakford", Straight(8000), Day5.Seconds); t.Advance(w, Day5.Seconds + 6 * 3600);
        Assert.Equal(JourneyState.Travelling, t.Journey!.State); Assert.NotEmpty(t.Encounters);
        var world = EconFixture.WorldWith(econ); world.Travel = t;
        byte[] bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(Hex(bytes), Hex(SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0"))));
        Assert.Equal(t.Journey.ProgressMm, back.Travel!.Journey!.ProgressMm); Assert.Equal(t.Encounters.Count, back.Travel.Encounters.Count);
        // the loaded journey goes on to the same end
        var t2 = back.Travel; t2.Advance(w, Day5.Seconds + 40 * 3600); t.Advance(w, Day5.Seconds + 40 * 3600);
        Assert.Equal(t.Journey.Sec, t2.Journey!.Sec); Assert.Equal(t.Encounters.Count, t2.Encounters.Count);
        // in camp, with a fire, stock, sentries and companions
        var c = t.MakeCamp(5000, 6000, Day5.Seconds + 20 * 3600, [EntityId.Make(IdKind.Person, 5), EntityId.Make(IdKind.Person, 6)]);
        t.StoreOf(c.Id).Create(econ.Goods.IdOf("firewood"), 30_000, CreateReason.Other); t.StoreOf(c.Id).Create(econ.Goods.IdOf("wheat"), 9000, CreateReason.Other);
        t.AddFuel(c, 20_000); c.FireLit = true; c.Shelter = ShelterKind.Tent; t.SetSentries(c, [EntityId.Make(IdKind.Person, 5)]); t.StepFire(c, c.MadeAtSec + 3700);
        t.Companions.Add(new CampMember { Person = EntityId.Make(IdKind.Person, 5), Name = "Aldric", Loyalty = 500, AmbitionProgress = 2 });
        t.TalkAroundFire(c, t.Companions, c.MadeAtSec + 3600, false); t.Snares.Add(new Snare { Id = 1, Tile = 77, SetSec = 5 });
        t.Carcasses.Add(new Carcass { Id = EntityId.Make(IdKind.Other, 3), Species = "hare", MassG = 4000, DeathSec = 7 });
        bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(Hex(bytes), Hex(SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0"))));
        var bc = back.Travel!.Camp!; Assert.True(bc.FireLit); Assert.Equal(c.FuelG, bc.FuelG); Assert.Equal(9000, back.Travel.StoreOf(bc.Id).Qty("wheat")); Assert.Equal(ShelterKind.Tent, bc.Shelter);
        // a world nobody travelled in keeps its old bytes
        Assert.DoesNotContain("travel", SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(EconFixture.WorldWith(EconFixture.NewEconomy(false)), EconFixture.ContentHash, "0.1.0"))).Sections.Keys);
    }

    [Fact]
    public void TheTravelSection_RoundTripsOnAPrivateLedgerToo()
    {
        var t = NewState(); var c = NewCamp(t, 100, EntityId.Make(IdKind.Person, 2));
        t.StoreOf(c.Id).Create(t.Goods!.IdOf("bread"), 1234, CreateReason.Other); t.Journey = new Journey { Path = Straight(500), DestName = "X", State = JourneyState.Paused };
        t.Bus.Raise(InterruptKind.Dusk, 5, 1, 2, "dusk"); t.Bus.Raise(InterruptKind.Hostile, 6, 1, 2, "wolves");
        var a = SaveCodec.Serialize(TravelSave.Capture(t));
        var t2 = NewState(); SaveCodec.Deserialize<TravelSave>(a).Restore(t2);
        Assert.Equal(Hex(a), Hex(SaveCodec.Serialize(TravelSave.Capture(t2))));
        Assert.Equal(1234, t2.StoreOf(c.Id).Qty("bread")); Assert.True(t2.Bus.Paused); Assert.True(t2.Bus.SpeedCapped);
    }
}
