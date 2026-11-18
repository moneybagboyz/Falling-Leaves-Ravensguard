using System;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>Where the street view's world comes from: the test hamlet, or the generated region streamed chunk by chunk (plan 2.2, 2.3).</summary>
public abstract class StreetSource
{
    /// <summary>The live world, with the hero at <paramref name="start"/> (world cells for a region, hamlet cells for the hamlet) or at the source's own default.</summary>
    public abstract LiveWorld CreateWorld(Cell3? start, GameTime time);
    public abstract Cell3 DefaultStart { get; }
    public abstract string Name { get; }
    /// <summary>The generated region this source streams (null for the test hamlet): the world map and the weather read it.</summary>
    public virtual RegionData? Region => null;
    /// <summary>The generated regions of the seamless world (null for the test hamlet and a single-region world).</summary>
    public virtual RegionManager? Regions => null;
    /// <summary>The continent of the world, when the source knows it (the world map shows it).</summary>
    public Continent? Continent { get; protected set; }
    /// <summary>The continent weather for the region, when it is already at hand (a seed start); otherwise the view rebuilds it from the region's seed in the background.</summary>
    public virtual Ravensguard.Sim.Weather.RegionWeather? Weather => null;
    /// <summary>The player character and the Codex the street view shows panels for (null for the test hamlet), the world's history for the Codex links, and the content for names.</summary>
    public Ravensguard.Sim.Characters.HeroRecord? Hero { get; set; }
    public Ravensguard.Sim.Codex.CodexBook? Codex { get; set; }
    public WorldHistory? History { get; set; }
    /// <summary>A saved run to resume (hero position, clock, torch, explored map, Codex progress); null starts fresh at the spawn.</summary>
    public Ravensguard.Sim.Live.LiveResume? Resume { get; set; }
    public ContentDatabase? Content { get; protected set; }

    public static StreetSource Hamlet() => new HamletSource();

    /// <summary>The generated region as a street source. Chunks generate on first visit; pass the save's chunk store to resume where the world was left.</summary>
    public static StreetSource ForRegion(RegionData region, Population population, ContentDatabase content, ChunkStore? store = null, Cell3? spawn = null, string? poi = null,
        Ravensguard.Sim.Characters.HeroRecord? hero = null, Ravensguard.Sim.Codex.CodexBook? codex = null, WorldHistory? history = null, Continent? continent = null,
        Ravensguard.Sim.Live.LiveResume? resume = null, RegionSettings? regionSettings = null)
        => new RegionSource(region, population, content, store, spawn)
        {
            Poi = poi, Hero = hero, Codex = codex, History = history, Resume = resume,
            // with the continent and history at hand the neighbours generate as the hero nears an edge (seamless continent, plan 1B)
            RegionsInit = continent != null && history != null ? MakeRegions(continent, history, region, population, content, regionSettings) : null,
            ContinentInit = continent,
            // the world's own (possibly customised) continent drives the weather, not one regenerated from default settings
            WeatherInit = continent == null ? null : Ravensguard.Sim.Weather.RegionWeather.ForContinent(continent, region.Seed, Ravensguard.Sim.Weather.WeatherSettings.LoadDefaults(StreetView.ContentDir())),
        };

    static RegionManager MakeRegions(Continent continent, WorldHistory history, RegionData region, Population population, ContentDatabase content, RegionSettings? settings)
    {
        var rs = settings ?? RegionSettings.LoadDefaults(StreetView.ContentDir());
        rs.Seed = region.Seed;
        var m = new RegionManager(continent, history, rs, content);
        m.Adopt(RegionCoord.OfCell(region.CellX, region.CellY), region, population);
        return m;
    }

