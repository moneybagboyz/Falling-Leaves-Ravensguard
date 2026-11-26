using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Naval;

public enum CarrierEventKind : byte { Boarded, Climbed, KnockedOver, Overboard, ClimbedOut, Drowned }

/// <summary>Something that happened aboard (not hashed: the state it changed is).</summary>
public readonly record struct CarrierEvent(int Tick, CarrierEventKind Kind, int Fighter, ulong Person, ulong Frame);

/// <summary>
/// P7-SH-A, carriers in a fight (ships-and-naval-war 4.6, <c>FightWorld.Carriers.cs</c>; the pattern of the mounted layer): a fighter who rides a ship has his place in the ship's <see cref="ShipFrame"/> (local millimetres and a deck) and the fight sees him
/// through its transform. Each tick, before anyone moves, the ships move and every rider is put at the transform of his local place with his facing turned by the ship's turn (the carrier's motion is applied before his own step); the fight then moves
/// him as it moves anyone, against the ship's decks as walls (gunwale, mast, parapet and solids stop him; a body that is not aboard finds the hull solid); after the step his local place follows where he walked, and a man whose place is past the rail
/// is overboard: he leaves the fight and enters the water (<see cref="Swimmers"/>), swimming or sinking by his kit. A man who stood still keeps his local millimetres exactly through any number of turns, and his world point is always the transform of them
/// (SH-13). A heavy blow that would throw a man at the rail over it puts him overboard too (<see cref="TryKnockOver"/>). Integer only; every walk is in ascending fighter id order; no draw but the swimmers' (0x7D2).
/// </summary>
public sealed class CarrierLayer : ILevelWalls
{
    public FightWorld World { get; }
    public List<ShipFrame> Frames { get; } = [];
    public Swimmers Water { get; }
    /// <summary>The walls of the place around the ships (a quay), or null.</summary>
    public IFightWalls? Base { get; set; }
    public List<CarrierEvent> Events { get; } = [];
    /// <summary>The kit a fighter wears for the water (default: his shield and his worn pieces).</summary>
    public Func<Fighter, IEnumerable<GearPiece>> KitOf { get; set; } = DefaultKit;
    /// <summary>His Athletics for the water (default 30: the fight does not carry the skill).</summary>
    public Func<Fighter, int> AthleticsOf { get; set; } = _ => 30;
    /// <summary>A landed blow of at least this much momentum (milli) that is driving a man at the rail over it throws him overboard.</summary>
    public long KnockMilli { get; set; } = 60_000;
    /// <summary>How far (mm) a thrown man travels.</summary>
    public int KnockMm { get; set; } = 2500;

    /// <summary>P7-SH-D: the ship's own state that a fight touches (fire arrows landing, a prize); null in a fight that needs none.</summary>
    public ShipCombat? Combat { get; set; }
    /// <summary>P7-SH-D: the rules of the grapples' climb (a rope: 4 s up a side a metre higher or more, else 1 s over the rail).</summary>
    public ShipFightRules Rules { get; set; } = ShipFightRules.Default;
    /// <summary>The ammo id of an arrow with a burning head: where it lands on a ship it may light what it hits.</summary>
    public const string FireArrowAmmo = "fire_arrow";

    private sealed class Seat { public ShipFrame Frame = null!; public RiderKind Kind; public ulong Id; public int Wx, Wy; }
    private Seat?[] _seats = [];
    private Seat? _ctx;
    private readonly List<int> _riders = [];
    private readonly Action<Fighter, Fighter, BlowResult, StrikeV2>? _prior;

    public CarrierLayer(FightWorld world, Swimmers water)
    {
        World = world; Water = water;
        if (world.Walls != null && !ReferenceEquals(world.Walls, this)) Base = world.Walls;
        world.Walls = this; world.Carriers = this;
        _prior = world.OnBlow; world.OnBlow = OnBlow;
        water.OnDrowned += s => Drowned(s);
        water.OnClimbedOut += s => ClimbedOut(s);
    }

    public static IEnumerable<GearPiece> DefaultKit(Fighter f)
    {
        if (f.Shield != null) yield return GearPiece.OfShield(f.Shield.Def.Id, f.Shield.Def.MassG);
        foreach (var w in f.Worn) yield return GearPiece.OfArmour(w.Stats.Shape.Id, w.Stats.Shape.MassG);
    }

