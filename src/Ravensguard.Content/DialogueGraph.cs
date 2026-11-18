namespace Ravensguard.Content;

/// <summary>
/// The structural checks of an authored dialogue (dialogue spec 3.4), run at load: the start node exists, every <c>goto</c> resolves, no node is unreachable, a node has text, at most six choices,
/// and no cycle is without a way out (every reachable node can reach <c>end</c>). Condition and effect ids are checked by the vocabulary; the en.csv keys by <see cref="MissingKeys"/>.
/// </summary>
public static class DialogueGraph
{
    public const string End = "end";
    public const int MaxChoices = 6;

    public static void Check(DialogueDef d, SourceLocation loc, ContentReport report)
    {
        foreach (var msg in Problems(d)) report.Error(loc, msg);
    }

    /// <summary>Every problem of the dialogue, each naming the dialogue and the node.</summary>
    public static List<string> Problems(DialogueDef d)
    {
        var res = new List<string>(); string who = $"dialogue '{d.Id}'";
        if (!d.Nodes.ContainsKey(d.Start)) res.Add($"{who}: start node '{d.Start}' does not exist");
        if (d.Repeat.Length > 0 && !d.Nodes.ContainsKey(d.Repeat)) res.Add($"{who}: repeat node '{d.Repeat}' does not exist");
        foreach (var (name, n) in d.Nodes)
        {
            if (string.IsNullOrEmpty(n.Text)) res.Add($"{who}, node '{name}': no text key");
            if (n.Choices.Count > MaxChoices) res.Add($"{who}, node '{name}': {n.Choices.Count} choices (at most {MaxChoices})");
            if (n.Choices.Count == 0 && n.Goto.Length == 0) res.Add($"{who}, node '{name}': no goto and no choices (use \"goto\": \"end\")");
            foreach (var t in Targets(n))
                if (t != End && !d.Nodes.ContainsKey(t)) res.Add($"{who}, node '{name}': goto '{t}' does not exist");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal); var stack = new Stack<string>();
        foreach (var s in new[] { d.Start, d.Repeat }) if (s.Length > 0 && d.Nodes.ContainsKey(s) && seen.Add(s)) stack.Push(s);
        while (stack.Count > 0)
            foreach (var t in Targets(d.Nodes[stack.Pop()])) if (t != End && d.Nodes.ContainsKey(t) && seen.Add(t)) stack.Push(t);
        foreach (var name in d.Nodes.Keys.Order(StringComparer.Ordinal)) if (!seen.Contains(name)) res.Add($"{who}, node '{name}': unreachable from '{d.Start}'");
        // every reachable node must be able to reach end
        var canEnd = new HashSet<string>(StringComparer.Ordinal); bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (name, n) in d.Nodes)
                if (!canEnd.Contains(name) && Targets(n).Any(t => t == End || canEnd.Contains(t))) { canEnd.Add(name); grew = true; }
        }
        foreach (var name in seen.Order(StringComparer.Ordinal)) if (!canEnd.Contains(name)) res.Add($"{who}, node '{name}': a cycle with no way to leave (needs a choice that goes to 'end')");
        return res;
    }

    private static IEnumerable<string> Targets(DialogueNodeDef n)
    {
        if (n.Goto.Length > 0) yield return n.Goto;
        foreach (var c in n.Choices) yield return c.Goto;
    }

    /// <summary>Every text key of the dialogue that <paramref name="keys"/> lacks, as readable errors.</summary>
    public static List<string> MissingKeys(DialogueDef d, Func<string, bool> keys)
    {
        var res = new List<string>();
        foreach (var (name, n) in d.Nodes)
        {
            if (n.Text.Length > 0 && !keys(n.Text)) res.Add($"dialogue '{d.Id}', node '{name}': key {n.Text} is not in en.csv");
            foreach (var c in n.Choices) if (!keys(c.Text)) res.Add($"dialogue '{d.Id}', node '{name}': choice key {c.Text} is not in en.csv");
        }
        return res;
    }
}
