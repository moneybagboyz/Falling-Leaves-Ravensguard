using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// The construction's place in the abstract tick (spec 4.10): every hour, in the outbox phase (which every hour runs, a coarse catch-up hour included), the crews work the hour's quanta,
/// groans fall, the day's escrow and pay run, and the haul jobs to the piles are brought up to the hour's end. At hour 3 of each day the rust and rot of item rows in stores standing on
/// cells (piles, rubble, chests) whose room is not Indoor (items 4.4). A no-op without a <see cref="ConstructionWorld"/>.
/// </summary>
[AbstractSystem]
public sealed class ConstructionSystem : AbstractSystem
{
    public const int CorrosionHour = 3;
    public ConstructionSystem() : base("construction", TickPhase.H7Outbox, SystemOrder.Outbox + 110) { }

    public override void Step(TickContext ctx)
    {
        var cw = ctx.World.Get<ConstructionWorld>(); if (cw == null) return;
        var ob = ctx.Outbox;
        cw.Chronicle = (when, type, subjects, causes, data) => ob.Chronicle(when, type, subjects, causes, data);
        long end = (ctx.Hour + 1) * 3600;
        cw.AdvanceHauls(end);
        cw.AdvanceTo(end);
        if (ctx.HourOfDay == CorrosionHour) cw.CorrosionDay((int)ctx.Day);
    }
}

/// <summary>Installs construction on the home region (P5-B hook in <see cref="WorldRuntime"/>): the world state's book (the "construction" section), the world in the sim's services, its hooks.</summary>
public static class ConstructionWiring
{
    public static ConstructionWorld Attach(RegionWorld home, WorldState state, ContentDatabase content, RegionWeather? weather)
    {
        var cw = Make(state, content, home.Population, home.Sim.Economy, home.Coord.Id);
        Hook(cw, state, content, home.Sim.Get<LandWorld>(), weather, home.Population, () => home.Sim.Get<WoundLedger>() ?? state.Wounds);
        home.Sim.Set(cw);
        return cw;
    }

    /// <summary>The construction world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static ConstructionWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<ConstructionWorld>() is { } cw) return cw;
        return Loose.GetValue(state, s =>
        {
            var w = Make(s, content, s.Population, s.Economy ?? throw new InvalidOperationException("construction needs the world's economy"), s.HomeRegion.Id);
            Hook(w, s, content, s.HomeRecords?.Sim.Get<LandWorld>() ?? (s.Land != null ? LandWiring.For(s, content) : null), null, s.Population, () => s.Wounds);
            return w;
        });
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, ConstructionWorld> Loose = new();

    private static ConstructionWorld Make(WorldState state, ContentDatabase content, Population pop, EconomyState econ, int region)
    {
        var book = state.Construction ??= new ConstructionBook();
        return new ConstructionWorld(book, econ, PieceCosts.Of(content), region) { Pop = pop, Content = content, Seed = state.Seed, Designs = state.Designs ??= new DesignBook() };   // P5-C: Designs
    }

