using System.Text.Json;
using System.Text.Json.Nodes;
using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Dialogue;

/// <summary>
/// The talk content in one place (dialogue spec 3): systemic lines, barks, authored dialogues, rumour types and the numbers. <see cref="Default"/> is the shipped
/// <c>content/base/dialogue</c> folder, embedded in the assembly so the pure selection functions need no content folder; <see cref="From"/> reads a loaded <see cref="ContentDatabase"/> (a mod's
/// changes), falling back to the default for any type it lacks.
/// </summary>
public sealed class LineBook
{
    public IReadOnlyList<LineDef> Lines { get; init; } = [];
    public IReadOnlyList<BarkDef> Barks { get; init; } = [];
    public IReadOnlyList<DialogueDef> Dialogues { get; init; } = [];
    public IReadOnlyList<RumourTypeDef> RumourTypes { get; init; } = [];
    public TalkSettingsDef Settings { get; init; } = new() { Id = "default" };

    private Dictionary<string, LineDef[]>? _byTopic;
    private Dictionary<string, RumourTypeDef[]>? _byEntry;
    private static readonly LineDef[] None = [];

    private static readonly Lazy<LineBook> DefaultBook = new(LoadEmbedded);
    public static LineBook Default => DefaultBook.Value;

    public static readonly string[] Topics = ["greeting", "farewell", "smalltalk", "work_offer", "trade", "hire", "recruit", "directions", "rumour", "refusal", "need", "hostile"];

