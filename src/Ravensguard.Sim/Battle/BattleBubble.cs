using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// One formation's worth of soldiers as the bubble holds them before they are fighters (battle-bubble-and-formations 3.1, 4.1): a record in a side's reserve or in the queue of people entering at the
/// box edge. <see cref="Soldiers"/> are in deploy order, with the kits (the people's own: their persons keep their wound books in the world ledger). When it enters it becomes a <see cref="FormationRow"/>.
/// </summary>
public sealed class BattleUnit
{
    public FormationKind Kind = FormationKind.Infantry;
    public Formation Shape = Formation.Line;
    public int Ranks, Drill;
    public List<FighterKit> Soldiers = [];
    /// <summary>Indices into <see cref="Soldiers"/>: the officers (men-at-arms and knights are captain first), the banner bearer (-1 none), and the Leadership of each soldier (empty: all 0).</summary>
    public int[] Officers = [];
    public int Banner = -1;
    public int[] Leaders = [];
    // ---- while it enters ----
    /// <summary>The wave it belongs to (0: the opening) and where its first rank forms; the fighters entered so far (ids in soldier order).</summary>
    public int Wave, AnchorX, AnchorY, FormId;
    public List<int> Ids = [];
    public int Count => Soldiers.Count;
    public int Entered => Ids.Count;

    /// <summary>The first <paramref name="n"/> soldiers as a unit of their own (a wave smaller than a formation), leaving the rest here.</summary>
    internal BattleUnit Split(int n)
    {
        var head = new BattleUnit { Kind = Kind, Shape = Shape, Ranks = Ranks, Drill = Drill, Soldiers = Soldiers.GetRange(0, n) };
        head.Officers = [.. Officers.Where(i => i < n)]; head.Leaders = Leaders.Length == 0 ? [] : [.. Leaders.Take(n)]; head.Banner = Banner >= 0 && Banner < n ? Banner : -1;
        Soldiers.RemoveRange(0, n);
        Officers = [.. Officers.Where(i => i >= n).Select(i => i - n)]; if (Leaders.Length > 0) Leaders = [.. Leaders.Skip(n)]; Banner = Banner >= n ? Banner - n : -1;
        return head;
    }
}

/// <summary>One side as the opening is asked for it: its team, realm, commander, host, what kind of side it is and its units in deploy order (the first are live, the rest wait in the reserve).</summary>
public sealed class BattleSideSpec
{
    public int Team, Realm;
    public ulong Commander, HostId;
    public SideKind Kind = SideKind.Army;
    public List<BattleUnit> Units = [];
    /// <summary>Where the side's road meets the box, or null for the middle of the box edge behind the side's line.</summary>
    public (int X, int Y)? Entry;
    /// <summary>The player's fighter: formation <see cref="HeroUnit"/>'s soldier <see cref="HeroIndex"/> (-1: none), with this Leadership. The side then takes the player's orders; the other sides' captains run their formations always.</summary>
    public int HeroUnit = -1, HeroIndex = -1, HeroLeadership = 50;
    /// <summary>P8-LW-3: the owners (a warband's lord, a host's baggage or commander) whose war wagons and chariots in <see cref="BattleOpening.Rides"/> come onto the field with this side.</summary>
    public List<Ravensguard.Sim.Core.EntityId> VehicleOwners = [];
}

/// <summary>What opens a battle: the fight's identity, where its frame sits on the continent, the sides, the battle size, the cause and place for the chronicle.</summary>
public sealed class BattleOpening
{
    public ulong Id = 1, Seed = 1;
    public long GameSec = 8 * 3600;
    /// <summary>Where the fight's (0, 0) is on the continent, millimetres: chunks, the live bubbles and the paused ambient simulation are in that frame.</summary>
    public (long X, long Y) OriginMm;
    /// <summary>The battle-size setting (200..2,000); 0: the content's default.</summary>
    public int CapTotal;
    /// <summary>P8-LW-1: the wave threshold in permille of a side's share (0: the content's).</summary>
    public int WaveUnderPm;
    public List<BattleSideSpec> Sides = [];
    /// <summary>P8-LW-3: the ride book the sides' <see cref="BattleSideSpec.VehicleOwners"/> keep their war wagons and chariots in; null: no vehicles come (<see cref="BattleVehicles"/>).</summary>
    public Ravensguard.Sim.Animals.RideBook? Rides;
    /// <summary>P8-LW-3: no wagon fort on this field (an assault's walls are its own); chariots still come.</summary>
    public bool NoFort;
    /// <summary>The distance between the two front lines at the opening (the lines of three or four sides stand on the axes), mm.</summary>
    public int GapMm = 150_000;
    /// <summary>The chronicle entry that brought the sides together (a campaign, a raid, an ambush) and how; none: the battle entry is a root.</summary>
    public CauseKind CauseKind = CauseKind.Battle;
    public EntityId CauseEntry = EntityId.None;
    public string CauseNoteKey = "ch_note_bt_field";
    public EntityId Place = EntityId.None;
    public WoundLedger? Ledger;
    public FightTerrain? Terrain;
    public string Weather = "";
}

