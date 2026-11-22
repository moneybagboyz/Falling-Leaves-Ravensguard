using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>
/// What becomes of a works whose owner has died or left the region (P7-EA3e, EA_F20). It no longer closes with its owner: it passes by the rules a business and a house follow. (1) The owner's will names an heir, else the law's order of kin
/// (<see cref="EstateSettler.Heirs"/>: sons, daughters, the spouse, siblings, parents), the first of them alive, of management age and owning no works. (2) With none, the works is put up for sale: the richest household of the fief whose head can pay the
/// price (half the buildings' bill) buys it; the price goes to the household of the law's first heir if there is one, else to the fief's treasury. (3) With no buyer it escheats to the fief's lord. (4) Only a fief with no living lord closes it.
/// The works keeps its stages, crews, stores and cash box through all of it: the ownership of every stage's business changes and nothing else, so no coin or good is created or lost.
/// </summary>
public sealed partial class IndustryWorld
{
    /// <summary>The will's heir of a dead owner for a thing named by its id (the estates' will book through the labour market's hook); null or -1: none.</summary>
    public Func<int, EntityId, int>? WillHeir { get; set; }
    /// <summary>The children of a person (the sim's kin graph); null: the person table's Father and Mother columns.</summary>
    public Func<int, IEnumerable<int>>? ChildrenOf { get; set; }

    /// <summary>Counters of what happened to the works of owners who were gone (diagnostics, not saved).</summary>
    public int PassedToHeir, PassedBySale, PassedToLord, ClosedOrphaned;

    /// <summary>What the sale asks, in permille of the buildings' bill.</summary>
    private const int SalePricePm = 500;

    private bool FitOwner(int c, int day)
        => c >= 0 && c < People.Count && People.Status[c] is PersonStatus.Alive or PersonStatus.Hero
           && People.AgeYears(c, day) is >= LabourMarket.ManagementMinAge and < LabourMarket.RetireAge && !OwnsWorks(c);

    /// <summary>The price a works is sold for: half the dearer of what its buildings cost and the chain's first tier at base prices.</summary>
    public long SalePriceOf(Works w) => Math.Max(w.BuildingsCostMites, BaseBillMites(w.Chain)) * SalePricePm / 1000;

