using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>
/// P7-IN2 (industries 5.2, 5.3, 5.6, 5.7, 5.9): what a stage's premises make of it. Tiers are recognised from the pieces standing in the stage's site (<see cref="Recognise"/>), a machine's share of the day's power comes from the
/// premises' power net (<see cref="MachinePm"/>), upgrades are the next tier's plan placed as ghosts (the desk) and counted when the last piece is built, and the season, the accident, the kiln, the finery, the drying days and the packing rules live here.
/// </summary>
public sealed partial class IndustryWorld
{
    /// <summary>The construction book the pieces are read from (null: no premises are ever recognised, a stage keeps the tier its owner set, as in IN1).</summary>
    public ConstructionWorld? Build { get; set; }
    /// <summary>The power rules whose nets feed the stages' machines (null: machines always give their full share).</summary>
    public PowerWorld? Power { get; set; }
    /// <summary>River-high days of a game day (a flooded mine or pit stops); null: never.</summary>
    public Func<long, int>? RiverHighDays { get; set; }
    /// <summary>Hurts a worker at a stage (the wound book): person row, the height of the fall the blow is worked out as, the event key, game second; returns whether he died. Null: accidents leave no wound (a world without a wound book).</summary>
    public Func<int, int, ulong, long, bool>? Injure { get; set; }
    /// <summary>A forced accident (tests): true makes the first worker of the stage's crew have one on the stage's day.</summary>
    public Func<bool>? ForceAccident { get; set; }
    /// <summary>A forced kiln loss in permille (tests); null: the draw of stream 0x742.</summary>
    public Func<int>? ForceLossPm { get; set; }

    // ------------------------------------------------------------------ recognition (5.2)

