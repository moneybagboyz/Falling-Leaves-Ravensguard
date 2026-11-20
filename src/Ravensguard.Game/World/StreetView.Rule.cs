using System;
using System.Linq;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of rule and income (P6-C3, rule-and-income 6): the Ledger's Fief tab is built in <see cref="RuleLedger.With"/> from the rule book at the same barrier as the rest of the Ledger. This file holds only the
/// dev shot (--ng-shot rule): the hero is made lord of the fief he stands in, a skimming bailiff, a relief in arrears and a loan are staged so the tab has something to show.
/// </summary>
public partial class StreetView
{
    /// <summary>Dev: makes the hero the holder of his fief with a thief for a bailiff, an overdue relief and an open loan, and opens the Ledger on the Fief tab.</summary>
    public void DebugRule()
    {
        if (Runtime is not { } rt || _heroLink == null || rt.State.Rule is not { } book || rt.State.HomeRecords is not { } home) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        int day = (int)(HeroNow() / 86400);
        rt.AtBarrier(() =>
        {
            var hero = rt.State.Party!.Hero; var sim = home.Sim; var c = RuleWiring.Context(sim, book, RuleChron.Of(rt.State.Chronicle, rt.State.Ids));
            int s = Math.Max(0, HeroSettlement()); int fief = c.Region.Settlements[Math.Min(s, c.Region.Settlements.Count - 1)].FiefIndex; if (fief < 0) fief = book.Fiefs.Keys.First();
            var f = book.Fiefs[fief]; f.Holder = (long)hero.Value; f.Forfeited = false;
            var bailiff = book.OfficeOf(OfficeKind.Bailiff, fief); if (bailiff != null) { bailiff.Honesty = 20; bailiff.Collected = 41_300; bailiff.Expected = 61_000; bailiff.Skimmed = 4_100; }
            var realm = Party.OfRealm(Math.Max(0, f.Realm));
            RuleDues.AddDue(c, DueKind.Relief, Party.OfFief(fief), realm, 10_000, fief, day - 5, EntityId.None).State = DueState.Arrears;
            RuleDues.AddDue(c, DueKind.Aid, Party.OfFief(fief), realm, 6_400, 0, day + 12, EntityId.None);
            var hht = rt.State.Population.Households;
            var lender = hht.IdOf(Enumerable.Range(0, hht.Count).First(h => hht.MemberCount[h] > 0));
            book.Loans.Add(new Loan { Serial = book.NextLoan++, Lender = Party.OfHousehold(lender), Borrower = Party.OfFief(fief), PrincipalMites = 20_000, RatePmPerSeason = 40, StartDay = day - 20, TermSeasons = 4, SeasonsPaid = 1, State = LoanState.Open });
            return 0;
        }).GetAwaiter().GetResult();
        RefreshBusiness();
        OpenLedger(LedgerTab.Fief);
    }
}
