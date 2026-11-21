using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// The shared kit of the Phase 6 overview tests (docs/specs/phase6/00-phase6-overview.md section 4): a new game of seed 1234 on the hero desk (every step a command through <see cref="HeroDesk"/>, applied at the
/// next barrier as the game's are), the links the game attaches to the runtime (talk, law with its heist, bond and faith-politics links, family, contracts) and a save-at-a-barrier helper. Same shape as
/// <c>Phase5OverviewTests</c>.
/// </summary>
internal static class P6Game
{
    public const string Hash = "h";

    public static (SeamWorld W, WorldState State) Game(ulong seed = 1234)
    {
        var w = SeamWorld.Get(seed);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    /// <summary>What the street view attaches at a game's start: the talk, law (with its heist, bond and faith-politics links), family and contracts links.</summary>
    public static FamilyLink AttachGameLinks(WorldRuntime rt, ContentDatabase content, bool talk = true)
    {
        if (talk) TalkLink.Attach(rt, LineBook.From(content));
        LawLink.Attach(rt);
        var family = FamilyLink.Attach(rt);
        ContractLink.Attach(rt);
        return family;
    }

    public static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    public static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        return d;
    }).GetAwaiter().GetResult();

    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static Dictionary<string, string>? _en;
    /// <summary>The English of src/Ravensguard.Game/localization/en.csv (key to text), as the game's tr() reads it.</summary>
    public static Dictionary<string, string> English()
    {
        if (_en != null) return _en;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv")))
        {
            int c = line.IndexOf(',');
            if (c <= 0) continue;
            string v = line[(c + 1)..];
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\"\"", "\"");
            d[line[..c]] = v;
        }
        return _en = d;
    }
    public static string Tr(string k) => English().TryGetValue(k, out var v) ? v : k;
}

/// <summary>The hero's desk on a runtime stepped on the test's thread (every command applied at the next barrier, as the game's are).</summary>
internal sealed class P6Play(WorldRuntime rt, HeroContext ctx)
{
    public readonly HeroDesk Desk = new();
    public WorldRuntime Rt => rt;
    public HeroContext Ctx => ctx;
    public HeroResult Do(HeroCommand c)
    {
        var cmd = Desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
        Desk.ApplyDue(ctx, rt.HourDone + 1);
        return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
    }
    public long Sec => (rt.HourDone + 1) * 3600;
    public int Day => (int)(rt.HourDone / 24);
    public void Days(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
    public void Hours(int hours) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + hours));
    /// <summary>Runs to a given game hour (when it is still ahead).</summary>
    public void To(long hour) { if (hour > rt.HourDone) rt.AdvanceTo(GameTime.FromGameHours(hour)); }
}
