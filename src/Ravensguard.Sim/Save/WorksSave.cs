using MessagePack;

namespace Ravensguard.Sim.Save;

/// <summary>
/// P7-LB1: the works' state (large-buildings 5), for now the sluices that are shut (slot keys of built sluice pieces). Empty while every sluice is open. P7-LB2 extends it additively
/// (warehouse leases, depot policies, the armoury's issue book).
/// </summary>
public sealed class WorksBook
{
    public SortedSet<long> SluiceShut { get; } = [];
    public bool IsEmpty => SluiceShut.Count == 0;
}

/// <summary>The optional "works" section (a name of <c>Phase7Seeding.SectionNames</c>), written only while the book is not empty, so a world with no shut sluice keeps its bytes.</summary>
[MessagePackObject]
public sealed class WorksSave
{
    public const string SectionName = "works";

    [Key(0)] public long[] SluiceShut { get; set; } = [];

    public static WorksSave Capture(WorksBook b) => new() { SluiceShut = [.. b.SluiceShut] };

    public WorksBook Restore()
    {
        var b = new WorksBook();
        foreach (var k in SluiceShut) b.SluiceShut.Add(k);
        return b;
    }
}
