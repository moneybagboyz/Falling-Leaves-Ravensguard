using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Burning;

/// <summary>Takes a built piece down by fire (<paramref name="burnt"/>: its materials burn) or by a hook (pulled down, salvaged); true when the piece was the host's.</summary>
public delegate bool PieceDestroy(int x, int y, int z, bool fixture, bool burnt, long sec, EntityId cause);

/// <summary>Writes one chronicle entry (the outbox's keyed overload, or a test's chronicle) and returns its id.</summary>
public delegate EntityId FireChronicle(GameTime when, string typeId, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, (string Key, string Value)[] data);

/// <summary>
/// The live fire (fire and burning 4.1 to 4.3, 4.9): cells that burn by their fuel's ignition and spread to what they touch, throw sparks from burning roofs, burn out and leave scars, take
/// their pieces with them (through <see cref="Destroy"/> where Phase 5 construction owns the piece, so a burnt support brings the floor above down by the support rules), fill rooms with
/// smoke, burn the people standing in them and the goods stored there. It runs on the live tick (<see cref="AdvanceTo"/>: a sim-second step with the spread every 5 sim-seconds) and does
/// nothing at all, not even a walk, while nothing burns. Integer only; every walk is in (z, y, x) key order; every draw is the counter RNG keyed on the target cell and the spread step.
/// </summary>
public sealed partial class FireWorld
{
    public FireBook Book { get; }
    public RoomWorld Rooms { get; }
    public FuelTable Fuels { get; }
    public ulong Seed { get; }

    // ---- hooks (the wiring sets what the host has; every one may stay null) ----
    /// <summary>The weather at a game hour; null: calm and dry.</summary>
    public Func<long, FireWeather>? Weather { get; set; }
    public FireChronicle? Chronicle { get; set; }
    /// <summary>Phase 5 construction takes a built piece down (cell, fixture or structure, burnt or pulled down, game second, the fire): true when it owned the piece; false: the fire edits the room world and the chunk tiles itself.</summary>
    public PieceDestroy? Destroy { get; set; }
    /// <summary>Writes a cell's remaining pieces back to the chunk store (the street's tiles).</summary>
    public Ravensguard.Sim.Construction.ITileSink? Tiles { get; set; }
    /// <summary>The pieces of generated buildings in a box of the chunk store, so the room world knows them: (x0, y0, x1, y1) inclusive, to cell edits. Null: none to adopt.</summary>
    public Func<int, int, int, int, IEnumerable<CellEdit>>? Adopt { get; set; }
    /// <summary>The people the fire can reach, appended to the list (the live tier's bubble and the hero).</summary>
    public Action<List<FirePerson>>? PeopleNear { get; set; }
    public Action<IncidentEnded>? Ended { get; set; }

    private readonly HashSet<long> _adopted = [];
    private readonly Dictionary<string, int> _classOf = new(StringComparer.Ordinal);

    public FireWorld(FireBook book, RoomWorld rooms, FuelTable fuels, ulong seed)
    {
        Book = book; Rooms = rooms; Fuels = fuels; Seed = seed;
    }

    public const int GroanFloorStoreys = 3;

    // ------------------------------------------------------------------ cells and fuel

    public static long Key(int x, int y, int z) => RoomWorld.Key(x, y, z);
    public static (int X, int Y, int Z) Unpack(long k) => RoomWorld.Unpack(k);

    /// <summary>The fuel of a cell: the structure piece's class when it burns, else the fixture's, else none.</summary>
    public FuelClass FuelAt(int x, int y, int z)
    {
        var s = Rooms.PieceAt(x, y, z);
        if (s != null) { var c = Fuels.ClassOf(s.Id); if (c.Burns) return c; }
        var f = Rooms.FixtureAt(x, y, z);
        if (f != null) { var c = Fuels.ClassOf(f.Id); if (c.Burns) return c; }
        if (CropAt?.Invoke(x, y, z) == true && Fuels.Find("thatch") is { } straw) return straw;          // a standing crop in late summer is straw (open question 2)
        return Fuels.Get(FuelTable.None);
    }

    /// <summary>The arson bounty (crime 4.x): 2,000 mites and a tenth of the damage in mites.</summary>
    public static long ArsonBountyMites(long damageMites) => 2_000 + damageMites / 10;