    private static void Hook(ConstructionWorld cw, WorldState state, ContentDatabase content, LandWorld? land, RegionWeather? weather, Population pop, Func<WoundLedger?> wounds)
    {
        var econ = cw.Econ;
        var hero = EconomyState.HeroOwner;
        if (land != null) HookLand(cw, land, pop);
        var region = pop.Region;
        ChunkGenerator? gen = null;
        cw.Ground = (x, y) =>
        {
            gen ??= ChunkGenerator.For(region, pop, content);
            int tx = Math.Clamp(x / Ravensguard.Sim.WorldGen.Region.Region.TileM, 0, Ravensguard.Sim.WorldGen.Region.Region.Size - 1), ty = Math.Clamp(y / Ravensguard.Sim.WorldGen.Region.Region.TileM, 0, Ravensguard.Sim.WorldGen.Region.Region.Size - 1);
            return (gen.IsRoadCell(x, y), region.SlopePermille[Ravensguard.Sim.WorldGen.Region.Region.Index(tx, ty)]);
        };
        var pal = BuildingPalette(content); var lib = PieceCosts.Of(content).Library;
        var standingCache = new Dictionary<ChunkKey, (int Ops, Ravensguard.Sim.Save.ChunkContent? Content)>();
        cw.Standing = (x, y, z) =>
        {
            // a generated building in a visited chunk (what the chunk store holds; construction's own cells are the book's)
            int cx = ChunkStreamer.FloorDiv(x, ChunkContent.Size), cy = ChunkStreamer.FloorDiv(y, ChunkContent.Size);
            if (state.Chunks.GetSnapshot(new ChunkKey(cx, cy, 0)) == null || z < ChunkKey.MinZ || z > ChunkKey.MaxZ) return false;
            var key = new ChunkKey(cx, cy, z);
            int ops = state.Chunks.DeltaOpCount(key);
            if (!standingCache.TryGetValue(key, out var hit) || hit.Ops != ops) standingCache[key] = hit = (ops, state.Chunks.Rebuild(key));
            if (standingCache.Count > 64) standingCache.Clear();
            var c = hit.Content;
            if (c == null) return false;
            int code = c.Tiles.GetValueOrDefault((y - cy * ChunkContent.Size) * ChunkContent.Size + x - cx * ChunkContent.Size);
            return lib.Pieces.KindOf(pal.Id(TileCode.Structure(code))) is { } k && k != PieceKind.Ground;
        };
        // P5-C: the trees and props on a cell of a visited chunk (build mode clears the natural ones; a well or a grave refuses the piece)
        cw.PropAt = (x, y, z) =>
        {
            int cx = ChunkStreamer.FloorDiv(x, ChunkContent.Size), cy = ChunkStreamer.FloorDiv(y, ChunkContent.Size);
            if (state.Chunks.GetSnapshot(new ChunkKey(cx, cy, 0)) == null || z < ChunkKey.MinZ || z > ChunkKey.MaxZ) return PropHere.None;
            var key = new ChunkKey(cx, cy, z);
            int ops = state.Chunks.DeltaOpCount(key);
            if (!standingCache.TryGetValue(key, out var hit) || hit.Ops != ops) standingCache[key] = hit = (ops, state.Chunks.Rebuild(key));
            if (standingCache.Count > 64) standingCache.Clear();
            return PropClearing.Here(hit.Content, x - cx * ChunkContent.Size, y - cy * ChunkContent.Size);
        };
        if (weather != null)
        {
            int cell = (state.EmbarkCellY + 8) * Continent.Size + state.EmbarkCellX + 8;
            cw.Weather = h => { var w = weather.AtLite(cell, h); return (w.PrecipIsSnow ? 0 : (int)((long)w.PrecipMmH.Raw * 10 >> 16), w.TempC.Floor()); };
        }
        var cs = state.Crafting ??= new CraftState();
        var people = pop.People;
        if (cs.Book.Base == null)
            cs.Book.Base = (p, s) =>
            {
                if (p == hero) return state.Hero?.Skill(Ravensguard.Sim.Characters.HeroRecord.SkillId(s)) ?? 0;
                int i = people.IndexOf(p); return i >= 0 ? people.SkillLevelOf(i, s) : 0;
            };
        cw.SkillOf = (p, s) => cs.Book.Level(p, s);
        cw.Train = (p, s, dx, diff, sec) => cs.Book.Use(p, s, dx, diff, sec);
        cw.Tiles = new ChunkTileSink(state.Chunks, BuildingPalette(content));
        cw.Jobs = () =>
        {
            var e = state.Economy; if (e == null) return null;
            return (new JobEnv { Ledger = e.Ledger, Econ = e, Pop = state.Population, Tuning = Ravensguard.Sim.Behaviour.BehaviourContent.From(content).OddJobs }, state.Jobs ??= new JobTable());
        };
        cw.PayeeOf = p =>
        {
            if (econ.Ledger.TryFind(PurseKind.Person, p, out var pp)) return pp;
            int i = people.IndexOf(p);
            if (i >= 0 && people.Household[i] is int h and >= 0 && h < econ.HouseholdCount) return econ.HouseholdPurse[h];
            return null;
        };
        cw.NameOf = p => p == hero ? state.Hero?.Name ?? "" : people.IndexOf(p) is int i and >= 0 ? people.FullName(i) : "";
        cw.Fall = (p, mm, ev, sec) => FallWounds.Apply(content, wounds(), p, mm, ev, sec, state.Seed);
        cw.PeopleOn = cells => cw.HeroCell is { } hc && cells.Contains(ConstructionIds.CellKey(hc.X, hc.Y, hc.Z)) ? [(hero, hc.X, hc.Y, hc.Z)] : [];
        cw.Moved = (p, x, y, z) => { if (p == hero) cw.HeroLanded(x, y, z); };
    }