    private void PassWorks(Works w, int day, GameTime now)
    {
        int dead = w.Owner; bool died = People.Status[dead] == PersonStatus.Dead;
        var laws = Ravensguard.Sim.Succession.EstateSettler.Heirs(People, dead, day, ChildrenOf is { } co ? p => co(p).OrderBy(c => c) : null);
        int heir = -1;
        int will = WillHeir?.Invoke(dead, new EntityId(w.Id)) ?? -1;
        if (FitOwner(will, day)) heir = will;
        else foreach (var h in laws) if (FitOwner(h.Row, day)) { heir = h.Row; break; }
        if (heir >= 0) { Reown(w, heir); PassedToHeir++; WritePassed(w, now, "works_inherited", "ch_ind_inherited", dead, heir, died, "ch_note_works_owner_died", "ch_note_works_owner_left", 0); return; }

        var region = Prod.Pop.Region; int fief = region.Settlements[w.Settlement].FiefIndex;
        long price = SalePriceOf(w);
        int buyerHh = price > 0 ? FindBuyer(w, price, fief, day) : -1;
        if (buyerHh >= 0)
        {
            var econ = Prod.Econ; PurseId to = default; bool escheat = true, found = false;
            foreach (var h in laws) if (People.Status[h.Row] == PersonStatus.Alive && People.Household[h.Row] >= 0) { to = econ.HouseholdPurse[People.Household[h.Row]]; escheat = false; found = true; break; }
            if (!found && fief >= 0 && Led.TryFind(PurseKind.Treasury, region.Fiefs[fief].Id, out var tr)) { to = tr; found = true; }
            if (found && Led.Transfer(econ.HouseholdPurse[buyerHh], to, new Coin(price), escheat ? TransferReason.Escheat : TransferReason.Trade))
            {
                int buyer = Prod.Pop.Households.Head[buyerHh];
                Reown(w, buyer); PassedBySale++;
                WritePassed(w, now, "works_sold", "ch_ind_sold", dead, buyer, died, "ch_note_works_no_heir", "ch_note_works_no_heir", price);
                return;
            }
        }
        int lord = fief >= 0 && fief < Prod.Pop.FiefLord.Length ? Prod.Pop.FiefLord[fief] : -1;
        if (lord >= 0 && lord != dead && People.Status[lord] == PersonStatus.Alive)
        {
            Reown(w, lord); PassedToLord++;
            WritePassed(w, now, "works_escheated", "ch_ind_escheated", dead, lord, died, "ch_note_works_no_heir", "ch_note_works_no_heir", 0);
            return;
        }
        CloseWorks(w); ClosedOrphaned++;
        var who = People.IdOf(dead); var place = region.Settlements[w.Settlement].Id;
        Chron.Chron(now, "works_closed", "ch_ind_closed", [ChronParam.Person("owner", who), ChronParam.TextKey("chain", "ind_chain_" + w.Chain), ChronParam.Place("place", place)],
            [who, new EntityId(w.Id)], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_works_closed")]);
    }

    /// <summary>The household of the fief that buys: head alive, 18 to 64, no works of his own, purse over twice the price; the richest, ties to the lower household. -1: none.</summary>
    private int FindBuyer(Works w, long price, int fief, int day)
    {
        var hht = Prod.Pop.Households; var region = Prod.Pop.Region; var econ = Prod.Econ;
        int best = -1; long bestBal = 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Settlement[h] >= region.Settlements.Count || region.Settlements[hht.Settlement[h]].FiefIndex != fief) continue;
            int head = hht.Head[h];
            if (head < 0 || head >= People.Count || People.Status[head] != PersonStatus.Alive || head == w.Owner) continue;
            if (People.AgeYears(head, day) is < 18 or >= LabourMarket.RetireAge || OwnsWorks(head)) continue;
            long bal = Led.Balance(econ.HouseholdPurse[h]).Mites;
            if (bal < price * 2 || bal <= bestBal) continue;
            best = h; bestBal = bal;
        }
        return best;
    }

    /// <summary>Hands the works to a new owner: every stage's business changes owner (and the owner's household), a crew member who is the new owner leaves the staff, the staff's employer follows.</summary>
    private void Reown(Works w, int owner)
    {
        int old = w.Owner; int hh = People.Household[owner];
        w.Owner = owner;
        foreach (var s in w.Stages)
        {
            if (Prod.ById(new EntityId(s.Business)) is not { } b) continue;
            int at = b.Staff.IndexOf(owner); if (at >= 0) { Prod.DropStaff(b, at, alive: true, keepJob: true); People.Employer[owner] = -1; }
            b.Owner = owner; b.OwnerHousehold = hh; b.OwnerWorks = false;
            foreach (int p in b.Staff) if (People.Employer[p] == old || People.Employer[p] < 0) People.Employer[p] = owner;
        }
    }

    private void WritePassed(Works w, GameTime now, string type, string text, int dead, int to, bool died, string diedNote, string leftNote, long price)
    {
        var from = People.IdOf(dead); var next = People.IdOf(to); var place = Prod.Pop.Region.Settlements[w.Settlement].Id;
        var ps = new List<ChronParam> { ChronParam.Person("owner", from), ChronParam.Person("heir", next), ChronParam.TextKey("chain", "ind_chain_" + w.Chain), ChronParam.Place("place", place) };
        if (price > 0) ps.Add(ChronParam.Mites("price", price));
        Chron.Chron(now, type, text, ps, [from, next, new EntityId(w.Id)],
            [new Cause(died ? CauseKind.Death : CauseKind.Migration, null, NoteKey: died ? diedNote : leftNote)]);
    }
}
