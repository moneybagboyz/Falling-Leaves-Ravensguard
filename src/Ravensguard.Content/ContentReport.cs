namespace Ravensguard.Content;

public readonly record struct SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}:{Column}" : File;
}

public enum IssueSeverity { Warning, Error }

public sealed record ContentIssue(IssueSeverity Severity, SourceLocation? Location, string Message)
{
    public override string ToString() =>
        $"{(Location is { } l ? l.ToString() + ": " : "")}{Severity.ToString().ToLowerInvariant()}: {Message}";
}

/// <summary>Collects every problem found while loading instead of throwing on the first.</summary>
public sealed class ContentReport
{
    private readonly List<ContentIssue> _issues = [];
    public IReadOnlyList<ContentIssue> Issues => _issues;
    public IEnumerable<ContentIssue> Errors => _issues.Where(i => i.Severity == IssueSeverity.Error);
    public IEnumerable<ContentIssue> Warnings => _issues.Where(i => i.Severity == IssueSeverity.Warning);
    public bool HasErrors => _issues.Any(i => i.Severity == IssueSeverity.Error);

    public void Error(SourceLocation? loc, string message) => _issues.Add(new(IssueSeverity.Error, loc, message));
    public void Warning(SourceLocation? loc, string message) => _issues.Add(new(IssueSeverity.Warning, loc, message));

    public override string ToString() => string.Join(Environment.NewLine, _issues);
}

public sealed class ContentException(string message) : Exception(message);
