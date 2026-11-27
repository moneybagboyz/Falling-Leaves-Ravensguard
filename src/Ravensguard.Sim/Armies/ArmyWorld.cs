using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>What one step of the armies needs of the tick: the world, where chronicle entries go (null in a bare call: nothing is written) and the hour.</summary>
public readonly record struct ArmyEnv(SimWorld W, RegionOutbox? Ob, long Hour, RuleChron? Writer = null)
{
    /// <summary>Where the chronicle goes: the writer a caller gave (a barrier's, a test's), else the tick's outbox, else nowhere.</summary>
    public RuleChron Chronicler => Writer ?? (Ob != null ? RuleChron.Of(Ob) : RuleChron.Silent);
    public long Day => Hour / 24;
    public int HourOfDay => (int)(Hour % 24);
    public GameTime Time => GameTime.FromGameHours(Hour);
    public int DayOfYear => Time.DayOfYear;
    public bool IsSeasonStartDay => (DayOfYear - 1) % TimeConst.DaysPerSeason == 0;
    public int SeasonNo => (int)(Day / TimeConst.DaysPerSeason);
    public int DayOfSeason => (DayOfYear - 1) % TimeConst.DaysPerSeason + 1;
    public bool Winter => Time.Season == Ravensguard.Sim.Core.Season.Winter;
}

/// <summary>
/// P7-AM-A: the standing warbands of the lords on the running home region (armies-on-the-map 3.1, 4.1): the book, the rules, the soldiers' runtime and the road graph the men ride on. One per home region, held in the
/// sim's services (<see cref="ArmyWiring"/>); it holds no state of its own beyond the book and a cache of road paths (rebuilt from the region, never saved). Its steps run in the abstract tick
/// (<see cref="ArmySystem"/>) and are plain calls a test can make between hours.
/// </summary>
public sealed partial class ArmyWorld
{
    public ArmyBook Book { get; }
    public ArmyRulesDef Rules { get; }
    public SimWorld W { get; }
    public SoldierWorld? Soldiers { get; }
    public ContentDatabase? Content { get; }
    /// <summary>The hero is a lord whose warband is his to command: the lords' brain leaves it alone.</summary>
    public Func<ulong, bool> IsHero { get; set; } = _ => false;
    /// <summary>The hero's person id (0 when there is none): P8-LW-2's beach fight puts him among the riders.</summary>
    public Func<ulong> HeroId { get; set; } = () => 0;
    /// <summary>The key of the armoury of a settlement's lord, if there is one (the works' armouries, by the room's origin cell); a test or the economy may install another.</summary>
    public Func<int, long?>? ArmouryAt { get; set; }

    /// <summary>Wall time of the armies' steps (not saved): the hourly walk, the daily pay and bands, the season's hiring; read by the budget tests.</summary>
    public TimingCell HourCost { get; } = new();
    public TimingCell DayCost { get; } = new();
    public TimingCell SeasonCost { get; } = new();

    /// <summary>A test's own road between two settlements (P7-AM-C: the march gate walks a measured 8 km); null answers fall back to the region's roads. Not part of the saved state.</summary>
    public Func<int, int, CaravanPath?>? PathSource { get; set; }

    private readonly Dictionary<long, CaravanPath?> _paths = [];
    private readonly Dictionary<ulong, int> _lordFief = [];

    public ArmyWorld(SimWorld w, ArmyBook book, ContentDatabase? content, SoldierWorld? soldiers)
    {
        W = w; Book = book; Content = content; Soldiers = soldiers;
        Rules = content?.All<ArmyRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ArmyRulesDef { Id = "army_rules" };
        if (Rules.Holdings.Count == 0) Rules = Rules with { Holdings = [new HoldingSizeDef { Id = "manor", MinPop = 0, Min = 3, Max = 6 }] };
    }

    // ------------------------------------------------------------------ the world it reads

    public RegionData Region => W.Population.Region;
    public PersonTable People => W.People;
    public HouseholdTable Households => W.Households;
    public RuleBook? Rule => W.Get<RuleBook>();
    public PoliticsBook? Politics => W.Get<PoliticsBook>();
    public EconomyWorld? Ew => W.Get<EconomyWorld>();

    public RuleCtx? RuleCtx(ArmyEnv e)
    {
        var rb = Rule; if (rb == null) return null;
        return RuleWiring.Context(W, rb, e.Chronicler);
    }

    // ------------------------------------------------------------------ geometry and roads

    public (int X, int Y) Center(int settlement) { var s = Region.Settlements[settlement]; return (s.CenterX, s.CenterY); }

    /// <summary>The straight distance in metres between two settlements.</summary>
    public int DistM(int a, int b)
    {
        var (ax, ay) = Center(a); var (bx, by) = Center(b);
        return (int)CaravanPath.Isqrt((long)(ax - bx) * (ax - bx) + (long)(ay - by) * (ay - by));
    }

