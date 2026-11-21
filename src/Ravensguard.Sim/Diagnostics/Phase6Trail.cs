using System.Security.Cryptography;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Family;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>What the scripted run did (overview OV-8): one line per scene, in order, for the test to read and the tool to print.</summary>
public sealed class Phase6TrailReport
{
    public List<string> Scenes { get; } = [];
    public string Hash = "";
    public int Points;
    /// <summary>The world as the run ended (the runtime is disposed; the books and the chronicle stay readable).</summary>
    public WorldState? State;
}

/// <summary>
/// The Phase 6 trail (overview OV-8; the <c>phase6_trail</c> line of <c>WorldTool hashes</c>, compared across Linux and Windows by CI): a new game of the seed's world with the game's own links (talk, law with its heist, bond and faith-politics links,
/// family, contracts) run through the whole runtime for <see cref="Days"/> game days, with a script of events at fixed days: a court day that flogs a thief the second time, a hearth fire in a thatched dwelling, a riot after an eviction, a slave
/// sold at a market town, a plague's arrival, a council of the realm and a jailbreak by the drain. Every scene is a call of the system's own function at the barrier (or a recorded hero command), on the test's thread. The hash covers every
/// section of the save (all the Phase 6 sections, the Phase 5 ones and the chronicle), taken at the end. <paramref name="atPoint"/> is called at six days with the runtime stopped at a barrier (the test saves, loads and saves there).
/// </summary>
public static class Phase6Trail
{
    public const int Days = 60;
    /// <summary>The days the six save points fall on (after the day's scenes, at the day's first hour).</summary>
    public static readonly int[] PointDays = [6, 16, 26, 36, 46, 56];

