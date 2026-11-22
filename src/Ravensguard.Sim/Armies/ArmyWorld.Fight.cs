using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>What a fight between a warband and a band left (the numbers the chronicle and the tests read).</summary>
public sealed record WarbandFight(bool WarbandWon, int WarbandDead, int BandDead, int BandCaught, int BandLeft);

public sealed partial class ArmyWorld
{
    // ------------------------------------------------------------------ searching for bandits

    /// <summary>
    /// A warband has ridden to the place of the news and searched it (4.1 chase): any band whose lair lies within the search radius of the place is found and fought; none found, the men ride home. The place is the news's, the
    /// lair is the band's own: the news can be stale. Never reads a band the radius does not reach.
    /// </summary>
    public void Search(ArmyEnv e, Warband wb)
    {
        wb.Phase = 1; BanditBand? found = null;
        foreach (var b in Book.Bands.Values)
        {
            if (b.Lair < 0 || b.Members.Count == 0 || DistM(wb.Here, b.Lair) > Rules.SearchRadiusM) continue;
            if (found == null || (ulong)b.Id == wb.TargetId) found = b;
            if ((ulong)b.Id == wb.TargetId) break;
        }
        wb.Stops.Clear();
        if (found != null) FightBand(e, wb, found);
        if (wb.Here != wb.Seat) wb.Stops.Add(wb.Seat);
    }

    /// <summary>The warband against the band: the abstract battle of Phase 4 with the real men of both (the warband's in the soldier's kit, the band's as bandits); the dead are killed with their chain, the beaten who yielded go home, the rest remain.</summary>
    public WarbandFight FightBand(ArmyEnv e, Warband wb, BanditBand band)
    {
        var w = W; var pt = People; int day = (int)e.Day;
        var ob = e.Ob ?? new RegionOutbox(w.Ids);     // a bare call with no outbox: the entries are dropped
        var ab = w.Get<AbstractBattleService>();
        var mine = wb.Members.Where(m => RowOf(m) >= 0 && pt.IsAlive(RowOf(m))).ToList(); var theirs = band.Members.Where(m => RowOf(m) >= 0 && pt.IsAlive(RowOf(m))).ToList();
        if (mine.Count == 0 || theirs.Count == 0) return new WarbandFight(mine.Count > 0, 0, 0, 0, theirs.Count);
        var newsPlace = band.Lair >= 0 ? band.Lair : wb.Here;
        var beganParams = new List<ChronParam> { ChronParam.Count("band", theirs.Count), ChronParam.Place("place", PlaceId(newsPlace)) };
        var cause = new Cause(CauseKind.Raid, null, "", "ch_note_am_chase", beganParams);
        bool won; int wdead = 0, bdead = 0, caught = 0;
        if (ab is { Ready: true })
        {
            var fc = ab.Fight;
            var soldier = FightSetup.KitDef(fc, Rules.FightKit, Rules.FightKit); var guard = FightSetup.KitDef(fc, Rules.GuardKit, Rules.GuardKit); var bandit = FightSetup.KitDef(fc, "bandit_light", "bandit_light");
            ulong seed = w.Seed ^ DetRng.Mix((ulong)wb.Id * 7919UL + (ulong)band.Id * 104729UL + (ulong)day);
            var ours = new List<FighterKit>(); var oursRows = new List<int>();
            foreach (ulong m in mine)
            {
                int r = RowOf(m);
                ours.Add(FightSetup.Roll(fc, pt.Occupation[r] == Occupation.Guard ? guard : soldier, seed, m, pt.FullName(r), 1, 0, 0, FightMath.One, 0) with { Stance = "balanced" }); oursRows.Add(r);
            }
            var bandKits = new List<FighterKit>();
            foreach (ulong m in theirs) { int r = RowOf(m); bandKits.Add(FightSetup.Roll(fc, bandit, seed, m, pt.FullName(r), 0, 0, 0, FightMath.One, 0)); }
            ulong battleId = (ulong)e.Hour * 4096 + ((ulong)band.Id & 4095);
            var setup = FightSetup.Ambush(bandKits, ours, seed, battleId, e.Hour * 3600, "warband_chase", ambusherKind: SideKind.Guards);
            var ledger = w.Get<WoundLedger>();
            WoundBook? BookOf(ulong person) { if (ledger == null) return null; lock (ledger.Sync) return ledger.Find(person); }
            var resolver = new AbstractBattles { BookOf = BookOf, PlaceKey = BattleChronicle.RoadFrom, PlaceId = PlaceId(newsPlace) };
            var rep = resolver.Resolve(setup, fc); var br = rep.Battle!;
            var caughtMen = new List<ulong>();
            foreach (var cmd in rep.Commands)
            {
                int p = pt.IndexOf(new EntityId(cmd.Person)); if (p < 0) continue;
                if (cmd.Kind == BattleCommandKind.Wounds && cmd.Book != null && ledger != null && !cmd.Book.Dead) ledger.Adopt(cmd.Person, cmd.Book, e.Hour * 3600);
                if (cmd.Kind != BattleCommandKind.Status) continue;
                if (cmd.Status == SoldierStatus.Dead && pt.Status[p] == PersonStatus.Alive)
                {
                    var n = br.Notables.FirstOrDefault(x => x.Person == cmd.Person);
                    var book = rep.Commands.FirstOrDefault(x => x.Person == cmd.Person && x.Kind == BattleCommandKind.Wounds)?.Book;
                    var dc = book is { Dead: true } ? WoundSystem.CauseOf(book.Death) : DeathCause.Violence;
                    Demography.Kill(w, p, dc, e.Time, ob, chain: BattleChronicle.DeathChain(n, br, "ch_began_am_chase", beganParams));
                    if (mine.Contains(cmd.Person)) wdead++; else bdead++;
                }
                else if (cmd.Status is SoldierStatus.Captured or SoldierStatus.Surrendered && theirs.Contains(cmd.Person)) caughtMen.Add(cmd.Person);
            }
            won = br.Winner == 1;
            BattleChronicle.Report(ob, e.Time, br, new EntityId(wb.Lord), PlaceId(newsPlace), "ch_side_bandits", "ch_side_am_warband", cause);
            foreach (ulong m in caughtMen.OrderBy(x => x)) { ReleaseBandit(band, m); caught++; }
        }
        else
        {
            // a bare world with no wound tables: the stronger side wins and a fifth of the loser falls, the lowest ids first
            won = mine.Count >= theirs.Count;
            var losers = won ? theirs : mine; int fall = Math.Max(1, losers.Count / 5);
            foreach (ulong m in losers.Take(fall)) { int p = RowOf(m); Demography.Kill(w, p, DeathCause.Violence, e.Time, ob, chain: new Cause(CauseKind.Wound, null, "", "ch_note_am_chase", beganParams)); if (won) bdead++; else wdead++; }
        }
        PruneBand(e, band);
        PruneDead(wb);
        wb.Morale = Math.Clamp(wb.Morale + (won ? 15 : -25), 0, 100);
        return new WarbandFight(won, wdead, bdead, caught, Book.Bands.TryGetValue(band.Id, out var left) ? left.Members.Count : 0);
    }

