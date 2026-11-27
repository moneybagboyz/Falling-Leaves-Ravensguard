using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.WarSupply;

/// <summary>What a number of levy kits costs to make (army-economy 4.10, AE-16): grams of iron, minutes of smith work in the munition patterns (with practice), minutes of other work (carpentry, tailoring), and mites at the order prices.</summary>
public sealed record KitCost(long IronG, long SmithMinutes, long OtherMinutes, long Mites)
{
    public long SmithHours => SmithMinutes / 60;
    public long OtherHours => OtherMinutes / 60;
}

/// <summary>What a season's order for a kit target did: the gaps against the armoury, what was ordered inside the budget, what was left over, the mites ordered and the budget.</summary>
public sealed record ArmsOrderResult(IReadOnlyDictionary<string, int> Gaps, IReadOnlyDictionary<string, int> Ordered, IReadOnlyDictionary<string, int> Deferred, long Mites, long BudgetMites);

/// <summary>What <see cref="ArmsWorld.Sort"/> did with a field's items: to the armoury, kept by the holder (the armoury had no place; no sale), to the refit queue, salvaged, and the scrap and rags that came of it.</summary>
public sealed record LootResult(int ToArmoury, int Kept, int ToRefit, int Salvaged, long ScrapG, long RagsG);

/// <summary>One caravan of the arms trade.</summary>
public sealed record ArmsShipment(int From, int To, long Grams, long Mites);

/// <summary>A season of the arms trade: the caravans that ran and the number stopped at the gate by a ban.</summary>
public sealed record ArmsTradeResult(IReadOnlyList<ArmsShipment> Shipments, int Stopped);

/// <summary>
/// P7-AE3 (army-economy 4.10): the arms industry. Kit costs by the munition patterns, the season's orders within the arms budget, the armoury's kit fill and gaps, loot sorted by condition (armoury, refit queue, salvage to
/// <c>scrap_iron</c> and <c>rags</c>), <c>forge_scrap</c>, and the arms trade among realms (an index per realm, caravans drawn on stream 0x754, export bans). Integer arithmetic; items and goods move only through the item
/// table and the stores (a salvaged item folds into goods as <c>Dematerialised</c>), coin only through the ledger by the caller's payment hook.
/// </summary>
public sealed class ArmsWorld
{
    public ArmsBook Book { get; }
    public ArmsRulesDef Rules { get; }
    private readonly ScaleSettingsDef _scale;
    private readonly ItemTable _items;
    private readonly Armouries? _arm;
    private readonly GoodCatalog _cat;
    private readonly ulong _seed;

    public ArmsWorld(ArmsBook book, ContentDatabase db, ItemTable items, Armouries? armouries, GoodCatalog cat, ulong seed)
    {
        Book = book; _items = items; _arm = armouries; _cat = cat; _seed = seed;
        Rules = db.All<ArmsRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ArmsRulesDef { Id = "arms_rules" };
        _scale = db.All<ScaleSettingsDef>().OrderBy(r => r.Id, StringComparer.Ordinal).First();
    }

    // ------------------------------------------------------------------ kits

    /// <summary>The iron, the work and the price of <paramref name="kits"/> levy kits. The forged pieces are made in runs of the pattern's size (the last run short), each run its setup plus the pieces, with practice when <paramref name="practised"/>.</summary>
    public KitCost KitCost(int kits, bool practised = true)
    {
        long iron = 0, smith = 0, other = 0, mites = 0;
        foreach (var p in Rules.LevyKit)
        {
            iron += (long)p.IronG * kits; other += (long)p.OtherMinutes * kits; mites += (long)p.PriceMites * kits;
            var pat = p.Recipe.Length > 0 ? Patterns.Of(_scale, p.Recipe) : null;
            if (pat == null) continue;
            for (int left = kits; left > 0; left -= pat.RunMax) smith += Patterns.RunMinutes(pat, Math.Min(left, pat.RunMax), practised, _scale.PracticePm);
        }
        return new KitCost(iron, smith, other, mites);
    }

    /// <summary>The pieces of one kit, in issue order, as a shape-to-count template for <c>kits</c> kits.</summary>
    public IReadOnlyDictionary<string, int> Template(int kits) => Rules.LevyKit.ToDictionary(p => p.Shape, _ => kits, StringComparer.Ordinal);

