using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// Site generation on first visit (exploration-and-sites 4.1): a pure function of (world seed, site id, level, content hash). Each level is room-and-corridor (a binary partition
/// of the footprint, rooms chosen by weight from the template, corridors 1-2 wide with a few loops), cellular-automaton rock with chambers (caves) or drifts with props and stopes
/// (mines); portals put each level in reach of the one above; a monster lair gets its fixed room on the deepest level. Then the history fills it: loot from the budget, the named
/// dead, inhabitants, hazards and traps. Stream 0x4B0 lays out (key site, level), 0x4B1 rolls loot (site, room), 0x4B2 the inhabitants (site, serial), 0x4B3 the hazard zones.
/// Generation runs level by level (<see cref="Begin"/> / <see cref="Job.Step"/>) so the game can spread it over frames (4 ms a frame, 30 ms a level).
/// </summary>
public static partial class SiteGen
{
    public const int MinSize = 40, MaxSize = 72;

    /// <summary>Generates the whole site at once.</summary>
    public static SiteMap Generate(SiteContent c, SiteHistoryInfo h, ulong worldSeed)
    {
        var job = Begin(c, h, worldSeed);
        while (!job.Step()) { }
        return job.Result!;
    }

    public static Job Begin(SiteContent c, SiteHistoryInfo h, ulong worldSeed) => new(c, h, worldSeed);

    /// <summary>A generation in progress: <see cref="Step"/> lays out one level per call, then fills the site (loot, people, hazards, traps) in the last call.</summary>
    public sealed partial class Job
    {
        private readonly SiteContent _c;
        private readonly SiteHistoryInfo _h;
        private readonly SiteTemplateDef _t;
        private readonly ulong _seed;
        private SiteMap? _m;
        private int _next;
        private readonly List<List<string>> _plan = [];
        public SiteMap? Result { get; private set; }
        public int LevelsDone => _next;
        public int LevelCount { get; }

        internal Job(SiteContent c, SiteHistoryInfo h, ulong worldSeed)
        {
            _c = c; _h = h;
            _t = c.TemplateFor(h.Kind.ToString(), h.MonsterLair ? "lair" : h.Sub, h.Kind == SiteKind.Mine ? h.Mine.ToString().ToLowerInvariant() : null, h.LairKind)
                ?? throw new InvalidOperationException($"no site template for {h.Kind} {h.Sub}");
            _seed = worldSeed ^ 0x5173_0000_0000_0000UL;
            int lo = Math.Max(1, _t.Levels[0]), hi = Math.Max(lo, _t.Levels.Count > 1 ? _t.Levels[1] : lo);
            if (_t.Layout == "surface") { lo = hi = 1; }
            LevelCount = h.Opened ? Math.Clamp(h.Levels, 1, MineSites.MaxLevels) : Math.Clamp(h.Levels, lo, hi);       // P8-LM-3: an opened mine has a level for each 10 m of depth
        }

        private RngStream Rng(long level, uint start = 0) => new(_seed, level, _h.SiteId, RngStreams.SiteLayout, start);

        /// <summary>Lays out the next level; true when the site is complete.</summary>
        public bool Step()
        {
            if (Result != null) return true;
            if (_m == null) { Init(); return false; }
            if (_next < LevelCount) { LayOut(_next); _next++; return false; }
            Fill();
            Result = _m;
            return true;
        }

        private void Init()
        {
            var r = Rng(1000);
            int lo = Math.Clamp(_t.SizeTiles[0], MinSize, MaxSize), hi = Math.Clamp(_t.SizeTiles.Count > 1 ? _t.SizeTiles[1] : lo, lo, MaxSize);
            int size = lo + r.Range(0, hi - lo + 1);
            int n = size * size * LevelCount;
            _m = new SiteMap
            {
                SiteId = _h.SiteId, Seed = _seed, Template = _t.Id, ContentHash = _c.Hash, W = size, H = size, Levels = LevelCount,
                Tiles = new SiteTile[n], Air = new ushort[n], WaterCm = new ushort[n], Room = new short[n], Arrival = new int[LevelCount], FloodCm = new int[LevelCount],
            };
            Array.Fill(_m.Air, (ushort)1000);
            Array.Fill(_m.Room, (short)-1);
            _m.Taboo = _t.Taboo; _m.Lair = _h.MonsterLair; _m.Walls = _t.Walls;
            PlanRooms(r);
        }

        /// <summary>Which rooms go on which level: the required on the first, the lair on the deepest, the weighted picks spread over all.</summary>
        private void PlanRooms(RngStream r)
        {
            for (int l = 0; l < LevelCount; l++) _plan.Add([]);
            var req = new List<string>(_t.Rooms.Required.Where(x => x != "stairs_down"));   // every level's arrival is a stairs room already
            if (_h.Kind == SiteKind.Barrow && _h.Dead > 0 && !req.Contains("mass_grave") && _c.Rooms.ContainsKey("mass_grave")) req.Add("mass_grave");
            for (int i = 0; i < req.Count; i++) _plan[Math.Min(LevelCount - 1, i == 0 ? 0 : (i - 1) % LevelCount)].Add(req[i]);
            int total = _t.Rooms.Min + r.Range(0, Math.Max(1, _t.Rooms.Max - _t.Rooms.Min + 1));
            int wsum = _t.Rooms.Weighted.Sum(w => w.Weight);
            for (int k = req.Count; k < total && wsum > 0; k++)
            {
                int roll = r.Range(0, wsum), acc = 0; string pick = _t.Rooms.Weighted[0].Room;
                foreach (var w in _t.Rooms.Weighted) { acc += w.Weight; if (roll < acc) { pick = w.Room; break; } }
                _plan[k % LevelCount].Add(pick);
            }
            if (_h.MonsterLair && _t.Rooms.Lair is { } lair) _plan[LevelCount - 1].Insert(0, lair);
            // every level gets at least two rooms besides its arrival so there is somewhere to go
            for (int l = 0; l < LevelCount; l++)
                while (_plan[l].Count < 2 && _t.Rooms.Weighted.Count > 0) _plan[l].Add(_t.Rooms.Weighted[r.Range(0, _t.Rooms.Weighted.Count)].Room);
        }
    }
}
