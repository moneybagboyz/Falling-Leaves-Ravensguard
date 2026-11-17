using System;
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
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>Where the street view's world comes from: the test hamlet, or the generated region streamed chunk by chunk (plan 2.2, 2.3).</summary>
public abstract class StreetSource
{
    /// <summary>The live world, with the hero at <paramref name="start"/> (world cells for a region, hamlet cells for the hamlet) or at the source's own default.</summary>
    public abstract LiveWorld CreateWorld(Cell3? start, GameTime time);
    public abstract Cell3 DefaultStart { get; }
    public abstract string Name { get; }

    public static StreetSource Hamlet() => new HamletSource();

    /// <summary>The generated region as a street source. Chunks generate on first visit; pass the save's chunk store to resume where the world was left.</summary>
    public static StreetSource ForRegion(RegionData region, Population population, ContentDatabase content, ChunkStore? store = null, Cell3? spawn = null, string? poi = null)
        => new RegionSource(region, population, content, store, spawn) { Poi = poi };

    /// <summary>Dev: builds continent, history, embark region and population from a seed (seconds), then streams it.</summary>
    public static StreetSource FromSeed(ulong seed, string? poi = null)
    {
        var dir = StreetView.ContentDir();
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir]);
        if (!load.Success) throw new InvalidOperationException("content failed to load: " + load.Report);
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var region = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        var pop = Populator.Populate(region, h, load.Database);
        GD.Print($"[street] region for seed {seed}: cell ({pick.CellX},{pick.CellY}), {region.Towns.Count()} towns, {region.Villages.Count()} villages, pop {pop.People.Count}, built in {sw.ElapsedMilliseconds} ms");
        return new RegionSource(region, pop, load.Database, null, null) { Poi = poi };
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
        public ChunkStreamer? Streamer { get; private set; }

        public RegionSource(RegionData region, Population pop, ContentDatabase content, ChunkStore? store, Cell3? spawn)
        { _region = region; _pop = pop; _content = content; _store = store; _spawn = spawn; }

        public override string Name => $"region {_region.Seed}";
        public override Cell3 DefaultStart => _spawn ?? _found ?? new Cell3(RegionData.Meters / 2, RegionData.Meters / 2, 0);

        public override LiveWorld CreateWorld(Cell3? start, GameTime time)
        {
            var gen = ChunkGenerator.For(_region, _pop, _content);
            Streamer = new ChunkStreamer(gen, _store);
            var spot = start ?? _spawn;
            if (spot == null)
            {
                spot = Poi != null && Poi.StartsWith("at:", StringComparison.Ordinal) ? RegionSpawn.AtSettlement(Streamer, _region, Poi[3..]) : Poi switch
                {
                    "village" => RegionSpawn.VillageEdge(Streamer, _region),
                    "river" => RegionSpawn.RiverCrossing(Streamer, _region),
                    _ => RegionSpawn.TownSquare(Streamer, _region),
                } ?? RegionSpawn.TownSquare(Streamer, _region);
                if (spot == null) throw new InvalidOperationException("no town or village to start in");
            }
            _found = spot;
            return new LiveWorld(Streamer, spot.Value, time);
        }
    }
}
