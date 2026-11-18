using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Growth;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// The Phase 3 code and bug check, simulation half (docs/reviews/phase-3.md): one regression test or more per finding, each failing before its fix. The reviewer's probes
/// (<c>review4/probe</c>: traveller ids, the job table's counters, the cut report.json) are F2, F5 and F10 here.
/// </summary>
public class Phase3SimReviewTests(ITestOutputHelper output)
{
    // ---------------------------------------------------------------- F1: growth on the worker against chunk generation on the pool

    private static List<LotRecord> Plots(SoakWorld w, int count)
    {
        // the same plots on any copy of the seed's region: each found after the previous one was built, villages in turn
        var reg = w.Pop.Region; var res = new List<LotRecord>();
        var villages = Enumerable.Range(0, reg.Settlements.Count).Where(s => reg.Settlements[s].Class == SettlementClass.Village).ToList();
        for (int k = 0, v = 0; res.Count < count && k < count * 8; k++, v++)
        {
            int s = villages[v % villages.Count];
            if (!RegionGrowth.FindPlot(reg, s, 16, 38, 30, [], out var rect, out byte f)) continue;
            var rec = new LotRecord(s, reg.Settlements[s].Plan.Lots.Count, true, LotUse.Cottage, DwellingType.Cottage, "peasant_cottage", rect, f, 0, 5, 1);
            RegionGrowth.AddLot(reg, w.Pop, rec); res.Add(rec);
        }
        return res;
    }

    private static List<(int X, int Y)> ChunksOf(IEnumerable<LotRecord> lots)
    {
        var keys = new SortedSet<(int, int)>();
        foreach (var l in lots)
            for (int y = (l.Rect.Y - 8) / 32; y <= (l.Rect.Bottom + 8) / 32; y++)
                for (int x = (l.Rect.X - 8) / 32; x <= (l.Rect.Right + 8) / 32; x++) keys.Add((x, y));
        return [.. keys];
    }

