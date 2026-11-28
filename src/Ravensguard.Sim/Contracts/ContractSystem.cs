using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Contracts;

public enum TakeResult : byte { Taken, NoSuchContract, NotOpen, Hidden, CannotBond, Busy, NoPurse }

/// <summary>
/// The life of a contract (contracts-and-rumours-v2 4.2): posting with the pay moved into escrow, taking (with the bond of an escort or guard), verification from what happened, completion paying the escrow to
/// the taker, failure and expiry returning it to the issuer with consequences. Every coin move is a ledger transfer (<see cref="TransferReason.ContractEscrow"/>, <c>ContractPay</c>, <c>ContractRefund</c>);
/// every contract is a chronicle entry with its cause. Deadlines are absolute game days: a contract lapses as its deadline day begins.
/// </summary>
public static class ContractSystem
{
    public static EntityId EscrowId(int id) => EntityId.Make(IdKind.Other, 0x47_0000_0000_0000UL | (uint)id);
    public static EntityId BondId(int id) => EntityId.Make(IdKind.Other, 0x48_0000_0000_0000UL | (uint)id);
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ pay

    /// <summary>The pay of a kind: <c>base + rate x u1 x u2 / 100</c> (the rate in hundredths of a mite), integer, at least the base. The two units depend on the kind's measure (km and kg; days and guards; days).</summary>
    public static long PayOf(ContractKindDef k, long u1, long u2 = 1)
    {
        long pay = k.PayBase;
        if (k.PayPerUnitX100 > 0) pay += k.PayUnit switch
        {
            "km_kg" or "guard_day" => k.PayPerUnitX100 * Math.Max(1, u1) * Math.Max(1, u2) / 100,
            "mite_pct" => k.PayPerUnitX100 * Math.Max(0, u1) / 10_000,
            "head" or "day" => k.PayPerUnitX100 * Math.Max(1, u1) / 100,
            _ => 0,
        };
        return pay;
    }

    // ------------------------------------------------------------------ the need and the posting

