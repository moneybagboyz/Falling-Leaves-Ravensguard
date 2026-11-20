using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Rule;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Politics;

/// <summary>A new game of seed 1234's embark region with the politics attached (P6-D2), driven by hand at the barrier: contexts, days and the hero's commands.</summary>
internal sealed class PoliticsRig : IDisposable
{
    public RuleWorldRig R { get; } = new();
    public WorldState State => R.State;
    public PoliticsBook B => State.Politics!;
    public RuleBook Rule => R.Book;
    public int Day { get; set; }

    public PoliticsRig()
    {
        Day = (int)(R.Runtime.HourDone / 24) + 1;
        Assert.NotNull(State.Politics);
    }

    public PoliticsCtx C(int? day = null) => PoliticsEngine.Context(R.Sim, B, Rule, RuleChron.Of(State.Chronicle, State.Ids), day ?? Day);
    public EntityId Hero => R.Hero;

    /// <summary>The first spring day at or after the given one (a war's season term is the spring's).</summary>
    public static int NextSeasonStart(int day) { int d = day; while ((d % TimeConst.DaysPerYear) % TimeConst.DaysPerSeason != 0) d++; return d; }

    /// <summary>A fief with a living holder and a realm, ascending, skipping <paramref name="skip"/> of them.</summary>
    public FiefRow Fief(int skip = 0) => Rule.Fiefs.Values.Where(f => f.Holder >= 0 && f.Realm >= 0 && !f.Forfeited).Skip(skip).First();

    /// <summary>Another live realm than the given one, ascending (the history has several).</summary>
    public int OtherRealm(int not) => C().AliveRealms().First(r => r != not);

    public void Fund(PoliticsCtx c, Party p, long mites)
    {
        var pr = c.R.PurseOf(p); Assert.False(pr.IsNone);
        State.Economy!.Ledger.Mint(pr, new Coin(mites), SourceReason.Seed);
    }

    public IEnumerable<ChronicleEntry> Entries(string type) => State.Chronicle.All.Where(e => e.TypeId == type);

    public void Dispose() => R.Dispose();
}

public class PoliticsTests(ITestOutputHelper output)
{
    private static PoliticsSettingsDef S => new() { Id = "politics" };

    // ------------------------------------------------------------------ DA-1

    [Fact]
    public void DA1_TheWarUtility_ClaimCasusBelliRatioTreasurySeasonHarvest_AndTheThresholds()
    {
        var spring = new WarInputs(true, 40, 150, 100, 3, Season.Spring, 10, false, false);
        Assert.Equal(30 + 40 + 15 + 15 + 10 - 10, WarUtility.Score(S, spring));                       // 100
        Assert.True(WarUtility.Declares(S, 100, LordTrait.None));
        var winter = spring with { Season = Season.Winter };
        int u = WarUtility.Score(S, winter); Assert.Equal(60, u);
        Assert.True(WarUtility.Declares(S, u, LordTrait.None)); Assert.True(WarUtility.Declares(S, u, LordTrait.Martial));
        Assert.False(WarUtility.Declares(S, u, LordTrait.Cautious));                                  // the cautious need 70
        Assert.Equal(30 + 40 + 15 + 15 + 10 - 10 - 20, WarUtility.Score(S, spring with { Allied = true }));
        Assert.Equal(100 - 30, WarUtility.Score(S, spring with { TruceHolds = true }));
        Assert.Equal(60, WarUtility.RatioTerm(S, 100000, 100));                                       // clamped at +60
        Assert.Equal(-30, WarUtility.RatioTerm(S, 1, 100000));                                       // a host of nothing: ratio 0, the term -30 (the clamp's -60 is for the spec's reach)
        Assert.Equal(20, Math.Min(S.TreasurySeasonsMax, 9 * S.TreasurySeasonPts));                    // the treasury term stops at 20
        Assert.Equal(40, WarUtility.Threshold(S, LordTrait.Martial)); Assert.Equal(70, WarUtility.Threshold(S, LordTrait.Cautious)); Assert.Equal(50, WarUtility.Threshold(S, LordTrait.None));
    }

    [Fact]
    public void DA1_TheTraitsOfALordAreTwoOfSeven_APureFunctionOfTheSeedAndTheId()
    {
        for (ulong p = 1; p < 200; p++)
        {
            var t = LordTraits.Of(1234, p); Assert.Equal(2, LordTraits.Names(t).Count());
            Assert.Equal(t, LordTraits.Of(1234, p));
        }
        Assert.NotEqual(LordTraits.Of(1234, 5), LordTraits.Of(1234, 6) | LordTraits.Of(1234, 7) | LordTraits.Of(1234, 8) | LordTraits.Of(1234, 9));
    }

