using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>
/// Stage 2 world generation: history v0 (plan §1A). Seasonal ticks (4 a year) over the continent grid.
/// Integer maths only; every random draw is keyed by (seed, tick, entity, stream, draw) via <see cref="DetRng"/>;
/// all iteration is in stable index (= id) order.
/// </summary>
public sealed partial class HistoryGenerator
{
    private const int N = Continent.Size, A = Continent.Cells;
    private static readonly int[] D4x = [1, -1, 0, 0], D4y = [0, 0, 1, -1];
    private static readonly int[] SeasonGrow = [120, 120, 90, 30];

    private static class S
    {
        public const uint Pop = 1, Found = 2, Name = 3, Weather = 4, Fire = 5, Flood = 6, Mine = 7, Plague = 8, Famine = 9, Death = 10, Birth = 11,
            Marry = 12, Craft = 13, Forge = 14, Outlaw = 15, Incident = 16, War = 17, Battle = 18, Siege = 19, Succ = 20, Rebel = 21, Coup = 22,
            Misc = 23, Art = 24, Saint = 25, Route = 26, Init = 27, Text = 28;
    }

    private readonly Continent _c;
    private readonly HistorySettings _s;
    private readonly ulong _seed;
    private readonly WorldHistory _h;
    private readonly IdAllocator _ids = new();
    private readonly NameGen _names;
    private readonly int _R;
    private int _tick, _year, _season, _evInTick;
    private uint _dc;

    private readonly int[] _owner = new int[A], _pop = new int[A], _K = new int[A], _settleAt = new int[A];
    private readonly bool[] _metal = new bool[A];
    private readonly int[] _stamp = new int[A];
    private int _stampN;
    private readonly HashSet<string> _usedNames = [];

    private HistoryGenerator(Continent c, HistorySettings s)
    {
        _c = c; _s = s; _seed = s.Seed;
        _h = new WorldHistory { Seed = s.Seed, Years = s.Years };
        _names = new NameGen(s.Seed, c.Cultures);
        _R = Math.Max(s.MaxRealms, c.Realms.Count + 8);
        _border = new int[_R * _R]; _opinion = new int[_R * _R]; _truce = new int[_R * _R]; _cb = new CbState[_R * _R];
        _cultCount = new int[_R * 8]; _atWar = new int[_R]; Array.Fill(_atWar, -1);
        _general = new EntityId[_R];
    }

    /// <summary>Runs <c>settings.Years</c> years of history over the continent.</summary>
    public static WorldHistory Generate(Continent c, HistorySettings settings)
    {
        var g = new HistoryGenerator(c, settings);
        g.Run();
        return g._h;
    }

    // ---------- deterministic helpers ----------
    private bool Chance(ulong ent, uint stream, long ppm) => ppm > 0 && DetRng.Range(_seed, _tick, ent, stream, _dc++, 0, 1_000_000) < ppm;
    private bool Pct(ulong ent, uint stream, int pct) => Chance(ent, stream, pct * 10_000L);
    private int Roll(ulong ent, uint stream, int lo, int hi) => DetRng.Range(_seed, _tick, ent, stream, _dc++, lo, hi);
    private RngStream Rs(ulong ent, uint stream) => new(_seed, _tick, ent, stream, _dc++ * 64);
    private RealmState Rm(int i) => _h.Realms[i];
    private static int Cx(int cell) => cell % N;
    private static int Cy(int cell) => cell / N;
    private static int Dist(int a, int b) => Math.Max(Math.Abs(Cx(a) - Cx(b)), Math.Abs(Cy(a) - Cy(b)));
    private static bool In(int x, int y) => (uint)x < N && (uint)y < N;
    private static EntityId? Opt(EntityId id) => id.Value == 0 ? null : id;
    private static Cause Cz(CauseKind k, EntityId ev, string note) => new(k, Opt(ev), note);
    private static string Plural(int n, string one, string many) => n == 1 ? one : many;
    private static string Num(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private EntityId Ev(string type, string title, string text, int cell, EntityId[] subjects, params Cause[] causes)
    {
        if (causes.Length == 0) throw new ArgumentException("every event needs a cause");
        var id = _ids.Allocate(IdKind.Event);
        int day = 1 + Math.Min(TimeConst.DaysPerSeason - 1, _evInTick++);
        var when = GameTime.FromCalendar(_year, (Season)_season, day);
        var data = new Dictionary<string, string> { ["title"] = title, ["text"] = text, ["cell"] = cell.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        _h.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, data));
        return id;
    }

