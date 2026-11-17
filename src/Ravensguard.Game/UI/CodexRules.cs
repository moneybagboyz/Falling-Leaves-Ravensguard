using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Codex;

namespace Ravensguard.Game.UI;

/// <summary>The rule pages of the Codex (plan 3f: the Codex is the in-game manual): one page per input context listing every action with the key it is bound to now.</summary>
public static class CodexRules
{
    /// <summary>Key hints are written as <c>{@action.id}</c> tokens and turned into the bound key when a page is shown, so a rebind shows without rebuilding the book (and saved books never hold stale keys).</summary>
    static string Key(string actionId) => "{@" + actionId + "}";

    static readonly Regex Token = new(@"\{@([a-z_.]+)\}", RegexOptions.Compiled);

    /// <summary>Replaces key tokens by the keys bound right now ("unbound" text for an action without one).</summary>
    public static string ResolveKeys(string text) => text.IndexOf("{@", System.StringComparison.Ordinal) < 0 ? text : Token.Replace(text, m =>
    {
        string k = InputBootstrap.Hint(m.Groups[1].Value);
        return k.Length > 0 ? k : L.T("CONTROLS_UNBOUND");
    });

    /// <summary>Contexts that exist only in debug builds (the blueprint editor's keys); release builds do not list them.</summary>
    static bool IsDevContext(InputContext c) => c == InputContext.BlueprintEditor && !OS.IsDebugBuild();

    public static List<CodexRule> FromInput()
    {
        var rules = new List<CodexRule>
        {
            new("rule.basics", L.T("CODEX_BASICS_TITLE"), L.F("CODEX_BASICS_TEXT", Key("world.panel_character"), Key("world.panel_codex"), Key("world.map"))),
        };
        foreach (var g in InputDefs.All.GroupBy(a => a.Context).OrderBy(g => (int)g.Key))
        {
            if (IsDevContext(g.Key)) continue;
            var sb = new StringBuilder();
            foreach (var a in g) sb.Append(L.T(a.LocKey)).Append(": ").Append(Key(a.Id)).Append('\n');
            string ctx = g.Key.ToString();
            rules.Add(new CodexRule("rule.controls." + ctx.ToLowerInvariant(), L.F("CODEX_CONTROLS", L.T("CONTEXT_" + ctx.ToUpperInvariant())), sb.ToString().TrimEnd()));
        }
        return rules;
    }
}
