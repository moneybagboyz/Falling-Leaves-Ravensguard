using System.Diagnostics;
using System.Text.Json;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.History;

/// <summary>History recalibrated to the live rules (history-recalibration, gate HR-1 to HR-10): v0 frozen, v1 calibrated over 50 seeds x 200 years.</summary>
public class HistoryV1Tests(ITestOutputHelper output)
{
    private static readonly Lazy<string> Dir = new(() => ContinentSettings.FindBaseContent()!);
    private static readonly Lazy<ContentDatabase> Db = new(() => new ModLoader(ContentTypeRegistry.Default).Load([Dir.Value]).Database);
    private static readonly Lazy<ChronicleTypes> Types = new(() => ChronicleTypes.From(Db.Value));

    private static (Continent C, HistorySettings S) Inputs(ulong seed, int years, int rules)
    {
        var cs = ContinentSettings.LoadDefaults(Dir.Value); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(Dir.Value); hs.Seed = seed; hs.Years = years;
        if (rules >= 1) hs.UseRulesV1(Db.Value);
        return (ContinentGenerator.Generate(cs), hs);
    }

    private static Dictionary<string, string> EnglishRows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Ravensguard.Game", "localization", "en.csv")))
        {
            int c = line.IndexOf(',');
            if (c <= 0) continue;
            string v = line[(c + 1)..];
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\"\"", "\"");
            d[line[..c]] = v;
        }
        return d;
    }

    // ------------------------------------------------------------------ the 50-seed sample

    private sealed record Sample(ulong Seed, HistoryStats Stats, double Coverage, ChainReport Chains, List<string> Problems, List<int> TownLossPm, int Founded0, int Cultures, List<int> DerivedSaints,
        int ExcommUsed, int Excomms, int Declared, int MinUtility);

    private const int Seeds = 50, Years = 200;
    private static readonly Lazy<List<Sample>> Set = new(() =>
    {
        var db = Db.Value; var types = Types.Value;
        var res = new Sample[Seeds];
        Parallel.For(0, Seeds, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i =>
        {
            ulong seed = (ulong)i + 1;
            var (c, s) = Inputs(seed, Years, 1);
            var h = HistoryGenerator.Generate(c, s);
            var problems = new List<string>();
            var loss = new List<int>();
            int rebellions = 0;
            var u = HistoryRules.Unrest;
            int declared = 0, minUtil = int.MaxValue;
            foreach (var e in h.Chronicle.All)
            {
                switch (e.TypeId)
                {
                    case "h_rebellion":
                        rebellions++;
                        int pm = int.Parse(e.Data["unrest"]), tops = int.Parse(e.Data["top_seasons"]);
                        if (!(UnrestRules.RungFor(u, pm) >= 4 && UnrestRules.RevoltDue(u, pm, tops * TimeConst.DaysPerSeason, 0))) problems.Add($"rebellion {e.Id} at {pm} for {tops} seasons");
                        break;
                    case "h_unrest_rung":
                        if (int.Parse(e.Data["unrest"]) < u.Rung3) problems.Add($"rung entry {e.Id} below the riot rung");
                        break;
                    case "h_plague_reaches": loss.Add(int.Parse(e.Data["lost_pm"])); break;
                    case "h_war_declared": declared++; minUtil = Math.Min(minUtil, int.Parse(e.Data["utility"])); break;
                }
            }
            int used = h.Excommunications.Count(x => x.WarTick >= 0 && x.WarTick - x.Tick <= 40);
            var book = ReligionBook.Derive(seed, db, h);
            int founded0 = h.Chronicle.All.Count(e => e.TypeId == "h_religion_founded" && e.When.Year == 0);
            res[i] = new Sample(seed, HistoryStats.Of(c, h), h.Chronicle.SpecificCoverage(types).Ratio, ChronicleChains.Check(h.Chronicle, types), problems, loss, founded0, Math.Min(8, c.Cultures.Count),
                book.All.Select(r => r.Saints.Count).ToList(), used, h.Excommunications.Count, declared, minUtil == int.MaxValue ? 0 : minUtil);
        });
        return [.. res];
    });

    // ------------------------------------------------------------------ HR-1: v0 frozen

    [Theory]
    [InlineData(1UL, "57C88F0A21F5FBDDD38FA23607021879E9C902E4F5F03DD50511C7C90C96AA52")]      // the Phase 1 pins of GoldenHashes
    [InlineData(1234UL, "D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9")]
    [InlineData(7UL, "F2549C4C615E39C1")]                                                      // pinned at P6-F (the v0 code path is unchanged; seeds 1 and 1234 prove it against Phase 1)
    public void HR1_TheV0HistoryIsFrozen_ForTheSeedsOfThePhase1Pins(ulong seed, string pin)
    {
        var (c, s) = Inputs(seed, 250, 0);
        Assert.Equal(0, s.RulesVersion);
        var h = HistoryGenerator.Generate(c, s);
        Assert.Equal(0, h.RulesVersion);
        Assert.StartsWith(pin, h.ComputeHash());
        Assert.Empty(h.Religions); Assert.Empty(h.Heresies); Assert.Empty(h.SlaveRaids);
        Assert.DoesNotContain(h.Chronicle.All, e => e.TextKey.Length > 0 || e.Causes.Any(k => k.NoteKey.Length > 0));      // v0 writes English only
    }

    [Fact]
    public void HR1_AWorldFileOrSaveWithoutTheVersion_IsV0_AndV0SettingsWriteNoNewKeys()
    {
        var v0 = HistorySettings.LoadDefaults(Dir.Value);
        string json = JsonSerializer.Serialize(v0);
        Assert.DoesNotContain("RulesVersion", json); Assert.DoesNotContain("CultureRules", json);      // a load-and-resave of an older world file or save keeps its bytes
        var back = JsonSerializer.Deserialize<HistorySettings>(json)!;
        Assert.Equal(0, back.RulesVersion); Assert.False(back.IsV1);
        Assert.Equal(json, JsonSerializer.Serialize(back));
        var v1 = HistorySettings.LoadDefaults(Dir.Value).UseRulesV1(Db.Value);
        string j1 = JsonSerializer.Serialize(v1);
        Assert.Contains("\"RulesVersion\":1", j1);
        var b1 = JsonSerializer.Deserialize<HistorySettings>(j1)!;
        Assert.True(b1.IsV1); Assert.Equal(v1.CultureRules!.Count, b1.CultureRules!.Count);
        Assert.Equal(j1, JsonSerializer.Serialize(b1));
        Assert.Equal("Phase 1", HistoryRules.Label(0)); Assert.Equal("Phase 6", HistoryRules.Label(1));
    }

    [Fact]
    public void HR1_ARulesVersion1WorldRegeneratesIdenticallyFromItsSettings_AndDiffersFromV0()
    {
        var (c, s) = Inputs(11, 80, 1);
        var a = HistoryGenerator.Generate(c, s);
        var b = HistoryGenerator.Generate(c, JsonSerializer.Deserialize<HistorySettings>(JsonSerializer.Serialize(s))!);       // as a save regenerates its history from the stored settings
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        var (c0, s0) = Inputs(11, 80, 0);
        Assert.NotEqual(a.ComputeHash(), HistoryGenerator.Generate(c0, s0).ComputeHash());
        Assert.Equal(1, a.RulesVersion);
    }


    // ------------------------------------------------------------------ the library keeps each world's rules

    [Fact]
    public void HR1_ALibraryWorldKeepsItsRules_AV0WorldFileHasNoNewKeys_AndBothLoadWithoutALayerChangedRefusal()
    {
        string root = Path.Combine(Path.GetTempPath(), "rg-hrv-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lib = new Ravensguard.Sim.Worlds.WorldLibrary(root);
            var cs = ContinentSettings.LoadDefaults(Dir.Value); cs.Seed = 21;
            var h0 = HistorySettings.LoadDefaults(Dir.Value); h0.Seed = 21; h0.Years = 50;
            var cs1 = ContinentSettings.LoadDefaults(Dir.Value); cs1.Seed = 22;
            var h1 = HistorySettings.LoadDefaults(Dir.Value).UseRulesV1(Db.Value); h1.Seed = 22; h1.Years = 50;
            var w0 = lib.Create("Old", cs, h0, "h", "t"); var w1 = lib.Create("New", cs1, h1, "h", "t");
            string f0 = File.ReadAllText(Path.Combine(lib.FolderOf(w0.Info.Id), "world.json")), f1 = File.ReadAllText(Path.Combine(lib.FolderOf(w1.Info.Id), "world.json"));
            Assert.DoesNotContain("RulesVersion", f0); Assert.Contains("RulesVersion", f1);
            var r0 = lib.Load(w0.Info.Id, "h"); var r1 = lib.Load(w1.Info.Id, "h");                 // the hash check regenerates each from its own settings
            Assert.Equal(0, r0.History.RulesVersion); Assert.Equal(1, r1.History.RulesVersion);
            Assert.Equal(w0.Info.File.HistoryHash, r0.History.ComputeHash()); Assert.Equal(w1.Info.File.HistoryHash, r1.History.ComputeHash());
            Assert.Equal(0, lib.List().First(x => x.Id == w0.Info.Id).File.History.RulesVersion);
            Assert.Equal(1, lib.List().First(x => x.Id == w1.Info.Id).File.History.RulesVersion);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    // ------------------------------------------------------------------ HR-2: derived Phase 6 state of a v0 world is pure

    [Fact]
    public void HR2_TheReligionsOfAV0WorldAreDerivedFromItsHistory_AFunctionOfSeedContentAndHistoryOnly()
    {
        var (c, s) = Inputs(1234, 250, 0);
        var h = HistoryGenerator.Generate(c, s);
        var a = ReligionBook.Derive(1234, Db.Value, h); var b = ReligionBook.Derive(1234, Db.Value, h);
        Assert.Equal(a.Count, b.Count); Assert.True(a.Count >= 4);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a.All[i].Id, b.All[i].Id);
            Assert.Equal(a.All[i].Saints.Select(x => x.Full), b.All[i].Saints.Select(x => x.Full));
            Assert.Equal(a.All[i].HolyDays.Select(x => (x.DayOfYear, x.Id)), b.All[i].HolyDays.Select(x => (x.DayOfYear, x.Id)));
            Assert.Equal(a.All[i].HolySiteHistory, b.All[i].HolySiteHistory);
        }
        for (ulong p = 1; p < 50; p++) Assert.Equal(LordTraits.Of(1234, p), LordTraits.Of(1234, p));                    // lord traits: id and seed only
    }

    // ------------------------------------------------------------------ HR-3: war

    [Fact]
    public void HR3_V1War_PairsAtWarShareAndWarsPerNeighbouringPair_InTheTargets()
    {
        var set = Set.Value;
        int share = (int)set.Average(x => x.Stats.WarShareMille), perCentury = (int)set.Average(x => x.Stats.WarsPerPairCenturyMille);
        output.WriteLine($"{Seeds} seeds x {Years} y: war share {share / 10.0:F1}% of the pair-seasons, wars per neighbouring pair per century {perCentury / 1000.0:F2}, ever warred {set.Average(x => x.Stats.PairsEverWarredMille) / 10:F0}% of pairs, realms fallen {set.Average(x => x.Stats.RealmsFell * 100.0 / x.Stats.StartRealms):F0}%");
        Assert.InRange(share, 0, 350);                                   // <= 35 percent (v0's old dice: about 71)
        Assert.InRange(perCentury, 500, 1500);                           // 0.5 to 1.5
        Assert.All(set, x => Assert.InRange(x.Stats.WarShareMille, 0, 350));
        int fell = (int)set.Average(x => x.Stats.RealmsFell * 1000 / x.Stats.StartRealms);
        Assert.InRange(fell, 100, 300);                                  // realms fallen in 200 years: 10 to 30 percent
    }

    [Fact]
    public void DA2_TheHistoryDeclaresByTheSharedWarUtility_AtItsThreshold()
    {
        var set = Set.Value;
        int declared = set.Sum(x => x.Declared);
        Assert.True(declared > 500, $"only {declared} declarations");
        Assert.All(set.Where(x => x.Declared > 0), x => Assert.True(x.MinUtility >= HistoryRules.Politics.MartialDeclareAt, $"seed {x.Seed}: a war declared at utility {x.MinUtility}"));   // nobody declares under the martial threshold of 40
        // the one function: the history's politics numbers are the content's defaults
        Assert.Equal(Db.Value.All<PoliticsSettingsDef>().Single(), HistoryRules.Politics);
        Assert.Equal(Db.Value.All<UnrestSettingsDef>().Single(), HistoryRules.Unrest);
    }

    // ------------------------------------------------------------------ HR-4: rebellions only past the ladder (UR-10)

    [Fact]
    public void HR4_V1RebellionsStartOnlyPastTheLaddersThresholds_OnTheCoarseUnrest()
    {
        var set = Set.Value;
        Assert.All(set, x => Assert.True(x.Problems.Count == 0, string.Join("; ", x.Problems)));
        int rebellions = set.Sum(x => x.Stats.Rebellions);
        output.WriteLine($"{rebellions} rebellions in {Seeds} worlds ({rebellions / (double)Seeds:F1} each)");
        Assert.True(rebellions > 100);
        var u = HistoryRules.Unrest;
        Assert.Equal([150, 300, 450, 600], [u.Rung1, u.Rung2, u.Rung3, u.Rung4]);                          // UR-10's thresholds the history reads
        Assert.False(UnrestRules.RevoltDue(u, 599, 60, 0)); Assert.False(UnrestRules.RevoltDue(u, 600, 29, 0)); Assert.True(UnrestRules.RevoltDue(u, 600, 30, 0));
    }

    // ------------------------------------------------------------------ HR-5: plague

    [Fact]
    public void HR5_TheGreatPlague_ByTheLiveFunctions_ZeroToFourInTwoHundredYears_AndTownsLoseAboutAThird()
    {
        var set = Set.Value;
        double mean = set.Average(x => x.Stats.Plagues);
        int within = set.Count(x => x.Stats.Plagues <= 4);
        var losses = set.SelectMany(x => x.TownLossPm).ToList();
        output.WriteLine($"great plagues per {Years} years: mean {mean:F2}, {within} of {Seeds} worlds within 0-4; {losses.Count} town visits, mean loss {(losses.Count == 0 ? 0 : losses.Average()):F0} per mille");
        Assert.InRange(mean, 1.5, 2.5);
        Assert.True(within >= Seeds * 9 / 10, $"{within} of {Seeds} within 0-4");
        Assert.True(losses.Count > 50);
        Assert.InRange(losses.Average(), 260, 340);                                                          // 26 to 34 percent
        // the function the history calls is the live one: attack x fatality
        var dr = DiseaseRules.Defaults();
        Assert.Equal(30, dr.S.AttackTownPct * (dr.DeathPpm(IllnessKind.Plague, IllnessCare.None) / 10_000) / 100);
        Assert.Equal(100, new Difficulty().PlagueStartPermilleX10 * 1);                                    // 1 percent a continent-year at normal
        int starts = 0; for (int y = 0; y < 20_000; y++) if (ContinentPlagueStep.Starts(5, y, PlagueSeverity.Normal)) starts++;
        Assert.InRange(starts, 140, 260);
    }

    // ------------------------------------------------------------------ HR-6: faith

    [Fact]
    public void HR6_EveryCulturesReligionIsFoundedAtYearZero_SaintsHeresiesAndExcommunicationsAsTargeted()
    {
        var set = Set.Value;
        Assert.All(set, x => Assert.Equal(x.Cultures, x.Founded0));
        var saints = set.SelectMany(x => x.DerivedSaints).ToList();
        double perReligion = saints.Average();
        output.WriteLine($"saints per religion (history saints and founders): mean {perReligion:F1}, min {saints.Min()}, max {saints.Max()}; heresies {set.Average(x => x.Stats.Heresies):F1} a world; excommunications {set.Sum(x => x.Excomms)}, used as a casus belli within ten years: {set.Sum(x => x.ExcommUsed)}");
        Assert.All(saints, n => Assert.InRange(n, 4, 12));
        double heresiesPerReligionCentury = set.Average(x => x.Stats.Heresies) / set.Average(x => x.Cultures) / (Years / 100.0);
        Assert.InRange(heresiesPerReligionCentury, 0.0, 2.0);
        Assert.True(set.Sum(x => x.Stats.Heresies) > 0);
        Assert.Equal(0, set.Sum(x => x.Stats.Schisms));                                                       // schisms are the overview's cut item 5
        int ex = set.Sum(x => x.Excomms), used = set.Sum(x => x.ExcommUsed);
        Assert.True(ex >= 100);
        Assert.True(used * 2 >= ex, $"{used} of {ex} excommunications used within ten years");
    }

    // ------------------------------------------------------------------ HR-7: bondage

    [Fact]
    public void HR7_SlaveRaids_OnlyByChattelAndWarCaptiveRealms_AndTheirCaptivesReachAMarket()
    {
        var (c, s) = Inputs(3, 200, 1);
        var h = HistoryGenerator.Generate(c, s);
        Assert.True(h.SlaveRaids.Count > 10);
        foreach (var r in h.SlaveRaids)
        {
            var realm = h.Realms[r.Raider];
            string law = s.CultureRules![Math.Clamp(realm.CultureId, 0, 7)].Slavery;
            Assert.True(law is "chattel" or "war_captives", $"{realm.Name} ({law}) raided");
            Assert.NotEqual(0UL, r.MarketEvent.Value);                                                       // the sale is a chronicle entry of its own
            var sale = h.Chronicle.Get(r.MarketEvent)!;
            Assert.Equal("h_slaves_sold", sale.TypeId); Assert.Contains(sale.Causes, k => k.EventId == r.Event);
            Assert.Equal("h_slave_raid", h.Chronicle.Get(r.Event)!.TypeId);
        }
        Assert.DoesNotContain(h.SlaveRaids, r => s.CultureRules![Math.Clamp(h.Realms[r.Raider].CultureId, 0, 7)].Slavery is "none" or "debt");
        // no raid by a culture whose faith forbids it, in a world where the base cultures hold what the content says
        Assert.Equal(["war_captives", "none", "debt", "chattel"], s.CultureRules!.Take(4).Select(x => x.Slavery).OrderBy(x => x == "war_captives" ? 0 : x == "none" ? 1 : x == "debt" ? 2 : 3).ToArray());
    }

    // ------------------------------------------------------------------ HR-8: coverage and chains

    [Fact]
    public void HR8_SpecificCauseCoverageIsAtLeastNinetyPercent_ForEveryV1History_AndEveryChainResolves()
    {
        var set = Set.Value;
        output.WriteLine($"coverage min {set.Min(x => x.Coverage):P1}, mean {set.Average(x => x.Coverage):P1}");
        Assert.All(set, x => Assert.True(x.Coverage >= 0.90, $"seed {x.Seed}: {x.Coverage:P1}"));
        Assert.All(set, x => Assert.True(x.Chains.Sound, $"seed {x.Seed}: " + string.Join("; ", x.Chains.Problems)));
    }

    [Fact]
    public void TheV1TypesAllHaveRows_AndTheirKeysAndNoteKeysAreInEnCsv()
    {
        var (c, s) = Inputs(5, 120, 1);
        var h = HistoryGenerator.Generate(c, s);
        var types = Types.Value;
        var en = EnglishRows();
        foreach (var e in h.Chronicle.All)
        {
            Assert.True(types.Has(e.TypeId), "no row for " + e.TypeId);
            if (e.TextKey.Length == 0) continue;
            Assert.True(en.ContainsKey(e.TextKey), "no text for " + e.TextKey);
            foreach (var p in e.Params!.Where(p => p.Kind == ChronParamKind.TextKey)) Assert.True(en.ContainsKey(p.Key), "no text for " + p.Key);
            foreach (var k in e.Causes.Where(k => k.NoteKey.Length > 0)) Assert.True(en.ContainsKey(k.NoteKey), "no text for " + k.NoteKey);
            Assert.Contains("{", en[e.TextKey]);                                                             // a sentence with holes for its parameters
        }
        // a keyed entry formats to a sentence with the names of the legends
        var war = h.Chronicle.All.FirstOrDefault(e => e.TypeId == "h_war_declared");
        if (war != null) Assert.DoesNotContain("{", h.Title(war, k => en.GetValueOrDefault(k, k)));
    }

    // ------------------------------------------------------------------ the pinned numbers equal the content's

    [Fact]
    public void TheV1ConstantsEqualTheContentTheyAreFrozenFrom_AndTheVitalTable()
    {
        var db = Db.Value;
        int Amount(string id) => db.All<OpinionKindDef>().Single(o => o.Id == id).Amount;
        Assert.Equal(Amount("kin"), HistoryRules.OpKin); Assert.Equal(Amount("marriage"), HistoryRules.OpMarriage); Assert.Equal(Amount("same_religion"), HistoryRules.OpSameReligion);
        Assert.Equal(Amount("heresy"), HistoryRules.OpHeresy); Assert.Equal(Amount("excommunicated"), HistoryRules.OpExcommunicated); Assert.Equal(Amount("war"), HistoryRules.OpWar);
        var fam = Ravensguard.Sim.Family.FamilyContent.From(db);
        for (int age = 0; age < 110; age++) Assert.Equal(fam.OldAgePct(age), HistoryRules.OldAgePctYear(age));     // the hero's table is the notables' (overview 8.3)
    }

    // ------------------------------------------------------------------ HR-9: budget

    [Fact]
    public void HR9_TheV1History_FitsTheBudget_AndAddsLittleOverV0()
    {
        long Time(int rules, ulong seed)
        {
            var (c, s) = Inputs(seed, 250, rules);
            HistoryGenerator.Generate(c, s);                                                                // warm the JIT
            var sw = Stopwatch.StartNew(); HistoryGenerator.Generate(c, s); return sw.ElapsedMilliseconds;
        }
        long v0 = 0, v1 = 0;
        foreach (ulong seed in new ulong[] { 1, 1234, 77 }) { long a = Time(0, seed), b = Time(1, seed); v0 += a; v1 += b; output.WriteLine($"seed {seed}: v0 {a} ms, v1 {b} ms"); }
        output.WriteLine($"v0 {v0 / 3} ms, v1 {v1 / 3} ms (mean, 250 years)");
#if DEBUG
        Assert.True(v1 / 3 < 120_000);
#else
        Assert.True(v1 / 3 < 60_000);                                                                        // the reference budget (68 s allowed on the CI box)
        Assert.True((v1 - v0) / 3 < 8_000);                                                                  // v1 adds at most 8 s over v0
#endif
    }

    // ------------------------------------------------------------------ HR-10: determinism of the v1 line

    [Fact]
    public void HR10_TheV1HistoryIsDeterministic_TwoRunsAndAParallelRun_AndMatchesTheHashToolLine()
    {
        var (c, s) = Inputs(42, 250, 1);
        string a = HistoryGenerator.Generate(c, s).ComputeHash();
        string b = null!;
        Parallel.Invoke(() => b = HistoryGenerator.Generate(c, s).ComputeHash(), () => HistoryGenerator.Generate(c, Inputs(43, 60, 1).S));
        Assert.Equal(a, b);
    }
}
