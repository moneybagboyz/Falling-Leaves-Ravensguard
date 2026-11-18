namespace Ravensguard.Sim.Core;

/// <summary>
/// Who issued a command (docs/specs/phase3/autopilot-v1.md 2, 4.1): the player, the autopilot layer or an NPC. It is part of the command log, so a replay of a session with autopilot in it repeats both, and the
/// barrier can tell which commands the limits apply to. Append only.
/// </summary>
public enum CommandOrigin : byte { Human = 0, Autopilot = 1, Npc = 2 }
