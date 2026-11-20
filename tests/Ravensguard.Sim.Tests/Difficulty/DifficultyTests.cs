using System.Text.RegularExpressions;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Farming;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.History;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.DifficultySliders;

/// <summary>The honest sliders (docs/specs/phase6/difficulty-sliders.md 7: DS-1 to DS-7).</summary>
[Collection("resolver2")]
public class DifficultyTests(V2Fixture f, ITestOutputHelper output)
{
    private BodyPlanV2 H => f.Human;

    private static WorldState NewState() => EconFixture.WorldWith(EconFixture.NewEconomy());

    // ------------------------------------------------------------------ DS-1

    [Fact]
    public void DS1_TheDefaultRecordIsTheGameAsCalibrated_AndWritesNothing()
    {
        var d = Difficulty.Default;
        Assert.True(d.IsDefault);
        Assert.Equal((100, 100, 100, 100, 100), (d.WoundLethalityPct, d.BanditPct, d.OriginCoinPct, d.HarvestVariancePct, d.SpoilagePct));
        Assert.Equal(1, d.AgingPace); Assert.Equal(100, d.PlagueStartPermilleX10); Assert.Equal(40, d.PlagueFatalityPct(40));
        Assert.Equal(1, new HistorySettings().AgingPace);
        var w = NewState();
        Assert.True(w.Difficulty.IsDefault);
        Assert.False(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(DifficultySave.SectionName));   // a default run keeps its bytes
        Assert.Equal(d, Difficulty.FromArray(d.ToArray()));
        Assert.Equal(d with { Plague = PlagueSeverity.Rare, Economy = EconomyHarshness.Harsh, BanditPct = 150, WoundLethalityPct = 85, AgingPace = 3, Ironman = true },
            Difficulty.FromArray((d with { Plague = PlagueSeverity.Rare, Economy = EconomyHarshness.Harsh, BanditPct = 150, WoundLethalityPct = 85, AgingPace = 3, Ironman = true }).ToArray()));
    }

    // ------------------------------------------------------------------ DS-2

    private WoundV2 Femoral(int rate10) { int p = f.P("upper_leg_l"); var pt = H.Parts[p]; int si = Array.FindIndex(pt.Structures, s => s.Id == "femoral_artery");
        return new WoundV2 { Part = (short)p, Tissue = (short)pt.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rate10, Flags = WoundFlags.ArteryHit }; }

    private WoundV2 Cut() { int p = f.P("upper_arm_l"); var l = H.Parts[p].Layers[0];
        return new WoundV2 { Part = (short)p, Tissue = (short)l.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 300, BleedX10 = 5 }; }

    [Fact]
    public void DS2_WoundLethality70_AFemoralWoundBleeds70InsteadOf100_AndInfectionIsSeventyPercentAsLikely()
    {
        var led = new WoundLedger(1) { LethalityPct = 70 };
        var b = led.For(7, H, 0);
        b.Apply(H, [Femoral(1000)], 0, 0, 0, 0, false);
        Assert.Equal(700, b.Records[0].BleedX10);                                  // 70 ml/s instead of 100
        var normal = new WoundLedger(1); var n = normal.For(7, H, 0); n.Apply(H, [Femoral(1000)], 0, 0, 0, 0, false);
        Assert.Equal(1000, n.Records[0].BleedX10);                                 // the default ledger is untouched

        // infection per day: the same draws, so the infected set at 70% is inside the set at 100%, and about 0.7 of it
        int hundred = 0, seventy = 0, merciless = 0;
        for (ulong k = 1; k <= 4000; k++)
        {
            var a = new WoundBook(3, k, 5000, 10, "human") { RegenPm = 0, LethalityPct = 100 }; a.Apply(H, [Cut()], 0, 0, 0, 0, false); a.DailyStep(1); if (a.MaxInfection > 0) hundred++;
            var c = new WoundBook(3, k, 5000, 10, "human") { RegenPm = 0, LethalityPct = 70 }; c.Apply(H, [Cut()], 0, 0, 0, 0, false); c.DailyStep(1); if (c.MaxInfection > 0) seventy++;
            var m = new WoundBook(3, k, 5000, 10, "human") { RegenPm = 0, LethalityPct = 130 }; m.Apply(H, [Cut()], 0, 0, 0, 0, false); m.DailyStep(1); if (m.MaxInfection > 0) merciless++;
        }
        output.WriteLine($"infected of 4000: 70% {seventy}, 100% {hundred}, 130% {merciless}");
        Assert.True(hundred > 100 && seventy < hundred && merciless > hundred);
        Assert.InRange(seventy * 100 / hundred, 60, 80);
        Assert.InRange(merciless * 100 / hundred, 120, 140);
    }

