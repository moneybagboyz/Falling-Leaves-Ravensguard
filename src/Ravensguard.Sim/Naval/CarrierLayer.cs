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
public sealed class CarrierLayer : IFighterWalls
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
        foreach (var fr in Frames) fr.Step();
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

    // ------------------------------------------------------------------ the hash

    /// <summary>The <c>NavalHash</c> of the fight's ships: every frame (placement, motion, riders), who rides what, and the water.</summary>
    public ulong Hash(ulong h)
    {
        foreach (var fr in Frames) h = fr.Hash(h);
        foreach (int i in _riders) { h = ShipFrame.Mix(h, i); h = ShipFrame.Mix(h, (long)_seats[i]!.Frame.Id.Value); }
        return Water.Hash(h);
    }
}