    private int Levy(RealmState r) => (int)((long)r.Pop * _s.LevyPermille / 1000);
    private string Culture(int id) => id >= 0 && id < _c.Cultures.Count ? _c.Cultures[id].Name : "Unknown";
    private string Title(Notable n) => n.Role == NotableRole.Ruler ? (n.Female ? "Queen" : "King") : n.Role == NotableRole.General ? "General" : n.Role == NotableRole.Saint ? "Saint" : n.Female ? "Lady" : "Lord";
    private string Who(Notable n) => $"{Title(n)} {n.Full}";
    private string HouseOf(Notable n) { var d = _h.GetDynasty(n.Dynasty); return d == null ? "" : $"House {d.Name}"; }

    // ---------- main loop ----------
    private void Run()
    {
        Setup();
        int ticks = _s.Years * 4;
        for (_tick = 0; _tick < ticks; _tick++)
        {
            _year = _tick / 4; _season = _tick % 4; _dc = 0; if (_tick > 0) _evInTick = 0;
            WeatherPass();
            PopulationPass();
            if (_season == 0) SeaPass();
            SettlementPass();
            FoundingPass(false);
            MinorFoundingPass(false);
            DisasterPass();
            NotablesPass();
            PoliticsPass();
            WarsPass();
            LifecyclePass();
            if (_tick % (Math.Max(1, _s.RouteRefreshYears) * 4) == 0 && _tick > 0) RoutesPass();
            if (_season == 0 && _year > 0 && _year % 50 == 0) TakeSnapshot(_year);
        }
        Finish();
    }

    private void Finish()
    {
        _h.FinalOwner = new byte[A];
        for (int i = 0; i < A; i++) _h.FinalOwner[i] = _owner[i] < 0 ? Continent.None : (byte)_owner[i];
        _h.FinalPop = (int[])_pop.Clone();
        long total = 0;
        for (int i = 0; i < A; i++) total += _pop[i];
        foreach (var s in _h.Settlements) if (!s.IsRuin) total += s.Pop;
        _h.EndPopulation = total;
        _year = _s.Years;
        TakeSnapshot(_s.Years);
    }

    private void TakeSnapshot(int year)
    {
        var b = new byte[A];
        for (int i = 0; i < A; i++) b[i] = _owner[i] < 0 ? Continent.None : (byte)_owner[i];
        _h.Snapshots.Add(new Snapshot(year, b));
    }

