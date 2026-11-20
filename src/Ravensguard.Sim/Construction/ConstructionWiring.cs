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
        cw.GrowthSites?.LinkSeen(new GameTime(end));                                   // P5-D: growth projects on plots the player has now seen become sites
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
        HookEconomy(cw, home.Economy, state.Chunks, content);                                  // P5-D: builders, markets, growth made visible
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
        return new ConstructionWorld(book, econ, PieceCosts.Of(content), region) { Pop = pop, Content = content, Seed = state.Seed };
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

    /// <summary>
    /// P5-D: the economy's side of contracts and visible growth: the region's builders (open carpentry and masonry businesses: their owner and staff, their store, purse and market party),
    /// the market books, and growth's projects on plots the player has seen made sites (<see cref="GrowthSites"/>).
    /// </summary>
    public static void HookEconomy(ConstructionWorld cw, EconomyWorld ew, ChunkStore chunks, ContentDatabase content)
    {
        var pop = cw.Pop!;
        cw.Market = s => s >= 0 && s < pop.Region.Settlements.Count ? ew.Markets.BookForSettlement(s) : null;
        cw.Builders = () => BuildersOf(cw, ew, pop);
        var gs = new GrowthSites(cw, ew.Growth, chunks, ChunkGenerator.For(pop.Region, pop, content));
        cw.GrowthSites = gs;
        ew.Growth.Sites = gs;
    }

    /// <summary>The region's builders: open businesses of the builder kinds, by id; their people the owner (when he works) and the staff.</summary>
    public static IReadOnlyList<BuilderInfo> BuildersOf(ConstructionWorld cw, EconomyWorld ew, Population pop)
    {
        var kinds = new HashSet<string>(cw.ContractRules.BuilderKinds, StringComparer.Ordinal);
        var list = new List<BuilderInfo>();
        var pt = pop.People;
        foreach (var b in ew.Production.Businesses)
        {
            if (b.State != Ravensguard.Sim.Production.BusinessState.Open || b.Kind >= ew.Content.Kinds.Length || !kinds.Contains(ew.Content.Kinds[b.Kind].Id)) continue;
            var people = new List<int>();
            if (b.OwnerWorks && b.Owner >= 0) people.Add(b.Owner);
            foreach (int st in b.Staff) if (st >= 0 && !people.Contains(st)) people.Add(st);
            int best = 0;
            foreach (int r in people) if (r >= 0 && r < pt.Count) best = Math.Max(best, Math.Max(pt.SkillLevelOf(r, Skill.Carpentry), pt.SkillLevelOf(r, Skill.Masonry)));
            var st0 = pop.Region.Settlements[Math.Clamp(b.Settlement, 0, pop.Region.Settlements.Count - 1)];
            string who = b.Owner >= 0 && b.Owner < pt.Count ? pt.FullName(b.Owner) : ew.Content.Kinds[b.Kind].Id;
            list.Add(new BuilderInfo(b.Id, PurseKind.Business, $"{who} ({ew.Content.Kinds[b.Kind].Id}, {st0.Name})", b.Settlement, st0.CenterX, st0.CenterY, best, people, b.Store, b.Party));
        }
        list.Sort((a, c) => a.Id.CompareTo(c.Id));
        return list;
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
        if (code == cur) return;
        store.AppendDelta(key, [Ravensguard.Sim.Save.ChunkOp.SetTile(lx, ly, code)]);
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