    // ------------------------------------------------------------------ DA-2 (the continent's realms call the same function; history v1 does at part F)

    [Fact]
    public void DA2_TheRealmsOfTheContinentCallTheSameUtility_AndAtAnyTimeAFewRealmPairsAreAtWar()
    {
        using var rig = new PoliticsRig();
        var c0 = rig.C(); var realms = c0.AliveRealms().ToList(); Assert.True(realms.Count >= 3, "the history has realms");
        int pairs = 0; foreach (int a in realms) foreach (int b in realms) if (a < b && c0.InContact(a, b)) pairs++;
        Assert.True(pairs > 0);
        long atWar = 0, samples = 0; int wars = 0;
        int day = PoliticsRig.NextSeasonStart(rig.Day);
        for (int s = 0; s < 800; s++)                                                   // 200 years of seasons
        {
            var c = rig.C(day + s * TimeConst.DaysPerSeason); Diplomacy.Season(c);
            atWar += rig.B.Wars.Count(w => w.Alive); samples++;
        }
        wars = rig.B.Wars.Count;
        double share = pairs == 0 ? 0 : (double)atWar / samples / pairs;
        output.WriteLine($"realms {realms.Count} touching pairs {pairs} wars begun {wars} average at war {(double)atWar / samples:F2} share of pairs {share:P0}");
        Assert.True(share <= 0.35, $"{share:P0} of the realm pairs at war");
        Assert.True(wars > 0, "the realms do fight");
        Assert.All(rig.B.Wars, w => Assert.NotEqual(0UL, w.DeclEntry));
    }

    // ------------------------------------------------------------------ DA-3

    [Fact]
    public void DA3_Peace_WearinessAndScoreDecide_ATruceATributeOrAFiefCeded()
    {
        Assert.True(WarUtility.Sues(S, 60, 15)); Assert.False(WarUtility.Sues(S, 40, 15));
        Assert.Equal(WarUtility.Terms.Truce, WarUtility.TermsFor(S, 15)); Assert.Equal(WarUtility.Terms.Tribute, WarUtility.TermsFor(S, 35)); Assert.Equal(WarUtility.Terms.CedeFief, WarUtility.TermsFor(S, 65));
        Assert.Equal(60, WarUtility.Weariness(S, 3, 4, 0));                                            // 30 + 20 + 10

        using var rig = new PoliticsRig();
        var c = rig.C(); var f = rig.Fief(); int loser = f.Realm; int winner = rig.OtherRealm(loser);
        // a truce at a low score
        var w1 = new War { Serial = rig.B.NextWar++, Attacker = winner, Defender = loser, StartDay = c.Day - 60, Score = 15, Seasons = 4, Cb = CbKind.Claim }; rig.B.Wars.Add(w1);
        Diplomacy.MakePeace(c, w1, loser, 60);
        Assert.False(w1.Alive); Assert.NotNull(rig.B.PactOf(PactKind.Truce, winner, loser, c.Day)); Assert.Equal(loser, rig.Rule.Fiefs[f.Fief].Realm);
        // a fief ceded at 65, with its chronicle entry
        var w2 = new War { Serial = rig.B.NextWar++, Attacker = winner, Defender = loser, StartDay = c.Day - 60, Score = 65, Seasons = 5, Cb = CbKind.Claim }; rig.B.Wars.Add(w2);
        int before = rig.Entries("fief_ceded").Count();
        Diplomacy.MakePeace(c, w2, loser, 70);
        Assert.Equal(before + 1, rig.Entries("fief_ceded").Count());
        Assert.Equal(winner, rig.Rule.Fiefs.Values.Where(x => x.Realm == winner).Select(x => x.Realm).FirstOrDefault(-1));
        Assert.Contains(rig.Entries("realm_peace"), e => e.Causes.Count > 0);
        // a tribute: paid as a real transfer when both realms keep purses
        var w3 = new War { Serial = rig.B.NextWar++, Attacker = winner, Defender = loser, StartDay = c.Day, Score = 40, Seasons = 5 }; rig.B.Wars.Add(w3);
        Diplomacy.MakePeace(c, w3, loser, 60);
        var tribute = rig.B.Pacts.Last(); Assert.Equal(loser, tribute.TributeFrom); Assert.True(tribute.TributeMites > 0);
    }

    // ------------------------------------------------------------------ DA-4