/// <summary>The side as the HUD and the chronicle read it.</summary>
public readonly record struct BattleCounts(int Team, int Live, int Cap, int Reserve, int Entering, int Lost, int Men, int Waves);

/// <summary>
/// The battle bubble (battle-bubble-and-formations 3.1, 4.1; plan 2.3): the live area of a big fight. It owns the <see cref="FightWorld"/> of the battle and adds what the fight does not know: the <see cref="Box"/> (the
/// engagement's extent plus 150 m, clipped to a square kilometre), each side's live cap and reserve, the soldiers entering at the box edge (at most 50 a tick, over a few ticks), reinforcement waves, the ambient chunks
/// that pause around the box, the close and what the battle leaves in the chronicle. Everything is integers and stable-id order; nothing reads a camera.
/// <para>
/// <b>Money, goods, labour.</b> The bubble creates and moves none (overview T3): soldiers are the people and kits it is given, wounds stay in the world ledger, nothing is paid or taken.
/// </para>
/// </summary>
public sealed partial class BattleBubble
{
    public ulong Id { get; private set; }
    public FightWorld W { get; private set; } = null!;
    public BubbleRulesDef Rules { get; private set; } = null!;
    /// <summary>The box in the fight's frame, millimetres: minimum and maximum.</summary>
    public (int X0, int Y0, int X1, int Y1) Box { get; private set; }
    public (long X, long Y) OriginMm { get; private set; }
    public int CapTotal { get; private set; }
    public List<BattleSide> Sides { get; } = [];
    public CauseKind CauseKind { get; private set; }
    public EntityId CauseEntry { get; private set; }
    public string CauseNoteKey { get; private set; } = "";
    public EntityId Place { get; private set; }
    public int StartTick { get; private set; }
    public bool Closed { get; private set; }
    public BattleResult? Result { get; private set; }
    /// <summary>Where the chronicle entries go (the world's outbox, or a test's list); null: none are written.</summary>
    public BattleChronicleSink? Chronicle { get; set; }
    /// <summary>The fight id of the hero's fighter, or -1.</summary>
    public int Hero { get; private set; } = -1;

    internal bool ContactSeen;
    internal int LastEngaged;
    internal bool Truce;
    /// <summary>Named captains (a person is behind them) by fighter id at the opening and for each wave: the chronicle keeps the dead ones.</summary>
    internal readonly List<int> NamedCaptains = [];
    private HashSet<long> _paused = [];

    /// <summary>Entering soldiers over all sides, for the diagnostics.</summary>
    public int MaxEnteredInATick { get; private set; }
    public int Entering => Sides.Sum(s => s.Pending.Sum(u => u.Count - u.Entered));

    // ------------------------------------------------------------------ opening

