using System.Text;

namespace Ravensguard.Sim.Debug;

/// <summary>Named dev commands with help text. Handlers return the output text. Ordering is ordinal, so output is deterministic.</summary>
public sealed class DevCommandRegistry
{
    readonly SortedDictionary<string, (string Help, Func<string[], string> Run)> _commands = new(StringComparer.Ordinal);

    public DevCommandRegistry()
    {
        Register("help", "List commands, or help <command> for one", Help);
    }

    public IEnumerable<string> Names => _commands.Keys;

    public void Register(string name, string help, Func<string[], string> handler)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
            throw new ArgumentException("Command name must be non-empty and contain no whitespace.", nameof(name));
        _commands[name] = (help, handler);
    }

    /// <summary>Runs a command line. Never throws for user error; handler exceptions become an error line.</summary>
    public string Execute(string line)
    {
        if (!TryParse(line, out var tokens, out var error)) return error;
        if (tokens.Count == 0) return "";
        if (!_commands.TryGetValue(tokens[0], out var cmd)) return Unknown(tokens[0]);
        try { return cmd.Run(tokens.Skip(1).ToArray()); }
        catch (Exception e) { return $"error: {tokens[0]} failed: {e.Message}"; }
    }

    string Unknown(string name)
    {
        var near = _commands.Keys.Where(k => k.StartsWith(name, StringComparison.Ordinal) || name.StartsWith(k, StringComparison.Ordinal)).ToList();
        var msg = $"Unknown command '{name}'. Type 'help' for a list.";
        return near.Count > 0 ? msg + " Did you mean: " + string.Join(", ", near) + "?" : msg;
    }

    string Help(string[] args)
    {
        if (args.Length > 0)
            return _commands.TryGetValue(args[0], out var c) ? $"{args[0]} - {c.Help}" : Unknown(args[0]);
        var sb = new StringBuilder();
        foreach (var kv in _commands) sb.Append(kv.Key).Append(" - ").Append(kv.Value.Help).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Splits on whitespace; double quotes group words; backslash escapes a quote or backslash inside quotes.</summary>
    public static bool TryParse(string line, out List<string> tokens, out string error)
    {
        tokens = new List<string>(); error = "";
        var cur = new StringBuilder(); bool inQuote = false, have = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuote)
            {
                if (ch == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\')) cur.Append(line[++i]);
                else if (ch == '"') inQuote = false;
                else cur.Append(ch);
            }
            else if (ch == '"') { inQuote = true; have = true; }
            else if (char.IsWhiteSpace(ch)) { if (have) { tokens.Add(cur.ToString()); cur.Clear(); have = false; } }
            else { cur.Append(ch); have = true; }
        }
        if (inQuote) { error = "error: unterminated quote"; tokens.Clear(); return false; }
        if (have) tokens.Add(cur.ToString());
        return true;
    }
}
