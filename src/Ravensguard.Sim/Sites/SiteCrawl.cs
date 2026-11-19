using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The hero (and party) inside a site (exploration-and-sites 4.4 to 4.10): one <see cref="Step"/> is one game second, a twentieth of a sim-second, so a torch burns the same at
/// 1x and 8x (the live tick steps the crawl <c>speed</c> times; EX-5). Movement on the site grid in millimetres, the carried lights and the light grid, the auto-map and the
/// breadcrumbs, the hazards (bad air, water, cave-ins), traps, the inhabitants' brains and, when they close in, a fight through <see cref="FightSetup.SiteInhabitants"/>.
/// Integer only; draws key on game time and ids (streams 0x4B3 to 0x4B9); inputs apply at the next step.
/// </summary>
public sealed partial class SiteCrawl
{
    public SiteMap Map { get; }
    public SiteContent C { get; }
    public FightContent Fc { get; }
    public SiteBlows Blows { get; }
    public WoundLedger Ledger { get; }
    public LevelGrid Grid { get; private set; }
    public LevelPathfinder Pathfinder { get; private set; }
    public LightGrid Lights { get; }
    public AutoMap Known { get; private set; }
    public List<CrawlMember> Members { get; } = [];
    public CrawlMember Hero => Members[0];
    public long GameSec { get; private set; }
    public long Steps { get; internal set; }
    public ulong Seed { get; }
    public List<CrawlEvent> Events { get; } = [];
    /// <summary>Chronicle entries asked for (type, data) for the owner to write at its barrier: a taboo broken, a monster slain, a fight's dead.</summary>
    public List<(string Type, Dictionary<string, string> Data)> Chronicle { get; } = [];
    public CrawlSnapshot Snapshot { get; internal set; } = null!;
    public bool Ended { get; private set; }
    /// <summary>The region distance (metres) to the nearest surgeon, the town's name, for the HUD (4.10).</summary>
    public SurgeonInfo Surgeon { get; set; } = SurgeonInfo.None;
    /// <summary>Weather above (cave-ins x2 in heavy rain) and the hour's local clock (the cave beast sleeps by day).</summary>
    public bool HeavyRainAbove { get; set; }
    public bool Resting { get; internal set; }
    public bool TabooBroken { get; internal set; }
    /// <summary>The crawl steps its own fight (tests, a standalone crawl); false when the live world owns it (<c>LiveWorld.AttachFight</c>).</summary>
    public bool OwnsFight { get; set; } = true;
    /// <summary>The hero fights under the AI (watch mode, tests, the autoplay); false: the player's inputs drive the hero's fighter.</summary>
    public bool HeroAi { get; set; }
    /// <summary>The auto-dress stop-gap after a fight (the game's setting; saved with the crawl).</summary>
    public bool AutoDress { get; set; } = true;

    private readonly Queue<CrawlInput> _inputs = new();
    private LightFramePair _pair;
    private LightFrame _frame;
    private readonly List<LightSource> _dyn = [];
    private bool _lightDirty = true, _mapDirty = true;
    private int _loudWindow = -1;
    public LightFrame Frame => _frame;

    public SiteCrawl(SiteMap map, SiteContent c, FightContent fc, WoundLedger ledger, long gameSec, ulong seed, AutoMap? known = null)
    {
        Map = map; C = c; Fc = fc; Ledger = ledger; GameSec = gameSec; Seed = seed ^ 0xC4A5_0000_0000_0000UL;
        Blows = new SiteBlows(fc);
        Grid = map.BuildGrid(); Pathfinder = new LevelPathfinder(Grid);
        Lights = map.BuildLight(); _pair = new LightFramePair(Lights);
        Known = known ?? new AutoMap(map.W, map.H, map.Levels);
        _frame = _pair.Next(new Ambient(0, 0, DayPhase.Night), null);
        Snapshot = Publish();
    }

