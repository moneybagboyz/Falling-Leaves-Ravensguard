using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-2 (overview 4, "you can rise to lord"): on a whole running game of seed 1234 the hero (every step a command through his desk, as the autoplay bot and the screens send them) does three contracts in a lord's fief,
/// is offered fealty and swears, is granted a manor, serves in the realm's war, is granted a vacant fief and pays relief, appoints a reeve and a bailiff, rules a season whose receipts are on the Fief tab, has the skimming
/// bailiff found by the audit, and the world runs about two game years (120 days). Coin and deeds stay exact. The game's own gaps (the lords' guard, escort and bounty contracts are not events a headless hero can finish; the
/// war-service contract kinds wait for the lords' war aims) are named in docs/reviews/phase-6.md: the contracts are three deliveries in the fief, the war service is recorded as C2's war-aim contract does.
/// </summary>
public class Ov2LordTests(ITestOutputHelper output)
{
    [Fact]
    public void OV2_TheHeroDoesThreeContractsForALord_SwearsFealty_IsGrantedAManorAndAVacantFief_PaysRelief_AppointsAReeveAndABailiff_AFindsTheSkimmingBailiff_InTwoYears_CoinAndDeedsExact()
    {
        var (w, s) = P6Game.Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new P6Play(rt, ctx);
        var e = s.Economy!; var pt = s.Population.People; var region = s.Population.Region;
        var hero = s.Party!.Hero;
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(300), SourceReason.Seed);                                 // the test's purse: bonds, gifts and the relief of a fief
        long seeded = e.Ledger.Audit().Seeded.Mites;
        long start = rt.HourDone;
        p.Days(6);

