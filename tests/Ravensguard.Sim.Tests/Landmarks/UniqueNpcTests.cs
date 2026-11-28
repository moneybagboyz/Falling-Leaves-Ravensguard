using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Landmarks;

/// <summary>Shared builds for the unique NPC tests: a populated home region per seed (landmarks placed, nothing else), built once.</summary>
internal static class NpcWorlds
{
    public static readonly ulong[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 1234];
    private static readonly Dictionary<ulong, Population> Cache = [];

    /// <summary>A fresh copy of the generated population of the seed's home region (landmarks placed, no NPC).</summary>
    public static Population Pop(ulong seed)
    {
        var w = TestWorld.Get(seed);
        lock (Cache)
        {
            if (!Cache.TryGetValue(seed, out var p))
            {
                LandmarkPlacer.Place(w.Region, w.History, w.Content);
                Cache[seed] = p = Populator.Populate(w.Region, w.History, w.Content);
            }
            return p.Clone();
        }
    }

    public static string[] Cultures(TestWorld w) => [.. w.Continent.Cultures.Select(c => c.Name)];

    public static WorldState NewGame(ulong seed, string origin = "")
    {
        var w = TestWorld.Get(seed);
        var dir = TestWorld.Content_.Dir;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var o = origin.Length > 0 ? w.Content.Get<OriginDef>(origin) : w.Content.All<OriginDef>().OrderBy(x => x.Order).First();
        var hero = HeroRecord.FromOrigin("Aldric Vane", false, o);
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.RegionSettings, w.Pick.CellX, w.Pick.CellY, Pop(seed), hero, null);
        st.Content = w.Content;
        return st;
    }
}

/// <summary>P8-LM-2 (unique NPCs, spec 03 gates LM-5 to LM-8): thirty authored characters with 20 to 50 lines each, one of each to a world, mortal like anyone, and per-culture names for their weapons and relics.</summary>
[Collection("resolver2")]
public class UniqueNpcTests(V2Fixture f, ITestOutputHelper output)
{
    private static ContentDatabase Db => TestWorld.Content_.Db;

