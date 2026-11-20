using Ravensguard.Content;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// The utility AI's arithmetic (diplomacy-and-ai-lords 3, 4.2): an action's considerations are piecewise-linear curves over named inputs, each giving 0..1000, multiplied into a utility 0..1000. The strongest driver with a note
/// is the cause the chronicle gives. Pure and integer: no float, no random draw.
/// </summary>
public static class Considerations
{
    /// <summary>A curve's value at <paramref name="x"/>: linear between its points, flat beyond the ends; an empty curve is 1000 (it never vetoes).</summary>
    public static int Curve(IReadOnlyList<List<int>> pts, int x)
    {
        if (pts.Count == 0) return 1000;
        if (x <= pts[0][0]) return Math.Clamp(pts[0][1], 0, 1000);
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i];
            if (x <= b[0])
            {
                int dx = b[0] - a[0]; if (dx <= 0) return Math.Clamp(b[1], 0, 1000);
                return Math.Clamp(a[1] + (b[1] - a[1]) * (x - a[0]) / dx, 0, 1000);
            }
        }
        return Math.Clamp(pts[^1][1], 0, 1000);
    }

    /// <summary>The utility of an action for these inputs and the note of its strongest driver ("" when none is annotated).</summary>
    public static (int Utility, string Note) Evaluate(LordActionDef def, Func<string, int> input)
    {
        int u = 1000; int best = -1; string note = "";
        foreach (var c in def.Considerations)
        {
            int v = Curve(c.Curve, input(c.Input));
            u = u * v / 1000;
            if (c.NoteKey.Length > 0 && v > best) { best = v; note = c.NoteKey; }
        }
        return (u, note);
    }
}
