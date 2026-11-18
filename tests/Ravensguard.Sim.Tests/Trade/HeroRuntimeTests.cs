#pragma warning disable xUnit1031 // these tests drive the tick worker on purpose and wait for its tasks
using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.Trade;

/// <summary>The hero's commands on the running world: applied at the barrier after the hour they were asked in, the same at 1x and 8x; and the party in the save.</summary>
public class HeroRuntimeTests
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var origin = w.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, origin);
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, hero, null));
    }

    /// <summary>The script: what the hero asks and when (game seconds after the opening hour), chosen from the opening state so both runs ask the same.</summary>
    private static List<(long At, HeroCommand Cmd)> Script(WorldState s)
    {
        var books = s.Markets!.AllBooks().OrderByDescending(b => b.Goods.Sum(g => g.StockUnits)).ToList();
        var book = books[0];
        var goods = Enumerable.Range(0, book.Goods.Length).Where(g => book.Goods[g].StockUnits > 50_000 && !s.Economy!.Goods.IsPiece((ushort)g)).Take(3).Select(g => (ushort)g).ToList();
        Assert.NotEmpty(goods);
        var p = s.Population.People; int day = (int)(s.Abstract!.HourDone / 24);
        var person = Enumerable.Range(0, p.Count).First(i => p.IsAlive(i) && p.AgeYears(i, day) is >= 20 and < 45 && p.Household[i] >= 0);
        int st = book.SettlementIndex;
        var l = new List<(long, HeroCommand)>
        {
            (40, new HeroCommand { Kind = HeroCommandKind.VisitMarket, Settlement = st }),
            (300, new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = st, Good = goods[0], Qty = 4_000 }),
            (1_900, new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = st, Good = goods[^1], Qty = 2_500 }),
            (3_610, new HeroCommand { Kind = HeroCommandKind.Sell, Settlement = st, Good = goods[0], Qty = 1_000 }),
            (3_620, new HeroCommand { Kind = HeroCommandKind.Talk, Person = p.IdOf(person) }),
            (7_300, new HeroCommand { Kind = HeroCommandKind.Talk, Person = p.IdOf(person) }),
            (7_330, new HeroCommand { Kind = HeroCommandKind.Hire, Person = p.IdOf(person), Wage = 2_000, Days = 3 }),
            (10_810, new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = st, Good = goods[0], Qty = 9_000 }),
            (14_401, new HeroCommand { Kind = HeroCommandKind.Order, Person = p.IdOf(person), Order = SquadOrder.Hold, X = 1000, Y = 2000 }),
            (18_000 - 5, new HeroCommand { Kind = HeroCommandKind.Sell, Settlement = st, Good = goods[^1], Qty = 2_500 }),
        };
        return l;
    }

    /// <summary>Plays six game hours on the real worker at the given speed (game seconds per 20 Hz tick), issuing the script at its game seconds, then returns the records and the hero's results.</summary>
    private static (string Records, byte[] Party, string Results) Play(int gameSecPerTick, int seedJitter)
    {
        var (w, s) = Game();
        var script = Script(s);
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var hw = HeroWorld.Attach(rt, w.Content)!;
        rt.Start();
        var home = s.HomeRegion;
        long tStart = s.Abstract!.HourDone * 3600;
        long t0 = (tStart / 86400 + (tStart % 86400 >= 9 * 3600 ? 1 : 0)) * 86400 + 9 * 3600;   // the script starts at the next 09:00 (market hours)
        long end = t0 + 6 * 3600;
        int next = 0; var rng = new Random(seedJitter);
        for (long t = tStart; t <= end; t += gameSecPerTick)
        {
            while (next < script.Count && t0 + script[next].At <= t) { hw.Issue(script[next].Cmd with { IssuedSec = t0 + script[next].At }); next++; }
            rt.Pump(new GameTime(t), home.OriginXM + 16000, home.OriginYM + 16000);
            if (rng.Next(50) == 0) Thread.Sleep(rng.Next(3));          // the main thread's frames are uneven
        }
        while (rt.HourDone < end / 3600) { rt.Pump(new GameTime(end), home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(2); }
        hw.Kick().GetAwaiter().GetResult();
        rt.Quiesce();
        Assert.Null(rt.Fault);
        Assert.Equal(0, hw.Desk.Pending);
        var (records, party) = rt.AtBarrier(() => (rt.Records.HomeWorld!.RecordsHash(), SaveCodec.SerializeCompressed(HeroPartySave.Capture(s.Party!)))).GetAwaiter().GetResult();
        var results = string.Join("\n", hw.View!.Results.Where(r => r.Seq > 0).Select(r => $"{r.Seq} {r.Code} {r.Text}"));
        return (records, party, results);
    }

    [Fact]
    public void Hero_commands_apply_at_the_barrier_after_their_hour_the_same_at_1x_and_8x()
    {
        var a = Play(1, 1);
        var b = Play(8, 2);
        Assert.Equal(a.Results, b.Results);
        Assert.Equal(a.Party, b.Party);
        Assert.Equal(a.Records, b.Records);
        Assert.Contains("Bought", a.Results);
        Assert.Contains("agrees", a.Results);
    }

    [Fact]
    public void A_command_waits_for_the_hour_it_was_asked_in()
    {
        var w = new TradeWorld();
        long hour = w.Sec / 3600;
        var c = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Talk, Person = w.State.Population.People.IdOf(0), IssuedSec = (hour + 1) * 3600 + 5 });
        Assert.Equal(0, w.Desk.ApplyDue(w.Ctx, hour));                     // the world has not finished the next hour yet
        Assert.Equal(1, w.Desk.Pending);
        Assert.Equal(1, w.Desk.ApplyDue(w.Ctx, hour + 1));
        Assert.Equal(c.Seq, w.Desk.View!.AppliedSeq);
    }

    [Fact]
    public void Save_load_save_is_byte_identical_with_a_full_pack_an_unfinished_craft_and_a_hired_hand()
    {
        var w = new TradeWorld(heroCrowns: 3_000);
        foreach (var g in new[] { "flour", "wheat", "salt", "ale" }) Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = w.G(g), Qty = 3_000 }).Ok);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.VisitMarket }).Ok);
        // an unfinished craft in the world's crafting state, its components in the job's store
        var db = w.Ctx.Recipes; var site = new CraftSiteSpec(255, true, [.. db.Tools.Keys], [.. db.Stations.Keys], []);
        var r = db["bake_loaf"];
        foreach (var slot in r.Slots) w.Ctx.Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Seed);
        foreach (var st in r.Steps) if (st.FuelGood != ItemTable.NoGood) w.Ctx.Pack.Create(st.FuelGood, st.FuelQty, CreateReason.Seed);
        w.Ctx.Craft.State.Book.Set(w.Ctx.Party.Hero, Skill.Baking, 40 * CraftMath.MicroPerLevel);
        var start = w.Do(new HeroCommand { Kind = HeroCommandKind.CraftStart, Recipe = r.Index, Site = site });
        Assert.True(start.Ok, start.Text);
        var job = w.Ctx.Craft.State.Jobs.Values.Single(j => j.Owner == w.Ctx.Party.Hero);
        w.Sec += 300;
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = job.Id, UntilSec = w.Sec }).Ok);
        Assert.False(job.Closed);
        Assert.Equal(300, job.TotalSeconds);
        // a hired companion
        var p = w.State.Population.People; int day = (int)(w.Sec / 86400);
        int i = Enumerable.Range(0, p.Count).First(k => p.IsAlive(k) && p.AgeYears(k, day) is >= 20 and < 45 && p.Household[k] >= 0);
        var hire = w.Do(new HeroCommand { Kind = HeroCommandKind.Hire, Person = p.IdOf(i), Wage = 2_000, Days = 5 });
        Assert.True(hire.Ok, hire.Text);
        Assert.True(w.Econ.MassAuditHolds());
        w.State.Markets = null;                                            // this bare world has no production section to restore markets against; the book's goods stay in the economy
        var bytes = SaveCodec.Encode(WorldSave.Capture(w.State, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        Assert.NotNull(back.Party);
        Assert.Single(back.Party!.Squad.Members);
        Assert.Equal(job.TotalSeconds, back.Crafting!.Get(job.Id)!.TotalSeconds);
        Assert.Equal(w.State.Party!.Carried.GoodIn, back.Party.Carried.GoodIn);
        Assert.Equal(w.Packed("flour"), back.Economy!.OtherStores[back.Party.Hero].Qty(w.G("flour")));
        // the loaded party works on: the job resumes on the world clock and finishes
        var ctx = new HeroContext(back, back.Party, EconFixture.Content);
        var desk = new HeroDesk(back.Party.LastSeq);
        var lj = back.Crafting.Get(job.Id)!;
        var cmd = desk.Issue(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = job.Id, UntilSec = w.Sec + 6 * 3600, IssuedSec = w.Sec + 1 });
        desk.ApplyDue(ctx, cmd.Hour);
        Assert.True(lj.TotalSeconds > 300);
    }

    [Fact]
    public void The_character_inventory_and_squad_keys_have_no_conflicts()
    {
        var src = File.ReadAllText(Path.Combine(FindRepo(), "src", "Ravensguard.Game", "Input", "InputDefs.cs"));
        var rx = new Regex(@"A\((\w+), ""(\w+)""((?:, (?:K|M|Binding\.P)\([^)]*\))+)\);");
        var all = rx.Matches(src).Select(m => (Ctx: m.Groups[1].Value, Name: m.Groups[2].Value, Keys: Regex.Matches(m.Groups[3].Value, @"(?:K|M|Binding\.P)\(([^)]*)\)").Select(k => k.Groups[1].Value.Replace(" ", "")).ToList())).ToList();
        Assert.Contains(all, a => a.Ctx == "W" && a.Name == "panel_character" && a.Keys.Contains("Key.C"));
        Assert.Contains(all, a => a.Ctx == "W" && a.Name == "panel_inventory" && a.Keys.Contains("Key.I"));
        Assert.Contains(all, a => a.Ctx == "W" && a.Name == "panel_squad" && a.Keys.Contains("Key.P"));
        // no key twice inside a context, and none between World and the layers active with it
        foreach (var grp in all.GroupBy(a => a.Ctx))
        {
            var dup = grp.SelectMany(a => a.Keys.Select(k => (k, a.Name))).GroupBy(x => x.k).Where(g => g.Select(x => x.Name).Distinct().Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dup.Count == 0, $"context {grp.Key}: {string.Join(", ", dup)} bound twice");
        }
        // C, I and P are bound once in World and nowhere in the contexts live beside it (Global always, Build over World; KeyMap.ActiveTogether)
        foreach (var key in new[] { "Key.C", "Key.I", "Key.P" })
            Assert.Single(all, a => a.Ctx is "W" or "G" or "B" && a.Keys.Contains(key));
        // the trade screen's own keys (a modal context) include N for the notebook and Tab for the pane
        Assert.Contains(all, a => a.Ctx == "T" && a.Name == "notebook" && a.Keys.Contains("Key.N"));
        Assert.Contains(all, a => a.Ctx == "T" && a.Name == "pane" && a.Keys.Contains("Key.Tab"));
    }

    private static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
