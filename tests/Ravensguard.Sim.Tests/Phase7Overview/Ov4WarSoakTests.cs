namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-4 (the war soak) is NOT duplicated here: it is <c>WarSoak/WarSoakTests.cs</c> (AW-15 in CI, AW-16 nightly under the <c>Soak</c> trait). Its consequence gates were moved to Phase 8 by the user. This one test only pins
/// that the two exist, so a rename or deletion of the soak fails the close-out.
/// </summary>
public class Ov4WarSoakTests
{
    [Fact]
    public void OV4_IsTheWarSoak_AW15InCi_AW16Nightly_Exist()
    {
        var t = typeof(Ravensguard.Sim.Tests.WarSoakTests.WarSoakTests);
        Assert.NotNull(t.GetMethod("AW15_WarSoak_CiPart_Seed1234_2Years_CoinMassAndPeopleExact_TheTrailIsTheSameRunToRun"));
        Assert.NotNull(t.GetMethod("AW16_WarSoak_Nightly_TenWorlds_25Years_EveryGateOfSection46"));
    }
}