    /// <summary>The road path from one settlement to another (the same graph a caravan walks), or null when none joins them.</summary>
    public CaravanPath? PathOf(int from, int to)
    {
        long key = ((long)from << 20) | (uint)to;
        if (_paths.TryGetValue(key, out var p)) return p;
        p = from == to ? null : PathSource?.Invoke(from, to) ?? CaravanRoads.BuildBetween(Region, from, to, false);
        if (_paths.Count > 4096) _paths.Clear();
        return _paths[key] = p;
    }

    /// <summary>The continent millimetres of the home region's frame origin (its north-west corner): the road paths are in continent metres already.</summary>
    public long OriginXMm => RegionCoord.OfCell(Region.CellX, Region.CellY).OriginXM * 1000L;
    public long OriginYMm => RegionCoord.OfCell(Region.CellX, Region.CellY).OriginYM * 1000L;

    /// <summary>Where a settlement's centre is, in continent millimetres.</summary>
    public (long XMm, long YMm) CenterMm(int settlement) { var (x, y) = Center(settlement); return (OriginXMm + x * 1000L, OriginYMm + y * 1000L); }

    /// <summary>Where a warband stands in continent millimetres: on a road leg at its place along the path, else at its settlement's centre; the pure function the live bubble will read.</summary>
    public (long XMm, long YMm)? PositionOf(Warband wb)
    {
        if (wb.Here >= 0 && wb.Here < Region.Settlements.Count) return CenterMm(wb.Here);
        if (wb.From < 0 || wb.To < 0) return null;
        var p = PathOf(wb.From, wb.To); if (p == null) return null;
        var at = p.At(Math.Clamp(wb.DistMm, 0, p.LengthMm));
        return (at.XMm, at.YMm);
    }

    // ------------------------------------------------------------------ people

    public int RowOf(ulong person) => People.IndexOf(new EntityId(person));
    public bool IsStaff(int row) => Ew?.Labour.IsStaff(row) == true;
    public int HouseholdOf(ulong person) { int r = RowOf(person); return r < 0 ? -1 : People.Household[r]; }

    /// <summary>The wage in mites a day of a soldier who is not on a manor's payroll: the retainer wage for a levy kept on, else the role's own.</summary>
    public int WageOf(ulong person)
    {
        var row = Soldiers?.Book.Of(person);
        if (row == null || row.Role == "levy" || Soldiers == null) return Rules.RetainerWageMitesDay;
        return Soldiers.Roles.WageCp(row.Role) * 10;
    }

    /// <summary>The fief a lord holds (a person id value), -1 when none.</summary>
    public int FiefOfLord(ulong lord)
    {
        var rb = Rule; if (rb == null) return -1;
        foreach (var f in rb.Fiefs.Values) if (f.Holder >= 0 && (ulong)f.Holder == lord && !f.Forfeited) return f.Fief;
        return -1;
    }

    // ------------------------------------------------------------------ the holding's size

    /// <summary>The holding of a fief: the last row of the content whose population it reaches.</summary>
    public HoldingSizeDef HoldingOf(int fief)
    {
        int pop = (uint)fief < (uint)Region.Fiefs.Count ? Region.Fiefs[fief].Pop : 0;
        var h = Rules.Holdings[0];
        foreach (var x in Rules.Holdings) if (pop >= x.MinPop) h = x;
        return h;
    }

    /// <summary>The men the holding calls for before the wage roll's cap: from the row's minimum to its maximum as the fief's people grow across <c>growth_pop</c>.</summary>
    public int TargetSize(int fief)
    {
        int pop = (uint)fief < (uint)Region.Fiefs.Count ? Region.Fiefs[fief].Pop : 0; var h = HoldingOf(fief);
        int over = Math.Max(0, pop - h.MinPop);
        return h.Min + (int)((long)(h.Max - h.Min) * Math.Min(over, Rules.GrowthPop) / Math.Max(1, Rules.GrowthPop));
    }

    // ------------------------------------------------------------------ the real baggage of a warband (loot from raids)

    /// <summary>The baggage store of a warband: a store of its own in the economy's other stores, under a reserved id (never meets a table row's id).</summary>
    public static EntityId BaggageOwner(int warband) => EntityId.Reserved(IdKind.Other, 0, BaggageSpace, (ulong)warband);
    public const EntityId.ReservedSpace BaggageSpace = (EntityId.ReservedSpace)7, BandSpace = (EntityId.ReservedSpace)8;
    public static EntityId BandStoreOwner(int band) => EntityId.Reserved(IdKind.Other, 0, BandSpace, (ulong)band);

    public Store BaggageOf(int warband) => W.Economy.OpenStore(StoreKind.Chest, BaggageOwner(warband), 0, 0, enforce: false);
    public Store StoreOfBand(int band) => W.Economy.OpenStore(StoreKind.Chest, BandStoreOwner(band), 0, 0, enforce: false);

