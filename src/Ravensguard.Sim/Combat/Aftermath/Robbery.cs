using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>
/// Robbery by bandits (morale-capture-succession 4.4): the purse first (all of it, but a pittance of 5%, at least 20 mites, to a victim with a wound that needs a doctor), then the things in the
/// robbers' order until their carrying capacity (<c>15 kg + 2.5 kg x strength / 125</c> each, minus what they already carry) is full: every weapon, the mount, armour that fits a bandit
/// (<see cref="Fit.Fits"/>) or is worth more than 400 mites, then goods and tools by value density. Left: the under layer, boots, a day's food, a dagger unless the victim fought to the
/// end. <see cref="Plan"/> is pure; <see cref="Apply"/> moves coin through the <see cref="Ledger"/> and item rows and goods to the band's stash, so nothing is created or destroyed (MC-12).
/// </summary>
public static class Robbery
{
    public static long CapacityG(AftermathRules rules, IReadOnlyList<Robber> robbers)
    {
        var D = rules.D; long cap = 0;
        foreach (var r in robbers) cap += Math.Max(0, D.CarryBaseG + (long)D.CarryPerStrengthG * r.Strength / 125 - D.RobberCarriesG);
        return cap;
    }

    /// <summary>The pittance left to a victim who needs a doctor: 5% of the purse, at least 20 mites, never more than there is.</summary>
    public static long Pittance(AftermathRules rules, long coin, bool needsDoctor) =>
        !needsDoctor || coin <= 0 ? 0 : Math.Min(coin, Math.Max(rules.D.PittanceMinMites, coin * rules.D.PittancePm / 1000));

    public static RobberyPlan Plan(AftermathRules rules, long coinMites, bool needsDoctor, IReadOnlyList<LootItem> items, bool resisted, IReadOnlyList<Robber> robbers, bool purseOnly = false)
    {
        var D = rules.D;
        long left = Pittance(rules, coinMites, needsDoctor);
        long taken = Math.Max(0, coinMites - left);
        if (purseOnly) return new RobberyPlan(taken, left, [], [.. items], 0, items.Where(i => i.Kind == LootKind.Food).Sum(i => i.MassG));
        var leftShapes = new HashSet<string>(D.LeftShapes, StringComparer.Ordinal);
        var take = new List<LootItem>(); var keep = new List<LootItem>();
        long cap = CapacityG(rules, robbers);
        bool daggerLeft = resisted;                                  // a victim who fought to the end is "seen": no dagger is left
        long foodLeft = D.FoodLeftG, foodTaken = 0, foodKept = 0;
        var rest = new List<LootItem>();
        var foodGoods = new HashSet<ushort>();
        // fixed order of the candidates: by kind, then the robbers' preference, then id (stable)
        foreach (var it in items.OrderBy(i => i.Id.Value).ThenBy(i => i.Good))
        {
            switch (it.Kind)
            {
                case LootKind.Clothing when leftShapes.Contains(it.Shape): keep.Add(it); continue;
                case LootKind.Food:
                {
                    foodGoods.Add(it.Good);
                    long k = Math.Min(foodLeft, it.MassG); foodLeft -= k;
                    if (k >= it.MassG) { keep.Add(it); foodKept += k; continue; }
                    if (k > 0) { keep.Add(it with { MassG = k, Qty = Qty(it, k) }); foodKept += k; }
                    rest.Add(it with { MassG = it.MassG - k, Qty = it.Qty - Qty(it, k), Kind = LootKind.Goods });
                    continue;
                }
                case LootKind.Weapon when it.Dagger && !daggerLeft: daggerLeft = true; keep.Add(it); continue;
                case LootKind.Weapon: take.Add(it); cap -= it.MassG; continue;            // every weapon
                case LootKind.Mount: take.Add(it); continue;                              // led away, not carried
                default: rest.Add(it); continue;
            }
        }
        // armour that fits one of them or is worth the trouble, then goods and tools, by value density
        bool Fits(LootItem i) => robbers.Any(r => Fit.Fits(i.MadeForCm3, r.SizeCm3)) || i.ValueMites > D.ArmourKeepMites;
        var armour = rest.Where(i => i.Kind is LootKind.Armour or LootKind.Clothing && Fits(i)).OrderByDescending(Density).ThenBy(i => i.Id.Value).ToList();
        var goods = rest.Where(i => i.Kind is LootKind.Goods or LootKind.Tool).OrderByDescending(Density).ThenBy(i => i.Id.Value).ThenBy(i => i.Good).ToList();
        var chosen = new HashSet<LootItem>(ReferenceEqualityComparer.Instance);
        foreach (var i in armour.Concat(goods))
        {
            if (cap <= 0) break;
            if (i.MassG <= cap) { take.Add(i); chosen.Add(i); cap -= i.MassG; continue; }
            if (i.Good != ushort.MaxValue && i.Qty > 1)
            {
                long q = Qty(i, cap);                                                      // a stack of goods: as much as still fits
                if (q <= 0) continue;
                long g = i.MassG * q / i.Qty;
                take.Add(i with { MassG = g, Qty = q }); keep.Add(i with { MassG = i.MassG - g, Qty = i.Qty - q }); chosen.Add(i); cap -= g;
            }
        }
        foreach (var i in rest) if (!chosen.Contains(i)) keep.Add(i);
        foreach (var i in take) if (i.Good != ushort.MaxValue && foodGoods.Contains(i.Good)) foodTaken += i.MassG;   // the food the robbers carry off (it was always 0: review finding 7)
        return new RobberyPlan(taken, left, take, keep, foodTaken, foodKept);
    }