    /// <summary>Dev: builds continent, history, embark region and population from a seed (seconds), then streams it.</summary>
    public static StreetSource FromSeed(ulong seed, string? poi = null)
    {
        var dir = StreetView.ContentDir();
        var load = new ModLoader(ContentTypeRegistry.Default).Load(Ravensguard.Game.Tools.BlueprintEditor.EditorLibrary.ModFolders()); // base plus the user blueprint mod (hot reload: read afresh each start)
        if (!load.Success) throw new InvalidOperationException("content failed to load: " + load.Report);
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var region = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        Ravensguard.Sim.WorldGen.Region.LandmarkPlacer.Place(region, h, load.Database);
        var pop = Populator.Populate(region, h, load.Database);
        GD.Print($"[street] region for seed {seed}: cell ({pick.CellX},{pick.CellY}), {region.Towns.Count()} towns, {region.Villages.Count()} villages, pop {pop.People.Count}, built in {sw.ElapsedMilliseconds} ms");
        var weather = Ravensguard.Sim.Weather.RegionWeather.ForContinent(c, seed, Ravensguard.Sim.Weather.WeatherSettings.LoadDefaults(dir));
        // dev: a hero from the first origin, and the Codex of this world, so the panels can be opened from a seed start
        var origin = load.Database.All<OriginDef>().OrderBy(o => o.Order).First();
        return new RegionSource(region, pop, load.Database, null, null)
        {
            Poi = poi, WeatherInit = weather, History = h, RegionsInit = MakeRegions(c, h, region, pop, load.Database, rs), ContinentInit = c,
            Hero = Ravensguard.Sim.Characters.HeroRecord.FromOrigin("Aldric Vane", false, origin),
            Codex = Ravensguard.Sim.Codex.CodexBook.Build(h, region, Ravensguard.Game.UI.CodexRules.FromInput()),
        };
    }

    sealed class HamletSource : StreetSource
    {
        BuildingLibrary? _lib;
        public override string Name => "hamlet";
        public override Cell3 DefaultStart => HamletBuilder.HeroStart;
        public override LiveWorld CreateWorld(Cell3? start, GameTime time)
        {
            _lib ??= StreetView.LoadLibrary();
            var map = HamletBuilder.Build(_lib);
            return new LiveWorld(map, start ?? DefaultStart, time);
        }
    }

    sealed class RegionSource : StreetSource
    {
        readonly RegionData _region;
        readonly Population _pop;
        readonly ContentDatabase _content;
        readonly ChunkStore? _store;
        readonly Cell3? _spawn;
        Cell3? _found;
        public string? Poi { get; init; }
        public RegionManager? RegionsInit { get => _regions; init => _regions = value; }
        public Continent? ContinentInit { init => Continent = value; }
        RegionManager? _regions;
        public override RegionManager? Regions => _regions;
        public override RegionData? Region => _region;
        public override Ravensguard.Sim.Weather.RegionWeather? Weather => _weather;
        Ravensguard.Sim.Weather.RegionWeather? _weather;
        public Ravensguard.Sim.Weather.RegionWeather? WeatherInit { init => _weather = value; }
        public ChunkStreamer? Streamer { get; private set; }

        public RegionSource(RegionData region, Population pop, ContentDatabase content, ChunkStore? store, Cell3? spawn)
        { _region = region; _pop = pop; _content = content; _store = store; _spawn = spawn; Content = content; }

        public override string Name => $"region {_region.Seed}";
        public override Cell3 DefaultStart => _spawn ?? _found ?? new Cell3(RegionData.Meters / 2, RegionData.Meters / 2, 0);

        public override LiveWorld CreateWorld(Cell3? start, GameTime time)
        {
            var gen = ChunkGenerator.For(_region, _pop, _content);
            Streamer = Regions != null ? new ChunkStreamer(Regions, RegionCoord.OfCell(_region.CellX, _region.CellY), _store) : new ChunkStreamer(gen, _store);
            if (Resume is { } r)
            {
                _found = r.Cell; // the hero stands exactly where the save left it, with its clock, torch, explored map and Codex
                return new LiveWorld(Streamer, r, codex: Codex);
            }
            var spot = start ?? _spawn;
            if (spot == null)
            {
                spot = Poi != null && Poi.StartsWith("at:", StringComparison.Ordinal) ? RegionSpawn.AtSettlement(Streamer, _region, Poi[3..])
                    : Poi != null && Poi.StartsWith("landmark", StringComparison.Ordinal) ? RegionSpawn.AtLandmark(Streamer, _region, Poi.Length > 9 && int.TryParse(Poi[9..], out var li) ? li : -1) : Poi switch
                {
                    "village" => RegionSpawn.VillageEdge(Streamer, _region),
                    "river" => RegionSpawn.RiverCrossing(Streamer, _region),
                    "border" => RegionSpawn.AtBorder(Streamer, _region),
                    _ => RegionSpawn.TownSquare(Streamer, _region),
                } ?? RegionSpawn.TownSquare(Streamer, _region);
                if (spot == null) throw new InvalidOperationException("no town or village to start in");
            }
            _found = spot;
            return new LiveWorld(Streamer, spot.Value, time, codex: Codex);
        }
    }
}