    // ------------------------------------------------------------------ raids in war

    /// <summary>
    /// A raid (4.1, in war only): the warband rides into an enemy village, takes grain from up to a share of the households' stores into its own baggage (real goods, moved, never made) and rides home to lay it down at the
    /// seat. The chronicle names the war's declaration as its cause; the realm that was raided hears of it (the news its lords garrison against). Returns the grams taken.
    /// </summary>
    public long DoRaid(ArmyEnv e, Warband wb)
    {
        int s = wb.Target; if ((uint)s >= (uint)Region.Settlements.Count) return 0;
        var rb = Rule; var pb = Politics; if (rb == null) return 0;
        int fi = Region.Settlements[s].FiefIndex; int enemy = fi >= 0 && rb.Fiefs.TryGetValue(fi, out var ef) ? ef.Realm : -1;
        var war = pb != null && enemy >= 0 && wb.Realm >= 0 ? pb.WarOf(wb.Realm, enemy) : null;
        wb.Stops.Clear(); if (wb.Here != wb.Seat) wb.Stops.Add(wb.Seat);
        if (war == null) return 0;                                             // no war, no raid
        var ht = Households; var hhs = new List<int>();
        for (int h = 0; h < ht.Count; h++) if (ht.MemberCount[h] > 0 && ht.Settlement[h] == s) hhs.Add(h);
        if (hhs.Count == 0) return 0;
        int robbed = Math.Max(1, hhs.Count * Rules.RaidMaxHouseholdPct / 100);
        int start = (int)(DetRng.Next(W.Seed, e.Day, (ulong)wb.Id, RngStreams.WarbandTies, 2) % (ulong)hhs.Count);
        var bag = BaggageOf(wb.Id); long grams = 0; int n = 0;
        for (int k = 0; k < hhs.Count && n < robbed; k++)
        {
            long got = TakeGrain(hhs[(start + k) % hhs.Count], bag, Rules.RaidKgPerHousehold);
            if (got > 0) { grams += got; n++; }
        }
        wb.LootG = bag.TotalMassG();
        Book.Hear((int)e.Day, s, 0, wb.Realm, wb.Members.Count);
        EntityId declEntry = war.DeclEntry != 0 ? new EntityId(war.DeclEntry) : EntityId.None;
        Chron(e, "warband_raid", "ch_am_raid",
            [ChronParam.Person("lord", new EntityId(wb.Lord)), ChronParam.Place("place", PlaceId(s)), ChronParam.Count("kg", (int)(grams / 1000)), ChronParam.Count("households", n)],
            [new EntityId(wb.Lord), PlaceId(s)], new Cause(CauseKind.War, declEntry == EntityId.None ? null : declEntry, "", "ch_note_am_war_raid"));
        return grams;
    }
}