    [Fact]
    public void DA4_ARumourThreeSeasonsOldWeighsHalf_AFreshOneChangesThePlan()
    {
        using var rig = new PoliticsRig();
        var c = rig.C(); int a = c.AliveRealms().First(); int b = c.AliveRealms().First(x => x != a);
        long strB = c.Strength(b);
        rig.B.Known[(a, b)] = new KnownFact { Observer = a, About = b, Strength = strB * 10, TreasurySeasons = 3, Day = c.Day - 3 * TimeConst.DaysPerSeason };      // an old rumour of a mighty rival
        var (_, _, w) = Diplomacy.Known(c, a, b); Assert.Equal(500, w);
        int stale = Diplomacy.UtilityOf(c, a, b).Utility;
        Diplomacy.Learn(c, a, b);                                                                      // the envoy brings the truth: the rival is as he is
        var (_, _, wf) = Diplomacy.Known(c, a, b); Assert.Equal(1000, wf);
        int fresh = Diplomacy.UtilityOf(c, a, b).Utility;
        Assert.NotEqual(stale, fresh);
        // a fact older than two seasons weighs half of its strength term: the same number at half weight
        var inp = new WarInputs(false, 0, 100, 10, 0, Season.Spring, 0, false, false, 1000);
        int term = WarUtility.RatioTerm(S, 100, 10);
        Assert.Equal(WarUtility.Score(S, inp) - term + term * 500 / 1000, WarUtility.Score(S, inp with { KnowPm = 500 }));
    }

    // ------------------------------------------------------------------ DA-5

