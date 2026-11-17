using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.History;

public class HistoryTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 1234];

    private static (Continent C, HistorySettings S) Inputs(ulong seed)
    {
        var cs = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(ContinentSettings.FindBaseContent());
        hs.Seed = seed; hs.Years = 250;
        return (ContinentGenerator.Generate(cs), hs);
    }

    private static readonly Dictionary<ulong, WorldHistory> Cache = [];
    private static WorldHistory Get(ulong seed)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(seed, out var h))
            {
                var (c, s) = Inputs(seed);
                Cache[seed] = h = HistoryGenerator.Generate(c, s);
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
        Assert.True(h.Realms.Any(r => r.Alive));
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
}