    /// <summary>Adds a person at the mouth (the hero first). The body comes from the kit, the wound book from the shared ledger.</summary>
    public CrawlMember Add(CrawlMember m)
    {
        m.Plan = Fc.T.Plan(m.Kit.Plan);
        m.Worn = Blows.Worn(m.Kit);
        m.Book = Ledger.For(m.Person, m.Plan, GameSec, m.Kit.SizeCm3, m.Kit.Toughness);
        if (m.X == 0 && m.Y == 0) { var (l, x, y) = Map.At(Map.Mouth); m.Level = l; m.X = x * 1000 + 500; m.Y = y * 1000 + 500 + Members.Count * 0; }
        m.Crumbs = new Breadcrumbs(C.K.BreadcrumbCells);
        m.Hero = Members.Count == 0;
        Members.Add(m);
        m.Cell = CellOf(m);
        m.Crumbs.Add(m.Cell);
        if (m.Hero) Note(CrawlEventKind.Entered, m.Level);
        _lightDirty = _mapDirty = true;
        Snapshot = Publish();
        return m;
    }

    /// <summary>The member's wound book: the ledger's (a fight writes there, and the ledger drops a book with nothing in it).</summary>
    public WoundBook BookOf(CrawlMember m)
    {
        lock (Ledger.Sync)
            if (Ledger.Books.TryGetValue(m.Person, out var b)) { m.Book = b; return b; }
        return m.Book = Ledger.For(m.Person, m.Plan, GameSec, m.Kit.SizeCm3, m.Kit.Toughness);
    }

    public int CellOf(CrawlMember m) => Map.Index(m.Level, Math.Clamp(m.X / 1000, 0, Map.W - 1), Math.Clamp(m.Y / 1000, 0, Map.H - 1));
    public int CellOf(Inhabitant n) => Map.Index(n.Level, Math.Clamp(n.X / 1000, 0, Map.W - 1), Math.Clamp(n.Y / 1000, 0, Map.H - 1));
    public int DaySec => (int)(GameSec % 86400);
    public int Day => (int)(GameSec / 86400);
    public long SimSecond => GameSec / 20;

    public void Input(CrawlInput i) => _inputs.Enqueue(i);

    internal void Note(CrawlEventKind k, int a = 0, int b = 0, string text = "") => Events.Add(new CrawlEvent(GameSec, k, a, b, text));

    /// <summary>Events since the last call (the view toasts them).</summary>
    public List<CrawlEvent> TakeEvents() { var e = new List<CrawlEvent>(Events); Events.Clear(); return e; }

    // ------------------------------------------------------------------ the step

    /// <summary>The lock shared with the site book (a save captures the crawl from the tick worker while the main thread steps it).</summary>
    public object Sync { get; set; } = new();

    /// <summary>One game second (a twentieth of a sim-second).</summary>
    public void Step() { lock (Sync) StepCore(); }

    private void StepCore()
    {
        if (Ended) return;
        GameSec++; Steps++;
        if (OwnsFight) Ledger.AdvanceTo(GameSec);         // standalone: the crawl keeps the wounds' clock (the live world does it when it owns the crawl)
        foreach (var m in Members) BookOf(m);              // the ledger drops a healed book; the member's is always the ledger's
        bool second = GameSec % 20 == 0;
        while (_inputs.Count > 0) Apply(_inputs.Dequeue());
        if (Fight == null)
        {
            foreach (var m in Members) if (m.Active) MoveMember(m);
        }
        foreach (var m in Members) if (m.Active && SiteLight.Burn(ref m.Light)) { Note(CrawlEventKind.TorchOut, Members.IndexOf(m)); _lightDirty = true; }
        foreach (var m in Members) if (m.Active && SiteLight.Guttering(m.Light, C.K) && m.Light.LeftMilliS == C.K.GutterS * 1000L) { Note(CrawlEventKind.TorchGuttering, Members.IndexOf(m)); _lightDirty = true; }
        if (second) PerSecond();
        if (GameSec % 3600 == 0) PerHour();
        if (Fight != null) FightStep();
        else Brains(GameSec % 4 == 0);
        RefreshLight();
        if (Hero.Dead || Members.All(m => m.Dead)) Ended = true;
        Snapshot = Publish();
    }

