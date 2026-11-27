using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>
/// P8-LW-2: the captains of a live fight (live-war 3.2). Each captain is a ship of a team with a target ship; every <see cref="CaptainBrain.EveryTicks"/> ticks (2 s) the drive asks <see cref="CaptainBrain.Decide"/> and carries the answer out
/// through the same rules a player's ship uses: <see cref="ShipCombat.Ram"/> for a ram, <see cref="ShipCombat.Throw"/> and <see cref="ShipCombat.Haul"/> for the grapples (the ships then <see cref="CarrierLayer.Join"/>), <see cref="CarrierLayer.BeginBoard"/>
/// for the men, and the ship's own speed and turn for closing and fleeing. Ships and captains are walked in stable id order; nothing here draws except the grapple and the approach side (0x7D5, 0x858).
/// </summary>
public sealed class CaptainDrive(CarrierLayer layer, ShipCombat combat, ulong seed)
{
    public sealed class Captain { public ShipFrame Ship = null!; public ShipFrame Target = null!; public int Team; public int Start; public bool HasStem = true, Rammed, Fleeing; public int Side; public CaptainAct Last; }

    public const int CruiseMmps = 3000, FleeMmps = 3500, TurnDegPerS = 3, ThrowSkill = 40, HaulMen = 4, BoardersPerThink = 2;
    public SortedDictionary<ulong, Captain> Captains { get; } = [];

    public Captain Add(ShipFrame ship, int team, ShipFrame target, bool hasStem = true)
    {
        var c = new Captain { Ship = ship, Target = target, Team = team, HasStem = hasStem, Start = layer.CrewFit(ship, team) };
        Captains[ship.Id.Value] = c; return c;
    }

    /// <summary>Every captain thinks when the world's tick falls on the 2 s beat; the call is made once a tick by whoever steps the fight (after the step).</summary>
    public void Step()
    {
        if (layer.World.Tick % CaptainBrain.EveryTicks != 0) return;
        long minute = layer.World.Tick / (CaptainBrain.EveryTicks * 30);
        foreach (var c in Captains.Values)
        {
            var bind = combat.BindOf(c.Ship.Id.Value, c.Target.Id.Value);
            bool grappled = bind is { Joined: true } && layer.Joined(c.Ship, c.Target);
            var v = CaptainBrain.View(layer, c.Ship, c.Target, c.Team, c.Start, c.HasStem && !c.Rammed, grappled, Leaking(c.Ship.Id.Value));
            var act = CaptainBrain.Decide(v); c.Last = act;
            switch (act)
            {
                case CaptainAct.Approach: Close(c, minute); break;
                case CaptainAct.Ram: DoRam(c, v); break;
                case CaptainAct.Grapple: DoGrapple(c, v); break;
                case CaptainAct.Board: DoBoard(c); break;
                case CaptainAct.Shoot: c.Ship.SpeedMmps = 0; break;
                case CaptainAct.Flee: c.Fleeing = true; Run(c); break;
            }
        }
    }

    /// <summary>The hull takes water faster than the pumps clear it: more holed cells than hands bailing (a reading of 4.6's sinking rule).</summary>
    private bool Leaking(ulong ship) => combat.Ship(ship) is { } v && combat.Holes(v) > combat.Row(ship).Bailers;

    private void Close(Captain c, long minute)
    {
        if (c.Side == 0) c.Side = CaptainBrain.ApproachSide(seed, c.Ship.Id.Value, minute, 0);
        Turn(c.Ship, c.Target.XMm, c.Target.YMm, +1);
        c.Ship.SpeedMmps = CruiseMmps;
    }

    private void Run(Captain c) { Turn(c.Ship, c.Target.XMm, c.Target.YMm, -1); c.Ship.SpeedMmps = FleeMmps; }

    /// <summary>Turns the ship toward (sign +1) or away from (-1) a point at the ship's turn rate, by the sign of the cross product of the heading and the bearing.</summary>
    private static void Turn(ShipFrame ship, int px, int py, int sign)
    {
        var (c, s) = ship.CosSin; long dx = px - ship.XMm, dy = py - ship.YMm;
        long cross = (c * dy - s * dx) >> 30, dot = (c * dx + s * dy) >> 30;
        int dir = cross == 0 ? (dot >= 0 ? 0 : 1) : Math.Sign(cross) * sign;
        ship.TurnPerTick = dir * ShipFrame.TurnOf(TurnDegPerS);
    }

    private void DoRam(Captain c, CaptainView v)
    {
        var (lx, ly) = c.Target.ToLocal(c.Ship.XMm, c.Ship.YMm);
        int cx = Math.Clamp(c.Target.Deck.CellX(lx), 0, c.Target.Deck.Width - 1), cy = Math.Clamp(c.Target.Deck.CellY(ly), 0, c.Target.Deck.Length - 1);
        var r = combat.Ram(c.Ship.Id.Value, c.Target.Id.Value, cx, cy, v.ClosingMmps);
        c.Rammed = true;                                                                    // one blow of this kind; it then closes to grapple
        c.Ship.SpeedMmps = Math.Max(0, c.Ship.SpeedMmps / 2);
        _ = r;
    }

    private void DoGrapple(Captain c, CaptainView v)
    {
        var bind = combat.BindOf(c.Ship.Id.Value, c.Target.Id.Value);
        if (bind == null || bind.Lines < 2) { combat.Throw(c.Ship.Id.Value, c.Target.Id.Value, v.DistMm, ThrowSkill, 0); bind = combat.BindOf(c.Ship.Id.Value, c.Target.Id.Value); }
        c.Ship.SpeedMmps = 0;
        if (bind is { Lines: >= 2 } && combat.Haul(bind, HaulMen, CaptainBrain.EveryTicks / 20)) layer.Join(c.Ship, c.Target);
    }

    private void DoBoard(Captain c)
    {
        int sent = 0; var floors = new List<(int X, int Y)>();
        var deck = c.Target.Deck;
        for (int y = 0; y < deck.Length; y++) for (int x = 0; x < deck.Width; x++) if (deck.Walkable(x, y, 0)) floors.Add((x, y));
        if (floors.Count == 0) return;
        for (int i = 0; i < layer.World.Fighters.Count && sent < BoardersPerThink; i++)
        {
            var f = layer.World.Fighters[i];
            if (f.Team != c.Team || !ReferenceEquals(layer.FrameOf(f.Id), c.Ship) || f.Act is FAct.Dead or FAct.Down or FAct.Fled or FAct.Stagger) continue;
            var cell = floors[(sent * 7 + i) % floors.Count];
            if (layer.BeginBoard(f, c.Target, cell.X, cell.Y, sent & 1)) sent++;
        }
    }
}
