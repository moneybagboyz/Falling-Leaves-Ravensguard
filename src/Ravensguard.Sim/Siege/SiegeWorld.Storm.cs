using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Abstract;

namespace Ravensguard.Sim.Siege;

public sealed partial class SiegeWorld
{
    // ------------------------------------------------------------------ the assault

    /// <summary>
    /// The besieger storms (4.3): if the player's bubble can take it (<see cref="OpenAssault"/>) the siege waits in the Assault state for the bubble's end (<see cref="EndAssault"/>; a day after <see cref="SiegeConfig.LiveFallbackHours"/> hours with no answer it is
    /// fought abstractly); else it is fought now by the abstract rule (<see cref="Resolve"/>).
    /// </summary>
    private void Assault(ArmyEnv e, SiegeRow s, ArmyHost host, List<ulong> men, List<ulong> garrison, bool breach, bool walled)
    {
        s.AssaultDay = (int)e.Day; s.Assaults++;
        if (OpenAssault?.Invoke(s) == true) { s.State = SiegeState.Assault; s.LiveSinceHour = e.Hour; return; }
        var (won, lostA, lostD) = Resolve(e, s, men, garrison, breach, walled);
        Finish(e, s, host, won, lostA, lostD, breach);
    }

    /// <summary>A bubble that fought the assault reports its end: who won the place and what each side lost (the dead are already dead in the fight). The siege goes on to storm and sack or back to its blockade.</summary>
    public void EndAssault(ArmyEnv e, SiegeRow s, bool attackersWon, int lostAttackers, int lostDefenders)
    {
        if (s.State != SiegeState.Assault || !Armies.Book.Hosts.TryGetValue(s.Host, out var host)) return;
        s.LiveSinceHour = -1; s.State = SiegeState.Blockade;
        Finish(e, s, host, attackersWon, lostAttackers, lostDefenders, IsBreachOpen(s));
    }

    private void TickLive(ArmyEnv e, SiegeRow s)
    {
        if (s.LiveSinceHour < 0 || e.Hour - s.LiveSinceHour < SiegeConfig.LiveFallbackHours) return;
        if (!Armies.Book.Hosts.TryGetValue(s.Host, out var host)) return;
        s.State = SiegeState.Blockade; s.LiveSinceHour = -1;
        var men = Besiegers(s); var garrison = Garrison(s.Place); bool breach = IsBreachOpen(s);
        var (won, la, ld) = Resolve(e, s, men, garrison, breach, WallsOf?.Invoke(s.Place) != null);
        Finish(e, s, host, won, la, ld, breach);
    }

    /// <summary>
    /// The abstract storm: the besiegers' power is a hundred a man against a defence of 300 a man behind unbroken walls, 150 behind a breach and 120 in an unwalled place. The stronger takes the place; each side loses men in proportion
    /// to the other's share of the power (a quarter of the besiegers at the most; the defenders lose half of what a won place's share gives, a tenth of a held one's), the dead chosen by the siege stream and killed as persons with the siege as the cause.
    /// </summary>
    private (bool Won, int LostA, int LostD) Resolve(ArmyEnv e, SiegeRow s, List<ulong> men, List<ulong> garrison, bool breach, bool walled)
    {
        var (won, lostA, lostD) = AbstractStorm(men.Count, garrison.Count, breach, walled);
        lostA = Slay(e, s, men, lostA, 0); lostD = Slay(e, s, garrison, lostD, 1);
        return (won, lostA, lostD);
    }

    /// <summary>The abstract storm's arithmetic (a pure function, so the live storm can be held to it, LW-1): who takes the place and what each side loses.</summary>
    public static (bool Won, int LostA, int LostD) AbstractStorm(int men, int garrison, bool breach, bool walled)
    {
        long pa = (long)men * SiegeConfig.AttackPower, pd = (long)garrison * (!walled ? SiegeConfig.DefenceOpen : breach ? SiegeConfig.DefenceBreached : SiegeConfig.DefenceWalled);
        bool won = men > 0 && (pa > pd || garrison == 0);          // P7R-3: an emptied host storms nothing
        long share = pa + pd == 0 ? 0 : pd * 1000 / (pa + pd);
        int lostA = (int)Math.Min(men / 4, men * share / SiegeConfig.StormLostAttackersDiv + (garrison > 0 ? 1 : 0));
        int lostD = won ? (int)(garrison * (1000 - share) / SiegeConfig.StormLostDefendersDiv) : garrison / 10;
        return (won, lostA, lostD);
    }