    public bool IsBurning(int x, int y, int z) => Book.Cells.ContainsKey(Key(x, y, z));
    public FireCell? CellAt(int x, int y, int z) => Book.Cells.GetValueOrDefault(Key(x, y, z));
    public ScarKind? ScarAt(int x, int y, int z) => Book.Scars.TryGetValue(Key(x, y, z), out var s) ? s : null;

    /// <summary>Whether anything needs a step (a burning cell, a person breathing smoke): the live tick skips the world when not.</summary>
    public bool Active => Book.Cells.Count > 0 || Book.Exposed.Count > 0;

    // ------------------------------------------------------------------ ignition (4.1)

    /// <summary>
    /// Sets a cell alight with its cause (a torch, a hearth's sparks, lightning, a riot, a firedamp blast). The cell must hold fuel. A flame touching a fire already burning joins its
    /// incident; anything else starts a new one with a <c>fire_started</c> entry. False when the cell has no fuel or already burns.
    /// </summary>
    public bool Ignite(int x, int y, int z, long gameSec, FireCause cause)
    {
        long key = Key(x, y, z);
        if (Book.Cells.ContainsKey(key)) return false;
        EnsureAdopted(x, y);
        var fuel = FuelAt(x, y, z);
        if (!fuel.Burns) return false;
        ulong incident = JoinedIncident(x, y, z);
        if (incident == 0) incident = StartIncident(x, y, z, gameSec, cause);
        if (Book.Cells.Count == 0 && Book.Exposed.Count == 0) Book.SecDone = FireClock.SimS(gameSec) - 1;
        Add(key, fuel, gameSec, incident);
        return true;
    }

    /// <summary>The seconds a hand takes to set a flame to the cell (the Q verb "Set fire": 10 sim-s, thatch 4, fire 4.1); 0 when there is nothing to light.</summary>
    public int SetFireSeconds(int x, int y, int z) => FuelAt(x, y, z) is { Burns: true } f ? (f.Id == "thatch" ? 4 : 10) : 0;

