using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

public enum CommissionState : byte { Queued, Working, Ready, Collected, Cancelled, Sold }

/// <summary>
/// A commission (crafting-full 3.4, 4.7): a client asks a maker for a recipe he knows, with the client's materials or his. The price is fixed at ordering; half is held in escrow (a chest purse of the
/// commission) and the rest is paid on collection. The client's materials sit in the commission's store, marked his; the made items sit there too (owned by the commission until he collects them in person).
/// </summary>
public sealed class Commission
{
    public EntityId Id, Client, Maker, Business;
    public ushort Recipe;
    public byte[] Choice = [];
    public int Batch = 1;
    public bool MaterialsFromClient;
    public long PriceMites, Escrow, LabourMites, MaterialsMites;
    /// <summary>The value of the client's materials (refunded if the work is sold uncollected).</summary>
    public long ClientMaterialsMites;
    public long QueuedAtHour, DueHour, ReadyHour;
    /// <summary>Work seconds of this commission at the maker's speed, and the queue ahead of it when ordered (the due date's two parts).</summary>
    public long WorkSec, AheadSec;
    public CommissionState State;
    public EntityId Job;
    public int FitTo;
    public List<EntityId> Items = [];
    public bool Open => State is CommissionState.Queued or CommissionState.Working or CommissionState.Ready;
}

/// <summary>Why a commission was refused or what happened to it (for the hero's messages).</summary>
public enum CommissionAnswer : byte { Ok, NotKnown, NoMaker, NoMaterials, CannotAfford, NotHere, NotReady, Gone }

/// <summary>
/// Commissions (crafting-full 4.7, P5-E). <b>Price</b> = labour hours x his wage class (skilled 35, master 50 mites an hour) x 1,450 pm, plus his materials and fuel at his ask when he supplies them.
/// Half goes to escrow at ordering. The job joins his queue: <c>due = queue end + the recipe's hours at his speed</c>, counted in his working hours (10 a day from 08:00). He makes it with his own skill,
/// tools and quality roll (the same <see cref="CraftRunner"/> calls a hero uses), hour by hour in the hourly pass. The client collects in person; after 15 days uncollected the maker may sell it and refunds
/// the client's materials' value. Cancelling before work starts refunds everything; after, the materials used are lost and half the labour is paid. Never instant, never made by anyone but him.
/// Coin moves only through the ledger and goods only between stores, so both audits stay exact.
/// </summary>
public static class Commissions
{
    /// <summary>The wage class of a maker by his skill: master from 60, skilled from 25, else unskilled (Phase 2 hourly wages 50 / 35 / 15).</summary>
    public static int WagePerHour(int makerSkill) => makerSkill >= 60 ? ItemMath.WageMaster : makerSkill >= 25 ? ItemMath.WageSkilled : ItemMath.WageUnskilled;

    /// <summary>Labour of a recipe in whole base hours (the recipe's attended steps, a batch scaled as the work is), rounded up.</summary>
    public static long LabourHours(CraftRecipe r, int batch)
    {
        long sec = 0;
        foreach (var s in r.Steps) if (s.Attended && !s.Conditional) sec += s.Seconds;
        sec = CraftMath.BatchSeconds(sec, batch, r.TimeScalePm);
        return (sec + 3599) / 3600;
    }

    /// <summary>The labour price: <c>hours x wage x margin / 1000</c> (CF-11: 24 h x 50 x 1,450 pm = 1,740 mites).</summary>
    public static long LabourMites(long hours, int wagePerHour, int marginPm = 1450) => hours * wagePerHour * marginPm / 1000;

    /// <summary>Whether an NPC maker knows a recipe: his skill reaches its difficulty (a master of his trade knows its work), and a guild secret only if he is of that guild's trade.</summary>
    public static bool MakerKnows(CraftContext w, CraftRecipe r, EntityId maker)
    {
        if (w.Runner.State.Know.Knows(r, maker, w.Runner.State.Book)) return true;
        if (r.Skill == Skill.None) return false;
        if (w.Runner.State.Book.Level(maker, r.Skill) < r.Difficulty) return false;
        if (r.Guild != null && w.Runner.Db.Guilds.TryGetValue(r.Guild, out var g) && !g.Trades.Contains(w.TradeOf(maker))) return false;
        return true;
    }

    /// <summary>The makers of a settlement who know a recipe and have a site for it, best skill first, then id (the Commission tab's list).</summary>
    public static List<EntityId> Makers(CraftContext w, CraftRecipe r, int settlement, int max = 6)
    {
        var book = w.Runner.State.Book;
        return [.. w.PeopleOf(settlement).Where(p => MakerKnows(w, r, p) && w.SiteOf(p, r) != null)
            .OrderByDescending(p => r.Skill == Skill.None ? 0 : book.Level(p, r.Skill)).ThenBy(p => p).Take(max)];
    }

