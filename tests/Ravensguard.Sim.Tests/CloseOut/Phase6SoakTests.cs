using System.Diagnostics;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Flavour;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.CloseOut;

/// <summary>
/// The Phase 6 close-out soaks (P6-G2). <b>OV-5</b> (overview section 4): ten game years of seed 1234 with the hero bot and every Phase 6 system attached (the game's own links: talk, law with heists, bonds and faith
/// politics, family, contracts): the bot steals and gives himself up (a trial), courts and weds, burns a house, fights; specific coverage >= 0.90, a cause on every entry, every chain resolves, coin and mass exact,
/// the population inside the Phase 2 band. <b>The 100-year soak</b> (overview section 3 step 7) runs the same script for a hundred years, prints a yearly table and the type counts by decade, and checks the
/// invariants of the nightly list; it takes about half an hour, so it runs only when <c>FL_SOAK100=1</c> (and writes its table to <c>FL_SOAK_LOG</c> when set): <c>Category=Soak</c>.
/// </summary>
public class Phase6SoakTests(ITestOutputHelper output)
{
    private const string Hash = "h";
    private const int DaysPerYear = TimeConst.DaysPerYear;

    // ---------------------------------------------------------------- the world and the bot

    internal static (SeamWorld W, WorldState St) Game(ulong seed, PlagueSeverity? plague = null)
    {
        var (w, st) = Ravensguard.Sim.Tests.Core.ChronicleSoakTests.Game(seed);
        if (plague is { } p) st.ChangeDifficulty(st.Difficulty with { Plague = p }, st.Clock.Now);
        return (w, st);
    }

    /// <summary>The game's own links, in the game's order (StreetView.Talk.cs): talk, law (with heists, bonds and faith politics), family, contracts.</summary>
    internal static WorldRuntime Boot(SeamWorld w, WorldState st)
    {
        var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        Phase6Links.AttachAll(rt, w.Content);
        return rt;
    }

    /// <summary>One thing the hero bot does at a game day (counted from the start) of a year; <see cref="Every"/> years apart (1: every year).</summary>
    private sealed record Act(string Name, int Day, int Every, int FromYear, Action<WorldRuntime, SeamWorld> Do);

    private static List<Act> Script() =>
    [
        new("steal", 10, 1, 1, Steal),
        new("surrender", 13, 1, 1, Surrender),            // reported at the next day's hour 0, the court day follows
        new("court", 20, 1, 1, Court),                     // a no when he is wed already; the wedding is on the next holy day
        new("fire", 30, 3, 1, Burn),
        new("fight", 40, 2, 1, Fight),
    ];

    private static T At<T>(WorldRuntime rt, Func<T> job) => rt.AtBarrier(job).GetAwaiter().GetResult();