    private void Apply(CrawlInput i)
    {
        var h = Hero;
        switch (i.Kind)
        {
            case CrawlInputKind.Move:
                h.MoveDir = i.A; h.Mode = (i.B & 1) != 0 ? CrawlMode.Sprint : (i.B & 2) != 0 ? CrawlMode.Walk : CrawlMode.Jog; h.Sneak = (i.B & 4) != 0;
                if (i.A >= 0) { StopPath(h, false); StopRest(); }
                break;
            case CrawlInputKind.MoveTo: GoTo(h, i.A); break;
            case CrawlInputKind.Stop: h.MoveDir = -1; StopPath(h, false); break;
            case CrawlInputKind.Act: Act(h); break;
            case CrawlInputKind.Torch: ToggleLight(h); break;
            case CrawlInputKind.Retrace: Retrace(h); break;
            case CrawlInputKind.Mark: Known.Pins.Add(new MapPin(h.Level, h.Cell, 1)); Note(CrawlEventKind.Marked, h.Cell); break;
            case CrawlInputKind.PullLadder: PullLadder(h); break;
            case CrawlInputKind.Rest: StartRest(h); break;
            case CrawlInputKind.Burn: Burn(h); break;
            case CrawlInputKind.Leave: if (h.Cell == Map.Mouth) Leave(); break;
            case CrawlInputKind.DragOut: DragOut(h); break;
            case CrawlInputKind.Treat: if (!Treat(h)) Note(CrawlEventKind.NothingHere); break;
        }
    }

    public void Leave()
    {
        if (Fight != null) return;
        Ended = true;
        Note(CrawlEventKind.Left);
    }

    // ------------------------------------------------------------------ movement

    private static readonly (int Dx, int Dy)[] Dirs = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    /// <summary>Speed in mm per sim-second: walk 1.5, jog 3, sprint 5 m/s; sneaking halves it; bad air, water, a load over capacity and being lost slow it (4.5, 4.10).</summary>
    public int SpeedMmps(CrawlMember m)
    {
        if (!m.CanAct || m.DragBy >= 0 && m.Unconscious) return 0;
        int s = m.Mode switch { CrawlMode.Walk => 1500, CrawlMode.Sprint => 5000, _ => 3000 };
        if (m.Sneak) s /= 2;
        var air = SiteLight.Air(Map.Air[m.Cell], C.K);
        s = s * air.SpeedPm / 1000;
        int water = Map.Water(m.Cell);
        if (water > C.K.SwimCm) s = Math.Min(s, C.K.SwimMmps);
        else if (water >= C.K.WadeSlowCm) s = s * 600 / 1000;
        int carried = m.GearG + m.LootG;
        if (carried > m.CapacityG && m.CapacityG > 0) s = (int)Math.Max(300, (long)s * (2L * m.CapacityG - carried) / m.CapacityG);
        if (m.Lost) s /= 2;
        var bs = m.Book.Function(m.Plan);
        s = s * Math.Clamp(bs.WalkSpeedPm, 0, 1000) / 1000;
        return Math.Max(0, s);
    }

    private void MoveMember(CrawlMember m)
    {
        int dx = 0, dy = 0;
        if (m.Hero && m.MoveDir >= 0 && m.Path == null) { (dx, dy) = Dirs[m.MoveDir & 7]; if (m.Lost) (dx, dy) = Dirs[(m.MoveDir + LostDrift(m)) & 7]; }
        else if (m.Path != null) { if (!FollowPath(m, out dx, out dy)) return; }
        else if (!m.Hero) { Follow(m); return; }
        if (dx == 0 && dy == 0) return;
        int speed = SpeedMmps(m);
        if (speed <= 0) return;
        int step = speed / 20;
        if (dx != 0 && dy != 0) step = step * 724 / 1024;
        m.FX = dx * 1024; m.FY = dy * 1024;
        Slide(m, dx * step, dy * step);
    }