    // ------------------------------------------------------------------ DS-3

    [Fact]
    public void DS3_Bandits200_AmbushTollAndBlockWeightsAreDoubled_TheRestOfTheTableIsNot()
    {
        (int Hostile, int Other) Count(int pct)
        {
            var goods = EconFixture.Catalog();
            var t = new TravelState(EconFixture.Content, 5, goods, new MassLedger(goods.Count)) { GameStartSec = 0, HeroId = EntityId.Make(IdKind.Person, 1), BanditPct = pct };
            var w = new FlatWorld(); var tmpl = FlatWorld.Default(); tmpl.DangerPm = 600; tmpl.Terrain |= TerrainFlags.Pasture | TerrainFlags.Woodland; tmpl.RainDaysAgo = 1; tmpl.HolyDayInDays = 3; w.Template = tmpl;
            w.PerSecond = (_, x) => { x.Night = false; x.Daytime = true; x.Season = Season.Summer; return x; };
            int hostile = 0, other = 0; long day5 = GameTime.FromCalendar(1, Season.Summer, 5, 8).Seconds;
            for (int leg = 0; leg < 3000; leg++)
            {
                t.HeroId = EntityId.Make(IdKind.Person, (ulong)(leg + 1)); t.LastById.Clear(); t.LastEncounterSec = long.MinValue; t.LegFired.Clear();
                for (int slot = 0; slot < 42; slot++)
                {
                    long sec = day5 + leg * 90_000L + slot * 600L;
                    var d = EncounterDirector.Roll(t, w.Sample(sec, 0, 0), w.Hero, sec);
                    if (d == null) continue;
                    if (d.Kind is "Ambush" or "Demand" or "Block") hostile++; else other++;
                    t.LastById[d.Id] = sec; t.LastEncounterSec = sec;
                }
            }
            return (hostile, other);
        }
        var (h100, o100) = Count(100); var (h200, o200) = Count(200); var (h50, _) = Count(50);
        output.WriteLine($"hostile 100%: {h100}, 200%: {h200}, 50%: {h50}; others {o100} / {o200}");
        Assert.True(h100 > 150);
        // the table's weights double against the rest: the odds of a hostile row against any other row double (the encounter rate itself is unchanged)
        double odds100 = (double)h100 / o100, odds200 = (double)h200 / o200, odds50 = (double)h50 / (double)Math.Max(1, Count(50).Other);
        Assert.InRange(odds200 / odds100, 1.8, 2.2); Assert.InRange(odds50 / odds100, 0.4, 0.6);
        Assert.InRange((h200 + o200) * 100 / (h100 + o100), 95, 105);
    }

    // ------------------------------------------------------------------ DS-4