    [Fact]
    public void F1_Growth_applying_lots_while_chunks_generate_in_parallel_agrees_with_the_records()
    {
        const int Lots = 10, Rounds = 2;
        // the reference: the same lots built one by one on a twin, every chunk they touch generated at every version
        using var twin = SoakWorld.Create(withTicker: false);
        var fresh = new ChunkGenerator(twin.Pop.Region, twin.Pop, EconFixture.Content);   // the versions are read from this one, built before any lot
        var plots = new List<LotRecord>(); List<(int X, int Y)> chunks;
        var refs = new Dictionary<(int, int), HashSet<string>>();
        {
            using var probe = SoakWorld.Create(withTicker: false);
            plots = Plots(probe, Lots);
            chunks = ChunksOf(plots);
        }
        Assert.Equal(Lots, plots.Count);
        void Reference() { foreach (var c in chunks) { if (!refs.TryGetValue(c, out var set)) refs[c] = set = []; set.Add(fresh.GenerateLevels(c.X, c.Y).ContentHash); } }
        Reference();
        foreach (var rec in plots) { RegionGrowth.AddLot(twin.Pop.Region, twin.Pop, rec); Reference(); }
        var final = chunks.ToDictionary(c => c, c => fresh.GenerateLevels(c.X, c.Y));
        output.WriteLine($"{plots.Count} lots, {chunks.Count} chunks, {refs.Values.Sum(s => s.Count)} reference versions");

        for (int round = 0; round < Rounds; round++)
        {
            using var w = SoakWorld.Create(withTicker: false);
            var reg = w.Pop.Region;
            var gen = new ChunkGenerator(reg, w.Pop, EconFixture.Content);
            var store = new ChunkStore();
            var writer = new ChunkDeltaWriter(gen, store);
            var streamer = new ChunkStreamer(gen, store);
            for (int i = 0; i < chunks.Count; i += 3) streamer.LoadSync(chunks[i].X, chunks[i].Y);     // a third visited before anything is built
            var errors = new ConcurrentQueue<string>(); var seen = new ConcurrentQueue<((int, int) C, string Hash)>();
            int done = 0;
            var growth = new Thread(() =>
            {
                try
                {
                    var rng = new Random(round);
                    foreach (var rec in plots)
                    {
                        RegionGrowth.Change(reg, writer, [new RectM(rec.Rect.X - 6, rec.Rect.Y - 6, rec.Rect.W + 12, rec.Rect.H + 12)], () => RegionGrowth.AddLot(reg, w.Pop, rec));
                        Thread.SpinWait(rng.Next(20_000));
                    }
                }
                catch (Exception e) { errors.Enqueue("growth: " + e); }
                finally { Volatile.Write(ref done, 1); }
            });
            var pool = Enumerable.Range(0, 3).Select(t => new Thread(() =>
            {
                try
                {
                    int n = 0;
                    while (Volatile.Read(ref done) == 0 || n < chunks.Count)
                    {
                        var c = chunks[(n++ * 7 + t * 5) % chunks.Count];
                        seen.Enqueue((c, gen.GenerateLevels(c.X, c.Y).ContentHash));
                    }
                }
                catch (Exception e) { errors.Enqueue($"pool {t}: " + e); }
            })).ToList();
            var visitor = new Thread(() =>
            {
                try
                {
                    // the streamer's thread: first visits of the other chunks while the lots go up, then unload and reload a few
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (i % 3 == 0) continue;
                        streamer.LoadSync(chunks[i].X, chunks[i].Y);
                        Thread.SpinWait(5_000);
                    }
                    for (int i = 0; i < chunks.Count; i += 2) { streamer.Unload(chunks[i].X, chunks[i].Y); streamer.LoadSync(chunks[i].X, chunks[i].Y); }
                }
                catch (Exception e) { errors.Enqueue("streamer: " + e); }
            });
            growth.Start(); foreach (var p in pool) p.Start(); visitor.Start();
            growth.Join(); foreach (var p in pool) p.Join(); visitor.Join();
            Assert.True(errors.IsEmpty, string.Join("\n", errors));
            int bad = seen.Count(s => !refs[s.C].Contains(s.Hash));
            output.WriteLine($"round {round}: {seen.Count} chunks generated in parallel, {bad} matching no version of the records");
            Assert.Equal(0, bad);
            // every chunk as the store holds it (first-visit snapshot plus growth's deltas) is the chunk of the grown records
            foreach (var c in chunks)
                foreach (var (z, want) in final[c].Levels)
                {
                    if (want.IsEmpty) continue;
                    var have = store.Rebuild(new ChunkKey(c.X, c.Y, z));
                    Assert.True(have != null, $"chunk {c} level {z} was never recorded");
                    Assert.True(want.ContentHash() == have!.ContentHash(), $"round {round}: chunk {c} level {z} in the store differs from the grown records");
                }
        }
    }
    // ---------------------------------------------------------------- F2: generated travellers in a reserved id space

    [Fact]
    public void F2_Generated_traveller_camp_and_carcass_ids_never_equal_a_record_id()
    {
        // probe P1: traveller 1 was person 1 of region 254, the region of grid square (14, 15)
        var real = EntityId.Make(IdKind.Person, EntityId.RegionBase(new RegionCoord(14, 15).Id) | 1);
        Assert.NotEqual(real, EncounterPlacement.TravellerId(1));
        var goods = EconFixture.Catalog();
        var st = new TravelState(EconFixture.Content, 1, goods, new MassLedger(goods.Count));
        var d = EconFixture.Content.All<EncounterDef>().First(e => e.Groups.Count > 0);
        var cast = EncounterPlacement.Cast(st, d, d.Groups[0], 500, 0, 0, 0);
        var records = new HashSet<EntityId>();
        foreach (int region in new[] { 0, 252, 253, 254, 255 })
        {
            var ids = IdAllocator.ForRegion(region);
            for (int i = 0; i < 2000; i++) foreach (var k in Enum.GetValues<IdKind>()) records.Add(ids.Allocate(k));
            for (ulong n = 1; n <= 2000; n++) records.Add(JobTable.MakeId(region, n));
        }
        Assert.Equal(500, cast.Count);
        Assert.All(cast, id => { Assert.Equal(IdKind.Person, id.Kind); Assert.True(id.IsReservedSerial); Assert.DoesNotContain(id, records); });
        for (ulong n = 1; n <= 2000; n++)
        {
            Assert.DoesNotContain(TravelState.CampId(n), records); Assert.DoesNotContain(TravelState.CarcassId(n), records);
        }
        Assert.Equal(cast.Count, cast.Distinct().Count());
    }

    // ---------------------------------------------------------------- F3: a bad command answers Invalid and never faults the tick

    [Fact]
    public void F3_A_malformed_or_throwing_command_is_answered_invalid_and_the_desk_goes_on()
    {
        var w = new TradeWorld(heroCrowns: 50);
        int i = Adult(w); var id = w.State.Population.People.IdOf(i);
        var flour = w.G("flour"); w.Ctx.Pack.Create(flour, 5_000, CreateReason.Seed);
        long coin0 = w.TotalCoin; var g0 = w.GoodsTotals();
        // a negative quantity reached Store.Move and threw ArgumentOutOfRangeException out of the barrier
        var neg = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Give = [(flour, -1_000)], CoinTake = 0 });
        Assert.Equal(HeroResultCode.Invalid, neg.Code);
        foreach (var bad in new[]
        {
            new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Give = [(flour, 100)], CoinGive = -5 },
            new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Take = [(ushort.MaxValue - 1, 10)] },
            new HeroCommand { Kind = HeroCommandKind.Hire, Person = id, Wage = -2_000, Days = 3 },
            new HeroCommand { Kind = HeroCommandKind.Buy, Good = flour, Qty = -3 },
            new HeroCommand { Kind = HeroCommandKind.EncounterPay, CoinGive = -100 },
        })
            Assert.Equal(HeroResultCode.Invalid, w.Do(bad).Code);
        // a command whose application throws (here the autopilot's barrier check) is caught, counted and logged; the next command still runs
        var seen = new List<string>();
        w.Desk.Gate = (_, _) => throw new InvalidOperationException("boom");
        w.Desk.Faulted = (c, ex) => seen.Add(ex.Message);
        var thrown = w.Do(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id, Origin = CommandOrigin.Autopilot });
        Assert.Equal(HeroResultCode.Invalid, thrown.Code);
        Assert.Equal(1, w.Desk.Faults); Assert.Equal(["boom"], seen); Assert.Contains("boom", w.Desk.LastFault);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id }).Ok);
        Assert.Equal(coin0, w.TotalCoin); Assert.Equal(g0, w.GoodsTotals());
    }

    private static int Adult(TradeWorld w, int skip = 0)
    {
        var p = w.State.Population.People; int day = (int)(w.Sec / 86400);
        for (int i = 0; i < p.Count; i++) if (p.IsAlive(i) && p.AgeYears(i, day) is >= 20 and < 50 && p.Household[i] >= 0 && skip-- <= 0) return i;
        throw new InvalidOperationException("no adult");
    }

    // ---------------------------------------------------------------- F4: barter lines named twice

    [Fact]
    public void F4_Duplicate_barter_lines_are_summed_so_they_cannot_be_credited_twice()
    {
        var w = new TradeWorld(heroCrowns: 50);
        int i = Adult(w, 5); var id = w.State.Population.People.IdOf(i); int hh = w.State.Population.People.Household[i];
        w.Econ.Ledger.Mint(w.Econ.HouseholdPurse[hh], new Coin(5_000_000), SourceReason.Seed);
        var salt = w.G("salt"); w.Ctx.Pack.Create(salt, 2_000, CreateReason.Seed);
        long coin0 = w.TotalCoin, hero0 = w.Coin; var g0 = w.GoodsTotals();
        // what 2 kg of salt is worth to them: a refusal names it
        var ask = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Give = [(salt, 2_000)], CoinTake = 4_000_000 });
        Assert.Equal(HeroResultCode.Refused, ask.Code);
        long worth = ask.Mites;
        Assert.True(worth > 10, $"worth {worth}");
        // the same 2 kg named twice was checked against the pack twice and credited twice: coin for 4 kg, 2 kg given (the second move failed unnoticed)
        var twice = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Give = [(salt, 2_000), (salt, 2_000)], CoinTake = worth * 2 - 5 });
        Assert.NotEqual(HeroResultCode.Ok, twice.Code);
        Assert.Equal(hero0, w.Coin); Assert.Equal(2_000, w.Ctx.Pack.Qty(salt));
        // the same item twice is one item's worth
        if (w.Econ.Goods.TryIdOf("knife", out ushort kg))
        {
            w.Ctx.Pack.Create(kg, 1, CreateReason.Seed);
            if (w.Ctx.Factory.Materialise(w.Ctx.Pack, w.Econ.Items, w.State.Ids, kg, w.Ctx.Party.Hero) is { } k)
            {
                long one = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, GiveItems = [k], CoinTake = 4_000_000 }).Mites;
                long two = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, GiveItems = [k, k], CoinTake = 4_000_000 }).Mites;
                Assert.Equal(one, two);
                Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, GiveItems = [k, k, k] }).Ok);
                Assert.Equal(id, w.Econ.Items.Get(k).Owner);
            }
        }
        Assert.Equal(coin0, w.TotalCoin);
        Assert.True(w.Econ.MassAuditHolds());
        output.WriteLine($"2 kg of salt is worth {worth} mites to them");
    }

    // ---------------------------------------------------------------- F5: counters of an emptied table are saved

    [Fact]
    public void F5_Counters_survive_a_save_when_the_tables_are_empty()
    {
        var econ = EconFixture.NewEconomy(true);
        var world = EconFixture.WorldWith(econ);
        world.Jobs = new JobTable { NextSerial = 9, LastPostedDay = 400 };                 // probe P3: every job gone, the serial and the day advanced
        world.Travel = new TravelState(EconFixture.Content, 1, econ.Goods, econ.Mass) { Hosted = true, OpenStore = gid => econ.OpenStore(StoreKind.Open, gid), NextTraveller = 5, NextCarcass = 3, NextSnare = 2, LastMealSec = 7_200 };
        var rides = new RideBook(EconFixture.Content, econ, 1); rides.Animals.NextSerial = 4; rides.Vehicles.NextSerial = 3; world.Mounts = rides;
        Assert.False(world.Jobs.IsEmpty); Assert.False(world.Travel.IsEmpty); Assert.False(rides.IsEmpty);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"))), EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Jobs); Assert.Equal(9UL, back.Jobs!.NextSerial); Assert.Equal(400, back.Jobs.LastPostedDay);
        Assert.NotNull(back.Travel); Assert.Equal(5UL, back.Travel!.NextTraveller); Assert.Equal(3UL, back.Travel.NextCarcass); Assert.Equal(2, back.Travel.NextSnare); Assert.Equal(7_200, back.Travel.LastMealSec);
        Assert.NotNull(back.Mounts); Assert.Equal(4UL, back.Mounts!.Animals.NextSerial); Assert.Equal(3UL, back.Mounts.Vehicles.NextSerial);
        // a fresh table still saves nothing
        Assert.True(new JobTable().IsEmpty);
        Assert.True(new TravelState(EconFixture.Content, 1, econ.Goods, econ.Mass).IsEmpty);
    }

    // ---------------------------------------------------------------- F6: commands waiting for their hour across a save

    [Fact]
    public void F6_Save_load_continue_with_commands_waiting_equals_the_uninterrupted_run()
    {
        TradeWorld Make()
        {
            var w = new TradeWorld(heroCrowns: 400);
            w.State.Markets = null;                                                       // the bare world restores no markets; barter needs none
            return w;
        }
        (List<HeroCommand> Cmds, long Hour) Script(TradeWorld w)
        {
            long hour = w.Sec / 3600;
            int i = Adult(w, 3); var id = w.State.Population.People.IdOf(i); var store = w.Econ.HouseholdStore[w.State.Population.People.Household[i]];
            ushort g = store.Rows().OrderByDescending(r => r.Qty).First().Good;
            return ([
                new HeroCommand { Kind = HeroCommandKind.Talk, Person = id, IssuedSec = (hour + 1) * 3600 + 10 },
                new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Take = [(g, Math.Min(500, store.Qty(g)))], CoinGive = 30_000, IssuedSec = (hour + 1) * 3600 + 20 },
                new HeroCommand { Kind = HeroCommandKind.Talk, Person = id, IssuedSec = (hour + 2) * 3600 + 5 },
            ], hour);
        }
        string Results(HeroDesk d) => string.Join("\n", d.View!.Results.Where(r => r.Seq > 0).Select(r => $"{r.Seq} {r.Code} {r.Mites} {r.Text}"));
        // the straight run
        var a = Make(); var (cmds, hour) = Script(a);
        foreach (var c in cmds) a.Desk.Issue(c);
        Assert.Equal(0, a.Desk.ApplyDue(a.Ctx, hour));
        a.Desk.ApplyDue(a.Ctx, hour + 1); a.Desk.ApplyDue(a.Ctx, hour + 2);
        var straight = SaveCodec.Encode(WorldSave.Capture(a.State, EconFixture.ContentHash, "0.1.0"));
        // the same, saved while all three wait, loaded, then continued
        var b = Make(); (cmds, hour) = Script(b);
        foreach (var c in cmds) b.Desk.Issue(c);
        Assert.Equal(0, b.Desk.ApplyDue(b.Ctx, hour));
        b.State.HeroPendingSource = b.Desk.PendingCommands;
        var mid = SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(b.State, EconFixture.ContentHash, "0.1.0")));
        Assert.Contains(HeroPendingSave.SectionName, mid.Sections.Keys);
        var back = WorldSave.Restore(mid, EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(3, back.HeroPending.Count);
        var desk = new HeroDesk(back.Party!.LastSeq); desk.RestorePending(back.HeroPending); back.HeroPending = [];
        var ctx = new HeroContext(back, back.Party, EconFixture.Content);
        desk.ApplyDue(ctx, hour + 1); desk.ApplyDue(ctx, hour + 2);
        Assert.Equal(Results(a.Desk), Results(desk));
        Assert.Equal(straight, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        Assert.True(desk.Issue(new HeroCommand { Kind = HeroCommandKind.Talk }).Seq > cmds.Count);    // numbering goes on after the restored commands
        // the autopilot's booked spends for waiting commands are saved too, so they are settled once
        var book = new AutopilotBook(); book.Reserved[7] = (EntityId.Make(IdKind.Person, 3), 12, 4_500);
        var rb = SaveCodec.DeserializeCompressed<AutopilotSave>(SaveCodec.SerializeCompressed(AutopilotSave.Capture(book))).Restore();
        Assert.Equal(book.Reserved, rb.Reserved); Assert.False(book.IsEmpty);
    }

    // ---------------------------------------------------------------- F7: integer directions and cube root

    private static string SimSource(string rel) => File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!, "..", "..")), "src", "Ravensguard.Sim", rel));
    /// <summary>The source without its comments (what the checks below look for is code, not the prose that explains the change).</summary>
    private static string SimCode(string rel) => string.Join("\n", SimSource(rel).Split('\n').Select(l => { int c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l[..c] : l; }));

    [Fact]
    public void F7_Directions_and_the_cube_root_are_integer_and_match_the_doubles()
    {
        foreach (var f in new[] { "Onboarding/OnboardingSetup.cs", "Onboarding/PracticeYard.cs", "Inventory/ContainerRules.cs" })
            Assert.DoesNotMatch(@"Math\.(Cos|Sin|Cbrt|Pow|Sqrt)\(", SimCode(f));
        for (int k = 0; k < 3600; k++)
        {
            var (c, s) = IntMath.CosSinQ30(k, 3600); double a = k / 3600.0 * 2 * Math.PI;
            Assert.InRange(c - Math.Cos(a) * IntMath.One30, -2, 2); Assert.InRange(s - Math.Sin(a) * IntMath.One30, -2, 2);
        }
        var eight = IntMath.Directions(8);
        Assert.Equal((IntMath.One30, 0L), eight[0]); Assert.Equal((0L, IntMath.One30), eight[2]); Assert.Equal((-IntMath.One30, 0L), eight[4]); Assert.Equal((759_250_125L, 759_250_125L), eight[1]);
        for (long n = 0; n < 200_000; n++) Assert.Equal((long)Math.Round(Math.Cbrt(n)), IntMath.CbrtRound(n));
        foreach (long n in new long[] { 1L << 62, long.MaxValue, 999_999_999_999 }) { long c = IntMath.CbrtFloor(n); Assert.True((Int128)c * c * c <= n && (Int128)(c + 1) * (c + 1) * (c + 1) > n); }
    }

    // ---------------------------------------------------------------- F8: a failure wastes its share of a small stack

    [Fact]
    public void F8_Waste_of_a_small_stack_is_its_share_on_average_and_deterministic()
    {
        Assert.DoesNotMatch(@"\? q \* pct / 100 : q \* pct / 100", SimCode("Crafting/CraftRunner.cs"));
        long lost = 0; int n = 2000;
        for (int k = 1; k <= n; k++)
        {
            var job = EntityId.Make(IdKind.Other, (ulong)k);
            long l = CraftRunner.WasteOf(1234, job, 7, 3, 10);
            Assert.InRange(l, 0, 1); lost += l;
            Assert.Equal(l, CraftRunner.WasteOf(1234, job, 7, 3, 10));
        }
        Assert.InRange(lost * 1000 / n, 250, 350);                                       // 3 x 10 percent = 0.3 a stack; truncation lost none
        Assert.Equal(25, CraftRunner.WasteOf(1, EntityId.Make(IdKind.Other, 1), 0, 250, 10));
        Assert.Equal(40, CraftRunner.WasteOf(1, EntityId.Make(IdKind.Other, 1), 0, 40, 100));
        Assert.Equal(0, CraftRunner.WasteOf(1, EntityId.Make(IdKind.Other, 1), 0, 40, 0));
    }

    // ---------------------------------------------------------------- F9: severance as paid

    [Fact]
    public void F9_Dismiss_reports_the_severance_actually_paid()
    {
        var w = new TradeWorld(heroCrowns: 300);
        int i = Adult(w, 2); var id = w.State.Population.People.IdOf(i);
        var hire = w.Do(new HeroCommand { Kind = HeroCommandKind.Hire, Person = id, Wage = 2_000, Days = 5 });
        Assert.True(hire.Ok, hire.Text);
        // the hero spends everything first: nothing is left for the severance
        var sink = w.Party(PartyKind.Business, 0);
        Assert.True(w.Econ.Ledger.Transfer(w.Ctx.Purse, sink.Purse, new Coin(w.Coin), TransferReason.Trade));
        Assert.True(Recruiting.Severance(w.Ctx.Trade, 2_000) > 0);
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Dismiss, Person = id });
        Assert.True(r.Ok);
        Assert.Equal(0, r.Mites);
        Assert.DoesNotContain(" with ", r.Text);
    }

    // ---------------------------------------------------------------- F10, F11: report.json and the stored consent

    [Fact]
    public void F10_A_report_json_over_its_cap_still_parses()
    {
        using var dir = new Ravensguard.Sim.Tests.Save.TempDir();
        var consent = new ConsentStore(dir.Path); consent.Accept();
        var rb = new ReportBuilder(consent, new Redactor("/nowhere", "zz"), Path.Combine(dir.Path, "reports"));
        var where = new Dictionary<string, string> { ["x"] = new string('a', 200_000) };          // probe P5
        for (int k = 0; k < 300; k++) where[$"k{k:000}"] = new string('b', 1_999) + "\U0001F600";   // a surrogate pair at the cut
        var res = rb.Write(new ReportRequest { Where = where, WhatHappened = new string('c', 1_999) + "\U0001F600" });
        using var z = ZipFile.OpenRead(res.Path); using var rd = new StreamReader(z.GetEntry("report.json")!.Open()); var txt = rd.ReadToEnd();
        var node = JsonNode.Parse(txt)!;
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(txt) <= ReportBuilder.ReportJsonMaxBytes);
        Assert.NotNull(node["id"]); Assert.NotNull(node["what_happened"]);
    }

    [Fact]
    public void F11_The_stored_consent_limits_what_a_report_holds()
    {
        using var dir = new Ravensguard.Sim.Tests.Save.TempDir();
        var log = Path.Combine(dir.Path, "game.log"); File.WriteAllText(log, "a line\n");
        var save = Path.Combine(dir.Path, "auto.save"); File.WriteAllBytes(save, [1, 2, 3]);
        var req = new ReportRequest { LogPath = log, AutosavePath = save, BugSavePath = save, Screenshot = [9, 9, 9] };          // every Include flag at its default (true)
        string[] Names(ConsentStore c) { var rb = new ReportBuilder(c, new Redactor("/nowhere", "zz"), Path.Combine(dir.Path, "reports")); using var z = ZipFile.OpenRead(rb.Write(req).Path); return [.. z.Entries.Select(e => e.Name)]; }
        var none = new ConsentStore(Path.Combine(dir.Path, "a")); none.Accept(includeSave: false, includeScreenshot: false, includeLog: false);
        var n = Names(none);
        foreach (var f in new[] { "log_tail.txt", "screenshot.png", "autosave.bin", "bug.save" }) Assert.DoesNotContain(f, n);
        var all = new ConsentStore(Path.Combine(dir.Path, "b")); all.Accept();
        var y = Names(all);
        foreach (var f in new[] { "log_tail.txt", "screenshot.png", "autosave.bin", "bug.save" }) Assert.Contains(f, y);
        var preview = new ReportBuilder(none, new Redactor("/nowhere", "zz"), Path.Combine(dir.Path, "reports")).Preview(req);
        Assert.DoesNotContain(preview, p => p.Name is "log_tail.txt" or "screenshot.png" or "autosave.bin" or "bug.save");
    }

    // ---------------------------------------------------------------- F12: start_contract kinds

    [Fact]
    public void F12_Start_contract_posts_haul_and_deliver_and_content_with_another_kind_is_refused()
    {
        var w = new TradeWorld(heroCrowns: 50);
        var p = w.State.Population.People; var e = w.Econ;
        int speaker = Enumerable.Range(0, p.Count).First(i => p.IsAlive(i) && p.Household[i] >= 0 && p.Household[i] < e.HouseholdCount && e.HouseholdStore[p.Household[i]].Rows().Any(r => r.Qty >= 20_000));
        e.Ledger.Mint(e.HouseholdPurse[p.Household[speaker]], new Coin(500_000), SourceReason.Seed);
        long coin0 = w.TotalCoin; var g0 = w.GoodsTotals();
        var haul = DialogueEffects.StartContract(w.State, EconFixture.Content, speaker, new Dictionary<string, string> { ["kind"] = "haul" }, w.Sec);
        var deliver = DialogueEffects.StartContract(w.State, EconFixture.Content, speaker, new Dictionary<string, string> { ["kind"] = "Deliver", ["kg"] = "10" }, w.Sec);
        Assert.NotNull(haul); Assert.NotNull(deliver);
        Assert.Equal(OddJobKind.Haul, haul!.Kind); Assert.Equal(OddJobKind.Deliver, deliver!.Kind);
        foreach (var j in new[] { haul, deliver }) { Assert.True(j.P5 > 0); Assert.Equal(j.SrcOwner, j.DstOwner); Assert.True(j.P2 != j.P0 || j.P3 != j.P1); }
        Assert.Equal(10_000, deliver.P5);
        Assert.Null(DialogueEffects.StartContract(w.State, EconFixture.Content, speaker, new Dictionary<string, string> { ["kind"] = "smuggle" }, w.Sec));
        Assert.Equal(coin0, w.TotalCoin); Assert.Equal(g0, w.GoodsTotals());                   // the wages are in escrow, nothing made or lost
        // at load: an effect naming an unknown kind is an error, the documented ones are not
        var vocab = BaseVocabulary.Create();
        foreach (var (kind, ok) in new[] { ("haul", true), ("deliver", true), ("guard", true), ("first", true), ("loan", true), ("smuggle", false) })
        {
            var report = new ContentReport();
            vocab.Validate(JsonNode.Parse("{\"effects\":[{\"id\":\"start_contract\",\"args\":{\"kind\":\"" + kind + "\"}}]}"), new SourceLocation("t.jsonc", 1, 1), "dialogue 't'", report);
            Assert.Equal(ok, !report.Errors.Any());
        }
    }

    // ---------------------------------------------------------------- F13: ride commands queue

    [Fact]
    public void F13_Two_ride_commands_given_between_steps_both_run_in_order()
    {
        var g = new LevelGrid(40, 20, 0, 1); g.FillRect(0, 0, 39, 19, 0, true);
        var live = new LiveWorld(g, new Ravensguard.Sim.Lighting.LightGrid(g), new Cell3(5, 5, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        var goods = EconFixture.Catalog();
        live.AttachRide(new RideBook(EconFixture.Content, goods, new MassLedger(goods.Count), 1));
        int serial = live.RideMsgSerial;
        var t = new Thread(() => live.RideCommand(RideCode.Unhitch)); t.Start(); t.Join();       // from another thread, as the game's input may
        live.RideCommand(RideCode.Unload);
        Assert.Equal(2, live.PendingRideCommands);
        live.Step();
        Assert.Equal(0, live.PendingRideCommands);
        Assert.Equal(serial + 2, live.RideMsgSerial);                                     // both said something: the first was not overwritten
        Assert.Equal("RIDE_UNLOADED", live.LastRideKey);
    }

    // ---------------------------------------------------------------- F14: tunables in content, keys not English

    [Fact]
    public void F14_Tunables_come_from_content_and_the_desk_returns_keys()
    {
        var cfg = EncounterTable.From(EconFixture.Content).Cfg;
        Assert.Equal((250, 5, 250, 900, 900, 1100, "hare"), (cfg.HuntBasePm, cfg.HuntPerSkillPm, cfg.HuntArmedPm, cfg.HuntMaxPm, cfg.KillMassMinPm, cfg.KillMassMaxPm, cfg.SnareSpecies));
        Assert.Equal(1100, EconFixture.Content.All<CampEventDef>().Single(e => e.Effect == "steal_food").StealG);
        var gt = EconFixture.Content.All<GrowthTuningDef>().Single();
        Assert.Equal((14, 18, 24, 28), (gt.BusinessPlotWidthTownM, gt.BusinessPlotWidthVillageM, gt.BusinessPlotDepthTownM, gt.BusinessPlotDepthVillageM));
        var ob = EconFixture.Content.All<OnboardingSettingsDef>().Single();
        Assert.Equal((1000, 30), (ob.YardMinPopulation, ob.YardBeyondM));
        Assert.DoesNotMatch(@"Math\.Min\(900|\b250 \+ 5|""hare""", SimCode("Travel/Gathering.cs"));
        Assert.DoesNotMatch(@"1100\)", SimCode("Travel/Camp.cs"));
        Assert.DoesNotMatch(@"town \? 14 : 18", SimCode("Growth/GrowthSystem.cs"));
        Assert.DoesNotMatch(@"RadiusM \+ 30|minPopulation = 1000", SimCode("Onboarding/PracticeYard.cs"));
        // no English sentence in the desk: every result is a key of en.csv, which the game resolves
        var sentence = new Regex(@"\$?""[A-Z][a-z]+ [a-z][^""]*""");
        foreach (var f in new[] { "Trade/HeroDesk.cs", "Trade/AbroadDealing.cs" })
            Assert.Empty(sentence.Matches(string.Join("\n", SimSource(f).Split('\n').Where(l => !l.TrimStart().StartsWith("///") && !l.Contains("InvalidOperationException")))).Select(m => m.Value));
        var csv = EnCsv.Load(EnCsv.Find());
        Assert.NotEmpty(csv);
        var used = new Regex(@"(?:\bT|HeroText\.Of)\(""([A-Z0-9_]+)""").Matches(SimSource("Trade/HeroDesk.cs") + SimSource("Trade/AbroadDealing.cs") + SimSource("Autopilot/AutopilotWorld.Actions.cs")).Select(m => m.Groups[1].Value).Where(k => !k.EndsWith('_')).Distinct().ToList();
        Assert.True(used.Count > 90, $"{used.Count} keys");
        Assert.All(used, k => Assert.True(csv.ContainsKey(k), $"{k} is not in en.csv"));
        foreach (var o in Enum.GetValues<SquadOrder>()) Assert.True(csv.ContainsKey("HERO_ORDER_" + o.ToString().ToUpperInvariant()));
        var w = new TradeWorld();
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = w.G("flour"), Qty = 1_000 });
        Assert.Equal("HERO_BOUGHT", r.Message.Key);
        Assert.StartsWith("Bought ", r.Text);
        Assert.Equal("Ann: hold.", HeroText.Of("HERO_ORDER", "Ann", HeroText.Of("HERO_ORDER_HOLD")).Render());
        Assert.Equal("NO_SUCH_KEY(1)", HeroText.Of("NO_SUCH_KEY", 1).Render());
    }

    // ---------------------------------------------------------------- F15: the view's heavy parts are kept

    [Fact]
    public void F15_A_barrier_with_nothing_new_does_not_rescan_the_items_or_copy_the_crafting_state()
    {
        var w = new TradeWorld();
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.VisitMarket }).Ok);
        long scans = w.Desk.MerchantScans, copies = w.Desk.CraftCopies;
        Assert.True(scans >= 1 && copies >= 1);
        long hour = w.Sec / 3600;
        for (int k = 0; k < 5; k++) w.Desk.Publish(w.Ctx, hour);
        Assert.Equal(scans, w.Desk.MerchantScans); Assert.Equal(copies, w.Desk.CraftCopies);
        Assert.Same(w.Desk.View!.Crafting, w.Desk.BuildView(w.Ctx, hour).Crafting);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = w.G("salt"), Qty = 100 }).Ok);
        Assert.True(w.Desk.MerchantScans > scans); Assert.True(w.Desk.CraftCopies > copies);          // a command: both are made again
        w.Desk.Publish(w.Ctx, hour + 24);
        Assert.True(w.Desk.MerchantScans > scans + 1);                                     // the next day too
    }

    // ---------------------------------------------------------------- F16: integer needs, limits and garments

    [Fact]
    public void F16_Needs_round_trips_and_garment_coverage_are_integer_with_the_same_results()
    {
        foreach (var f in new[] { "Behaviour/NeedModel.cs", "Autopilot/LimitGuard.cs", "Clothing/Garments.cs" })
            Assert.DoesNotMatch(@"\bdouble\b|3600\.0|1000\.0", SimCode(f));
        // the doubles' answers, worked again here, against the integer code
        Assert.Equal(26_667, LimitGuard.RoundTripSec(2000));
        for (long m = 0; m <= 6000; m += 7)
            for (int start = 6 * 60; start < 15 * 60; start += 13)
            {
                double rt = 2.0 * m * 1000 / 150 / 60.0;
                Assert.Equal(start + rt <= Ravensguard.Sim.Weather.Daylight.SunsetMinute(480, 53) - 30, LimitGuard.HomeByNightfallOk(480, 53, start, LimitGuard.RoundTripSec(m), 30));
            }
        foreach (var d in EconFixture.Content.All<ArmourShapeDef>())
        {
            var gm = Garment.From(d);
            for (int r = 0; r < Garment.RegionCount; r++)
            {
                double sum = 0; foreach (var part in Garment.Parts[r]) if (d.Coverage.TryGetValue(part, out var c)) sum += c;
                Assert.Equal((int)Math.Round(sum * 1000.0 / Garment.Parts[r].Length), gm.RegionPm[r]);
            }
        }
        var pop = EconFixture.Pop; int checkedN = 0;
        for (int i = 0; i < pop.People.Count && checkedN < 40; i += 97)
        {
            if (!pop.People.IsAlive(i)) continue; checkedN++;
            var a = PersonAnchors.From(pop, i, EconFixture.Seed, 0, 86_400);
            for (long sec = 86_400; sec < 3 * 86_400; sec += 1_013)
            {
                NeedModel.Read(a, sec, out var n, out _);
                Assert.InRange(n.FatiguePm, 0, 1000); Assert.InRange(n.SocialPm, 300, 1000);
            }
        }
        Assert.True(checkedN > 10);
    }
}