    /// <summary>Moves by (ddx, ddy) mm, one axis at a time; walking into a closed door opens it, a detected trap or rubble stops the move.</summary>
    internal bool Slide(CrawlMember m, int ddx, int ddy)
    {
        bool moved = false;
        if (ddx != 0 && Enter(m, m.X + ddx, m.Y)) { m.X += ddx; moved = true; }
        if (ddy != 0 && Enter(m, m.X, m.Y + ddy)) { m.Y += ddy; moved = true; }
        int cell = CellOf(m);
        if (cell != m.Cell) OnEnterCell(m, cell);
        return moved;
    }

    private bool Enter(CrawlMember m, int nx, int ny)
    {
        int x = nx / 1000, y = ny / 1000;
        if (!Map.In(m.Level, x, y) || nx < 0 || ny < 0) return false;
        int c = Map.Index(m.Level, x, y);
        if (c == m.Cell) return true;
        var t = Map.Tiles[c];
        if (t == SiteTile.Door) { OpenDoor(c); return false; }
        if (!SiteTiles.Walkable(t)) return false;
        foreach (var n in Map.Inhabitants) if (n.State != InhabitantState.Dead && n.Level == m.Level && CellOf(n) == c && n.State is InhabitantState.Dormant or InhabitantState.Asleep or InhabitantState.Home) return false;
        return true;
    }

    public void OpenDoor(int cell)
    {
        if (Map.Tiles[cell] != SiteTile.Door) return;
        Map.Tiles[cell] = SiteTile.DoorOpen;
        var (l, x, y) = Map.At(cell);
        Lights.SetOpaque(x, y, SiteMap.Z(l), false);
        Note(CrawlEventKind.DoorOpened, cell);
        _lightDirty = _mapDirty = true;
    }

    private void OnEnterCell(CrawlMember m, int cell)
    {
        m.Cell = cell;
        m.Crumbs.Add(cell);
        if (m.Hero || m.HasMap) _mapDirty = true;
        _lightDirty |= m.Light.Lit;
        if (Map.Tiles[cell] == SiteTile.Hole) { FallThrough(m, cell); return; }
        StepOnTrap(m, cell);
    }

    private int LostDrift(CrawlMember m)
    {
        long w = GameSec / 100;                                                   // a new drift every 5 sim-seconds
        int r = DetRng.Range(Seed, w, m.Person, RngStreams.HazardEvent, 77, 0, 3);
        return r - 1;
    }

    /// <summary>A party member follows the hero (4.10): it walks the hero's way, staying a couple of metres behind, through the same portals.</summary>
    private void Follow(CrawlMember m)
    {
        if (!m.CanAct) { if (m.DragBy >= 0 && m.DragBy < Members.Count) DragAlong(m, Members[m.DragBy]); return; }
        var h = Hero;
        long dx = h.X - m.X, dy = h.Y - m.Y;
        if (h.Level == m.Level && dx * dx + dy * dy <= 2500L * 2500L) return;
        if (m.Path == null || m.PathIdx >= m.Path.Count || GameSec % 40 == 0) GoTo(m, h.Cell, quiet: true);
        if (m.Path == null) return;
        m.Mode = h.Mode; m.Sneak = h.Sneak;
        if (FollowPath(m, out int fx, out int fy) && (fx != 0 || fy != 0))
        {
            int step = Math.Max(1, SpeedMmps(m) / 20);
            if (fx != 0 && fy != 0) step = step * 724 / 1024;
            Slide(m, fx * step, fy * step);
        }
    }

    private void DragAlong(CrawlMember m, CrawlMember by)
    {
        if (by.Dead || by.Level != m.Level) return;
        long dx = by.X - m.X, dy = by.Y - m.Y;
        if (dx * dx + dy * dy <= 1200L * 1200L) return;
        m.X = by.X - (int)Math.Clamp(dx, -900, 900); m.Y = by.Y - (int)Math.Clamp(dy, -900, 900);
        if (!SiteTiles.Walkable(Map.Tiles[CellOf(m)])) { m.X = by.X; m.Y = by.Y; }
        int c = CellOf(m); if (c != m.Cell) { m.Cell = c; m.Crumbs.Add(c); }
    }

