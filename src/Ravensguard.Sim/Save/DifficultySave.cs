using MessagePack;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Save;

/// <summary>The optional "difficulty" section (P6-A4; no format bump, absent while every slider is at its default and none ever changed): the slider values and the history of changes.</summary>
[MessagePackObject]
public sealed class DifficultySave
{
    public const string SectionName = "difficulty";

    /// <summary><see cref="Difficulty.ToArray"/>.</summary>
    [Key(0)] public int[] Values { get; set; } = [];
    /// <summary>Per change: game second, then the six values after it.</summary>
    [Key(1)] public long[][] Changes { get; set; } = [];

    public static DifficultySave Capture(WorldState w) => new()
    {
        Values = w.Difficulty.ToArray(),
        Changes = [.. w.DifficultyLog.Select(c => new long[] { c.Sec }.Concat(c.Values.Select(v => (long)v)).ToArray())],
    };

    public void Restore(WorldState w)
    {
        if (Values.Length < Difficulty.ValueCount) throw new SaveException("Corrupt 'difficulty' section in save.");
        w.Difficulty = Difficulty.FromArray(Values);
        w.DifficultyLog.Clear();
        foreach (var c in Changes)
        {
            if (c.Length < 1 + Difficulty.ValueCount) throw new SaveException("Corrupt difficulty change in save.");
            w.DifficultyLog.Add(new DifficultyChange(c[0], [.. c.Skip(1).Select(v => (int)v)]));
        }
    }
}