    private static long Qty(LootItem i, long grams) => i.Qty <= 0 || i.MassG <= 0 ? 0 : Math.Min(i.Qty, grams * i.Qty / i.MassG);
    private static long Density(LootItem i) => i.MassG <= 0 ? i.ValueMites * 1000 : i.ValueMites * 1000 / i.MassG;

    // ------------------------------------------------------------------ the records

    /// <summary>What a person carries, as the robbers judge it: the item rows they own (worn, held, packed) and the goods of their pack. Values by the item factory.</summary>
    public static List<LootItem> Holdings(ContentDatabase content, EconomyState e, Carried carried, int bodyCm3 = 70000, Func<int, long>? valueOf = null, EntityId mount = default, long mountValue = 0)
    {
        var under = new HashSet<string>(AftermathRules.For(content).D.LeftShapes, StringComparer.Ordinal);
        var weapons = content.All<WeaponShapeDef>().Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var shields = content.All<ShieldShapeDef>().Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var armour = content.All<ArmourShapeDef>().Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var list = new List<LootItem>();
        var mine = carried.Rows(e).ToList();                                                   // one walk of the item table (review finding 7: one per row and per container before)
        foreach (int row in mine)
        {
            ref var r = ref e.Items.At(row);
            if (r.Parent >= 0 && e.Items.At(r.Parent).Owner == carried.Owner) continue;      // contents go with their container
            long mass = MassWithContents(e, carried.Owner, row, mine);
            long value = valueOf?.Invoke(row) ?? 0;
            var kind = weapons.Contains(r.Shape) || shields.Contains(r.Shape) ? LootKind.Weapon : under.Contains(r.Shape) ? LootKind.Clothing
                : armour.Contains(r.Shape) ? LootKind.Armour : carried.Equip.ContainsKey(r.Id) ? LootKind.Clothing : LootKind.Tool;
            list.Add(new LootItem(r.Id, r.Shape, kind, value, mass, bodyCm3, Dagger: r.Shape is "dagger" or "knife"));
        }
        var pack = carried.Pack(e);
        var food = new HashSet<ushort>(e.Food.Staples.Concat(e.Food.Relishes));
        foreach (var (g, q) in pack.Rows().ToList())
        {
            if (q <= 0) continue;
            long mass = e.Goods.MassG(g, q);
            long value = e.Goods.IsPiece(g) ? q * e.Goods.BasePriceMilliMites(g) / 1000 : mass * e.Goods.BasePriceMilliMites(g) / 1_000_000;
            list.Add(new LootItem(EntityId.None, e.Goods.Def(g).Id, food.Contains(g) ? LootKind.Food : LootKind.Goods, value, mass, Good: g, Qty: q));
        }
        if (mount != EntityId.None) list.Add(new LootItem(mount, "mount", LootKind.Mount, mountValue, 0));
        return list;
    }