    /// <summary>Plans a walk to a cell (levels and portals included): the click-move, a follower's way to the hero.</summary>
    public bool GoTo(CrawlMember m, int cell, bool quiet = false)
    {
        var (tl, tx, ty) = Map.At(cell);
        var r = Pathfinder.FindPath(new Cell3(m.X / 1000, m.Y / 1000, SiteMap.Z(m.Level)), new Cell3(tx, ty, SiteMap.Z(tl)));
        if (r == null || r.Cells.Count < 1) { m.Path = null; return false; }
        m.Path = [.. r.Cells.Select(c => Map.Index(SiteMap.LevelOf(c.Z), c.X, c.Y))];
        m.PathIdx = 1; m.Retracing = false;
        _ = quiet;
        return true;
    }

    private void StopPath(CrawlMember m, bool interrupted)
    {
        if (m.Path == null) return;
        bool retr = m.Retracing;
        m.Path = null; m.Retracing = false;
        if (retr && interrupted) Note(CrawlEventKind.RetraceStopped);
    }

    /// <summary>Walks the planned cells; a step onto another level takes the portal. False when there is nothing left.</summary>
    private bool FollowPath(CrawlMember m, out int dx, out int dy)
    {
        dx = dy = 0;
        var p = m.Path!;
        while (m.PathIdx < p.Count && p[m.PathIdx] == m.Cell) m.PathIdx++;
        if (m.PathIdx >= p.Count)
        {
            bool retr = m.Retracing;
            m.Path = null; m.Retracing = false;
            if (retr) Note(CrawlEventKind.RetraceDone);
            return false;
        }
        int next = p[m.PathIdx];
        var (nl, nx, ny) = Map.At(next);
        if (nl != m.Level) { UsePortalAt(m, m.Cell, next); return false; }
        int tx = nx * 1000 + 500, ty = ny * 1000 + 500;
        int ex = tx - m.X, ey = ty - m.Y;
        int speed = SpeedMmps(m) / 20;
        if (speed <= 0) return false;
        if (Math.Abs(ex) <= speed && Math.Abs(ey) <= speed)
        {
            // close enough to place on the cell centre this step
            if (Enter(m, tx, m.Y) || CellOf(m) == next) m.X = tx;
            if (Enter(m, m.X, ty) || CellOf(m) == next) m.Y = ty;
            int c = CellOf(m); if (c != m.Cell) OnEnterCell(m, c);
            return false;
        }
        dx = Math.Sign(ex) * (Math.Abs(ex) > speed / 3 ? 1 : 0); dy = Math.Sign(ey) * (Math.Abs(ey) > speed / 3 ? 1 : 0);
        return true;
    }

    // ------------------------------------------------------------------ acting (E)

    /// <summary>
    /// The default action (E): take the portal underfoot, open the container beside you, disarm a trap you found, dig rubble with a pick, climb down a rope; the mouth's stairs
    /// lead out.
    /// </summary>
    public void Act(CrawlMember m)
    {
        if (!m.CanAct || Fight != null) return;
        var t = Map.Tiles[m.Cell];
        if (t is SiteTile.StairsUp or SiteTile.ShaftUp or SiteTile.StairsDown or SiteTile.ShaftDown)
        {
            if (m.Cell == Map.Mouth && m.Hero) { Leave(); return; }
            var p = Map.PortalAt(m.Cell);
            if (p != null) { UsePortalAt(m, m.Cell, p.UpperCell == m.Cell ? p.LowerCell : p.UpperCell); return; }
        }
        foreach (int c in Around(m.Cell))
        {
            if (Map.ContainerAt(c) is { } box && (box.Items.Count > 0 || !box.Opened)) { Loot(m, box); return; }
        }
        foreach (int c in Around(m.Cell, withSelf: true))
            if (Map.TrapAt(c) is { Armed: true, Detected: true } tr) { StartBusy(m, CrawlInputKind.Act, C.K.DisarmS, tr.Id, 1); return; }
        foreach (int c in Around(m.Cell))
            if (Map.Tiles[c] == SiteTile.Rubble)
            {
                if (m.DigQuality <= 0) { Note(CrawlEventKind.Blocked, c); return; }
                StartBusy(m, CrawlInputKind.Act, Math.Max(1, C.K.DigS / m.DigQuality), c, 2); return;
            }
        foreach (int c in Around(m.Cell)) if (Map.Tiles[c] == SiteTile.Door) { OpenDoor(c); return; }
        Note(CrawlEventKind.NothingHere);
    }