    /// <summary>The household stores' grain goods (spoil class <c>grain</c>): what a raid and a robbery take.</summary>
    public IEnumerable<ushort> GrainGoods()
    {
        var cat = W.Economy.Goods;
        for (int g = 0; g < cat.Count; g++) if (cat.Def((ushort)g).SpoilClass == "grain") yield return (ushort)g;
    }

    /// <summary>Takes up to <paramref name="kg"/> of grain from a household's store into <paramref name="to"/> (a real move: mass is conserved); returns the grams moved.</summary>
    public long TakeGrain(int household, Store to, long kg)
    {
        var from = W.Economy.HouseholdStore[household]; var cat = W.Economy.Goods; long left = kg * 1000, moved = 0;
        foreach (var g in GrainGoods())
        {
            if (left <= 0) break;
            long have = from.Qty(g); if (have <= 0) continue;
            long unit = Math.Max(1, cat.MassG(g, 1));
            long take = Math.Min(have, left / unit); if (take <= 0) continue;
            long got = Store.MoveUpTo(from, to, g, take);
            moved += got * unit; left -= got * unit;
        }
        return moved;
    }
}

/// <summary>Installs the armies on the running home region (the P7-AM-A hook in <c>WorldRuntime</c>): the book of the world state, the rules from content, the soldiers' runtime and the hero test; the systems find <see cref="ArmyWorld"/> in the sim's services.</summary>
public static class ArmyWiring
{
    public static ArmyWorld? Attach(Worlds.RegionWorld home, Save.WorldState state, ContentDatabase content)
    {
        if (state.Rule == null || state.Economy == null) return null;                       // no fiefs, no lords, no warbands
        var book = state.Armies ??= new ArmyBook();
        var sw = SoldierWiring.For(state, content);
        var aw = new ArmyWorld(home.Sim, book, content, sw)
        {
            IsHero = id => state.Party != null && state.Party.Hero.Value == id,
            HeroId = () => state.Party != null ? state.Party.Hero.Value : 0UL,
        };
        aw.SupplyProvider = () => WarSupplyWiring.Ensure(home, state);
        home.Sim.Set(aw);
        if (state.ContinentState is { } cont) cont.Armies = new ContinentArmies(book, content.All<CountBattleDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault());     // P7-AW-D: the count hosts' season in the continent tick
        var wx = home.Services.Weather; var at = home.Coord; int size = Ravensguard.Sim.WorldGen.Continent.Size;
        int wcell = Math.Min(size - 1, at.CellY + 8) * size + Math.Min(size - 1, at.CellX + 8);
        aw.Wetness = MarchWeather.Sampler(wx, wcell);
        if (state.Disease is { } disease)                                                  // P7-AM-C: camps feed the disease rule's army-camp and unboiled-water terms (phase-6-open C4)
        {
            var prevCamp = disease.ArmyCampSize; var prevWater = disease.UnboiledWaterDays;
            disease.ArmyCampSize = id => Math.Max(aw.CampSizeOf(id), prevCamp?.Invoke(id) ?? 0);
            disease.UnboiledWaterDays = id => Math.Max(aw.UnboiledDaysOf(id), prevWater?.Invoke(id) ?? 0);
        }
        if (home.Economy != null)
        {
            var prev = home.Economy.IsWarbandMan;
            home.Economy.IsWarbandMan = p => book.IsAway(home.Sim.People.IdOf(p).Value) || prev?.Invoke(p) == true;
        }
        return aw;
    }
}

/// <summary>The ground the weather leaves (P7-AM-C): the rain of the last 24 hours, snow lying, frost and thaw, from the region's cell, memoised for the hour asked.</summary>
public static class MarchWeather
{
    public static Func<long, long, long, Ravensguard.Sim.Carts.WetState> Sampler(Ravensguard.Sim.Weather.RegionWeather w, int cell)
    {
        long lastHour = long.MinValue; Ravensguard.Sim.Carts.WetState last = default;
        return (hour, _, _) =>
        {
            if (hour == lastHour) return last;
            long rain = 0;
            for (int k = 0; k < 24; k++) { var s = w.AtLite(cell, hour - k); if (!s.PrecipIsSnow) rain += (long)s.PrecipMmH.Raw * 10 / Fixed.OneRaw; }
            var now = w.AtLite(cell, hour);
            int snow = (int)((long)now.SnowDepthCm.Raw / Fixed.OneRaw);
            bool frozen = now.TempC.Raw <= 0;
            last = new Ravensguard.Sim.Carts.WetState((int)Math.Min(rain, 10_000), snow, frozen, snow > 0 && !frozen);
            lastHour = hour;
            return last;
        };
    }
}