    /// <summary>
    /// Opens a battle: caps and the opening live set per side (whole formations in deploy order, within the cap), the lines laid out facing each other, the box, the entry points, the paused chunks. The people of the
    /// opening are in the bubble already, so they stand formed where the lines are (no entering, no pop); the reserve waits at the entry point.
    /// </summary>
    public static BattleBubble Open(FightContent content, BattleOpening o)
    {
        var rules = FormationRules.For(content.Db);
        var R = rules.Bubble;
        var b = new BattleBubble { Id = o.Id, Rules = R, OriginMm = o.OriginMm, CauseKind = o.CauseKind, CauseEntry = o.CauseEntry, CauseNoteKey = o.CauseNoteKey, Place = o.Place };
        b.CapTotal = Math.Clamp(o.CapTotal > 0 ? o.CapTotal : R.CapTotalDefault, R.CapTotalMin, R.CapTotalMax);
        b.WaveUnderPm = o.WaveUnderPm;
        var w = new FightWorld(content, o.Seed, o.Id, o.GameSec, o.Ledger) { RecordEvents = false, Rules = FightRules.For(FightKind.Raid), SetupKind = FightKind.Raid };
        b.W = w;
        if (o.Terrain != null) w.UseTerrain(o.Terrain);
        if (FightWorld.WeatherRowOf(content.Db, o.Weather) is { } wr) w.WeatherRow = wr;
        var book = w.EnsureFormations();

        int total = o.Sides.Sum(s => s.Units.Sum(u => u.Count));
        for (int i = 0; i < o.Sides.Count; i++)
        {
            var sp = o.Sides[i];
            int men = sp.Units.Sum(u => u.Count);
            var side = new BattleSide { Team = sp.Team, Realm = sp.Realm, Commander = sp.Commander, HostId = sp.HostId, Kind = sp.Kind, Strength = men, Human = sp.HeroUnit >= 0, NextForm = 1 };
            int share = total == 0 ? 0 : (int)((long)b.CapTotal * men / total);
            side.LiveCap = Math.Min(men, Math.Max(b.CapTotal / Math.Max(1, R.MinCapDiv), share));
            side.Reserve = [.. sp.Units];
            b.Sides.Add(side);
        }

        // the opening live set: whole formations in deploy order within the cap (the first unit is cut down to the cap when it alone is over it)
        var live = new List<List<BattleUnit>>();
        for (int i = 0; i < b.Sides.Count; i++)
        {
            var s = b.Sides[i];
            var units = b.TakeFromReserve(s, s.LiveCap);
            foreach (var u in units) u.Wave = 0;
            live.Add(units);
        }

        // the lines: side i faces the middle from its own direction (two sides: west and east), each side's formations in lines of at most LineWidthMax, the front line GapMm / 2 from the middle
        var dirs = new (int X, int Y)[] { (1024, 0), (-1024, 0), (0, 1024), (0, -1024) };
        var faces = new (int X, int Y)[b.Sides.Count];
        for (int i = 0; i < b.Sides.Count; i++)
        {
            var s = b.Sides[i]; var sp = o.Sides[i];
            var f = dirs[i % 4]; faces[i] = f; s.FaceX = f.X; s.FaceY = f.Y;
            var placed = b.LayOut(rules, live[i], 0, 0, f.X, f.Y, o.GapMm / 2, centred: true);
            for (int k = 0; k < live[i].Count; k++)
            {
                var u = live[i][k];
                var (ax, ay) = placed[k];
                u.AnchorX = ax; u.AnchorY = ay;
                b.MakeFighters(u, s, f.X, f.Y, sp.HeroUnit == k ? sp.HeroIndex : -1);
                b.FormUnit(u, s, sp.HeroUnit == k ? sp.HeroIndex : -1, sp.HeroLeadership);
            }
        }
        // the box: the live men's extent and the entry points of the reserves, plus the margin; clipped to the area limit around the hero's side or the larger group
        long mnX = long.MaxValue, mnY = long.MaxValue, mxX = long.MinValue, mxY = long.MinValue;
        foreach (var f in w.Fighters) { mnX = Math.Min(mnX, f.X); mxX = Math.Max(mxX, f.X); mnY = Math.Min(mnY, f.Y); mxY = Math.Max(mxY, f.Y); }
        if (w.Fighters.Count == 0) { mnX = mnY = mxX = mxY = 0; }
        b.Box = ClipBox(R, mnX - R.BoxMarginMm, mnY - R.BoxMarginMm, mxX + R.BoxMarginMm, mxY + R.BoxMarginMm, b.HeroCentre() ?? b.LargerGroupCentre(w));
        for (int i = 0; i < b.Sides.Count; i++)
        {
            var s = b.Sides[i]; var sp = o.Sides[i];
            var (ex, ey) = sp.Entry ?? b.DefaultEntry(i, faces[i], live[i]);
            s.EntryX = ex; s.EntryY = ey;
            book.SetEntry(s.Team, ex, ey);
        }
        foreach (var r in book.Rows) r.CaptainAlways = !b.Sides.First(s => s.Team == r.Side).Human;
        var (cx, cy) = b.HeroCentre() ?? ((b.Box.X0 + b.Box.X1) / 2, (b.Box.Y0 + b.Box.Y1) / 2);
        w.LodCentre = (cx, cy); if (b.Hero >= 0) w.LodFighter = b.Hero;                            // the AI thinks at 20 Hz near the hero, else near the middle of the box, 5 Hz beyond (never by the camera)
        b.RecomputePaused();
        b.StartTick = w.Tick;
        BattleVehicles.Attach(b, o);                                   // P8-LW-3: the owners' war wagons and chariots, when the opening names a ride book
        return b;
    }