    public IEnumerable<int> Around(int cell, bool withSelf = false)
    {
        var (l, x, y) = Map.At(cell);
        if (withSelf) yield return cell;
        foreach (var (dx, dy) in Dirs) if (Map.In(l, x + dx, y + dy)) yield return Map.Index(l, x + dx, y + dy);
    }

    private void StartBusy(CrawlMember m, CrawlInputKind kind, int seconds, int target, int what)
    {
        m.BusyKind = kind; m.BusyS = seconds; m.BusyTarget = target | what << 24;
        Note(CrawlEventKind.Busy, what, seconds);
    }

    private void FinishBusy(CrawlMember m)
    {
        int what = m.BusyTarget >> 24, target = m.BusyTarget & 0xFFFFFF;
        m.BusyTarget = -1;
        if (what == 1 && target < Map.Traps.Count) Disarm(m, Map.Traps[target]);
        else if (what == 2 && target < Map.Tiles.Length && Map.Tiles[target] == SiteTile.Rubble)
        {
            Map.Tiles[target] = SiteTile.Floor;
            var (l, x, y) = Map.At(target);
            Grid.SetOpen(x, y, SiteMap.Z(l), true); Lights.SetOpaque(x, y, SiteMap.Z(l), false);
            Note(CrawlEventKind.Dug, target);
            _lightDirty = _mapDirty = true;
        }
    }

    /// <summary>Takes the portal from <paramref name="from"/> to <paramref name="to"/>: stairs always, a ladder while it is down; a pulled-up ladder can be climbed down on a rope.</summary>
    private void UsePortalAt(CrawlMember m, int from, int to)
    {
        var p = Map.PortalAt(from);
        if (p == null) return;
        if (!p.Enabled)
        {
            bool down = p.UpperCell == from;
            if (down && m.Rope)
            {
                // a 10 m rope down a 3 m shaft: an Athletics check, a fall on failure (4.10)
                int roll = DetRng.Range(Seed, SimSecond, m.Person, RngStreams.HazardEvent, 900u + (uint)p.Id, 0, 100);
                MoveTo(m, to);
                if (roll >= 50 + m.Athletics / 2) { Note(CrawlEventKind.RopeFell, Members.IndexOf(m)); FallBlow(m, 3000, 0x52_0000u + (uint)p.Id); }
                else Note(CrawlEventKind.ClimbedRope, Members.IndexOf(m));
                return;
            }
            Note(CrawlEventKind.LadderUp, p.Id);
            return;
        }
        MoveTo(m, to);
    }

    private void MoveTo(CrawlMember m, int cell)
    {
        var (l, x, y) = Map.At(cell);
        bool levelChange = l != m.Level;
        m.Level = l; m.X = x * 1000 + 500; m.Y = y * 1000 + 500;
        OnEnterCell(m, cell);
        if (levelChange && m.Hero) Note(CrawlEventKind.LevelChanged, l);
        _lightDirty = _mapDirty = true;
    }