        // 1. three contracts in one lord's fief: deliveries of goods a household or workshop is short of (a faucet makes the goods: what he bought and carted)
        // (since P8-BL-1 the places' carters take most deliveries the day they are posted, so the hero does not find three open at once: he watches the boards day by day, settles on the fief of the first open small delivery
        // and does each of that fief's open ones as it is posted, until he has three; the carters compete for the same board, first come by the claim rule)
        int fief = -1; var mine = new List<WorkContract>();
        for (int d = 0; d < 40 && mine.Count < 3; d++)
        {
            var openSmall = s.Contracts!.Contracts.Values.Where(k => k.Open && k.Kind == "deliver_goods" && k.Settlement >= 0 && k.Grams <= 100_000).OrderBy(k => k.Id).ToList();
            // (a fief is settled on only when it has a village besides its seat for the manor of step 3 and its board shows three open small deliveries together: a lone one, as day 0's was, would leave the hero short in a fief where the carters take the rest)
            if (fief < 0) { var pick = openSmall.GroupBy(k => region.Settlements[k.Settlement].FiefIndex).Where(g => g.Key >= 0 && g.Count() >= 3 && region.Fiefs[g.Key].Settlements.Any(x => x != region.Fiefs[g.Key].SeatSettlement)).OrderBy(g => g.Min(k => k.Id)).FirstOrDefault(); if (pick != null) fief = pick.Key; else openSmall.Clear(); }
            foreach (var k in openSmall)
            {
                int kf = region.Settlements[k.Settlement].FiefIndex; if (kf < 0 || mine.Count >= 3 || fief >= 0 && kf != fief) continue;
                var take = p.Do(ContractDesk.Take(k.Id, p.Sec)); if (!take.Ok) continue;
                fief = kf; mine.Add(k);
                ctx.Pack.Create((ushort)k.Good, k.Grams, CreateReason.Import);
                var del = p.Do(ContractDesk.Deliver(k.Id, k.Grams, p.Sec)); Assert.True(del.Ok, del.Text.ToString());
            }
            if (mine.Count < 3) p.Days(1);
        }
        Assert.Equal(3, mine.Count);
        p.Days(2);
        Assert.All(mine, k => Assert.Equal(WorkState.Done, s.Contracts!.Get(k.Id)!.State));
        Assert.Equal(3, PoliticsWiring.ContractsDoneIn(s, hero, fief));
        var fr = s.Rule!.Fiefs[fief]; ulong lord = (ulong)fr.Holder; int realm = fr.Realm;
        output.WriteLine($"fief {fief} ({region.Fiefs[fief].Name}), lord {pt.FullName(pt.IndexOf(new EntityId(lord)))}, realm {realm}; three deliveries done: {string.Join(", ", mine.Select(k => $"#{k.Id} {k.PayMites} mites"))}");

        // 2. fealty: standing 300, renown 100 and the lord's regard 30 (the test's faucets: the standing the deeds of a month would give), then the oath
        var links = s.HomeRecords!.Sim.Get<PoliticsLinks>()!;
        links.StandingWithFief = (_, f) => f == fief ? 300 : 0; links.RenownPm = _ => 100;
        int day = (int)(rt.HourDone / 24);
        Opinions.Add(s.Politics!, s.Rule, lord, hero.Value, "fealty", day, 0, 30);
        var sworn = p.Do(PoliticsDesk.Swear(fief, p.Sec)); Assert.True(sworn.Ok, sworn.Text.ToString());
        Assert.Equal(lord, s.Politics!.OathOf(hero.Value)!.Liege);
        var oath = s.Chronicle.All.Single(c => c.TypeId == "fealty_sworn"); Assert.Equal("ch_note_dp_service_done", oath.Causes[0].NoteKey);

        // 3. a manor: the lord thinks well of his vassal (50), a vacant village of the fief becomes his, and pays him its share of the hearth tax out of the treasury
        Opinions.Add(s.Politics, s.Rule, lord, hero.Value, "fealty", day, 0, 50);
        var manor = p.Do(PoliticsDesk.Manor(fief, p.Sec)); Assert.True(manor.Ok, manor.Text.ToString());
        var manorRow = s.Politics.Manors.Single(m => m.Holder == (long)hero.Value || Opinions.K((ulong)m.Holder) == Opinions.K(hero.Value));
        Assert.Equal(fief, manorRow.OverlordFief); Assert.Contains(s.Chronicle.All, c => c.TypeId == "manor_granted");

        // 4. war service in the realm's war (what a war-aim contract records on completion), then a vacant fief of the realm: the line died out
        var pc = PoliticsEngine.Context(s.HomeRecords.Sim, s.Politics, s.Rule, RuleChron.Of(s.Chronicle, s.Ids), day);
        Road.RecordService(pc, hero, realm);
        Opinions.Add(s.Politics, s.Rule, pc.KeyOf(realm), hero.Value, "granted", day, 0, 20);
        var vacant = s.Rule.Fiefs.Values.First(f => f.Realm == realm && f.Fief != fief && f.Holder >= 0 && !f.Forfeited);
        vacant.Holder = -1; vacant.PopLord = -1; vacant.Receipts = [4000, 4000, 4000, 4000];
        e.Ledger.Mint(pc.R.PurseOf(pc.R.HouseholdOf((long)hero.Value)), Coin.FromCrowns(50), SourceReason.Seed);            // the test's purse for the relief: the hero's household pays it
        seeded = e.Ledger.Audit().Seeded.Mites;
        long relief = RuleMath.Relief(vacant.LastYearReceipts, s.Rule.Settings.ReliefPct);
        var realmPurse = pc.R.PurseOf(Party.OfRealm(realm));
        long realmBefore = e.Ledger.Balance(realmPurse).Mites;
        int fiefsHeld = s.Rule.Fiefs.Count, heldByHero0 = s.Rule.Fiefs.Values.Count(f => f.Holder == (long)hero.Value);          // a fief is a FiefRow standing for its HoldingKind.Fief deed (C3)
        var asked = p.Do(PoliticsDesk.AskFief(vacant.Fief, p.Sec)); Assert.True(asked.Ok, asked.Text.ToString());
        Assert.Equal((long)hero.Value, s.Rule.Fiefs[vacant.Fief].Holder);
        Assert.Contains(s.Chronicle.All, c => c.TypeId == "fief_granted" && c.Causes.Any(z => z.NoteKey == "ch_note_dp_war_service"));
        Assert.Equal(relief, e.Ledger.Balance(realmPurse).Mites - realmBefore);                              // the relief went to the realm's treasury
        Assert.Contains(s.Rule.Dues, d => d.Kind == DueKind.Relief && d.State == DueState.Paid);
        Assert.Equal(heldByHero0 + 1, s.Rule.Fiefs.Values.Count(f => f.Holder == (long)hero.Value)); Assert.Equal(fiefsHeld, s.Rule.Fiefs.Count);        // his fief is a deed: the count is exact
        int mineFief = vacant.Fief;

        // 5. he appoints a reeve and a bailiff of real adults of his fief; the bailiff (the test's faucet: honesty 5) will skim the hearth tax
        int day2 = (int)(rt.HourDone / 24);
        var adults = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] >= 0 && region.Settlements[pt.HomeSettlement[i]].FiefIndex == mineFief && pt.AgeYears(i, day2) is >= 25 and < 55).ToList();
        int treasurerRow = adults.OrderByDescending(i => pt.SkillLevelOf(i, Skill.Law)).ThenBy(i => i).First();                         // the one who knows the law best keeps the books and audits
        pt.SetSkill(treasurerRow, 0, Skill.Law, 80);                                                           // (the test's faucet: a clerk who has read the law, so the audit's chance is its 90 percent cap)
        int bailiffRow = adults.Where(i => i != treasurerRow && pt.Occupation[i] == Occupation.Labourer).OrderBy(i => pt.SkillLevelOf(i, Skill.Law)).ThenBy(i => i).First();
        int reeveRow = adults.First(i => i != treasurerRow && i != bailiffRow && pt.Occupation[i] == Occupation.Labourer);
        foreach (var (kind, row) in new[] { (OfficeKind.Reeve, reeveRow), (OfficeKind.Bailiff, bailiffRow), (OfficeKind.Treasurer, treasurerRow) })
        {
            var ok = p.Do(RuleDesk.Appoint(kind, mineFief, (long)pt.IdOf(row).Value, p.Sec)); Assert.True(ok.Ok, ok.Text.ToString());
        }
        var bailiff = s.Rule.OfficeOf(OfficeKind.Bailiff, mineFief)!;
        s.Rule.Honesty[(ulong)bailiff.Holder] = 5; bailiff.Honesty = 5;                                      // the test's faucet: a dishonest man took the bailiff's post
        output.WriteLine($"audit chance per season {RuleMath.AuditPct(s.Rule.Settings, pt.SkillLevelOf(treasurerRow, Skill.Law), pt.SkillLevelOf(bailiffRow, Skill.Law))}%");

        // 6. two game years: seasons roll, receipts reach the Fief tab, the bailiff skims and the audit finds him; coin and deeds stay exact
        int seasonsRun = 0;
        for (; seasonsRun < 8 && bailiff.InOffice; seasonsRun++) p.Days(15);
        output.WriteLine($"{seasonsRun} seasons: bailiff collected {bailiff.Collected} of {bailiff.Expected}, skimmed {bailiff.Skimmed}, found {bailiff.Found}, in office {bailiff.InOffice}");
        Assert.True(bailiff.Skimmed > 0, "the bailiff skimmed nothing: the fief's hearth tax was not his to collect");
        Assert.False(bailiff.InOffice); Assert.True(bailiff.Found > 0);
        Assert.Contains(s.Chronicle.All, c => c.TypeId == "official_found_skimming");
        Assert.Contains(s.Chronicle.All, c => c.TypeId == "official_appointed" && c.Subjects.Contains(pt.IdOf(bailiffRow)));
        int day3 = (int)(rt.HourDone / 24);
        var rows = RuleLedger.Rows(s, day3);
        Assert.Contains(rows, r => r.Cells[0] == "@ru_row_arrears"); Assert.Contains(rows, r => r.Cells[0].StartsWith("@ru_office", StringComparison.Ordinal));
        if (day3 - (int)(start / 24) < 120) p.Days(120 - (day3 - (int)(start / 24)));
        Assert.InRange((int)(rt.HourDone / 24) - (int)(start / 24), 120, 140);                              // about two game years

        var audit = e.Ledger.Audit();
        Assert.True(audit.IsConserved);
        Assert.Equal(seeded, audit.Seeded.Mites);                                                           // every mite since the faucet of the purse is accounted for (nothing minted by the rule, the relief, the wages or the skim)
        Assert.True(audit.IsConserved);   // P7-CN1: the mints strike the silver the hearths send (Minted is no longer 0)
        Assert.Equal(heldByHero0 + 1, s.Rule.Fiefs.Values.Count(f => f.Holder == (long)hero.Value)); Assert.Equal(fiefsHeld, s.Rule.Fiefs.Count);
        Assert.Equal(mineFief, s.Rule.Fiefs.Values.Single(f => f.Holder == (long)hero.Value).Fief);
        Assert.True(e.MassAuditHolds());
    }
}