    /// <summary>
    /// The land's hooks (land 4.1, 4.4): who may build where, from a registry rebuilt when the land book changes (a registry costs tens of milliseconds; placements are rare), and the claim
    /// a recognised dwelling improves (once per structure).
    /// </summary>
    public static void HookLand(ConstructionWorld cw, LandWorld land, Population pop)
    {
        LandRegistry? reg = null; string sig = "";
        // P5-C: the lot under a point for the plan fitter: a settlement lot with its street side (the lot's facing), or a claim's tiles without one
        cw.LotAt = (x, y) =>
        {
            string now = land.Book.ComputeHash() + "|" + pop.Households.Count;
            if (reg == null || now != sig) { reg = land.Registry(); sig = now; }
            var (s, l) = reg.LotAt(x, y);
            if (s >= 0) { var lot = pop.Region.Settlements[s].Plan.Lots[l]; return new FitLot(lot.Rect.X, lot.Rect.Y, lot.Rect.W, lot.Rect.H, lot.Facing & 3); }
            int t = LandRules.TileAt(x, y);
            foreach (var c in land.Book.Claims)
            {
                if (c.State is not (ClaimState.Open or ClaimState.Deeded) || Array.BinarySearch(c.Tiles, t) < 0) continue;
                int n = Ravensguard.Sim.WorldGen.Region.Region.Size, tm = Ravensguard.Sim.WorldGen.Region.Region.TileM;
                return new FitLot(t % n * tm, t / n * tm, tm, tm, -1);
            }
            return null;
        };
        cw.May = (p, hh, act, x, y) =>
        {
            string now = land.Book.ComputeHash() + "|" + pop.Households.Count;
            if (reg == null || now != sig) { reg = land.Registry(); sig = now; }
            return reg.May(p, hh, act, x, y);
        };
        cw.DwellingBuilt = (x, y, day) =>
        {
            int t = LandRules.TileAt(x, y);
            foreach (var c in land.Book.Claims)
            {
                if (c.State is not (ClaimState.Open or ClaimState.Deeded) || Array.BinarySearch(c.Tiles, t) < 0) continue;
                int st = cw.Rooms.StructureAt(x, y, 0) is var s0 and > 0 ? s0 : cw.Rooms.StructureAt(x, y, 1);
                if (cw.ImprovedBy(c.Serial, st)) return -1;
                land.Improve(c.Serial, ImprovementKind.Dwelling, day, (when, type, subjects, causes, data) => cw.Chronicle?.Invoke(when, type, subjects, causes, data) ?? default);
                return c.Serial;
            }
            return -1;
        };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, PiecePalette> Palettes = new();
    private static PiecePalette BuildingPalette(ContentDatabase db) => Palettes.GetValue(db, d => new PiecePalette(PieceCosts.Of(d).Library.Pieces));
}

/// <summary>
/// Built pieces into the chunk store as ID-keyed <c>SetTile</c> deltas (spec 3.2), so a place the player visited keeps them through unloads and saves. Only chunks with a stored
/// snapshot take a delta: a chunk never visited is generated whole on its first visit and would lose its terrain to a delta-only entry; the street view lays such cells from the
/// construction view instead (the book is the truth).
/// </summary>
public sealed class ChunkTileSink(ChunkStore store, PiecePalette palette) : ITileSink
{
    public long Written { get; private set; }

    public void Set(int x, int y, int z, string? structure, string? fixture)
    {
        int cx = ChunkStreamer.FloorDiv(x, ChunkContent.Size), cy = ChunkStreamer.FloorDiv(y, ChunkContent.Size);
        if (store.GetSnapshot(new ChunkKey(cx, cy, 0)) == null) return;
        if (z < ChunkKey.MinZ || z > ChunkKey.MaxZ) return;
        var key = new ChunkKey(cx, cy, z);
        int lx = x - cx * ChunkContent.Size, ly = y - cy * ChunkContent.Size;
        int cur = store.Rebuild(key) is { } content ? content.Tiles.GetValueOrDefault(ly * ChunkContent.Size + lx) : 0;
        int code = TileCode.WithFixture(TileCode.WithStructure(cur, palette.Of(structure)), palette.Of(fixture));
        // P5-C: a piece laid on a cell clears the trees and props there (PropClearing; build mode refused the ones it may not clear)
        var clear = new List<Ravensguard.Sim.Save.ChunkOp>();
        if (structure != null && store.Rebuild(key) is { } now)
            foreach (var (id, p) in PropClearing.At(now, lx, ly)) if (PropClearing.Of(p.Kind) == PropHere.Clears) clear.Add(Ravensguard.Sim.Save.ChunkOp.RemoveObject(new EntityId(id)));
        if (code == cur && clear.Count == 0) return;
        if (code != cur) clear.Add(Ravensguard.Sim.Save.ChunkOp.SetTile(lx, ly, code));
        store.AppendDelta(key, clear);
        Written++;
    }
}

/// <summary>A fall through floors (spec 4.5): the Phase 4 fall rules through the one resolver onto the person's wound book (stream 0x416 places the landing; key the collapse event, person).</summary>
public static class FallWounds
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, SiteBlows> Blows = new();

    public static int Apply(ContentDatabase content, WoundLedger? ledger, EntityId person, int heightMm, ulong eventId, long sec, ulong seed)
    {
        if (ledger == null || heightMm <= 0) return 0;
        var blows = Blows.GetValue(content, d => new SiteBlows(new FightContent(new CombatTables(d), d)));
        var plan = blows.T.Plan("human");
        var rules = PieceCosts.Of(content).Rules;
        var book = ledger.For(person.Value, plan, sec);
        int g = DetRng.Range(seed, (long)eventId, person.Value, RngStreams.HitJitter, 0, 0, 100);
        int k = DetRng.Range(seed, (long)eventId, person.Value, RngStreams.HitJitter, 1, 0, 4);
        string part = g < 30 ? ((k & 2) == 0 ? "upper_arm" : "lower_arm") : g < 50 ? "upper_body" : g < 75 ? ((k & 2) == 0 ? "upper_leg" : "lower_leg") : g < 85 ? "head" : "hand";
        int idx = SiteBlows.Part(plan, part, (k & 1) == 1);
        lock (ledger.Sync)
            return blows.Fall(book, plan, [], plan.SizeCm3, rules.FallBodyKg, heightMm, idx, new BlowKey(seed, (long)eventId, 0x4D10_0000UL + (person.Value & 0xFFFF), 0), sec, 0x4D10_0000UL);
    }
}
