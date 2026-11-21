using Ravensguard.Sim.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA1 gate EA-16: <c>econ-audit</c> prints every row of economy-audit section 4 with a result.</summary>
public class EconAuditTests(ITestOutputHelper output)
{
    private static readonly string[] SpecRows =
    [
        "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8", "X9", "X10", "X11", "X12", "X13", "X14",
        "W1", "W2", "W3", "W4", "W5", "W6", "W7", "B1", "B2", "B3", "B4", "B5", "B6", "L1", "L2", "L3", "L4", "L5", "L6",
        "G1", "G2", "G3", "G4", "G5", "I1", "I2", "I3", "I4", "S-1", "S-2", "S-3", "S-4", "S-5", "S-6", "S-7", "S-8",
    ];

    [Fact]
    public void EA16_EveryRowOfSection4_IsPrintedWithAResult()
    {
        Assert.Equal(SpecRows, EconAudit.Ids);
        using var run = AuditWorld.Create(links: false);
        var rows = EconAudit.Run(run.Economy);
        Assert.Equal(SpecRows, rows.Select(r => r.Id).ToArray());
        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Result), $"{r.Id} has no result");
            Assert.False(string.IsNullOrWhiteSpace(r.Criterion)); Assert.False(string.IsNullOrWhiteSpace(r.Title));
            if (r.State == AuditState.Pending) Assert.Contains("not measured by the harness", r.Result);       // a pending row names the pass that closes it
        });
        var text = EconAudit.Format(rows);
        foreach (var id in SpecRows) Assert.Contains($"\n{id,-4} ", "\n" + text);
        output.WriteLine(text);
        // the suspects the harness measures itself agree with the spec's reading of Phase 6
        Assert.Equal(AuditState.Fail, rows.Single(r => r.Id == "S-1").State); Assert.Contains("woodcutting 8.", rows.Single(r => r.Id == "S-1").Result);
        Assert.Equal(AuditState.Fail, rows.Single(r => r.Id == "S-3").State);
        Assert.Equal(AuditState.Pass, rows.Single(r => r.Id == "L2").State);
        Assert.Equal(AuditState.Pass, rows.Single(r => r.Id == "L5").State);
    }

    [Fact]
    public void Audit_IsADeterministicReadOfTheWorld()
    {
        using var a = AuditWorld.Create(links: false); using var b = AuditWorld.Create(links: false);
        Assert.Equal(EconAudit.Format(EconAudit.Run(a.Economy)), EconAudit.Format(EconAudit.Run(b.Economy)));
    }
}
