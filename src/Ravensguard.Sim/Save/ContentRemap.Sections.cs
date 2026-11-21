using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Save;

public static partial class ContentRemap
{
    /// <summary>Rewrites each section that stores a content index (every other section is kept as its bytes).</summary>
    private sealed class Rewriter(Dictionary<string, TableRemap> remaps, ContentTables ct)
    {
        private readonly TableRemap? G = remaps.GetValueOrDefault(IdTables.Goods);
        private readonly TableRemap? R = remaps.GetValueOrDefault(IdTables.CraftRecipes);
        private readonly TableRemap? P = remaps.GetValueOrDefault(IdTables.Proficiencies);
        private readonly TableRemap? K = remaps.GetValueOrDefault(IdTables.Businesses);
        private readonly TableRemap? B = remaps.GetValueOrDefault(IdTables.BookRecipes);
        private readonly TableRemap? X = remaps.GetValueOrDefault(IdTables.Extractions);
        private readonly string _hash = ct.Goods.Hash();
        private readonly long[] _writtenOff = new long[remaps.GetValueOrDefault(IdTables.Goods)?.Old.Length ?? 0];
        private readonly SortedDictionary<string, int> _dropped = new(StringComparer.Ordinal);

        private bool GoodsMove => G is { Identity: false };
        private static bool Moves(TableRemap? t) => t is { Identity: false };
        private void Count(string what, int n = 1) { if (n > 0) _dropped[what] = _dropped.GetValueOrDefault(what) + n; }

        public byte[] Section(string name, ReadOnlyMemory<byte> bytes)
        {
            switch (name)
            {
                case WorldSave.EconomySection: { var s = Decode<EconomySave>(bytes, name); Economy(s); return SaveCodec.SerializeCompressed(s); }
                case ProductionSave.SectionName: { var s = Decode<ProductionSave>(bytes, name); Production(s); return SaveCodec.SerializeCompressed(s); }
                case MarketsSave.SectionName: { var s = Decode<MarketsSave>(bytes, name); Markets(s); return SaveCodec.SerializeCompressed(s); }
                case WorldSave.ContinentSection when GoodsMove: { var s = Decode<ContinentSave>(bytes, name); Continent(s); return SaveCodec.SerializeCompressed(s); }
                case MountsSave.SectionName when GoodsMove: { var s = Decode<MountsSave>(bytes, name); Mounts(s); return SaveCodec.SerializeCompressed(s); }
                case TravelSave.SectionName when GoodsMove: { var s = Decode<TravelSave>(bytes, name); Travel(s); return SaveCodec.SerializeCompressed(s); }
                case WorldSave.PartySection when GoodsMove: { var s = Decode<HeroPartySave>(bytes, name); Party(s); return SaveCodec.SerializeCompressed(s); }
                case HeroPendingSave.SectionName when GoodsMove || Moves(R): { var s = Decode<HeroPendingSave>(bytes, name); Pending(s); return SaveCodec.SerializeCompressed(s); }
                case ConstructionSave.SectionName when GoodsMove: { var s = Decode<ConstructionSave>(bytes, name); Construction(s); return SaveCodec.SerializeCompressed(s); }
                case WorldSave.CraftingSection when GoodsMove || Moves(R) || Moves(P): { var s = Decode<CraftingSave>(bytes, name); Crafting(s); return SaveCodec.SerializeCompressed(s); }
                case WorldSave.JobsSection when GoodsMove: { var s = Decode<JobsSave>(bytes, name); Jobs(s); return SaveCodec.SerializeCompressed(s); }
                case AutopilotSave.SectionName when GoodsMove: { var s = Decode<AutopilotSave>(bytes, name); Autopilot(s); return SaveCodec.SerializeCompressed(s); }
                case OwnershipSave.SectionName when GoodsMove || Moves(B): { var s = Decode<OwnershipSave>(bytes, name); Ownership(s); return SaveCodec.SerializeCompressed(s); }   // P5-F
                case WorldSave.GrowthSection when Moves(K): { var s = Decode<GrowthSave>(bytes, name); Growth(s); return SaveCodec.SerializeCompressed(s); }     // P5-F: new business kinds
            }
            if (name.StartsWith(WorldSave.RegionSectionName, StringComparison.Ordinal) && int.TryParse(name.AsSpan(WorldSave.RegionSectionName.Length), out _))
            {
                var s = Decode<RegionSectionData>(bytes, name);
                Economy(s.Economy); Production(s.Production); Markets(s.Markets);
                if (s.Growth != null && Moves(K)) Growth(s.Growth);                                  // P5-F
                return SaveCodec.SerializeCompressed(s);
            }
            return bytes.ToArray();
        }