    /// <summary>The last one up pulls the ladder after him (4.10): the portal stays up (a delta) until someone lets it down again.</summary>
    public void PullLadder(CrawlMember m)
    {
        var p = Map.PortalAt(m.Cell);
        if (p == null || p.Kind != PortalKindSite.Ladder || p.UpperCell != m.Cell) { Note(CrawlEventKind.NothingHere); return; }
        p.Enabled = !p.Enabled;
        var gp = Grid.Portals.FirstOrDefault(g => Map.Index(SiteMap.LevelOf(g.A.Z), g.A.X, g.A.Y) == p.UpperCell);
        if (gp != null) Grid.SetPortalEnabled(gp.Id, p.Enabled);
        Note(CrawlEventKind.LadderPulled, p.Id, p.Enabled ? 0 : 1);
    }

    // ------------------------------------------------------------------ light

    /// <summary>V: lights or snuffs the carried light. Lighting a new torch needs tinder or another flame in the party; a snuffed torch loses nothing.</summary>
    public void ToggleLight(CrawlMember m)
    {
        if (m.Light.Lit) { m.Light.Lit = false; _lightDirty = true; return; }
        var air = SiteLight.Air(Map.Air[m.Cell], C.K);
        bool flame = m.Tinder || Members.Any(o => o != m && o.Light.Lit && o.Level == m.Level);
        if (!flame || air.FlameOut || m.Swimming) { Note(CrawlEventKind.NoLight); return; }
        if (m.Light.Out)
        {
            if (m.SpareTorches > 0) { m.SpareTorches--; m.TorchesUsed++; m.Light = CarriedLight.Torch(C.K); }
            else if (m.SpareCandles > 0) { m.SpareCandles--; m.CandlesUsed++; m.Light = CarriedLight.Candle(C.K); }
            else { Note(CrawlEventKind.NoLight); return; }
        }
        m.Light.Lit = true;
        Note(CrawlEventKind.TorchLit, Members.IndexOf(m));
        _lightDirty = true;
    }

    private void RefreshLight()
    {
        if (_lightDirty)
        {
            _dyn.Clear();
            foreach (var m in Members)
            {
                if (!m.Active) continue;
                var air = SiteLight.Air(Map.Air[m.Cell], C.K);
                var (r, i) = SiteLight.Cast(m.Light, C.K, air);
                if (r > 0) _dyn.Add(new LightSource(m.X / 1000, m.Y / 1000, SiteMap.Z(m.Level), r, i));
            }
            _frame = _pair.Next(new Ambient(0, 0, DayPhase.Night), _dyn);
            _lightDirty = false; _mapDirty = true;
        }
        if (_mapDirty)
        {
            foreach (var m in Members)
            {
                if (!m.Active || m.Unconscious || !m.HasMap) continue;
                Known.Observe(Map, _frame, m.Level, m.X / 1000, m.Y / 1000, C.K.SeenLight, 37);
            }
            _mapDirty = false;
        }
    }

    /// <summary>The light (0..255) at a cell now.</summary>
    public int LightAt(int cell) { var (l, x, y) = Map.At(cell); return _frame.Total(x, y, SiteMap.Z(l)); }

    // ------------------------------------------------------------------ retrace, rest, drag

    /// <summary>Q, Retrace steps (4.7): walks the hero back along the breadcrumbs to the mouth (loops cut out), through the portals, at the slowest member's speed; any interrupt stops it.</summary>
    public void Retrace(CrawlMember m)
    {
        var way = m.Crumbs.WayBack(c => c == Map.Mouth);
        if (way.Count < 2) { Note(CrawlEventKind.NothingHere); return; }
        m.Path = way; m.PathIdx = 1; m.Retracing = true; m.MoveDir = -1;
        Note(CrawlEventKind.RetraceStarted, way.Count);
    }

    /// <summary>Camping below (4.10): needs air of 600 or better and no water over 30 cm; sentries are still needed (the site's people wander).</summary>
    public bool CanRest(CrawlMember m) => Map.Air[m.Cell] >= C.K.RestAir && Map.Water(m.Cell) <= C.K.WadeSlowCm && Fight == null;