    /// <summary>Work still queued with a maker, seconds at his speed (the open commissions ahead of a new one).</summary>
    public static long BusySec(CraftState st, EntityId maker, Func<CraftJob, long> leftOf)
    {
        long sec = 0;
        foreach (var c in st.Commissions.Values)
        {
            if (c.Maker != maker || c.State is not (CommissionState.Queued or CommissionState.Working)) continue;
            var j = c.Job != EntityId.None ? st.Get(c.Job) : null;
            sec += j != null && !j.Closed ? leftOf(j) : c.WorkSec;
        }
        return sec;
    }

    /// <summary>The calendar hour at which <paramref name="workSec"/> of a maker's work is done, counting only his working hours (10 a day from 08:00), from <paramref name="fromHour"/>.</summary>
    public static long DueHour(long fromHour, long workSec, int dayStart = 8, int hoursPerDay = 10)
    {
        long workH = (workSec + 3599) / 3600, h = fromHour;
        while (workH > 0)
        {
            int hod = (int)(h % 24);
            if (hod >= dayStart && hod < dayStart + hoursPerDay) workH--;
            h++;
        }
        return h;
    }

    /// <summary>
    /// The price and timing of a commission before ordering (the screen shows it): labour, materials at his ask when he supplies them, fuel, the work seconds at his speed and the due hour.
    /// Null when he does not know the recipe or has no site for it.
    /// </summary>
    public static Commission? Quote(CraftContext w, CraftRecipe r, EntityId client, EntityId maker, bool clientMaterials, long nowSec, int batch = 1, int[]? choice = null)
    {
        if (!MakerKnows(w, r, maker) || w.SiteOf(maker, r) is not { } site) return null;
        var st = w.Runner.State; var actor = w.ActorOf(maker);
        int skill = r.Skill == Skill.None ? 40 : st.Book.Level(maker, r.Skill);
        var plan = w.Runner.Plan(r, actor, site, (Store?)null, batch, choice, hasTarget: true);
        var c = new Commission { Client = client, Maker = maker, Recipe = r.Index, Batch = plan.Batch, MaterialsFromClient = clientMaterials, Choice = (byte[])plan.Choice.Clone() };
        for (int i = 0; i < c.Choice.Length; i++) if (choice != null && i < choice.Length && choice[i] >= 0 && choice[i] < r.Slots[i].Alts.Length) c.Choice[i] = (byte)choice[i];
        c.LabourMites = LabourMites(LabourHours(r, c.Batch), WagePerHour(skill), w.Runner.Db.Settings.CommissionMarginPm);
        long mats = 0, fuel = 0;
        for (int i = 0; i < r.Slots.Length; i++) { var a = r.Slots[i].Alts[c.Choice[i]]; if (!w.Econ.Goods.HasTag(a.Good, "ambient")) mats += w.Ask(a.Good, a.Qty * c.Batch); }
        foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) fuel += w.Ask(s.FuelGood, s.FuelQty * c.Batch);
        c.MaterialsMites = clientMaterials ? 0 : mats + fuel;
        c.ClientMaterialsMites = clientMaterials ? mats + fuel : 0;
        c.PriceMites = c.LabourMites + c.MaterialsMites;
        c.WorkSec = plan.AttendedSeconds;
        c.AheadSec = BusySec(st, maker, j => w.Runner.LeftSec(j));
        long nowHour = nowSec / 3600;
        c.QueuedAtHour = nowHour;
        c.DueHour = DueHour(nowHour, c.AheadSec + c.WorkSec, w.Runner.Db.Settings.MakerDayStartHour, w.Runner.Db.Settings.MakerHoursPerDay);
        return c;
    }

    /// <summary>
    /// Orders it (a barrier command): half the price to escrow, the client's materials (when he supplies them) from <paramref name="clientSources"/> into the commission's store. Nothing changes on a refusal.
    /// </summary>
    public static (CommissionAnswer Answer, Commission? C) Order(CraftContext w, CraftRecipe r, EntityId client, EntityId maker, bool clientMaterials, CraftSources clientSources, long nowSec, IdAllocator ids,
        int batch = 1, int[]? choice = null, int fitTo = 0)
    {
        var c = Quote(w, r, client, maker, clientMaterials, nowSec, batch, choice);
        if (c == null) return (CommissionAnswer.NotKnown, null);
        if (w.PurseOf(client) is not { } cp || w.PurseOf(maker) is null) return (CommissionAnswer.NoMaker, null);
        long half = c.PriceMites / 2;
        if (w.Ledger.Balance(cp).Mites < half) return (CommissionAnswer.CannotAfford, null);
        // the client's materials must all be there before anything moves
        var needs = new List<(ushort Good, long Qty)>();
        if (clientMaterials)
        {
            for (int i = 0; i < r.Slots.Length; i++) { var a = r.Slots[i].Alts[c.Choice[i]]; if (!w.Econ.Goods.HasTag(a.Good, "ambient")) needs.Add((a.Good, a.Qty * c.Batch)); }
            foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) needs.Add((s.FuelGood, s.FuelQty * c.Batch));
            var sum = new Dictionary<ushort, long>();
            foreach (var (g, q) in needs) sum[g] = sum.GetValueOrDefault(g) + q;
            foreach (var (g, q) in sum) if (clientSources.Qty(g) < q) return (CommissionAnswer.NoMaterials, null);
        }
        c.Id = ids.Allocate(IdKind.Other); c.FitTo = fitTo;
        var escrow = w.Ledger.Open(PurseKind.Chest, c.Id);
        if (half > 0 && !w.Ledger.Transfer(cp, escrow, new Coin(half), TransferReason.Trade)) return (CommissionAnswer.CannotAfford, null);
        c.Escrow = half;
        var store = w.Econ.OpenStore(StoreKind.House, c.Id, 0, 0, false);
        foreach (var (g, q0) in needs)
        {
            long q = q0;
            foreach (var src in clientSources) { long t = Math.Min(q, src.Store.Qty(g)); if (t > 0 && Store.Move(src.Store, store, g, t)) q -= t; if (q == 0) break; }
        }
        w.Runner.State.Commissions[c.Id] = c;
        return (CommissionAnswer.Ok, c);
    }

    /// <summary>
    /// The hourly pass (crafting-full 4.10: H2 for abstract makers): each maker works his oldest open commission during his working hours, an hour at a time, with the same runner calls a hero uses;
    /// a finished one is Ready; one Ready for 15 days is sold (its items become his, the client's materials' value is refunded from his purse). Returns the commissions that changed, in id order.
    /// </summary>
    public static List<Commission> Hourly(CraftContext w, long hour)
    {
        var st = w.Runner.State; var set = w.Runner.Db.Settings; var changed = new List<Commission>();
        int hod = (int)(hour % 24);
        bool working = hod >= set.MakerDayStartHour && hod < set.MakerDayStartHour + set.MakerHoursPerDay;
        var busy = new HashSet<EntityId>();
        foreach (var c in st.Commissions.Values.ToList())
        {
            if (c.State == CommissionState.Ready && hour >= c.ReadyHour + set.CommissionWindowDays * 24L) { SellUncollected(w, c); changed.Add(c); continue; }
            if (!working || c.State is not (CommissionState.Queued or CommissionState.Working) || !busy.Add(c.Maker)) continue;
            var r = w.Runner.Db[c.Recipe];
            if (c.State == CommissionState.Queued)
            {
                var site = w.SiteOf(c.Maker, r); if (site == null) continue;
                var srcs = new CraftSources();
                if (w.Econ.OtherStores.TryGetValue(c.Id, out var cs0)) srcs.Add(new CraftSource { Store = cs0, Id = c.Id, Name = "commission" });
                if (!c.MaterialsFromClient && w.StoreOf(c.Maker) is { } ms) srcs.Add(new CraftSource { Store = ms, Id = c.Maker, Name = "maker" });
                var plan = w.Runner.Plan(r, w.ActorOf(c.Maker), site, srcs, c.Batch, [.. c.Choice.Select(x => (int)x)], hasTarget: true);
                if (!plan.CanStart) continue;            // he lacks the materials or the tools: it waits in his queue
                var job = w.Runner.Start(plan, w.ActorOf(c.Maker), c.Id, srcs, hour * 3600, 0, 0, default, c.FitTo);
                if (job == null) continue;
                job.Commission = c.Id; c.Job = job.Id; c.State = CommissionState.Working; changed.Add(c);
            }
            var j = st.Get(c.Job);
            if (j == null) continue;
            if (j.State == CraftJobState.Waiting && hour * 3600 >= j.ReadySec) w.Runner.Collect(j, w.ActorOf(c.Maker), hour * 3600);
            if (j.State == CraftJobState.Active) w.Runner.Advance(j, 3600, hour * 3600);
            if (j.Closed)
            {
                c.State = CommissionState.Ready; c.ReadyHour = hour + 1;
                changed.Add(c);
            }
        }
        return changed;
    }

    /// <summary>
    /// The client collects in person (E on the maker or his counter): only the client, only in the maker's settlement, only when Ready. The rest of the price goes to the maker with the escrow;
    /// the items become the client's and the goods move to <paramref name="to"/>.
    /// </summary>
    public static CommissionAnswer Collect(CraftContext w, Commission c, EntityId who, int whereSettlement, Store to)
    {
        if (who != c.Client) return CommissionAnswer.NotHere;
        if (c.State != CommissionState.Ready) return c.Open ? CommissionAnswer.NotReady : CommissionAnswer.Gone;
        if (whereSettlement < 0 || whereSettlement != w.SettlementOf(c.Maker)) return CommissionAnswer.NotHere;
        if (w.PurseOf(c.Client) is not { } cp || w.PurseOf(c.Maker) is not { } mp) return CommissionAnswer.NoMaker;
        long rest = c.PriceMites - c.Escrow;
        if (w.Ledger.Balance(cp).Mites < rest) return CommissionAnswer.CannotAfford;
        var escrow = w.Ledger.Open(PurseKind.Chest, c.Id);
        if (c.Escrow > 0) w.Ledger.Transfer(escrow, mp, new Coin(c.Escrow), TransferReason.Trade);
        if (rest > 0) w.Ledger.Transfer(cp, mp, new Coin(rest), TransferReason.Trade);
        foreach (var id in c.Items) if (w.Econ.Items.IsLive(id)) w.Econ.Items.Get(id).Owner = c.Client;
        if (w.Econ.OtherStores.TryGetValue(c.Id, out var s)) { foreach (var (g, q) in s.Rows().ToList()) Store.Move(s, to, g, q); if (s.RowCount == 0) w.Econ.OtherStores.Remove(c.Id); }
        c.State = CommissionState.Collected;
        return CommissionAnswer.Ok;
    }

    /// <summary>
    /// Cancels: before work starts everything comes back (escrow and materials); after, the materials used are lost (the work in hand is the maker's) and half the labour is paid from the escrow,
    /// the rest of it back to the client.
    /// </summary>
    public static CommissionAnswer Cancel(CraftContext w, Commission c, EntityId who, Store to)
    {
        if (who != c.Client) return CommissionAnswer.NotHere;
        if (c.State is not (CommissionState.Queued or CommissionState.Working)) return CommissionAnswer.Gone;
        if (w.PurseOf(c.Client) is not { } cp || w.PurseOf(c.Maker) is not { } mp) return CommissionAnswer.NoMaker;
        var escrow = w.Ledger.Open(PurseKind.Chest, c.Id);
        long toMaker = c.State == CommissionState.Working ? Math.Min(c.Escrow, c.LabourMites / 2) : 0;
        if (toMaker > 0) w.Ledger.Transfer(escrow, mp, new Coin(toMaker), TransferReason.Trade);
        if (c.Escrow - toMaker > 0) w.Ledger.Transfer(escrow, cp, new Coin(c.Escrow - toMaker), TransferReason.Trade);
        if (c.State == CommissionState.Working && w.Runner.State.Get(c.Job) is { } j)
        {
            // the work in hand stays with the maker
            var dest = w.StoreOf(c.Maker);
            if (w.Econ.OtherStores.TryGetValue(j.Id, out var js) && dest != null) { foreach (var (g, q) in js.Rows().ToList()) Store.Move(js, dest, g, q); }
            if (js != null && js.RowCount == 0) w.Econ.OtherStores.Remove(j.Id);
            w.Runner.State.Remove(j.Id);
        }
        if (w.Econ.OtherStores.TryGetValue(c.Id, out var s)) { foreach (var (g, q) in s.Rows().ToList()) Store.Move(s, to, g, q); if (s.RowCount == 0) w.Econ.OtherStores.Remove(c.Id); }
        c.Escrow = 0; c.State = CommissionState.Cancelled;
        return CommissionAnswer.Ok;
    }

    /// <summary>Fifteen days uncollected: the maker keeps the escrow and may sell the work (its items become his), and refunds the value of the client's materials from his purse as far as it goes.</summary>
    private static void SellUncollected(CraftContext w, Commission c)
    {
        var escrow = w.Ledger.Open(PurseKind.Chest, c.Id);
        if (w.PurseOf(c.Maker) is { } mp)
        {
            if (c.Escrow > 0) w.Ledger.Transfer(escrow, mp, new Coin(c.Escrow), TransferReason.Trade);
            if (w.PurseOf(c.Client) is { } cp && c.ClientMaterialsMites > 0)
                w.Ledger.Transfer(mp, cp, new Coin(Math.Min(c.ClientMaterialsMites, w.Ledger.Balance(mp).Mites)), TransferReason.Trade);
        }
        foreach (var id in c.Items) if (w.Econ.Items.IsLive(id)) w.Econ.Items.Get(id).Owner = c.Maker;
        if (w.Econ.OtherStores.TryGetValue(c.Id, out var s) && w.StoreOf(c.Maker) is { } dest) { foreach (var (g, q) in s.Rows().ToList()) Store.Move(s, dest, g, q); if (s.RowCount == 0) w.Econ.OtherStores.Remove(c.Id); }
        c.Escrow = 0; c.State = CommissionState.Sold;
    }
}