    [Fact]
    public void DS4_EconomyHarsh_OriginCoinThreeQuarters_HarvestDeviationOneAndAHalf_SpoilageAQuarterMore()
    {
        var harsh = Difficulty.Default with { Economy = EconomyHarshness.Harsh }; var lenient = Difficulty.Default with { Economy = EconomyHarshness.Lenient };
        Assert.Equal(75, harsh.ScaleOriginCoin(100)); Assert.Equal(150, lenient.ScaleOriginCoin(100)); Assert.Equal(40, Difficulty.Default.ScaleOriginCoin(40));
        Assert.Equal((150, 125), (harsh.HarvestVariancePct, harsh.SpoilagePct)); Assert.Equal((50, 75), (lenient.HarvestVariancePct, lenient.SpoilagePct));

        // the harvest: the same draws, the deviation from the mean scaled (checked field by field), and the spoilage of the same stores at the same draws
        long[] Year(int var, int spoil, out long spoiled)
        {
            var s = new FarmingSoak(); s.Econ.HarvestVariancePct = var; s.Econ.SpoilagePct = spoil; s.Run(1);
            spoiled = s.Years[0].SpoiledG; return [s.Years[0].HarvestG];
        }
        var a = Year(100, 100, out long sp100); var b = Year(150, 125, out long sp125);
        output.WriteLine($"harvest {a[0]} / {b[0]}; spoiled {sp100} / {sp125}");
        Assert.NotEqual(a[0], b[0]);
        Assert.InRange(b[0] * 1000 / a[0], 970, 1030);                       // +-4% x 1.5 around the same mean: a small, real shift
        Assert.InRange(sp125 * 100 / Math.Max(1, sp100), 115, 135);
    }

    // ------------------------------------------------------------------ DS-5

    [Fact]
    public void DS5_PlagueOff_NoStartInAThousandContinentYears_AndTheOtherValuesAreWhatTheTableSays()
    {
        int Starts(Difficulty d)
        {
            int n = 0;
            for (int year = 0; year < 1000; year++) if (DetRng.Range(77, year, 0, RngStreams.EncounterRoll, 0, 0, 10_000) < d.PlagueStartPermilleX10) n++;
            return n;
        }
        Assert.Equal(0, Starts(Difficulty.Default with { Plague = PlagueSeverity.Off }));
        int normal = Starts(Difficulty.Default), rare = Starts(Difficulty.Default with { Plague = PlagueSeverity.Rare }), freq = Starts(Difficulty.Default with { Plague = PlagueSeverity.Frequent });
        output.WriteLine($"plague starts in 1000 years: normal {normal}, rare {rare}, frequent {freq}");
        Assert.True(rare < normal && normal < freq);
        Assert.Equal((100, 50, 250), (Difficulty.Default.PlagueStartPermilleX10, (Difficulty.Default with { Plague = PlagueSeverity.Rare }).PlagueStartPermilleX10, (Difficulty.Default with { Plague = PlagueSeverity.Frequent }).PlagueStartPermilleX10));
        Assert.Equal(20, (Difficulty.Default with { Plague = PlagueSeverity.Rare }).PlagueFatalityPct(40));
        Assert.Equal(60, (Difficulty.Default with { Plague = PlagueSeverity.Frequent }).PlagueFatalityPct(40));
        Assert.Equal(90, (Difficulty.Default with { Plague = PlagueSeverity.Frequent }).PlagueFatalityPct(80));          // capped
    }

    // ------------------------------------------------------------------ DS-6