    private static ulong RiderId(Fighter f) => f.Person != 0 ? f.Person : (1UL << 62) | (uint)f.Id;
    public ShipFrame Add(ShipFrame f) { if (!Frames.Contains(f)) Frames.Add(f); return f; }

    // ------------------------------------------------------------------ boarding

    /// <summary>A fighter comes aboard <paramref name="frame"/> at local (lx, ly) on a deck: he stands there, in the world, through the transform.</summary>
    public void Board(Fighter f, ShipFrame frame, int lx, int ly, int deck)
    {
        Add(frame);
        Leave(f);
        frame.Board(RiderKind.Person, RiderId(f), lx, ly, deck);
        if (_seats.Length <= f.Id) Array.Resize(ref _seats, Math.Max(f.Id + 1, _seats.Length * 2));
        var (wx, wy) = frame.ToWorld(lx, ly);
        _seats[f.Id] = new Seat { Frame = frame, Kind = RiderKind.Person, Id = RiderId(f), Wx = wx, Wy = wy };
        f.X = wx; f.Y = wy;
        RebuildRiders();
        Events.Add(new CarrierEvent(World.Tick, CarrierEventKind.Boarded, f.Id, f.Person, frame.Id.Value));
    }

    /// <summary>A fighter comes aboard on a cell's centre.</summary>
    public void BoardAt(Fighter f, ShipFrame frame, int cx, int cy, int deck) => Board(f, frame, frame.Deck.CentreX(cx), frame.Deck.CentreY(cy), deck);

    /// <summary>He is no longer a rider (he left by a plank, or went over the side): his place in the frame is dropped.</summary>
    public void Leave(Fighter f)
    {
        if (f.Id >= _seats.Length || _seats[f.Id] is not { } s) return;
        s.Frame.Leave(s.Kind, s.Id); _seats[f.Id] = null; RebuildRiders();
    }

    private void RebuildRiders() { _riders.Clear(); for (int i = 0; i < _seats.Length; i++) if (_seats[i] != null) _riders.Add(i); }

    public bool Rides(int fighter) => fighter >= 0 && fighter < _seats.Length && _seats[fighter] != null;
    public ShipFrame? FrameOf(int fighter) => Rides(fighter) ? _seats[fighter]!.Frame : null;
    public Rider? RiderOf(int fighter) => Rides(fighter) ? _seats[fighter]!.Frame.RiderOf(_seats[fighter]!.Kind, _seats[fighter]!.Id) : null;

    /// <summary>He climbs a ladder up or down (the cell he stands on is a ladder and the deck above or below has a floor over it): his deck changes, his place does not.</summary>
    public bool Climb(int fighter, bool up)
    {
        var r = RiderOf(fighter); if (r == null) return false;
        var f = _seats[fighter]!.Frame; int cx = f.Deck.CellX(r.Lx), cy = f.Deck.CellY(r.Ly);
        if (f.Deck.Kind(cx, cy, r.Deck) != DeckKind.Ladder) return false;
        int nd = r.Deck + (up ? 1 : -1);
        if (!f.Deck.Walkable(cx, cy, nd)) return false;
        r.Deck = (byte)nd;
        Events.Add(new CarrierEvent(World.Tick, CarrierEventKind.Climbed, fighter, World.Fighters[fighter].Person, f.Id.Value));
        return true;
    }

    // ------------------------------------------------------------------ the step

    /// <summary>Before anyone moves: the ships move, and each rider is put where his local place is now, facing turned with the ship.</summary>
    public void Before()
    {
        foreach (var fr in Frames) if (!IsChild(fr)) fr.Step();
        foreach (var j in _joins) Follow(j);
        var turned = new Dictionary<ShipFrame, (long Cos, long Sin)>();
        foreach (int i in _riders)
        {
            var s = _seats[i]!; var f = World.Fighters[i]; var r = s.Frame.RiderOf(s.Kind, s.Id)!;
            var (wx, wy) = s.Frame.ToWorld(r.Lx, r.Ly);
            f.X = wx; f.Y = wy; s.Wx = wx; s.Wy = wy;
            int turn = s.Frame.TurnPerTick;
            if (turn != 0)
            {
                if (!turned.TryGetValue(s.Frame, out var cs)) turned[s.Frame] = cs = IntMath.CosSinQ30(turn & FrameMath.Mask, FrameMath.Turn);
                long fx = f.FX, fy = f.FY;
                f.FX = (int)((fx * cs.Cos - fy * cs.Sin + (1L << 29)) >> 30); f.FY = (int)((fx * cs.Sin + fy * cs.Cos + (1L << 29)) >> 30);
            }
        }
        foreach (var rf in Water.Refuges) if (rf.Frame != 0) { var fr = Frames.Find(x => x.Id.Value == rf.Frame); if (fr != null) (rf.XMm, rf.YMm) = fr.ToWorld(rf.AnchorLx, rf.AnchorLy); }
        Water.Step();
    }

