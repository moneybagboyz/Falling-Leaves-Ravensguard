namespace Ravensguard.Sim.Debug;

/// <summary>One labelled value in an inspector section.</summary>
public readonly record struct InspectRow(string Label, string Value);

/// <summary>A titled group of rows shown (collapsibly) by the sim inspector.</summary>
public sealed record InspectSection(string Title, IReadOnlyList<InspectRow> Rows)
{
    public static InspectSection Of(string title, params (string label, string value)[] rows) =>
        new(title, rows.Select(r => new InspectRow(r.label, r.value)).ToList());
}

/// <summary>Anything the dev inspector can display. Read-only; must not mutate sim state.</summary>
public interface IInspectable
{
    string InspectTitle { get; }
    IEnumerable<InspectSection> Inspect();
}
