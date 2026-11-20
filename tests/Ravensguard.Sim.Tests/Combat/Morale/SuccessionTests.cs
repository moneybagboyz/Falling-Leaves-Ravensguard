using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Tests.Combat.Morale;

/// <summary>Ironman succession by kinship (morale-capture-succession 4.8; gates SU-1 to SU-5).</summary>
public class SuccessionTests
{
    private const int Now = 60 * 100;                                       // day 6,000: year 100

    /// <summary>A family: the hero (40), a spouse, a child of 17, a child of 14, a sibling of 20, the hero's mother; and two strangers.</summary>
    private sealed class Family
    {
        public PersonTable P = new(16, 1000);
        public int Hero, Spouse, Child17, Child14, Sibling, Mother, Father, Stranger;
        public Family()
        {
            int Add(int age, Sex sex = Sex.Male) { int i = P.Add(); P.BirthDay[i] = Now - age * TimeConst.DaysPerYear - 3; P.Sex[i] = sex; P.Given[i] = $"P{i}"; P.Family[i] = "Vane"; return i; }
            Father = Add(70); Mother = Add(66, Sex.Female);
            Hero = Add(40); Sibling = Add(20, Sex.Female); Spouse = Add(38, Sex.Female);
            Child17 = Add(17, Sex.Female); Child14 = Add(14);
            Stranger = Add(30);
            P.Father[Hero] = Father; P.Mother[Hero] = Mother; P.Father[Sibling] = Father; P.Mother[Sibling] = Mother;
            P.Spouse[Hero] = Spouse; P.Spouse[Spouse] = Hero;
            foreach (int c in new[] { Child17, Child14 }) { P.Father[c] = Hero; P.Mother[c] = Spouse; }
            P.Status[Father] = PersonStatus.Dead;
        }
        public void Kill(int i) => P.Status[i] = PersonStatus.Dead;
    }

    private static readonly SquadMember[] Squad =
    [
        new(EntityId.Make(IdKind.Person, 5001), -1, 600, 10, Recruited: true, "Brand"),
        new(EntityId.Make(IdKind.Person, 5002), -1, 900, 40, Recruited: true, "Ysolde"),
        new(EntityId.Make(IdKind.Person, 5003), -1, 900, 20, Recruited: true, "Odo"),
        new(EntityId.Make(IdKind.Person, 5004), -1, 950, 5, Recruited: false, "Hireling"),
    ];

    [Fact]
    public void SU1_AChildOf17_InheritsBeforeTheChildOf14_TheSpouseAndTheSibling()
    {
        var f = new Family();
        var order = SuccessionOrder.Order(f.P, f.Hero, Now, Squad);
        Assert.Equal(f.Child17, order[0].PersonIndex); Assert.Equal(SuccessionRule.Child, order[0].Rule); Assert.Equal(17, order[0].AgeYears);
        Assert.Equal([SuccessionRule.Child, SuccessionRule.Spouse, SuccessionRule.Sibling, SuccessionRule.YoungChild, SuccessionRule.Parent,
            SuccessionRule.Companion, SuccessionRule.Companion, SuccessionRule.Companion, SuccessionRule.Follower], order.Select(c => c.Rule));
        Assert.Equal([f.Child17, f.Spouse, f.Sibling, f.Child14, f.Mother], order.Take(5).Select(c => c.PersonIndex));
        // the child of 14 comes with the most loyal follower as guardian (the hireling's 950 tops the companions' 900)
        Assert.Equal(Squad[3].Person, order[3].Guardian);
        // companions by loyalty, then the earliest recruited, then id
        Assert.Equal([Squad[2].Person, Squad[1].Person, Squad[0].Person], order.Where(c => c.Rule == SuccessionRule.Companion).Select(c => c.Person));
        Assert.DoesNotContain(order, c => c.PersonIndex == f.Stranger || c.PersonIndex == f.Father);
    }

    [Fact]
    public void SU2_ThenTheSpouse_ThenTheSibling_ThenTheMostLoyalCompanion_ThenNobody()
    {
        var f = new Family();
        f.Kill(f.Child17);
        Assert.Equal(f.Spouse, SuccessionOrder.Heir(f.P, f.Hero, Now, Squad)!.PersonIndex);
        f.Kill(f.Spouse);
        Assert.Equal(f.Sibling, SuccessionOrder.Heir(f.P, f.Hero, Now, Squad)!.PersonIndex);
        f.Kill(f.Sibling);
        Assert.Equal(SuccessionRule.YoungChild, SuccessionOrder.Heir(f.P, f.Hero, Now, Squad)!.Rule);
        f.Kill(f.Child14); f.Kill(f.Mother);
        var heir = SuccessionOrder.Heir(f.P, f.Hero, Now, Squad)!;
        Assert.Equal(SuccessionRule.Companion, heir.Rule); Assert.Equal(Squad[2].Person, heir.Person);
        // a captive or paralysed kinsman is passed over
        var g = new Family();
        Assert.Equal(g.Spouse, SuccessionOrder.Heir(g.P, g.Hero, Now, Squad, (i, _) => i == g.Child17)!.PersonIndex);
        // nobody left: the run ends
        Assert.Null(SuccessionOrder.Heir(f.P, f.Hero, Now, []));
        var book = new SuccessionBook(); var l = new Ledger();
        var dead = l.Open(PurseKind.Person, f.P.IdOf(f.Hero)); l.Mint(dead, Coin.FromMites(100), SourceReason.Seed);
        SuccessionOrder.Settle(book, l, f.P.IdOf(f.Hero), dead, null, dead, dead, [], [], Now);
        Assert.True(book.RunEnded);
        Assert.Equal(100, l.Balance(dead).Mites);                            // the estate waits; nothing is destroyed
    }