    /// <summary>The built pieces standing in a stage's site, by piece id (fixtures only; a piece worn to 0 hit points no longer counts).</summary>
    public Dictionary<string, int> CountPieces(WorkStage s)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (Build == null || !s.HasSite) return counts;
        foreach (var b in Build.Book.Built.Values)
        {
            if (b.Slot != GhostSlot.Fixture || b.Hp <= 0 || b.X < s.SiteX0 || b.X > s.SiteX1 || b.Y < s.SiteY0 || b.Y > s.SiteY1) continue;
            counts[b.Piece] = counts.GetValueOrDefault(b.Piece) + 1;
        }
        return counts;
    }

    /// <summary>The highest tier whose recognise clauses (and every lower tier's) the pieces meet; tier 1 needs nothing.</summary>
    public static int RecognisedTier(StageKind k, IReadOnlyDictionary<string, int> counts)
    {
        int tier = 1;
        for (int u = 2; u <= k.TierCount; u++)
        {
            if (k.Tier(u).Clauses.All(c => c.Met(counts))) tier = u; else break;
        }
        return tier;
    }

    /// <summary>What the premises still lack for a tier, as clause texts ("crusher_rolls", "mine_rail x8"): the upgrade view's list.</summary>
    public List<string> Missing(WorkStage s, int tier)
    {
        var counts = CountPieces(s); var k = KindOf(s); var miss = new List<string>();
        for (int u = 2; u <= Math.Min(tier, k.TierCount); u++)
            foreach (var c in k.Tier(u).Clauses) if (!c.Met(counts)) miss.Add(c.ToString());
        return miss;
    }

    /// <summary>The recognition of a works' stages for a day (every stage with premises): the tier follows the pieces up and down the same day; a rise writes <c>works_upgraded</c>, clears the upgrade in hand and adds the plan's bill to the buildings' cost.</summary>
    private void RecogniseAll(Works w, int day, GameTime now)
    {
        if (Build == null) return;
        foreach (var s in w.Stages)
        {
            if (!s.HasSite) continue;
            var k = KindOf(s);
            int tier = RecogniseWithPieces(k, s);
            if (tier == s.Tier) continue;
            int old = s.Tier;
            SetTier(s.Business, tier);
            if (s.UpgradeTo != 0 && tier >= s.UpgradeTo) { s.UpgradeTo = 0; s.UpgradeSite = 0; }
            if (tier > old)
            {
                for (int u = old + 1; u <= tier; u++) w.BuildingsCostMites += PlanBill(k.Tier(u).Plan);
                WriteUpgraded(w, s, k, tier, now);
            }
        }
    }

    private int RecogniseWithPieces(StageKind k, WorkStage s) => RecognisedTier(k, CountPieces(s));

    /// <summary>A plan's bill in mites: its pieces' materials at the goods' base prices and its worker-hours at the settings' labour rate (5.3a), the rope left to the audit.</summary>
    public long PlanBill(string planId)
    {
        if (Build == null || planId.Length == 0) return 0;
        var g = Build.Plans().Grid(planId); if (g == null) return 0;
        var cells = PlanLibrary.Cells(g, Build.Costs);
        var (milliHours, grams) = Build.Costs.Sum(cells.Select(c => c.Piece));
        long mites = milliHours * Set.PlanLabourMites / 1000;
        foreach (var (good, q) in grams)
            if (Prod.Econ.Goods.TryIdOf(good, out var id) && good != "rope") mites += q * Prod.Econ.Goods.BasePriceMilliMites(id) / 1_000_000;
        return mites;
    }

    /// <summary>Sets a stage's premises (the rectangle of the home region its tier is recognised from) and recognises it at once. At most <see cref="IndustrySettingsDef.SiteMaxM"/> metres each way.</summary>
    public OwnResult SetSite(ulong business, int x0, int y0, int x1, int y1)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        if (x1 < x0 || y1 < y0 || x1 - x0 >= Set.SiteMaxM || y1 - y0 >= Set.SiteMaxM) return OwnResult.No("in_site_bad");
        f.S.SiteX0 = x0; f.S.SiteY0 = y0; f.S.SiteX1 = x1; f.S.SiteY1 = y1;
        if (Build != null) SetTier(business, RecogniseWithPieces(f.K, f.S));
        return OwnResult.Yes("in_site_set");
    }

    /// <summary>The tier an upgrade would build and the plan that makes it, or null when the stage is at its top tier.</summary>
    public (int Tier, string Plan)? NextUpgrade(WorkStage s)
    {
        var k = KindOf(s);
        if (s.Tier >= k.TierCount) return null;
        return (s.Tier + 1, k.Tier(s.Tier + 1).Plan);
    }

    /// <summary>Notes that an upgrade is in hand: the stage's site grows by the plan's footprint (so its pieces count when built) and the target tier is set.</summary>
    public OwnResult BeginUpgrade(ulong business, int planX, int planY, int planW, int planH, long constructionSite)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        var next = NextUpgrade(f.S); if (next == null) return OwnResult.No("in_top_tier");
        f.S.SiteX0 = Math.Min(f.S.SiteX0, planX); f.S.SiteY0 = Math.Min(f.S.SiteY0, planY);
        f.S.SiteX1 = Math.Max(f.S.SiteX1, planX + planW - 1); f.S.SiteY1 = Math.Max(f.S.SiteY1, planY + planH - 1);
        f.S.UpgradeTo = next.Value.Tier; f.S.UpgradeSite = constructionSite;
        return OwnResult.Yes("in_upgrade_begun", next.Value.Tier);
    }

    private void WriteUpgraded(Works w, WorkStage s, StageKind k, int tier, GameTime now)
    {
        var who = People.IdOf(w.Owner); var place = Prod.Pop.Region.Settlements[w.Settlement].Id;
        Chron.Chron(now, "works_upgraded", "ch_ind_upgraded",
            [ChronParam.Person("owner", who), ChronParam.Place("place", place), ChronParam.TextKey("stage", "ind_stage_" + k.Id), ChronParam.Count("tier", tier)],
            [who, new EntityId(s.Business)], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_stage_upgraded")]);
    }

    // ------------------------------------------------------------------ the machines' share (4.1, 5.2)

    /// <summary>
    /// A stage's machine share in permille, the premises' power net (<c>large-buildings.md</c> 4.1): for each machine piece the recognised tier asks for, the best effect among those standing in the site on a net (the net's share times the machine's condition); the least of them. 1,000 for a stage
    /// without premises, machines or a power world; 0 when a machine it needs is on no net.
    /// </summary>
    public int MachinePm(Works w, WorkStage s)
    {
        if (Power == null || !s.HasSite) return 1000;
        var k = KindOf(s); var t = k.Tier(s.Tier);
        if (t.PowerW <= 0) return 1000;
        var need = new List<string>();
        var counts = CountPieces(s);
        for (int u = 1; u <= s.Tier; u++)
            foreach (var c in k.Tier(u).Clauses)
                foreach (var (piece, n) in c.Any)
                    if (counts.GetValueOrDefault(piece) >= n && Power.Cat.Get(piece)?.Role == "machine" && !need.Contains(piece)) need.Add(piece);
        if (need.Count == 0) return 1000;
        var best = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in Power.Nets)
            foreach (var m in n.Machines)
            {
                var c = m.Cell;
                if (c.X < s.SiteX0 || c.X > s.SiteX1 || c.Y < s.SiteY0 || c.Y > s.SiteY1) continue;
                int e = n.EffectPm(m, Power.Rules);
                if (e > best.GetValueOrDefault(m.Def.Piece, -1)) best[m.Def.Piece] = e;
            }
        int least = 1000;
        foreach (var p in need) least = Math.Min(least, best.GetValueOrDefault(p, 0));
        return least;
    }

    // ------------------------------------------------------------------ the season (5.6)

    private static bool IsWinter(int day) => new GameTime((long)day * 86400).Season == Season.Winter;

    /// <summary>A stop the weather puts on a stage today: flood and wet days at the tiers that have them.</summary>
    private StageStop SeasonStop(StageKind k, int tier, int day)
    {
        if (k.Tier(Math.Max(1, tier)).Floods && RiverHighDays != null && RiverHighDays(day) > 0) return StageStop.Flooded;
        return StageStop.None;
    }

    private static int WinterPm(StageKind k, int tier, int day) => IsWinter(day) ? k.Tier(Math.Max(1, tier)).WinterPm : 1000;

    // ------------------------------------------------------------------ drying (5.1, 5.6)

    /// <summary>
    /// A drying stage's day: its batches age (a full drying day, a part of one in winter by the tier: open hacks none, sheds half, hot floors all), what has arrived since yesterday becomes a new batch, and the grams of those old enough (6 drying days) are what may leave.
    /// </summary>
    private long DryEligible(Run r, int day)
    {
        var s = r.S; var t = r.K.Tier(Math.Max(1, s.Tier)); var b = r.B;
        long add = IsWinter(day) ? t.DryWinterPm : 1000;
        for (int i = 0; i < s.Dwell.Count; i++) s.Dwell[i] = (s.Dwell[i].AgeMilli + add, s.Dwell[i].G);
        long have = b.Store.Qty(r.K.Main), tracked = 0;
        foreach (var (_, g) in s.Dwell) tracked += g;
        if (have > tracked) s.Dwell.Add((0, have - tracked));
        else if (have < tracked) DwellTake(s, tracked - have, fromNewest: true);
        long ripe = 0, need = t.DryDays * 1000L;
        foreach (var (a, g) in s.Dwell) { if (a >= need) ripe += g; else break; }
        return ripe;
    }

    /// <summary>Takes grams out of a stage's batches, the oldest first (what left) or the newest first (what went missing).</summary>
    private static void DwellTake(WorkStage s, long grams, bool fromNewest = false)
    {
        while (grams > 0 && s.Dwell.Count > 0)
        {
            int i = fromNewest ? s.Dwell.Count - 1 : 0;
            var (a, g) = s.Dwell[i];
            if (g <= grams) { grams -= g; s.Dwell.RemoveAt(i); }
            else { s.Dwell[i] = (a, g - grams); grams = 0; }
        }
    }

    // ------------------------------------------------------------------ the finery (5.1)

    /// <summary>The finery hearth of a smeltery tier that has one: bloom becomes bar (6 kg of bloom to 4.8 kg of bar), up to the finery's day; the scale is waste. The bar is the stage's second output.</summary>
    private void Finery(Run r, StageTier t)
    {
        var k = r.K; var b = r.B;
        if (k.Outputs.Length < 2) return;
        ushort bloom = k.Outputs[0].Good, bar = k.Outputs[1].Good;
        long take = Math.Min(b.Store.Qty(bloom), t.FineryG);
        if (take <= 0) return;
        long barQ = take * t.BarPm / 1000, scale = take - barQ;
        if (!b.Store.Destroy(bloom, barQ, DestroyReason.Consumed)) throw new InvalidOperationException("finery bloom vanished");
        if (scale > 0 && !b.Store.Destroy(bloom, scale, DestroyReason.Waste)) throw new InvalidOperationException("finery bloom vanished");
        Prod.ConsumedUnits[bloom] += take;
        b.Store.Create(bar, barQ, CreateReason.Crafted); Prod.ProducedUnits[bar] += barQ;
    }

    // ------------------------------------------------------------------ the kiln's losses (5.1, stream 0x742)

    private int DrawLossPm(Business b, WorkStage s, StageTier t)
    {
        if (ForceLossPm != null) { s.Batch++; return ForceLossPm(); }
        int span = Math.Max(1, t.LossHiPm - t.LossLoPm + 1);
        uint u = DetRng.NextUInt(Seed, s.Batch++, b.Id.Value, RngStreams.KilnLoss, 0);
        return t.LossLoPm + (int)((ulong)u * (ulong)span >> 32);
    }

    // ------------------------------------------------------------------ accidents (5.7, stream 0x740)

    /// <summary>A worker-day at a working stage risks an accident (millionths by tier); an accident is a blow through the wound book, and a death is written to the chronicle with the stage as its cause.</summary>
    private void Accidents(Works w, Run r, int day, GameTime now)
    {
        if (Injure == null || (!r.Working && r.MovedG <= 0)) return;
        var t = r.K.Tier(Math.Max(1, r.S.Tier));
        bool forced = ForceAccident?.Invoke() == true;
        for (int i = 0; i < r.B.Staff.Count; i++)
        {
            int p = r.B.Staff[i]; if (People.Status[p] != PersonStatus.Alive) continue;
            bool hit = forced && i == 0 || t.AccidentPpm > 0 && DetRng.Range(Seed, day, r.B.Id.Value, RngStreams.StageAccident, (uint)i, 0, 1_000_000) < t.AccidentPpm;
            if (!hit) continue;
            bool died = Injure(p, Set.AccidentFallMm, r.B.Id.Value * 1000UL + (ulong)day, (long)day * 86400 + 12 * 3600);
            Accidented++;
            if (died) WriteAccident(w, r, p, now);
            if (forced) break;
        }
    }

    /// <summary>Accidents that happened (never saved; the tests and the tab read it).</summary>
    public int Accidented { get; private set; }

    private void WriteAccident(Works w, Run r, int person, GameTime now)
    {
        var who = People.IdOf(person); var owner = People.IdOf(w.Owner); var place = Prod.Pop.Region.Settlements[w.Settlement].Id;
        Chron.Chron(now, "works_accident", "ch_ind_accident",
            [ChronParam.Person("who", who), ChronParam.Person("owner", owner), ChronParam.Place("place", place), ChronParam.TextKey("stage", "ind_stage_" + r.K.Id)],
            [who, r.B.Id], [new Cause(CauseKind.Industry, null, NoteKey: "ch_note_stage_accident")]);
    }

    // ------------------------------------------------------------------ packing (5.9)

    /// <summary>The containers a stage's store holds that are not yet bound to goods, in pieces.</summary>
    private static long FreeContainers(Business b, WorkStage s, ushort container)
    {
        long bound = 0; foreach (var p in s.Packed) if (p.Container == container) bound += p.Containers;
        return b.Store.Qty(container) - bound;
    }

    /// <summary>
    /// Packs up to <paramref name="grams"/> of a good in the stage's store into containers it holds (<c>PackedStock</c>): one container to each <see cref="StageKind.ContainerG"/>, as many as the store has free. Returns the grams packed.
    /// </summary>
    public long Pack(ulong business, ushort good, long grams)
    {
        if (Find(business) is not { } f || f.K.Container == ushort.MaxValue || f.K.ContainerG <= 0) return 0;
        var b = BusinessOf(f.S)!; var s = f.S;
        long packedHere = 0; foreach (var p in s.Packed) if (p.Good == good) packedHere += p.Grams;
        long loose = b.Store.Qty(good) - packedHere;
        long want = Math.Min(grams, loose); if (want <= 0) return 0;
        long n = Math.Min((want + f.K.ContainerG - 1) / f.K.ContainerG, FreeContainers(b, s, f.K.Container));
        if (n <= 0) return 0;
        long take = Math.Min(want, n * f.K.ContainerG);
        var row = s.Packed.FirstOrDefault(p => p.Good == good && p.Container == f.K.Container);
        if (row == null) s.Packed.Add(row = new PackedRow { Good = good, Container = f.K.Container });
        row.Grams += take; row.Containers += n;
        return take;
    }

    /// <summary>
    /// Takes <paramref name="grams"/> of a packed good out of its containers at the buyer (5.9): the containers come back as goods into <paramref name="into"/> (the buyer's store, or the stage's own when the goods are sold at the gate), a tenth of them worn out
    /// (<c>DestroyReason.Worn</c>), so mass stays exact. Returns the containers returned.
    /// </summary>
    public long Unpack(WorkStage s, ushort good, long grams, Store into)
    {
        var row = s.Packed.FirstOrDefault(p => p.Good == good); if (row == null || row.Grams <= 0 || grams <= 0) return 0;
        var b = BusinessOf(s)!;
        grams = Math.Min(grams, row.Grams);
        long n = grams == row.Grams ? row.Containers : row.Containers * grams / row.Grams;
        row.Grams -= grams; row.Containers -= n;
        if (row.Grams <= 0 && row.Containers <= 0) s.Packed.Remove(row);
        long worn = n * Set.ContainerWornPm / 1000;
        if (worn > 0 && !b.Store.Destroy(row.Container, worn, DestroyReason.Worn)) throw new InvalidOperationException("packed containers vanished");
        if (!ReferenceEquals(into, b.Store) && n - worn > 0 && !Store.Move(b.Store, into, row.Container, n - worn)) throw new InvalidOperationException("packed containers vanished");
        return n - worn;
    }

    /// <summary>Goods moving on to the next stage take the containers they are packed in with them, and stay packed there.</summary>
    private static void MovePacked(Run from, Run to, ushort good, long q)
    {
        var row = from.S.Packed.FirstOrDefault(p => p.Good == good); if (row == null || row.Grams <= 0 || q <= 0) return;
        ushort container = row.Container;
        long part = Math.Min(q, row.Grams), n = part == row.Grams ? row.Containers : row.Containers * part / row.Grams;
        row.Grams -= part; row.Containers -= n;
        if (row.Grams <= 0 && row.Containers <= 0) from.S.Packed.Remove(row);
        if (n > 0 && !Store.Move(from.B.Store, to.B.Store, container, n)) throw new InvalidOperationException("packed containers vanished");
        var tr = to.S.Packed.FirstOrDefault(p => p.Good == good && p.Container == container);
        if (tr == null) to.S.Packed.Add(tr = new PackedRow { Good = good, Container = container });
        tr.Grams += part; tr.Containers += n;
    }

    /// <summary>The grams of a good that sit packed in a stage's store.</summary>
    public long PackedG(WorkStage s, ushort good) { long q = 0; foreach (var p in s.Packed) if (p.Good == good) q += p.Grams; return q; }

    /// <summary>Packing before the goods leave: packs what the stage's day lets it (limit), then moves the goods and, with them, the containers bound to them.</summary>
    private void PackStage(Run r, long limit)
    {
        if (r.K.Container == ushort.MaxValue) return;
        long left = limit;
        foreach (var g in ForwardGoods(r.K))
        {
            if (left <= 0) break;
            left -= Pack(r.B.Id.Value, g, left);
        }
    }

    /// <summary>Containers for tomorrow: the market is asked for the casks the stage's day will need (two days' worth).</summary>
    private void BuyContainers(Run r, long dayG)
    {
        if (Prod.Markets == null || r.K.Container == ushort.MaxValue || r.K.ContainerG <= 0 || dayG <= 0) return;
        long want = (dayG + r.K.ContainerG - 1) / r.K.ContainerG * 2, have = FreeContainers(r.B, r.S, r.K.Container);
        if (have >= want) return;
        var book = Prod.Markets.BookForSettlement(r.B.Settlement);
        book.Post(new BuyOrder(r.B.Party, r.K.Container, want - have, 3000) { Priority = 5, OnFilled = Prod.InputHook(r.B), HookKind = 1, HookArg = r.B.Index });
    }
}