    /// <summary>P7R-16: deaths go to the tick's outbox; a bare call (the live assault's end) drafts into its own and <see cref="Flush"/> writes it to the chronicle, so no death entry is dropped.</summary>
    private RegionOutbox ObOf(ArmyEnv e) => e.Ob ?? new RegionOutbox(W.Ids);

    private void Flush(ArmyEnv e, RegionOutbox ob) { if (e.Ob == null) ob.ApplyTo(W.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal)); }

    /// <summary>P8-LW-1: the men who fell in the live assault are killed as persons (the siege is the cause), the hero spared; returns how many were alive to die.</summary>
    internal int SlayFallen(ArmyEnv e, SiegeRow s, IEnumerable<ulong> persons)
    {
        var pt = Armies.People; var ob = ObOf(e); int dead = 0;
        foreach (ulong m in persons.OrderBy(m => m))
        {
            int r = Armies.RowOf(m); if (r < 0 || pt.Status[r] != PersonStatus.Alive || Hero(m)) continue;
            Demography.Kill(W, r, DeathCause.Violence, e.Time, ob, chain: OnSiege(s, "ch_note_sg_assault")); dead++;
        }
        Flush(e, ob);
        return dead;
    }

    private int Slay(ArmyEnv e, SiegeRow s, List<ulong> from, int n, ulong side)
    {
        if (n <= 0) return 0;
        var pt = Armies.People; var ob = ObOf(e); var order = from.Where(m => !Hero(m)).OrderBy(m => DetRng.Next(W.Seed, e.Day, m, RngStreams.SiegeAiTies, (uint)(1000 + s.Id * 2 + (int)side))).ThenBy(m => m).Take(n).ToList(); int dead = 0;
        foreach (ulong m in order) { int r = Armies.RowOf(m); if (r < 0 || pt.Status[r] != PersonStatus.Alive) continue; Demography.Kill(W, r, DeathCause.Violence, e.Time, ob, chain: OnSiege(s, "ch_note_sg_assault")); dead++; }
        Flush(e, ob);
        return dead;
    }

    /// <summary>The assault's end on the chain: <c>siege_assault</c>, and when the place fell <c>siege_stormed</c> and the sack. A repulsed assault leaves the blockade and the engines as they were; the besiegers' morale pays.</summary>
    private void Finish(ArmyEnv e, SiegeRow s, ArmyHost host, bool won, int lostA, int lostD, bool breach)
    {
        s.LostAttackers += lostA; s.LostDefenders += lostD;
        var outcome = won ? (breach ? "ch_sg_out_breach" : "ch_sg_out_walk") : "ch_sg_out_repulsed";
        var assault = Chron(e, "siege_assault", "ch_sg_assault", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.TextKey("outcome", outcome), ChronParam.Count("lost_a", lostA), ChronParam.Count("lost_b", lostD)], [PlaceId(s.Place)], OnSiege(s, "ch_note_sg_assault"));
        if (!won) { var st = Armies.RationsOf(s.Supply); st.Morale = Math.Max(0, st.Morale - 15); st.DefeatDay = (int)e.Day; return; }
        var stormed = Chron(e, "siege_stormed", "ch_sg_stormed", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.Count("lost_a", s.LostAttackers), ChronParam.Count("lost_b", s.LostDefenders)], [PlaceId(s.Place)], new Cause(CauseKind.Siege, assault, "", "ch_note_sg_stormed"));
        bool sack = !Hero(host.Commander);                                                  // the hero's host does not sack unbidden: his is the choice
        if (sack) Sack(e, s, host, stormed);
        Close(s, SiegeState.Stormed, sack ? "ch_note_sg_sacked" : "ch_note_sg_stormed", stormed, e);
    }

    // ------------------------------------------------------------------ the sack

    /// <summary>
    /// A stormed place is sacked (4.6, overview 10a Q8): each household in it loses a share of its coin to the host's soldiers' purse (ledger transfers) and of its stores to the host's baggage (store moves); one house in five burns, losing half its store (destroyed as
    /// burned); 1 to 5 in a hundred of the people who are not the garrison are killed (stream 0x7C3, the cruelty a draw on the siege and commander, 0 to 4 points); every surviving household bears <c>sacked</c> +10 for a year. Nothing is made: coin and goods move or are destroyed.
    /// Captives are taken only where the law allows them (slavery is a Phase 6 law and no captive rule is wired to sieges yet, so none are taken).
    /// </summary>
    private void Sack(ArmyEnv e, SiegeRow s, ArmyHost host, EntityId stormed)
    {
        var hs = Hs; var econ = W.Economy; var pt = Armies.People; var ht = Armies.Households; var ub = W.Get<UnrestBook>();
        if (hs == null) return; var hid = new HostId(s.Supply);
        var purse = hs.SoldiersPurse(hid); var bag = hs.BaggageOf(hid); var led = econ.Ledger; var cat = econ.Goods;
        int cruel = DetRng.Range(W.Seed, e.Day, host.Commander, RngStreams.SackRolls, (uint)s.Id * 4, 0, 5);                   // 0 to 4 points of cruelty above the 1% floor
        int pm = SiegeConfig.SackKillMinPermille + cruel * SiegeConfig.SackKillSpanPermille / 4;
        var garrison = new HashSet<ulong>(Garrison(s.Place)); var ob = ObOf(e);
        long coin = 0, grams = 0; int burned = 0; var households = new List<int>();
        for (int h = 0; h < ht.Count; h++) if (ht.MemberCount[h] > 0 && ht.Settlement[h] == s.Place) households.Add(h);
        var causeOf = OnSiege(s, "ch_note_sg_sacked"); causeOf = causeOf with { EventId = stormed };
        foreach (int h in households)
        {
            long bal = led.Balance(econ.HouseholdPurse[h]).Mites, take = bal * SiegeConfig.SackPlunderCoinPct / 100;
            if (take > 0 && led.Transfer(econ.HouseholdPurse[h], purse, new Coin(take), TransferReason.Plunder)) coin += take;
            var store = econ.HouseholdStore[h];
            foreach (var (g, q) in store.Rows()) { long mv = q * SiegeConfig.SackPlunderGoodsPct / 100; if (mv > 0) grams += Store.MoveUpTo(store, bag, g, mv) * Math.Max(1, cat.MassG(g, 1)); }
            if (DetRng.Range(W.Seed, e.Day, (ulong)h, RngStreams.SackRolls, (uint)s.Id * 4 + 1, 0, SiegeConfig.BurnOneIn) == 0)
            {
                foreach (var (g, q) in store.Rows()) store.DestroyUpTo(g, q * SiegeConfig.BurnLossPct / 100, DestroyReason.Burned);
                burned++;
            }
        }
        int dead = 0;
        for (int r = 0; r < pt.Count; r++)
        {
            if (pt.Status[r] != PersonStatus.Alive || pt.HomeSettlement[r] != s.Place || garrison.Contains(pt.IdOf(r).Value) || Hero(pt.IdOf(r).Value)) continue;
            if (DetRng.Range(W.Seed, e.Day, pt.IdOf(r).Value, RngStreams.SackRolls, (uint)s.Id * 4 + 2, 0, 1000) >= pm) continue;
            Demography.Kill(W, r, DeathCause.Violence, e.Time, ob, chain: causeOf); dead++;
        }
        Flush(e, ob);
        s.SackDead = dead; s.Burned = burned; s.PlunderMites += coin; s.PlunderG += grams;
        var sacked = Chron(e, "town_sacked", "ch_sg_sacked", [ChronParam.Place("place", PlaceId(s.Place)), ChronParam.Count("dead", dead), ChronParam.Count("burned", burned), ChronParam.Count("taken", 0), ChronParam.Mites("coin", coin)], [PlaceId(s.Place)], causeOf);
        if (ub != null) foreach (int h in households) if (ht.MemberCount[h] > 0) ub.Add(ht.IdOf(h).Value, "sacked", (int)e.Day, sacked.Value);
    }
}