    private static string Money(long m) => m.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static Phase6TrailReport Run(ContentDatabase db, Continent continent, WorldHistory history, ContinentSettings cs, HistorySettings hs, RegionSettings rs, int cellX, int cellY, Population pop,
        Action<WorldRuntime, WorldState, int>? atPoint = null, Action<WorldRuntime>? onStart = null, Action<WorldRuntime, WorldState>? onEnd = null)
    {
        var report = new Phase6TrailReport();
        var origin = db.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        var st = NewGameWorld.Create(db, continent, history, cs, hs, rs, cellX, cellY, pop.Clone(), hero, null);
        st.Content = db;
        using var rt = new WorldRuntime(st, db, new MemoryRegionPager());
        TalkLink.Attach(rt, LineBook.From(db));
        LawLink.Attach(rt);                                                      // with the heist, bond and faith-politics links
        FamilyLink.Attach(rt);
        ContractLink.Attach(rt);
        onStart?.Invoke(rt);                                                      // tests: the budgets time each link of the hour from here
        var ctx = new HeroContext(st, st.Party!, db);
        var desk = new HeroDesk();
        var pt = st.Population.People; var region = st.Population.Region; var e = st.Economy!;
        int home = pt.HomeSettlement[st.Hero!.PersonIndex];
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24;                    // the first day's first hour
        var sk = (Dictionary<string, int>)st.Hero.Skills; sk["lockpicking"] = 80; sk["stealth"] = 70;      // the hero has learned the pick and the soft step

        HeroResult Do(HeroCommand c)
        {
            var cmd = desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            desk.ApplyDue(ctx, rt.HourDone + 1);
            return desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }

        EntityId? thief = null; int ptIdx = 0;
        for (int day = 1; day <= Days; day++)
        {
            long dayStart = h0 + (long)(day - 1) * 24;                           // scenes run on the barrier of a given hour of a given day
            void At(int hour) { if (rt.HourDone < dayStart + hour - 1) rt.AdvanceTo(GameTime.FromGameHours(dayStart + hour - 1)); }

            // ---- the scenes
            if (day == 2) { At(12); thief = CourtScene(st, db, home, rt.HourDone / 24, rt.HourDone * 3600, report); }
            if (day == 8) { At(3); FireScene(st, rt.HourDone + 1, report); }
            if (day == 14) { At(23); RiotScene(st, home, (int)(rt.HourDone / 24), report); }
            if (day == 20) { At(10); SaleScene(st, db, home, (int)(rt.HourDone / 24), rt.HourDone * 3600, report); }
            if (day == 26) { At(7); PlagueScene(st, home, (int)(rt.HourDone / 24), rt.HourDone * 3600, report); }
            if (day == 32) { At(8); CouncilScene(st, (int)(rt.HourDone / 24), report); }
            if (day == 40) { HeistScene(st, db, rt, Do, home, h0 + (long)(day - 2) * 24, report); }

            // ---- the save points (the hour after the day's first: a barrier)
            if (ptIdx < PointDays.Length && day == PointDays[ptIdx])
            {
                At(2);
                atPoint?.Invoke(rt, st, ptIdx); ptIdx++; report.Points++;
            }
            rt.AdvanceTo(GameTime.FromGameHours(dayStart + 24));
        }
        _ = thief;
        onEnd?.Invoke(rt, st);                                                    // tests: read the ticker's timings before the runtime is disposed

        // ---- the hash: every section of the save, in name order
        var file = WorldSave.Capture(st, "h", "0.1");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in file.Sections.Keys.Order(StringComparer.Ordinal))
        {
            if (name == WorldSave.MetaSection) continue;
            sha.AppendData(Encoding.UTF8.GetBytes(name)); sha.AppendData([0]);
            var bytes = file.Sections[name]; sha.AppendData(BitConverter.GetBytes(bytes.Length)); sha.AppendData(bytes.Span);
        }
        report.Hash = Convert.ToHexString(sha.GetHashAndReset())[..16];
        report.State = st;
        return report;
    }

    // ------------------------------------------------------------------ the scenes

    /// <summary>A thief of the home town, seen by the watch, reported and taken: the court day tries him by the culture's ladder (crime and justice 4.5); the flogging of the run is the courts' own, the NPC thieves of its sixty days.</summary>
    private static EntityId CourtScene(WorldState st, ContentDatabase db, int home, long day, long sec, Phase6TrailReport rep)
    {
        var pt = st.Population.People; int d = (int)day;
        int town = home;
        var guards = new HashSet<int>(Guards.Of(pt, town));
        int c = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && pt.AgeYears(i, d) >= 18 && i != st.Hero!.PersonIndex);
        int victim = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && i != c && pt.AgeYears(i, d) >= 18);
        int watch = Guards.Of(pt, town)[0];
        var law = LawWorld.From(st, db); var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
        var who = pt.IdOf(c);
        string crimeKind = "theft";
        var rec = CrimeSystem.Commit(law, cb, new ActSpec(crimeKind, who, pt.IdOf(victim), town, sec, 300, 0, "ch_note_crime_greed"), [new WitnessRow(pt.IdOf(watch).Value, WitnessRow.KnewIt)], ActorLook.None);
        CrimeSystem.Report(law, rec, pt.IdOf(watch), sec + 600);
        Gaol.Arrest(law, cb, jb, who, town, sec + 1200, inAct: false, pt.IdOf(watch));
        rep.Scenes.Add($"court: {pt.FullName(c)} held for {crimeKind}, expected sentence {Sentences.Expect(law, cb, jb, rec, who, law.FiefOf(town)).Kind}");
        return who;
    }

    /// <summary>
    /// A hearth lights the thatch of a dwelling nobody is watching (the abstract rule, fire 4.5): three game hours of burning, a neighbour at risk. The abstract fire covers the structures the fire's room world holds (built and adopted
    /// ones); the scene raises a timber cottage of eight metres by six, thatched, with its hearth and a bed, far from anything else in the room world, and lets the hearth set it alight.
    /// </summary>
    private static void FireScene(WorldState st, long hour, Phase6TrailReport rep)
    {
        var fw = st.HomeRecords?.Sim.Get<Ravensguard.Sim.Burning.FireWorld>();
        if (fw == null) { rep.Scenes.Add("fire: no fire world"); return; }
        var rooms = fw.Rooms; const int x0 = 5000, y0 = 5000;
        var edits = new List<Ravensguard.Sim.Building.CellEdit>();
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
            {
                bool ring = x == 0 || y == 0 || x == 7 || y == 5;
                edits.Add(new(x0 + x, y0 + y, 0, Ravensguard.Sim.Building.CellLayer.Structure, ring ? (x == 3 && y == 5 ? "door_wood" : "wall_timber") : "floor_plank"));
            }
        edits.Add(new(x0 + 1, y0 + 1, 0, Ravensguard.Sim.Building.CellLayer.Fixture, "hearth")); edits.Add(new(x0 + 5, y0 + 1, 0, Ravensguard.Sim.Building.CellLayer.Fixture, "bed"));
        rooms.Apply(edits);
        int structure = rooms.StructureAt(x0 + 2, y0 + 2, 0);
        if (structure > 0) rooms.RoofOver("roof_thatch", [structure]);
        bool lit = structure > 0 && fw.IgniteBuilding(structure, hour, Ravensguard.Sim.Burning.FireCause.Hearth);
        rep.Scenes.Add($"fire: structure {(structure > 0 ? "built" : "none")} {(lit ? "lit" : "not lit")}");
    }

    /// <summary>An eviction in the home town fills its households' grievances and triggers a riot (unrest 4.2): the next daily pass raises it.</summary>
    private static void RiotScene(WorldState st, int town, int day, Phase6TrailReport rep)
    {
        var sim = st.HomeRecords!.Sim; var ub = st.Unrest ??= new UnrestBook();
        var hht = st.Population.Households; EntityId evicted = EntityId.None;
        for (int h = 0; h < hht.Count; h++)
            if (hht.MemberCount[h] > 0 && hht.Settlement[h] == town)
            {
                if (evicted == EntityId.None) evicted = hht.IdOf(h);
                st.Economy!.Grievances[h] = Grievance.Hunger | Grievance.Crowding;                      // a hungry winter in the town
            }
        UnrestConcede.Evicted(ub, sim, town, evicted, day, 0);                                          // and an eviction at its end: a trigger the next daily pass reads
        rep.Scenes.Add($"riot: eviction in {st.Population.Region.Settlements[town].Name}, trigger {ub.Peek(town)?.TriggerKind}");
    }

    /// <summary>A man held as a slave is sold at the first market town of the region whose law has one (slavery 4.2): the best buyer pays through the ledger.</summary>
    private static void SaleScene(WorldState st, ContentDatabase db, int home, int day, long sec, Phase6TrailReport rep)
    {
        var region = st.Population.Region; var pt = st.Population.People; var hht = st.Population.Households; var e = st.Economy!;
        var bw = BondWiring.World(st, db);
        int market = -1;
        for (int s = 0; s < region.Settlements.Count; s++) if (Slavery.HasMarket(bw.LawAt(s)) && hht.Count > 0) { market = s; break; }
        if (market < 0)
        {
            // the region's own cultures keep no slave market (the home region of seed 1234 is Valeholt and Highmarch): the biggest other town is taken for a Seaboard trading post (its culture set, as SL-7 does) so the region has one
            int coast = Enumerable.Range(0, 8).FirstOrDefault(c => Ravensguard.Sim.Faith.ReligionBook.CultureOf(db, c) is { } cu && BondRules.Parse(cu.Slavery) == SlaveLaw.Chattel, -1);
            int best = -1, bestHh = 0;
            for (int s = 0; s < region.Settlements.Count; s++)
            {
                if (s == home) continue;
                int n = 0; for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == s && hht.Beds[h] - hht.MemberCount[h] >= 1) n++;
                if (n > bestHh) { bestHh = n; best = s; }
            }
            if (coast >= 0 && best >= 0) { market = best; region.Settlements[market].Culture = coast; bw = BondWiring.World(st, db); }
        }
        var book = st.Bonds ??= new BondBook();
        if (market < 0) { rep.Scenes.Add("sale: no market in the region"); if (st.Bonds is { IsEmpty: true }) st.Bonds = null; return; }
        // a rich household of the town to buy, a poor one to own; the one who is sold is the owner's man
        var hhs = Enumerable.Range(0, hht.Count).Where(h => hht.MemberCount[h] > 0 && hht.Settlement[h] == market && hht.Beds[h] - hht.MemberCount[h] >= 1).ToList();
        if (hhs.Count < 2) { rep.Scenes.Add("sale: no households to trade"); if (st.Bonds is { IsEmpty: true }) st.Bonds = null; return; }
        int ownerHh = hhs[0];
        for (int i = 1; i < Math.Min(4, hhs.Count); i++) e.Ledger.Mint(e.HouseholdPurse[hhs[i]], new Coin(200_000), SourceReason.Seed);
        int person = hht.Members(hhs[1]).First(m => pt.IsAlive(m) && pt.AgeYears(m, day) is >= 16 and < 50 && m != st.Hero!.PersonIndex);
        var owner = hht.IdOf(ownerHh); var slave = pt.IdOf(person);
        Slavery.Make(bw, book, slave, owner, BondKind.Chattel, BondOrigin.Captured, day, market, 0, 0, 0);
        var res = Slavery.Sell(bw, book, owner, slave, market, day, sec);
        rep.Scenes.Add($"sale: {pt.FullName(person)} at {region.Settlements[market].Name}: {res.Key} {Money(res.Mites)} mites");
    }

    /// <summary>The great plague reaches the region at the home town (disease 4.2): the first epidemic starts.</summary>
    private static void PlagueScene(WorldState st, int town, int day, long sec, Phase6TrailReport rep)
    {
        var sim = st.HomeRecords!.Sim; var b = st.Disease ??= new DiseaseBook();
        var ob = new RegionOutbox(st.Ids);
        var gp = DiseaseEngine.BeginRegion(sim, b, day, GameTime.FromGameSeconds(sec), ob, town, 0, 1);
        ob.ApplyTo(st.Chronicle, null, new Dictionary<string, long>(StringComparer.Ordinal));
        rep.Scenes.Add($"plague: begins at {st.Population.Region.Settlements[town].Name}, {gp.Epidemics.Count} epidemic(s)");
    }

    /// <summary>A realm calls a council (rule 4.7) for its defence: the fief holders vote by their gains and fears and the sum is levied.</summary>
    private static void CouncilScene(WorldState st, int day, Phase6TrailReport rep)
    {
        var sim = st.HomeRecords!.Sim; var rb = RuleWiring.Bind(st, st.Content);
        if (rb == null) { rep.Scenes.Add("council: no rule book"); return; }
        var c = RuleWiring.Context(sim, rb, RuleChron.Of(st.Chronicle, st.Ids));
        int realm = rb.Fiefs.Values.Where(f => f.Realm >= 0 && f.Holder >= 0).Select(f => f.Realm).Order().First();
        var rec = RuleCouncil.Call(c, realm, "defence", day);
        rep.Scenes.Add($"council: realm {realm}, {rec.Yes} yes to {rec.No} no, {(rec.Passed ? "passed" : "refused")}");
    }

    /// <summary>A companion jailed in the gaol of a fief seat; the hero comes through the drain in the small hours, picks the bars and leaves unseen (heists 4.2, HJ-9).</summary>
    private static void HeistScene(WorldState st, ContentDatabase db, WorldRuntime rt, Func<HeroCommand, HeroResult> Do, int home, long dayBeforeStart, Phase6TrailReport rep)
    {
        var pt = st.Population.People; var region = st.Population.Region;
        var seats = new HashSet<int>(region.Fiefs.Select(f => f.SeatSettlement));
        var plan = HeistSites.Plan(region, pt);
        int town = plan.Where(x => x.Kind == HeistKind.Drain && seats.Contains(x.Settlement)).Select(x => x.Settlement).DefaultIfEmpty(-1).First();
        if (town < 0) { rep.Scenes.Add("heist: no drain at a seat"); return; }
        // the night before: a companion of the seat's, caught stealing under the eye of the watch, reported and held
        if (rt.HourDone < dayBeforeStart + 12) rt.AdvanceTo(GameTime.FromGameHours(dayBeforeStart + 12));
        int day = (int)(rt.HourDone / 24); long sec = rt.HourDone * 3600;
        var guards = new HashSet<int>(Guards.Of(pt, town));
        var held = new HashSet<ulong>(st.Justice != null ? st.Justice.Gaol.Keys : []);
        int c = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && pt.AgeYears(i, day) >= 18 && i != st.Hero!.PersonIndex && !held.Contains(pt.IdOf(i).Value));
        int victim = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && i != c && pt.AgeYears(i, day) >= 18);
        int watch = Guards.Of(pt, town)[0];
        var law = LawWorld.From(st, db); var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
        var comp = pt.IdOf(c);
        var rec = CrimeSystem.Commit(law, cb, new ActSpec("theft", comp, pt.IdOf(victim), town, sec, 240, 0, "ch_note_crime_greed"), [new WitnessRow(pt.IdOf(watch).Value, WitnessRow.KnewIt)], ActorLook.None);
        CrimeSystem.Report(law, rec, pt.IdOf(watch), sec + 600);
        Gaol.Arrest(law, cb, jb, comp, town, sec + 1200, inAct: false, pt.IdOf(watch));
        rt.AdvanceTo(GameTime.FromGameHours((rt.HourDone / 24 + 1) * 24));                        // midnight; the hero's next barrier is one o'clock
        long Sec() => (rt.HourDone + 1) * 3600;
        var enter = Do(HeistDesk.Enter(town, HeistKind.Drain, 0, Sec()));
        string last = enter.Text.ToString();
        if (enter.Ok)
            foreach (var a in new[] { "sneak|1", "go|5|2", "pry|0", "go|19|4", "pry|1", "go|33|4", "portal", "go|2|4", "pick|0", "go|2|2", "free|0", "go|1|10", "portal", "go|0|2", "leave" })
            {
                var q = Do(HeistDesk.Do(a, Sec())); last = q.Text.ToString();
                for (int again = 0; a == "pick|0" && q.Ok && st.Heists?.Run is { } r && r.Here.Door[0] == 0 && again < 5; again++) { q = Do(HeistDesk.Do(a, Sec())); last = q.Text.ToString(); }
                if (!q.Ok || st.Heists?.Run == null) break;
            }
        if (st.Heists?.Run != null) Do(HeistDesk.End(Sec()));
        rep.Scenes.Add($"heist: {region.Settlements[town].Name}: {last}");
    }
}