    private (int X, int Y)? HeroCentre()
    {
        if (Hero < 0) return null;
        var f = W.Fighters[Hero];
        return (f.X, f.Y);
    }

    private (int X, int Y) LargerGroupCentre(FightWorld w)
    {
        int best = -1, bestN = -1;
        for (int i = 0; i < Sides.Count; i++) { int n = Sides[i].Strength; if (n > bestN) { bestN = n; best = i; } }
        if (best < 0) return (0, 0);
        long sx = 0, sy = 0; int c = 0;
        foreach (var f in w.Fighters) if (f.Team == Sides[best].Team) { sx += f.X; sy += f.Y; c++; }
        return c == 0 ? (0, 0) : ((int)(sx / c), (int)(sy / c));
    }

    /// <summary>The box clipped to <c>MaxSideM</c> on a side and <c>MaxAreaM2</c> in all, centred on <paramref name="centre"/> when it has to be cut (at most 1,400 m on a side, at most a square kilometre).</summary>
    public static (int X0, int Y0, int X1, int Y1) ClipBox(BubbleRulesDef R, long x0, long y0, long x1, long y1, (int X, int Y) centre)
    {
        long maxSide = R.MaxSideM * 1000L;
        long w = x1 - x0, h = y1 - y0;
        long nw = Math.Min(w, maxSide), nh = Math.Min(h, maxSide);
        long maxArea = R.MaxAreaM2 * 1_000_000L;                                                 // mm2
        if (nw * nh > maxArea) { if (nw >= nh) nw = maxArea / nh; else nh = maxArea / nw; }
        if (nw == w && nh == h) return ((int)x0, (int)y0, (int)x1, (int)y1);
        long cx = Math.Clamp(centre.X, x0, x1), cy = Math.Clamp(centre.Y, y0, y1);
        long bx0 = Math.Clamp(cx - nw / 2, x0, x1 - nw), by0 = Math.Clamp(cy - nh / 2, y0, y1 - nh);
        return ((int)bx0, (int)by0, (int)(bx0 + nw), (int)(by0 + nh));
    }

    /// <summary>The middle of the box edge behind a side's line (the road it came by), clipped to the box.</summary>
    private (int, int) DefaultEntry(int side, (int X, int Y) face, List<BattleUnit> live)
    {
        long sx = 0, sy = 0; int n = 0;
        foreach (var f in W.Fighters) if (f.Team == Sides[side].Team) { sx += f.X; sy += f.Y; n++; }
        int mx = n == 0 ? 0 : (int)(sx / n), my = n == 0 ? 0 : (int)(sy / n);
        int ex = face.X > 0 ? Box.X0 : face.X < 0 ? Box.X1 : Math.Clamp(mx, Box.X0, Box.X1);
        int ey = face.Y > 0 ? Box.Y0 : face.Y < 0 ? Box.Y1 : Math.Clamp(my, Box.Y0, Box.Y1);
        return (ex, ey);
    }

    // ------------------------------------------------------------------ units to fighters

    /// <summary>
    /// Takes whole units from the front of a side's reserve while they fit within <paramref name="max"/> soldiers; if the first does not fit, the first <paramref name="max"/> of it as a unit of its own (so a wave or an
    /// opening is never over the cap and never empty while the side has men).
    /// </summary>
    internal List<BattleUnit> TakeFromReserve(BattleSide s, int max)
    {
        var taken = new List<BattleUnit>(); int sum = 0;
        while (s.Reserve.Count > 0 && sum + s.Reserve[0].Count <= max) { var u = s.Reserve[0]; s.Reserve.RemoveAt(0); sum += u.Count; taken.Add(u); }
        if (taken.Count == 0 && s.Reserve.Count > 0 && max > 0 && s.Reserve[0].Count > max) taken.Add(s.Reserve[0].Split(max));
        return taken;
    }

