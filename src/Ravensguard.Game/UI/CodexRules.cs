using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Codex;

namespace Ravensguard.Game.UI;

/// <summary>The rule pages of the Codex (plan 3f: the Codex is the in-game manual): one page per input context listing every action with the key it is bound to now.</summary>
public static class CodexRules
{
    public static List<CodexRule> FromInput()
    {
        var rules = new List<CodexRule>
        {
            new("rule.basics", L.T("CODEX_BASICS_TITLE"), L.F("CODEX_BASICS_TEXT", InputBootstrap.Hint("world.panel_character"), InputBootstrap.Hint("world.panel_codex"), InputBootstrap.Hint("world.map"))),
        };
        foreach (var g in InputDefs.All.GroupBy(a => a.Context).OrderBy(g => (int)g.Key))
        {
            var sb = new StringBuilder();
            foreach (var a in g)
            {
                string key = InputBootstrap.Hint(a.Id);
                sb.Append(L.T(a.LocKey)).Append(": ").Append(key.Length > 0 ? key : L.T("CONTROLS_UNBOUND")).Append('\n');
            }
            string ctx = g.Key.ToString();
            rules.Add(new CodexRule("rule.controls." + ctx.ToLowerInvariant(), L.F("CODEX_CONTROLS", L.T("CONTEXT_" + ctx.ToUpperInvariant())), sb.ToString().TrimEnd()));
        }
        return rules;
    }
}