    private ulong JoinedIncident(int x, int y, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx | dy | dz) != 0 && Book.Cells.TryGetValue(Key(x + dx, y + dy, z + dz), out var c)) return c.Fire;
        return 0;
    }

    private ulong StartIncident(int x, int y, int z, long sec, FireCause cause)
    {
        var parameters = new List<ChronParam> { ChronParam.TextKey("cause", cause.NoteKey), ChronParam.Count("x", x), ChronParam.Count("y", y) };
        var id = Chronicle?.Invoke(new GameTime(sec), "fire_started", "ch_fire_started", parameters, cause.Subjects, cause.Causes,
            [("x", x.ToString()), ("y", y.ToString()), ("z", z.ToString()), ("cause", cause.Kind)]) ?? EntityId.None;
        if (id == EntityId.None) id = EntityId.Make(IdKind.Event, 0xF1E_0000_0000UL | Book.NextSerial++);
        var inc = new FireIncident { Entry = id.Value, StartSec = sec, CauseKind = cause.Kind };
        Book.Incidents[inc.Entry] = inc;
        return inc.Entry;
    }

    private FireCell Add(long key, FuelClass fuel, long sec, ulong incident)
    {
        var c = new FireCell
        {
            Key = key, State = CellFireState.Smouldering, Intensity = 1, StartS = sec, FuelLeftS = fuel.BurnS,
            Fire = incident, Class = (byte)fuel.Index,
        };
        Book.Cells[key] = c;
        return c;
    }

    // ------------------------------------------------------------------ adoption of generated buildings

    private const int AdoptBlock = 32;

    /// <summary>Brings the generated buildings near (x, y) into the room world (every 32 m block within 16 m), so the fire sees them. Idempotent.</summary>
    public void EnsureAdopted(int x, int y)
    {
        if (Adopt == null) return;
        for (int by = Div(y - 16); by <= Div(y + 16); by++)
            for (int bx = Div(x - 16); bx <= Div(x + 16); bx++)
            {
                long k = ((long)bx << 32) ^ (uint)by;
                if (!_adopted.Add(k)) continue;
                var edits = Adopt(bx * AdoptBlock, by * AdoptBlock, bx * AdoptBlock + AdoptBlock - 1, by * AdoptBlock + AdoptBlock - 1).ToList();
                if (edits.Count == 0) continue;
                var change = Rooms.Apply(edits);
                // generated buildings carry no roof piece in their chunks: the fire's copy roofs each enclosed room, in thatch where the walls can burn and slate where they are stone (4.2, 4.5)
                var thatched = change.Touched.Where(st => !StoneWalled(st)).ToList(); var slated = change.Touched.Where(StoneWalled).ToList();
                if (thatched.Count > 0) Rooms.RoofOver("roof_thatch", thatched);
                if (slated.Count > 0) Rooms.RoofOver("roof_slate", slated);
            }
    }

    private bool StoneWalled(int structure)
    {
        bool any = false;
        foreach (var k in Rooms.StructureCells(structure))
        {
            var (x, y, z) = Unpack(k);
            var p = Rooms.PieceAt(x, y, z);
            if (p is { Kind: PieceKind.Wall or PieceKind.Post }) { any = true; if (Fuels.ClassOf(p.Id).Burns) return false; }
        }
        return any;
    }

    private static int Div(int v) => v >= 0 ? v / AdoptBlock : -((-v + AdoptBlock - 1) / AdoptBlock);

    // ------------------------------------------------------------------ the clock

    private readonly List<long> _keys = [];
    private readonly List<long> _spent = [];
    private readonly List<(long Key, byte Class, ulong Fire)> _lit = [];
    private readonly List<long> _doused = [];
    private readonly List<FirePerson> _people = [];

    /// <summary>A cell that has just caught smoulders before it flames: 15 sim-s (daub and turf, which never rise above intensity 1, 40), and a smouldering cell spreads at a fifth.</summary>
    public const int SmoulderS = 15, SmoulderSlowS = 40, SmoulderSpreadPm = 200;

    /// <summary>The longest gap the fire is stepped second by second (8 game hours); a longer one burns what is left out at once (the hero was away: the abstract step would have).</summary>
    public const int MaxCatchUpS = 8 * 180;

    /// <summary>Steps the fire to game second <paramref name="gameSec"/> (whole sim-seconds, in order). Free while nothing burns.</summary>
    public void AdvanceTo(long gameSec)
    {
        if (!Active) return;
        long target = FireClock.SimS(gameSec);
        if (Book.SecDone < 0) Book.SecDone = target - 1;
        if (target - Book.SecDone > MaxCatchUpS) { BurnOutAll(gameSec); Book.SecDone = target; return; }
        while (Book.SecDone < target)
        {
            long s = ++Book.SecDone;
            if (!Active) { Book.SecDone = target; return; }
            Second(s);
        }
    }

    private void Second(long s)
    {
        long sec = s * FireClock.GameSecPerSimS;
        _keys.Clear(); _keys.AddRange(Book.Cells.Keys);
        // spread, sparks and the rain's hand, on the step
        if (s % FireClock.SpreadEveryS == 0 && _keys.Count > 0) SpreadStep(s / FireClock.SpreadEveryS, sec);
        // age, intensity and burn-out
        _spent.Clear();
        bool raining = WeatherAt(sec).Raining;
        foreach (var k in _keys)
        {
            if (!Book.Cells.TryGetValue(k, out var c)) continue;
            var fuel = Fuels.Get(c.Class);
            int age = (int)(s - c.StartS / FireClock.GameSecPerSimS);
            if (c.State == CellFireState.Smouldering && age >= (fuel.MaxIntensity == 1 ? SmoulderSlowS : SmoulderS)) c.State = CellFireState.Burning;
            int rise = Math.Min(fuel.MaxIntensity, 1 + age / 20);
            if (fuel.BurnS >= 120 && c.FuelLeftS <= 20 && rise > 1) rise--;
            if (raining && rise > 2) { var (rx, ry, rz) = Unpack(k); if (ExposedToSky(rx, ry, rz)) rise = 2; }             // rain beats a roof fire down: no cell open to the sky reaches 3, so no sparks
            c.Intensity = (byte)Math.Max(1, rise - c.Knock);
            c.FuelLeftS--;
            var (cx, cy, cz) = Unpack(k);
            if (c.FuelLeftS <= 0 || !FuelAt(cx, cy, cz).Burns) _spent.Add(k);                  // spent, or its piece was taken (it fell, a hook pulled it)
        }
        if (_spent.Count > 0) BurnOut(_spent, sec);
        Goods(s, sec);
        Exposure(s, sec);
        Fighting(s, sec);
        CloseIncidents(sec);
    }

    // ------------------------------------------------------------------ spread and sparks (4.2)

    private long _wxHour = long.MinValue; private FireWeather _wx;
    private FireWeather WeatherAt(long sec)
    {
        if (Weather == null) return FireWeather.Calm;
        long h = sec / 3600;
        if (h != _wxHour) { _wx = Weather(h); _wxHour = h; }
        return _wx;
    }

    /// <summary>The weather's push on a touch or a spark going from the source toward (dx, dy): downwind, across or upwind, permille.</summary>
    /// <summary>Sparks carried across the wind (not along it): a quarter of the downwind carry's half, tuned so a crosswind street loses 30 to 70 percent of its houses (FB-3).</summary>
    public const int AcrossSparkPm = 300;

    public static int WindPm(in FireWeather w, int dx, int dy, bool spark)
    {
        if (w.WindMmS < 2000) return 1000;
        int toX = -SinPm(w.WindFromDeg), toY = CosPm(w.WindFromDeg);
        long nn = Math.Max(1, (long)Math.Sqrt((double)dx * dx + (double)dy * dy) * 1000);
        long cos = ((long)dx * toX + (long)dy * toY) * 1000 / nn;          // cosine of the angle between the push and the wind, permille
        if (cos >= 500) return 2000;
        if (cos <= -500) return spark ? 150 : 300;
        return spark ? AcrossSparkPm : 1000;
    }

    private static int SinPm(int deg) => (int)((long)WeatherMath.SinDeg(Fixed.FromInt(deg)).Raw * 1000 / Fixed.OneRaw);
    private static int CosPm(int deg) => (int)((long)WeatherMath.CosDeg(Fixed.FromInt(deg)).Raw * 1000 / Fixed.OneRaw);

    private int WetPm(long sec, in FireWeather w)
    {
        if (w.Raining) return 200;
        if (Book.LastRainSec >= 0 && sec - Book.LastRainSec <= 4 * 3600) return 300;
        return w.Drought ? 1500 : 1000;
    }

    private static readonly (int Dx, int Dy)[] Around = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private void SpreadStep(long step, long sec)
    {
        var w = WeatherAt(sec);
        if (w.Raining) Book.LastRainSec = sec;
        int wet = WetPm(sec, w);
        _lit.Clear(); _doused.Clear();
        var seen = new HashSet<long>();
        foreach (var k in _keys)
        {
            var c = Book.Cells[k];
            var (x, y, z) = Unpack(k);
            EnsureAdopted(x, y);
            // rain puts a roof fire out (plan 2.10): a chance each step by the rain and how far the fire has got
            if (w.Raining && ExposedToSky(x, y, z))
            {
                int pm = w.RainMmH >= 4 ? 180 : 70;
                pm = pm * (c.Intensity == 3 ? 400 : c.Intensity == 2 ? 700 : 1000) / 1000;
                if (DetRng.Range(Seed, step, (ulong)k, RngStreams.FireSpread, 40, 0, 1000) < pm) { _doused.Add(k); continue; }
            }
            int half = c.State == CellFireState.Smouldering ? SmoulderSpreadPm : 1000;
            for (int d = 0; d < 8; d++)
            {
                var (dx, dy) = Around[d];
                Touch(c, x + dx, y + dy, z, d, dx, dy, step, sec, w, wet, half, seen);
            }
            if (Linked(x, y, z, z + 1)) Touch(c, x, y, z + 1, 8, 0, 0, step, sec, w, wet, half, seen);
            if (Linked(x, y, z, z - 1)) Touch(c, x, y, z - 1, 9, 0, 0, step, sec, w, wet, half, seen);
            if (c.Intensity == 3 && w.WindMmS >= 2000 && Fuels.Get(c.Class).Sparks && IsRoof(x, y, z)) Sparks(c, x, y, z, step, w, wet, seen);
        }
        foreach (var k in _doused) Douse(k, sec);
        foreach (var (key, cls, fire) in _lit)
        {
            if (Book.Cells.ContainsKey(key)) continue;
            Add(key, Fuels.Get(cls), sec, fire);
        }
    }

    private bool IsRoof(int x, int y, int z) => Rooms.PieceAt(x, y, z)?.Kind == PieceKind.Roof;

    /// <summary>Nothing roofs the cell from above: rain falls on it.</summary>
    private bool ExposedToSky(int x, int y, int z)
    {
        for (int zz = z + 1; zz <= z + 3; zz++) if (Rooms.PieceAt(x, y, zz) is { Kind: PieceKind.Roof or PieceKind.Floor or PieceKind.Beam }) return false;
        return true;
    }

    /// <summary>Whether fire passes between the cell and the one above or below it: through a floor, roof, beam or stair, or wall to wall (fire 4.2).</summary>
    private bool Linked(int x, int y, int z, int z2)
    {
        var lo = Rooms.PieceAt(x, y, Math.Min(z, z2)); var hi = Rooms.PieceAt(x, y, Math.Max(z, z2));
        static bool Surface(PieceInfo? p) => p != null && p.Kind is PieceKind.Floor or PieceKind.Roof or PieceKind.Beam or PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor;
        static bool Upright(PieceInfo? p) => p != null && p.Kind is PieceKind.Wall or PieceKind.Post;
        return Surface(lo) || Surface(hi) || (Upright(lo) && Upright(hi));
    }

    private void Touch(FireCell src, int tx, int ty, int tz, int dir, int dx, int dy, long step, long sec, in FireWeather w, int wet, int half, HashSet<long> seen)
    {
        long tk = Key(tx, ty, tz);
        if (Book.Cells.ContainsKey(tk) || seen.Contains(tk)) return;
        var fuel = FuelAt(tx, ty, tz);
        if (!fuel.Burns) { NoteWall(src, tx, ty, tz); return; }
        long p = (long)fuel.SpreadPm * 1000 * src.Intensity / 2 * half / 1000;
        p = p * WindPm(w, dx, dy, false) / 1000;
        p = p * wet / 1000;
        p = p * SoakPm(tk, sec) / 1000;
        p = p * LeakPm(tk, tx, ty, tz, fuel, sec) / 1000;
        if (p <= 0) return;
        if (DetRng.Range(Seed, step, (ulong)tk, RngStreams.FireSpread, (uint)dir, 0, 1_000_000) < p)
        {
            seen.Add(tk);
            _lit.Add((tk, (byte)fuel.Index, src.Fire));
        }
    }

    private int SoakPm(long tk, long sec) => Book.Soaked.TryGetValue(tk, out var until) && sec < until ? 300 : 1000;

    private int LeakPm(long tk, int x, int y, int z, FuelClass fuel, long sec)
    {
        if (fuel.Id != "thatch") return 1000;
        return ConditionPm(tk, fuel, sec) < Fuels.Upkeep.LeakBelowPm ? 1500 : 1000;
    }

    /// <summary>Sparks from a burning roof at intensity 3 (4.2): each thatch or shingle roof cell within 8 m downwind at 20 permille a step, within 15 m at 5 permille, stream 0x6C1.</summary>
    private void Sparks(FireCell src, int x, int y, int z, long step, in FireWeather w, int wet, HashSet<long> seen)
    {
        for (int dy = -15; dy <= 15; dy++)
            for (int dx = -15; dx <= 15; dx++)
            {
                int d2 = dx * dx + dy * dy;
                if (d2 == 0 || d2 > 225) continue;
                int tx = x + dx, ty = y + dy;
                var p = Rooms.PieceAt(tx, ty, z);
                if (p == null || p.Kind != PieceKind.Roof) continue;
                long tk = Key(tx, ty, z);
                if (Book.Cells.ContainsKey(tk) || seen.Contains(tk)) continue;
                var fuel = Fuels.ClassOf(p.Id);
                if (!fuel.Burns || !fuel.Sparks) continue;
                int near = d2 <= 64 ? 20 : 5;
                long pr = (long)near * 1000 * WindPm(w, dx, dy, true) / 1000;
                pr = pr * wet / 1000; pr = pr * SoakPm(tk, step * 5 * FireClock.GameSecPerSimS) / 1000;
                if (pr <= 0) continue;
                if (DetRng.Range(Seed, step, (ulong)src.Key, RngStreams.FireSparks, (uint)((dy + 15) * 31 + dx + 15), 0, 1_000_000) < pr)
                {
                    seen.Add(tk);
                    _lit.Add((tk, (byte)fuel.Index, src.Fire));
                }
            }
    }

    /// <summary>A flame beside a stone wall that does not burn: the natural reason a fire stops there (the faith hook reads it, overview 10a).</summary>
    private void NoteWall(FireCell src, int tx, int ty, int tz)
    {
        if (!Book.Incidents.TryGetValue(src.Fire, out var inc) || inc.StoppedBy.Length > 0) return;
        var p = Rooms.PieceAt(tx, ty, tz);
        if (p != null && p.Kind is PieceKind.Wall or PieceKind.Door or PieceKind.Window && !Fuels.ClassOf(p.Id).Burns)
        {
            int st = Rooms.StructureAt(tx, ty, tz);
            if (st > 0) { inc.StoppedBy = "stone_wall"; inc.StoppedAtStructure = st; }
        }
    }

    private void Douse(long key, long sec)
    {
        Book.Cells.Remove(key);
        Book.Soaked[key] = sec + 2 * 3600;
        if (Book.Cells.Count == 0) { }
    }

    // ------------------------------------------------------------------ burn-out (4.2)

    private void BurnOut(List<long> keys, long sec)
    {
        var edits = new List<CellEdit>();
        var builtTaken = new List<(int X, int Y, int Z)>();
        foreach (var k in keys)
        {
            if (!Book.Cells.TryGetValue(k, out var c)) continue;
            var (x, y, z) = Unpack(k);
            var fuel = Fuels.Get(c.Class);
            var inc = Book.Incidents.GetValueOrDefault(c.Fire);
            if (inc != null) { inc.CellsBurnt++; int st = Rooms.StructureAt(x, y, z); if (st > 0) inc.Structures.Add(st); }
            var s = Rooms.PieceAt(x, y, z); var f = Rooms.FixtureAt(x, y, z);
            bool burnS = s != null && Fuels.ClassOf(s.Id).Burns, burnF = f != null && Fuels.ClassOf(f.Id).Burns;
            Book.Cells.Remove(k);
            Book.Scars[k] = fuel.After;
            var cause = inc != null ? new EntityId(inc.Entry) : EntityId.None;
            // fixtures first (a table before the floor under it), then the structure; a built piece goes through construction (its support rules and its mass audit)
            if (s == null && f == null) CropBurnt?.Invoke(x, y, sec);
            if (burnF && !(Destroy?.Invoke(x, y, z, true, true, sec, cause) ?? false)) edits.Add(new CellEdit(x, y, z, CellLayer.Fixture, null));
            if (burnS && !(Destroy?.Invoke(x, y, z, false, true, sec, cause) ?? false)) edits.Add(new CellEdit(x, y, z, CellLayer.Structure, null));
            if (burnS || burnF) builtTaken.Add((x, y, z));
        }
        if (edits.Count > 0)
        {
            Rooms.Apply(edits);
            if (Tiles != null) foreach (var (x, y, z) in builtTaken) Tiles.Set(x, y, z, Rooms.PieceAt(x, y, z)?.Id, Rooms.FixtureAt(x, y, z)?.Id);
        }
    }

    /// <summary>Burns every cell out at once (a gap longer than <see cref="MaxCatchUpS"/>): the fire did what it would have done.</summary>
    private void BurnOutAll(long sec)
    {
        for (int round = 0; round < 64 && Book.Cells.Count > 0; round++)
        {
            _keys.Clear(); _keys.AddRange(Book.Cells.Keys);
            BurnOut(_keys, sec);
        }
        CloseIncidents(sec);
    }

    // ------------------------------------------------------------------ the end of a fire (4.6)

    private void CloseIncidents(long sec)
    {
        if (Book.Incidents.Count == 0) return;
        List<ulong>? done = null;
        foreach (var inc in Book.Incidents.Values)
        {
            if (inc.Entry == 0) continue;
            bool live = false;
            foreach (var c in Book.Cells.Values) if (c.Fire == inc.Entry) { live = true; break; }
            if (!live) (done ??= []).Add(inc.Entry);
        }
        if (done == null) return;
        foreach (var id in done) End(Book.Incidents[id], sec);
    }

    public sealed record IncidentEnded(ulong Fire, int Structures, int Deaths, long GoodsMites, string StoppedBy, string CauseKind);

    private void End(FireIncident inc, long sec)
    {
        Book.Incidents.Remove(inc.Entry);
        string stop = inc.StoppedBy;
        // a fire that left an altar's building standing: the natural reason it stopped, for faith to read (overview 10a)
        foreach (var st in SacredNear(inc))
        {
            string why = stop.Length > 0 ? stop : WeatherAt(sec).Raining ? "rain" : "wind_change";
            Book.Stops.Add(new FireStop(sec, inc.Entry, st, why, true));
        }
        var ps = new List<ChronParam>
        {
            ChronParam.Count("buildings", inc.Structures.Count), ChronParam.Mites("goods", inc.GoodsMites), ChronParam.Count("deaths", inc.Deaths.Count), ChronParam.Count("cells", inc.CellsBurnt),
        };
        var subjects = inc.Deaths.Select(d => new EntityId(d)).Take(8).ToList();
        Chronicle?.Invoke(new GameTime(sec), "fire_ended", "ch_fire_ended", ps, subjects, [new Cause(CauseKind.Fire, new EntityId(inc.Entry), "", "ch_note_fire_burnt_out")],
            [("buildings", inc.Structures.Count.ToString()), ("deaths", inc.Deaths.Count.ToString()), ("goods_mites", inc.GoodsMites.ToString()), ("cells", inc.CellsBurnt.ToString()),
             ("stopped_by", stop), ("cause", inc.CauseKind)]);
        foreach (var p in inc.Fighters)
            Chronicle?.Invoke(new GameTime(sec), "fire_fought", "ch_fire_fought", [ChronParam.Person("who", new EntityId(p))], [new EntityId(p)],
                [new Cause(CauseKind.Fire, new EntityId(inc.Entry), "", "ch_note_fire_fought")], [("fire", inc.Entry.ToString())]);
        foreach (var p in inc.Burned) if (Book.Exposed.TryGetValue(p, out var ex) && ex.Fire == inc.Entry && ex.StopSec < 0 && ex.SmokeS == 0) Book.Exposed.Remove(p);
        Ended?.Invoke(new IncidentEnded(inc.Entry, inc.Structures.Count, inc.Deaths.Count, inc.GoodsMites, stop, inc.CauseKind));
    }

    /// <summary>Structures with an altar that stand within 12 m of where the fire burned and were not burnt: the places a fire could be said to have stopped at.</summary>
    private IEnumerable<int> SacredNear(FireIncident inc)
    {
        if (inc.CellsBurnt == 0) yield break;
        var burnt = new List<(int X, int Y)>();
        foreach (var (k, _) in Book.Scars) { var (x, y, _) = Unpack(k); burnt.Add((x, y)); if (burnt.Count > 4000) break; }
        var seen = new SortedSet<int>();
        foreach (var st in Rooms.StructureIds)
        {
            if (inc.Structures.Contains(st)) continue;
            var f = Rooms.Facts(st);
            if (f == null) continue;
            bool altar = false; (int X, int Y) at = default;
            foreach (var ck in Rooms.StructureCells(st))
            {
                var (x, y, z) = Unpack(ck);
                if (Rooms.FixtureAt(x, y, z) is { } fx && fx.Tags.Contains("altar")) { altar = true; at = (x, y); break; }
            }
            if (!altar) continue;
            foreach (var (bx, by) in burnt)
                if ((bx - at.X) * (bx - at.X) + (by - at.Y) * (by - at.Y) <= 144) { seen.Add(st); break; }
        }
        foreach (var s in seen) yield return s;
    }

    // ------------------------------------------------------------------ upkeep reads (4.7)

    /// <summary>A piece's condition, permille: 1000 less its class's yearly decay for each game year since it was made or mended (a piece with no mark is kept as new).</summary>
    public int ConditionPm(long key, FuelClass fuel, long sec)
    {
        if (!Book.Made.TryGetValue(key, out var made)) return 1000;
        long years100 = Math.Max(0, sec - made) * 100 / TimeConst.SecondsPerYear;
        return (int)Math.Clamp(1000 - Fuels.DecayPmPerYear(fuel) * years100 / 100, 0, 1000);
    }
}
