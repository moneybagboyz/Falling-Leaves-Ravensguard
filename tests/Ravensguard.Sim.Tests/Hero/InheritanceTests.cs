using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Hero;

/// <summary>
/// P5-H inheritance (docs/specs/phase5/inheritance.md, gate IN-2 to IN-13): one rule settles every estate (<see cref="EstateWorld"/>, <see cref="EstateSettler"/>): the hero's at his death in play
/// and every person's who dies holding something; wills (<see cref="WillDesk"/>), the law (primogeniture, partible), escheat, debts and the creditors' claims at the season's start, a minor heir's
/// guardian, NPC business succession by the same law with the trade check, the save, the plague budget and the reserved streams. Worlds are real new games (seed 1234 or 7) whose people are
/// arranged where a case needs kin of given ages.
/// </summary>
public class InheritanceTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;

    internal static WorldState Game(ulong seed, string origin, bool female = false)
    {
        var w = SeamWorld.Get(seed);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var hero = HeroRecord.FromOrigin(female ? "Maud Tanner" : "Aldric Vane", female, Db.Get<OriginDef>(origin));
        var st = NewGameWorld.Create(Db, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null);
        st.Content = Db;
        return st;
    }

    internal sealed class Rig
    {
        public required WorldState St;
        public required HeroContext Ctx;
        public readonly HeroDesk Desk = new();
        public readonly HashSet<int> Used = [];
        public PersonTable Pt => St.Population.People;
        public HouseholdTable Hh => St.Population.Households;
        public ProductionSystem Prod => St.Production!;
        public Ledger Led => St.Economy!.Ledger;
        public LandWorld Land => LandWiring.For(St, Db);
        public OwnershipWorld Own => OwnershipWiring.For(St, Db);
        public EstateWorld Est => EstateWiring.For(St, Db);
        public int HeroRow => St.Hero!.PersonIndex;
        public int Town => Pt.HomeSettlement[HeroRow];
        public long Sec;
        public int Day => (int)(Sec / 86400);

        public static Rig Of(WorldState st) => new() { St = st, Ctx = new HeroContext(st, st.Party!, Db), Sec = st.Clock.Now.Seconds };

        /// <summary>An alive commoner of the place (no business, no post, not gentry), made the sex and age asked (deterministic: lowest row first).</summary>
        public int Person(Sex sex, int age, int settlement = -1, bool idle = true, Func<int, bool>? also = null)
        {
            settlement = settlement < 0 ? Town : settlement;
            for (int i = 0; i < Pt.Count; i++)
            {
                if (also != null && !also(i)) continue;
                if (Used.Contains(i) || Pt.Status[i] != PersonStatus.Alive || Pt.Household[i] < 0 || Pt.HomeSettlement[i] != settlement) continue;
                if (Hh.Class[Pt.Household[i]] == SocialClass.Gentry || Prod.Businesses.Any(b => b.Owner == i || b.Staff.Contains(i))) continue;
                if (idle && Pt.Occupation[i] is not (Occupation.None or Occupation.Labourer)) continue;
                if (St.Succession?.Kin.Any(k => k.Person == Pt.IdOf(i)) == true) continue;
                Used.Add(i);
                Pt.Sex[i] = sex; Pt.BirthDay[i] = Day - age * TimeConst.DaysPerYear - 5;
                return i;
            }
            throw new InvalidOperationException("no person left to arrange");
        }

        public void SetCoin(PurseId p, long mites)
        {
            long b = Led.Balance(p).Mites; var sink = Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 0x7E57_0001UL));
            if (b > mites) Led.Transfer(p, sink, new Coin(b - mites), TransferReason.Gift); else if (b < mites) Led.Mint(p, new Coin(mites - b), SourceReason.Seed);
        }

        public Business Shop(string kind, int settlement, int owner)
        {
            var k = Prod.Content.Kind(kind);
            var b = Prod.NewBusiness(k, settlement, -1, owner); b.OwnerWorks = owner != HeroRow;
            foreach (var (s, n) in k.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Station { Kind = s });
            if (owner == HeroRow) Own.Hold(b).FoundedDay = Day;
            return b;
        }

        public Deed Lot(int settlement, int lot, EntityId holder, PurseKind kind)
        {
            var r = Land.ConveyLot(settlement, lot, holder, kind, Day, DeedCause.Bought);
            Assert.True(r.Ok);
            return St.Land!.Deeds[r.Deed];
        }

        /// <summary>The hero dies where his home place is; <paramref name="heir"/> continues (none: the line ends).</summary>
        public HeroResult Die(EntityId heir)
        {
            var s = St.Population.Region.Settlements[Town];
            var cmd = Desk.Issue(heir == EntityId.None ? SuccessionDesk.EndCommand(s.CenterX * 1000L, s.CenterY * 1000L, Sec) : SuccessionDesk.HeirCommand(heir, s.CenterX * 1000L, s.CenterY * 1000L, Sec));
            Desk.ApplyDue(Ctx, cmd.Hour);
            return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }

        public HeroResult Do(HeroCommand c) { var cmd = Desk.Issue(c); Desk.ApplyDue(Ctx, cmd.Hour); return Desk.View!.Results.Last(r => r.Seq == cmd.Seq); }

        public long TotalCoin => Led.Audit().InPurses.Mites;
        public int Witness()
        {
            for (int i = 0; i < Pt.Count; i++) if (Pt.Status[i] == PersonStatus.Alive && Pt.Occupation[i] is Occupation.Priest or Occupation.Reeve && Pt.Household[i] >= 0) return i;
            throw new InvalidOperationException("no priest or reeve");
        }
    }

    /// <summary>The culture byte of a law: the name pool of a base culture with that law.</summary>
    private static byte CultureOf(InheritanceRule law)
    {
        var pools = Db.All<NamePoolDef>().Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        var cu = Db.All<CultureDef>().OrderBy(c => c.Id, StringComparer.Ordinal).First(c => (c.Inheritance == InheritanceRule.Partible) == (law == InheritanceRule.Partible));
        return (byte)pools.IndexOf(cu.NamePool);
    }

    // ------------------------------------------------------------------ IN-2, IN-3: an NPC smith's estate by the law, and by a will

    /// <summary>A smith of the region (an NPC owner of a smithy whose household holds its dwelling) with a wife, sons of 24 and 19 and a daughter of 21 in his household.</summary>
    private static (Rig R, int Smith, int Wife, int Son24, int Son19, int Daughter, Business Smithy, int Hh, Deed House) Smith()
    {
        var r = Rig.Of(Game(1234, "disgraced_squire"));
        var reg = r.Land.Registry();
        var smithy = r.Prod.Businesses.Where(b => b.State == BusinessState.Open && b.Owner >= 0 && r.Prod.KindOf(b).Id == "smithy" && r.Pt.Status[b.Owner] == PersonStatus.Alive
            && r.Pt.Household[b.Owner] is int h && h >= 0 && r.Hh.Head[h] == b.Owner && reg.DwellingOf(h) != null).OrderBy(b => b.Index).First();
        int smith = smithy.Owner, hh = r.Pt.Household[smith], s = r.Hh.Settlement[hh];
        r.Used.Add(smith);
        foreach (int m in r.Hh.Members(hh).ToList()) if (m != smith) r.Used.Add(m);
        int wife = r.Person(Sex.Female, 46, s), son24 = r.Person(Sex.Male, 24, s), son19 = r.Person(Sex.Male, 19, s), daughter = r.Person(Sex.Female, 21, s);
        var sim = r.St.HomeRecords!.Sim;
        foreach (int k in new[] { wife, son24, son19, daughter }) if (r.Pt.Household[k] != hh) Demography.MovePerson(sim, k, r.Pt.Household[k], hh);
        r.Pt.Sex[smith] = Sex.Male; r.Pt.BirthDay[smith] = r.Day - 50 * TimeConst.DaysPerYear;
        r.Pt.Spouse[smith] = wife; r.Pt.Spouse[wife] = smith;
        foreach (int c in new[] { son24, son19, daughter }) { r.Pt.Father[c] = smith; r.Pt.Mother[c] = wife; r.Pt.Occupation[c] = Occupation.None; }
        foreach (int k in new[] { smith, wife, son24, son19, daughter }) r.Pt.Culture[k] = CultureOf(InheritanceRule.Primogeniture);
        // other members of the household that the head's death would pick are made minors (only the arranged family is adult)
        var family = new HashSet<int> { smith, wife, son24, son19, daughter };
        foreach (int m in r.Hh.Members(hh).ToList()) if (!family.Contains(m)) r.Pt.BirthDay[m] = r.Day - 8 * TimeConst.DaysPerYear;
        r.St.Population.ResetKin();
        var house = reg.DwellingOf(hh)!;
        return (r, smith, wife, son24, son19, daughter, smithy, hh, house);
    }

    internal static void Kill(Rig r, int p)
    {
        var sim = r.St.HomeRecords!.Sim;
        var ob = new Ravensguard.Sim.Abstract.RegionOutbox(r.St.Ids);
        Demography.Kill(sim, p, DeathCause.Age, new GameTime(r.Sec), ob);
    }

    [Fact]
    public void IN2_Primogeniture_TheDeadSmithsSmithyHouseAndShares_GoToTheSonOf24_TheOthersGetNothing()
    {
        var (r, smith, wife, son24, son19, daughter, smithy, hh, house) = Smith();
        // a share the smith held in his own name in another business (a person-held share row)
        var other = r.Prod.Businesses.First(b => b.State == BusinessState.Open && b.Owner >= 0 && b.Owner != smith && r.Pt.Status[b.Owner] == PersonStatus.Alive && r.Pt.Household[b.Owner] != hh);
        r.Own.Hold(other).Shares.Add(new ShareRow(r.Pt.IdOf(smith), 150));
        long total = r.TotalCoin;
        Kill(r, smith);
        r.St.HomeRecords!.Economy.Labour.Daily(new GameTime(r.Sec));                        // the labour market's succession: the law's order
        int settled = r.Est.Daily(r.Day, r.Sec);                                             // then the estates of the day's dead
        Assert.Equal(1, settled);
        Assert.Equal(son24, smithy.Owner);                                                   // the smithy: the eldest son of 16 and over
        Assert.Equal(son24, r.Hh.Head[hh]);                                                  // the house deed's holder is his household now headed by him
        Assert.Equal(r.Hh.IdOf(hh), r.Land.Registry().DwellingOf(hh)!.Holder);
        Assert.Equal(house.Id, r.Land.Registry().DwellingOf(hh)!.Id);
        var share = Assert.Single(r.Own.Book.Of(other.Id)!.Shares);
        Assert.Equal(r.Hh.IdOf(r.Pt.Household[son24]), share.Holder); Assert.Equal(150, share.Pm);   // the shares: to the son of 24 (his household)
        Assert.All(new[] { wife, son19, daughter }, k => Assert.DoesNotContain(r.Prod.Businesses, b => b.Owner == k));
        var est = r.St.Succession!.Estates.Last();
        Assert.Equal(r.Pt.IdOf(smith), est.Dead); Assert.Equal(r.Pt.IdOf(son24), est.Heir);
        Assert.Equal(EstateThing.Share, Assert.Single(est.Holdings).Kind);
        Assert.Equal(total, r.TotalCoin);
        // the law's order itself (sons by age, daughters, the spouse; nobody else)
        var heirs = EstateSettler.Heirs(r.Pt, smith, r.Day);
        Assert.Equal(new[] { son24, son19, daughter, wife }, heirs.Take(4).Select(h => h.Row).ToArray());
        Assert.Equal(new[] { PropertyRule.Son, PropertyRule.Son, PropertyRule.Daughter, PropertyRule.Spouse }, heirs.Take(4).Select(h => h.Rule).ToArray());
    }

    [Fact]
    public void IN3_AWill_LeavesTheHouseToTheWife_AndTheRestFollowsTheLawToTheSonOf24()
    {
        var (r, smith, wife, son24, _, _, smithy, hh, house) = Smith();
        var book = r.St.Wills ??= new WillBook();
        int witness = r.Witness();
        var (ok, key) = book.Make(r.Led, r.Pt.IdOf(smith), r.St.Economy!.HouseholdPurse[hh], r.St.Economy.HouseholdPurse[r.Pt.Household[witness]], r.Pt.IdOf(witness),
            [new Bequest(r.Pt.IdOf(wife), BequestWhat.Deed, house.Id)], r.Day);
        Assert.True(ok, key);
        Kill(r, smith);
        r.St.HomeRecords!.Economy.Labour.Daily(new GameTime(r.Sec));
        Assert.Equal(1, r.Est.Daily(r.Day, r.Sec));
        Assert.Equal(wife, r.Hh.Head[hh]);                                                   // the house: to the wife, as the will says
        Assert.Equal(house.Id, r.Land.Registry().DwellingOf(hh)!.Id);
        Assert.Equal(son24, smithy.Owner);                                                   // the rest: the law
        Assert.Null(r.St.Wills!.Of(r.Pt.IdOf(smith)));                                       // a will is struck out once its estate is settled
        var est = r.St.Succession!.Estates.Last();
        Assert.Contains(est.Holdings, h => h.Kind == EstateThing.Deed && h.Id == house.Id && h.ToRow == wife);
    }

    // ------------------------------------------------------------------ IN-4 to IN-8: the hero's death

    [Fact]
    public void IN4_TheHeroDies_HisSonOf17Continues_FuneralWagesAndCoin_DeedsBusinessAndSiteToTheSon_Conserved()
    {
        var r = Rig.Of(Game(1234, "disgraced_squire"));
        int son = r.Person(Sex.Male, 17);
        r.Pt.Father[son] = r.HeroRow; r.Pt.Culture[r.HeroRow] = CultureOf(InheritanceRule.Primogeniture); r.St.Population.ResetKin();
        r.St.Succession!.Kin.Add(new HeroKinLink(r.Pt.IdOf(son), KinRelation.Child));
        var d1 = r.Lot(r.Town, 0, r.Ctx.Party.Hero, PurseKind.Person); var d2 = r.Lot(r.Town, 1, r.Ctx.Party.Hero, PurseKind.Person);
        var shop = r.Shop("smithy", r.Town, r.HeroRow);
        int worker = r.Person(Sex.Male, 30);
        int workerHh = r.Pt.Household[worker];
        r.Prod.Employ(shop, worker, Occupation.Smith);
        shop.Owed[shop.Staff.IndexOf(worker)] = 400;
        // a half-built site of his (P5-B): it passes paused, for the heir to resume (OV-5)
        var cb = r.St.Construction ??= new Ravensguard.Sim.Construction.ConstructionBook();
        var site = new Ravensguard.Sim.Construction.ConstructionSite { Id = EntityId.Make(IdKind.Other, 0x5173_0001UL), Owner = r.Ctx.Party.Hero, Payer = r.Ctx.Party.Hero, State = Ravensguard.Sim.Construction.SiteState.Active };
        cb.Sites[site.Id] = site;
        r.SetCoin(r.Ctx.Purse, 3000);
        long total = r.TotalCoin; int deeds = r.St.Land!.Deeds.Count;
        long workerBefore = r.Led.Balance(r.St.Economy!.HouseholdPurse[workerHh]).Mites;
        var res = r.Die(r.Pt.IdOf(son));
        Assert.True(res.Ok, res.Text);
        var est = r.St.Succession.Estates.Last();
        Assert.Equal(150, est.FuneralMites);                                                 // the funeral: 5% of 3,000
        var wage = Assert.Single(est.Debts);
        Assert.Equal(DebtKind.Wage, wage.Kind); Assert.Equal(400, wage.OwedMites); Assert.Equal(400, wage.PaidMites);
        Assert.Equal(workerBefore + 400, r.Led.Balance(r.St.Economy.HouseholdPurse[workerHh]).Mites);
        Assert.Equal(0, shop.Owed[shop.Staff.IndexOf(worker)]);
        Assert.Equal(2450, r.Led.Balance(r.Ctx.Purse).Mites);                                // 3,000 - 150 - 400 to the son, who takes up the purse
        // play switches to the son, who holds the deeds (the hero's id), the business and the site
        Assert.Equal(son, r.St.Hero!.PersonIndex);
        Assert.Equal(PersonStatus.Hero, r.Pt.Status[son]);
        foreach (var d in new[] { d1, d2 }) { Assert.Equal(r.Ctx.Party.Hero, d.Holder); Assert.Equal(DeedCause.Inherited, d.Cause); }
        Assert.Equal(2, r.St.Succession.Records.Last().Deeds.Count);
        Assert.Equal(son, shop.Owner);
        Assert.True(r.Own.IsHeroOwner(shop));
        Assert.True(r.Own.Book.Of(shop.Id)!.InheritedDay >= 0);
        Assert.Equal(Ravensguard.Sim.Construction.PauseReason.Owner, site.Pause);
        Assert.Equal(r.Ctx.Party.Hero, site.Owner);
        Assert.Equal("disgraced_squire", r.St.Hero.Origin == "heir" ? "disgraced_squire" : "");
        Assert.Equal("regain_honour", r.St.Hero.AmbitionId);                                 // kin: the family's goal goes on
        Assert.Equal(total, r.TotalCoin); Assert.True(r.Led.Audit().IsConserved);
        Assert.Equal(deeds, r.St.Land.Deeds.Count);
        Assert.Contains(r.St.Chronicle.All, e => e.TypeId == "estate_settled");
        // the Ledger marks them inherited and shows the estate
        var lv = SuccessionDesk.WithEstate(r.St, r.Own.Ledger(r.Day));
        Assert.Contains(lv.Tab(LedgerTab.Property), x => x.Id == d1.Id && x.Inherited);
        Assert.Contains(lv.Tab(LedgerTab.Businesses), x => x.Id == shop.Id && x.Inherited);
        Assert.Equal(400, lv.EstateDebts); Assert.Equal(0, lv.EstateUnpaid);
    }

    /// <summary>A hero with no kin (the discharged soldier) and a recruited companion; his two deeds and a business.</summary>
    private static (Rig R, int Companion, Deed D1, Deed D2, Business Shop) Lonely()
    {
        var r = Rig.Of(Game(1234, "discharged_soldier"));
        int c = r.Person(Sex.Male, 28);
        r.St.Party!.Squad.Members.Add(new Follower { Person = r.Pt.IdOf(c), PersonIndex = c, Name = r.Pt.FullName(c), State = HireState.Active, Loyalty = 800, Origin = FollowerOrigin.Recruited, JoinedDay = r.Day });
        var d1 = r.Lot(r.Town, 0, r.Ctx.Party.Hero, PurseKind.Person); var d2 = r.Lot(r.Town, 1, r.Ctx.Party.Hero, PurseKind.Person);
        var shop = r.Shop("bakery", r.Town, r.HeroRow);
        r.SetCoin(r.Ctx.Purse, 2000);
        return (r, c, d1, d2, shop);
    }

    [Fact]
    public void IN5_TheCompanionContinues_NoWillNoKin_HeKeepsTheBodysThingsAndCoin_DeedsAndTheBusinessEscheatToTheLord()
    {
        var (r, c, d1, d2, shop) = Lonely();
        var e = r.St.Economy!;
        var items = r.St.Party!.Carried.Rows(e).Select(x => e.Items.At(x).Id).ToList();
        long total = r.TotalCoin;
        var lord = r.Est.LordAt(r.Town);
        var res = r.Die(r.Pt.IdOf(c));
        Assert.True(res.Ok, res.Text);
        var est = r.St.Succession!.Estates.Last();
        Assert.Equal(2000 - 100, r.Led.Balance(r.Ctx.Purse).Mites);                          // his coin, less the funeral (Phase 4)
        foreach (var id in items) Assert.Equal(SuccessionDesk.EstateOwner(est.Id), e.Items.Get(id).Owner);   // the body's things, where it lies, for him to fetch
        foreach (var d in new[] { d1, d2 }) { Assert.Equal(lord.Holder, d.Holder); Assert.Equal(DeedCause.Escheated, d.Cause); }
        Assert.Equal(lord.Row, shop.Owner);                                                  // the business's ownership: the lord (his row, or none: the labour market finds one)
        Assert.False(r.Own.IsHeroOwner(shop));
        Assert.All(est.Holdings.Where(h => h.Kind is EstateThing.Deed or EstateThing.Business), h => Assert.True(h.Escheat));
        Assert.Equal(c, r.St.Hero!.PersonIndex);
        Assert.Equal(SuccessionDesk.DefaultAmbition, r.St.Hero.AmbitionId);                  // not kin: "find your place"
        Assert.Equal(total, r.TotalCoin);
    }

    [Fact]
    public void IN6_WithAWillNamingTheCompanion_HeGetsEverything()
    {
        var (r, c, d1, d2, shop) = Lonely();
        int w = r.Witness();
        long before = r.Led.Balance(r.Ctx.Purse).Mites;
        var made = r.Do(WillDesk.Make(r.Pt.IdOf(w), [new Bequest(r.Pt.IdOf(c), BequestWhat.All)], r.Sec));
        Assert.True(made.Ok, made.Text);
        Assert.Equal(before - WillBook.FeeMites, r.Led.Balance(r.Ctx.Purse).Mites);           // 2 crowns to the witness
        Assert.NotNull(r.St.Wills!.Of(r.Ctx.Party.Hero));
        // the will panel's view: the companion is offered, the deeds and the business are things to leave, the estate today goes to him
        var view = WillDesk.View(r.St, r.St.Party!, Db, r.Day);
        Assert.Contains(view.People, p => p.Person == r.Pt.IdOf(c) && p.How == "companion");
        Assert.Equal(3, view.Things.Count);
        Assert.All(view.Estate.Lines.Where(l => l.Kind is EstateThing.Deed or EstateThing.Business), l => Assert.True(l.ToHeir));
        var res = r.Die(r.Pt.IdOf(c));
        Assert.True(res.Ok, res.Text);
        foreach (var d in new[] { d1, d2 }) { Assert.Equal(r.Ctx.Party.Hero, d.Holder); Assert.Equal(DeedCause.Inherited, d.Cause); }
        Assert.Equal(c, shop.Owner); Assert.True(r.Own.IsHeroOwner(shop));
        Assert.Null(r.St.Wills?.Of(r.Ctx.Party.Hero));
        Assert.DoesNotContain(r.St.Succession!.Estates.Last().Holdings, h => h.Escheat);
        // a will needs a priest or the reeve
        var bad = r.Do(WillDesk.Make(r.Pt.IdOf(r.Person(Sex.Male, 40)), [new Bequest(r.Pt.IdOf(w), BequestWhat.All)], r.Sec));
        Assert.False(bad.Ok);
    }

    [Fact]
    public void IN7_DebtsAboveTheCoin_CoinThenGoodsAtBid_ThenAtTheSeasonsStartTheStallAndTheSharesAreClaimedBeforeTheShop()
    {
        var (r, c, _, _, shop) = Lonely();
        // a stall rented for the season, a share in a baker's of the town, a little grain in the pack; 2,000 owed to his baker; 500 in the purse
        r.SetCoin(r.Ctx.Purse, 20_000);
        Assert.True(r.Land.LeaseStall(r.Ctx.Party.Hero, PurseKind.Person, r.Town, 0, false, r.Day, (_, _, _, _, _) => default).Ok);
        r.Own.OpenStall(r.Town, 0, shop.Index);
        var npc = r.Prod.Businesses.First(b => b.State == BusinessState.Open && b.Owner >= 0 && b.Owner != r.HeroRow && r.Pt.Status[b.Owner] == PersonStatus.Alive);
        r.Own.Hold(npc).Shares.Add(new ShareRow(r.Ctx.Party.Hero, 100));
        r.Ctx.Pack.Create(r.St.Economy!.Goods.IdOf("wheat"), 2000, Ravensguard.Sim.Goods.CreateReason.Seed);
        int baker = r.Person(Sex.Male, 33);
        r.Prod.Employ(shop, baker, Occupation.Baker);
        shop.Owed[shop.Staff.IndexOf(baker)] = 2000;
        r.SetCoin(r.Ctx.Purse, 500);
        // the will names the companion, so what is claimed later is taken from him
        r.St.Wills = new WillBook(); r.St.Wills.ByTestator[r.Ctx.Party.Hero] = new Will { Testator = r.Ctx.Party.Hero, Day = r.Day }; r.St.Wills.Of(r.Ctx.Party.Hero)!.Bequests.Add(new Bequest(r.Pt.IdOf(c), BequestWhat.All));
        long total = r.TotalCoin;
        var res = r.Die(r.Pt.IdOf(c));
        Assert.True(res.Ok, res.Text);
        var est = r.St.Succession!.Estates.Last();
        var debt = Assert.Single(est.Debts);
        Assert.Equal(2000, debt.OwedMites);
        Assert.True(debt.PaidMites > 475, $"paid {debt.PaidMites}");                          // the coin after the funeral (475), then the grain at bid
        Assert.Equal(0, r.Led.Balance(r.Ctx.Purse).Mites);
        Assert.Equal(0, r.St.Economy.OtherStores.GetValueOrDefault(SuccessionDesk.EstateOwner(est.Id))?.Qty(r.St.Economy.Goods.IdOf("wheat")) ?? 0);   // the grain went to the baker's household
        Assert.True(debt.Left > 0);
        // the season's start: claims in the order stalls, shares, vehicles and animals, businesses, deeds
        int claims = r.Est.Seasonal(r.Day + 1, r.Sec + 86400);
        Assert.True(claims >= 1);
        var order = r.St.Chronicle.All.Where(e => e.TypeId == "estate_claimed").Select(e => e.Data["kind"]).ToList();
        output.WriteLine(string.Join(", ", order) + $"; left {debt.Left}");
        Assert.Equal(nameof(EstateThing.Stall), order[0]);
        int iShop = order.IndexOf(nameof(EstateThing.Business)), iShare = order.IndexOf(nameof(EstateThing.Share));
        if (iShop >= 0) Assert.True(iShare >= 0 && iShare < iShop);
        Assert.Contains(est.Holdings, h => h.Kind == EstateThing.Stall && h.ClaimedBy != EntityId.None);
        if (debt.Left == 0 && iShop < 0) Assert.True(r.Own.IsHeroOwner(shop));                // paid before the shop's turn: the shop stays the heir's
        Assert.Equal(total, r.TotalCoin);
        Assert.True(r.St.Economy.MassAuditHolds());
    }

    [Fact]
    public void IN8_AMinorHeirOf13_TheGuardianRunsTheBusinessAsSteward_AndTheHeirMayNotSellADeedUntil16()
    {
        var r = Rig.Of(Game(1234, "disgraced_squire"));
        int child = r.Person(Sex.Male, 13);
        r.Pt.Father[child] = r.HeroRow; r.St.Population.ResetKin();
        r.St.Succession!.Kin.Add(new HeroKinLink(r.Pt.IdOf(child), KinRelation.Child));
        int g = r.Person(Sex.Male, 35);
        r.St.Party!.Squad.Members.Add(new Follower { Person = r.Pt.IdOf(g), PersonIndex = g, Name = r.Pt.FullName(g), State = HireState.Active, Loyalty = 900, Origin = FollowerOrigin.Hired, JoinedDay = r.Day });
        var deed = r.Lot(r.Town, 0, r.Ctx.Party.Hero, PurseKind.Person);
        var shop = r.Shop("smithy", r.Town, r.HeroRow);
        var order = SuccessionDesk.Candidates(r.St, r.St.Party, r.Day);
        var heir = order.First(x => x.Person == r.Pt.IdOf(child));
        Assert.Equal(SuccessionRule.YoungChild, heir.Rule); Assert.Equal(r.Pt.IdOf(g), heir.Guardian);
        Assert.True(r.Die(heir.Person).Ok);
        var h = r.Own.Book.Of(shop.Id)!;
        Assert.Equal(child, shop.Owner);
        Assert.Equal(g, h.Steward);                                                         // the guardian runs it as steward with the default directives
        Assert.False(h.HandsOn);
        Assert.Equal(-1, h.Orders.Recipe);
        var sale = r.Do(LandDesk.Offer(deed.Id, 50_000, r.Sec));
        Assert.False(sale.Ok); Assert.Equal("in_minor_no_sale", sale.Message.Key);
        Assert.Equal(0, deed.AskMites);
        // at 16 he may
        r.Sec += 3L * TimeConst.DaysPerYear * 86400 + 86400;
        Assert.True(r.Do(LandDesk.Offer(deed.Id, 50_000, r.Sec)).Ok);
    }

    // ------------------------------------------------------------------ IN-9: partible law

    [Fact]
    public void IN9_Partible_ThreeSonsAndDeedsOf900_500_300_100_EldestGets900And100_CoinEquallyWithTheRemainderToTheEldest()
    {
        var deal = EstateSettler.RoundRobin([(new EntityId(1), 900), (new EntityId(2), 500), (new EntityId(3), 300), (new EntityId(4), 100)], 3);
        Assert.Equal(new[] { 0, 1, 2, 0 }, deal);
        Assert.Equal(new long[] { 335, 333, 333 }, EstateSettler.Equally(1001, 3));
        // in the world: a dead father of a partible culture holding four deeds in his own name and coin in his purse; three sons in three households
        var r = Rig.Of(Game(1234, "disgraced_squire"));
        int father = r.Person(Sex.Male, 60);
        var hhs = new HashSet<int> { r.Pt.Household[father] };
        int Son(int age) { int s = r.Person(Sex.Male, age, also: i => !hhs.Contains(r.Pt.Household[i])); hhs.Add(r.Pt.Household[s]); return s; }
        var sons = new[] { Son(34), Son(30), Son(26) };
        foreach (int s in sons) r.Pt.Father[s] = father;
        r.Pt.Culture[father] = CultureOf(InheritanceRule.Partible);
        r.St.Population.ResetKin();
        var id = r.Pt.IdOf(father);
        var deeds = Enumerable.Range(0, 4).Select(l => r.Lot(r.Town, l, id, PurseKind.Person)).ToList();
        var purse = r.Led.Open(PurseKind.Person, id); r.Led.Mint(purse, new Coin(1001), SourceReason.Seed);
        var hhOf = sons.Select(s => r.Pt.Household[s]).ToArray();
        Assert.Equal(3, hhOf.Distinct().Count());
        var before = hhOf.Select(h => r.Led.Balance(r.St.Economy!.HouseholdPurse[h]).Mites).ToArray();
        long total = r.TotalCoin;
        Kill(r, father);
        Assert.Equal(1, r.Est.Daily(r.Day, r.Sec));
        var byValue = deeds.Select(d => (d, v: r.Land.ValueOf(d))).OrderByDescending(x => x.v).ThenBy(x => x.d.Id).Select(x => x.d).ToList();
        var want = new[] { 0, 1, 2, 0 };
        for (int k = 0; k < 4; k++) Assert.Equal(r.Hh.IdOf(hhOf[want[k]]), byValue[k].Holder);
        Assert.All(deeds, d => Assert.Equal(DeedCause.Inherited, d.Cause));
        for (int k = 0; k < 3; k++) Assert.Equal(before[k] + (k == 0 ? 335 : 333), r.Led.Balance(r.St.Economy!.HouseholdPurse[hhOf[k]]).Mites);
        Assert.Equal(0, r.Led.Balance(purse).Mites);
        Assert.Equal(total, r.TotalCoin);
    }

    // ------------------------------------------------------------------ IN-10: NPC continuity, the heir of another trade

    [Fact]
    public void IN10_ASmithDies_HisSonIsNoSmith_TheSonOwnsIt_TheLabourMarketHiresASmith_AndTheDividendsAreHis()
    {
        var (r, smith, _, son24, _, _, smithy, _, _) = Smith();
        r.Pt.Occupation[son24] = Occupation.Weaver;
        r.Led.Mint(smithy.Purse, new Coin(200_000), SourceReason.Seed);
        var lab = r.St.HomeRecords!.Economy.Labour;
        int smiths0 = smithy.Staff.Count(s => r.Pt.Occupation[s] == Occupation.Smith);
        Kill(r, smith);
        lab.Daily(new GameTime(r.Sec));
        Assert.Equal(son24, smithy.Owner);
        Assert.Equal(Occupation.Weaver, r.Pt.Occupation[son24]);                             // he keeps his own trade
        Assert.False(smithy.OwnerWorks);
        Assert.Equal(r.Pt.Household[son24], smithy.OwnerHousehold);                          // the owner's dividends go to his household
        Assert.True(r.Prod.NeedsMaster(smithy));
        lab.Daily(new GameTime(r.Sec + 86400));
        Assert.True(smithy.Staff.Count(s => r.Pt.Occupation[s] == Occupation.Smith) > smiths0, "a smith is hired at the next day's labour market");
    }

    // ------------------------------------------------------------------ IN-11: save mid-succession

    [Fact]
    public void IN11_SavedWithTheSuccessionScreenOpen_TheLoadGivesTheSameHeirAndEstateSummary_AndEstateHashEqual()
    {
        var r = Rig.Of(Game(1234, "disgraced_squire"));
        int son = r.Person(Sex.Male, 20);
        r.Pt.Father[son] = r.HeroRow; r.St.Population.ResetKin();
        r.St.Succession!.Kin.Add(new HeroKinLink(r.Pt.IdOf(son), KinRelation.Child));
        r.Lot(r.Town, 0, r.Ctx.Party.Hero, PurseKind.Person);
        var shop = r.Shop("smithy", r.Town, r.HeroRow);
        int w = r.Witness();
        Assert.True(r.Do(WillDesk.Make(r.Pt.IdOf(w), [new Bequest(r.Pt.IdOf(son), BequestWhat.Coin, default, 500)], r.Sec)).Ok);
        // a settled NPC estate with an unpaid debt on the books too
        int dead = r.Person(Sex.Male, 50);
        var nid = r.Pt.IdOf(dead);
        var stall = r.Land.LeaseStall(nid, PurseKind.Person, r.Town, 1, false, r.Day, (_, _, _, _, _) => default);
        if (stall.Ok) r.St.Land!.Deeds[stall.Deed].OwedMites = 900;
        Kill(r, dead); r.Est.Daily(r.Day, r.Sec);
        int day = r.Day;
        string Text(EstateSummary s) => string.Join("|", s.Lines.Select(l => $"{l.Kind},{l.Id.Value},{l.Name},{l.ValueMites},{l.To},{l.Escheat},{l.ToHeir}")) + $"#{s.CoinMites},{s.FuneralMites},{s.DebtsMites},{s.CoinToHeirMites},{s.Law},{s.Will}";
        var heir0 = SuccessionDesk.Candidates(r.St, r.St.Party!, day).First();
        string a = Text(SuccessionDesk.Estate(r.St, r.St.Party!, Db, heir0, day));
        ulong h0 = r.St.Succession.Hash(r.St.Wills);
        var bytes = SaveCodec.Encode(WorldSave.Capture(r.St, "h", "0.1"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), Db, "h"); back.Content = Db;
        var heir1 = SuccessionDesk.Candidates(back, back.Party!, day).First();
        Assert.Equal(heir0.Person, heir1.Person);
        Assert.Equal(a, Text(SuccessionDesk.Estate(back, back.Party!, Db, heir1, day)));
        Assert.Equal(h0, back.Succession!.Hash(back.Wills));
        Assert.Contains(WillsSave.SectionName, WorldSave.Capture(back, "h", "0.1").Sections.Keys);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, "h", "0.1")));
        output.WriteLine(a);
    }

    // ------------------------------------------------------------------ IN-12: a plague's day

    // ------------------------------------------------------------------ IN-13: no randomness

    [Fact]
    public void IN13_SettlingDrawsNothing_Streams0x457And0x561AreNeverDrawn_AndTheSameDeathGivesTheSameEstate()
    {
        Assert.Equal(0x457u, RngStreams.SuccessionReserved);
        Assert.Equal(0x561u, RngStreams.InheritanceDisputesReserved);
        string root = Ravensguard.Sim.Tests.Dialogue.TalkKit.Root();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "Ravensguard.Sim"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.EndsWith("RngStreams.cs", StringComparison.Ordinal)) continue;
            var src = File.ReadAllText(f);
            Assert.DoesNotContain("InheritanceDisputesReserved", src, StringComparison.Ordinal);
            Assert.DoesNotContain("SuccessionReserved", src, StringComparison.Ordinal);
            Assert.DoesNotContain("0x561,", src, StringComparison.Ordinal); Assert.DoesNotContain("0x561)", src, StringComparison.Ordinal);
        }
        foreach (var name in new[] { "EstateSettler.cs", "EstateWorld.cs", "Wills.cs", "WillDesk.cs", "SuccessionDesk.cs", "Succession.cs" })
        {
            var src = File.ReadAllText(Path.Combine(root, "src", "Ravensguard.Sim", "Hero", name));
            Assert.DoesNotContain("DetRng", src, StringComparison.Ordinal); Assert.DoesNotContain("Rnd(", src, StringComparison.Ordinal); Assert.DoesNotContain("Random", src, StringComparison.Ordinal);
        }
        string Run()
        {
            var (r, c, _, _, _) = Lonely();
            r.Die(r.Pt.IdOf(c));
            return $"{r.St.Succession!.Hash(r.St.Wills):X16} {r.St.Land!.ComputeHash()} {r.St.Ownership!.ComputeHash()}";
        }
        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------------ the tanner's widow (the eighth origin)

    [Fact]
    public void TheTannersWidow_BeginsWithHerHusbandsTannery_OnALeaseWithHisArrears_AndPaysThemAtTheDesk()
    {
        var st = Game(7, "tanners_widow", true);
        var origin = Db.Get<OriginDef>("tanners_widow");
        Assert.Equal(8, Db.All<OriginDef>().Count());
        Assert.Equal("keep_the_tannery", origin.Ambition);
        var setup = Ravensguard.Sim.Onboarding.OnboardingSetup.Create(st, Db, origin, st.Clock.Now.Seconds, 7);
        Assert.NotNull(setup.Book);
        var r = Rig.Of(st);
        var biz = Assert.Single(r.Prod.Businesses, b => b.Owner == r.HeroRow);
        Assert.Equal("tannery", r.Prod.KindOf(biz).Id);
        Assert.True(r.Own.IsHeroOwner(biz));
        Assert.True(r.Own.Book.Of(biz.Id)!.InheritedDay >= 0);
        Assert.True(biz.Store.Qty(st.Economy!.Goods.IdOf("hide_raw")) > 0);
        var lease = Assert.Single(st.Land!.Deeds.Values, d => d.Holder == r.Ctx.Party.Hero);
        Assert.True(lease.IsLease); Assert.Equal(DeedCause.Inherited, lease.Cause); Assert.True(lease.OwedMites > 0);
        Assert.Equal(lease.DuesMitesPerSeason * 2, lease.OwedMites);
        var res = r.Do(LandDesk.PayArrears(lease.Id, r.Sec));
        Assert.True(res.Ok, res.Text);
        Assert.Equal(0, lease.OwedMites); Assert.Equal(0, lease.ArrearsSeasons);
        // her estate today: her children are minors, so with no will and no other kin the tannery and the lease would go to the lord
        var view = WillDesk.View(st, st.Party!, Db, r.Day);
        Assert.Contains(view.People, p => p.How == "child");
        Assert.True(r.Led.Audit().IsConserved);
    }
}