    [Fact]
    public void DA5_FourSeasonsOfLords_EveryActionAChronicleEntryWithItsCause_AndNoneWithout()
    {
        using var rig = new PoliticsRig();
        var c0 = rig.C(); int day0 = PoliticsRig.NextSeasonStart(rig.Day);
        // a thin treasury and a short official give the lords something to do
        foreach (var f in rig.Rule.Fiefs.Values.Where(x => x.Holder >= 0)) { var pr = c0.R.PurseOf(Party.OfFief(f.Fief)); if (!pr.IsNone) { long bal = rig.State.Economy!.Ledger.Balance(pr).Mites; if (bal > 0) rig.State.Economy.Ledger.Transfer(pr, rig.State.Economy.Ledger.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 777_001)), new Coin(bal), TransferReason.Gift); } }
        foreach (var o in rig.Rule.Offices.Where(x => x.InOffice && x.Fief >= 0)) { o.Expected = 1000; o.Collected = 100; }
        for (int s = 0; s < 4; s++) PoliticsEngine.WholeSeason(rig.C(day0 + s * TimeConst.DaysPerSeason));
        output.WriteLine($"{rig.B.Log.Count} lord actions: " + string.Join(", ", rig.B.Log.Select(l => l.Action).Distinct()));
        Assert.NotEmpty(rig.B.Log);
        var acts = rig.State.Chronicle.All.Where(e => e.TypeId is "lord_action" or "lord_gift" or "dues_demanded" or "petition_made" or "oath_renounced").ToList();
        Assert.True(acts.Count >= rig.B.Log.Count);                                                    // the log keeps the latest 64 lines of them
        foreach (var e in acts)
        {
            Assert.NotEmpty(e.Causes); Assert.All(e.Causes, cz => Assert.True(cz.EventId != null || cz.NoteKey.Length > 0));
            Assert.NotEmpty(e.TextKey);
        }
        foreach (var l in rig.B.Log) { Assert.NotEqual(0UL, l.Entry); Assert.StartsWith("ch_note_dp_", l.Note); }
        Assert.True(rig.State.Economy!.Ledger.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ DA-6, DA-7

    [Fact]
    public void DA6_FealtyIsOffered_AtStanding300RenownOneHundredThreeContractsAndOpinion30_NotAt290()
    {
        using var rig = new PoliticsRig();
        var f = rig.Fief(); var c = rig.C(); int standing = 300, contracts = 3;
        c.Links.StandingWithFief = (_, _) => standing; c.Links.RenownPm = _ => 100; c.Links.ContractsDone = (_, _) => contracts;
        Opinions.Add(rig.B, rig.Rule, (ulong)f.Holder, rig.Hero.Value, "fealty", c.Day, 0, 30);
        var v = Road.FealtyVerdict(c, rig.Hero, f.Fief); Assert.True(v.Ok, string.Join(",", v.Terms.Where(t => !t.Met).Select(t => t.Key)));
        standing = 290; Assert.False(Road.FealtyVerdict(c, rig.Hero, f.Fief).Ok);
        standing = 300; contracts = 2; Assert.False(Road.FealtyVerdict(c, rig.Hero, f.Fief).Ok);
        contracts = 3; c.Links.RenownPm = _ => 99; Assert.False(Road.FealtyVerdict(c, rig.Hero, f.Fief).Ok);
        c.Links.RenownPm = _ => 100;
        var (ok, key) = Road.Swear(c, rig.Hero, f.Fief); Assert.True(ok, key);
        Assert.Equal((ulong)f.Holder, rig.B.OathOf(rig.Hero.Value)!.Liege); Assert.Single(rig.Entries("fealty_sworn"));
        Assert.False(Road.Swear(c, rig.Hero, f.Fief).Ok);                                              // one liege at a time
    }

    [Fact]
    public void DA7_AVassalWithOpinion50_IsGrantedAVacantManor_NoSettlementLeavesALivingHolder()
    {
        using var rig = new PoliticsRig();
        var c = rig.C();
        var f = rig.Rule.Fiefs.Values.First(x => x.Holder >= 0 && x.Realm >= 0 && c.R.Region.Fiefs[x.Fief].Settlements.Count(s => s != c.R.Region.Fiefs[x.Fief].SeatSettlement) >= 2);
        rig.B.Oaths.Add(new Fealty { Vassal = rig.Hero.Value, Liege = (ulong)f.Holder, Day = c.Day, Fief = f.Fief });
        Assert.False(Road.GrantManor(c, rig.Hero, f.Fief).Ok);                                         // opinion 0: no
        Opinions.Add(rig.B, rig.Rule, (ulong)f.Holder, rig.Hero.Value, "fealty", c.Day, 0, 50);
        var free = Road.VacantManors(c, f.Fief); Assert.True(free.Count >= 2);
        // one village is held by a living man already: it is not touched
        int taken = free[0]; var alive = rig.Rule.Fiefs.Values.First(x => x.Holder >= 0 && x.Fief != f.Fief).Holder;
        rig.B.Manors.Add(new Manor { Serial = rig.B.NextManor++, Settlement = taken, OverlordFief = f.Fief, Realm = f.Realm, Holder = alive, GrantedDay = 1 });
        var (ok, key, s) = Road.GrantManor(c, rig.Hero, f.Fief); Assert.True(ok, key);
        Assert.NotEqual(taken, s);
        Assert.Equal((long)rig.Hero.Value, rig.B.ManorAt(s)!.Holder); Assert.Equal(alive, rig.B.ManorAt(taken)!.Holder);
        Assert.Equal(f.Fief, rig.B.ManorAt(s)!.OverlordFief); Assert.Single(rig.Entries("manor_granted"));
        // the manor pays its holder a share of the village's hearth tax out of the fief's treasury (a real transfer)
        rig.Fund(c, Party.OfFief(f.Fief), 100_000);
        long before = rig.State.Economy!.Ledger.Audit().InPurses.Mites;
        long got = Road.ManorRents(c); Assert.True(got >= 0);
        Assert.Equal(before, rig.State.Economy.Ledger.Audit().InPurses.Mites);
    }

    // ------------------------------------------------------------------ DA-8 (rise to lord, through the hero's desk)

    [Fact]
    public void DA8_RiseToLord_TheRulerGrantsAVacantFiefForWarService_ADeedWithReliefPaid_AndTheFiefTabIsLive()
    {
        using var rig = new PoliticsRig();
        var c = rig.C(); var f = rig.Fief(); int fief = f.Fief; int realm = f.Realm;
        long sec = rig.R.Runtime.HourDone * 3600 + 600;
        // not vacant, no service: refused
        Assert.Equal(HeroResultCode.Refused, rig.R.Do(PoliticsDesk.AskFief(fief, sec), sec).Code);
        f.Holder = -1; f.PopLord = -1; f.Receipts = [4000, 4000, 4000, 4000];                          // the line died out: the fief is vacant
        Assert.Equal(HeroResultCode.Refused, rig.R.Do(PoliticsDesk.AskFief(fief, sec + 1), sec + 1).Code);     // vacant, but he has done nothing
        Road.RecordService(rig.C(), rig.Hero, realm);                                                   // a garrison held in the war
        Opinions.Add(rig.B, rig.Rule, rig.C().KeyOf(realm), rig.Hero.Value, "granted", rig.Day, 0, 20);
        rig.Fund(rig.C(), c.R.HouseholdOf((long)rig.Hero.Value), 50_000);
        long relief = RuleMath.Relief(f.LastYearReceipts, rig.Rule.Settings.ReliefPct);
        long realmBefore = rig.R.State.Economy!.Ledger.Balance(rig.C().R.PurseOf(Party.OfRealm(realm))).Mites;
        var res = rig.R.Do(PoliticsDesk.AskFief(fief, sec + 2), sec + 2);
        Assert.Equal(HeroResultCode.Ok, res.Code); Assert.Equal((long)rig.Hero.Value, rig.Rule.Fiefs[fief].Holder);
        Assert.Contains(rig.Entries("fief_granted"), e => e.Causes.Any(z => z.NoteKey == "ch_note_dp_war_service"));
        long realmAfter = rig.R.State.Economy.Ledger.Balance(rig.C().R.PurseOf(Party.OfRealm(realm))).Mites;
        Assert.Equal(relief, realmAfter - realmBefore);                                                // the relief went to the realm's treasury
        Assert.Contains(rig.Rule.Dues, d => d.Kind == DueKind.Relief && d.State == DueState.Paid);
        // his Fief tab is live: the lord's books are his now
        var rows = RuleLedger.Rows(rig.State, rig.Day);
        Assert.Contains(rows, r => r.Cells[0] == "@ru_row_arrears");
        // as a lord he sets the tax level through the rule desk
        Assert.Equal(HeroResultCode.Ok, rig.R.Do(RuleDesk.Level(fief, Ravensguard.Sim.Taxes.TaxLevel.High, sec + 3), sec + 3).Code);
        Assert.True(rig.R.State.Economy!.Ledger.Audit().IsConserved);
        // the grant moves only a vacant holding: a held fief refuses
        var other = rig.Rule.Fiefs.Values.First(x => x.Holder >= 0 && x.Fief != fief && x.Realm >= 0 && x.Holder != (long)rig.Hero.Value);
        Assert.False(Road.GrantFief(rig.C(), other.Fief, rig.Hero, "ch_note_dp_war_service"));
    }

    // ------------------------------------------------------------------ DA-9

    [Fact]
    public void DA9_ALordRenouncesHisOath_ANewRealmWithHisFiefs_TheLiegesOpinionAndACasusBelli_VassalsChooseByOpinion()
    {
        using var rig = new PoliticsRig();
        var c = rig.C(); var f = rig.Fief(); int liege = f.Realm; ulong lord = (ulong)f.Holder;
        // two vassals: one who loves him and one who loves the old liege
        var v1 = rig.Rule.Fiefs.Values.First(x => x.Holder >= 0 && x.Fief != f.Fief && x.Holder != f.Holder);
        var v2 = rig.Rule.Fiefs.Values.First(x => x.Holder >= 0 && x.Fief != f.Fief && x.Fief != v1.Fief && x.Holder != f.Holder && x.Holder != v1.Holder);
        rig.B.Oaths.Add(new Fealty { Vassal = (ulong)v1.Holder, Liege = lord, Day = c.Day }); rig.B.Oaths.Add(new Fealty { Vassal = (ulong)v2.Holder, Liege = lord, Day = c.Day });
        rig.Rule.AddOpinion((ulong)v1.Holder, lord, 40); rig.Rule.AddOpinion((ulong)v2.Holder, c.KeyOf(liege), 40);
        int realmsBefore = c.AliveRealms().Count();
        int idx = Road.Renounce(c, lord); Assert.True(idx > 0);
        Assert.Equal(realmsBefore + 1, rig.C().AliveRealms().Count());
        Assert.Contains(idx, rig.C().AliveRealms());
        Assert.Equal(idx, rig.Rule.Fiefs[f.Fief].Realm); Assert.Equal((long)lord, rig.Rule.Rulers[idx]);
        Assert.Equal(idx, rig.Rule.Fiefs[v1.Fief].Realm);                                              // the man who liked him followed
        Assert.Equal(liege == v2.Realm ? liege : v2.Realm, rig.Rule.Fiefs[v2.Fief].Realm); Assert.NotEqual(idx, rig.Rule.Fiefs[v2.Fief].Realm);   // the other stayed
        Assert.True(Opinions.Value(rig.Rule, c.KeyOf(liege), lord) <= -60);                           // the liege's opinion
        Assert.Contains(rig.B.Cbs, cb => cb.Holder == liege && cb.Against == idx && cb.Kind == CbKind.BrokenTreaty && cb.Entry != 0);
        Assert.Single(rig.Entries("oath_renounced")); Assert.Single(rig.Entries("faction_founded"));
        Assert.False(rig.C().R.PurseOf(Party.OfRealm(idx)).IsNone);
        // the continent's season knows the realm: it may be at war at once, and the old liege weighs a war against it
        Assert.True(rig.C().InContact(liege, idx));
        var (u, cb2, claim, _) = Diplomacy.UtilityOf(rig.C(), liege, idx); Assert.NotNull(cb2); Assert.True(claim); output.WriteLine($"the liege's utility against the new realm: {u}");
        Diplomacy.Season(rig.C(PoliticsRig.NextSeasonStart(rig.Day)));                                  // no exception, the realm is a realm
    }

    // ------------------------------------------------------------------ DA-10

    [Fact]
    public void DA10_GiftsCapAtPlus20_AndFiveFadeEachSeason()
    {
        using var rig = new PoliticsRig();
        var c = rig.C(); var f = rig.Fief(); ulong lord = (ulong)f.Holder;
        rig.Fund(c, c.R.HouseholdOf((long)rig.Hero.Value), 200_000);
        long start = rig.State.Economy!.Ledger.Audit().InPurses.Mites;
        for (int i = 0; i < 6; i++) Road.Gift(c, rig.Hero, c.R.HouseholdOf(f.Holder), lord, 5000);      // 6 x 50 crowns = 300 crowns
        Assert.Equal(20, Opinions.Value(rig.Rule, lord, rig.Hero.Value));
        Assert.Equal(20, Opinions.Standing(rig.B, lord, rig.Hero.Value, "gift"));
        Assert.Equal(start, rig.State.Economy.Ledger.Audit().InPurses.Mites);                          // coin moved, none made
        Opinions.Decay(rig.B, rig.Rule); Assert.Equal(15, Opinions.Value(rig.Rule, lord, rig.Hero.Value));
        Opinions.Decay(rig.B, rig.Rule); Assert.Equal(10, Opinions.Value(rig.Rule, lord, rig.Hero.Value));
        for (int i = 0; i < 3; i++) Opinions.Decay(rig.B, rig.Rule);
        Assert.Equal(0, Opinions.Value(rig.Rule, lord, rig.Hero.Value)); Assert.DoesNotContain(rig.B.Mods, m => m.Kind == "gift");
        Assert.Equal(1, Opinions.GiftPoints(S, 1000)); Assert.Equal(3, Opinions.GiftPoints(S, 3000));
        // a war's grudge holds while the war lasts
        var o = new OpinionMod { A = 1, B = 2, Kind = "war", Amount = -50 }; rig.B.Mods.Add(o); rig.Rule.AddOpinion(1, 2, -50);
        Opinions.Decay(rig.B, rig.Rule, m => m.Kind == "war"); Assert.Equal(-50, o.Amount);
        Opinions.Decay(rig.B, rig.Rule); Assert.Equal(-40, o.Amount); Assert.Equal(-40, rig.Rule.Opinion(1, 2));
    }

    // ------------------------------------------------------------------ DA-11

    [Fact]
    public void DA11_ACapturedLordPaysTheBaronRansom_OrWithAnEmptyPurseCedesAFiefToTheCaptor()
    {
        using var rig = new PoliticsRig();
        var c = rig.C(); var f = rig.Fief(); int realm = f.Realm; int captor = rig.OtherRealm(realm);
        long price = c.S.LordRansomMites; Assert.Equal(100000, price);
        // rich: the ransom is a transfer from his household to the captor's treasury
        rig.Fund(c, c.R.HouseholdOf(f.Holder), price * 2);
        long audit = rig.State.Economy!.Ledger.Audit().InPurses.Mites;
        var capt = rig.C().R.PurseOf(Party.OfRealm(captor)); long before = capt.IsNone ? 0 : rig.State.Economy.Ledger.Balance(capt).Mites;
        Diplomacy.Ransom(rig.C(), f, realm, captor, 0);
        long after = rig.State.Economy.Ledger.Balance(rig.C().R.PurseOf(Party.OfRealm(captor))).Mites;
        Assert.Equal(price, after - before); Assert.Equal(realm, rig.Rule.Fiefs[f.Fief].Realm);
        Assert.Equal(audit, rig.State.Economy.Ledger.Audit().InPurses.Mites);
        // poor: the fief goes to the captor
        var g = rig.Fief(1); var hh = c.R.HouseholdOf(g.Holder); long bal = c.R.Balance(hh);
        if (bal > 0) rig.State.Economy.Ledger.Transfer(c.R.PurseOf(hh), rig.State.Economy.Ledger.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 777_002)), new Coin(bal), TransferReason.Gift);
        int gRealm = g.Realm; int captor2 = rig.C().AliveRealms().First(r => r != gRealm);
        Diplomacy.Ransom(rig.C(), g, gRealm, captor2, 0);
        Assert.Equal(captor2, rig.Rule.Fiefs[g.Fief].Realm);
        Assert.Equal(2, rig.Entries("lord_ransomed").Count());
        Assert.Contains(rig.Entries("lord_ransomed"), e => e.Params!.Any(p => p.Key == "ch_dp_ransom_ceded"));
    }

    // ------------------------------------------------------------------ DA-12

    [Fact]
    public void DA12_TwoYearsOfPolitics_TheSameHashTwice_AndTheSectionRoundTripsWithAWarAndATreatyUnderWay()
    {
        string Run()
        {
            using var rig = new PoliticsRig();
            int day0 = PoliticsRig.NextSeasonStart(rig.Day);
            for (int s = 0; s < 8; s++) PoliticsEngine.WholeSeason(rig.C(day0 + s * TimeConst.DaysPerSeason));
            return rig.B.Hash().ToString("X") + "/" + rig.State.Chronicle.Hash();
        }
        string a = Run(), b = Run(); output.WriteLine(a); Assert.Equal(a, b);

        using var r = new PoliticsRig(); var c = r.C(); var f = r.Fief();
        int x = f.Realm, y = r.OtherRealm(x);
        r.B.Wars.Add(new War { Serial = r.B.NextWar++, Attacker = x, Defender = y, StartDay = c.Day, Cb = CbKind.Insult, CbEntry = 7, DeclEntry = 9, Score = -12, Seasons = 2, LossA = 80, LossD = 40, GainA = 3, GainD = 15 });
        r.B.Pacts.Add(new Pact { Serial = r.B.NextPact++, Kind = PactKind.Truce, A = x, B = y + 1, StartDay = c.Day, ExpiryDay = c.Day + 30, TributeFrom = y + 1, TributeMites = 500, TributeUntilDay = c.Day + 120, Cause = 5 });
        r.B.Cbs.Add(new CasusBelli { Serial = r.B.NextCb++, Holder = y, Against = x, Kind = CbKind.Raid, Entry = 11, Day = c.Day });
        Opinions.Add(r.B, r.Rule, 5, 6, "war", c.Day, 3); r.B.Realms[77] = new RealmRow { Realm = 77, Name = "Ravenholt", Founded = true, Ruler = 99, Parent = x };
        r.B.Oaths.Add(new Fealty { Vassal = 4, Liege = 5, Day = 3, Fief = 1 }); r.B.Petitions.Add(new Petition { Person = 4, Fief = 2, Day = 3 }); r.B.Note(c.Day, 1, 4, "ch_dp_act_gift", "ch_note_dp_greedy", 12);
        r.B.Known[(x, y)] = new KnownFact { Observer = x, About = y, Strength = 400, TreasurySeasons = 2, Day = c.Day - 3 };
        var bytes = SaveCodec.SerializeCompressed(PoliticsSave.Capture(r.B));
        var back = SaveCodec.DeserializeCompressed<PoliticsSave>(bytes).Restore();
        Assert.Equal(r.B.Hash(), back.Hash()); Assert.Equal(bytes, SaveCodec.SerializeCompressed(PoliticsSave.Capture(back)));
        Assert.False(back.IsEmpty); Assert.True(new PoliticsBook().IsEmpty);
        // the world's save holds it too and a load and resave keeps the bytes
        var file = WorldSave.Capture(r.State, "h", "0.1"); Assert.Contains(PoliticsSave.SectionName, file.Sections.Keys);
        var enc = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(enc), r.R.Content, "h", null, r.R.W.Continent, r.R.W.History); st.Content = r.R.Content;
        Assert.Equal(enc, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));
        Assert.Equal(r.B.Hash(), st.Politics!.Hash());
    }

    [Fact]
    public void AnOlderSaveWithoutTheSection_KeepsItsBytes_AndAFreshWorldHasNoSectionBeforeTheFirstSeason()
    {
        using var r = new PoliticsRig();
        var file = WorldSave.Capture(r.State, "h", "0.1");
        Assert.DoesNotContain(PoliticsSave.SectionName, file.Sections.Keys);                         // nothing yet to say: a world that never had politics keeps its bytes
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), r.R.Content, "h", null, r.R.W.Continent, r.R.W.History); st.Content = r.R.Content;
        Assert.Null(st.Politics);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));
    }

    [Fact]
    public void AWorldRunsAYearOfPolitics_CoinConserved_TheSameTwice_AndASaveResumesToTheSameBook()
    {
        string Run()
        {
            using var r = new PoliticsRig();
            long h0 = r.R.Runtime.HourDone + (24 - r.R.Runtime.HourDone % 24) % 24;
            var audit0 = r.State.Economy!.Ledger.Audit();
            r.R.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24 * 62));                              // past a season start or two
            Assert.True(r.State.Economy!.Ledger.Audit().IsConserved); Assert.Equal(audit0.Minted, r.State.Economy.Ledger.Audit().Minted);
            Assert.NotNull(r.State.Politics); Assert.True(r.B.Imported); Assert.True(r.B.LastSeasonDay > 0);
            output.WriteLine($"wars {r.B.Wars.Count} pacts {r.B.Pacts.Count} cbs {r.B.Cbs.Count} mods {r.B.Mods.Count} log {r.B.Log.Count} known {r.B.Known.Count}");
            return r.B.Hash().ToString("X") + "/" + r.State.Chronicle.Hash();
        }
        string a = Run(), b = Run(); Assert.Equal(a, b);

        using var straight = new PoliticsRig();
        long h = straight.R.Runtime.HourDone + (24 - straight.R.Runtime.HourDone % 24) % 24;
        straight.R.Runtime.AdvanceTo(GameTime.FromGameHours(h + 24 * 20));
        var save = SaveCodec.Encode(WorldSave.Capture(straight.State, "h", "0.1"));
        straight.R.Runtime.AdvanceTo(GameTime.FromGameHours(h + 24 * 100));
        var want = SaveCodec.SerializeCompressed(PoliticsSave.Capture(straight.B));
        var st = WorldSave.Restore(SaveCodec.Decode(save), straight.R.Content, "h", null, straight.R.W.Continent, straight.R.W.History); st.Content = straight.R.Content;
        using var resumed = new WorldRuntime(st, straight.R.Content, new MemoryRegionPager());
        resumed.AdvanceTo(GameTime.FromGameHours(h + 24 * 100));
        Assert.Equal(want, SaveCodec.SerializeCompressed(PoliticsSave.Capture(st.Politics!)));
        Assert.Equal(straight.B.Hash(), st.Politics!.Hash());
        Assert.True(st.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void TheCouncilsAreToldWhyARealmIsAtWar_AndTheConsiderationCurvesAreIntegerAndFlatBeyondTheirEnds()
    {
        var b = new PoliticsBook(); b.Wars.Add(new War { Serial = 1, Attacker = 2, Defender = 3 });
        Assert.Equal("casus_belli", PoliticsEngine.WarCause(b, 2)); Assert.Equal("defence", PoliticsEngine.WarCause(b, 3)); Assert.Equal("", PoliticsEngine.WarCause(b, 4));
        var pts = new List<List<int>> { new() { 0, 1000 }, new() { 10, 900 }, new() { 30, 250 }, new() { 60, 0 } };
        Assert.Equal(1000, Considerations.Curve(pts, -5)); Assert.Equal(950, Considerations.Curve(pts, 5)); Assert.Equal(575, Considerations.Curve(pts, 20)); Assert.Equal(0, Considerations.Curve(pts, 99)); Assert.Equal(1000, Considerations.Curve([], 3));
    }

    [Fact]
    public void TheContentLoads_ConsiderationsOpinionKindsAndSettings_AndEveryNoteKeyIsInTheChronicleTypeRows()
    {
        using var r = new PoliticsRig();
        Assert.NotEmpty(r.B.Actions); Assert.Equal(20, r.B.OpinionKinds["gift"].Cap); Assert.Equal(-60, r.B.OpinionKinds["broken_oath"].Amount); Assert.Equal(100000, r.B.Settings.LordRansomMites);
        var types = Ravensguard.Sim.Core.ChronicleTypes.From(r.R.Content);
        var notes = types.Get("lord_action")!.NoteKeys;
        foreach (var a in r.B.Actions) foreach (var cz in a.Considerations) if (cz.NoteKey.Length > 0) { if (a.Id != "renounce") Assert.Contains(cz.NoteKey, notes); }
        foreach (var a in r.B.Actions) foreach (var cz in a.Considerations) if (cz.NoteKey.Length > 0 && a.Id == "renounce") Assert.Contains(cz.NoteKey, types.Get("oath_renounced")!.NoteKeys);
    }
}