    /// <summary>After the fight has moved and separated everyone: a rider's local place follows where he walked; one past the rail is overboard.</summary>
    public void After()
    {
        FinishClimbs();
        List<int>? over = null;
        foreach (int i in _riders)
        {
            var s = _seats[i]!; var f = World.Fighters[i]; var r = s.Frame.RiderOf(s.Kind, s.Id)!;
            if (f.X != s.Wx || f.Y != s.Wy)
            {
                (r.Lx, r.Ly) = s.Frame.ToLocal(f.X, f.Y);
                (f.X, f.Y) = s.Frame.ToWorld(r.Lx, r.Ly);                // the world point is always the transform of the local one
                s.Wx = f.X; s.Wy = f.Y;
            }
            if (f.Act is FAct.Dead) continue;
            if (s.Frame.Deck.KindAtMm(r.Lx, r.Ly, r.Deck) == DeckKind.None) (over ??= []).Add(i);
        }
        if (over != null) foreach (int i in over) GoOverboard(i, "fall");
    }

    // ------------------------------------------------------------------ the rail and the water

    /// <summary>A fighter goes into the water where he stands (he stepped off, jumped, or was thrown): he leaves the fight and the ship, and becomes a swimmer who swims or sinks by what he wears.</summary>
    public Swimmer? GoOverboard(int fighter, string cause)
    {
        if (!Rides(fighter)) return null;
        var f = World.Fighters[fighter]; var s = _seats[fighter]!; ulong frame = s.Frame.Id.Value;
        Leave(f);
        f.Act = FAct.Fled; f.ActTicks = 0; f.EndedAt = World.Tick;
        var sw = Water.Enter(f.Person != 0 ? f.Person : (1UL << 62) | (uint)f.Id, f.X, f.Y, AthleticsOf(f), KitOf(f), cause, 0, fighter);
        Events.Add(new CarrierEvent(World.Tick, cause == "blow" ? CarrierEventKind.KnockedOver : CarrierEventKind.Overboard, fighter, f.Person, frame));
        return sw;
    }

    /// <summary>
    /// A man is thrown <see cref="KnockMm"/> along (dx, dy) (world): over the rail (a gunwale is only a rail) and out past the ship's edge he is overboard (true); a mast, a parapet or a solid on the way stops him and he is not thrown (false).
    /// </summary>
    public bool TryKnockOver(int fighter, int dxMm, int dyMm, string cause = "blow")
    {
        if (!Rides(fighter) || (dxMm == 0 && dyMm == 0)) return false;
        var f = World.Fighters[fighter]; var s = _seats[fighter]!; var r = RiderOf(fighter)!;
        long len = FightMath.Len(dxMm, dyMm);
        int steps = Math.Max(1, KnockMm / 250);
        for (int k = 1; k <= steps; k++)
        {
            int wx = f.X + (int)((long)dxMm * KnockMm * k / (len * steps)), wy = f.Y + (int)((long)dyMm * KnockMm * k / (len * steps));
            var (lx, ly) = s.Frame.ToLocal(wx, wy);
            if (!s.Frame.Deck.Inside(lx, ly)) { f.X = wx; f.Y = wy; return GoOverboard(fighter, cause) != null; }
            if (DeckMap.Blocks(s.Frame.Deck.KindAtMm(lx, ly, r.Deck)) && s.Frame.Deck.KindAtMm(lx, ly, r.Deck) != DeckKind.Gunwale) return false;
        }
        return false;
    }

    private void OnBlow(Fighter a, Fighter t, BlowResult r, StrikeV2 s)
    {
        _prior?.Invoke(a, t, r, s);
        if (!r.Hit || r.InitialMilli < KnockMilli || !Rides(t.Id)) return;
        TryKnockOver(t.Id, t.X - a.X, t.Y - a.Y);
    }