    public LineDef[] OfTopic(string topic)
    {
        _byTopic ??= Lines.GroupBy(l => l.Topic, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        return _byTopic.TryGetValue(topic, out var a) ? a : None;
    }

    /// <summary>The rumour rows of a chronicle entry type, in file order (the first whose <c>match</c> holds wins).</summary>
    public RumourTypeDef[] RumourRowsOf(string entryType)
    {
        _byEntry ??= RumourTypes.GroupBy(r => r.Entry, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        return _byEntry.TryGetValue(entryType, out var a) ? a : [];
    }

    public DialogueDef? DialogueOf(string speaker) => Dialogues.FirstOrDefault(d => d.Speaker == speaker);

    public static LineBook From(ContentDatabase db)
    {
        var d = Default;
        return new LineBook
        {
            Lines = db.All<LineDef>().ToList() is { Count: > 0 } l ? l : d.Lines,
            Barks = db.All<BarkDef>().ToList() is { Count: > 0 } b ? b : d.Barks,
            Dialogues = db.All<DialogueDef>().ToList() is { Count: > 0 } dl ? dl : d.Dialogues,
            RumourTypes = db.All<RumourTypeDef>().ToList() is { Count: > 0 } r ? r : d.RumourTypes,
            Settings = db.TryGet<TalkSettingsDef>("default", out var s) ? s : d.Settings,
        };
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static LineBook LoadEmbedded()
    {
        var asm = typeof(LineBook).Assembly;
        var lines = new List<LineDef>(); var barks = new List<BarkDef>(); var dlg = new List<DialogueDef>(); var rt = new List<RumourTypeDef>(); TalkSettingsDef? set = null;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("dialogue/", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var arr = JsonNode.Parse(s, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();
            foreach (var n in arr)
            {
                var o = n!.AsObject(); string type = o["type"]!.GetValue<string>(); o.Remove("type");
                switch (type)
                {
                    case "line": lines.Add(o.Deserialize<LineDef>(Opts)!); break;
                    case "bark": barks.Add(o.Deserialize<BarkDef>(Opts)!); break;
                    case "dialogue": dlg.Add(o.Deserialize<DialogueDef>(Opts)!); break;
                    case "rumour_type": rt.Add(o.Deserialize<RumourTypeDef>(Opts)!); break;
                    case "talk_settings": set = o.Deserialize<TalkSettingsDef>(Opts); break;
                }
            }
        }
        return new LineBook { Lines = lines, Barks = barks, Dialogues = dlg, RumourTypes = rt, Settings = set ?? new() { Id = "default" } };
    }
}

/// <summary>The lines a speaker used lately (dialogue spec 4.1): the last 8 of a speaker and the last 24 of a (speaker, listener) pair get weight 0.</summary>
public sealed class TalkHistory
{
    private readonly Dictionary<ulong, LinkedList<string>> _speaker = [];
    private readonly Dictionary<(ulong, ulong), LinkedList<string>> _pair = [];
    public int SpeakerTurns { get; init; } = 8;
    public int PairTurns { get; init; } = 24;

    public bool Recent(ulong speaker, ulong listener, string lineId) => RecentBySpeaker(speaker, lineId) || RecentByPair(speaker, listener, lineId);
    public bool RecentBySpeaker(ulong speaker, string lineId) => _speaker.TryGetValue(speaker, out var a) && a.Contains(lineId);
    public bool RecentByPair(ulong speaker, ulong listener, string lineId) => _pair.TryGetValue((speaker, listener), out var b) && b.Contains(lineId);

    public void Record(ulong speaker, ulong listener, string lineId)
    {
        Push(_speaker.TryGetValue(speaker, out var a) ? a : _speaker[speaker] = new(), lineId, SpeakerTurns);
        Push(_pair.TryGetValue((speaker, listener), out var b) ? b : _pair[(speaker, listener)] = new(), lineId, PairTurns);
    }

    private static void Push(LinkedList<string> l, string id, int cap) { l.AddLast(id); while (l.Count > cap) l.RemoveFirst(); }
}

/// <summary>Choosing a systemic line (dialogue spec 4.1).</summary>
public static class LineSelector
{
    /// <summary>The number of conditions of the line that hold, plus 1: the weight multiplier.</summary>
    public static int Specificity(LineDef l) => l.Conditions.Count;

    /// <summary>
    /// Candidates = lines of the topic whose conditions all hold; score = <c>weight_pm x (1 + specificity)</c>; lines used lately score 0; a weighted draw on stream 0x310 keyed
    /// <c>(gameSec / 60, speaker, listenerIdx x 16 + turn mod 16)</c>. No candidate: the <c>smalltalk</c> lines; if every one was used lately the history is ignored (never silence).
    /// </summary>
    public static LineDef? Pick(LineBook book, string topic, TalkContext x, ulong seed, long gameSec, ulong speaker, int listenerIdx, int turn, TalkHistory? hist = null, ulong listener = 0, Func<LineDef, bool>? extra = null)
    {
        var pool = Candidates(book, topic, x, extra);
        if (pool.Count == 0 && topic != "smalltalk") pool = Candidates(book, "smalltalk", x, extra);
        if (pool.Count == 0) return null;
        long total = 0; Span<long> w = pool.Count <= 512 ? stackalloc long[pool.Count] : new long[pool.Count];
        // first both windows count; when they leave nothing the pair's window is set aside, then the speaker's (never silence)
        for (int pass = 0; pass < 3 && total == 0; pass++)
            for (int i = 0; i < pool.Count; i++)
            {
                bool used = hist != null && (pass == 0 ? hist.Recent(speaker, listener, pool[i].Id) : pass == 1 && hist.RecentBySpeaker(speaker, pool[i].Id));
                w[i] = used ? 0 : (long)pool[i].WeightPm * (1 + Specificity(pool[i])); total += w[i];
            }
        if (total <= 0) return pool[0];
        long r = (long)(DetRng.Next(seed, gameSec / 60, speaker, RngStreams.LinePick, (uint)(listenerIdx * 16 + (turn & 15))) % (ulong)total);
        for (int i = 0; i < pool.Count; i++) { if (r < w[i]) return pool[i]; r -= w[i]; }
        return pool[^1];
    }

    public static List<LineDef> Candidates(LineBook book, string topic, TalkContext x, Func<LineDef, bool>? extra = null)
    {
        var res = new List<LineDef>();
        foreach (var l in book.OfTopic(topic))
            if (TalkConditions.All(l.Conditions, x) && (extra == null || extra(l))) res.Add(l);
        return res;
    }
}