    [Fact]
    public void DS6_OnlyTheNamedSitesReadTheRecord()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Ravensguard.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string sim = Path.Combine(dir!.FullName, "src", "Ravensguard.Sim");
        // the state that holds the record and its save, and the new-game world (origin coin); every other site gets one plain number pushed in
        string[] allowed = ["Save/WorldSave.cs", "Save/DifficultySave.cs", "Worlds/NewGameWorld.cs"];
        var rx = new Regex(@"\bDifficulty (?:Difficulty|difficulty|_\w+)\b|\bDifficulty\?|Difficulty\.(?:Default|FromArray)|<Difficulty>|\bDifficulty with\b", RegexOptions.Compiled);
        var comments = new Regex(@"//.*$", RegexOptions.Multiline);
        var found = Directory.EnumerateFiles(sim, "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("/obj/") && !p.Contains("\\obj\\") && rx.IsMatch(comments.Replace(File.ReadAllText(p), "")))
            .Select(p => Path.GetRelativePath(sim, p).Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(allowed.OrderBy(x => x, StringComparer.Ordinal), found);
        // the numbers pushed in are read in exactly the named places
        string Src(string rel) => File.ReadAllText(Path.Combine(sim, rel));
        Assert.Contains("LethalityPct", Src("Combat/Wounds/WoundBook.cs")); Assert.Contains("LethalityPct", Src("Combat/Wounds/WoundBook.Healing.cs"));
        Assert.Contains("BanditPct", Src("Travel/Encounters.cs"));
        Assert.Contains("HarvestVariancePct", Src("Farming/FarmingSystem.Daily.cs")); Assert.Contains("SpoilagePct", Src("Economy/EconomyState.cs"));
        // nothing reads the hero's success: the record has no member that takes a person, a purse or a death count
        var m = typeof(Difficulty).GetMethods().Where(x => x.DeclaringType == typeof(Difficulty) && !x.IsSpecialName && x.Name is not ("Equals" or "GetHashCode" or "ToString" or "PrintMembers" or "Deconstruct") && !x.Name.Contains('<')).SelectMany(x => x.GetParameters()).Select(x => x.ParameterType).Distinct().ToList();
        Assert.All(m, t => Assert.True(t == typeof(int) || t == typeof(int[]) || t == typeof(Difficulty) || t == typeof(object), t.Name));
    }

    // ------------------------------------------------------------------ DS-7

    [Fact]
    public void DS7_ANonIronmanChangeWritesTheChronicleAndTheLog_AnIronmanRunRefusesIt_AndTheValuesSurviveASave()
    {
        var w = NewState();
        var at = GameTime.FromCalendar(5, Season.Autumn, 3, 9);
        int before = w.Chronicle.Count;
        Assert.True(w.ChangeDifficulty(Difficulty.Default with { WoundLethalityPct = 70, Economy = EconomyHarshness.Harsh }, at));
        Assert.Equal(before + 1, w.Chronicle.Count);
        var e = w.Chronicle.ByType("difficulty_changed").Single();
        Assert.Equal("player", e.Data["cause"]); Assert.Equal(at, e.When); Assert.Equal(70, w.Difficulty.WoundLethalityPct);
        Assert.Equal(125, w.Economy!.SpoilagePct);                                              // handed to the economy
        Assert.False(w.ChangeDifficulty(w.Difficulty, at));                                      // nothing changed: nothing written
        Assert.Single(w.DifficultyLog);

        // the run saves with its slider values and the history of changes, and comes back the same
        Assert.True(w.ChangeDifficulty(w.Difficulty with { BanditPct = 150 }, at));
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        Assert.True(file.Sections.ContainsKey(DifficultySave.SectionName));
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(w.Difficulty, back.Difficulty); Assert.Equal(2, back.DifficultyLog.Count);
        Assert.Equal(w.DifficultyLog[1].Values, back.DifficultyLog[1].Values);
        Assert.Equal(125, back.Economy!.SpoilagePct);
        Assert.Equal(SaveCodec.Encode(file), SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));

        // Ironman refuses it
        var iron = NewState(); iron.Difficulty = Difficulty.Default with { Ironman = true };
        Assert.False(iron.ChangeDifficulty(Difficulty.Default with { WoundLethalityPct = 70 }, at));
        Assert.Equal(100, iron.Difficulty.WoundLethalityPct); Assert.Empty(iron.Chronicle.ByType("difficulty_changed")); Assert.Empty(iron.DifficultyLog);
        // and the world's ageing pace is never a run setting
        Assert.False(NewState().ChangeDifficulty(Difficulty.Default with { AgingPace = 4 }, at));
    }
}
