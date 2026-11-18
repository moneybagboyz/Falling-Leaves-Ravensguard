using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

public enum LogCode : byte { Start, Stop, Directive, Action, Interrupt, Violation, Summary, Job, Meal, Note }

/// <summary>One line of the autopilot log (spec 4.5, 9): what happened, when, to whom. Text is generated from the facts, never composed by a model.</summary>
public readonly record struct LogLine(long Sec, EntityId Person, LogCode Code, byte Arg, string Text);

/// <summary>
/// The autopilot's ring buffer (at most <see cref="Cap"/> lines kept, about 100 KB in the save) and its counters. <see cref="Violations"/> counts the commands the barrier rejected (AP-12: zero from planning errors);
/// <see cref="Crimes"/> the crime-tagged actions that were carried out (zero with <c>NoCrimes</c>).
/// </summary>
public sealed class AutopilotLog
{
    public int Cap { get; set; } = 2000;
    private readonly List<LogLine> _lines = [];
    public IReadOnlyList<LogLine> Lines => _lines;
    public long Violations { get; set; }
    public long PlanViolations { get; set; }
    public long Crimes { get; set; }
    public long Added { get; private set; }

    public void Add(long sec, EntityId person, LogCode code, string text, byte arg = 0)
    {
        _lines.Add(new LogLine(sec, person, code, arg, text)); Added++;
        if (_lines.Count > Cap) _lines.RemoveRange(0, _lines.Count - Cap);
    }

    public int Count(LogCode code) { int n = 0; foreach (var l in _lines) if (l.Code == code) n++; return n; }
    public int CountInterrupts(InterruptKind k) { int n = 0; foreach (var l in _lines) if (l.Code == LogCode.Interrupt && l.Arg == (byte)k) n++; return n; }

    public void Restore(IEnumerable<LogLine> lines, long violations, long planViolations, long crimes, long added)
    {
        _lines.Clear(); _lines.AddRange(lines); Violations = violations; PlanViolations = planViolations; Crimes = crimes; Added = added;
    }
}