    /// <summary>LM-5: thirty NPCs; each has 20 to 50 lines in en.csv and a placement rule that fits at least one seed of ten.</summary>
    [Fact]
    public void LM5_thirty_npcs_load_each_with_20_to_50_lines_and_a_rule_that_fits_a_seed()
    {
        var all = UniqueNpcs.All(Db);
        Assert.Equal(30, all.Count);
        Assert.Equal(22, all.Count(d => d.Place.Kind != "role"));
        Assert.Equal(8, all.Count(d => d.Place.Kind == "role"));
        var csv = Ravensguard.Sim.Tests.Dialogue.TalkKit.EnCsv(); var book = LineBook.Default;
        var skills = Enum.GetNames<Skill>().Select(n => n.ToLowerInvariant()).ToHashSet();
        var occs = Enum.GetNames<Occupation>().Select(n => n.ToLowerInvariant()).ToHashSet();
        var landmarks = Db.All<LandmarkDef>().ToDictionary(d => d.Id);
        foreach (var d in all)
        {
            // the lines: the dialogue of the speaker, its distinct text keys (what the NPC says and what the hero answers) are 20 to 50, each in en.csv
            var dlg = book.Dialogues.Where(x => x.Speaker == UniqueNpcs.SpeakerId(d)).ToList();
            Assert.True(dlg.Count == 1, $"{d.Id}: {dlg.Count} dialogues");
            var keys = dlg[0].Nodes.Values.SelectMany(n => new[] { n.Text }.Concat(n.Choices.Select(c => c.Text))).ToHashSet();
            Assert.InRange(keys.Count, 20, 50);
            foreach (var k in keys) Assert.True(csv.ContainsKey(k), $"{d.Id}: {k}");
            Assert.Equal(d.Id + ".intro", dlg[0].Id);
            // the life: a real occupation, skills, traits, attributes and class; the one contract is a kind the talk layer posts, and the dialogue posts exactly that
            Assert.Contains(d.Occupation.Replace("_", ""), occs);
            Assert.InRange(d.Skills.Count, 0, PersonTable.SkillSlots);
            foreach (var (s, l) in d.Skills) { Assert.Contains(s.Replace("_", ""), skills); Assert.InRange(l, 1, 100); }
            foreach (var t in d.Traits) Assert.Contains(t, UniqueNpcs.TraitNames);
            Assert.True(d.Attributes.Count is 0 or PersonTable.AttrCount, d.Id);
            Assert.Contains(d.Class, new[] { "peasant", "burgher", "gentry" });
            var effects = dlg[0].Nodes.Values.SelectMany(n => n.Effects).Where(e => e.Id == "start_contract").ToList();
            if (d.Contract.Length > 0) { Assert.Contains(d.Contract, BaseVocabulary.ContractKinds); Assert.Equal([d.Contract], effects.Select(e => e.Args["kind"])); }
            else if (d.Place.Kind != "role") Assert.Empty(effects);
            if (d.Heirloom.Length > 0) Assert.Contains(d.Heirloom, new[] { "weapon", "relic" });
            Assert.Contains(d.Schedule.Day, new[] { "landmark", "work", "home", "church", "square", "road" });
            Assert.Contains(d.Schedule.Night, new[] { "home", "church" });
            Assert.True(d.Place.Kind is "landmark" or "settlement" or "role", d.Id);
            if (d.Place.Kind != "role") { Assert.True(d.Given.Length > 0, d.Id); Assert.InRange(d.AgeMin, 16, 90); Assert.InRange(d.AgeMax, d.AgeMin, 90); }
            foreach (var lm in d.Place.Landmarks) { Assert.True(landmarks.TryGetValue(lm, out var def), $"{d.Id}: landmark {lm}"); Assert.Equal(d.Id, def!.Keeper); }
            if (d.Place.Origin.Length > 0) Assert.True(Db.Contains("origin", d.Place.Origin), d.Id);
        }
        // a landmark names its keeper back: every landmark that has one is on that NPC's list, and no keeper is left without a landmark
        foreach (var l in landmarks.Values.Where(l => l.Keeper.Length > 0))
        {
            var k = all.Single(d => d.Id == l.Keeper);
            Assert.Contains(l.Id, k.Place.Landmarks);
            Assert.True(l.LoreKeeperDead.Contains("{person}"), l.Id);
        }
        foreach (var k in all.Where(d => d.Place.Kind == "landmark")) Assert.NotEmpty(k.Place.Landmarks);
        // the lines are distinct text, and no key is shared between two NPCs but the common "back" choice
        var seen = new Dictionary<string, string>();
        foreach (var d in all.Where(d => d.Place.Kind != "role"))
            foreach (var k in book.Dialogues.Single(x => x.Speaker == UniqueNpcs.SpeakerId(d)).Nodes.Values.SelectMany(n => new[] { n.Text }.Concat(n.Choices.Select(c => c.Text))).Where(k => k != "D_ONB_BACK").Distinct())
                Assert.True(seen.TryAdd(k, d.Id), $"{k} is in {d.Id} and {seen[k]}");

        // the rule fits: each placeable NPC has a region among the ten seeds' home regions where it is placed (a rival for its own origin)
        var fits = new Dictionary<string, int>();
        foreach (var seed in NpcWorlds.Seeds)
        {
            var w = TestWorld.Get(seed); var pop = NpcWorlds.Pop(seed);
            foreach (var d in all.Where(d => d.Place.Kind != "role"))
                if (UniqueNpcs.Fit(d, pop.Region, w.History, d.Place.Origin, seed) != null) fits[d.Id] = fits.GetValueOrDefault(d.Id) + 1;
        }
        output.WriteLine(string.Join(", ", all.Where(d => d.Place.Kind != "role").Select(d => $"{d.Id}={fits.GetValueOrDefault(d.Id)}")));
        foreach (var d in all.Where(d => d.Place.Kind != "role")) Assert.True(fits.GetValueOrDefault(d.Id) >= 1, $"{d.Id} fits none of the ten seeds");
        // a mentor's rule is a trade the region has (the onboarding picks the person)
        var pop1 = NpcWorlds.Pop(1234);
        foreach (var d in all.Where(d => d.Place.Kind == "role"))
        {
            var trades = Ravensguard.Sim.Onboarding.StartSiteFinder.MentorTrades(d.Id);
            Assert.NotEmpty(trades);
            Assert.True(Enumerable.Range(0, pop1.People.Count).Any(i => trades.Contains(pop1.People.Occupation[i])), d.Id);
        }
    }

