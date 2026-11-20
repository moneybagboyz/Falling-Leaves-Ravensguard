using Ravensguard.Content;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.WorldTool;

/// <summary>
/// `hashes [--seeds 1,1234]`: prints one line per generated layer (continent, history, region, people, sample chunks, road gates, a 2 x 2 block of regions)
/// for each seed. CI runs it on Linux and Windows and fails if the outputs differ (plan §2.2, §5 Phase 1 done-when:
/// "the same seed gives the same continent, history and region hashes on Windows and Linux").
/// </summary>
public static class HashesCommand
{
    public static int Run(string[] args)
    {
        var seeds = (Opt(args, "--seeds") ?? "1,1234").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ulong.Parse).ToArray();
        var baseDir = BaseContent.Require();
        var db = new ModLoader(ContentTypeRegistry.Default).Load([baseDir]).Database;
        foreach (var seed in seeds)
            foreach (var line in Lines(seed, baseDir, db)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>The lines printed for one seed, in order: continent, history, embark pick, region, landmarks, weather, codex, people and four sample chunks. The golden-hash tests compare these.</summary>
    public static List<string> Lines(ulong seed, string baseDir, ContentDatabase db)
    {
        var lines = new List<string>();
        var wset = WeatherSettings.LoadDefaults(baseDir);
        var cs = ContinentSettings.LoadDefaults(baseDir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(baseDir); hs.Seed = seed; // every layer takes the world seed, as the game and the tests do
        var rs = RegionSettings.LoadDefaults(baseDir); rs.Seed = seed;
        var continent = ContinentGenerator.Generate(cs);
        lines.Add($"seed {seed} continent {continent.ComputeHash()}");
        var history = HistoryGenerator.Generate(continent, hs);
        lines.Add($"seed {seed} history {history.ComputeHash()}");
        var pick = EmbarkSite.AutoPick(continent, history);
        lines.Add($"seed {seed} embark {pick.CellX},{pick.CellY} score {pick.Total}");
        var region = RegionGenerator.Generate(continent, history, pick.CellX, pick.CellY, rs);
        lines.Add($"seed {seed} region {region.ComputeHash()}");
        var marks = LandmarkPlacer.Place(region, history, db);
        region.Landmarks = marks;
        lines.Add($"seed {seed} landmarks {marks.Count} {LandmarkPlacer.ComputeHash(marks)}");
        var weather = RegionWeather.ForContinent(continent, seed, wset);
        lines.Add($"seed {seed} weather {weather.StandardSampleHash(pick.CellX, pick.CellY)}");
        lines.Add($"seed {seed} codex {CodexBook.Build(history, region).ComputeHash()}");
        var pop = Populator.Populate(region, history, db);
        lines.Add($"seed {seed} people {pop.ComputeHash()}");
        // a few chunks: the largest town's centre and three fixed spots across the region
        var gen = ChunkGenerator.For(region, pop, db);
        var town = region.Towns.OrderByDescending(t => t.TargetPop).FirstOrDefault();
        var spots = new List<(int X, int Y)> { (100, 100), (500, 500), (900, 300) };
        if (town != null) spots.Insert(0, (town.CenterX / ChunkGenerator.Size, town.CenterY / ChunkGenerator.Size));
        foreach (var (x, y) in spots)
            lines.Add($"seed {seed} chunk {x},{y} {gen.GenerateLevels(x, y).ContentHash}");
        // the seamless continent: the gates of the continental road network, and the 2 x 2 block of region squares whose north-west square is the embark square
        // (clamped to fit the grid): each region's own hashes, so CI's Windows/Linux compare covers neighbours generated from the continent and history alone
        var gates = BorderGates.For(history);
        lines.Add($"seed {seed} gates {gates.Total} {gates.ComputeHash()}");
        var embark = RegionCoord.Snap(pick.CellX, pick.CellY);
        var nw = new RegionCoord(Math.Min(embark.X, RegionCoord.Grid - 2), Math.Min(embark.Y, RegionCoord.Grid - 2));
        for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                var rc = nw.Offset(dx, dy);
                var r = RegionGenerator.Generate(continent, history, rc, rs);
                var m = LandmarkPlacer.Place(r, history, db);
                r.Landmarks = m;
                var p = Populator.Populate(r, history, db);
                lines.Add($"seed {seed} block {rc} region {r.ComputeHash()} landmarks {m.Count} {LandmarkPlacer.ComputeHash(m)} people {p.ComputeHash()}");
            }
        // Phase 4 (ML-15, OV-1): a seeded 10 v 10 skirmish with spears, shields, a bow, maces and axes, 600 fight ticks: the same FightHash on Windows and Linux
        var fight = Ravensguard.Sim.Combat.Melee2.FightCalibration.Skirmish(new Ravensguard.Sim.Combat.Melee2.FightContent(new Ravensguard.Sim.Combat.CombatTables(db), db), seed, 10);
        for (int t = 0; t < 600; t++) fight.StepWithAi();
        lines.Add($"seed {seed} fight {fight.Hash():X16} active {fight.Fighters.Count(f => f.Active)}");
        // Phase 4 close-out (morale-capture-succession 6): the CaptiveHash of a band's captive and an army's after twelve game days of the hourly step, and the EstateHash of a settled estate
        lines.Add($"seed {seed} captive {CaptiveHash(seed, db):X16} estate {EstateHash(seed):X16}");
        // Phase 5 review (OV-6's cross-OS part, VD-20): the boats' draft, speeds, capsize rolls (0x550), swamping (0x551), fishing, the ferry and the barge legs' delays (0x552)
        lines.Add($"seed {seed} vehicles {Ravensguard.Sim.Carts.VehicleHash.Of(seed)}");
        return lines;
    }

    /// <summary>Two captives (a band's, an army's) taken at day 10 and stepped hourly for twelve days with no ledger: the hourly step's guards, transfers, renegotiation and release.</summary>
    static ulong CaptiveHash(ulong seed, ContentDatabase db)
    {
        var r = Ravensguard.Sim.Combat.Aftermath.AftermathRules.For(db);
        var book = new Ravensguard.Sim.Combat.Aftermath.CaptiveBook(seed);
        long sec = 10L * 86400 + 9 * 3600;
        var band = new Ravensguard.Sim.Combat.Aftermath.PurseRef(Ravensguard.Sim.Economy.PurseKind.Household, EntityId.Make(IdKind.Other, 77));
        book.Capture(r, Ravensguard.Sim.Combat.Aftermath.CaptorKind.Bandits, EntityId.Make(IdKind.Person, 1), true, EntityId.Make(IdKind.Other, 77), band, sec, default, 1500,
            Ravensguard.Sim.Combat.Aftermath.PayerKind.Friends, [], [], (30, 30, 20, 40, 25), rescueMembers: 1);
        book.Capture(r, Ravensguard.Sim.Combat.Aftermath.CaptorKind.Army, EntityId.Make(IdKind.Person, 2), false, EntityId.Make(IdKind.Other, 78), band, sec, default, 4000,
            Ravensguard.Sim.Combat.Aftermath.PayerKind.Household, [], [], (20, 40, 10, 30, 30));
        for (long h = sec / 3600 + 1; h <= sec / 3600 + 12 * 24; h++) book.StepHour(r, null, h);
        return book.Hash();
    }

    /// <summary>A hero's estate settled twice (an heir, then nobody: the run ends) through a ledger: the funeral, the inheritance and the records.</summary>
    static ulong EstateHash(ulong seed)
    {
        var book = new Ravensguard.Sim.Succession.SuccessionBook();
        var l = new Ravensguard.Sim.Economy.Ledger();
        var hero = EntityId.Make(IdKind.Person, 1); var heir = EntityId.Make(IdKind.Person, 2);
        var dead = l.Open(Ravensguard.Sim.Economy.PurseKind.Person, hero); var heirs = l.Open(Ravensguard.Sim.Economy.PurseKind.Person, heir);
        var church = l.Open(Ravensguard.Sim.Economy.PurseKind.Settlement, EntityId.Make(IdKind.Settlement, 1));
        l.Mint(dead, Ravensguard.Sim.Economy.Coin.FromMites(10_000 + (long)(seed % 1000)), Ravensguard.Sim.Economy.SourceReason.Seed);
        var cand = new Ravensguard.Sim.Succession.SuccessionCandidate(heir, 5, Ravensguard.Sim.Succession.SuccessionRule.Sibling, 22, "heir");
        Ravensguard.Sim.Succession.SuccessionOrder.Settle(book, l, hero, dead, cand, heirs, church, [EntityId.Make(IdKind.Item, 9)], [], 40, 1000, 2000, "arming_sword");
        Ravensguard.Sim.Succession.SuccessionOrder.Settle(book, l, heir, heirs, null, heirs, heirs, [], [], 70);
        return book.Hash();
    }

    static string? Opt(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }
}