    private void StartRest(CrawlMember m)
    {
        if (!CanRest(m)) { Note(CrawlEventKind.CannotRest); return; }
        Resting = true; Note(CrawlEventKind.RestStarted);
    }

    private void StopRest() { if (Resting) { Resting = false; Note(CrawlEventKind.RestStopped); } }

    /// <summary>Drag out (Q): the hero takes an unconscious companion beside him along (bad air, a collapse).</summary>
    private void DragOut(CrawlMember h)
    {
        foreach (var m in Members)
            if (m != h && m.Unconscious && m.Level == h.Level && Math.Abs(m.X - h.X) <= 1500 && Math.Abs(m.Y - h.Y) <= 1500) { m.DragBy = Members.IndexOf(h); return; }
        Note(CrawlEventKind.NothingHere);
    }

    // ------------------------------------------------------------------ the snapshot and the hash

    private CrawlSnapshot Publish()
    {
        if (Members.Count == 0) return new CrawlSnapshot(GameSec, 0, 0, 0, 0, false, LightKind.None, 0, 1000, false, 0, false, false, 0, 0, 0, 0, false, false, 0, "", 0);
        var h = Hero;
        int air = Map.Air[h.Cell];
        var (em, es) = ExitTime(h);
        string busy = h.BusyS > 0 ? (h.BusyTarget < 0 ? "recover" : (h.BusyTarget >> 24) == 1 ? "disarm" : "dig") : h.HeldS > 0 ? "held" : "";
        return new CrawlSnapshot(GameSec, h.Level, h.X, h.Y, h.Light.LeftS, h.Light.Lit, h.Light.Kind, h.SpareTorches, air, air < C.K.AirGutter, Map.Water(h.Cell), h.Lost, Fight != null,
            em, es, h.CarriedKg, h.CapacityG / 1000, Resting, h.Retracing, Known.Count, busy, Known.Revision);
    }

    private int _exitCacheCell = -1, _exitM, _exitS;
    /// <summary>The way out (4.10: "42 m to the stairs, 9 minutes at your load"): the walked length of the path to the mouth, and the sim-seconds it takes at the member's speed and load.</summary>
    public (int Metres, int Seconds) ExitTime(CrawlMember m)
    {
        if (m.Cell != _exitCacheCell || GameSec % 20 == 0)
        {
            _exitCacheCell = m.Cell;
            var (ml, mx, my) = Map.At(Map.Mouth);
            var r = Pathfinder.FindPath(new Cell3(m.X / 1000, m.Y / 1000, SiteMap.Z(m.Level)), new Cell3(mx, my, SiteMap.Z(ml)));
            _exitM = r == null ? -1 : (int)(r.Cost / LevelGrid.Straight);
            int speed = Math.Max(300, Math.Min(SpeedMmps(m) <= 0 ? 3000 : SpeedMmps(m), m.Mode == CrawlMode.Sprint ? 5000 : 3000));
            _exitS = _exitM < 0 ? -1 : _exitM * 1000 / speed;
        }
        return (_exitM, _exitS);
    }

    /// <summary>The crawl's part of the <c>SiteHash</c>: the map, the members, the auto-map and the clock.</summary>
    public ulong Hash()
    {
        ulong h = Map.Hash();
        static ulong M(ulong h, long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 29; return h; } }
        h = M(h, GameSec);
        foreach (var m in Members)
        {
            h = M(h, (long)m.Person); h = M(h, m.X); h = M(h, m.Y); h = M(h, m.Level); h = M(h, m.Light.LeftMilliS); h = M(h, m.Light.Lit ? 1 : 0);
            h = M(h, m.FoulS); h = M(h, m.BreathS); h = M(h, m.HeldS); h = M(h, m.BusyS); h = M(h, m.LootG); h = M(h, m.Dead ? 1 : 0); h = M(h, m.Unconscious ? 1 : 0);
            h = M(h, m.SpareTorches); h = m.Book.Hash(h);
        }
        h = Known.Hash(h);
        if (Fight != null) h = M(h, (long)Fight.Hash());
        return h;
    }
}