    /// <summary>A row's mass with what is inside it; <paramref name="mine"/> is every row the owner holds (contents are always the owner's too).</summary>
    private static long MassWithContents(EconomyState e, EntityId owner, int row, List<int> mine)
    {
        long m = e.Items.MassG(row, e.Goods);
        foreach (int i in mine) if (e.Items.At(i).Parent == row && e.Items.At(i).Owner == owner) m += MassWithContents(e, owner, i, mine);
        return m;
    }

    /// <summary>
    /// Carries the plan out: the coin through the ledger (victim to band, reason Plunder), every taken item row to the stash owner (with what is inside it), every taken stack of goods into the
    /// stash's store. Returns the item ids moved. A mount is the caller's (the ride book's) to move.
    /// </summary>
    public static List<EntityId> Apply(EconomyState e, Carried carried, PurseId victim, PurseId band, EntityId stashOwner, RobberyPlan plan)
    {
        if (plan.CoinTaken > 0 && victim != band) e.Ledger.Transfer(victim, band, Coin.FromMites(Math.Min(plan.CoinTaken, e.Ledger.Balance(victim).Mites)), AftermathCoin.Plunder);
        var moved = new List<EntityId>();
        var stash = e.OpenStore(StoreKind.Chest, stashOwner);
        var pack = carried.Pack(e);
        var mine = carried.Rows(e).ToList();
        foreach (var it in plan.Taken)
        {
            if (it.Kind == LootKind.Mount) continue;
            if (it.Id != EntityId.None && e.Items.IndexOf(it.Id) is int row and >= 0 && e.Items.At(row).Owner == carried.Owner)
            {
                MoveRow(e, carried, row, stashOwner, moved, mine);
                e.Items.At(row).Parent = -1;
            }
            else if (it.Good != ushort.MaxValue && it.Qty > 0) Store.MoveUpTo(pack, stash, it.Good, it.Qty);
        }
        return moved;
    }

    private static void MoveRow(EconomyState e, Carried carried, int row, EntityId to, List<EntityId> moved, List<int> mine)
    {
        var id = e.Items.At(row).Id;
        foreach (int i in mine) if (e.Items.At(i).Parent == row && e.Items.At(i).Owner == carried.Owner) MoveRow(e, carried, i, to, moved, mine);
        e.Items.At(row).Owner = to;
        carried.Equip.Remove(id);
        for (int q = 0; q < carried.QuickBar.Length; q++) if (carried.QuickBar[q] == id) carried.QuickBar[q] = EntityId.None;
        moved.Add(id);
    }

    /// <summary>Gives items back (a ransom paid to an army returns the stash): rows owned by <paramref name="stashOwner"/> among <paramref name="ids"/> go back to the person.</summary>
    public static int Return(EconomyState e, EntityId stashOwner, EntityId person, IEnumerable<EntityId> ids)
    {
        int n = 0;
        foreach (var id in ids)
        {
            int row = e.Items.IndexOf(id);
            if (row < 0 || e.Items.At(row).Owner != stashOwner) continue;
            e.Items.At(row).Owner = person; n++;
        }
        return n;
    }

    /// <summary>The grams of every live item row (the conservation audit of MC-12: items move, never vanish).</summary>
    public static long ItemMassG(EconomyState e)
    {
        long m = 0;
        foreach (int i in e.Items.LiveRows()) m += e.Items.MassG(i, e.Goods);
        return m;
    }
}