    /// <summary>Issues <paramref name="kits"/> levy kits from an armoury to a holder: for each piece the best available (quality tier, condition, id); what is lacking comes back as gaps (AE-15).</summary>
    public IssueResult Fill(long armoury, int kits, EntityId holder, int day) =>
        _arm!.Issue(armoury, [.. Rules.LevyKit.Select(p => new ArmsRequest(p.Shape, kits))], holder, day);

    // ------------------------------------------------------------------ orders

    /// <summary>
    /// The season's order (S5): the gap between the armoury's stock and <paramref name="kits"/> kits, ordered piece by piece in the kit's order while the cost stays inside the season's budget, <c>BudgetPm</c> of the lord's last year's
    /// receipts (AEQ-6), less what the lord's orders of this season already cost; those pending orders are taken off the gaps by shape, and rows of an earlier season are dropped as stale; a piece that no longer fits is deferred, a cheaper piece after it may still fit. Writes <c>arms_ordered</c> when anything was ordered. The orders are rows of the book, to be delivered by the smiths, manufactories and the arms trade; no coin moves until they are.
    /// </summary>
    public ArmsOrderResult Order(ulong lord, long armoury, int kits, long lastYearReceipts, int day, RuleChron chron)
    {
        int season = day / TimeConst.DaysPerSeason;
        Book.Orders.RemoveAll(o => o.Day / TimeConst.DaysPerSeason < season);                                              // last seasons' orders are stale
        var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal); foreach (var (k, v) in _arm!.Gaps(armoury, Template(kits))) gaps[k] = v;
        long budget = lastYearReceipts * Rules.BudgetPm / 1000, spent = 0, pendingCost = 0; int pieces = 0;
        foreach (var o in Book.Orders.Where(o => o.Lord == lord))
        {
            pendingCost += o.Mites;
            if (gaps.TryGetValue(o.Shape, out int g)) gaps[o.Shape] = Math.Max(0, g - o.Count);
        }
        long left = budget - pendingCost;
        var ordered = new SortedDictionary<string, int>(StringComparer.Ordinal); var deferred = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in Rules.LevyKit)
        {
            if (!gaps.TryGetValue(p.Shape, out int gap) || gap <= 0) continue;
            int n = p.PriceMites <= 0 ? gap : (int)Math.Min(gap, Math.Max(0, left) / p.PriceMites);
            if (n > 0)
            {
                long cost = (long)n * p.PriceMites; left -= cost; spent += cost; pieces += n; ordered[p.Shape] = n;
                Book.Orders.Add(new ArmsOrderRow(lord, day, p.Shape, n, cost));
            }
            if (n < gap) deferred[p.Shape] = gap - n;
        }
        Book.Stats[3] += pieces;
        if (pieces > 0)
            chron.Chron(GameTime.FromGameHours((long)day * 24 + 5), "arms_ordered", "ch_war_arms_ordered", [ChronParam.Person("lord", new EntityId(lord)), ChronParam.Count("kits", pieces), ChronParam.Mites("budget", spent)],
                lord != 0 ? [new EntityId(lord)] : [], [new Cause(CauseKind.Supply, null, NoteKey: "ch_note_war_arms")]);
        return new ArmsOrderResult(gaps, ordered, deferred, spent, budget);
    }

    // ------------------------------------------------------------------ loot

    /// <summary>
    /// Sorts the items a side holding the field took (army-economy 4.10). Each is stripped first: its condition falls by 0 to <c>StripMaxPm</c> permille of itself (stream 0x752, key item, battle). Then: condition of
    /// <c>KeepPm</c> or more to the armoury (kept by the holder, with no sale, when it has no place), <c>RefitPm</c> to <c>KeepPm</c> to the refit queue, below to salvage: the iron comes back as <c>scrap_iron</c> and the leather and cloth as <c>rags</c>
    /// (<c>ScrapPm</c> and <c>RagsPm</c> of their mass) into <paramref name="store"/>, the item gone (the rest is slag and rot).
    /// </summary>
    public LootResult Sort(long battle, EntityId holder, long armoury, ArmouryCapacity cap, IReadOnlyList<EntityId> items, Store store)
    {
        int keep = 0, kept = 0, refit = 0, salvaged = 0; long scrap = 0, rags = 0;
        ushort scrapGood = _cat.IdOf("scrap_iron"), ragsGood = _cat.IdOf("rags");
        foreach (var id in items.Distinct().OrderBy(i => i.Value))
        {
            int row = _items.IndexOf(id); if (row < 0) continue;
            if (_arm!.IsStocked(id)) continue;
            ref var r = ref _items.At(row);
            int strip = DetRng.Range(_seed, battle, id.Value, RngStreams.LootCondition, 0, 0, Rules.StripMaxPm + 1);
            r.ConditionPermille = (short)Math.Max(0, r.ConditionPermille - r.ConditionPermille * strip / 1000);
            int cond = r.ConditionPermille;
            if (cond >= Rules.KeepPm)
            {
                if (_arm.Add(armoury, cap, id)) keep++; else { r.Owner = holder; kept++; Book.Stats[8]++; }
            }
            else if (cond >= Rules.RefitPm) { r.Owner = Armouries.HolderOf(armoury); Book.Refit[id.Value] = armoury; refit++; }
            else
            {
                long iron = 0, soft = 0;
                for (int k = 0; k < r.ComponentCount; k++)
                {
                    var c = _items.Component(r.ComponentStart + k);
                    if (Rules.IronMaterials.Contains(c.Material)) iron += c.MassG; else if (Rules.SoftMaterials.Contains(c.Material)) soft += c.MassG;
                }
                long sg = iron * Rules.ScrapPm / 1000, rg = soft * Rules.RagsPm / 1000;
                if (!_items.Free(id)) continue;
                if (sg > 0) sg = store.CreateUpTo(scrapGood, sg, CreateReason.Dematerialised);                              // what the store has room for; the rest is lost with the slag
                if (rg > 0) rg = store.CreateUpTo(ragsGood, rg, CreateReason.Dematerialised);
                scrap += sg; rags += rg; salvaged++;
            }
        }
        Book.Stats[4] += keep; Book.Stats[6] += scrap; Book.Stats[9] += rags;
        return new LootResult(keep, kept, refit, salvaged, scrap, rags);
    }

    /// <summary>
    /// The armoury's armourers work the refit queue: each armourer restores one item a day, in item id order, to the cap of its quality tier (permille) and puts it on the armoury's stands (it stays queued, unrestored, until there is a place; an item that can never be stocked is dropped from the queue).
    /// Returns the items finished.
    /// </summary>
    public int RunRefit(int armourers, Func<long, ArmouryCapacity> capacity)
    {
        int done = 0;
        foreach (var (item, armoury) in Book.Refit.ToList())
        {
            if (done >= armourers) break;
            var id = new EntityId(item); int row = _items.IndexOf(id);
            if (row < 0) { Book.Refit.Remove(item); continue; }
            var kind = _arm!.KindOf(row);
            if (kind == ArmsKind.None || _arm.IsStocked(id)) { Book.Refit.Remove(item); continue; }                        // it can never be added
            if (!_arm.Fits(armoury, capacity(armoury), kind)) continue;                                                      // no place yet: stays as it is, uses no armourer
            ref var r = ref _items.At(row);
            int cap = Rules.RefitCapPm.TryGetValue(r.Quality.ToString(), out int c) ? c : 800;
            if (r.ConditionPermille < cap) r.ConditionPermille = (short)cap;
            if (!_arm.Add(armoury, capacity(armoury), id)) { Book.Refit.Remove(item); continue; }
            Book.Refit.Remove(item); done++; Book.Stats[5]++;
        }
        return done;
    }

    /// <summary><c>forge_scrap</c> at a forge (AE-18): each batch of 10 kg of scrap and 6 kg of charcoal gives 8 kg of bar in 8 hours. Makes up to <paramref name="maxBatches"/> batches from what the store holds; returns the batches and the hours.</summary>
    public (int Batches, int Hours) ForgeScrap(Store store, int maxBatches)
    {
        ushort scrap = _cat.IdOf("scrap_iron"), coal = _cat.IdOf("charcoal"), bar = _cat.IdOf("iron_bar");
        long barRoom = store.Room(bar) / Math.Max(1, Rules.BarBatchG);
        int n = (int)Math.Min(maxBatches, Math.Min(barRoom, Math.Min(store.Qty(scrap) / Rules.ScrapBatchG, store.Qty(coal) / Rules.CharcoalBatchG)));
        if (n <= 0) return (0, 0);
        store.Destroy(scrap, (long)n * Rules.ScrapBatchG, DestroyReason.Consumed); store.Destroy(coal, (long)n * Rules.CharcoalBatchG, DestroyReason.Burned);
        store.Create(bar, (long)n * Rules.BarBatchG, CreateReason.Crafted);
        Book.Stats[7] += (long)n * Rules.BarBatchG;
        return (n, n * Rules.ForgeHours);
    }

    // ------------------------------------------------------------------ the arms trade

    /// <summary>A realm's arms price index, permille: its war multiple times its target over its stock (the stock floored at a quarter of the target), clamped.</summary>
    public int IndexOf(int realm, bool atWar)
    {
        long stock = Book.Stock.TryGetValue(realm, out long g) ? g : Rules.TargetG, mult = atWar ? Rules.WarDemandPm : 1000;
        long idx = mult * Rules.TargetG / Math.Max(Rules.TargetG / 4, Math.Max(1, stock));
        return (int)Math.Clamp(idx, Rules.IndexMinPm, Rules.IndexMaxPm);
    }

    /// <summary>
    /// One season of the arms trade (continent tick, stream 0x754): each realm in id order has its index (a realm at war asks three times the arms). Every ordered pair (exporter, importer) in id order is drawn (key the pair,
    /// the season): on a draw under <c>CaravanPm</c>, if the exporter holds more than half its target and the importer's index is the higher, a caravan of up to <c>CaravanG</c> sets out, unless the exporter bans exports to the
    /// importer, when it is stopped at the gate. A caravan runs only when <paramref name="pay"/> (the caller's ledger move, exporter, importer, mites) says the coin moved; the stocks move with it. One caravan a season to an importer.
    /// A realm sees stock only once it is named here, at its target.
    /// </summary>
    public ArmsTradeResult Season(int day, IReadOnlyList<(int Realm, bool AtWar)> realms, Func<int, int, long, bool> pay)
    {
        var list = realms.OrderBy(r => r.Realm).ToList(); var atWar = list.ToDictionary(r => r.Realm, r => r.AtWar);
        foreach (var (r, _) in list) if (!Book.Stock.ContainsKey(r)) Book.Stock[r] = Rules.TargetG;
        foreach (var (r, w) in list) Book.Index[r] = IndexOf(r, w);
        int season = day / TimeConst.DaysPerSeason, stopped = 0; var ran = new List<ArmsShipment>(); var served = new HashSet<int>();
        foreach (var (e, _) in list)
            foreach (var (i, _) in list)
            {
                if (e == i || served.Contains(i) || Book.Stock[e] <= Rules.TargetG / 2 || Book.Index[i] <= Book.Index[e]) continue;
                if (DetRng.Range(_seed, season, ((ulong)(uint)e << 32) | (uint)i, RngStreams.ArmsCaravans, 0, 0, 1000) >= Rules.CaravanPm) continue;
                if (Book.Banned(e, i)) { stopped++; Book.Stats[2]++; Log(new ArmsShipRow(e, i, 0, 0, day, 1)); continue; }
                long load = Math.Min(Rules.CaravanG, Book.Stock[e] - Rules.TargetG / 2); if (load <= 0) continue;
                long price = load / 1000 * Rules.PriceMitesPerKg * (Book.Index[e] + Book.Index[i]) / 2 / 1000;
                if (!pay(e, i, price)) continue;
                Book.Stock[e] -= load; Book.Stock[i] += load; served.Add(i);
                Book.Stats[0] += load; Book.Stats[1] += price; ran.Add(new ArmsShipment(e, i, load, price)); Log(new ArmsShipRow(e, i, load, price, day, 0));
            }
        foreach (var (r, w) in list) Book.Index[r] = IndexOf(r, w);
        return new ArmsTradeResult(ran, stopped);
    }

    private void Log(ArmsShipRow row) { Book.Log.Add(row); if (Book.Log.Count > ArmsBook.LogCap) Book.Log.RemoveAt(0); }
}
