using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Phase6Overview;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// The shared kit of the Phase 7 overview tests (docs/specs/phase7/00-phase7-overview.md section 4), the same shape as <see cref="P6Game"/>: a new game of seed 1234 (delegated to it), save-load-save round trips,
/// the chronicle chain walk and the section diff. The armies, sieges and sea raids run on the existing <c>ArmyRig</c> (the whole runtime of seed 1234 on the rule world).
/// </summary>
internal static class P7Game
{
    public const string Hash = "h";

    public static (SeamWorld W, WorldState State) Game(ulong seed = 1234) => P6Game.Game(seed);

    /// <summary>The state saved, loaded and saved again: both byte arrays must be equal. Returns the loaded state and the bytes.</summary>
    public static (WorldState Back, byte[] Bytes) RoundTrip(WorldState st, SeamWorld w, string label, string version = "0.4.0-dev")
    {
        var file = WorldSave.Capture(st, Hash, version);
        var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History); back.Content = w.Content;
        var again = WorldSave.Capture(back, Hash, version);
        var bytes2 = SaveCodec.Encode(again);
        Assert.True(bytes.AsSpan().SequenceEqual(bytes2), $"{label}: the save changed after a load (sections {Differences(file, again)})");
        return (back, bytes);
    }

    public static string Differences(SaveFile a, SaveFile b) =>
        string.Join(",", a.Sections.Keys.Union(b.Sections.Keys).Order(StringComparer.Ordinal).Where(k => !(a.Sections.TryGetValue(k, out var x) && b.Sections.TryGetValue(k, out var y) && x.Span.SequenceEqual(y.Span))));

    /// <summary>An entry and its first-cause ancestors to the root (at most 60).</summary>
    public static List<ChronicleEntry> Chain(WorldState st, ChronicleEntry e)
    {
        var all = st.Chronicle.All.ToDictionary(x => x.Id.Value); var l = new List<ChronicleEntry>();
        for (var cur = e; cur != null && l.Count < 60;)
        {
            l.Add(cur);
            var next = cur.Causes.Select(c => c.EventId).FirstOrDefault(x => x != null && all.ContainsKey(x.Value.Value));
            cur = next == null ? null : all[next.Value.Value];
        }
        return l;
    }
}
