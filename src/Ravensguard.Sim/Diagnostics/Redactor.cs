using System.Text.RegularExpressions;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// Strips the tester's identity out of text (testers spec 3.2, 4.6): the home folder and the user name in any path become <c>~</c>.
/// Machine names and environment variables are never written in the first place.
/// </summary>
public sealed class Redactor
{
    private readonly string[] _homes;
    private readonly string? _user;
    private static readonly Regex HomePath = new(@"(?:/home/|/Users/|[A-Za-z]:[\\/]+Users[\\/]+)[^/\\\s""':;,|]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A redactor for a given home folder and user name (tests pass a fake pair).</summary>
    public Redactor(string? home, string? userName)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(home))
        {
            string h = home.TrimEnd('/', '\\');
            if (h.Length > 1) { list.Add(h); list.Add(h.Replace('\\', '/')); list.Add(h.Replace('/', '\\')); }
        }
        _homes = list.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length).ToArray();
        _user = string.IsNullOrWhiteSpace(userName) || userName.Length < 3 ? null : userName;
    }

    public static Redactor ForCurrentUser() => new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    /// <summary>The text with every home path and user name replaced by <c>~</c>.</summary>
    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var h in _homes) text = text.Replace(h, "~", StringComparison.OrdinalIgnoreCase);
        text = HomePath.Replace(text, "~");
        if (_user != null) text = Regex.Replace(text, @"(?<![A-Za-z0-9])" + Regex.Escape(_user) + @"(?![A-Za-z0-9])", "~", RegexOptions.IgnoreCase);
        return text;
    }
}