    [Fact]
    public void SU3_TheEstate_CoinLessA5PercentFuneralToTheHeir_ItemsStayOnTheBody_RobbedAreGone()
    {
        var f = new Family();
        var l = new Ledger();
        var heroId = f.P.IdOf(f.Hero);
        var dead = l.Open(PurseKind.Person, heroId); l.Mint(dead, Coin.FromMites(1000), SourceReason.Seed);
        var heirP = l.Open(PurseKind.Person, f.P.IdOf(f.Child17));
        var church = l.Open(PurseKind.Settlement, EntityId.Make(IdKind.Settlement, 1));
        var sword = EntityId.Make(IdKind.Item, 1); var boots = EntityId.Make(IdKind.Item, 2); var purseItem = EntityId.Make(IdKind.Item, 3);
        var heir = SuccessionOrder.Heir(f.P, f.Hero, Now, Squad);
        var book = new SuccessionBook();
        var (est, rec) = SuccessionOrder.Settle(book, l, heroId, dead, heir, heirP, church, [sword, boots, purseItem], new HashSet<EntityId> { purseItem }, Now, 500, 600, "arming_sword");
        Assert.Equal(950, l.Balance(heirP).Mites); Assert.Equal(50, l.Balance(church).Mites); Assert.Equal(0, l.Balance(dead).Mites);
        Assert.True(l.Audit().IsConserved);
        Assert.Equal([sword, boots], est.Items); Assert.True(est.Looted);
        Assert.Equal(heir!.Person, est.Heir);
        Assert.NotNull(rec); Assert.Equal(950, rec!.Coin); Assert.Empty(rec.Deeds); Assert.Equal("", rec.ReputationPending); Assert.Equal("arming_sword", rec.RecoverShape);
        Assert.Equal(heir.Person, book.CurrentHero);
        // who stays: kin always, companions with loyalty 500 or more
        var stay = SuccessionOrder.Stay([.. Squad, new(EntityId.Make(IdKind.Person, 5005), -1, 200, 1, true)], new HashSet<EntityId>(), heir.Person);
        Assert.Equal(4, stay.Count);
        // the heir as the hero: attributes and skills from the row, origin "heir"
        f.P.Attr[f.Child17 * PersonTable.AttrCount] = 61; f.P.SetSkill(f.Child17, 0, Skill.Blades, 33);
        var h = SuccessionOrder.HeroFrom(f.P, f.Child17, "P5 Vane");
        Assert.Equal("heir", h.Origin); Assert.Equal(61, h.Attributes[0]); Assert.Equal(33, h.Skill("blades")); Assert.True(h.Female);
    }

    [Fact]
    public void SU4_TheNonIronmanListIsTheOrder_IronmanTakesTheFirst_TheWorldPersistsAfterTheRunEnds()
    {
        var f = new Family();
        var order = SuccessionOrder.Order(f.P, f.Hero, Now, Squad);
        Assert.Equal(order, SuccessionOrder.Options(order, ironman: false));
        Assert.Equal([order[0]], SuccessionOrder.Options(order, ironman: true));
        Assert.Empty(SuccessionOrder.Options([], ironman: true));
        // the run ends with nobody left; the book (and the world around it) still saves and loads unchanged
        var book = new SuccessionBook(); var l = new Ledger();
        var dead = l.Open(PurseKind.Person, f.P.IdOf(f.Hero));
        SuccessionOrder.Settle(book, l, f.P.IdOf(f.Hero), dead, order[0], l.Open(PurseKind.Person, order[0].Person), l.Open(PurseKind.Settlement, EntityId.Make(IdKind.Settlement, 1)), [], [], Now);
        SuccessionOrder.Settle(book, l, order[0].Person, l.Open(PurseKind.Person, order[0].Person), null, dead, dead, [], [], Now + 30);
        Assert.True(book.RunEnded);
        var bytes = SaveCodec.SerializeCompressed(SuccessionSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<SuccessionSave>(bytes).Restore();
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(SuccessionSave.Capture(back)));
        Assert.True(back.RunEnded); Assert.Equal(2, back.Estates.Count); Assert.Single(back.Records);
        Assert.Equal(book.Hash(), back.Hash());
    }

    [Fact]
    public void SU5_SameTablesSameDeath_SameHeirAndEstateHash_AndSuccessionDrawsNothing()
    {
        (EntityId Heir, ulong Hash) Run()
        {
            var f = new Family();
            var l = new Ledger();
            var dead = l.Open(PurseKind.Person, f.P.IdOf(f.Hero)); l.Mint(dead, Coin.FromMites(12_345), SourceReason.Seed);
            var heir = SuccessionOrder.Heir(f.P, f.Hero, Now, Squad)!;
            var book = new SuccessionBook();
            SuccessionOrder.Settle(book, l, f.P.IdOf(f.Hero), dead, heir, l.Open(PurseKind.Person, heir.Person), l.Open(PurseKind.Settlement, EntityId.Make(IdKind.Settlement, 1)),
                [EntityId.Make(IdKind.Item, 4)], [], Now, 10, 20);
            return (heir.Person, book.Hash());
        }
        Assert.Equal(Run(), Run());
        // stream 0x457 is reserved: the succession code makes no random draw at all
        var src = File.ReadAllText(Path.Combine(FindRepo(), "src", "Ravensguard.Sim", "Hero", "Succession.cs"));
        Assert.DoesNotContain("DetRng", src);
        Assert.Equal(0x457u, RngStreams.SuccessionReserved);
    }

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