    /// <summary>LM-6: no unique NPC twice in a world; placing again, or with the world's other characters named, adds none.</summary>
    [Fact]
    public void LM6_no_unique_npc_twice_in_a_world()
    {
        int placedTotal = 0;
        foreach (var seed in NpcWorlds.Seeds)
        {
            var w = TestWorld.Get(seed); var cultures = NpcWorlds.Cultures(w);
            var pop = NpcWorlds.Pop(seed);
            var first = UniqueNpcs.Place(pop, w.History, w.Content, seed, "", null, cultures);
            placedTotal += first.Count;
            Assert.Equal(first.Count, first.Select(p => p.Npc).Distinct().Count());
            Assert.Equal(first.Select(p => p.Npc).Order(), pop.People.Authored.Values.Select(a => a.Id).Order());
            Assert.Empty(UniqueNpcs.Place(pop, w.History, w.Content, seed, "", null, cultures));                     // again: nobody is placed twice
            // the world's other regions: a neighbour made with the first one's characters named takes none of them
            var home = RegionCoord.Snap(w.Pick.CellX, w.Pick.CellY);
            var other = RegionGenerator.Generate(w.Continent, w.History, home.Offset(1, 0), w.RegionSettings);
            LandmarkPlacer.Place(other, w.History, w.Content);
            var pop2 = Populator.Populate(other, w.History, w.Content);
            var second = UniqueNpcs.Place(pop2, w.History, w.Content, seed, "", UniqueNpcs.Present(pop.People), cultures);
            Assert.Empty(second.Select(p => p.Npc).Intersect(first.Select(p => p.Npc)));
            // every placed one is a person like any other: alive, in a household of their own, never a child, at a settlement of the region
            foreach (var p in first)
            {
                Assert.Equal(PersonStatus.Alive, pop.People.Status[p.Row]);
                Assert.Equal(p.Row, pop.Households.Head[pop.People.Household[p.Row]]);
                Assert.True(pop.People.AgeYears(p.Row, pop.NowDay) >= 16);
                Assert.Equal(p.Settlement, pop.People.HomeSettlement[p.Row]);
                Assert.Equal(UniqueNpcs.SpeakerId(Db.Get<UniqueNpcDef>(p.Npc)), UniqueNpcs.SpeakerOf(pop.People, Db, p.Row));
            }
        }
        output.WriteLine($"{placedTotal} placed over {NpcWorlds.Seeds.Length} seeds");
        Assert.True(placedTotal >= NpcWorlds.Seeds.Length, "a seed should hold an NPC or more on average");
        // a rival comes only for the hero's own origin
        var w1 = TestWorld.Get(1234); var rival = NpcWorlds.Pop(1234);
        var rivals = UniqueNpcs.Place(rival, w1.History, w1.Content, 1234, "peddler", null, NpcWorlds.Cultures(w1)).Select(p => p.Npc);
        Assert.DoesNotContain("rival_squire_piers", rivals);
        Assert.Contains("rival_peddler_jory", rivals);
        // placement is a pure function of the seed: the same world twice gives the same people
        var a = NpcWorlds.Pop(1234); var b = NpcWorlds.Pop(1234);
        UniqueNpcs.Place(a, w1.History, w1.Content, 1234, "peddler", null, NpcWorlds.Cultures(w1)); UniqueNpcs.Place(b, w1.History, w1.Content, 1234, "peddler", null, NpcWorlds.Cultures(w1));
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }

    /// <summary>LM-7: a unique NPC killed in a fight leaves a corpse, an estate and the "since the death" lore; their lines are not used again; and they were placed as a person row, kept by the save.</summary>
    [Fact]
    public void LM7_a_unique_npc_killed_in_a_fight_leaves_a_corpse_an_estate_and_the_since_the_death_lore()
    {
        var st = NpcWorlds.NewGame(2);                                          // seed 2: a beacon hill, so Corwin Highfire keeps it
        var pt = st.Population.People; var pop = st.Population;
        int row = pt.RowOfAuthored("beacon_watcher_corwin");
        Assert.True(row >= 0, "seed 2's region has the beacon hill");
        var info = pt.AuthoredOf(row)!;
        var mark = pop.Region.Landmarks!.Single(m => m.X == info.X && m.Y == info.Y);
        Assert.Equal("beacon_hill", mark.TemplateId);
        Assert.Equal(mark.Lore, UniqueNpcs.LoreOf(mark, Db, pt));                // alive: the lore is as placed
        Assert.Equal("beacon_watcher_corwin", UniqueNpcs.SpeakerOf(pt, Db, row));
        string name = pt.FullName(row); Assert.Equal("Corwin Highfire", name);
        var audit0 = st.Economy!.Ledger.Audit(); Assert.True(audit0.IsConserved);
        Assert.Equal(PersonStatus.Alive, pt.Status[row]);
        int hh = pt.Household[row]; Assert.Equal(1, pop.Households.MemberCount[hh]);

        // the fight: a femoral artery cut in a wound book of the person's key, run to its end by the same function the live tier uses, then turned into the person's death
        var sim = st.HomeRecords!.Sim; var ledger = new WoundLedger(st.Seed);
        ulong key = pt.IdOf(row).Value;
        var book = new WoundBook(st.Seed, key, 5000, 10, "human") { RegenPm = 0 };
        int part = f.P("upper_leg_l"); var bp = f.Human.Parts[part]; int si = Array.FindIndex(bp.Structures, s => s.Id == "femoral_artery"); Assert.True(si >= 0);
        var cut = new WoundV2 { Part = (short)part, Tissue = (short)bp.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = 1000, Flags = WoundFlags.ArteryHit };
        book.Apply(f.Human, [cut], 0, 0, 0, 0, false);
        ledger.Adopt(key, book, 0);
        ledger.AdvanceTo(3600 * 3);
        var ob = new RegionOutbox(st.Ids);
        WoundSystem.Resolve(sim, ob, ledger);

        // a corpse: the row stays, dead, with a cause and a day; the wound book is the body's
        Assert.Equal(PersonStatus.Dead, pt.Status[row]); Assert.Equal(DeathCause.BloodLoss, pt.DeathCause[row]); Assert.True(pt.DeathDay[row] >= 0);
        Assert.True(pt.IsAuthored(row));
        // the death is in the chronicle, named and caused
        var death = ob.Entries.Single(e => e.TypeId == "death");
        Assert.Contains(pt.IdOf(row), death.Subjects); Assert.NotEmpty(death.Causes);
        // an estate: the household had no heir, so it passes to the lord's (escheat) with its entry, the coin conserved
        Assert.Equal(0, pop.Households.MemberCount[hh]);
        Assert.Contains(ob.Entries, e => e.TypeId == "escheat");
        Assert.True(st.Economy.Ledger.Audit().IsConserved); Assert.True(st.Economy.MassAuditHolds());
        // the lore says so, once; the lines are never used again
        string lore = UniqueNpcs.LoreOf(mark, Db, pt);
        Assert.StartsWith(mark.Lore, lore);
        Assert.Contains("Since the death of Corwin Highfire", lore);
        Assert.DoesNotContain("{", lore);
        Assert.Null(UniqueNpcs.SpeakerOf(pt, Db, row));
        // the Codex page follows
        var codex = Ravensguard.Sim.Codex.CodexBook.Build(st.History!, pop.Region, []);
        UniqueNpcs.RefreshCodex(codex, pop.Region, Db, pt);
        Assert.Contains("Since the death of Corwin Highfire", codex.Get("landmark." + mark.Index)!.Text);
        // a dead keeper's row is never dropped by the yearly compaction
        Assert.DoesNotContain(row, PersonCompaction.Candidates(st, new GameTime((long)(pt.DeathDay[row] + 40 * TimeConst.DaysPerYear) * 86400)));
    }