    /// <summary>Lays a row of units out across a line at <paramref name="dist"/> from (0, 0) behind a front facing <paramref name="fx"/>,<paramref name="fy"/>; returns each unit's anchor (centre front). More than the line's width wraps to a second line behind.</summary>
    private (int, int)[] LayOut(FormationRules rules, List<BattleUnit> units, int cx, int cy, int fx, int fy, int dist, bool centred)
    {
        var R = rules.Bubble;
        int px = -fy, py = fx;                                                                    // lateral: the facing turned a quarter
        var res = new (int, int)[units.Count];
        var lines = new List<List<int>> { new() }; var widths = new List<long> { 0 };
        var frontage = new long[units.Count]; var depth = new long[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            var def = rules.Shape(units[i].Shape, units[i].Kind);
            int ranks = units[i].Ranks > 0 ? units[i].Ranks : Math.Max(1, def.Ranks);
            int files = Math.Max(1, (units[i].Count + ranks - 1) / ranks);
            frontage[i] = (long)files * def.LateralMm; depth[i] = (long)ranks * def.RankMm;
            long add = frontage[i] + (lines[^1].Count > 0 ? R.FormationGapMm : 0);
            if (lines[^1].Count > 0 && widths[^1] + add > R.LineWidthMaxMm) { lines.Add([]); widths.Add(0); add = frontage[i]; }
            lines[^1].Add(i); widths[^1] += add;
        }
        long back = 0;
        for (int L = 0; L < lines.Count; L++)
        {
            long start = centred ? -widths[L] / 2 : 0, cur = start, maxDepth = 0;
            foreach (int i in lines[L])
            {
                long lat = cur + frontage[i] / 2;
                long d = dist + back;
                // the line's front is d from the middle on the side the men come from: the anchor is -d along the facing, lat along the lateral
                res[i] = ((int)(cx + ((lat * px - d * fx) >> 10)), (int)(cy + ((lat * py - d * fy) >> 10)));
                cur += frontage[i] + R.FormationGapMm; maxDepth = Math.Max(maxDepth, depth[i]);
            }
            back += maxDepth + R.LineGapMm;
        }
        return res;
    }

    /// <summary>The fighters of a unit, one after another in soldier order, standing on the slots of a line at its anchor (the fighters' own ids are the book's members).</summary>
    private void MakeFighters(BattleUnit u, BattleSide s, int fx, int fy, int heroIndex)
    {
        var def = FormationRules.For(W.Content.Db).Shape(u.Shape, u.Kind);
        int ranks = u.Ranks > 0 ? u.Ranks : Math.Max(1, def.Ranks);
        int files = Math.Max(1, (u.Count + ranks - 1) / ranks);
        for (int j = 0; j < u.Count; j++) AddOne(u, s, j, files, def, fx, fy, j == heroIndex);
    }

    private void AddOne(BattleUnit u, BattleSide s, int j, int files, FormationShapeDef def, int fx, int fy, bool hero)
    {
        int px = -fy, py = fx;
        long lat = (long)(j % files - files / 2) * def.LateralMm, back = (long)(j / files) * def.RankMm;
        int x = (int)(u.AnchorX + ((lat * px - back * fx) >> 10)), y = (int)(u.AnchorY + ((lat * py - back * fy) >> 10));
        var kit = u.Soldiers[j] with { Team = s.Team, X = x, Y = y, FaceX = fx, FaceY = fy };
        var f = W.Add(kit, player: hero);
        W.EnsureMorale(f); f.M.Side = s.Kind;                                                      // what kind of side he is on (who takes a surrender): per fighter, so set as he joins
        u.Ids.Add(f.Id);
        if (hero) Hero = f.Id;
    }

    /// <summary>Forms the unit's formation from its fighters (all entered) and numbers it on its side; the hero, when he is among them, becomes the book's hero.</summary>
    private void FormUnit(BattleUnit u, BattleSide s, int heroIndex, int heroLeadership)
    {
        var book = W.EnsureFormations();
        var officers = new HashSet<int>(); foreach (int i in u.Officers) if (i < u.Ids.Count) officers.Add(u.Ids[i]);
        Dictionary<int, int>? lead = null;
        if (u.Leaders.Length > 0) { lead = new(); for (int i = 0; i < u.Ids.Count && i < u.Leaders.Length; i++) lead[u.Ids[i]] = u.Leaders[i]; }
        int fx = s.FaceX, fy = s.FaceY;
        byte id = (byte)Math.Min(255, s.NextForm++);
        u.FormId = id;
        var r = book.Form(W, s.Team, id, u.Kind, u.Ids, u.AnchorX, u.AnchorY, fx, fy, u.Shape, u.Ranks, u.Drill, officers, lead, u.Banner >= 0 && u.Banner < u.Ids.Count ? u.Ids[u.Banner] : -1, place: true);
        r.CaptainAlways = !s.Human;
        if (r.Captain >= 0 && W.Fighters[r.Captain].Kit.Person != 0) NamedCaptains.Add(r.Captain);
        if (heroIndex >= 0 && heroIndex < u.Ids.Count) book.SetHero(W, u.Ids[heroIndex], heroLeadership);
    }