    /// <summary>Writes the entry of a need (the shortage, the band, the captive): the cause of the posting that follows. A root entry (nobody caused a shortage; the world is as it is).</summary>
    public static ChronicleEntry NeedEntry(ContractWorld w, string need, int settlement, long sec, IReadOnlyList<EntityId> subjects, ushort good = ushort.MaxValue, long qty = 0, int days = 0)
    {
        var ps = new List<ChronParam> { ChronParam.TextKey("what", "ct_need_" + need) };
        if (w.PlaceId(settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(settlement)));
        if (good != ushort.MaxValue) { ps.Add(ChronParam.Good("good", good)); ps.Add(ChronParam.Count("grams", qty)); }
        ps.Add(ChronParam.Count("days", days));
        var data = new Dictionary<string, string> { ["need"] = need, ["settlement"] = w.PlaceName(settlement) };
        if (good != ushort.MaxValue) { data["good"] = N(good); data["grams"] = N(qty); }
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), "work_need", subjects, [new Cause(CauseKind.Trade, null, NoteKey: "ch_note_ct_need_" + need)], data, TextKey: "ch_work_need", Params: ps);
        w.Chronicle.Add(e);
        return e;
    }

    /// <summary>
    /// Posts a contract: the pay moves from <paramref name="issuerPurse"/> into a new escrow chest at once, so nothing is posted that cannot be paid (CR-2). Null when the kind is unknown, the pay is nothing, the
    /// issuer cannot pay or the settlement's board is full. <paramref name="cause"/> is the entry of the need that posted it.
    /// </summary>
    public static WorkContract? Post(ContractWorld w, ContractBook book, string kindId, EntityId issuer, PurseId issuerPurse, int settlement, long pay, int day, ChronicleEntry cause, Action<WorkContract>? init = null)
    {
        var k = w.Content.Kind(kindId); if (k == null || pay <= 0) return null;
        int id = book.NextId;
        if (k.JitterPct > 0) pay = Math.Max(1, pay + pay * DetRng.Range(w.Seed, 0, (ulong)id, RngStreams.PayRounding, 0, -k.JitterPct, k.JitterPct + 1) / 100);
        if (w.Ledger.Balance(issuerPurse).Mites < pay) return null;
        if (book.Contracts.Values.Count(x => x.Open && x.Settlement == settlement) >= w.S.BoardCap) return null;
        book.NextId++;
        var escrow = w.Ledger.Open(PurseKind.Chest, EscrowId(id));
        if (!w.Ledger.Transfer(issuerPurse, escrow, new Coin(pay), TransferReason.ContractEscrow)) throw new InvalidOperationException("escrow transfer failed after a balance check");
        var c = new WorkContract { Id = id, Kind = kindId, Issuer = issuer, IssuerPurse = issuerPurse.Index, Escrow = escrow.Index, PayMites = pay, PostedDay = day, DeadlineDay = day + k.DeadlineDays, Settlement = settlement, State = WorkState.Open, CauseEntry = cause.Id.Value };
        init?.Invoke(c);
        var ps = new List<ChronParam> { ChronParam.TextKey("kind", "ct_kind_" + kindId), ChronParam.Mites("pay", pay), ChronParam.Count("deadline", c.DeadlineDay) };
        if (w.PlaceId(settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(settlement)));
        var subjects = new List<EntityId>(); if (issuer != EntityId.None) subjects.Add(issuer);
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + 3600), "work_posted", subjects, [new Cause(CauseKind.Trade, cause.Id)],
            new Dictionary<string, string> { ["contract"] = N(id), ["kind"] = kindId, ["pay"] = N(pay), ["deadline"] = N(c.DeadlineDay), ["why"] = k.Why }, TextKey: "ch_work_posted", Params: ps);
        w.Chronicle.Add(e); c.PostedEntry = e.Id.Value;
        book.Contracts[id] = c;
        return c;
    }

    // ------------------------------------------------------------------ taking

    /// <summary>Whether a person may see a contract: a criminal kind shows only to people the poster trusts (>= 300).</summary>
    public static bool Visible(ContractWorld w, WorkContract c, EntityId viewer)
    {
        var k = w.Content.Kind(c.Kind);
        return k is not { Crime: true } || w.TrustPm(c.Issuer, viewer) >= 300;
    }

    /// <summary>
    /// A person (or a company: <paramref name="byCompany"/>) takes an open contract. A claim like Phase 3's: one taker at a time; the person may hold only one contract. An escort's or guard's bond (a tenth of the pay)
    /// moves from the taker's purse into a bond chest. A delivery's baseline is the target store's count of the good now.
    /// </summary>
    public static TakeResult Take(ContractWorld w, ContractBook book, int id, EntityId taker, PurseId takerPurse, int day, bool byCompany = false)
    {
        var c = book.Get(id); if (c == null) return TakeResult.NoSuchContract;
        if (c.State != WorkState.Open) return TakeResult.NotOpen;
        if (!byCompany && !Visible(w, c, taker)) return TakeResult.Hidden;
        if (book.Contracts.Values.Any(x => x.State == WorkState.Taken && x.Taker == taker && x.Id != id && x.Kind != "tournament_entry")) return TakeResult.Busy;
        var k = w.Content.Kind(c.Kind);
        if (k is { BondPct: > 0 })
        {
            long bond = Math.Max(1, c.PayMites * k.BondPct / 100);
            if (takerPurse.IsNone) return TakeResult.NoPurse;
            if (w.Ledger.Balance(takerPurse).Mites < bond) return TakeResult.CannotBond;
            var chest = w.Ledger.Open(PurseKind.Chest, BondId(id));
            if (!w.Ledger.Transfer(takerPurse, chest, new Coin(bond), TransferReason.ContractEscrow)) return TakeResult.CannotBond;
            c.BondChest = chest.Index; c.BondMites = bond;
        }
        c.State = WorkState.Taken; c.Taker = taker; c.ByCompany = byCompany; c.TakenDay = day;
        if (c.Good >= 0 && c.TargetOwner != EntityId.None) { c.Baseline = w.StoreQty(c.TargetOwner, (ushort)c.Good); c.Delivered = 0; }
        var ps = new List<ChronParam> { ChronParam.TextKey("kind", "ct_kind_" + c.Kind) }; ps.Insert(0, ChronParam.Person("taker", taker));
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + 7200), "work_taken", byCompany ? [] : [taker], [new Cause(CauseKind.Trade, new EntityId(c.PostedEntry))],
            new Dictionary<string, string> { ["contract"] = N(id), ["kind"] = c.Kind, ["hero"] = taker == w.HeroId ? "1" : "0" }, TextKey: "ch_work_taken", Params: ps);
        w.Chronicle.Add(e); c.Evidence.Add(e.Id.Value);
        return TakeResult.Taken;
    }

    /// <summary>The taker puts a contract down: the same as failing it (the bond is charged, the escrow goes back), at the cost of standing.</summary>
    public static bool Abandon(ContractWorld w, ContractBook book, int id, EntityId by, int day)
    {
        var c = book.Get(id);
        if (c == null || c.State != WorkState.Taken || c.Taker != by) return false;
        Fail(w, book, c, day, "abandoned"); return true;
    }

    // ------------------------------------------------------------------ verification (events only)

    /// <summary>Told when goods have been put into a store (the hero's hand-over, a haul): every taken delivery whose target store is this one and whose good this is counts them (CR-4).</summary>
    public static void GoodsReceived(ContractWorld w, ContractBook book, EntityId owner, ushort good, long grams, int day)
    {
        if (grams <= 0) return;
        foreach (var c in book.Contracts.Values)
            if (c.State == WorkState.Taken && c.TargetOwner == owner && c.Good == good) { c.Delivered += grams; }
        Verify(w, book, day);
    }

    /// <summary>The caravan of a contract was lost or raided after the contract was taken (the chronicle says so).</summary>
    private static bool GoodsLost(ContractWorld w, WorkContract c)
    {
        if (c.Target == EntityId.None) return false;
        var all = w.Chronicle.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var e = all[i];
            if (e.When.TotalDays < c.TakenDay) break;
            if (e.TypeId is "caravan_lost" or "caravan_raid" && e.Subjects.Contains(c.Target)) return true;
        }
        return false;
    }

    /// <summary>Whether a taken contract has been done, from what happened (spec 4.2): returns null while it stands, true when done.</summary>
    public static bool? Check(ContractWorld w, WorkContract c, int day)
    {
        var k = w.Content.Kind(c.Kind); if (k == null) return null;
        switch (k.Verify)
        {
            case "store_gain":
            {
                if (c.TargetOwner == EntityId.None || c.Good < 0) return null;
                long now = w.StoreQty(c.TargetOwner, (ushort)c.Good);
                return Math.Max(c.Delivered, Math.Max(0, now - c.Baseline)) >= c.Grams ? true : null;
            }
            case "culprit_taken":
            {
                // the culprit held or dead after the contract was taken (S2: a man already in the gaol, or buried, when it was taken pays nothing)
                if (c.Target == EntityId.None) return null;
                if (w.IsHeld(c.Target) && (w.HeldSince?.Invoke(c.Target) ?? int.MaxValue) >= c.TakenDay) return true;
                return w.IsDead(c.Target) && (w.DeadSince?.Invoke(c.Target) ?? int.MaxValue) >= c.TakenDay ? true : null;
            }
            case "band_gone": return c.Target != EntityId.None && w.BandGone(c.Target) ? true : null;
            case "captive_free":
            {
                // done only when the taker's side freed him (rescued) after the contract was taken; any other ending is moot (see Moot)
                var e = w.CaptiveFree(c.Target);
                return e.Known && !e.Held && e.How == "rescued" && e.ByHero && e.EndDay >= c.TakenDay && w.SideOf(c.Taker, w.HeroId) ? true : null;
            }
            case "job_done":
                // service: the taker has served the days the contract asked (Param) and the goods, if any, are safe
                if (GoodsLost(w, c)) return false;
                return c.Param > 0 && day - c.TakenDay >= c.Param ? true : null;
            case "body_found": return c.Target != EntityId.None && w.IsDead(c.Target) ? true : null;
            default: return null;
        }
    }

    /// <summary>Whether the need behind a taken contract ended in a way the taker did not answer (S2): the captive was sold, ransomed, released, escaped or died, or the culprit was dead before the contract was taken.</summary>
    public static bool Moot(ContractWorld w, WorkContract c)
    {
        var k = w.Content.Kind(c.Kind); if (k == null) return false;
        switch (k.Verify)
        {
            case "captive_free": { var e = w.CaptiveFree(c.Target); return e.Known && !e.Held; }
            case "culprit_taken": return c.Target != EntityId.None && w.IsDead(c.Target) && (w.DeadSince?.Invoke(c.Target) ?? int.MaxValue) < c.TakenDay;
            default: return false;
        }
    }

    /// <summary>The need ended without the taker: the contract closes unanswered (no pay, no fault): the escrow returns to the issuer and the bond to the taker.</summary>
    public static void Void(ContractWorld w, WorkContract c, int day)
    {
        Refund(w, c); ReturnBond(w, c);
        c.State = WorkState.Cancelled; c.ClosedDay = day;
    }

    /// <summary>The hourly check of every taken contract: done ones pay, lost goods fail, moot ones close unanswered. Ascending contract id.</summary>
    public static void Verify(ContractWorld w, ContractBook book, int day)
    {
        foreach (var c in book.Contracts.Values.Where(x => x.State == WorkState.Taken).ToList())
        {
            var r = Check(w, c, day);
            if (r == null && Moot(w, c)) { Void(w, c, day); continue; }
            if (r == true) Complete(w, book, c, day);
            else if (r == false) Fail(w, book, c, day, "goods_lost");
        }
    }

    /// <summary>Marks the hand-in of a carried thing or a report (verify taker_reports) as the taker's own command: done when the kind asks nothing else.</summary>
    public static bool Report(ContractWorld w, ContractBook book, int id, EntityId by, int day)
    {
        var c = book.Get(id); var k = c == null ? null : w.Content.Kind(c.Kind);
        if (c == null || k == null || c.State != WorkState.Taken || c.Taker != by || k.Verify != "taker_reports") return false;
        Complete(w, book, c, day); return true;
    }

    // ------------------------------------------------------------------ closing

    private static PurseId? PayeeOf(ContractWorld w, WorkContract c) => c.ByCompany ? w.Companies().Where(x => x.Id == c.Taker).Select(x => (PurseId?)new PurseId(x.Purse)).FirstOrDefault() : w.PurseOf(c.Taker);

    private static void CloseEntry(ContractWorld w, WorkContract c, string type, string key, int day, long paid, int pm, bool person)
    {
        var ps = new List<ChronParam> { ChronParam.TextKey("kind", "ct_kind_" + c.Kind), ChronParam.Mites("pay", c.PayMites) };
        var subjects = new List<EntityId>();
        if (person) { if (!c.ByCompany) subjects.Add(c.Taker); ps.Insert(0, ChronParam.Person("taker", c.Taker)); }
        if (w.PlaceId(c.Settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(c.Settlement)));
        var causes = new List<Cause> { new(CauseKind.Trade, new EntityId(c.PostedEntry)) };
        var data = new Dictionary<string, string> { ["contract"] = N(c.Id), ["kind"] = c.Kind, ["paid"] = N(paid), ["pm"] = N(pm), ["hero"] = person && c.Taker == w.HeroId ? "1" : "0", ["settlement"] = w.PlaceName(c.Settlement), ["who"] = person ? w.Name(c.Taker) : "" };
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L), type, subjects, causes, data, TextKey: key, Params: ps);
        w.Chronicle.Add(e); c.Evidence.Add(e.Id.Value);
    }

    /// <summary>Done: the escrow pays the taker (a company's pay goes to its purse), the bond comes back, the issuer's group thinks better of him (the deed row reads the entry).</summary>
    public static void Complete(ContractWorld w, ContractBook book, WorkContract c, int day)
    {
        var k = w.Content.Kind(c.Kind); long paid = 0;
        var escrow = new PurseId(c.Escrow); long pay = w.Ledger.Balance(escrow).Mites;
        var payee = PayeeOf(w, c);
        if (pay > 0 && payee is { } to && w.Ledger.Transfer(escrow, to, new Coin(pay), TransferReason.ContractPay)) paid = pay;
        else if (pay > 0) Refund(w, c);
        ReturnBond(w, c);
        c.State = WorkState.Done; c.ClosedDay = day;
        CloseEntry(w, c, "work_done", "ch_work_done", day, paid, k?.SuccessPm ?? 10, true);
    }

    /// <summary>
    /// Failed (the taker lost the goods, gave up or let the deadline pass): the escrow goes back to the issuer and the taker's bond is charged to him (paid to the issuer), and his standing falls twice what success
    /// would have raised it (4.2, CR-5).
    /// </summary>
    public static void Fail(ContractWorld w, ContractBook book, WorkContract c, int day, string why)
    {
        var k = w.Content.Kind(c.Kind);
        Refund(w, c);
        if (c.BondChest >= 0 && w.Ledger.Balance(new PurseId(c.BondChest)).Mites is long bond and > 0)
        {
            var to = c.IssuerPurse >= 0 ? new PurseId(c.IssuerPurse) : PurseId.None;
            if (!to.IsNone) w.Ledger.Transfer(new PurseId(c.BondChest), to, new Coin(bond), TransferReason.ContractRefund);
        }
        c.State = WorkState.Failed; c.ClosedDay = day;
        CloseEntry(w, c, "work_failed", "ch_work_failed", day, 0, 2 * (k?.SuccessPm ?? 10), true);
    }

    /// <summary>Expired unclaimed: the escrow returns to the issuer, on the deadline day exactly.</summary>
    public static void Expire(ContractWorld w, ContractBook book, WorkContract c, int day)
    {
        Refund(w, c);
        c.State = WorkState.Expired; c.ClosedDay = day;
        // (an unanswered posting lapsing is not news: no chronicle entry, the contract row keeps its state and day)
    }

    /// <summary>The issuer takes an open contract down (the need is gone): the escrow returns.</summary>
    public static bool Cancel(ContractWorld w, ContractBook book, int id, int day)
    {
        var c = book.Get(id); if (c == null || c.State != WorkState.Open) return false;
        Refund(w, c); c.State = WorkState.Cancelled; c.ClosedDay = day; return true;
    }

    /// <summary>The arms kinds (P8-WS-2): paid out piece by piece by <c>ArmsSupply</c>, closed by it.</summary>
    public static bool IsArms(string kind) => kind is "arms_supply" or "refit_commission";

    /// <summary>
    /// Done, the escrow already paid out piece by piece by the verifier's own rule (P8-WS-2: arms deliveries and fittings): what is left of it goes back to the issuer, the bond (none) to the taker, and the close is written.
    /// <paramref name="paid"/> is the coin the taker was paid.
    /// </summary>
    public static void CompleteSettled(ContractWorld w, WorkContract c, int day, long paid)
    {
        var k = w.Content.Kind(c.Kind);
        Refund(w, c); ReturnBond(w, c);
        c.State = WorkState.Done; c.ClosedDay = day;
        CloseEntry(w, c, "work_done", "ch_work_done", day, paid, k?.SuccessPm ?? 10, true);
    }

    private static void Refund(ContractWorld w, WorkContract c)
    {
        var escrow = new PurseId(c.Escrow); long left = w.Ledger.Balance(escrow).Mites;
        if (left > 0 && c.IssuerPurse >= 0) w.Ledger.Transfer(escrow, new PurseId(c.IssuerPurse), new Coin(left), TransferReason.ContractRefund);
    }

    private static void ReturnBond(ContractWorld w, WorkContract c)
    {
        if (c.BondChest < 0) return;
        long left = w.Ledger.Balance(new PurseId(c.BondChest)).Mites;
        if (left <= 0) return;
        var to = PayeeOf(w, c);
        if (to is { } p) w.Ledger.Transfer(new PurseId(c.BondChest), p, new Coin(left), TransferReason.ContractRefund);
    }

    /// <summary>
    /// The day's expiries (hour 0): an open contract whose deadline day has begun lapses and its escrow returns to the issuer; a taken one fails (CR-3). Closed ones are dropped after a month. Ascending id.
    /// </summary>
    public static void Expiries(ContractWorld w, ContractBook book, int day)
    {
        foreach (var c in book.Contracts.Values.ToList())
        {
            if (c.Kind == "tournament_entry") continue;
            if (IsArms(c.Kind) && c.Live) continue;                                                                        // the arms service keeps its own deadlines (a late delivery pays less, it does not fail)
            if (c.State == WorkState.Open && day >= c.DeadlineDay)
            {
                Expire(w, book, c, day);
                if (c.Kind == "deliver_goods") w.Haul?.Invoke(c, c.PayMites);      // P8-BL-1: a post nobody took in ten days is withdrawn and its goods go by the seller's own carter, at the posted pay out of the buyer's own purse
            }
            else if (c.State == WorkState.Taken && day >= c.DeadlineDay) { var r = Check(w, c, day); if (r == true) Complete(w, book, c, day); else if (r == null && Moot(w, c)) Void(w, c, day); else Fail(w, book, c, day, "deadline"); }
            else if (c.Closed && c.ClosedDay >= 0 && day - c.ClosedDay > ContractBook.KeepClosedDays) book.Contracts.Remove(c.Id);
        }
    }
}
