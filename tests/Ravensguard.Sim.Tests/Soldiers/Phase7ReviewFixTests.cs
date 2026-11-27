using Ravensguard.Content;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Soldiers;

/// <summary>Phase 7 review fixes: P7R-1 (nobody paid twice), P7R-8 (a man with no purse blocks no arrears) and P7R-10 (no ransom for the dead, no self-pay stall).</summary>
public class Phase7ReviewFixTests
{
    private static ContentDatabase Db => EconFixture.Content;
    private static ulong P(int n) => EntityId.Make(IdKind.Person, (ulong)n).Value;
    private sealed class NoFit : IKitFit { public int FitOf(ulong item) => 70_000; public int BodyOf(ulong person) => 70_000; public bool SetFit(ulong item, int cm3) => true; }

    private static SoldierWorld World(EconomyState econ, Func<PersonTable?>? people = null) =>
        new(new SoldierBook(), Db, econ, new WorksBook(), Db.All<LargeBuildingRulesDef>().First(), new NoFit(), people ?? (() => null));

    [Fact]
    public void P7R1_AWarbandsMenAreNotPaidByTheSoldiersTick_ThePlainOnesAreOnce()
    {
        var econ = EconFixture.NewEconomy(); var led = econ.Ledger;
        var st = EconFixture.WorldWith(econ); st.Content = Db;
        st.Soldiers = new SoldierBook { Seeded = true };
        var sw = SoldierWiring.For(st, Db);
        var lordPurse = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 800)); led.Mint(lordPurse, new Coin(1_000_000), SourceReason.Seed);
        var purses = new Dictionary<ulong, PurseId>();
        var wb = new Warband { Id = 1, Lord = 100 };
        for (int i = 1; i <= 20; i++)
        {
            ulong p = P(i); sw.Book.Rows[p] = new SoldierRow { Person = p, Role = "spearman", Lord = 100 };
            purses[p] = led.Open(PurseKind.Person, new EntityId(p));
            if (i <= 10) wb.Members.Add(p);
        }
        purses[100] = lordPurse; sw.PurseHook = p => purses.TryGetValue(p, out var x) ? x : null;
        st.Armies = new ArmyBook(); st.Armies.Warbands[1] = wb;
        var w = new ContractWorld { Chronicle = st.Chronicle, Ids = st.Ids, Ledger = led, Seed = 1, People = st.Population.People, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [EntityId.Make(IdKind.Faction, 1)], Content = ContractContent.From(Db) };
        long wage = WageRoll.WageMites(sw, "spearman"); Assert.True(wage > 0);
        SoldierTick.OnDay(st, w, new ContractBook(), 3);
        for (int i = 1; i <= 10; i++) Assert.Equal(0, led.Balance(purses[P(i)]).Mites);            // the armies' own pay day pays these
        for (int i = 11; i <= 20; i++) Assert.Equal(wage, led.Balance(purses[P(i)]).Mites);        // paid once
        Assert.Equal(1_000_000 - 10 * wage, led.Balance(lordPurse).Mites);
    }

    [Fact]
    public void P7R8_AManWithNoPurse_IsOwedNothing_AndBlocksNobodysArrears()
    {
        var econ = EconFixture.NewEconomy(); var led = econ.Ledger; var sw = World(econ);
        var lord = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 801)); led.Mint(lord, new Coin(1_000_000), SourceReason.Seed);
        ulong homeless = P(1), owed = P(2);
        sw.Book.Rows[homeless] = new SoldierRow { Person = homeless, Role = "spearman", Lord = 0 };
        sw.Book.Rows[owed] = new SoldierRow { Person = owed, Role = "spearman", Lord = 0, ArrearsMites = 200 };
        var op = led.Open(PurseKind.Person, new EntityId(owed)); sw.PurseHook = p => p == owed ? op : null;
        WageRoll.PayDay(sw, 0, lord, led);
        Assert.Equal(0, sw.Book.Of(homeless)!.ArrearsMites);
        Assert.Equal(0, sw.Book.Of(owed)!.ArrearsMites);
    }

    private static (SoldierWorld Sw, PurseId Payer, PurseId Captor, PrisonerRow Row) Pens(PersonStatus status)
    {
        var (pt, hh) = EconFixture.Household(30);
        var econ = EconFixture.SmallEconomy(hh, pt); var led = econ.Ledger; var sw = World(econ, () => pt);
        var payer = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 802)); led.Mint(payer, new Coin(1_000_000), SourceReason.Seed);
        var captor = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 803));
        pt.Status[0] = status;
        var row = new PrisonerRow { Person = pt.IdOf(0).Value, Class = PrisonerClass.Ransom, ValueMites = 5000, DueDay = 0, PayerPurse = payer.Index, CaptorPurse = captor.Index, Captor = 1 };
        sw.Book.Prisoners[row.Person] = row;
        return (sw, payer, captor, row);
    }

    [Fact]
    public void P7R10_ARansomIsNotPaidForADeadMan_TheRowGoes()
    {
        var (sw, payer, captor, _) = Pens(PersonStatus.Dead);
        PrisonerPens.Day(sw, AftermathRules.Default, RuleChron.Of(new Chronicle(), new IdAllocator()), 1, 2);
        Assert.Empty(sw.Book.Prisoners); Assert.Equal(0, sw.Econ.Ledger.Balance(captor).Mites); Assert.Equal(1_000_000, sw.Econ.Ledger.Balance(payer).Mites);
    }

    [Fact]
    public void P7R10_ASelfPayRow_SettlesAndDoesNotStall()
    {
        var (sw, payer, _, row) = Pens(PersonStatus.Alive);
        row.CaptorPurse = payer.Index;
        PrisonerPens.Day(sw, AftermathRules.Default, RuleChron.Of(new Chronicle(), new IdAllocator()), 1, 2);
        Assert.Empty(sw.Book.Prisoners); Assert.Equal(1_000_000, sw.Econ.Ledger.Balance(payer).Mites);
    }
}