    // ---------- setup ----------
    private void Setup()
    {
        _tick = 0; _year = 0; _season = 0;
        Array.Fill(_settleAt, -1);
        Array.Fill(_owner, -1);
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i]) continue;
            _owner[i] = _c.RealmId[i] == Continent.None ? -1 : _c.RealmId[i];
            _K[i] = Capacity(i);
            _pop[i] = (int)((long)_K[i] * _s.StartFillPct / 100);
        }
        foreach (var v in _c.Veins)
            if (v.Type is MineralType.Iron or MineralType.Copper or MineralType.Tin or MineralType.Silver or MineralType.Gold or MineralType.Coal) _metal[v.Cell] = true;

        foreach (var cr in _c.Realms)
            _h.Realms.Add(new RealmState { Index = cr.Id, Id = _ids.Allocate(IdKind.Faction), Name = cr.Name, CultureId = cr.CultureId, FoundedTick = 0 });
        // Continent realm ids are dense 0..n-1.
        for (int k = 0; k < _h.Realms.Count; k++)
        {
            var r = _h.Realms[k];
            int cell = _c.Realms[k].SeedCell;
            if (_owner[cell] != k) { _owner[cell] = k; }
            var cap = AddSettlement(cell, 650 + Roll((ulong)k, S.Init, 0, 300), k, null);
            r.Capital = _h.Settlements.IndexOf(cap);
        }
        FoundingPass(true);
        MinorFoundingPass(true);
        for (int k = 0; k < _h.Realms.Count; k++) InitRuler(_h.Realms[k]);
        foreach (var s in _h.Settlements) s.PeakKind = s.Kind;
        AncientBattles();
        RoutesPass();
        long total = 0;
        for (int i = 0; i < A; i++) total += _pop[i];
        foreach (var s in _h.Settlements) total += s.Pop;
        _h.StartPopulation = total;
        TakeSnapshot(0);
        for (int i = 0; i < _R * _R; i++) _opinion[i] = 0;
        for (int a = 0; a < _h.Realms.Count; a++)
            for (int b = 0; b < _h.Realms.Count; b++)
                _opinion[a * _R + b] = Rm(a).CultureId == Rm(b).CultureId ? 15 : -5;
    }

    private int Capacity(int i)
    {
        if (_c.Ocean[i] || _c.Lake[i]) return 0;
        int mult = _c.Biomes[i] switch
        {
            Biome.Grassland => 110, Biome.RiverValley => 130, Biome.TemperateForest => 90, Biome.Steppe => 60, Biome.Taiga => 45,
            Biome.Tundra => 20, Biome.Desert => 12, Biome.Marsh => 45, Biome.Mountain => 20, _ => 0,
        };
        long k = (long)_c.Fertility[i] * mult / 100 * _s.CapacityScalePct / 100;
        if (_c.RiverFlow[i] >= _c.Settings.StreamFlowThreshold) k = k * 125 / 100;
        if (_c.CoastDistance[i] == 1) k = k * 115 / 100;
        return (int)Math.Clamp(k, 0, 160L * Math.Max(100, _s.CapacityScalePct) / 100);
    }

    private static readonly string[] ManorTypes = ["Hall", "Manor", "Keep", "Tower", "Grange", "Court"];

    private Settlement AddSettlement(int cell, int pop, int realm, EntityId? foundedEvent, bool minor = false, bool manor = false)
    {
        var id = _ids.Allocate(IdKind.Settlement);
        int cu = _c.CultureId[cell] == Continent.None ? 0 : _c.CultureId[cell];
        var rs = Rs(id.Value, S.Name);
        string name = _names.Place(cu, ref rs);
        for (int tries = 0; _usedNames.Contains(name) && tries < 6; tries++) name = _names.Place(cu, ref rs);
        if (_usedNames.Contains(name)) name = $"{_names.Feature(0, ref rs)} {name}";
        if (manor) name = $"{name} {ManorTypes[rs.Range(0, ManorTypes.Length)]}";
        if (!_usedNames.Add(name)) name = $"{name} {id.Counter}";
        var s = new Settlement
        {
            Id = id, Name = name, Cell = cell, Pop = pop, PeakPop = pop, Realm = realm, FoundedTick = _tick, Minor = minor, Manor = manor,
        };
        int x = Cx(cell), y = Cy(cell);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!In(x + dx, y + dy)) continue;
                int j = (y + dy) * N + x + dx;
                if (_c.RiverFlow[j] >= _c.Settings.RiverFlowThreshold && !_c.Ocean[j]) s.OnRiver = true;
            }
        s.Coastal = _c.CoastDistance[cell] <= 2;
        int hint = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!In(x + dx, y + dy)) continue;
                int j = (y + dy) * N + x + dx;
                hint += _K[j];
                if (_metal[j]) s.Mining = 1;
            }
        s.Hinterland = hint;
        _h.Add(s);
        _settleAt[cell] = _h.Settlements.Count - 1;
        return s;
    }

    private void InitRuler(RealmState r)
    {
        var dyn = NewDynasty(r.CultureId, EntityId.None, -Roll((ulong)r.Index, S.Init, 60, 200));
        dyn.Ruling = true;
        r.Dynasty = dyn.Id;
        bool female = Pct((ulong)r.Index, S.Init, 15);
        int age = Roll((ulong)r.Index, S.Init, 24, 56);
        var ruler = NewNotable(NotableRole.Ruler, r.CultureId, r.Index, dyn.Id, female, -age, EntityId.None, EntityId.None);
        dyn.Founder = ruler.Id;
        ruler.Residence = r.Capital;
        r.Ruler = ruler.Id;
        var spouse = NewConsort(ruler, age + Roll((ulong)r.Index, S.Init, -6, 4));
        int kids = Roll((ulong)r.Index, S.Init, 0, 4);
        for (int k = 0; k < kids && age - 18 - k * 3 > 0; k++)
        {
            var father = ruler.Female ? spouse : ruler; var mother = ruler.Female ? ruler : spouse;
            var kid = NewNotable(NotableRole.Heir, r.CultureId, r.Index, father.Dynasty, Pct((ulong)r.Index * 7 + (ulong)k, S.Init, 50), -(age - 19 - k * 3 > 1 ? Roll((ulong)r.Index * 5 + (ulong)k, S.Init, 1, age - 18 - k * 3 + 1) : 1), father.Id, mother.Id);
            kid.Residence = r.Capital;
        }
        r.Treasury = Math.Max(500, r.Pop / 20);
        var cap = _h.Settlements[r.Capital];
        r.FoundedEvent = Ev("realm_founded", $"The Realm of {r.Name}",
            $"{Who(ruler)} of {HouseOf(ruler)} rules the {Culture(r.CultureId)} lands from {cap.Name}",
            cap.Cell, [r.Id, cap.Id, ruler.Id], new Cause(CauseKind.Other, null, "the realm's ancient founding, before the records begin"));
    }

    // ---------- notable factories ----------
    private Dynasty NewDynasty(int culture, EntityId founder, int foundedYear)
    {
        var id = _ids.Allocate(IdKind.Household);
        var rs = Rs(id.Value, S.Name);
        string name = _names.House(culture, ref rs);
        var d = new Dynasty { Id = id, Name = name, Culture = culture, Founder = founder, FoundedYear = foundedYear };
        _h.Add(d);
        return d;
    }

    private Notable NewNotable(NotableRole role, int culture, int realm, EntityId dynasty, bool female, int birthYear, EntityId father, EntityId mother)
    {
        var id = _ids.Allocate(IdKind.Person);
        var rs = Rs(id.Value, S.Name);
        var n = new Notable
        {
            Id = id, Name = _names.Person(culture, female, ref rs), Female = female, Role = role, BirthYear = birthYear, Dynasty = dynasty,
            Father = father, Mother = mother, Realm = realm, HomeRealm = realm, Culture = culture,
            Ambition = rs.Range(10, 91), Skill = rs.Range(20, 91),
        };
        _h.Add(n);
        return n;
    }

    private Notable NewConsort(Notable spouse, int birthAge)
    {
        int cu = spouse.Culture;
        var house = MinorHouse(cu, spouse.Id.Value);
        var c = NewNotable(NotableRole.Noble, cu, spouse.Realm, house.Id, !spouse.Female, spouse.BirthYear + Math.Clamp(Roll(spouse.Id.Value, S.Marry, -6, 5), -6, 5), EntityId.None, EntityId.None);
        c.Residence = spouse.Residence;
        c.Spouse = spouse.Id; spouse.Spouse = c.Id;
        return c;
    }

    private readonly List<EntityId>[] _minorHouses = Enumerable.Range(0, 8).Select(_ => new List<EntityId>()).ToArray();
    private Dynasty MinorHouse(int culture, ulong salt)
    {
        var list = _minorHouses[Math.Clamp(culture, 0, 7)];
        if (list.Count >= 4 && !Pct(salt, S.Misc, 25)) return _h.GetDynasty(list[Roll(salt, S.Misc, 0, list.Count)])!;
        var d = NewDynasty(culture, EntityId.None, _year);
        list.Add(d.Id);
        return d;
    }
}
