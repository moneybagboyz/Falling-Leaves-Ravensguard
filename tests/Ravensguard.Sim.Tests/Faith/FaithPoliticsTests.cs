using Ravensguard.Content;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Faith;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Trade;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Faith;

/// <summary>
/// Faith politics (P6-E2, docs/specs/phase6/faith-and-church.md 7): FC-6 relic theft, FC-7 heresy spread and the bishop's answer, FC-8 excommunication by request, FC-9 penance, FC-11 superstition, FC-14 determinism and the
/// optional section; plus the believed miracles with their natural explanations (overview 10a), fake relics and witch hunts.
/// </summary>
public class FaithPoliticsTests(ITestOutputHelper output)
{
    private static long SecOf(int day) => day * 86400L + 8 * 3600;

    /// <summary>The world's own day (people's ages are counted from it).</summary>
    private static int Now(FaithRig r) => (int)(r.Runtime.HourDone / 24) + 1;

    private static int AdultsOf(FaithRig r, int settlement, int day) => Enumerable.Range(0, r.People.Count).Count(i => r.People.IsAlive(i) && r.People.HomeSettlement[i] == settlement && r.People.AgeYears(i, day) >= 16);

    private static EntityId Cause(WorldState st, string type, EntityId who, int day)
    {
        var e = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(SecOf(day)), type, [who], [new Cause(CauseKind.Other, null)], new Dictionary<string, string>());
        st.Chronicle.Add(e); return e.Id;
    }

    private static void Fund(FaithRig r, PurseId purse, long mites) => r.State.Economy!.Ledger.Mint(purse, new Coin(mites), SourceReason.Seed);

    /// <summary>Three lords of the hero's faith in the realm with most fiefs (a, b, v), and another realm for a's own rule.</summary>
    private static (EntityId A, EntityId B, EntityId V, FiefRow BFief, FiefRow VFief, int Realm, int OtherRealm) Lords(FaithRig r)
    {
        var rb = r.State.Rule!; int ri = r.HeroFaith().Religion; var pt = r.People;
        int realm = rb.Fiefs.Values.Where(f => f.Realm >= 0).GroupBy(f => f.Realm).OrderByDescending(g => g.Count()).First().Key;
        var mine = rb.Fiefs.Values.Where(f => f.Realm == realm && f.Holder >= 0 && !f.Forfeited && r.Religions.OfCulture(pt.Culture[pt.IndexOf(new EntityId((ulong)f.Holder))]) == ri).OrderBy(f => f.Fief).ToList();
        Assert.True(mine.Count >= 3, "three lords of the faith");
        var c = PoliticsEngine.Context(r.Sim, r.State.Politics!, rb, RuleChron.Silent, 5);
        int other = c.AliveRealms().First(x => x != realm);
        return (new EntityId((ulong)mine[0].Holder), new EntityId((ulong)mine[1].Holder), new EntityId((ulong)mine[2].Holder), mine[1], mine[2], realm, other);
    }

    // ------------------------------------------------------------------ FC-8, FC-9

    [Fact]
    public void FC8_ARulerWhoTheBishopTrustsAsksForARivalsExcommunication_TheBanReleasesHisVassalsAndGrievesHisFiefs_AndIsACasusBelliForTheFaith()
    {
        using var r = new FaithRig();
        var st = r.State; var rb = st.Rule!; var pt = r.People; var led = st.Economy!.Ledger; int ri = r.HeroFaith().Religion;
        var (a, b, v, bFief, vFief, realm, other) = Lords(r);
        rb.Rulers[realm] = (long)b.Value; rb.Rulers[other] = (long)a.Value;                                        // b rules the realm of his fief, a another
        var p = FaithOps.Of(st)!; int day = 40;
        var see = Excommunication.SeeFor(p, b.Value, ri, null)!; Assert.True(see.Bishop >= 0);
        var bishop = new EntityId((ulong)see.Bishop);
        var cause = Cause(st, "oath_broken", b, 38);
        // the bishop thinks little of a: refused; at 60 and with the gift in hand: done
        Fund(r, FaithOps.PurseOf(st, a), 20_000);
        Assert.Equal("fa_excomm_cold", Excommunication.Request(st, r.Content, a, b, cause, day, out _));
        rb.AddOpinion(bishop.Value, a.Value, 60);
        Assert.Equal("fa_excomm_no_cause", Excommunication.Request(st, r.Content, a, b, Cause(st, "wedding", b, 38), day, out _));
        long aBefore = led.Balance(FaithOps.PurseOf(st, a)).Mites, bishopBefore = led.Balance(FaithOps.PurseOf(st, bishop)).Mites;
        var pc = PoliticsEngine.Context(r.Sim, st.Politics!, rb, RuleChron.Silent, day);
        int renege0 = new LordBrain.Inputs(pc, vFief).Get("renege");
        Assert.Equal("", Excommunication.Request(st, r.Content, a, b, cause, day, out var row));
        Assert.NotNull(row);
        Assert.Equal(aBefore - 10_000, led.Balance(FaithOps.PurseOf(st, a)).Mites);                              // 100 crowns to the bishop, by the ledger
        Assert.Equal(bishopBefore + 10_000, led.Balance(FaithOps.PurseOf(st, bishop)).Mites);
        Assert.True(led.Audit().IsConserved);
        Assert.True(st.Standing!.Marks.IsExcommunicated(b, ri));
        Assert.False(ChurchServices.Allows(st.Standing, b, ri, ChurchServices.Service.Wedding));
        Assert.True(ChurchServices.RefusesWedding(st, b, v));                                                    // FC-10: no wedding at the church
        Assert.Equal(-1000, Standing.Of(st.Standing, b, GroupRef.Religion(ri)));
        // the chronicle: the ban names the cause entry
        var ban = st.Chronicle.All.Single(e => e.TypeId == "excommunication_asked");
        Assert.Equal(cause, ban.Causes[0].EventId);
        // his vassals' fealty utility -40; his fiefs +2 grievances a household; a casus belli for a's realm
        var pc2 = PoliticsEngine.Context(r.Sim, st.Politics!, rb, RuleChron.Silent, day);
        Assert.Equal(renege0 + 40, new LordBrain.Inputs(pc2, vFief).Get("renege"));
        var hh = pc2.R.HouseholdsOfFief(bFief.Fief).ToList(); Assert.NotEmpty(hh);
        Assert.All(hh, h => Assert.True(rb.GrievancePoints(pc2.R.Households.IdOf(h).Value, day) >= 2));
        Assert.Contains(st.Politics!.Cbs, cb => cb.Holder == other && cb.Against == realm && cb.Kind == CbKind.Excommunication);
        Assert.True(Opinions.Value(rb, v.Value, b.Value) < 0);                                                  // the "excommunicated" opinion row
        output.WriteLine($"FC-8: renege {renege0} -> {renege0 + 40}, {hh.Count} households grieved, standing {Standing.Of(st.Standing, b, GroupRef.Religion(ri))}");
    }

    [Fact]
    public void FC9_PenanceIsATenthOfTheTreasuryAndAPilgrimage_TheMarkIsClearedAndTheServicesReturn_ANewBishopMayForgiveToo()
    {
        using var r = new FaithRig();
        var st = r.State; var rb = st.Rule!; var led = st.Economy!.Ledger; int ri = r.HeroFaith().Religion;
        var (a, b, v, bFief, _, realm, _) = Lords(r);
        var p = FaithOps.Of(st)!; int day = 50;
        var see = Excommunication.SeeFor(p, b.Value, ri, null)!; var bishop = new EntityId((ulong)see.Bishop);
        var x = Excommunication.Impose(st, r.Content, p, null, b, ri, bishop, null, EntityId.None, day);   // by the bishop himself, on his own judgement
        Assert.True(st.Standing!.Marks.IsExcommunicated(b, ri));
        var treasury = led.Open(PurseKind.Treasury, r.Region.Fiefs[bFief.Fief].Id); Fund(r, treasury, 100_000);
        long due = Excommunication.PenanceDue(st, p, b).Sum(d => d.Mites); Assert.True(due >= 10_000);
        var seePurse = led.Open(PurseKind.Treasury, FaithEngine.SeePurseOwner(see.Index)); long seeBefore = led.Balance(seePurse).Mites;
        Assert.Equal("fa_penance_no_pilgrimage", Excommunication.Penance(st, r.Content, b, false, day + 2));       // the coin alone is not enough
        Assert.True(st.Standing.Marks.IsExcommunicated(b, ri));
        Assert.Equal("", Excommunication.Penance(st, r.Content, b, true, day + 3));
        Assert.False(st.Standing.Marks.IsExcommunicated(b, ri));
        Assert.Equal(seeBefore + due, led.Balance(seePurse).Mites);                                              // a tenth of the treasury to the see
        Assert.True(led.Audit().IsConserved);
        Assert.True(ChurchServices.Allows(st.Standing, b, ri, ChurchServices.Service.Wedding));
        Assert.False(ChurchServices.RefusesWedding(st, b, v));                                                   // services restored
        Assert.False(x.Active);
        // a new bishop's grace: the ban of a dead bishop lapses at a season's check (GracePct), sooner or later
        var p2 = FaithOps.Of(st)!;
        var y = Excommunication.Impose(st, r.Content, p2, null, v, ri, bishop, null, EntityId.None, 60);
        see.Bishop = -1; var heir = p2.Book.Parishes.Values.First(pa => pa.Priest >= 0 && pa.Priest != (long)bishop.Value); see.Bishop = heir.Priest;
        r.People.Status[r.People.IndexOf(bishop)] = PersonStatus.Dead;
        for (int s = 1; s <= 60 && y.Active; s++) Excommunication.Season(st, r.Content, 60 + s * TimeConst.DaysPerSeason);
        Assert.False(y.Active); Assert.Contains(st.Chronicle.All, e => e.TypeId == "grace_granted");
    }

    [Fact]
    public void TheHeroCanBeBanned_ThePenanceCommandWantsAPilgrimageFirst_AndTheBurialIsRefused()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var (hs, ri) = r.HeroFaith(); var church = r.Church;
        var p = FaithOps.Of(st)!; var see = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, hs)) ?? church.Sees.Values.First();
        Excommunication.Impose(st, r.Content, p, null, hero, ri, new EntityId((ulong)see.Bishop), null, EntityId.None, 20);
        Assert.False(ChurchServices.Allows(st.Standing, hero, ri, ChurchServices.Service.Burial));
        Assert.True(FaithWiring.OnBurial(st, hero, hs, 21));                                                      // the chronicle says the church refused
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "burial_refused");
        Fund(r, FaithOps.PurseOf(st, hero), 50_000);
        long sec = SecOf(22);
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Penance(sec), sec).Code);                              // no pilgrimage yet
        int site = r.Religions.HolySiteIn(r.Region, ri);
        Assert.True(r.Do(FaithDesk.Vow(hs, site, sec + 1), sec + 1).Ok);
        Assert.True(r.Do(FaithDesk.Arrive(site, sec + 100), sec + 100).Ok);
        var done = r.Do(FaithDesk.Penance(sec + 200), sec + 200); Assert.True(done.Ok, done.Text);
        Assert.False(st.Standing!.Marks.IsExcommunicated(hero, ri));
        Assert.True(st.Economy!.Ledger.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ FC-6

    [Fact]
    public void FC6_TakingARelicFromAnAltarWhileSeenIsSacrilege_PietyAndStandingFall_AndUnseenItIsOnlyMissing()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var (hs, ri) = r.HeroFaith(); var church = r.Church; var pol = st.FaithPol!;
        Assert.NotNull(church.ParishAt(hs));
        Piety.Adjust(church, r.People, hero, 100);
        pol.Relics[4242] = new RelicRow { Key = 4242, Name = "Bone of Saint Aldric", Religion = r.Religions.Get(ri)!.Id, Holder = 0, At = hs };
        pol.Relics[4243] = new RelicRow { Key = 4243, Name = "Tooth of Saint Wend", Religion = r.Religions.Get(ri)!.Id, Holder = 0, At = hs };
        var p = FaithOps.Of(st)!;
        Assert.Equal(2, Relics.OnAltar(p, hs).Count);
        int piety0 = Piety.Of(church, r.People, hero); long sec = SecOf(30);
        var res = r.Do(FaithDesk.Steal(hs, 4243, false, sec), sec);                                                // unseen: nobody knows who
        Assert.True(res.Ok, res.Text);
        Assert.Empty(st.Law?.Crimes.Values.Where(c => c.Kind == "sacrilege") ?? []);
        Assert.Equal(piety0, Piety.Of(church, r.People, hero)); Assert.Equal(0, Standing.Of(st.Standing, hero, GroupRef.Religion(ri)));
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "relic_taken" && e.Subjects.All(s => s != hero));
        res = r.Do(FaithDesk.Steal(hs, 4242, true, sec + 10), sec + 10);                                           // seen
        Assert.True(res.Ok, res.Text);
        var crime = Assert.Single(st.Law!.Crimes.Values, c => c.Kind == "sacrilege");
        Assert.Equal(hero.Value, crime.Doer);
        Assert.Equal(piety0 - 30, Piety.Of(church, r.People, hero));                                              // piety -30
        Assert.Equal(-300, Standing.Of(st.Standing, hero, GroupRef.Religion(ri)));                                // standing -300 with the faith
        Assert.Empty(Relics.OnAltar(FaithOps.Of(st)!, hs));
        Assert.Equal(2, Relics.CarriedBy(FaithOps.Of(st)!, hero).Count);
        output.WriteLine($"FC-6: piety {piety0} -> {Piety.Of(church, r.People, hero)}, standing {Standing.Of(st.Standing, hero, GroupRef.Religion(ri))}");
    }

    [Fact]
    public void AChurchBuysARelicForCoin_VenerationIsOnceASeason_AndAPardonersFakeIsSpottedOnlyByAnEyeForIt()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var (hs, ri) = r.HeroFaith(); var church = r.Church; var pol = st.FaithPol!; var led = st.Economy!.Ledger;
        string rid = r.Religions.Get(ri)!.Id;
        pol.Relics[7] = new RelicRow { Key = 7, Name = "Bone of Saint Aldric", Religion = rid, Holder = 0, At = hs };
        long sec = SecOf(32); int day = 32;
        Piety.Adjust(church, r.People, hero, -10); int p0 = Piety.Of(church, r.People, hero);
        Assert.True(r.Do(FaithDesk.Venerate(hs, 7, sec), sec).Ok);
        Assert.Equal(p0 + 2, Piety.Of(church, r.People, hero));
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.Venerate(hs, 7, sec + 5), sec + 5).Code);            // once a season
        Assert.True(r.Do(FaithDesk.Venerate(hs, 7, sec + 15 * 86400), sec + 15 * 86400).Ok);
        // a pardoner's fake, for sale
        pol.Relics[9] = new RelicRow { Key = 9, Name = "Bone of Saint Wend", Religion = rid, Holder = 2, At = hs, Fake = true };
        Fund(r, FaithOps.PurseOf(st, hero), 20_000);
        long coin0 = led.Balance(FaithOps.PurseOf(st, hero)).Mites;
        Assert.True(r.Do(FaithDesk.BuyRelic(hs, 9, sec + 100), sec + 100).Ok);
        Assert.Equal(coin0 - 5000, led.Balance(FaithOps.PurseOf(st, hero)).Mites);
        var p = FaithOps.Of(st)!; int row = FaithOps.RowOf(st, hero);
        Assert.Equal(Relics.SpotsFake(p.S, r.People, row), Relics.Inspect(st, r.Content, hero, 9).Knows);          // Wits 60 or Theology 30
        // the church buys it (a fake as willingly as a true one) and the chest pays; the fake is exposed in time
        var chest = led.TryFind(PurseKind.Chest, r.Region.Settlements[hs].Id, out var cp) ? cp : PurseId.None; Fund(r, chest, 10_000);
        var sold = r.Do(FaithDesk.SellRelic(hs, 9, sec + 200), sec + 200); Assert.True(sold.Ok, sold.Text);
        Assert.True(led.Audit().IsConserved);
        Assert.Equal(0, pol.Relics[9].Holder);
        Assert.True(pol.Relics[9].Fake);
        int exposed = 0; var priest = new EntityId((ulong)church.ParishAt(hs)!.Priest); int pp0 = Piety.Of(church, r.People, priest);
        for (int s = 1; s <= 40 && exposed == 0; s++) exposed += Relics.ExposeFakes(st, r.Content, day + s * 15);
        Assert.Equal(1, exposed); Assert.Equal(3, pol.Relics[9].Holder);
        Assert.True(Piety.Of(church, r.People, priest) < pp0);
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "fake_relic_exposed");
        // pardoners come to towns (0x656) on some season
        int came = 0; for (int s = 1; s <= 30; s++) came += Relics.PardonersCome(st, r.Content, s * 15);
        Assert.True(came > 0); Assert.All(pol.Relics.Values.Where(x => x.Holder == 2 && x.Key != 9), x => Assert.True(x.Fake));
    }

    // ------------------------------------------------------------------ FC-7

    [Fact]
    public void FC7_AHeresyInATownGrowsByTheConversionRule_AndTheBishopAnswersByHisPiety()
    {
        using var r = new FaithRig();
        var st = r.State; var pt = r.People; var church = r.Church; int ri = r.HeroFaith().Religion; int day = Now(r) + 30;
        int town = church.Parishes.Keys.Where(s => r.Religions.OfSettlement(r.Region, s) == ri).OrderByDescending(s => AdultsOf(r, s, day)).First();
        int adults = AdultsOf(r, town, day); output.WriteLine($"town {town}: {adults} adults");
        Assert.True(adults >= 150);
        var hht = st.Population.Households; var e = st.Economy!;
        int aggrieved = 0; for (int h = 0; h < hht.Count && aggrieved < 200; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == town) { e.Grievances[h] = Grievance.Hunger | Grievance.Tax; aggrieved++; }
        var preacher = new EntityId((ulong)church.Parishes[town].Priest);
        var row = Heresy.Start(st, r.Content, town, preacher, "tithe_poor", day)!;
        Assert.NotNull(row);
        var circle = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) >= 16 && pt.Occupation[i] != Occupation.Priest).Take(Math.Max(40, adults / 8)).ToList();
        foreach (int i in circle) row.Adherents.Add(pt.IdOf(i).Value);
        // the expectation of the rule, person by person
        var s = FaithOps.Of(st)!.S;
        var people = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) >= 16).ToList();
        var q = people.ToDictionary(i => i, i => row.Adherents.Contains(pt.IdOf(i).Value) ? 1.0 : 0.0);
        var conv = people.ToDictionary(i => i, i => Heresy.ConvertPm(s, Heresy.GrievancesOf(st, i), Piety.Of(church, pt, pt.IdOf(i))) / 1000.0);
        int start = row.Adherents.Count;
        for (int d = 0; d < TimeConst.DaysPerSeason; d++)
        {
            double n = q.Values.Sum(), cand = people.Count - n;
            double hear = Math.Min(1.0, n * s.HearingsPerAdherent / Math.Max(1.0, cand));
            foreach (int i in people) q[i] += (1 - q[i]) * hear * conv[i];
        }
        double expected = q.Values.Sum();
        for (int d = 0; d < TimeConst.DaysPerSeason; d++) Heresy.Daily(st, r.Content, day + 1 + d);
        output.WriteLine($"adherents {start} -> {row.Adherents.Count}, expectation {expected:F1}");
        Assert.InRange(row.Adherents.Count, (int)(expected * 0.9), (int)Math.Ceiling(expected * 1.1));
        Assert.True(row.Adherents.Count > start);

        // the bishop's answer, by his piety: a pious bishop tries the preacher, a middling one preaches against it, a lax one lets it be; a widespread one draws an interdict
        var see = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, town))!; var bishop = new EntityId((ulong)see.Bishop);
        row.Adults = adults;
        Piety.Adjust(church, pt, bishop, 100 - Piety.Of(church, pt, bishop)); int share = row.Adherents.Count * 100 / adults;
        Heresy.Season(st, r.Content, day + 40);
        Assert.True(row.Response is 2 or 3, $"response {row.Response} at {share}% adherents");                    // 3 (interdict) when a third of the adults follow, else 2
        Assert.Contains(st.Chronicle.All, c => c.TypeId is "heresy_charged" or "interdict_laid");
        if (row.Response == 2) Assert.Contains(st.Law!.Crimes.Values, c => c.Kind == "heresy" && c.Doer == preacher.Value);
        if (row.Response == 3) Assert.True(row.Interdict);
    }

    [Fact]
    public void ABishopWhoIsMiddlingPreachesAgainstItAndTakesAFifthBackASeason_ALaxOneLetsItBe()
    {
        using var r = new FaithRig();
        var st = r.State; var pt = r.People; var church = r.Church; int ri = r.HeroFaith().Religion; int day = Now(r) + 30;
        int town = church.Parishes.Keys.First(s => r.Religions.OfSettlement(r.Region, s) == ri && church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, s)) is { Bishop: >= 0 });
        var preacher = new EntityId((ulong)church.Parishes[town].Priest);
        var row = Heresy.Start(st, r.Content, town, preacher, "usury_forbid", day)!;
        var folk = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) >= 16 && pt.IdOf(i) != preacher).Take(50).ToList();
        foreach (int i in folk) row.Adherents.Add(pt.IdOf(i).Value);
        row.Adults = 4000;                                                                                        // a small share of a big town: no interdict
        var see = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, town))!; var bishop = new EntityId((ulong)see.Bishop);
        Piety.Adjust(church, pt, bishop, 55 - Piety.Of(church, pt, bishop));                                      // pious enough to preach, not to try
        int n0 = row.Adherents.Count;
        Heresy.Season(st, r.Content, 45);
        Assert.Equal(1, row.Response);
        Assert.InRange(row.Adherents.Count, n0 - n0 * 20 / 100 - 1, n0 - n0 * 20 / 100);                         // adherents -20%
        Assert.Contains(row.Preacher, row.Adherents.Select(a => (long)a));
        // a lax bishop lets it be
        var row2 = Heresy.Start(st, r.Content, church.Parishes.Keys.First(s => s != town && r.Religions.OfSettlement(r.Region, s) == ri && church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, s)) is { Bishop: >= 0 }), new EntityId((ulong)church.Parishes.Values.First(pp => pp.Settlement != town && r.Religions.OfSettlement(r.Region, pp.Settlement) == ri && church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, pp.Settlement)) is { Bishop: >= 0 }).Priest), "fasting_strict", 46)!;
        Assert.NotNull(row2);
        var see2 = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, row2.Settlement))!; var b2 = new EntityId((ulong)see2.Bishop);
        Piety.Adjust(church, pt, b2, 10 - Piety.Of(church, pt, b2));
        Heresy.Season(st, r.Content, 60);
        Assert.Equal(4, row2.Response);
    }

    // ------------------------------------------------------------------ FC-11

    [Fact]
    public void FC11_ALairNearbyAndTwoMonsterRumoursHeardByMostPushSuperstitionUp_AndAHeadShownTakesFortyOff()
    {
        using var r = new FaithRig();
        var st = r.State; var pt = r.People; var hero = r.Hero; var p = FaithOps.Of(st)!;
        int s = Enumerable.Range(0, r.Region.Settlements.Count).OrderByDescending(x => Superstition.BaseOf(p, x)).ThenBy(x => x).First();
        int b0 = Superstition.BaseOf(p, s); output.WriteLine($"settlement {s}: base {b0}");
        Assert.True(b0 >= 30, "a lair within 10 km");
        var talk = st.Talk ??= new TalkState();
        var residents = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == s).ToList();
        talk.Rumours.EnsureCounts(i => pt.HomeSettlement[i]);
        for (int k = 0; k < 2; k++)
        {
            var id = st.Ids.Allocate(IdKind.Event);
            talk.Rumours.Add(new Rumour { Id = id, Source = EntityId.None, Kind = RumourKind.Omen, TypeId = "lair_legend", Settlement = s, Place = "x", ExpiresDay = 999, Authored = true });
            foreach (int i in residents.Take(residents.Count * 6 / 10 + 1)) talk.Rumours.AddHearing(id, new Hearing { Listener = i, Teller = -1, Hour = 100, How = HearHow.Told }, 6);
        }
        int added = Superstition.Daily(st, r.Content, 20);
        Assert.Equal(20, added);
        Assert.True(Superstition.Of(p, s) >= 50);
        Assert.True(Superstition.AvoidsNight(p.S, Superstition.Of(p, s)));                                        // villagers keep off the night roads and the barrow
        Assert.Equal(0, Superstition.Daily(st, r.Content, 21));                                                  // once for each rumour
        // slay a monster and show its head: -40
        var kill = new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(SecOf(22)), "monster_slain", [hero], [new Cause(CauseKind.Other, null)], new Dictionary<string, string>());
        st.Chronicle.Add(kill);
        long sec = SecOf(23); int before = Superstition.Of(p, s);
        var res = r.Do(FaithDesk.ShowHead(s, sec), sec); Assert.True(res.Ok, res.Text);
        Assert.Equal(Math.Max(0, before - 40), Superstition.Of(FaithOps.Of(st)!, s));
        Assert.Equal(HeroResultCode.Refused, r.Do(FaithDesk.ShowHead(s, sec + 5), sec + 5).Code);                // one kill, one showing
        // it falls five a season back toward what the region gives
        int level = Superstition.Of(FaithOps.Of(st)!, s);
        Superstition.Add(FaithOps.Of(st)!, s, 30); int up = Superstition.Of(FaithOps.Of(st)!, s);
        Superstition.Season(st, r.Content, 75);
        Assert.Equal(up - 5, Superstition.Of(FaithOps.Of(st)!, s));
        Assert.True(level >= 0);
    }

    [Fact]
    public void AWitchHuntStartsWhereSuperstitionIsHighAfterAHardSeason_AndTheBishopMayStopIt()
    {
        using var r = new FaithRig();
        var st = r.State; var pt = r.People; var church = r.Church; int ri = r.HeroFaith().Religion;
        var p = FaithOps.Of(st)!;
        int town = church.Parishes.Keys.First(s => r.Religions.OfSettlement(r.Region, s) == ri && church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, s)) is { Bishop: >= 0 });
        var hht = st.Population.Households;
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == town) st.Economy!.Grievances[h] |= Grievance.Hunger;       // a bad harvest
        int started = 0, season = 0;
        for (; season < 60 && started == 0; season++)
        {
            Superstition.Set(FaithOps.Of(st)!, town, 90);
            started = Superstition.Season(st, r.Content, Now(r) + (season + 1) * TimeConst.DaysPerSeason);
        }
        Assert.Equal(1, started);
        var hunt = Assert.Single(FaithOps.Of(st)!.Pol.Hunts);
        Assert.Equal(0, hunt.State); Assert.Equal(town, hunt.Settlement);
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "witch_hunt" && e.Subjects.Contains(new EntityId(hunt.Accused)));
        // the next season: stopped by a merciful bishop or handed to the court
        var see = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, town))!;
        Piety.Adjust(church, pt, new EntityId((ulong)see.Bishop), 100);
        Superstition.Season(st, r.Content, Now(r) + (season + 3) * TimeConst.DaysPerSeason);
        Assert.NotEqual(0, hunt.State);
        Assert.Contains(st.Chronicle.All, e => e.TypeId is "witch_hunt_stopped" or "witch_hunt_tried");
        if (hunt.State == 2) Assert.Contains(st.Law!.Crimes.Values, c => c.Kind == "heresy" && c.Doer == hunt.Accused);
    }

    // ------------------------------------------------------------------ believed miracles (10a)

    [Fact]
    public void BelievedMiracles_AreRare_EachHasItsNaturalExplanation_AndMayBeDisputed()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var (hs, ri) = r.HeroFaith(); var church = r.Church;
        // a fire that stopped at the church: the natural reason is B5's, stone wall, rain or a change of wind
        var fire = st.Fire ??= new FireBook();
        int told = 0, tries = 400;
        for (int d = 1; d <= tries; d++)
        {
            fire.Stops.Add(new FireStop(SecOf(d), 7000UL + (ulong)d, 5, d % 3 == 0 ? "stone_wall" : d % 3 == 1 ? "rain" : "wind_change", true));
            told += Miracles.Daily(st, r.Content, d);
        }
        output.WriteLine($"{told} believed miracles in {tries} fires");
        Assert.InRange(told, 20, 120);                                                                            // 15%: rare, not never
        var pol = st.FaithPol!;
        Assert.Equal(told, pol.Miracles.Count);
        Assert.All(pol.Miracles, m => Assert.StartsWith("ch_note_miracle_", m.Natural));                          // the natural explanation is always there
        Assert.All(st.Chronicle.All.Where(e => e.TypeId == "miracle_fire"), e => Assert.StartsWith("ch_note_miracle_", e.Causes[0].NoteKey));
        Assert.Contains(pol.Miracles, m => m.Disputed); Assert.Contains(pol.Miracles, m => !m.Disputed);
        Assert.Contains(st.Chronicle.All, e => e.TypeId == "miracle_disputed");
        // a pilgrim's cure: a natural recovery made after the faith event
        long sec = SecOf(400);
        var plan = Ravensguard.Sim.Medicine.MedicineWiring.PlanOf(r.Content, "human")!;
        var wl = st.Wounds ??= new WoundLedger(st.Seed, sec); var book = wl.For(hero.Value, plan, sec); book.PainX100 = 5000;
        int site = r.Religions.HolySiteIn(r.Region, ri);
        Assert.True(r.Do(FaithDesk.Vow(hs, site, sec), sec).Ok);
        Assert.True(r.Do(FaithDesk.Arrive(site, sec + 100), sec + 100).Ok);
        Assert.Single(pol.Cures);
        int arriveDay = (int)((sec + 100) / 86400), cures = 0;
        for (int d = 0; d < 300 && cures == 0; d++)
        {
            book.PainX100 = 0;                                                                                    // he healed as the hurt heal
            if (pol.Cures.Count == 0) pol.Cures.Add(new CureWatch { Person = hero.Value, Day = arriveDay, Settlement = site, Entry = st.Chronicle.All.Last(e => e.TypeId == "pilgrimage_done").Id.Value });
            cures += Miracles.Daily(st, r.Content, arriveDay + 6 + d);
        }
        Assert.Equal(1, cures);
        var cure = st.Chronicle.All.Single(e => e.TypeId == "miracle_cure");
        Assert.Equal("ch_note_miracle_natural_recovery", cure.Causes[0].NoteKey);
        Assert.Equal("pilgrimage_done", st.Chronicle.Get(cure.Causes[0].EventId!.Value)!.TypeId);                // the faith event came first
    }

    // ------------------------------------------------------------------ the hero's zero id (Phase 6 review S7, S8)

    [Fact]
    public void S7_TheHeroHealedAfterAPilgrimage_IsNamedInTheCureEntry_AndGainsPiety()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var (hs, ri) = r.HeroFaith(); var p = FaithOps.Of(st)!; var pol = p.Pol;
        Assert.Equal(0UL, hero.Value);                                                                            // the premise: the hero's id is the zero id
        var plan = Ravensguard.Sim.Medicine.MedicineWiring.PlanOf(r.Content, "human")!;
        var wl = st.Wounds ??= new WoundLedger(st.Seed, SecOf(10)); var book = wl.For(hero.Value, plan, SecOf(10)); book.PainX100 = 0;
        int before = Piety.Of(p.Book, r.People, hero), cures = 0;
        for (int d = 0; d < 400 && cures == 0; d++)
        {
            pol.Cures.Add(new CureWatch { Person = hero.Value, Day = 1, Settlement = hs, Entry = 0 });
            cures += Miracles.Daily(st, r.Content, 30 + d);
        }
        Assert.Equal(1, cures);
        var cure = st.Chronicle.All.Single(e => e.TypeId == "miracle_cure");
        Assert.Contains(cure.Params!, x => x.Name == "doer");                                                      // "{doer}" was missing
        Assert.Contains(hero, cure.Subjects);
        Assert.True(Piety.Of(p.Book, r.People, hero) > before);
    }

    [Fact]
    public void S8_TheHerosOwnRequestForABan_IsAnAskedBan_AndABannedHeroDoesNotMakeARulerlessRealmHisOwn()
    {
        using var r = new FaithRig();
        var st = r.State; var hero = r.Hero; var rb = st.Rule!; var (hs, ri) = r.HeroFaith(); var p = FaithOps.Of(st)!;
        var (a, b, v, bFief, vFief, realm, other) = Lords(r);
        var see = Excommunication.SeeFor(p, b.Value, ri, null)!; var bishop = new EntityId((ulong)see.Bishop);
        // the hero asks: the entry is the asked kind, naming him
        Fund(r, FaithOps.PurseOf(st, hero), 20_000); rb.AddOpinion(bishop.Value, hero.Value, 60);
        rb.Rulers[realm] = (long)b.Value;
        Assert.Equal("", Excommunication.Request(st, r.Content, hero, b, Cause(st, "oath_broken", b, 38), 40, out var row));
        var asked = st.Chronicle.All.Last(e => e.TypeId.StartsWith("excommunicat"));
        Assert.Equal("excommunication_asked", asked.TypeId);
        Assert.Contains(asked.Params!, x => x.Name == "asker");
        // the hero banned: a realm with no ruler is not "his realm"
        rb.Rulers.Remove(other); var pc = PoliticsEngine.Context(r.Sim, st.Politics!, rb, RuleChron.Silent, 41);
        var rulerless = pc.AliveRealms().Where(x => pc.RulerOf(x) == 0 && !pc.HeroRules(x)).ToList();
        int cbs = st.Politics!.Cbs.Count;
        Excommunication.Impose(st, r.Content, p, pc, hero, ri, bishop, null, EntityId.None, 42);
        foreach (int x in rulerless) foreach (var f in rb.Fiefs.Values.Where(f => f.Realm == x && f.Holder > 0))
            Assert.Equal(0, Opinions.Standing(st.Politics!, (ulong)f.Holder, 0, "excommunicated"));                // old code: every lord of a ruler-less realm
        Assert.Equal(cbs, st.Politics!.Cbs.Count);                                                                // and no casus belli against it
    }

    // ------------------------------------------------------------------ FC-14

    private static void Populate(FaithRig r)
    {
        var st = r.State; var church = r.Church; var (hs, ri) = r.HeroFaith(); var pt = r.People;
        var p = FaithOps.Of(st)!; int town = church.Parishes.Keys.First(s => r.Religions.OfSettlement(r.Region, s) == ri);
        var preacher = new EntityId((ulong)church.Parishes[town].Priest);
        var h = Heresy.Start(st, r.Content, town, preacher, "slavery_forbid", 30)!;
        foreach (int i in Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town).Take(12)) h.Adherents.Add(pt.IdOf(i).Value);
        var (_, b, _, _, _, _, _) = Lords(r);
        var see = church.SeeOf(r.Religions.Get(ri)!.Id, ChurchSeeding.RealmOf(r.Region, town)) ?? church.Sees.Values.First();
        Excommunication.Impose(st, r.Content, p, null, b, ri, new EntityId((ulong)see.Bishop), null, EntityId.None, 31);
        church.Pilgrim = new PilgrimRow { Religion = r.Religions.Get(ri)!.Id, Target = town, StartDay = 32, Entry = 5 };                         // a pilgrimage under way
        p.Pol.Relics[88] = new RelicRow { Key = 88, Name = "Bone of Saint Aldric", Religion = r.Religions.Get(ri)!.Id, Holder = 1, Person = r.Hero.Value, Fame = 1, SinceDay = 33 };
        Superstition.Add(p, town, 25);
        p.Pol.Hunts.Add(new HuntRow { Settlement = town, Day = 34, Accused = pt.IdOf(0).Value, Entry = 3, State = 1 });
        p.Pol.Miracles.Add(new MiracleRow { Day = 35, Settlement = town, Kind = "fire_stopped", Natural = "ch_note_miracle_rain", Entry = 4, Disputed = true });
        p.Pol.Cures.Add(new CureWatch { Person = r.Hero.Value, Day = 36, Settlement = town, Entry = 6 });
        p.Pol.CountedRumours.Add(77); p.Pol.HeadsShown.Add(78); p.Pol.FireStopsSeen = 2; p.Pol.VenerateSeason = 2;
    }

    [Fact]
    public void FC14_TheFaithPoliticsSection_SavesLoadsAndSavesByteIdentical_WithAHeresyABanAPilgrimageAndRelics_AndIsAbsentWhileEmpty()
    {
        using var r = new FaithRig();
        var first = WorldSave.Capture(r.State, "h", "0.1");
        Assert.DoesNotContain(FaithPoliticsSave.SectionName, first.Sections.Keys);                                  // a world that never met a heresy keeps its bytes
        Populate(r);
        var pol = r.State.FaithPol!; ulong hash = pol.Hash();
        var b1 = SaveCodec.SerializeCompressed(FaithPoliticsSave.Capture(pol));
        var back = SaveCodec.DeserializeCompressed<FaithPoliticsSave>(b1).Restore();
        Assert.Equal(hash, back.Hash());
        Assert.Equal(b1, SaveCodec.SerializeCompressed(FaithPoliticsSave.Capture(back)));
        var bytes = SaveCodec.Encode(WorldSave.Capture(r.State, "h", "0.1"));
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), r.Content, "h", null, r.W.Continent, r.W.History); st.Content = r.Content;
        Assert.Equal(hash, st.FaithPol!.Hash());
        Assert.Equal(r.Church.Hash(), st.Church!.Hash());
        Assert.True(st.Standing!.Marks.Of(FaithPoliticsExcommunicated(st))?.Excommunicated.Count > 0);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));                                // save -> load -> save
        Assert.NotEmpty(st.FaithPol!.Heresies.Values.First().Adherents);
    }

    private static EntityId FaithPoliticsExcommunicated(WorldState st) => new(st.FaithPol!.Excomms[0].Person);

    [Fact]
    public void FC14_TwoIdenticalRunsOfTheFaithsDaysAndSeasons_GiveTheSameFaithHash_AndAResumedSaveGoesOnTheSame()
    {
        ulong Run()
        {
            using var r = new FaithRig();
            Populate(r);
            for (int d = 36; d < 36 + 45; d++)
            {
                FaithPoliticsLink.Daily(r.State, r.Content, d);
                if (d % TimeConst.DaysPerSeason == 0) FaithPoliticsLink.Season(r.State, r.Content, d);
            }
            return r.State.FaithPol!.Hash() ^ r.Church.Hash();
        }
        Assert.Equal(Run(), Run());
        // a save in the middle of it resumes to the same state as the uninterrupted run
        using var straight = new FaithRig(); Populate(straight);
        for (int d = 36; d < 60; d++) { FaithPoliticsLink.Daily(straight.State, straight.Content, d); if (d % TimeConst.DaysPerSeason == 0) FaithPoliticsLink.Season(straight.State, straight.Content, d); }
        var bytes = SaveCodec.Encode(WorldSave.Capture(straight.State, "h", "0.1"));
        var resumed = WorldSave.Restore(SaveCodec.Decode(bytes), straight.Content, "h", null, straight.W.Continent, straight.W.History); resumed.Content = straight.Content;
        for (int d = 60; d < 80; d++)
        {
            foreach (var w in new[] { straight.State, resumed })
            {
                FaithPoliticsLink.Daily(w, straight.Content, d); if (d % TimeConst.DaysPerSeason == 0) FaithPoliticsLink.Season(w, straight.Content, d);
            }
        }
        Assert.NotEqual(0UL, straight.State.FaithPol!.Hash());
        Assert.Equal(straight.State.FaithPol!.Hash(), resumed.FaithPol!.Hash());                                  // save -> load -> continue equals the uninterrupted run
        Assert.Equal(straight.State.Church!.Hash(), resumed.Church!.Hash());
    }

    [Fact]
    public void S9_ATrueReliquarySoldToAnotherChurch_IsListedOnItsAltar()
    {
        using var r = new FaithRig();
        var st = r.State; var (hs, ri) = r.HeroFaith(); var pol = st.FaithPol!;
        var art = new Ravensguard.Sim.WorldGen.History.Artefact { Id = EntityId.Make(IdKind.Other, 880_001), Name = "Reliquary of Saint Orme", Kind = "reliquary", Holder = EntityId.Make(IdKind.Other, 880_002), Status = Ravensguard.Sim.WorldGen.History.ArtefactStatus.Held };
        st.History!.Artefacts.Add(art);
        pol.Relics[art.Id.Value] = new RelicRow { Key = art.Id.Value, Name = art.Name, Religion = r.Religions.Get(ri)!.Id, Holder = 0, At = hs };
        Assert.Contains(Relics.OnAltar(FaithOps.Of(st)!, hs), x => x.Key == art.Id.Value);
    }
}
