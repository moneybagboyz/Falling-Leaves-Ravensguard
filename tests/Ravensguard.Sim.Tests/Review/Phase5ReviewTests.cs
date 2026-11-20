using System.Text.Json;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Hero;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// The Phase 5 code and bug check (docs/reviews/phase-5.md): one regression test for each High and Medium finding fixed in the review, each failing on the code before its fix. The races are
/// provoked the way the game meets them (the main thread changing the ride book under its lock while the tick worker reads it), so a missing lock shows as "collection was modified".
/// </summary>
public class Phase5ReviewTests
{
    private static LiveWorld Field(int z = 0)
    {
        var g = new LevelGrid(40, 20, 0, 1);
        g.FillRect(0, 0, 39, 19, 0, true); g.FillRect(0, 0, 39, 19, 1, true);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, z), GameTime.FromCalendar(1, Season.Spring, 8, 12));
    }

    // ------------------------------------------------------------------ F1: the live hash trail holds only what the live tier writes

    [Fact]
    public void F1_the_live_hash_trail_follows_the_driven_vehicle_and_ignores_what_the_tick_worker_does_to_sites_and_rows()
    {
        var w = Field();
        ulong none = LiveStateHash.Compute(w);
        var econ = EconFixture.NewEconomy(false);
        var b = new RideBook(EconFixture.Content, econ, 7);
        w.AttachRide(b);
        Assert.Equal(none, LiveStateHash.Compute(w));
        // a vehicle site of the hero's and a parked wagon: the tick worker's (sites, installs, voyages) and nobody's in the live tier: the trail does not move
        var owner = EntityId.Make(IdKind.Person, 77);
        var (a, site) = VehicleSites.Order(b, "ox_wagon", owner, 10_000, 20_000);
        Assert.Equal(SiteAnswer.Ok, a);
        var parked = b.Vehicles.Create(b.Templates.Get("cart"), EntityId.None, 30_000, 20_000);
        Assert.Equal(none, LiveStateHash.Compute(w));
        var ids = new IdAllocator();
        foreach (var part in VehicleSites.Wanted(site!, b.Templates)) VehicleSites.Deposit(b, econ.Items, site!, VehicleDesign.VehicleBuildTests.PartItem(econ, ids, part, owner), owner);
        b.Works.Sites[site!.Id].Crew.Add(owner); b.Works.Sites[site.Id].CrewSkill.Add(50);
        VehicleSystem.Hour(b, null, _ => null, econ.Items, 9, econ.Ledger);             // the worker's hour: a part fitted at the site
        Assert.True(b.Works.Sites.Count == 0 || b.Works.Sites[site.Id].WorkedMilli > 0);
        parked.XMm += 500;                                                             // (a voyage moves a boat's row the same way)
        Assert.Equal(none, LiveStateHash.Compute(w));                                  // before the fix: the site's work and the row changed the trail, so a replay (no worker) diverged
        // the vehicle the hero drives is the live tier's: its place and wear are in the trail
        var cart = b.Vehicles.Create(b.Templates.Get("cart"), EntityId.None, 5_500, 5_500);
        b.Driving = cart.Id; cart.Driven = true;
        ulong driving = LiveStateHash.Compute(w);
        Assert.NotEqual(none, driving);
        cart.XMm += 1500;
        Assert.NotEqual(driving, LiveStateHash.Compute(w));
    }

    // ------------------------------------------------------------------ F2: a save reads the ride book and the economy's stores under the ride book's lock

    [Fact]
    public void F2_a_save_on_the_tick_worker_copies_the_ride_book_and_the_stores_whole_while_the_main_thread_rides()
    {
        var econ = EconFixture.NewEconomy(false);
        var b = new RideBook(EconFixture.Content, econ, 7);
        var breed = b.Breeds.Get("horse_palfrey");
        var cart = b.Vehicles.Create(b.Templates.Get("cart"), EntityId.None, 10_000, 10_000);
        ushort oats = econ.Goods.IdOf("oats");
        cart.Cargo.Create(oats, 50_000, CreateReason.Seed);
        for (int i = 0; i < 20; i++) b.Animals.Create(breed, EntityId.None, 900, 0, 1000 * i, 0);
        Exception? error = null; int done = 0;
        var main = new Thread(() =>
        {
            // the main thread as the street runs it: a loan taken out and taken back, a bed unloaded into the heap and loaded again (each under the book's lock)
            long now = 0;
            while (Volatile.Read(ref done) == 0)
            {
                lock (b.Gate) { b.Loan(breed, EntityId.None, now, 0, 0, days: 0); b.Loan(breed, EntityId.None, now, 0, 0, days: 0); b.ExpireLoans(++now); }
                lock (b.Gate) { Store.Move(cart.Cargo, b.Heap, oats, 1000); Store.Move(b.Heap, cart.Cargo, oats, 1000); }
            }
        });
        main.Start();
        try
        {
            for (int i = 0; i < 2000; i++)
            {
                var (m, e) = WorldSave.CaptureRides(b, econ);
                Assert.NotNull(m); Assert.NotNull(e);
                Assert.Equal(20, m!.Animals.Count);                                          // never caught between a loan and its return
            }
        }
        catch (Exception ex) { error = ex; }
        finally { Volatile.Write(ref done, 1); main.Join(); }
        Assert.Null(error);
        Assert.True(econ.MassAuditHolds());
    }

    // ------------------------------------------------------------------ F3: the estates' daily pass reads the ride book's rows under its lock

    [Fact]
    public void F3_the_estates_daily_pass_on_the_tick_worker_survives_the_main_thread_taking_back_a_loaned_horse()
    {
        var r = InheritanceTests.Rig.Of(InheritanceTests.Game(1234, "disgraced_squire"));
        var b = r.St.Mounts = new RideBook(EconFixture.Content, r.St.Economy!, 1234);
        var breed = b.Breeds.Get("horse_palfrey");
        for (int i = 0; i < 20; i++) b.Animals.Create(breed, r.Ctx.Party.Hero, 900, 0, 1000 * i, 0);
        var est = r.Est;
        Exception? error = null; int done = 0;
        var main = new Thread(() =>
        {
            long now = 0;
            while (Volatile.Read(ref done) == 0)
            {
                lock (b.Gate) for (int k = 0; k < 4; k++) b.Loan(breed, EntityId.None, now, 0, 0, days: 0);
                b.ExpireLoans(++now);                                                         // before the fix the street called it without the lock
            }
        });
        main.Start();
        try { for (int i = 0; i < 2000; i++) est.Daily(r.Day, r.Sec); }
        catch (Exception ex) { error = ex; }
        finally { Volatile.Write(ref done, 1); main.Join(); }
        Assert.Null(error);                                                                   // before the fix: InvalidOperationException, "collection was modified", which faults the world's tick
        var (vehicles, animals) = b.RowsNow();
        Assert.Empty(vehicles); Assert.Equal(20, animals.Length);
    }

    // ------------------------------------------------------------------ F4: the hero's fall through a collapsing floor is in the replay trace

    [Fact]
    public void F4_a_replay_drops_the_hero_a_level_where_the_floor_fell_under_him()
    {
        var live = Field(z: 1);
        var rec = new SessionRecorder(live);
        for (int t = 0; t < 10; t++) rec.Step();
        rec.DropToLevel(0);                                                                   // what the street does on the construction world's HeroFell event
        for (int t = 0; t < 30; t++) rec.Step();
        Assert.Equal(0, live.Snapshot.Hero.Z);
        var (trace, csv) = rec.Export();
        Assert.Contains(rec.Commands.Items, c => c.Kind == ReplayKind.DropLevel && c.A == 0);
        var again = Field(z: 1);
        var res = ReplayRunner.Run(again, CommandLog.FromBytes(trace), StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.Equal(0, again.Snapshot.Hero.Z);
        // the street before the fix moved the hero without telling the recorder: the replay keeps him upstairs and the trail diverges
        var unrecorded = Field(z: 1);
        var rec2 = new SessionRecorder(unrecorded);
        for (int t = 0; t < 10; t++) rec2.Step();
        unrecorded.DropToLevel(0);
        for (int t = 0; t < 30; t++) rec2.Step();
        var (trace2, csv2) = rec2.Export();
        Assert.False(ReplayRunner.Run(Field(z: 1), CommandLog.FromBytes(trace2), StateHashTrail.FromCsv(csv2)).Matched);
    }

    // ------------------------------------------------------------------ F5: the v7 -> v8 step moves a crawl's fight and kits to the one quality scale

    [Fact]
    public void F5_the_v8_step_moves_the_quality_of_a_crawl_in_progress_its_fight_record_and_every_members_kit()
    {
        // a Phase 4 crawl in progress: a member with a masterwork sword (old byte 5) and standard mail (0), a fight below ground with the old bytes 0 and 6
        var kit = new FighterKit { Name = "Aldric", Weapon = new WeaponInstance("sword", "steel", (ItemQuality)5), Worn = [new WornItem("hauberk", "iron", (ItemQuality)0)] };
        string kitJson = JsonSerializer.Serialize(kit);
        var fight = new FightRecord { Fighters = [new FighterRow { Name = "a", WeaponQuality = 0, NowQuality = 6, WornQualities = [0, 3] }] };
        var sites = new SiteSave { Active = new CrawlSave { Site = 9, Members = [new CrawlMemberSave { Person = 1, Name = "Aldric", KitJson = kitJson }], Fight = fight.ToBytes() } };
        var v7 = new SaveFile(new SaveHeader { FormatVersion = 7 }, [new(SiteSave.SectionName, SaveCodec.SerializeCompressed(sites))]);
        var v8 = SaveMigrator.CreateDefault().Migrate(v7);
        var back = SaveCodec.DeserializeCompressed<SiteSave>(v8.Sections[SiteSave.SectionName].ToArray());
        var k = SiteSave.KitOf(back.Active!.Members[0].KitJson);
        Assert.Equal(ItemQuality.Masterwork, k.Weapon!.Quality);                              // before the fix: 5, past the end of the one scale
        Assert.Equal(ItemQuality.Common, k.Worn[0].Quality);                                   // before the fix: 0, read as Crude
        var f = FightRecord.FromBytes(back.Active.Fight).Fighters[0];
        Assert.Equal(((byte)ItemQuality.Common, (byte)ItemQuality.Masterwork), (f.WeaponQuality, f.NowQuality));
        Assert.Equal([(byte)ItemQuality.Common, (byte)ItemQuality.Fine], f.WornQualities);
        Assert.Equal("Aldric", k.Name);
    }
}
