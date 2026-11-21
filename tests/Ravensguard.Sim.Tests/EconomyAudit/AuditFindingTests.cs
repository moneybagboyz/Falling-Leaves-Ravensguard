using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Economy;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>
/// P7-EA2: the regression tests of the findings in docs/reviews/phase-7-audit.md that are not in <see cref="AuditProbeTests"/> (EA_F1 and EA_F2 are there beside their measures). A test that states a pass criterion the audited tag does not meet carries
/// <c>Skip</c> naming the part that owns the fix; the part removes the Skip when its fix lands (economy-audit 6: each fix has an <c>EA_F&lt;n&gt;</c> test that fails before it).
/// </summary>
public class AuditFindingTests(ITestOutputHelper output)
{
    private const string Owner = "owned by P7-EA3a (the audit's fixes); remove Skip when the fix lands";

    /// <summary>EA_F5 (X2, Low): the border desk pays the book's reference less 5 to 10%, whatever the book's own price is; at the glut threshold where it acts the book's ask is 0.65 of the reference, so the desk bids above it.</summary>
    [Fact(Skip = "EA_F5 accepted by the user as a known limit (Low, sign-off 21 Nov 2026); switch on if the desk is ever repriced")]
    public void EA_F5_DeskExportBid_IsNeverAboveTheBooksAsk()
    {
        var (_, glut, staple) = AuditMeasures.DeskOverAsk(EconFixture.Content.All<Ravensguard.Content.MoneySettingsDef>().Single());
        Assert.True(glut < 1.0 && staple < 1.0, $"desk bid over book ask at the glut threshold: {glut:0.00} (staple {staple:0.00})");
    }

    /// <summary>EA_F6 (Medium, with F-1): rope is priced at 2,000 mites a kg, four times bar iron and ten times the linen it is made of per kg of labour; the R1 repricing sets it from its labour.</summary>
    [Fact]
    public void EA_F6_Rope_IsNotDearerThanBarIron()
    {
        var cat = EconFixture.Catalog();
        Assert.True(cat.Def(cat.IdOf("rope")).BasePriceMitesPerKg <= cat.Def(cat.IdOf("iron_bar")).BasePriceMitesPerKg, "rope against bar iron");
    }

    /// <summary>EA_F7 (Medium, fixed here): the realm's treasury is empty at the muster (4,277 mites on seed 1234 at the Y6 muster), so a host of 2,000 starved from its first day; the soak's decision is <see cref="WarFunding"/>: the realm raises what the campaign needs from the fiefs' aid and a tallage, every step a ledger transfer.</summary>
    [Fact]
    public void EA_F7_TheRealmRaisesTheCoinOfACampaign_ByAidAndTallage_CoinConservedAndDeterministic()
    {
        (long Aid, long Tallage, long Held, bool Ok) One()
        {
            using var run = AuditWorld.Create(links: false);
            var led = run.Econ.Ledger; var realm = run.Economy.Taxes.Treasuries.All.First(t => t.Kind == Ravensguard.Sim.Taxes.TreasuryKind.Realm).Purse;
            long before = led.Balance(realm).Mites; Assert.True(before < 10_000, "the realm treasury holds next to nothing at the tag");
            var (aid, tallage) = WarFunding.Raise(run.Economy, realm, 11_700_000);                 // thirty days of a host of 2,000
            return (aid, tallage, led.Balance(realm).Mites, run.CoinConserved);
        }
        var a = One(); var b = One();
        Assert.Equal(a, b);
        Assert.True(a.Ok);
        Assert.InRange(a.Held, 11_600_000, 11_800_000);                                             // what it asked for (it counts what the treasury already holds)
        Assert.True(a.Tallage > 0 && a.Aid >= 0);
        output.WriteLine($"raised {a.Aid:N0} in aid and {a.Tallage:N0} in tallage = {(double)(a.Aid + a.Tallage) / 128_340_000:P1} of the region's coin; treasury now {a.Held:N0}");
    }

    /// <summary>EA_F9 (W4 / WL-6, Medium): 1,040 levies out through the Y8 harvest cost the harvest nothing (-0.2% against the control, the spec asks for 10-35%). Needs the control and the war: a Soak test.</summary>
    [Fact(Skip = "EA_F9 owned by P7-AE2 / P7-EA3b (levy quotas and the harvest's sensitivity to the labour a levy takes); remove Skip when the levy bites"), Trait("Category", "Soak")]
    public void EA_F9_LeviesOutThroughTheHarvest_CutTheHarvest10To35Percent()
    {
        using var war = AuditWorld.Create(); using var ctl = AuditWorld.Create();
        var w = new WarLoadSoak(war, 8); w.Run(); var c = new WarLoadSoak(ctl, 8, control: true); c.Run();
        double drop = 1 - (double)w.Rows[7].HarvestT / c.Rows[7].HarvestT;
        Assert.InRange(drop, 0.10, 0.35);
    }
}