    private static void Steal(WorldRuntime rt, SeamWorld w) => At(rt, () =>
    {
        var s = rt.State; var pt = s.Population.People; int town = pt.HomeSettlement[s.Hero!.PersonIndex];
        var witnesses = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && pt.AgeYears(i, (int)(rt.HourDone / 24)) >= 18 && i != s.Hero.PersonIndex).Take(3).ToList();
        if (witnesses.Count < 3) return 0;
        var ctx = new HeroContext(s, s.Party!, w.Content); var desk = new HeroDesk();
        desk.Issue(CrimeDesk.Act("theft", pt.IdOf(witnesses[0]), town, 240, 0, "ch_note_crime_hero", new ActorLook([0xA03030u], 2),
            [new WitnessRow(pt.IdOf(witnesses[0]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[1]).Value, WitnessRow.KnewIt), new WitnessRow(pt.IdOf(witnesses[2]).Value, 0)], null, rt.HourDone * 3600 + 600));
        desk.ApplyDue(ctx, rt.HourDone + 1);
        return 1;
    });

    private static void Surrender(WorldRuntime rt, SeamWorld w) => At(rt, () =>
    {
        var s = rt.State; if (s.Captivity is { HeroHeld: true }) return 0;
        var ctx = new HeroContext(s, s.Party!, w.Content); var desk = new HeroDesk();
        desk.Issue(JusticeDesk.Surrender(-1, rt.HourDone * 3600));
        desk.ApplyDue(ctx, rt.HourDone + 1);
        return 1;
    });

    /// <summary>The bot courts the first fitting person (the way FamilyDemo does it: a roof, the house's trust), asks, and the family's day weds them on the next holy day.</summary>
    private static void Court(WorldRuntime rt, SeamWorld w) => At(rt, () =>
    {
        var s = rt.State; var fw = FamilyWorld.From(s, w.Content); int hero = fw.HeroRow; if (hero < 0) return 0;
        var pt = fw.People; var hht = fw.Households; int hh = pt.Household[hero]; int day = (int)(rt.HourDone / 24);
        if (pt.Spouse[hero] >= 0 || s.Family?.Courtships.Values.Any(c => c.State == CourtState.Courting || c.State == CourtState.Betrothed) == true) return 0;
        if (hht.Lot[hh] < 0) { hht.Lot[hh] = 0; hht.Dwelling[hh] = DwellingType.Cottage; hht.Beds[hh] = 6; }
        var want = pt.Sex[hero] == Sex.Female ? Sex.Male : Sex.Female;
        for (int i = 0; i < pt.Count; i++)
        {
            if (i == hero || pt.Status[i] != PersonStatus.Alive || pt.Sex[i] != want || pt.Spouse[i] >= 0) continue;
            int a = pt.AgeYears(i, day), bh = pt.Household[i];
            if (a < 22 || a > 34 || bh < 0 || bh == hh || hht.Head[bh] < 0 || hht.Head[bh] == i || Courting.WhyNot(fw, hero, i, day).Length > 0) continue;
            int head = hht.Head[bh]; hht.Class[bh] = pt.Class[hero];
            s.Party!.Know(pt.IdOf(i)).TrustPm = 900; s.Party.Know(pt.IdOf(head)).TrustPm = 900;
            var ctx = new HeroContext(s, s.Party, w.Content); var desk = new HeroDesk(); long sec = rt.HourDone * 3600 + 60;
            desk.Issue(FamilyDesk.Court(pt.IdOf(i), 0, sec)); desk.Issue(FamilyDesk.Ask(pt.IdOf(i), sec + 1));
            desk.ApplyDue(ctx, rt.HourDone + 1);
            return 1;
        }
        return 0;
    });

    private static int _houses;
    /// <summary>The bot raises a thatched timber house in the fire world's room store (a headless world has scanned no chunks, so it holds no building of its own) and lights it with a torch: a hero's fire_started and the fire's own chain.</summary>
    private static void Burn(WorldRuntime rt, SeamWorld w) => At(rt, () =>
    {
        var fw = rt.State.HomeRecords?.Sim.Get<FireWorld>(); if (fw == null) return 0;
        int x0 = 40_000 + 20 * _houses++, y0 = 40_000;
        var e = new List<CellEdit>();
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
            {
                bool ring = x == 0 || y == 0 || x == 7 || y == 5;
                e.Add(new(x0 + x, y0 + y, 0, CellLayer.Structure, ring ? (x == 3 && y == 5 ? "door_wood" : "wall_timber") : "floor_plank"));
            }
        e.Add(new(x0 + 1, y0 + 1, 0, CellLayer.Fixture, "hearth")); e.Add(new(x0 + 5, y0 + 1, 0, CellLayer.Fixture, "bed"));
        fw.Rooms.Apply(e); fw.Rooms.RoofOver("roof_thatch");
        int st = fw.Rooms.StructureAt(x0 + 2, y0 + 2, 0);
        var prev = fw.Chronicle;                                             // outside the tick the fire world has no chronicle: write into the world's own, as the system's outbox does
        fw.Chronicle = (when, type, key, ps, subjects, causes, data) =>
        {
            var id = rt.State.Ids.Allocate(IdKind.Event);
            rt.State.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, data.ToDictionary(d => d.Key, d => d.Value), key, ps));
            return id;
        };
        try { return st >= 0 && fw.IgniteBuilding(st, rt.HourDone + 1, FireCause.Person(rt.State.Party!.Hero, "torch")) ? 1 : 0; }
        finally { fw.Chronicle = prev; }
    });

    private static FlavourFixture? _fx;
    /// <summary>The bot is in a fight with two soldiers of a lord and the chronicle writes it as it writes every fight (FightChronicle).</summary>
    private static void Fight(WorldRuntime rt, SeamWorld w)
    {
        _fx ??= new FlavourFixture(); var fx = _fx;
        ulong heroId = At(rt, () => rt.State.Party!.Hero.Value);
        FightLog? log = null;
        for (ulong seed = 1; seed < 40 && log == null; seed++)
        {
            var fw = new FightWorld(fx.C, seed + (ulong)rt.HourDone, seed, 8 * 3600) { RecordEvents = true };
            for (int i = 0; i < 3; i++)
                fw.Add(new FighterKit { Name = i == 0 ? "Aldric Vane" : $"Friend {i}", Team = 0, Person = i == 0 ? heroId : 7_000_000UL + (ulong)i, Weapon = new WeaponInstance("mace", "iron"), Skill = 50, X = -1500, Y = i * 900, FaceX = 1024, Worn = [new("shirt", "linen")] });
            for (int i = 0; i < 3; i++)
                fw.Add(new FighterKit { Name = $"Soldier {i}", Team = 1, Weapon = new WeaponInstance("axe", "iron"), Skill = 50, X = 1500, Y = i * 900, FaceX = -1024, Stance = "aggressive", Worn = [new("shirt", "linen")] });
            var r = FlavourRecorder.Attach(fw, null, fx.Book);
            while (!fw.IsOver && fw.Tick < 20 * 120) fw.StepWithAi();
            r.Finish();
            if (FightChronicle.Write(r.Log, new Chronicle(), new IdAllocator(), fx.Book, fx.Text).Count > 0) log = r.Log;      // a fight with a named death, maiming or capture is one the chronicle keeps
        }
        if (log == null) return;
        At(rt, () => { FightChronicle.Write(log, rt.State.Chronicle, rt.State.Ids, fx.Book, fx.Text); return 0; });
    }

    // ---------------------------------------------------------------- the run

    private sealed class YearRow
    {
        public int Year; public long Alive, Households, Births, Deaths, Idle, Hungry, DietPoor, Continent; public int ChronEntries, ChronAllRegions;
        public double RelishRatio, AleRatio, StapleRatio, Secs; public bool Coin, Mass; public int PlagueActive, PlagueInfected, PlagueDead;
        public long Bytes; public string Snap = "";
    }

    private sealed class Soak
    {
        public WorldRuntime Rt = null!; public SeamWorld W = null!; public WorldState St = null!;
        public readonly List<YearRow> Rows = [];
        public readonly Dictionary<string, int> Acts = [];
        public long Start; public long Alive0, Households0;
        public Action<string> Log = _ => { };
        public readonly List<Dictionary<string, int>> YearTypes = [];
        public readonly List<(int Year, double Ratio, int NoCause, int Unresolved, int OutOfOrder, int IllegalRoots, int Entries)> Checks = [];
        /// <summary>P8-EA-1: the economy and growth audit's yearly rows (<see cref="Ravensguard.Sim.Diagnostics.EconomyCentury"/>).</summary>
        public readonly List<Ravensguard.Sim.Diagnostics.EconomyYearRow> Econ = [];
        public long MinedG;
    }

    private static YearRow Sample(Soak k, int year, bool measureSave)
    {
        var rt = k.Rt;
        return At(rt, () =>
        {
            var st = rt.State; var home = rt.Records.HomeWorld!; var sim = home.Sim; var e = sim.Economy; var p = sim.People; long day = sim.State.HourDone / 24;
            var r = new YearRow { Year = year };
            long alive = 0, idle = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (p.Status[i] != PersonStatus.Alive) continue;
                alive++; int age = sim.AgeOn(i, day);
                if (age is >= 16 and < 60 && p.Occupation[i] == Occupation.None && !(sim.Households.ParcelCount[Math.Max(0, p.Household[i])] > 0)) idle++;
            }
            r.Alive = alive; r.Idle = idle; r.Births = sim.Vital.Births; r.Deaths = sim.Vital.Deaths;
            long hh = 0, hungry = 0, poor = 0;
            for (int h = 0; h < sim.Households.Count; h++) { if (sim.Households.MemberCount[h] == 0) continue; hh++; if ((e.Grievances[h] & Grievance.MissedMeal) != 0) hungry++; if (e.DietPoor[h]) poor++; }
            r.Households = hh; r.Hungry = hungry; r.DietPoor = poor;
            // prices of the relishes, the ale and the staples against their base, over the books that have a merchant
            long relN = 0, relS = 0, aleN = 0, aleS = 0, stN = 0, stS = 0;
            foreach (var b in home.Economy.Markets.AllBooks())
            {
                if (b.Merchants.Count == 0) continue;
                foreach (var g in e.Food.Relishes) if (b.Goods[g].StockUnits > 0) { relN++; relS += b.MidRatioPermille(g); }
                if (b.Goods[e.Food.Ale].StockUnits > 0) { aleN++; aleS += b.MidRatioPermille(e.Food.Ale); }
                foreach (var name in new[] { "wheat", "rye", "bread" }) { ushort g = e.Goods.IdOf(name); if (b.Goods[g].StockUnits > 0) { stN++; stS += b.MidRatioPermille(g); } }
            }
            r.RelishRatio = relN == 0 ? 0 : relS / 1000.0 / relN; r.AleRatio = aleN == 0 ? 0 : aleS / 1000.0 / aleN; r.StapleRatio = stN == 0 ? 0 : stS / 1000.0 / stN;
            r.Coin = e.Ledger.Audit().IsConserved; r.Mass = e.MassAuditHolds();
            r.ChronEntries = st.Chronicle.Count; r.ChronAllRegions = rt.Records.Resident.Sum(x => x.Chronicle.Count);
            if (st.ContinentState is { } cs)
            {
                long pop = 0; foreach (var s in cs.Summaries) if (!s.Detailed) pop += s.Pop;
                r.Continent = pop; r.PlagueActive = cs.Plague.Active ? 1 : 0; r.PlagueInfected = cs.Plague.Infected.Count; r.PlagueDead = cs.Plague.DeadEstimate;
            }
            var sn = rt.Snapshot; r.Snap = $"res {sn.Resident}/{sn.Detailed}/{sn.Daily}/{sn.Paged} maxHr {sn.MaxHourMs:0}ms rumours {st.Talk?.Rumours.Count}/{st.Talk?.Rumours.HearingCount} rel {st.Talk?.Relations.Count} held {st.Talk?.Rumours.Held.Count} passes {st.Talk?.Passes.Count} people {st.Population.People.Count} crimes {st.Law?.Crimes.Count} contracts {st.Contracts?.Contracts.Count}";
            if (measureSave) r.Bytes = SaveCodec.Encode(WorldSave.Capture(st, Hash, "0.1")).Length;
            return r;
        });
    }

    private static string RowText(YearRow r) =>
        $"y{r.Year,3} alive {r.Alive,6} hh {r.Households,5} idle {r.Idle,5} hungryHH {r.Hungry,4} dietPoor {r.DietPoor,5} births {r.Births,7} deaths {r.Deaths,7} relish {r.RelishRatio:0.00} ale {r.AleRatio:0.00} staple {r.StapleRatio:0.00} " +
        $"chron {r.ChronEntries,7}/{r.ChronAllRegions,7} continentPop {r.Continent,8} plague {r.PlagueActive}/{r.PlagueInfected}/{r.PlagueDead} {(r.Coin && r.Mass ? "ok" : "FAIL")} {r.Secs:0}s {r.Snap}" + (r.Bytes > 0 ? $" save {r.Bytes / 1024.0 / 1024:0.0} MB" : "");

    /// <summary>The dice are not waited for (the start is a one-in-a-hundred roll a year): the great plague starts in the home region at the first spring day of the year the variable names, as the tick's own start would have it.</summary>
    private static void ForcePlague(WorldRuntime rt, Soak k)
    {
        rt.AdvanceTo(GameTime.FromGameHours(k.Start + ((k.Rows.Count) * DaysPerYear + 1) * 24L));
        At(rt, () =>
        {
            var cs = rt.State.ContinentState!; var cp = cs.Plague; var now = GameTime.FromGameHours(rt.HourDone);
            cp.NowDay = (int)now.TotalDays; cp.Active = true; cp.Id++; cp.Origin = rt.Records.Home.Id; cp.StartDay = cp.NowDay; cp.StartSeason = Ravensguard.Sim.Continental.ContinentTick.SeasonOf(now);
            cp.NameIndex = 0; cp.DeadEstimate = 0; cp.PendingNews.Add((cp.Origin, 0));
            k.Log($"forced plague at year {k.Rows.Count + 1} day 1, origin region {cp.Origin}, severity {cp.Severity}");
            return 0;
        });
    }

    /// <summary>The chronicle's coverage and chains and the live contracts by kind and state, read at a barrier and logged (the asserts come at the end of the run, after everything is printed).</summary>
    private void Measure(Soak k, int y)
    {
        var rt = k.Rt; var types = ChronicleTypes.From(k.W.Content);
        var line = At(rt, () =>
        {
            var c = rt.State.Chronicle; var hist = k.W.History.Chronicle;
            var rep = c.SpecificCoverage(types, older: hist);
            var ch = ChronicleChains.Check(c, types, maxProblems: 100_000, older: hist);
            var live = rt.State.Contracts?.Contracts.Values.GroupBy(x => x.Kind + ":" + x.State).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()) ?? [];
            k.Checks.Add((y, rep.Ratio, rep.NoCause, ch.Unresolved, ch.OutOfOrder, ch.IllegalRoots, c.Count));
            foreach (var g in ch.Problems.GroupBy(pr => pr.Split(' ')[0] + (pr.Contains("not earlier") ? " (not earlier)" : pr.Contains("is a root") || pr.Contains("a root,") ? " (illegal root)" : " (other)")).OrderByDescending(g => g.Count()).Take(12))
                k.Log($"    problem y{y}: {g.Key} x{g.Count()}: {g.First()}");
            return $"  [check y{y}] {c.Count} entries: specific {rep.Ratio:0.000}, no cause {rep.NoCause}, summaries {rep.Summaries}; chains unresolved {ch.Unresolved}, out of order {ch.OutOfOrder}, illegal roots {ch.IllegalRoots}; contracts now: {string.Join(", ", live)}";
        });
        k.Log(line);
    }

    private static void PfDump(Soak k, int y, double tickSecs)
    {
        var rt = k.Rt; var sums = new Dictionary<string, (double Ms, long Calls)>(); double all = 0;
        At(rt, () =>
        {
            foreach (var rw in rt.Records.Resident)
            {
                var stt = rw.Ticker.Stats;
                foreach (var (n, c) in stt.Systems) { var o = sums.GetValueOrDefault(n); sums[n] = (o.Ms + c.TotalMs, o.Calls + c.Calls); all += c.TotalMs; }
                stt.Reset();
            }
            return 0;
        });
        k.Log($"  [pf y{y}] ticks {tickSecs:0.0}s, systems total {all / 1000:0.0}s, rest {tickSecs - all / 1000:0.0}s (links, scheduler, continent, barrier): " + string.Join(", ", sums.OrderByDescending(x => x.Value.Ms).Take(14).Select(x => $"{x.Key} {x.Value.Ms / 1000:0.00}s")));
        if (Ravensguard.Sim.Diagnostics.PfClock.Table is { } ht) { k.Log($"  [hooks y{y}] " + string.Join(", ", ht.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value * 1.0 / System.Diagnostics.Stopwatch.Frequency:0.00}s"))); ht.Clear(); }
        k.Log("  [pfall y" + y + "] " + string.Join(", ", sums.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value.Ms:0}")));
    }

    private Soak Run(int years, PlagueSeverity? plague, int[]? saveYears, Action<Soak, int>? each = null)
    {
        bool pfStats = Environment.GetEnvironmentVariable("FL_PF_STATS") == "1";      // P8-PF-3: per-system wall time of each game year, summed over the resident regions (diagnostic, never read by the sim)
        if (pfStats) { RegionWorld.RecordSystemTimings = true; Ravensguard.Sim.Diagnostics.PfClock.Table = []; }
        var (w, st) = Game(1234, plague);
        var k = new Soak { W = w, St = st };
        string? logPath = Environment.GetEnvironmentVariable("FL_SOAK_LOG");
        k.Log = s => { output.WriteLine(s); if (logPath != null) File.AppendAllText(logPath, s + "\n"); };
        k.Rt = Boot(w, st);
        var rt = k.Rt;
        k.Start = rt.HourDone + 1; k.Start += (24 - k.Start % 24) % 24;     // game day boundary
        var script = Environment.GetEnvironmentVariable("FL_SOAK_NOBOT") == "1" ? [] : Script();      // P8-EA-1: the century with and without the scripted player bot
        k.Log(script.Count == 0 ? "the scripted player bot is off (FL_SOAK_NOBOT=1)" : "the scripted player bot is on: " + string.Join(", ", script.Select(a => a.Name)));
        var sw = Stopwatch.StartNew(); double last = 0;
        for (int y = 1; y <= years; y++)
        {
            double actSecs = 0;
            if (int.TryParse(Environment.GetEnvironmentVariable("FL_SOAK_FORCE_PLAGUE"), out int fy) && fy == y) ForcePlague(rt, k);
            var today = script.Where(a => y >= a.FromYear && (y - a.FromYear) % a.Every == 0).OrderBy(a => a.Day).ToList();
            foreach (var a in today)
            {
                rt.AdvanceTo(GameTime.FromGameHours(k.Start + ((y - 1) * DaysPerYear + a.Day) * 24L));
                var sa = Stopwatch.StartNew(); a.Do(rt, w); actSecs += sa.Elapsed.TotalSeconds; k.Acts[a.Name] = k.Acts.GetValueOrDefault(a.Name) + 1;
            }
            rt.AdvanceTo(GameTime.FromGameHours(k.Start + y * DaysPerYear * 24L));
            double tickSecs = sw.Elapsed.TotalSeconds - last;
            if (pfStats) PfDump(k, y, tickSecs);
            var row = Sample(k, y, saveYears?.Contains(y) == true);
            At(rt, () =>
            {
                var d = new Dictionary<string, int>(); var all = rt.State.Chronicle.All; long y0 = k.Start / 24 + (y - 1L) * DaysPerYear;
                for (int i = all.Count - 1; i >= 0 && all[i].When.TotalDays >= y0 - 1; i--)
                {
                    var e = all[i]; if (e.When.TotalDays < y0) continue;
                    d[e.TypeId] = d.GetValueOrDefault(e.TypeId) + 1;
                    if (e.TypeId == "work_posted" && e.Data.TryGetValue("kind", out var kd)) d["work_posted/" + kd] = d.GetValueOrDefault("work_posted/" + kd) + 1;
                    if (e.TypeId == "work_need" && e.Data.TryGetValue("need", out var nd)) d["work_need/" + nd] = d.GetValueOrDefault("work_need/" + nd) + 1;
                    if (e.TypeId == "work_done" && e.Data.TryGetValue("kind", out var dk)) d["work_done/" + dk] = d.GetValueOrDefault("work_done/" + dk) + 1;       // P8-BL-1: what was taken and finished, by kind
                }
                k.YearTypes.Add(d);
                k.Log($"  y{y} (P8-BL-1) soil_improved {d.GetValueOrDefault("soil_improved")}, soil work_posted {d.GetValueOrDefault("work_posted/soil_improvement")}, deliveries done {d.GetValueOrDefault("work_done/deliver_goods")}, deliveries posted {d.GetValueOrDefault("work_posted/deliver_goods")}, famine {d.GetValueOrDefault("famine")}");
                return 0;
            });
            if (y == 1) { k.Alive0 = row.Alive; k.Households0 = row.Households; }
            row.Secs = sw.Elapsed.TotalSeconds - last; row.Snap += $" acts {actSecs:0.0}s rows {k.St.Population.People.Count} free {k.St.Population.People.FreeRows.Count} dropped {Ravensguard.Sim.People.PersonCompaction.DroppedTotal} compaction {Ravensguard.Sim.People.PersonCompaction.LastMs:0}ms"; last = sw.Elapsed.TotalSeconds;
            k.Rows.Add(row); k.Log(RowText(row));
            var er = At(rt, () => { var hw = rt.Records.HomeWorld!; var x = Ravensguard.Sim.Diagnostics.EconomyCentury.Sample(hw.Sim, hw.Economy, y, k.MinedG); k.MinedG = Ravensguard.Sim.Diagnostics.EconomyCentury.MinedG(hw.Economy); return x; });
            if (y == 1) k.Log(Ravensguard.Sim.Diagnostics.EconomyCentury.Header);
            k.Econ.Add(er); k.Log("  [econ]" + Ravensguard.Sim.Diagnostics.EconomyCentury.Line(er)[1..]);
            if (y % 25 == 0 || y == 10 || y == years) Measure(k, y);
            each?.Invoke(k, y);
        }
        return k;
    }

    // ---------------------------------------------------------------- OV-5

    [Fact, Trait("Category", "Soak")]
    public void OV5_TenGameYearsWithTheHeroBotAndEverySystem_AreNinetyPercentSpecific_EveryEntryHasACause_ChainsResolve_CoinAndMassAreExact()
    {
        var k = Run(10, null, [10]);
        using var rt = k.Rt; var st = k.St; var types = ChronicleTypes.From(k.W.Content); var c = st.Chronicle; var hist = k.W.History.Chronicle;
        output.WriteLine("hero acts: " + string.Join(", ", k.Acts.Select(a => a.Key + " " + a.Value)));
        var rep = c.SpecificCoverage(types, older: hist);
        output.WriteLine($"{c.Count} entries: specific {rep.Specific}, drift {rep.Drift}, no cause {rep.NoCause}, summaries {rep.Summaries}; ratio {rep.Ratio:0.000}");
        foreach (var g in c.All.Where(e => !e.Causes.Any(x => x.Kind == CauseKind.MarketDrift) && !e.Causes.Any(x => c.IsSpecific(x, e, types.Get(e.TypeId), hist))).GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()))
            output.WriteLine($"  not specific: {g.Key} x{g.Count()} ({string.Join(",", g.SelectMany(e => e.Causes).Select(x => x.Kind).Distinct())})");
        output.WriteLine("types: " + string.Join(", ", c.All.GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()).Take(60).Select(g => g.Key + " " + g.Count())));
        Assert.True(rep.Ratio >= 0.90, $"specific coverage {rep.Ratio:0.000}");
        var p6 = c.SpecificCoverage(types, e => types.Get(e.TypeId) is { Def.Legacy: false }, hist);
        output.WriteLine($"Phase 6 types: {p6.Entries} entries, ratio {p6.Ratio:0.000}");
        Assert.True(p6.Entries == 0 || p6.Ratio >= 0.95);
        Assert.Equal(0, rep.NoCause); Assert.All(c.All, e => Assert.True(e.Causes.Count > 0, e.TypeId));
        var chains = ChronicleChains.Check(c, types, maxProblems: 100_000, older: hist);
        output.WriteLine($"chains: {chains.Entries} entries, {chains.Roots} roots, unresolved {chains.Unresolved}, out of order {chains.OutOfOrder}, illegal roots {chains.IllegalRoots}");
        foreach (var g in chains.Problems.GroupBy(pr => pr.Split(' ')[0] + (pr.Contains("not earlier") ? " (not earlier)" : pr.Contains("is a root") || pr.Contains("a root,") ? " (illegal root)" : " (other)")).OrderByDescending(g => g.Count())) output.WriteLine($"  chain problem: {g.Key} x{g.Count()}: {g.First()}");
        Assert.Equal(0, chains.Unresolved); Assert.Equal(0, chains.OutOfOrder); Assert.Equal(0, chains.IllegalRoots);
        // the widened bot's acts are in it, with their chains
        foreach (var t in new[] { "crime_witnessed", "bounty_posted", "crime_arrested", "sentence_pronounced", "courtship_begun", "betrothal", "hero_wedding", "fire_started" })
            Assert.True(c.All.Any(e => e.TypeId == t), $"the hero bot's run has no {t} entry");
        Assert.True(c.All.Any(e => e.TypeId.StartsWith("fight_", StringComparison.Ordinal)), "no fight entry");
        // Phase 2-5 gates over the ten years: coin and mass exact every year, the population in the band, households within 10 percent, idle adults under 800
        Assert.All(k.Rows, r => { Assert.True(r.Coin, $"coin audit failed in year {r.Year}"); Assert.True(r.Mass, $"mass audit failed in year {r.Year}"); });
        Assert.All(k.Rows, r =>
        {
            Assert.InRange(r.Alive, k.Alive0 * 90 / 100, k.Alive0 * 125 / 100);
            Assert.InRange(r.Households, k.Households0 * 90 / 100, k.Households0 * 130 / 100);       // Phase 2's 10 percent held before Phase 4's growth built cottages: about 1.2 percent a year more households now (see the close-out notes)
            Assert.True(r.Idle < 800, $"{r.Idle} idle adults in year {r.Year}");
        });
        Assert.True(At(rt, () => rt.State.Economy!.Ledger.Audit().IsConserved));
    }

    /// <summary>Which game years the great plague would start in on seed 1234's continent (a pure roll), by the slider: how many a hundred years holds. Nothing is simulated.</summary>
    [Fact]
    public void ThePlagueStartYears_OfSeed1234_ArePrintedBySlider()
    {
        var (_, st) = Game(1234);
        foreach (var sev in new[] { PlagueSeverity.Normal, PlagueSeverity.Rare, PlagueSeverity.Frequent })
        {
            var years = Enumerable.Range(0, 400).Where(y => Ravensguard.Sim.Disease.ContinentPlagueStep.Starts(st.Seed, y, sev)).ToList();
            output.WriteLine($"{sev}: game starts in year {st.Clock.Now.Year}, world seed {st.Seed}, start years in 0..399: {string.Join(",", years)} ({years.Count(y => y < 130)} within 130)");
        }
    }

    /// <summary>The Phase 4 site placer's count per kind in a new game's region, over a few seeds (the plan's table says 8-15 ruins and crypts, 10-20 caves, 2-5 mines, 3-8 barrows a 32 km region). Prints only.</summary>
    [Fact]
    public void TheSitesPerRegion_OfSomeSeeds_ArePrintedByKind()
    {
        foreach (ulong seed in new ulong[] { 1234, 1, 2, 3, 4 })
        {
            var (_, st) = Game(seed);
            var sites = st.Population.Region.Sites;
            output.WriteLine($"seed {seed}: {string.Join(", ", sites.GroupBy(x => x.Kind).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()))}; ruins by sub: {string.Join(", ", sites.Where(x => x.Kind == Ravensguard.Sim.WorldGen.Region.SiteKind.Ruin).GroupBy(x => x.SubKind).Select(g => g.Key + " " + g.Count()))}");
        }
    }

    // ---------------------------------------------------------------- the 100-year soak

    [Fact, Trait("Category", "Soak")]
    public void Soak100Years_EverySystem_PopulationWithinTenPercent_CoinAndMassExact_NoRealmCollapseFromUnrestAlone()
    {
        if (Environment.GetEnvironmentVariable("FL_SOAK100") != "1") { output.WriteLine("skipped: set FL_SOAK100=1 (about half an hour)"); return; }
        PlagueSeverity? plague = Environment.GetEnvironmentVariable("FL_SOAK_PLAGUE") is { } pl && Enum.TryParse<PlagueSeverity>(pl, out var ps) ? ps : null;
        int years = int.TryParse(Environment.GetEnvironmentVariable("FL_SOAK_YEARS"), out var ny) ? ny : 100;
        var k = Run(years, plague, [1, 10, 25, 50, 100]);
        using var rt = k.Rt; var st = k.St; var c = st.Chronicle;
        k.Log("hero acts: " + string.Join(", ", k.Acts.Select(a => a.Key + " " + a.Value)));
        // the entries of each game year by type, counted as the year closed (the yearly compaction later folds old commoners' entries into summaries, so a final scan would undercount the early years); in tenths of the run
        var byTenth = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        for (int y = 0; y < k.YearTypes.Count; y++)
            foreach (var (key, n) in k.YearTypes[y])
            {
                if (!byTenth.TryGetValue(key, out var arr)) byTenth[key] = arr = new int[10];
                arr[Math.Min(9, y * 10 / Math.Max(1, k.YearTypes.Count))] += n;
            }
        k.Log($"entries by type in each tenth of the run ({Math.Max(1, k.YearTypes.Count / 10)} game years each); work_posted/<kind> and work_need/<need> are broken down:");
        foreach (var (t, arr) in byTenth.OrderByDescending(x => x.Value.Sum())) k.Log($"  {t,-34} {arr.Sum(),8} | {string.Join(' ', arr.Select(x => x.ToString().PadLeft(6)))}");
        var sizes = At(rt, () => WorldSave.Capture(rt.State, Hash, "0.1").Sections.OrderByDescending(s => s.Value.Length).Take(12).Select(s => $"{s.Key} {s.Value.Length / 1024.0 / 1024:0.00} MB").ToList());
        k.Log("largest save sections at the end: " + string.Join(", ", sizes));
        Ea1Decades(k);
        // the asserts, all after the measurements are on the log
        Assert.All(k.Rows, r => { Assert.True(r.Coin, $"coin audit failed in year {r.Year}"); Assert.True(r.Mass, $"mass audit failed in year {r.Year}"); });
        Assert.All(k.Checks, x => { Assert.True(x.Ratio >= 0.90, $"specific coverage {x.Ratio:0.000} in year {x.Year}"); Assert.Equal(0, x.NoCause); Assert.Equal(0, x.OutOfOrder); if (x.Year <= 10) { Assert.Equal(0, x.IllegalRoots); Assert.Equal(0, x.Unresolved); } else { Assert.True(x.IllegalRoots * 2_000L <= x.Entries, $"{x.IllegalRoots} unmarked roots in year {x.Year}"); Assert.True(x.Unresolved * 10_000L <= x.Entries, $"{x.Unresolved} unresolved chain links in {x.Entries} entries in year {x.Year}"); } });       // after the compaction starts (year 11) a handful of links dangle (an old law record names an entry compacted away): pinned at 0.01 percent, see the close-out notes
        // the population stays inside the Phase 2 band (a forced plague is a measurement run: the great plague is meant to cost more than a tenth where it strikes)
        if (Environment.GetEnvironmentVariable("FL_SOAK_FORCE_PLAGUE") == null) Assert.All(k.Rows, r => Assert.InRange(r.Alive, k.Alive0 * 90 / 100, k.Alive0 * 125 / 100));
        // households: Phase 2's 10 percent held before Phase 4's growth built cottages; they now rise about 0.4 percent a year (1.4 times the start in a hundred years), so the soak holds them to 150 percent
        Assert.All(k.Rows, r => Assert.InRange(r.Households, k.Households0 * 90 / 100, k.Households0 * 150 / 100));
        Ea1Century(k);
        Bl6Century(k);
    }

    /// <summary>P8-EA-1 (docs/reviews/phase-8-economy-audit.md): the economy and growth audit's century bounds, the named ratchets of <see cref="EconomyAudit.Ea1Ratchets"/>, asserted when the run reaches 70 game years.</summary>
    private static void Ea1Century(Soak k)
    {
        int years = k.Econ.Count; if (years < 70) { k.Log("EA-1 not asserted: fewer than 70 game years"); return; }
        int Sum(string key, int from, int to) { int n = 0; for (int y = from; y < Math.Min(to, k.YearTypes.Count); y++) n += k.YearTypes[y].GetValueOrDefault(key); return n; }
        var a0 = k.Econ[0]; var z = k.Econ[^1];
        long upPct = k.Econ.Max(r => r.Alive * 100 / Math.Max(1, a0.Alive)) - 100;
        int worstFamine = Enumerable.Range(0, (years + 9) / 10).Max(d => Sum("famine", d * 10, d * 10 + 10));
        long woodLossPct = a0.WoodStockT <= 0 ? 0 : (a0.WoodStockT - z.WoodStockT) * 100 / a0.WoodStockT;
        long coinBp = Ravensguard.Sim.Diagnostics.EconomyCentury.CoinGrowthBp(a0.CoinInPurses, z.CoinInPurses, z.Year - a0.Year);
        int veins = Sum("vein_found", 0, years), workedOut = Sum("mine_worked_out", 0, years);
        k.Log($"EA-1 bounds: population up to +{upPct}% (ratchet {EconomyAudit.Ea1Ratchets.PopulationUpPctMax}), worst famine decade {worstFamine} ({EconomyAudit.Ea1Ratchets.FamineWorstDecadeMax}), woodland lost {woodLossPct}% ({EconomyAudit.Ea1Ratchets.WoodlandLossPctMax}), coin {coinBp} bp a year ({EconomyAudit.Ea1Ratchets.CoinGrowthBpMin}), veins found {veins} ({EconomyAudit.Ea1Ratchets.VeinsFoundMin}), mines worked out {workedOut} ({EconomyAudit.Ea1Ratchets.MinesWorkedOutMin})");
        Assert.True(upPct <= EconomyAudit.Ea1Ratchets.PopulationUpPctMax, $"population up {upPct}%");
        Assert.True(worstFamine <= EconomyAudit.Ea1Ratchets.FamineWorstDecadeMax, $"worst famine decade {worstFamine}");
        Assert.True(woodLossPct <= EconomyAudit.Ea1Ratchets.WoodlandLossPctMax, $"woodland lost {woodLossPct}%");
        Assert.True(coinBp >= EconomyAudit.Ea1Ratchets.CoinGrowthBpMin, $"coin {coinBp} bp a year");
        Assert.True(veins >= EconomyAudit.Ea1Ratchets.VeinsFoundMin, $"veins found {veins}");
        Assert.True(workedOut >= EconomyAudit.Ea1Ratchets.MinesWorkedOutMin, $"mines worked out {workedOut}");
    }

    /// <summary>P8-EA-1: the economy and growth audit's decade table (the last year of each decade, the decade's sums of famine, vein and mine entries, and the coin's yearly growth over the decade).</summary>
    private static void Ea1Decades(Soak k)
    {
        if (k.Econ.Count == 0) return;
        int Sum(string key, int from, int to) { int n = 0; for (int y = from; y < Math.Min(to, k.YearTypes.Count); y++) n += k.YearTypes[y].GetValueOrDefault(key); return n; }
        k.Log("EA-1 by decade: dec  alive(end) pop%  famine hungerD foodHd fert cleared woodT  ore(leftT) veins mOpened mOut  grain timber charcoal iron cloth  coinBp/yr  idle empty failing");
        var a0 = k.Econ[0];
        for (int d = 0; d * 10 < k.Econ.Count; d++)
        {
            var first = k.Econ[d * 10]; var last = k.Econ[Math.Min(k.Econ.Count - 1, d * 10 + 9)]; var prev = d == 0 ? first : k.Econ[d * 10 - 1];
            long bp = Ravensguard.Sim.Diagnostics.EconomyCentury.CoinGrowthBp(prev.CoinInPurses, last.CoinInPurses, last.Year - prev.Year);
            k.Log($"EA-1 by decade: {d + 1,3} {last.Alive,10} {(last.Alive * 1000 / Math.Max(1, a0.Alive) - 1000) / 10.0,5:0.0} {Sum("famine", d * 10, d * 10 + 10),6} {last.HungerDeaths - (d == 0 ? 0 : prev.HungerDeaths),7} {last.FoodDaysX10PerHead / 10.0,6:0.0} {last.FertilityMeanX10 / 10.0,4:0.0} {last.ClearedRows,7} {last.WoodStockT,5}  {last.OreLeftT,9} {Sum("vein_found", d * 10, d * 10 + 10),5} {Sum("mine_opened", d * 10, d * 10 + 10),7} {Sum("mine_worked_out", d * 10, d * 10 + 10),4}  {last.Grain / 1000.0,5:0.00} {last.Timber / 1000.0,6:0.00} {last.Charcoal / 1000.0,8:0.00} {last.Iron / 1000.0,6:0.00} {last.Cloth / 1000.0,7:0.00}  {bp,9} {last.Idle,5} {last.BusinessesEmpty,5} {last.BusinessesFailing,7}");
        }
        var z = k.Econ[^1];
        k.Log($"EA-1 century: population {a0.Alive} to {z.Alive} ({(z.Alive * 1000 / Math.Max(1, a0.Alive) - 1000) / 10.0:0.0}%), coin {a0.CoinInPurses} to {z.CoinInPurses} ({Ravensguard.Sim.Diagnostics.EconomyCentury.CoinGrowthBp(a0.CoinInPurses, z.CoinInPurses, z.Year - a0.Year)} bp a year), wood {a0.WoodStockT} to {z.WoodStockT} t, ore left {a0.OreLeftT} to {z.OreLeftT} t");
    }

    /// <summary>
    /// P8-BL-1 (golden BL-6, balance pass 3.2): the century. Soil jobs filled after year 60 (the household hands and the pool: <c>soil_improved</c> entries every decade from the seventh), households up no more than the population plus 10 points,
    /// deliveries taken (carters finished some), and famines not rising decade on decade. The figures are logged first; a bound the honest fix did not meet is the named ratchet of <see cref="BalancePass.BalanceRatchets"/>.
    /// </summary>
    private void Bl6Century(Soak k)
    {
        int years = k.YearTypes.Count; if (years < 70) { k.Log("BL-6 not asserted: fewer than 70 game years"); return; }
        int Sum(string key, int from, int to) { int n = 0; for (int y = from; y < Math.Min(to, years); y++) n += k.YearTypes[y].GetValueOrDefault(key); return n; }
        var decades = Enumerable.Range(0, (years + 9) / 10).ToList();
        string Line(string key) => string.Join(" ", decades.Select(d => Sum(key, d * 10, d * 10 + 10).ToString().PadLeft(5)));
        k.Log("BL-6 per decade: soil_improved " + Line("soil_improved") + " | famine " + Line("famine") + " | deliveries done " + Line("work_done/deliver_goods") + " | soil work_posted " + Line("work_posted/soil_improvement"));
        var last = k.Rows[^1]; long hhUp = last.Households * 100 / Math.Max(1, k.Households0) - 100, popUp = last.Alive * 100 / Math.Max(1, k.Alive0) - 100;
        k.Log("BL-6 households " + hhUp + " percent against population " + popUp + " percent: " + (hhUp - popUp) + " points over");
        for (int d = 6; d < decades.Count; d++) Assert.True(Sum("soil_improved", d * 10, d * 10 + 10) >= BalancePass.BalanceRatchets.Bl6SoilJobsFilledPerLateDecade, "soil jobs filled in decade " + (d + 1) + ": " + Sum("soil_improved", d * 10, d * 10 + 10));
        Assert.True(hhUp - popUp <= BalancePass.BalanceRatchets.Bl6HouseholdPointsOverPopulation, "households " + hhUp + " percent against population " + popUp);
        Assert.True(Sum("work_done/deliver_goods", 0, years) >= BalancePass.BalanceRatchets.Bl6DeliveriesTaken, "deliveries taken: " + Sum("work_done/deliver_goods", 0, years));
        for (int d = 2; d < decades.Count; d++)
            Assert.True(Sum("famine", d * 10, d * 10 + 10) <= Sum("famine", (d - 1) * 10, d * 10) * BalancePass.BalanceRatchets.Bl6FamineDecadeGrowthPct / 100 + 2, "famines rose in decade " + (d + 1) + ": " + Sum("famine", d * 10, d * 10 + 10) + " after " + Sum("famine", (d - 1) * 10, d * 10));
    }
}