        public List<string> Notes()
        {
            var notes = new List<string>();
            foreach (var t in remaps.Values.Where(t => !t.Identity).OrderBy(t => t.Table, StringComparer.Ordinal))
            {
                var parts = new List<string>();
                if (t.Added > 0) parts.Add($"{t.Added} new");
                if (t.Migrated.Count > 0) parts.Add("moved " + string.Join(", ", t.Migrated));
                if (t.Dropped.Count > 0) parts.Add((t == G ? "no longer in the game: " : "no longer in the game, dropped: ") + string.Join(", ", t.Dropped));
                notes.Add($"Content table '{t.Table}' changed since this save ({(parts.Count > 0 ? string.Join("; ", parts) : "reordered")}); the save was mapped onto it by name.");
            }
            if (G is { Dropped.Count: > 0 } && G.Dropped.Any(id => _writtenOff[Array.IndexOf(G.Old, id)] != 0))
            {
                var off = string.Join(", ", G.Dropped.Where(id => _writtenOff[Array.IndexOf(G.Old, id)] != 0).Select(id => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{id} {_writtenOff[Array.IndexOf(G.Old, id)]:N0}")));
                var rec = _dropped.Count > 0 ? " Records dropped with them: " + string.Join(", ", _dropped.Select(kv => $"{kv.Value} {kv.Key}")) + "." : "";
                notes.Add($"Warning: goods no longer in the game were written off (quantity in grams, or pieces): {off}. Their ledger columns went with their stock, so the mass and coin audits stay exact.{rec}");
            }
            else if (_dropped.Count > 0) notes.Add("Records of content no longer in the game were dropped: " + string.Join(", ", _dropped.Select(kv => $"{kv.Value} {kv.Key}")) + ".");
            return notes;
        }

        // ---------------------------------------------------------------- goods helpers

        /// <summary>Store rows onto the new table: dropped goods written off, migrated ones added to their target, ascending (<see cref="Store"/> needs that).</summary>
        private (ushort[] Goods, long[] Qty) Rows(ReadOnlySpan<ushort> goods, ReadOnlySpan<long> qty)
        {
            var acc = new SortedDictionary<ushort, long>();
            for (int i = 0; i < goods.Length && i < qty.Length; i++)
            {
                int n = G![goods[i]];
                if (n < 0) { if (goods[i] < _writtenOff.Length) _writtenOff[goods[i]] += qty[i]; continue; }
                acc[(ushort)n] = checked(acc.GetValueOrDefault((ushort)n) + qty[i]);
            }
            return ([.. acc.Keys], [.. acc.Values]);
        }

        /// <summary>A good index (or <see cref="ItemTable.NoGood"/>) onto the new table; -1 when dropped.</summary>
        private int Good(ushort g) => g == ItemTable.NoGood ? ItemTable.NoGood : G![g];

        /// <summary>Per-good rows of a <paramref name="width"/>-wide array (old good count x width) onto the new table: a migrated good's rows add to its target's.</summary>
        private long[] PerGood(long[] a, int width)
        {
            int og = G!.Old.Length, ng = G.New.Length;
            if (a.Length != og * width) return a;
            var o = new long[ng * width];
            for (int g = 0; g < og; g++)
            {
                int n = G[g]; if (n < 0) continue;
                for (int r = 0; r < width; r++) o[n * width + r] = checked(o[n * width + r] + a[g * width + r]);
            }
            return o;
        }

        // ---------------------------------------------------------------- sections

        private void Economy(EconomySave e)
        {
            if (G == null || (e.GoodsHash.Length == 0 && e.GoodCount == 0)) return;     // an empty section (a region record without an economy)
            if (GoodsMove)
            {
                if (e.GoodCount != G.Old.Length) throw new SaveException("The economy section does not match the goods table this save names.");
                var rg = new List<ushort>(); var rq = new List<long>(); var counts = new int[e.StoreRowCount.Length]; int row = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    int n = e.StoreRowCount[i];
                    var (g, q) = Rows(e.RowGood.AsSpan(row, n), e.RowQty.AsSpan(row, n)); row += n;
                    counts[i] = g.Length; rg.AddRange(g); rq.AddRange(q);
                }
                e.StoreRowCount = counts; e.RowGood = [.. rg]; e.RowQty = [.. rq];
                e.MassCreated = PerGood(e.MassCreated, MassLedger.CreateReasons); e.MassDestroyed = PerGood(e.MassDestroyed, MassLedger.DestroyedWidth(G.Old.Length, e.MassDestroyed.Length) is var dw and >= 0 ? dw : MassLedger.DestroyReasons);
                for (int i = 0; i < e.ItemGood.Length; i++) { int n = Good(e.ItemGood[i]); e.ItemGood[i] = n < 0 ? ItemTable.NoGood : (ushort)n; }
                for (int i = 0; i < e.ItemContentsGood.Length; i++)
                {
                    int n = Good(e.ItemContentsGood[i]);
                    if (n < 0) { Count("item contents"); e.ItemContentsGood[i] = ItemTable.NoGood; if (i < e.ItemContentsQty.Length) e.ItemContentsQty[i] = 0; }
                    else e.ItemContentsGood[i] = (ushort)n;
                }
                e.GoodCount = G.New.Length;
            }
            e.GoodsHash = _hash;
        }

        private void Production(ProductionSave p)
        {
            if (G != null)
            {
                if (GoodsMove) { p.ProducedUnits = PerGood(p.ProducedUnits, 1); p.ConsumedUnits = PerGood(p.ConsumedUnits, 1); }
                if (p.GoodsHash.Length > 0) p.GoodsHash = _hash;
            }
            if (Moves(K))
                for (int i = 0; i < p.Kind.Length; i++)
                    p.Kind[i] = K![p.Kind[i]] is int n and >= 0 ? (ushort)n
                        : throw new SaveException($"This save has a business of a kind the game content no longer has ('{(p.Kind[i] < K.Old.Length ? K.Old[p.Kind[i]] : "?")}'), and no migration names its replacement.");
            if (Moves(B))
                for (int i = 0; i < p.StationRecipe.Length; i++)
                {
                    if (p.StationRecipe[i] < 0) continue;
                    int n = B![p.StationRecipe[i]];
                    if (n < 0) { Count("business batches"); p.StationRecipe[i] = -1; if (i < p.StationProgress.Length) p.StationProgress[i] = 0; }   // idle: its inputs were consumed at the start
                    else p.StationRecipe[i] = n;
                }
            if (Moves(X))
                for (int i = 0; i < p.JobKind.Length; i++)
                    p.JobKind[i] = X![p.JobKind[i]] is int n and >= 0 ? (ushort)n
                        : throw new SaveException($"This save has a crew at an extraction the game content no longer has ('{(p.JobKind[i] < X.Old.Length ? X.Old[p.JobKind[i]] : "?")}').");
        }

        /// <summary>
        /// P5-F: the growth section names business kinds by index too (a project's kind, the unmet wants keyed <c>settlement x 4096 + kind</c>): mapped by name when the kind table moves. A project of
        /// a kind no longer in the game keeps no kind (-1); an unmet want of one is dropped.
        /// </summary>
        private void Growth(GrowthSave g)
        {
            for (int i = 0; i + 1 < g.PBusiness.Length; i += 2)
            {
                int k = g.PBusiness[i]; if (k < 0) continue;
                int n = k < K!.Old.Length ? K[k] : -1;
                if (n < 0) Count("growth projects' business kinds");
                g.PBusiness[i] = n;
            }
            var unmet = new SortedDictionary<long, int>();
            for (int i = 0; i < g.UnmetKey.Length && i < g.UnmetVal.Length; i++)
            {
                long key = g.UnmetKey[i]; int k = (int)(key % 4096); long s = key / 4096;
                int n = k < K!.Old.Length ? K[k] : -1;
                if (n < 0) { Count("unmet business wants"); continue; }
                unmet[s * 4096 + n] = g.UnmetVal[i];
            }
            g.UnmetKey = [.. unmet.Keys]; g.UnmetVal = [.. unmet.Values];
        }

        /// <summary>P5-F: the ownership section's goods (a directive's kept and floored good, the steward's price memory) and book recipes (the "make N a day" order), by name.</summary>
        private void Ownership(OwnershipSave o)
        {
            foreach (var h in o.Holdings)
            {
                if (GoodsMove)
                {
                    for (int k = 0; k < 2 && k < h.OrderInts.Length; k++) if (h.OrderInts[k] >= 0) { int n = h.OrderInts[k] < G!.Old.Length ? G[h.OrderInts[k]] : -1; if (n < 0) Count("directives' goods"); h.OrderInts[k] = n; }
                    var pg = h.PriceGoods.Select(g => G![g]).ToArray();
                    if (pg.Any(n => n < 0)) { h.PriceGoods = []; h.PriceDays = []; }       // the steward forgets prices of a good no longer in the game: he looks again tomorrow
                    else h.PriceGoods = [.. pg.Select(n => (ushort)n)];
                }
                if (Moves(B) && h.OrderInts.Length > 4 && h.OrderInts[4] >= 0) { int n = B![h.OrderInts[4]]; if (n < 0) Count("directives' recipes"); h.OrderInts[4] = n; }
            }
            if (GoodsMove && o.GoodsHash.Length > 0) o.GoodsHash = _hash;
        }

        private void Markets(MarketsSave m)
        {
            if (G == null) return;
            if (GoodsMove && m.GoodsHash.Length > 0)
            {
                int og = G.Old.Length, ng = G.New.Length, books = m.BookSettlement.Length;
                if (m.RefMicro.Length != books * og) throw new SaveException("The markets section does not match the goods table this save names.");
                // per (book, good) price rows: a good keeps its own; a migrated good's rows go to a target that is new (an existing target keeps its prices); a new good is fresh
                var src = new int[ng]; Array.Fill(src, -1);
                for (int g = 0; g < og; g++) if (G.Same(g)) src[G[g]] = g;
                for (int g = 0; g < og; g++) if (G[g] >= 0 && src[G[g]] < 0) src[G[g]] = g;
                long[] L(long[] a) { var o = new long[books * ng]; for (int b = 0; b < books; b++) for (int n = 0; n < ng; n++) if (src[n] >= 0) o[b * ng + n] = a[b * og + src[n]]; return o; }
                int[] I(int[] a) { var o = new int[books * ng]; for (int b = 0; b < books; b++) for (int n = 0; n < ng; n++) if (src[n] >= 0) o[b * ng + n] = a[b * og + src[n]]; return o; }
                m.RefMicro = L(m.RefMicro); m.DemandEma = L(m.DemandEma); m.StructuralDemand = L(m.StructuralDemand); m.LastTradedDay = I(m.LastTradedDay);
                m.DemandToday = L(m.DemandToday); m.TradedToday = L(m.TradedToday); m.FloorDemand = L(m.FloorDemand);
                m.FreshGoods = [.. Enumerable.Range(0, ng).Where(n => src[n] < 0)];
                // lots (their stock is in the book's store, which the economy section remaps the same way)
                var keep = Enumerable.Range(0, m.LotId.Length).Where(i => G[m.LotGood[i]] >= 0).ToArray();
                Count("market lots", m.LotId.Length - keep.Length);
                m.LotBook = [.. keep.Select(i => m.LotBook[i])]; m.LotGood = [.. keep.Select(i => (ushort)G[m.LotGood[i]])]; m.LotId = [.. keep.Select(i => m.LotId[i])];
                m.LotOwner = [.. keep.Select(i => m.LotOwner[i])]; m.LotOwnerKind = [.. keep.Select(i => m.LotOwnerKind[i])]; m.LotQty = [.. keep.Select(i => m.LotQty[i])];
                m.LotFloor = [.. keep.Select(i => m.LotFloor[i])]; m.LotListedDay = [.. keep.Select(i => m.LotListedDay[i])]; m.LotMerchant = [.. keep.Select(i => m.LotMerchant[i])];
                // buy orders (they hold no coin: the buyer pays at the fill)
                var ko = Enumerable.Range(0, m.OrderGood.Length).Where(i => G[m.OrderGood[i]] >= 0).ToArray();
                Count("buy orders", m.OrderGood.Length - ko.Length);
                m.OrderBook = [.. ko.Select(i => m.OrderBook[i])]; m.OrderBuyer = [.. ko.Select(i => m.OrderBuyer[i])]; m.OrderGood = [.. ko.Select(i => (ushort)G[m.OrderGood[i]])];
                m.OrderQty = [.. ko.Select(i => m.OrderQty[i])]; m.OrderRatio = [.. ko.Select(i => m.OrderRatio[i])]; m.OrderPriority = [.. ko.Select(i => m.OrderPriority[i])];
                m.OrderHookKind = [.. ko.Select(i => m.OrderHookKind[i])]; m.OrderHookArg = [.. ko.Select(i => m.OrderHookArg[i])];
                if (m.OrderMaxSpend.Length > 0) m.OrderMaxSpend = [.. ko.Select(i => i < m.OrderMaxSpend.Length ? m.OrderMaxSpend[i] : 0)];
                var ki = Enumerable.Range(0, m.ImportGood.Length).Where(i => G[m.ImportGood[i]] >= 0).ToArray();
                Count("import requests", m.ImportGood.Length - ki.Length);
                m.ImportBook = [.. ki.Select(i => m.ImportBook[i])]; m.ImportGood = [.. ki.Select(i => (ushort)G[m.ImportGood[i]])]; m.ImportQty = [.. ki.Select(i => m.ImportQty[i])];
                m.ImportDay = [.. ki.Select(i => m.ImportDay[i])]; m.ImportMerchant = [.. ki.Select(i => m.ImportMerchant[i])];
            }
            if (m.GoodsHash.Length > 0) m.GoodsHash = _hash;
        }

        private void Continent(ContinentSave c)
        {
            foreach (var car in c.Caravans)
            {
                var keep = Enumerable.Range(0, car.WishGood.Length).Where(i => G![car.WishGood[i]] >= 0).ToArray();
                Count("caravan cargo lines", car.WishGood.Length - keep.Length);
                car.WishDest = car.WishDest.Length == car.WishGood.Length ? [.. keep.Select(i => car.WishDest[i])] : car.WishDest;
                car.WishUnits = [.. keep.Select(i => car.WishUnits[i])]; car.WishGood = [.. keep.Select(i => (ushort)G![car.WishGood[i]])];
            }
        }

        private void Mounts(MountsSave m)
        {
            foreach (var v in m.Vehicles) (v.CargoGood, v.CargoQty) = Rows(v.CargoGood, v.CargoQty);
            (m.HeapGood, m.HeapQty) = Rows(m.HeapGood, m.HeapQty);
        }

        private void Travel(TravelSave t)
        {
            void Camp(CampSave? c) { if (c != null) (c.StockGood, c.StockQty) = Rows(c.StockGood, c.StockQty); }
            Camp(t.Camp); foreach (var c in t.OldCamps) Camp(c);
        }

        private void Party(HeroPartySave p)
        {
            // where each good is carried: a good's own entry wins over a migrated one
            var stacks = new SortedDictionary<ushort, ulong>();
            foreach (bool pass in new[] { true, false })
                for (int i = 0; i < p.StackGood.Length; i++)
                    if (G!.Same(p.StackGood[i]) == pass && G[p.StackGood[i]] is int n and >= 0) stacks.TryAdd((ushort)n, p.StackIn[i]);
            p.StackGood = [.. stacks.Keys]; p.StackIn = [.. stacks.Values];
            var notes = new SortedDictionary<(int Book, ushort Good), int>();
            foreach (bool pass in new[] { true, false })
                for (int i = 0; i < p.NoteGood.Length; i++)
                    if (G!.Same(p.NoteGood[i]) == pass && G[p.NoteGood[i]] is int n and >= 0) notes.TryAdd((p.NoteBook[i], (ushort)n), i);
            Count("price notes", p.NoteGood.Count(g => G![g] < 0));
            var rows = notes.ToList();
            p.NoteAsk = [.. rows.Select(r => p.NoteAsk[r.Value])]; p.NoteBid = [.. rows.Select(r => p.NoteBid[r.Value])]; p.NoteStock = [.. rows.Select(r => p.NoteStock[r.Value])];
            p.NoteDay = [.. rows.Select(r => p.NoteDay[r.Value])]; p.NoteBook = [.. rows.Select(r => r.Key.Book)]; p.NoteGood = [.. rows.Select(r => r.Key.Good)];
            p.Traded = [.. p.Traded.Select(g => G![g]).Where(n => n >= 0).Select(n => (ushort)n).Distinct().Order()];
        }

        private void Pending(HeroPendingSave s)
        {
            var keep = new List<HeroCommandSave>();
            foreach (var c in s.Commands)
            {
                if (R != null && Moves(R)) { int r = R[c.Recipe]; if (r < 0) { Count("hero commands"); continue; } c.Recipe = (ushort)r; }
                if (GoodsMove)
                {
                    int n = G![c.Good]; if (n < 0) { c.Good = 0; c.Qty = 0; Count("hero commands' goods"); } else c.Good = (ushort)n;
                    (c.GiveGood, c.GiveQty) = Lines(c.GiveGood, c.GiveQty); (c.TakeGood, c.TakeQty) = Lines(c.TakeGood, c.TakeQty);
                }
                keep.Add(c);
            }
            s.Commands = keep;
        }

        private (ushort[], long[]) Lines(ushort[] g, long[] q)
        {
            var k = Enumerable.Range(0, g.Length).Where(i => G![g[i]] >= 0).ToArray();
            return ([.. k.Select(i => (ushort)G![g[i]])], [.. k.Select(i => i < q.Length ? q[i] : 0)]);
        }

        private void Construction(ConstructionSave c)
        {
            // counters of goods built into pieces and taken back out (by good): a dropped good's counter goes with it
            (ushort[], long[]) Counter(ushort[] g, long[] v)
            {
                var acc = new SortedDictionary<ushort, long>();
                for (int i = 0; i < g.Length && i < v.Length; i++) { int n = G![g[i]]; if (n >= 0) acc[(ushort)n] = checked(acc.GetValueOrDefault((ushort)n) + v[i]); }
                return ([.. acc.Keys], [.. acc.Values]);
            }
            (c.MatGood, c.MatG) = Counter(c.MatGood, c.MatG); (c.DematGood, c.DematG) = Counter(c.DematGood, c.DematG);
        }

        private void Crafting(CraftingSave s)
        {
            int Recipe(ushort r, string what)
            {
                if (!Moves(R)) return r;
                int n = R![r];
                if (n < 0) throw new SaveException($"This save has {what} of a recipe the game content no longer has ('{(r < R.Old.Length ? R.Old[r] : "?")}'), and no migration names its replacement.");
                return n;
            }
            foreach (var j in s.Jobs) j.Recipe = (ushort)Recipe(j.Recipe, "an unfinished craft");
            foreach (var c in s.Commissions ?? []) c.Recipe = (ushort)Recipe(c.Recipe, "a commission");
            foreach (var q in s.Queue ?? []) q.Recipe = (ushort)Recipe(q.Recipe, "a queued craft");
            int Opt(ushort r) => Moves(R) ? R![r] : r;
            // records of what crafted items were made of
            var recs = new List<CraftRecordRow>();
            foreach (var r in s.Records)
            {
                int n = Opt(r.Recipe); if (n < 0) { Count("craft records"); continue; }
                r.Recipe = (ushort)n;
                if (GoodsMove) (r.Goods, r.Qty) = Lines(r.Goods, r.Qty);
                recs.Add(r);
            }
            s.Records = recs;
            if (Moves(R))
            {
                var known = new SortedSet<(ulong, ushort)>();
                for (int i = 0; i < s.RecipeId.Length; i++) { int n = R![s.RecipeId[i]]; if (n < 0) Count("known recipes"); else known.Add((s.RecipePerson[i], (ushort)n)); }
                s.RecipePerson = [.. known.Select(k => k.Item1)]; s.RecipeId = [.. known.Select(k => k.Item2)];
                var watch = new SortedDictionary<(ulong, ushort), int>();
                for (int i = 0; i < s.WatchRecipe.Length; i++) { int n = R![s.WatchRecipe[i]]; if (n < 0) Count("recipes being watched"); else watch.TryAdd((s.WatchPerson[i], (ushort)n), i); }
                var wr = watch.ToList();
                s.WatchSec = [.. wr.Select(w => s.WatchSec[w.Value])]; s.WatchTries = [.. wr.Select(w => s.WatchTries[w.Value])];
                s.WatchPerson = [.. wr.Select(w => w.Key.Item1)]; s.WatchRecipe = [.. wr.Select(w => w.Key.Item2)];
                var sess = new List<TeachRow>();
                foreach (var t in s.Sessions) { int n = R![t.Recipe]; if (n < 0) { Count("teaching sessions"); continue; } t.Recipe = (ushort)n; sess.Add(t); }
                s.Sessions = sess;
                foreach (var a in s.Apprenticeships ?? []) a.Learnt = [.. a.Learnt.Select(r => R![r]).Where(n => n >= 0).Select(n => (ushort)n).Distinct()];
                var off = new List<GuildOffenceRow>();
                foreach (var f in s.Offences ?? []) { int n = R![f.Recipe]; if (n < 0) { Count("guild offences"); continue; } f.Recipe = (ushort)n; off.Add(f); }
                s.Offences = off;
            }
            if (Moves(P))
            {
                var prof = new SortedDictionary<(ulong, ushort), long>();
                for (int i = 0; i < s.ProfId.Length; i++) { int n = P![s.ProfId[i]]; if (n < 0) Count("proficiencies"); else prof.TryAdd((s.ProfPerson[i], (ushort)n), s.ProfRaw[i]); }
                s.ProfPerson = [.. prof.Keys.Select(k => k.Item1)]; s.ProfId = [.. prof.Keys.Select(k => k.Item2)]; s.ProfRaw = [.. prof.Values];
            }
        }

        private void Jobs(JobsSave s)
        {
            foreach (var j in s.Jobs)
            {
                if (j.Kind is not ((byte)Ravensguard.Sim.Jobs.OddJobKind.Haul or (byte)Ravensguard.Sim.Jobs.OddJobKind.Deliver or (byte)Ravensguard.Sim.Jobs.OddJobKind.Fetch) || j.P.Length < 6) continue;
                int n = (ulong)j.P[4] <= ushort.MaxValue ? G![(int)j.P[4]] : -1;
                if (n < 0) { Count("haul jobs' loads"); j.P[4] = 0; j.P[5] = 0; } else j.P[4] = n;      // a load of nothing: the job still runs, carrying nothing
            }
        }

        private void Autopilot(AutopilotSave s)
        {
            foreach (var st in s.States)
                foreach (var d in st.Queue)
                {
                    if (d.Kind != (byte)Ravensguard.Sim.Autopilot.DirectiveKind.TradeRoute || d.Params.Length < 2) continue;
                    int n = (ulong)d.Params[1] <= ushort.MaxValue ? G![(int)d.Params[1]] : -1;
                    if (n < 0) { Count("trade routes"); d.Params[1] = 0; if (d.State is (byte)Ravensguard.Sim.Autopilot.DirectiveState.Pending or (byte)Ravensguard.Sim.Autopilot.DirectiveState.Active) d.State = (byte)Ravensguard.Sim.Autopilot.DirectiveState.Failed; }
                    else d.Params[1] = n;
                }
        }
    }
}