    private void Drowned(Swimmer s)
    {
        if (s.Fighter >= 0 && s.Fighter < World.Fighters.Count) { var f = World.Fighters[s.Fighter]; f.Act = FAct.Dead; f.EndedAt = World.Tick; }
        Events.Add(new CarrierEvent(World.Tick, CarrierEventKind.Drowned, s.Fighter, s.Person, 0));
    }

    private void ClimbedOut(Swimmer s)
    {
        var fr = Frames.Find(x => x.Id.Value == (Water.Refuges.Find(r => r.Id == s.Refuge)?.Frame ?? 0));
        Events.Add(new CarrierEvent(World.Tick, CarrierEventKind.ClimbedOut, s.Fighter, s.Person, fr?.Id.Value ?? 0));
        if (fr == null || s.Fighter < 0) return;
        // back aboard at the nearest walkable cell of deck 0 to where he came up
        var (lx, ly) = fr.ToLocal(s.XMm, s.YMm);
        int bx = -1, by = -1; long bd = long.MaxValue;
        for (int y = 0; y < fr.Deck.Length; y++)
            for (int x = 0; x < fr.Deck.Width; x++)
            {
                if (fr.Deck.Kind(x, y, 0) is not (DeckKind.Floor or DeckKind.Plank)) continue;
                long dx = fr.Deck.CentreX(x) - lx, dy = fr.Deck.CentreY(y) - ly, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; bx = x; by = y; }
            }
        if (bx < 0) return;
        var f = World.Fighters[s.Fighter];
        if (f.Act == FAct.Dead) return;
        f.Act = FAct.Idle; f.ActTicks = 0; f.EndedAt = -1;
        BoardAt(f, fr, bx, by, 0);
    }

    // ------------------------------------------------------------------ bound ships and boarding (4.6 "Grapples and boarding")

    private sealed class JoinRow { public ShipFrame Parent = null!, Child = null!; public int Lx, Ly, Dh; }
    private readonly List<JoinRow> _joins = [];
    private sealed class ClimbRow { public int Fighter; public ShipFrame From = null!, To = null!; public int Cx, Cy; public int ReadyTick; public int Rope; }
    private readonly List<ClimbRow> _climbs = [];
    private readonly HashSet<(ulong Pair, int Rope)> _ropesBusy = [];

    private bool IsChild(ShipFrame f) { foreach (var j in _joins) if (ReferenceEquals(j.Child, f)) return true; return false; }

    /// <summary>
    /// The two ships' frames are joined (the lines are hauled taut): they move as one carrier. The second keeps the place and heading it has now against the first, and takes the first's speed and turn; riders of both go on being carried through
    /// their own frame's transform. Returns false if they are joined already or one is a child of another join.
    /// </summary>
    public bool Join(ShipFrame parent, ShipFrame child)
    {
        if (ReferenceEquals(parent, child) || IsChild(child) || IsChild(parent)) return false;
        Add(parent); Add(child);
        var (lx, ly) = parent.ToLocal(child.XMm, child.YMm);
        _joins.Add(new JoinRow { Parent = parent, Child = child, Lx = lx, Ly = ly, Dh = (child.Heading - parent.Heading) & FrameMath.Mask });
        child.SpeedMmps = parent.SpeedMmps; child.TurnPerTick = parent.TurnPerTick;
        return true;
    }

    public bool Joined(ShipFrame a, ShipFrame b) { foreach (var j in _joins) if ((ReferenceEquals(j.Parent, a) && ReferenceEquals(j.Child, b)) || (ReferenceEquals(j.Parent, b) && ReferenceEquals(j.Child, a))) return true; return false; }

    private static void Follow(JoinRow j)
    {
        j.Child.SpeedMmps = j.Parent.SpeedMmps; j.Child.TurnPerTick = j.Parent.TurnPerTick;
        (j.Child.XMm, j.Child.YMm) = j.Parent.ToWorld(j.Lx, j.Ly);
        j.Child.SetHeading(j.Parent.Heading + j.Dh);
    }

    /// <summary>The ticks a man's crossing takes: 4 s (80 ticks) up a side a metre higher or more than where he stands, else 1 s over the rail.</summary>
    public int ClimbTicks(ShipFrame from, ShipFrame to) => Math.Abs(to.SideMm - from.SideMm) >= Rules.ClimbOverMm ? Rules.ClimbTicks : Rules.StepOverTicks;

    private static ulong PairKey(ShipFrame a, ShipFrame b) => Math.Min(a.Id.Value, b.Id.Value) ^ (Math.Max(a.Id.Value, b.Id.Value) << 20);

    /// <summary>
    /// A man of a ship bound to <paramref name="to"/> starts across by one of its ropes (rope 0 or 1, one man a rope at a time): he is held in the climb for its ticks (staggered, open to blows) and then stands on the cell (cx, cy) of the other
    /// ship's deck 0. Returns false if the ships are not bound, the rope is busy, or the cell is not a floor.
    /// </summary>
    public bool BeginBoard(Fighter f, ShipFrame to, int cx, int cy, int rope)
    {
        var from = FrameOf(f.Id); if (from == null || !Joined(from, to) || !to.Deck.Walkable(cx, cy, 0)) return false;
        if (_climbs.Exists(c => c.Fighter == f.Id) || !_ropesBusy.Add((PairKey(from, to), rope))) return false;
        int ticks = ClimbTicks(from, to);
        f.Act = FAct.Stagger; f.ActTicks = ticks + 1;
        _climbs.Add(new ClimbRow { Fighter = f.Id, From = from, To = to, Cx = cx, Cy = cy, ReadyTick = World.Tick + ticks, Rope = rope });
        _climbs.Sort((a, b) => a.Fighter.CompareTo(b.Fighter));
        return true;
    }

    private void FinishClimbs()
    {
        if (_climbs.Count == 0) return;
        foreach (var c in _climbs.ToList())
        {
            var f = World.Fighters[c.Fighter];
            if (f.Act is FAct.Dead or FAct.Down or FAct.Fled) { Drop(c); continue; }       // knocked off the rope
            if (World.Tick < c.ReadyTick) { f.ActTicks = Math.Max(f.ActTicks, 1); continue; }
            Drop(c);
            f.Act = FAct.Idle; f.ActTicks = 0;
            BoardAt(f, c.To, c.Cx, c.Cy, 0);
        }
    }

    private void Drop(ClimbRow c) { _climbs.Remove(c); _ropesBusy.Remove((PairKey(c.From, c.To), c.Rope)); }

    private static bool Fit(Fighter f) => f.Act is not (FAct.Dead or FAct.Down or FAct.Fled or FAct.Surrendered);

    /// <summary>How many of <paramref name="team"/>'s fighters aboard the frame can still fight (a man who is down, overboard, yielded or dead is not counted).</summary>
    public int CrewFit(ShipFrame frame, int team)
    {
        int n = 0;
        foreach (int i in _riders) if (ReferenceEquals(_seats[i]!.Frame, frame) && World.Fighters[i].Team == team && Fit(World.Fighters[i])) n++;
        return n;
    }

    /// <summary>
    /// The ending of a ship (4.6): her crew of <paramref name="crewTeam"/> are all down, overboard, yielded or dead, and an enemy still fit stands aboard: she is taken (true), and the enemy's team is returned in <paramref name="captorTeam"/>.
    /// </summary>
    public bool IsTaken(ShipFrame frame, int crewTeam, out int captorTeam)
    {
        captorTeam = -1;
        if (CrewFit(frame, crewTeam) > 0) return false;
        foreach (int i in _riders)
            if (ReferenceEquals(_seats[i]!.Frame, frame) && World.Fighters[i].Team != crewTeam && Fit(World.Fighters[i])) { captorTeam = World.Fighters[i].Team; return true; }
        return false;
    }

    /// <summary>A missile's landing: a fire arrow that falls on a ship may light what it fell on (the ship's <see cref="Combat"/> draws).</summary>
    public void Landed(string ammo, int xMm, int yMm)
    {
        if (Combat == null || ammo != FireArrowAmmo) return;
        foreach (var fr in Frames)
        {
            var (lx, ly) = fr.ToLocal(xMm, yMm);
            if (!fr.Deck.Inside(lx, ly)) continue;
            Combat.Ignite(fr.Id.Value, fr.Deck.CellX(lx), fr.Deck.CellY(ly), 0, IgniteKind.Arrow);
            return;
        }
    }

    // ------------------------------------------------------------------ the walls

    public void Using(int fighterId) => _ctx = fighterId >= 0 && fighterId < _seats.Length ? _seats[fighterId] : null;

    public bool BlocksMove(int xMm, int yMm)
    {
        foreach (var fr in Frames)
        {
            var (lx, ly) = fr.ToLocal(xMm, yMm);
            if (!fr.Deck.Inside(lx, ly)) continue;
            if (_ctx != null && ReferenceEquals(_ctx.Frame, fr))
            {
                var r = fr.RiderOf(_ctx.Kind, _ctx.Id);
                return fr.Deck.BlocksMoveMm(lx, ly, r?.Deck ?? 0, rider: true);
            }
            return true;                                    // a hull is solid from outside
        }
        return Base?.BlocksMove(xMm, yMm) ?? false;
    }

    public bool BlocksMissile(int xMm, int yMm)
    {
        foreach (var fr in Frames)
        {
            var (lx, ly) = fr.ToLocal(xMm, yMm);
            if (fr.Deck.Inside(lx, ly)) return fr.Deck.Kind(fr.Deck.CellX(lx), fr.Deck.CellY(ly), 0) == DeckKind.Obstacle;
        }
        return Base?.BlocksMissile(xMm, yMm) ?? false;
    }

    // ---- ILevelWalls: the decks are levels (4.6 "Missiles": a castle shoots down, +15% range and a steadier aim a level up; a parapet covers by the crenel rule)

    private int LevelMm => World.AssaultRules.LevelMm;

    /// <summary>The height of the floor he stands on: his deck times the level (a man on the quay or in the water stands at 0).</summary>
    public int BaseMm(int fighter) => (RiderOf(fighter)?.Deck ?? 0) * LevelMm;

    /// <summary>A blow reaches only the same deck (a man on a castle and a man under it are on different floors; his missiles are the way down).</summary>
    public bool CanStrike(int attacker, int target) => Math.Abs(BaseMm(attacker) - BaseMm(target)) < LevelMm / 2;

    public int FootingPm(int fighter) => 1000;

    /// <summary>
    /// A missile at (x, y) and height z is stopped by a solid (a cabin) of the deck it flies at, or by a castle's parapet unless the shooter stands on that deck or above (then it is no hindrance); from below the parapet stops it with the assault
    /// rules' share (0x7C2, keyed on the shot and the cell; the crenel rule of sieges-and-engines 4.4). Off the ships the place's own walls decide.
    /// </summary>
    public bool MissileStopped(int xMm, int yMm, int zMm, int shooter, uint serial)
    {
        int level = zMm < 0 ? 0 : zMm / LevelMm;
        foreach (var fr in Frames)
        {
            var (lx, ly) = fr.ToLocal(xMm, yMm);
            if (!fr.Deck.Inside(lx, ly)) continue;
            if (level >= fr.Deck.Decks) return false;
            int cx = fr.Deck.CellX(lx), cy = fr.Deck.CellY(ly);
            var k = fr.Deck.Kind(cx, cy, level);
            if (k == DeckKind.Obstacle) return true;
            if (k != DeckKind.Parapet || BaseMm(shooter) >= level * LevelMm) return false;
            return DetRng.Range(World.Seed, serial, fr.Id.Value ^ ((ulong)(cy * fr.Deck.Width + cx) << 40), RngStreams.MissileMeetsMerlon, 0, 0, 1000) < World.AssaultRules.MerlonPm;
        }
        return Base is ILevelWalls bl ? bl.MissileStopped(xMm, yMm, zMm, shooter, serial) : Base?.BlocksMissile(xMm, yMm) ?? false;
    }

    // ------------------------------------------------------------------ the hash

    /// <summary>The <c>NavalHash</c> of the fight's ships: every frame (placement, motion, riders), who rides what, and the water.</summary>
    public ulong Hash(ulong h)
    {
        foreach (var fr in Frames) h = fr.Hash(h);
        foreach (var j in _joins) { h = ShipFrame.Mix(h, (long)j.Parent.Id.Value); h = ShipFrame.Mix(h, (long)j.Child.Id.Value); h = ShipFrame.Mix(h, j.Lx); h = ShipFrame.Mix(h, j.Ly); h = ShipFrame.Mix(h, j.Dh); }
        foreach (var c in _climbs) { h = ShipFrame.Mix(h, c.Fighter); h = ShipFrame.Mix(h, c.ReadyTick); h = ShipFrame.Mix(h, c.Rope); }
        foreach (int i in _riders) { h = ShipFrame.Mix(h, i); h = ShipFrame.Mix(h, (long)_seats[i]!.Frame.Id.Value); }
        return Water.Hash(h);
    }
}