    /// <summary>The authored rows and their posts are saved and come back (optional people key 48: a save of a world with none keeps its bytes), and a keeper's day is at their post.</summary>
    [Fact]
    public void An_authored_row_is_saved_and_a_keepers_day_is_spent_at_their_post()
    {
        var st = NpcWorlds.NewGame(2);
        var pt = st.Population.People; int row = pt.RowOfAuthored("beacon_watcher_corwin"); var info = pt.AuthoredOf(row)!;
        Assert.True(info.X >= 0 && info.Y >= 0);
        Assert.Equal(st.Population.Region.Settlements[pt.HomeSettlement[row]].Id, st.Population.Region.Settlements[UniqueNpcs.NearestSettlement(st.Population.Region, info.X, info.Y)].Id);
        var an = PersonAnchors.From(st.Population, row, st.Seed, 0, st.Population.NowDay * 86400L);
        Assert.True(an.HasWork); Assert.Equal(info.X * 1000L, an.WorkX); Assert.Equal(info.Y * 1000L, an.WorkY);
        var file = WorldSave.Capture(st, "h", "0.1");
        var w = TestWorld.Get(2);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), w.Content, "h", null, w.Continent, w.History);
        var bt = back.Population.People;
        Assert.Equal(pt.Authored.Select(a => (a.Key, a.Value)), bt.Authored.Select(a => (a.Key, a.Value)));
        Assert.Equal(pt.FullName(row), bt.FullName(row));
        // a world with no authored character writes no key 48
        var none = NpcWorlds.Pop(2);
        Assert.Null(WorldSave.EncodePeople(none).Authored);
        Assert.NotNull(WorldSave.EncodePeople(st.Population).Authored);
    }

    /// <summary>LM-8: the eight cultures of the continent each name a relic and a weapon from their own tables; no two tables are alike.</summary>
    [Fact]
    public void LM8_eight_cultures_each_name_a_relic_and_a_weapon_from_their_own_tables()
    {
        var cultures = ContinentSettings.LoadDefaults(TestWorld.Content_.Dir).CultureNames;
        Assert.Equal(8, cultures.Count);
        var pools = Db.All<ArtefactNamesDef>().ToList();
        var def = pools.Single(p => p.Culture.Length == 0);
        var weaponSets = new List<string>(); var relicSets = new List<string>();
        foreach (var c in cultures)
        {
            var pool = Artefacts.PoolOf(Db, c); Assert.NotNull(pool);
            Assert.InRange(pool!.Weapons.Count, 8, 24); Assert.InRange(pool.Relics.Count, 8, 24); Assert.InRange(pool.Heirlooms.Count, 8, 24);
            Assert.Equal(pool.Weapons.Count, pool.Weapons.Distinct().Count());
            Assert.Empty(pool.Weapons.Intersect(def.Epithets));
            for (ulong item = 1; item <= 60; item++)
            {
                string weapon = Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, item), "Wulfric Ashford", c);
                Assert.EndsWith(" of Wulfric", weapon);
                Assert.Contains(weapon[..^" of Wulfric".Length], pool.Weapons);                      // from this culture's table, not the default pool
                Assert.Equal(weapon, Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, item), "Wulfric Ashford", c));
                Assert.Contains(Artefacts.RelicName(Db, 7, item, c), pool.Relics);
                Assert.Contains(Artefacts.HeirloomName(Db, 7, item, c), pool.Heirlooms);
            }
            weaponSets.Add(string.Join("|", pool.Weapons.Order())); relicSets.Add(string.Join("|", pool.Relics.Order()));
            // the culture's whole table is reached (the draw is not stuck on one name)
            Assert.True(Enumerable.Range(1, 400).Select(i => Artefacts.RelicName(Db, 7, (ulong)i, c)).Distinct().Count() >= pool.Relics.Count - 1);
        }
        Assert.Equal(8, weaponSets.Distinct().Count()); Assert.Equal(8, relicSets.Distinct().Count());
        Assert.Empty(cultures.SelectMany(c => Artefacts.PoolOf(Db, c)!.Relics).GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key));   // no relic name in two cultures' tables
        // a culture with no table (or none given) draws from the default pool, as before
        string plain = Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, 5), "Wulfric");
        Assert.Equal(plain, Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, 5), "Wulfric", "Nowhere"));
        Assert.Contains(plain[..^" of Wulfric".Length], def.Epithets);
        // an NPC's heirloom is named from the table of the culture of the place they were put in
        var w = TestWorld.Get(2); var pop = NpcWorlds.Pop(2);
        var placed = UniqueNpcs.Place(pop, w.History, w.Content, 2, "", null, NpcWorlds.Cultures(w));
        foreach (var p in placed.Where(p => p.Heirloom.Length > 0))
        {
            var d = Db.Get<UniqueNpcDef>(p.Npc);
            string cu = NpcWorlds.Cultures(w)[pop.Region.Settlements[p.Settlement].Culture]; var pool = Artefacts.PoolOf(Db, cu)!;
            Assert.Contains(p.Heirloom, d.Heirloom == "relic" ? pool.Relics : pool.Heirlooms);
            Assert.Equal(p.Heirloom, pop.People.AuthoredOf(p.Row)!.Heirloom);
        }
    }
}
