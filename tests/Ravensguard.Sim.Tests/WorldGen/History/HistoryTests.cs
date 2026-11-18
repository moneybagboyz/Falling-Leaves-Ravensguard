using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.History;

public class HistoryTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 1234];
    private static readonly ulong[] MoreSeeds = [7, 42];

    private static (Continent C, HistorySettings S) Inputs(ulong seed)
    {
        var cs = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(ContinentSettings.FindBaseContent());
        hs.Seed = seed; hs.Years = 250;
        return (ContinentGenerator.Generate(cs), hs);
    }

    private static readonly Dictionary<ulong, Continent> ContCache = [];
    private static Continent Cont(ulong seed)
    {
        lock (ContCache)
        {
            if (!ContCache.TryGetValue(seed, out var c)) ContCache[seed] = c = Inputs(seed).C;
            return c;
        }
    }

    private static readonly Dictionary<ulong, WorldHistory> Cache = [];
    private static WorldHistory Get(ulong seed)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(seed, out var h))
            {
                var (_, s) = Inputs(seed);
                Cache[seed] = h = HistoryGenerator.Generate(Cont(seed), s);
            }
            return h;
        }
    }

    public static IEnumerable<object[]> SeedCases() => Seeds.Select(s => new object[] { s });

    [Fact]
    public void Defaults_load_from_content_pipeline()
    {
        var s = HistorySettings.LoadDefaults(ContinentSettings.FindBaseContent());
        Assert.Equal(250, s.Years);
        Assert.Equal(10, s.LevyPermille);
        Assert.Equal(60, s.MaxRealms);
        Assert.True(s.FoundSpacingRich < s.FoundSpacing);   // towns sit closer in fertile river lowlands
        Assert.True(s.MinorMaxPop < 500);                   // small places never become towns
        Assert.True(s.TributePct > 0 && s.VassalagePct > 0);
    }

    [Fact]
    public void Same_seed_same_hash_and_timing()
    {
        var (c, s) = Inputs(1234);
        var sw = Stopwatch.StartNew();
        var a = HistoryGenerator.Generate(c, s);
        sw.Stop();
        var b = HistoryGenerator.Generate(c, s);
        output.WriteLine($"250 years: {sw.ElapsedMilliseconds} ms (incl. JIT), {a.Chronicle.Count} events, hash {a.ComputeHash()[..16]}");
#if DEBUG
        Assert.True(sw.ElapsedMilliseconds < 120_000);
#else
        Assert.True(sw.ElapsedMilliseconds < 60_000);
#endif
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        var (c2, s2) = Inputs(2);
        Assert.NotEqual(a.ComputeHash(), HistoryGenerator.Generate(c2, s2).ComputeHash());
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void No_accidental_extinction(ulong seed)
    {
        var h = Get(seed);
        output.WriteLine($"seed {seed}: pop {h.StartPopulation:N0} -> {h.EndPopulation:N0}");
        Assert.True(h.EndPopulation * 100 > h.StartPopulation * 30);
        Assert.Contains(h.Realms, r => r.Alive);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Has_wars_realm_changes_ruins_and_lost_artefacts(ulong seed)
    {
        var h = Get(seed);
        int changes = h.Chronicle.All.Count(e => e.TypeId is "realm_split" or "realm_fell" or "realm_merged");
        output.WriteLine($"seed {seed}: wars {h.Wars.Count}, realm changes {changes}, ruins {h.Settlements.Count(s => s.IsRuin)}, " +
                         $"artefacts {h.Artefacts.Count} (lost {h.Artefacts.Count(a => a.Status == ArtefactStatus.Lost)}, buried {h.Artefacts.Count(a => a.Status == ArtefactStatus.Buried)}), events {h.Chronicle.Count}");
        Assert.True(h.Wars.Count >= 3);
        Assert.True(changes >= 1);
        Assert.Contains(h.Settlements, s => s.IsRuin);
        Assert.Contains(h.Artefacts, a => a.Status == ArtefactStatus.Lost);
        Assert.NotEmpty(h.Battlefields);
        Assert.NotEmpty(h.TradeRoutes);
        Assert.NotEmpty(h.Dynasties);
        Assert.NotEmpty(h.Grudges);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Every_ruin_has_a_cause_event(ulong seed)
    {
        var h = Get(seed);
        foreach (var s in h.Settlements.Where(s => s.IsRuin))
        {
            var ruin = h.Chronicle.Get(s.RuinEvent);
            var cause = h.Chronicle.Get(s.RuinCauseEvent);
            Assert.True(ruin != null, $"{s.Name}: ruin event missing");
            Assert.True(cause != null, $"{s.Name}: cause event missing");
            Assert.NotEmpty(ruin!.Causes);
            Assert.True(cause!.Id.Counter <= ruin.Id.Counter, "a cause cannot come after its effect");
            Assert.NotEqual("", s.RuinKind);
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Every_war_has_a_casus_belli_event(ulong seed)
    {
        var h = Get(seed);
        foreach (var w in h.Wars)
        {
            var cb = h.Chronicle.Get(w.CbEvent);
            var decl = h.Chronicle.Get(w.DeclEvent);
            Assert.True(cb != null, $"{w.Name}: casus belli event missing");
            Assert.NotEqual(CasusBelliKind.None, w.Cb);
            Assert.NotNull(decl);
            Assert.Contains(decl!.Causes, c => c.EventId == w.CbEvent);
            Assert.True(cb!.When <= decl.When);
            if (!w.Alive) Assert.NotNull(h.Chronicle.Get(w.EndEvent));
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Every_cause_id_resolves_and_precedes_its_effect(ulong seed)
    {
        var h = Get(seed);
        GameTime last = default;
        foreach (var e in h.Chronicle.All)
        {
            Assert.True(e.When >= last, "chronicle is time ordered");
            last = e.When;
            Assert.NotEmpty(e.Causes);
            foreach (var c in e.Causes.Where(c => c.EventId != null))
            {
                var target = h.Chronicle.Get(c.EventId!.Value);
                Assert.True(target != null, $"event {e.Id} ({e.TypeId}) cites missing cause {c.EventId}");
                Assert.True(target!.Id.Counter < e.Id.Counter);
            }
        }
        foreach (var b in h.Battlefields) Assert.NotNull(h.Chronicle.Get(b.Event));
        foreach (var g in h.Grudges) Assert.NotNull(h.Chronicle.Get(g.CauseEvent));
        foreach (var a in h.Artefacts.Where(a => a.Status != ArtefactStatus.Held)) Assert.NotNull(h.Chronicle.Get(a.LossEvent));
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Notables_are_consistent(ulong seed)
    {
        var h = Get(seed);
        foreach (var n in h.Notables)
        {
            if (!n.Alive) Assert.True(n.DeathYear >= n.BirthYear, $"{n.Name}: died {n.DeathYear} before birth {n.BirthYear}");
            Assert.NotEmpty(n.Name);
        }
        foreach (var r in h.Realms.Where(r => r.Alive))
        {
            var ruler = h.GetNotable(r.Ruler);
            Assert.NotNull(ruler);
            Assert.True(ruler!.Alive, $"{r.Name} is ruled by the dead {ruler.Name} ({ruler.DeathCause}, y{ruler.DeathYear}, role {ruler.Role}, ev {h.Chronicle.Get(ruler.DeathEvent)?.TypeId})");
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Cause_coverage_is_high(ulong seed)
    {
        var h = Get(seed);
        double cov = h.Chronicle.CauseCoverage();
        output.WriteLine($"seed {seed}: cause coverage {cov:P1} over {h.Chronicle.Count} events");
        Assert.True(cov >= 0.9);
    }

    [Fact]
    public void Legends_read_as_text()
    {
        var h = Get(1234);
        var battle = h.Chronicle.ByType("battle").First();
        string line = h.LegendLine(battle);
        output.WriteLine(line);
        Assert.Contains("The Battle of", line);
        Assert.Contains("Cause:", line);
        Assert.Contains($"(event #{battle.Id.Counter})", line);
    }

    // ---------- plan numbers (docs/REDESIGN_PLAN.md §1, §3b, §1A Stage 2) ----------

    [Theory, MemberData(nameof(SeedCases))]
    public void Wars_per_250_years_are_in_range(ulong seed)
    {
        var h = Get(seed);
        output.WriteLine($"seed {seed}: wars {h.Wars.Count} (naval {h.Wars.Count(w => w.Naval)}), battlefields {h.Battlefields.Count}");
        Assert.InRange(h.Wars.Count, 25, 45);
        Assert.True(h.Battlefields.Count >= 60);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Battle_and_war_names_vary(ulong seed)
    {
        var h = Get(seed);
        var battles = h.Battlefields.Select(b => b.Name).ToList();
        int distinct = battles.Distinct().Count();
        var words = battles.Select(n => n.Split(' ')[0]).GroupBy(w => w).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        output.WriteLine($"seed {seed}: {battles.Count} battlefields, {distinct} distinct names, most common first word used {words}x; e.g. {string.Join("; ", battles.Take(8))}");
        Assert.Equal(battles.Count, distinct);
        Assert.True(words * 12 < battles.Count, "one adjective or place name dominates the battle names");
        var wars = h.Wars.Select(w => w.Name).ToList();
        Assert.Equal(wars.Count, wars.Distinct().Count());
        Assert.True(wars.Select(n => n.Split('(')[0].Trim()).Distinct().Count() * 10 >= wars.Count * 8, "war names repeat too often");
    }

    [Fact]
    public void Treaties_go_beyond_truce_and_marriage()
    {
        var kinds = Seeds.SelectMany(sd => Get(sd).Treaties).Select(t => t.Kind).Distinct().ToList();
        output.WriteLine("treaty kinds: " + string.Join(", ", kinds));
        Assert.Contains(TreatyKind.Tribute, kinds);
        Assert.Contains(TreatyKind.Vassalage, kinds);
        foreach (var sd in Seeds)
        {
            var h = Get(sd);
            foreach (var t in h.Treaties.Where(t => t.Kind is TreatyKind.Tribute or TreatyKind.Vassalage))
            {
                var ev = h.Chronicle.Get(t.Event);
                Assert.NotNull(ev);
                Assert.Contains(ev!.Causes, c => c.EventId != null);   // a treaty follows a peace that follows a war
                Assert.True(t.ExpiryTick > t.StartTick);
            }
        }
    }

    [Fact]
    public void Plague_and_war_empty_small_places_and_every_ruin_says_why()
    {
        var ruins = Seeds.SelectMany(sd => Get(sd).Settlements.Where(s => s.IsRuin)).ToList();
        var kinds = ruins.GroupBy(r => r.RuinKind).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine("ruin kinds: " + string.Join(", ", kinds.Select(k => $"{k.Key}={k.Value}")));
        Assert.True(kinds.GetValueOrDefault("plague") >= 1, "plague never ruined a village");
        Assert.True(kinds.GetValueOrDefault("sacked") + kinds.GetValueOrDefault("razed") >= 5);
        Assert.True(kinds.GetValueOrDefault("famine") >= 1);
        Assert.True(kinds.Count >= 5);
        Assert.Contains(ruins, r => r.Manor && r.RuinKind is "sacked" or "abandoned");
        foreach (var sd in Seeds)
        {
            var h = Get(sd);
            foreach (var r in h.Settlements.Where(r => r.IsRuin))
            {
                Assert.NotNull(h.Chronicle.Get(r.RuinEvent));
                Assert.NotNull(h.Chronicle.Get(r.RuinCauseEvent));
            }
        }
    }

    [Fact]
    public void Island_realms_can_raid_across_the_sea()
    {
        // An archipelago: realms on different islands share no land border, so any war between them is a sea raid.
        int naval = 0, islandRealmsWithContact = 0;
        foreach (ulong seed in new ulong[] { 3, 11 })
        {
            var cs = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
            cs.Seed = seed; cs.Layout = ContinentLayout.Archipelago;
            var hs = HistorySettings.LoadDefaults(ContinentSettings.FindBaseContent());
            hs.Seed = seed; hs.Years = 250;
            var c = ContinentGenerator.Generate(cs);
            var h = HistoryGenerator.Generate(c, hs);
            naval += h.Wars.Count(w => w.Naval);
            islandRealmsWithContact += h.Chronicle.All.Count(e => e.TypeId == "border_raid");
            foreach (var w in h.Wars.Where(w => w.Naval))
            {
                var decl = h.Chronicle.Get(w.DeclEvent)!;
                Assert.Contains("ships", decl.Data["text"]);
                Assert.NotNull(h.Chronicle.Get(w.CbEvent));
            }
            output.WriteLine($"archipelago seed {seed}: wars {h.Wars.Count}, naval {h.Wars.Count(w => w.Naval)}, realms {h.Realms.Count}");
        }
        Assert.True(naval >= 1, "no island realm ever fought across the water");
        Assert.True(islandRealmsWithContact >= 1);
    }

    // ---------- the embark region the history feeds (plan §1, §3b) ----------

    private sealed record Plan(EmbarkScore Pick, EmbarkScore Window, Region R, long RuralIntended);

    private static readonly Dictionary<ulong, Plan> PlanCache = [];

    /// <summary>
    /// The window the picker would choose if it preferred plan-sized regions (3-4 towns): the best-scoring window with
    /// 3-4 towns. <see cref="EmbarkSite.AutoPick"/> itself is checked separately.
    /// </summary>
    private static Plan PlanRegion(ulong seed)
    {
        lock (PlanCache)
        {
            if (PlanCache.TryGetValue(seed, out var p)) return p;
            var dir = ContinentSettings.FindBaseContent();
            var c = Cont(seed); var h = Get(seed);
            var pick = EmbarkSite.AutoPick(c, h);
            EmbarkScore? best = null;
            for (int cy = 0; cy + EmbarkSite.Window <= Continent.Size; cy += 2)
                for (int cx = 0; cx + EmbarkSite.Window <= Continent.Size; cx += 2)
                {
                    var q = EmbarkSite.Score(c, h, cx, cy);
                    if (!q.Excluded && q.Towns is >= 3 and <= 4 && (best == null || q.Total > best.Total)) best = q;
                }
            Assert.NotNull(best);
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
            var r = RegionGenerator.Generate(c, h, best!.CellX, best.CellY, rs);
            // what the history alone says the window holds: rural folk scaled as the region builder does, plus living settlements
            long rural = 0, settl = 0;
            for (int dy = 0; dy < 16; dy++)
                for (int dx = 0; dx < 16; dx++)
                {
                    int i = Continent.Index(best.CellX + dx, best.CellY + dy);
                    if (!c.Ocean[i]) rural += h.FinalPop[i];
                }
            foreach (var s in h.Settlements)
            {
                int sx = s.Cell % Continent.Size - best.CellX, sy = s.Cell / Continent.Size - best.CellY;
                if (!s.IsRuin && sx is >= 0 and < 16 && sy is >= 0 and < 16) settl += s.Pop;
            }
            return PlanCache[seed] = new Plan(pick, best, r, rural * rs.RuralScalePct / 100 + settl);
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Plan_region_has_three_or_four_towns_with_plan_spacing(ulong seed)
    {
        var p = PlanRegion(seed);
        var towns = p.R.Towns.ToList();
        var nn = towns.Select(t => towns.Where(u => u != t).Min(u => Math.Sqrt(Math.Pow(t.CenterX - u.CenterX, 2) + Math.Pow(t.CenterY - u.CenterY, 2))) / 1000.0).ToList();
        output.WriteLine($"seed {seed} window ({p.Window.CellX},{p.Window.CellY}): towns {string.Join(",", towns.Select(t => t.TargetPop))}, nearest-neighbour km {string.Join(",", nn.Select(d => d.ToString("0.0")))}; auto-pick has {p.Pick.Towns} towns");
        Assert.InRange(towns.Count, 3, 4);
        Assert.All(towns, t => Assert.InRange(t.TargetPop, 700, 3400));
        Assert.True(towns.Count(t => t.TargetPop is >= 1000 and <= 3000) * 3 >= towns.Count * 2, "most towns are 1,000-3,000");
        Assert.Contains(nn, d => d is >= 5.5 and <= 11.5);   // towns of a lowland cluster sit 6-10 km apart (a few hundred metres of slack for where each stands in its 2 km cell)
        Assert.True(nn.OrderBy(d => d).ElementAt(nn.Count / 2) <= 13.0, "typical town spacing is about 6-12 km");
        Assert.All(nn, d => Assert.InRange(d, 4.0, 25.0));
        // the picker's own choice stays plausible: one to four towns, not a hamlet scatter or a city belt
        Assert.InRange(p.Pick.Towns, 1, 4);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Plan_region_population_comes_from_history_not_the_floor(ulong seed)
    {
        var p = PlanRegion(seed);
        var rs = RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        output.WriteLine($"seed {seed}: history says {p.RuralIntended:N0}, region holds {p.R.TotalPopulation:N0}, floor {rs.MinRegionPopulation:N0}");
        Assert.True(p.RuralIntended >= 22_000, "history alone supplies the plan's ~25,000 people");   // the region builder clamps to its 30,000 maximum
        Assert.True(rs.MinRegionPopulation <= 18_000, "the floor is back to a sane safety net");
        Assert.InRange(p.R.TotalPopulation, 20_000, 26_500);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Plan_region_holds_ruins_and_battlefields_from_history(ulong seed)
    {
        var p = PlanRegion(seed); var h = Get(seed);
        var ruins = p.R.Sites.Where(s => s.Kind == SiteKind.Ruin).ToList();
        var fields = p.R.Sites.Where(s => s.Kind == SiteKind.Battlefield).ToList();
        output.WriteLine($"seed {seed}: ruins {ruins.Count} ({string.Join(", ", ruins.GroupBy(r => r.SubKind).Select(g => g.Key + "=" + g.Count()))}), battlefields {fields.Count}, barrows {p.R.Sites.Count(s => s.Kind == SiteKind.Barrow)}");
        Assert.InRange(ruins.Count, 5, 15); // embark regions are squares of the region grid now, so a quiet one (seed 1: 6 ruins) is possible
        Assert.True(fields.Count >= 2);
        Assert.True(ruins.Select(r => r.SubKind).Distinct().Count() >= 2, "ruins come from more than one cause"); // the plan-spacing picker may choose a quiet region (seed 2: seven abandoned villages and a sacked abbey)
        Assert.All(ruins, r => { Assert.NotNull(h.Chronicle.Get(r.HistoryEvent)); Assert.NotNull(h.Chronicle.Get(r.CauseEvent)); });
        Assert.All(fields, f => Assert.NotNull(h.Chronicle.Get(f.HistoryEvent)));
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Auto_pick_is_a_plausible_embark(ulong seed)
    {
        var p = PlanRegion(seed);
        output.WriteLine($"seed {seed}: auto-pick ({p.Pick.CellX},{p.Pick.CellY}) towns {p.Pick.Towns}, villages {p.Pick.Villages}, ruins {p.Pick.Ruins}, pop {p.Pick.Population:N0}");
        Assert.False(p.Pick.Excluded);
        Assert.True(p.Pick.Ruins >= 5);
        Assert.True(p.Pick.Villages >= 5);
    }
}
