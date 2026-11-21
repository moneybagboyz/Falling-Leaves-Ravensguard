using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Save;
using Xunit;

namespace Ravensguard.Sim.Tests.Combat;

/// <summary>A book adopted from a fight or an abstract battle rolls with the ledger's seed and its person's key, so a saved and loaded world (which keeps only those) rolls as the unsaved one (found by the P7-EA1 save test: a quarrel's wounds infected in the straight run and not in the loaded one).</summary>
public class WoundAdoptTests
{
    [Fact]
    public void Adopt_GivesTheBookTheLedgersSeedAndItsKey_AndTheSaveRoundTripKeepsThem()
    {
        var l = new WoundLedger(1234);
        var made = new WoundBook(987654321UL, 55UL, 5000, 10, "human");          // made by a battle with its own seed and key
        l.Adopt(77UL, made, 0);
        Assert.Equal(1234UL, made.Seed); Assert.Equal(77UL, made.Key);
        var back = SaveCodec.Deserialize<WoundSave>(SaveCodec.Serialize(WoundSave.Capture(l))).Restore();
        var b = back.Books[77UL];
        Assert.Equal(made.Seed, b.Seed); Assert.Equal(made.Key, b.Key);
    }
}
