using System.Reflection;
using MessagePack;

namespace Ravensguard.Sim.Save;

/// <summary>One column of the people tables stored as what differs from the regenerated baseline: rows changed inside the baseline's length (gap-coded) with their new values, and the rows added after it.</summary>
[MessagePackObject]
public sealed class ColumnPatch<T>
{
    /// <summary>Length of the current column.</summary>
    [Key(0)] public int Length { get; set; }
    /// <summary>Changed row numbers, ascending, as gaps: the first is the row, each next is (row - previous row - 1).</summary>
    [Key(1)] public int[] Gaps { get; set; } = [];
    [Key(2)] public T[] Values { get; set; } = [];
    /// <summary>Rows from the baseline's length to <see cref="Length"/>.</summary>
    [Key(3)] public T[] Tail { get; set; } = [];
}

/// <summary>The columns of a <see cref="PeopleData"/> as patches over the baseline people (the population the region's generator makes again from seed, history and content). <see cref="Cols"/> follow the order of <see cref="PeopleDiffer.Columns"/>; an empty entry means the column equals the baseline.</summary>
[MessagePackObject]
public sealed class PeopleDiff
{
    [Key(0)] public byte[][] Cols { get; set; } = [];
}

/// <summary>
/// Saves a region's people as a difference from the baseline population (seamless-regions note N24). The baseline is the output of the pure populator for the region, which the
/// loader has anyway (it just generated it) or can make again; what a game changes in a few days (some jobs, beds, skills, new babies) is a tenth of the columns' rows, the names,
/// birth days, kin and attributes none. Pure and deterministic: the same people and baseline always give the same bytes, resident or paged.
/// </summary>
public static class PeopleDiffer
{
    /// <summary>The array columns of <see cref="PeopleData"/> in key order (the order patches are stored in).</summary>
    public static readonly PropertyInfo[] Columns = typeof(PeopleData).GetProperties()
        .Where(p => p.PropertyType.IsArray && p.GetCustomAttribute<KeyAttribute>()?.IntKey is not null)
        .OrderBy(p => p.GetCustomAttribute<KeyAttribute>()!.IntKey!.Value).ToArray();

    private static readonly MethodInfo PatchM = typeof(PeopleDiffer).GetMethod(nameof(PatchOf), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ApplyM = typeof(PeopleDiffer).GetMethod(nameof(ApplyTo), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The compact form of <paramref name="cur"/> over <paramref name="baseline"/>: counters kept, every column replaced by its patch.</summary>
    public static PeopleData Compact(PeopleData cur, PeopleData baseline)
    {
        var d = new PeopleData { Count = cur.Count, PersonBaseCounter = cur.PersonBaseCounter, HhCount = cur.HhCount, HhBaseCounter = cur.HhBaseCounter };
        var diff = new PeopleDiff { Cols = new byte[Columns.Length][] };
        for (int i = 0; i < Columns.Length; i++)
        {
            var c = Columns[i];
            diff.Cols[i] = (byte[])PatchM.MakeGenericMethod(c.PropertyType.GetElementType()!).Invoke(null, [c.GetValue(baseline)!, c.GetValue(cur)!])!;
            c.SetValue(d, Array.CreateInstance(c.PropertyType.GetElementType()!, 0));
        }
        d.Diff = diff;
        return d;
    }

    /// <summary>The whole <see cref="PeopleData"/> of a compact one (returned as it is when it holds no patches).</summary>
    public static PeopleData Expand(PeopleData compact, PeopleData baseline)
    {
        if (compact.Diff == null) return compact;
        if (compact.Diff.Cols.Length != Columns.Length) throw new SaveException("The people section was saved with a different set of columns.");
        var d = new PeopleData { Count = compact.Count, PersonBaseCounter = compact.PersonBaseCounter, HhCount = compact.HhCount, HhBaseCounter = compact.HhBaseCounter };
        for (int i = 0; i < Columns.Length; i++)
        {
            var c = Columns[i];
            c.SetValue(d, ApplyM.MakeGenericMethod(c.PropertyType.GetElementType()!).Invoke(null, [c.GetValue(baseline)!, compact.Diff.Cols[i]])!);
        }
        return d;
    }

    private static byte[] PatchOf<T>(T[] b, T[] c)
    {
        var eq = EqualityComparer<T>.Default;
        int common = Math.Min(b.Length, c.Length);
        var gaps = new List<int>(); var vals = new List<T>();
        int prev = -1;
        for (int i = 0; i < common; i++)
            if (!eq.Equals(b[i], c[i])) { gaps.Add(i - prev - 1); vals.Add(c[i]); prev = i; }
        if (gaps.Count == 0 && c.Length == b.Length) return [];
        return SaveCodec.Serialize(new ColumnPatch<T> { Length = c.Length, Gaps = [.. gaps], Values = [.. vals], Tail = c.Length > b.Length ? c.AsSpan(b.Length).ToArray() : [] });
    }

    private static T[] ApplyTo<T>(T[] b, byte[] patch)
    {
        if (patch.Length == 0) return (T[])b.Clone();
        var p = SaveCodec.Deserialize<ColumnPatch<T>>(patch);
        if (p.Length < 0 || p.Gaps.Length != p.Values.Length) throw new SaveException("Corrupt people patch.");
        var r = new T[p.Length];
        Array.Copy(b, r, Math.Min(b.Length, p.Length));
        int at = -1;
        for (int i = 0; i < p.Gaps.Length; i++) { at += p.Gaps[i] + 1; if ((uint)at >= (uint)Math.Min(b.Length, p.Length)) throw new SaveException("Corrupt people patch (row out of range)."); r[at] = p.Values[i]; }
        if (p.Tail.Length > 0) { if (b.Length + p.Tail.Length != p.Length) throw new SaveException("Corrupt people patch (tail)."); Array.Copy(p.Tail, 0, r, b.Length, p.Tail.Length); }
        return r;
    }
}