/// <summary>IN-12 (inheritance 9, 4.5): a great plague's day, 1,500 deaths of people holding something, settled within the 30 ms budget; alone in the timing collection (fairly measured).</summary>
[Collection(TimingCollection.Name)]
public class InheritanceBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void IN12_APlagueDay_1500DeathsWithHoldings_SettleWithin30ms_CoinMassAndDeedsExact()
    {
        var r = InheritanceTests.Rig.Of(InheritanceTests.Game(1234, "disgraced_squire"));
        var land = r.Land; var book = r.St.Land!;
        int serial = 1_000_000, next = 0;
        // a day of deaths: each of the next alive people holds a deed in his own name and coin in his purse, and dies
        List<int> Plague(int want)
        {
            var dead = new List<int>();
            for (; next < r.Pt.Count && dead.Count < want; next++)
            {
                int i = next;
                if (r.Pt.Status[i] != PersonStatus.Alive || r.Pt.Household[i] < 0 || r.Pt.HomeSettlement[i] < 0) continue;
                var id = r.Pt.IdOf(i);
                var d = new Deed { Id = LandIds.Serial(land.RegionId, serial++), Kind = HoldingKind.Claim, Subject = serial, Settlement = r.Pt.HomeSettlement[i], Holder = id, HolderPurse = PurseKind.Person, Tenure = Tenure.Freehold,
                    Lord = r.St.Population.Region.Settlements[r.Pt.HomeSettlement[i]].FiefIndex, DuesMitesPerSeason = 10, Cause = DeedCause.Claimed };
                book.Deeds[d.Id] = d;
                r.Led.Mint(r.Led.Open(PurseKind.Person, id), new Coin(100), SourceReason.Seed);
                dead.Add(i);
            }
            Assert.Equal(want, dead.Count);
            foreach (int p in dead) InheritanceTests.Kill(r, p);
            return dead;
        }
        Plague(10);
        Assert.Equal(10, r.Est.Daily(r.Day, r.Sec));                                         // a small day first warms the code
        double best = TestBudget.BestPass(30, 3, () =>
        {
            var dead = Plague(1500);
            long total = r.TotalCoin; int deeds = book.Deeds.Count;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = r.Est.Daily(r.Day, r.Sec);
            sw.Stop();
            output.WriteLine($"{n} estates settled in {sw.Elapsed.TotalMilliseconds:0.0} ms");
            Assert.Equal(1500, n);
            Assert.Equal(total, r.TotalCoin); Assert.True(r.St.Economy!.MassAuditHolds()); Assert.Equal(deeds, book.Deeds.Count);
            Assert.True(r.Led.Audit().IsConserved);
            var gone = dead.Select(p => r.Pt.IdOf(p)).ToHashSet();
            Assert.DoesNotContain(book.Deeds.Values, d => gone.Contains(d.Holder));
            foreach (var id in gone) Assert.Equal(0, r.Led.Balance(r.Led.Open(PurseKind.Person, id)).Mites);
            return sw.Elapsed.TotalMilliseconds;
        });
        Assert.True(best <= TestBudget.Ms(30), $"best {best:0.0} ms");
    }
}
