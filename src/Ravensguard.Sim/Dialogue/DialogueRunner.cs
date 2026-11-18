using Ravensguard.Content;

namespace Ravensguard.Sim.Dialogue;

/// <summary>An effect an authored dialogue asks the world for; the host applies it at a barrier (flags are set at once on the context so later conditions see them).</summary>
public sealed record DialogueEffect(string Id, IReadOnlyDictionary<string, string> Args);

/// <summary>A choice as the panel shows it: its text, and when its condition fails why (a skill or item or coin one is shown locked, any other is hidden).</summary>
public sealed record DialogueChoiceView(int Index, string Text, bool Locked, string NeedText);

/// <summary>
/// Plays an authored dialogue (dialogue spec 3.4): a node says its text and runs its effects when entered; it then has up to six choices or a <c>goto</c> that E advances. Nothing here is
/// scripting: conditions and effects are the typed vocabulary. A dialogue whose <c>once_flags</c> are set starts at its <c>repeat</c> node.
/// </summary>
public sealed class DialogueRunner
{
    private readonly DialogueDef _def; private readonly TalkContext _ctx; private readonly ITextSource _text;
    public string NodeId { get; private set; } = "";
    public bool Ended { get; private set; }
    public List<DialogueEffect> Effects { get; } = [];
    public List<string> History { get; } = [];

    public DialogueRunner(DialogueDef def, TalkContext ctx, ITextSource text)
    {
        _def = def; _ctx = ctx; _text = text;
        string start = def.Repeat.Length > 0 && def.OnceFlags.Any(ctx.Flags.Contains) ? def.Repeat : def.Start;
        Enter(start);
    }

    private DialogueNodeDef Node => _def.Nodes[NodeId];
    public string Text => Ended ? "" : ParamResolver.Say(Node.Text, _ctx, _text, _ctx.Female);
    public bool NeedsAdvance => !Ended && Node.Choices.Count == 0;

    public IReadOnlyList<DialogueChoiceView> Choices
    {
        get
        {
            var res = new List<DialogueChoiceView>(); if (Ended) return res;
            var n = Node;
            for (int i = 0; i < n.Choices.Count; i++)
            {
                var c = n.Choices[i];
                if (TalkConditions.All(c.Conditions, _ctx)) { res.Add(new DialogueChoiceView(i, ParamResolver.Say(c.Text, _ctx, _text), false, "")); continue; }
                var failed = c.Conditions.FirstOrDefault(k => !TalkConditions.Holds(k, _ctx));
                if (failed != null && failed.Id is "has_proficiency" or "has_item" or "has_coin_at_least" or "has_followers_at_least")
                    res.Add(new DialogueChoiceView(i, ParamResolver.Say(c.Text, _ctx, _text), true, NeedOf(failed)));
            }
            return res;
        }
    }

    private string NeedOf(ConditionRefDef c) => c.Id switch
    {
        "has_proficiency" => _text.T("TALK_NEEDS_SKILL").Replace("{skill}", TalkConditions.Arg(c, "skill")).Replace("{level}", TalkConditions.Arg(c, "level")),
        "has_item" => _text.T("TALK_NEEDS_ITEM").Replace("{item}", TalkConditions.Arg(c, "item")),
        "has_coin_at_least" => _text.T("TALK_NEEDS_COIN"),
        _ => _text.T("TALK_NEEDS_FOLLOWERS"),
    };

    /// <summary>Takes a choice (its index in the node, as <see cref="DialogueChoiceView.Index"/> says). False when it is hidden or locked.</summary>
    public bool Choose(int index)
    {
        if (Ended || index < 0 || index >= Node.Choices.Count) return false;
        var c = Node.Choices[index];
        if (!TalkConditions.All(c.Conditions, _ctx)) return false;
        History.Add(c.Text);
        Run(c.Effects); Enter(c.Goto); return true;
    }

    /// <summary>E on a node with no choices: on to its goto.</summary>
    public bool Advance()
    {
        if (Ended || Node.Choices.Count > 0) return false;
        Enter(Node.Goto); return true;
    }

    private void Enter(string id)
    {
        if (id == DialogueGraph.End || !_def.Nodes.ContainsKey(id)) { Ended = true; return; }
        NodeId = id; History.Add(Node.Text); Run(Node.Effects);
        if (Effects.Any(e => e.Id == "end_dialogue")) Ended = true;
    }

    private void Run(IReadOnlyList<ConditionRefDef> effects)
    {
        foreach (var e in effects)
        {
            if (e.Id == "set_flag") _ctx.Flags.Add(TalkConditions.Arg(e, "flag"));
            Effects.Add(new DialogueEffect(e.Id, e.Args));
        }
    }
}
