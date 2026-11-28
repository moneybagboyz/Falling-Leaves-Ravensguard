using System.Collections;
using System.Reflection;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Core;

/// <summary>
/// P8-PF-2: the ids that live records hold. One pass over every book of a <see cref="WorldState"/> (the chronicle, the people and household tables, the generated layers and the content aside), through
/// the fields of the objects, collecting every 64-bit value (<see cref="EntityId"/>, <c>ulong</c>, <c>long</c>, in arrays, lists, sets and dictionaries) that is an event id or one of the candidate person
/// ids asked about. It is conservative on purpose: a number that merely looks like an id keeps a row or an entry it need not, never the other way, so a link a book holds never dangles after compaction.
/// The result is a set, so the order the walk meets things in cannot change it.
/// </summary>
public static class HeldIds
{
    private static readonly HashSet<Type> Skipped =
    [
        typeof(Chronicle), typeof(Ravensguard.Sim.People.Population), typeof(Ravensguard.Sim.People.PersonTable), typeof(Ravensguard.Sim.People.HouseholdTable),
        typeof(Ravensguard.Content.ContentDatabase), typeof(Ravensguard.Sim.WorldGen.Region.Region), typeof(ChunkStore), typeof(string), typeof(Type), typeof(IdAllocator),
    ];

    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

    /// <summary>The event ids and (of <paramref name="personCandidates"/>) the person ids the books of <paramref name="st"/> hold, not counting the chronicle's own entries.</summary>
    public static (HashSet<ulong> Events, HashSet<ulong> Persons) Of(WorldState st, IReadOnlySet<ulong> personCandidates)
    {
        var events = new HashSet<ulong>(); var persons = new HashSet<ulong>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        void V(ulong v)
        {
            if ((v >> EntityId.CounterBits) == (ulong)IdKind.Event) events.Add(v);
            else if (personCandidates.Count > 0 && personCandidates.Contains(v)) persons.Add(v);
        }
        void Push(object? o)
        {
            if (o == null) return;
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || Skipped.Contains(t) || o is Delegate) return;
            if (!t.IsValueType && !seen.Add(o)) return;
            stack.Push(o);
        }
        void Item(object? x)
        {
            switch (x)
            {
                case null: return;
                case ulong u: V(u); return;
                case long l: V((ulong)l); return;
                case EntityId e: V(e.Value); return;
                default: Push(x); return;
            }
        }
        Push(st);
        while (stack.Count > 0)
        {
            var o = stack.Pop(); var t = o.GetType();
            switch (o)
            {
                case ulong[] ua: foreach (var v in ua) V(v); continue;
                case long[] la: foreach (var v in la) V((ulong)v); continue;
                case Array when t.GetElementType() is { IsPrimitive: true } or { IsEnum: true }: continue;
                case IDictionary d:
                    foreach (DictionaryEntry de in d) { Item(de.Key); Item(de.Value); }
                    continue;
                case IEnumerable en:
                    foreach (var x in en) Item(x);
                    if (t.Namespace is { } ns && ns.StartsWith("System", StringComparison.Ordinal)) continue;         // a book that is itself a collection also has fields
                    break;
            }
            FieldInfo[]? fields;
            lock (FieldCache) FieldCache.TryGetValue(t, out fields);
            if (fields == null)
            {
                var list = new List<FieldInfo>();
                for (var b = t; b != null && b != typeof(object); b = b.BaseType)
                    foreach (var f in b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        var ft = f.FieldType;
                        if (ft == typeof(string) || ft.IsEnum || (ft.IsPrimitive && ft != typeof(ulong) && ft != typeof(long)) || typeof(Delegate).IsAssignableFrom(ft) || ft.IsPointer || ft == typeof(Type)) continue;
                        list.Add(f);
                    }
                fields = [.. list];
                lock (FieldCache) FieldCache[t] = fields;
            }
            foreach (var f in fields)
            {
                object? v;
                try { v = f.GetValue(o); } catch { continue; }
                Item(v);
            }
        }
        return (events, persons);
    }

    /// <summary>The person ids of <paramref name="candidates"/> that kept chronicle entries name: as a subject, or as a person in an entry's or a cause's parameters.</summary>
    public static HashSet<ulong> NamedInChronicle(Chronicle c, IReadOnlySet<ulong> candidates)
    {
        var hit = new HashSet<ulong>();
        if (candidates.Count == 0) return hit;
        void P(IReadOnlyList<ChronParam>? ps) { if (ps != null) foreach (var p in ps) if (p.Kind == ChronParamKind.Person && candidates.Contains((ulong)p.Value)) hit.Add((ulong)p.Value); }
        foreach (var e in c.All)
        {
            foreach (var s in e.Subjects) if (candidates.Contains(s.Value)) hit.Add(s.Value);
            P(e.Params);
            foreach (var cs in e.Causes) P(cs.NoteParams);
        }
        return hit;
    }
}
