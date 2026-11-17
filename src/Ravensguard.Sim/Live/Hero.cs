using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Live;

public enum MoveMode : byte { Walk, Jog, Sprint }

/// <summary>Facing as 0 = south (down the screen), 1 = west, 2 = east, 3 = north. The view picks the sprite.</summary>
public enum Facing : byte { South, West, East, North }

/// <summary>What the player is asking for this tick (set by input; the sim does not know about keys).</summary>
public readonly record struct MoveInput(int Dx, int Dy, bool Sprint = false, bool Walk = false)
{
    public static readonly MoveInput None = default;
    public bool Any => Dx != 0 || Dy != 0;
}

/// <summary>Hero state as published in a snapshot. Prev* are the position one tick ago, for render interpolation.</summary>
public readonly record struct HeroSnapshot(Fixed X, Fixed Y, int Z, Fixed PrevX, Fixed PrevY, int PrevZ, Facing Facing, bool Moving, MoveMode Mode, bool Torch, int PathRemaining)
{
    public int CellX => X.Floor();
    public int CellY => Y.Floor();
    public Ravensguard.Sim.World.Cell3 Cell => new(CellX, CellY, Z);
}