    // ------------------------------------------------------------------ the paused chunks

    /// <summary>The key of the chunk (cx, cy) of the continent frame, as the paused set holds it.</summary>
    public static long ChunkKey(int cx, int cy) => (long)cx << 32 | (uint)cy;

    private static int FloorDiv(long a, long b) => (int)(a >= 0 ? a / b : -((-a + b - 1) / b));

    /// <summary>The ambient chunks around the box: those within 200 m of it that the box does not touch (inside the box people stay live and flee by their own sense).</summary>
    private void RecomputePaused()
    {
        long cm = Rules.ChunkM * 1000L;
        long bx0 = OriginMm.X + Box.X0, by0 = OriginMm.Y + Box.Y0, bx1 = OriginMm.X + Box.X1, by1 = OriginMm.Y + Box.Y1;
        int inX0 = FloorDiv(bx0, cm), inX1 = FloorDiv(bx1 - 1, cm), inY0 = FloorDiv(by0, cm), inY1 = FloorDiv(by1 - 1, cm);
        int cx0 = FloorDiv(bx0 - Rules.PausedMarginMm, cm), cx1 = FloorDiv(bx1 + Rules.PausedMarginMm - 1, cm), cy0 = FloorDiv(by0 - Rules.PausedMarginMm, cm), cy1 = FloorDiv(by1 + Rules.PausedMarginMm - 1, cm);
        var set = new HashSet<long>();
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
                if (!(cx >= inX0 && cx <= inX1 && cy >= inY0 && cy <= inY1)) set.Add(ChunkKey(cx, cy));
        _paused = set;
    }

    /// <summary>The chunk keys whose ambient simulation is paused while the bubble is open (none once it has closed), ascending.</summary>
    public IReadOnlyList<long> PausedChunks { get { if (Closed) return []; var l = _paused.ToList(); l.Sort(); return l; } }

    /// <summary>True when the continent point (millimetres) lies in a chunk whose ambient simulation is paused. The LODBridge asks it for each live person each step.</summary>
    public bool IsPausedAt(long contX, long contY)
    {
        if (Closed || _paused.Count == 0) return false;
        long cm = Rules.ChunkM * 1000L;
        return _paused.Contains(ChunkKey(FloorDiv(contX, cm), FloorDiv(contY, cm)));
    }

    /// <summary>True when the point of the fight's frame is inside the box.</summary>
    public bool InBox(int x, int y) => x >= Box.X0 && x <= Box.X1 && y >= Box.Y0 && y <= Box.Y1;

    // ------------------------------------------------------------------ counts

    /// <summary>The live count of a side: its fighters still in the fight (standing, down ones not counted) plus nothing waiting.</summary>
    public int LiveOf(int team) { int n = 0; foreach (var f in W.Fighters) if (f.Team == team && f.Active) n++; return n; }

    public BattleCounts CountsOf(BattleSide s)
    {
        int live = 0, lost = 0;
        foreach (var f in W.Fighters) { if (f.Team != s.Team) continue; if (f.Active) live++; else lost++; }
        int entering = 0; foreach (var u in s.Pending) entering += u.Count - u.Entered;
        int reserve = 0; foreach (var u in s.Reserve) reserve += u.Count;
        return new BattleCounts(s.Team, live, s.LiveCap, reserve, entering, lost, s.Strength, s.WavesSent);
    }

    public BattleSide? SideOf(int team) { foreach (var s in Sides) if (s.Team == team) return s; return null; }
}

/// <summary>One side of a bubble: its identity, live cap, the units waiting in the reserve and those entering, its waves.</summary>
public sealed class BattleSide
{
    public int Team, Realm;
    public ulong Commander, HostId;
    public SideKind Kind;
    public int LiveCap, Strength, WavesSent, EntryX, EntryY, FaceX, FaceY, NextForm;
    public int LastWaveTick = -1_000_000;
    public bool Human;
    public List<BattleUnit> Reserve = [];
    public List<BattleUnit> Pending = [];
}

/// <summary>Writes one chronicle entry (the outbox's keyed overload, or a test's chronicle) and returns its id.</summary>
public delegate EntityId BattleChronicleSink(GameTime when, string typeId, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, (string Key, string Value)[] data);
