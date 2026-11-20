# Resume drift of a whole `WorldRuntime`: diagnosis

**Reported (Phase 5 land helper):** "A whole `WorldRuntime` reloaded mid-game ends up with different economy, markets and production sections from one that ran on, even with no land at all."

**Base:** `1a10645` (Phase 4 closed, no Phase 5 code). No production code was changed for this diagnosis.

## Fixed
On branch `p5-resume-fix`, from `f1a9fea` (Phase 4 closed plus Phase 5 step 1: A1 to A4 merged). The three fixes proposed below:

1. **The continent is saved.** `WorldSave.Snapshot` sets `ContinentSt = ContinentSave.Capture(w.ContinentState)` (a deep copy, taken where the rest of the capture is taken: at the barrier). The `continent` section is optional and the reader already handled it, so there is no version bump. `WorldRuntime.Init`'s fallback for a save without the section (one from before the continent tick, or any save written before this fix) now builds the continent the way a new game does: after the home records exist it runs `ContinentTick.Run` once, as `RegionServices.EnsureContinent` does, which sets the price indexes and summaries and sets `LastSeason` to the current season, so that season is not ticked twice. A save that has other regions but no continent section logs a `Trace` warning (caravans and travellers in flight are lost in such a save).
2. **`SeedDebtG` grows on its own.** `FarmingSystem.SyncPopulation` grows the saved table to the household count independently of the working arrays (`GrowSeedDebt`). The two uses in `FarmingSystem.Seasonal` are guarded: the seed loan grows the table to at least the household count before writing (it only writes when something was lent), and the repayment treats a household past the end of the table as having no debt. A short table can no longer throw on the worker.
3. **Each wound book has its own day cursor.** `WoundBook.DayDone` is the last day whose daily step the book has had, or the day it joined the ledger. `WoundLedger.AdvanceTo` runs each book's `DailyStep` only for days after its own cursor (a book only touches itself and its rolls are keyed by seed, day and key, so book by book equals the old day-by-day order). A book made by `WoundLedger.For` starts on the ledger's day, as before. `CaravanAmbush` and `FightWorld.KeepBooksInLedger` put their books in with the new `WoundLedger.Adopt(key, book, nowSec)`, which starts the cursor no earlier than the day of the insert, so no `AdvanceTo` before the insert is needed. The cursor is saved as the optional key 31 of `WoundBookSave` (`DayDonePlus1`, 0 when absent); a v8 save from before it gives every ledger book the ledger's `LastDay`, which is exactly the old behaviour. No version bump. The ledger's hash is unchanged (the cursor is not in it), so no wound golden moved; the section bytes carry it.

**Tests.** `WorldRuntimeResumeTests` (below, committed) passes in all six cases, plus a seventh: a hero (a person row since P5-A4, with a party) who has bought a town lot and rented a stall through the hero's desk, with a named artefact on the crafting books, saved at N = 340 and continued over the turn of the season and the continent's tick (M = 72). That case asserts that the `land`, `hero`, `party`, `crafting` and `people` sections are present and compares them byte for byte with every other section. `WoundTests.WB17` is the focused test for cause 3 (the two ledgers at days 15,000 and 15,019 now agree; the cursor round-trips through the save; an older save's books take the ledger's day).

## Result
The symptom reproduces. Save → load → continue does **not** equal an uninterrupted run at the level of a full `WorldRuntime`. It fails at every N tried: mid-day, on a day boundary, across the continent's season tick and across the year boundary. It fails on the calling thread and on the tick worker alike.

There are two causes and one latent Phase 4 hazard:

1. **The continent section is never written** (the main cause, and the only cause in a world with just the home region). The abstract continent and its caravans are lost at every save. On load the runtime quietly builds a fresh continent instead. After the next midnight the home economy, markets and production drift.
2. **The farming `SeedDebtG` table is not grown after a load** when a household was founded between the last daily sync and the save. Any region's farming section then differs. Seed borrowing or repayment by that household can also throw `IndexOutOfRangeException` on the worker.
3. **Latent, not seen in these runs:** an empty wound ledger is not saved, and the load rebuilds its day cursor from the clock. A wound book dropped into it later gets daily steps for days before it existed.

When the continent is carried across the load by hand (`ContinentSave.Capture` → `Restore`, which is exactly what the fix restores), a home-only world matches byte for byte. It matches for 72 hours after N = 13, 48 and 360, and over M = 48 after N = 385, 1430 and 1440, on the worker thread too. With five resident neighbours it also matches, except for cause 2.

No worker-thread race, iteration-order leak, float use or unsaved RNG key was found. The worker path and the synchronous path agree hour for hour.

## Reproduction
Seed 1234, the home region `BlockOrigin.Offset(1, 1)`, no hero. The opening `HourDone` is 359,999 (the last hour before day 15,000, the first day of spring). A game year is 1,440 hours (four 15-day seasons).

Straight run: the runtime runs N + M hours. Resumed run: the runtime runs N hours, `WorldSave.Capture` runs at a barrier, then `SaveCodec.Encode`, `Decode`, `WorldSave.Restore`, a new `WorldRuntime`, and the remaining hours up to N + M. Both runs then compare every save section except `meta`, plus `ContinentSave.Capture(state.ContinentState)` and each resident region's `RecordsHash()`. The test is at the end of this document.

| N | M | Mode | Sections that differ |
|---|---|---|---|
| 13 | 48 | synchronous | abstract, economy, markets, production, continent, home records |
| 48 | 48 | synchronous | abstract, economy, markets, production, continent, home records |
| 385 | 48 | synchronous (season tick inside M) | abstract, economy, **farming**, markets, **people**, production, continent, home records |
| 1430 | 48 | synchronous (year boundary inside M) | abstract, economy, markets, production, continent, home records |
| 48 | 48 | tick worker, `Pump` and barriers | abstract, economy, markets, production, continent, home records |
| 13 | 48 | tick worker, 5 resident neighbours | abstract, economy, markets, production, region.125, region.140, region.157, continent, records of 125, 140, 141 and 157 |

### Bisection (field by field, hour by hour after the load)
Right after the load (0 hours stepped), the only differences are in the continent. Every home section is still identical, so restoring the home region itself is exact.

| Field | Straight | Resumed |
|---|---|---|
| `LastSeason` | 1000 | −1 |
| `Caravans.Count` | 19 at N = 48 (4 at N = 13, 23 at N = 360) | 0 |
| `Links.Count` (people, coin and goods in transit) | 1 | 0 |
| Summaries 71, 72, …: `PriceIdx[g]` | 874, 987, 992, 965, … | 1000 |
| Summaries 71, 72, …: `CoverX10`, `StockG`, `Pop`, `Households`, `Vacancies`, `CoinTotal`, `GrainCapacityNet`, `DwellingCap` | seeded and ticked | re-seeded |
| Summaries 71, 72, …: `PriceDay` | 15000 | 0 |

The first divergence in the home records comes at the next midnight. For N = 48 that is hour 360,048, the first hour after the load:

- `economy.PurseBalance[3, 7, 19, 20, …]` are each 1,750 mites lower in the resumed run. Twelve purses differ: six caravan accounts (purses 6125 to 6149 are the caravan accounts) and six crew households.
- `economy.FlowCoin[Wage]` is 1,952,123 straight and 1,941,623 resumed. `FlowCount[Wage]` is 10,469 straight and 10,463 resumed. `Sequence` is 110,539 straight and 110,533 resumed.
- That is six fewer daily caravan wages (`CaravanStep.PayWages`, `src/Ravensguard.Sim/Caravans/CaravanStep.cs:105-115`). The caravans that would pay them are gone.

From there the drift spreads: `markets.RefMicro` (292 of 8,632 entries by N = 360 + 1), then production, then the person and household tables, and at the season tick farming as well. For N = 13 the economy diverges at the same midnight. Until then only the continent differs.

The neighbour case, with the continent carried by hand, leaves exactly one difference: `region.125.Farming.SeedDebt` has length 3195 straight and 3194 resumed. All elements both runs share are equal, and the region's `RecordsHash` is equal.

## Root causes

### 1. The continent is never saved (main cause)
- `src/Ravensguard.Sim/Save/WorldSave.cs:359-389`: `WorldSave.Snapshot` builds the `LiveCapture` without `ContinentSt`.
- The rest of the path exists and works:
  - The DTO property: `WorldSave.cs:717`.
  - `Encode` writes the `continent` section when the property is set: `WorldSave.cs:776`.
  - `Restore` reads the section: `WorldSave.cs:521`.
  - The runtime attaches it: `src/Ravensguard.Sim/Worlds/WorldRuntime.cs:96-98`.
- The property was added in `766453c` ("caravans, continent tick and taxes in progress") and has never been assigned. `git log -S` finds no commit that ever set `ContinentSt =`.
- `WorldRuntime.cs:90-95` hides the gap. When a loaded state has no `ContinentState`, the runtime makes a fresh one with `ContinentState.Create` from the loaded head count and coin. This fallback was meant for saves from before the continent tick. It also differs from a new game's continent in three ways:
  - `RegionServices.EnsureContinent` (`src/Ravensguard.Sim/Worlds/RegionWorld.cs:51-63`) runs `ContinentTick.Run` once at creation, which sets `LastSeason` and the price indexes. The fallback never does.
  - The fallback leaves `LastSeason = -1`. `RegionScheduler.Update` (`src/Ravensguard.Sim/Abstract/RegionScheduler.cs:106-108`) therefore runs the current season's continent tick a second time on the first day 2 or later of a season after the load.
  - Every caravan in flight is lost, with its cargo and crew. The coin in its account purse stays stranded in the region ledger, because no caravan refers to that purse any more. The coin is not destroyed, so a coin audit still balances. Every `RegionLink` in transit is lost too: emigrants, pending arrivals and edge trade budgets.

**Proposed fix:**
- In `WorldSave.Snapshot`, add one line to the initializer:
  ```csharp
  ContinentSt = w.ContinentState == null ? null : ContinentSave.Capture(w.ContinentState),
  ```
  Snapshot already runs at the barrier on the tick worker (`AtBarrier`), and the scheduler mutates the continent only there, so the copy is consistent. The section is optional and the reader already handles it, so no format bump is needed.
- Change the fallback in `WorldRuntime.Init` so that a fresh continent for an old save is built the way a new game builds it (run `ContinentTick.Run` once, or set `LastSeason` to the current season). Then even an old save does not tick a season twice.
- Optionally, warn when a v7 save that has region or caravan state has no continent section.

The "carry the continent" experiment ran exactly this `ContinentSave.Capture` → `Restore` path and gave byte-identical results: 72 hours after N = 13, 48 and 360, and across N = 385 (season tick) and N = 1430 and 1440 (year boundary), on the worker thread as well.

### 2. `SeedDebtG` is not grown after a load
- `src/Ravensguard.Sim/Farming/FarmingSystem.cs:76`: the constructor sizes the working array `_hhHours` to the current household count.
- `src/Ravensguard.Sim/Farming/FarmingSystem.cs:281-285`: `SyncPopulation` grows the saved `State.SeedDebtG` only inside `if (_hhHours.Length < nh)`.
- `src/Ravensguard.Sim/Save/FarmingSave.cs:112-113`: the section restores `SeedDebtG` at its saved length.

Take a household founded after the day's `SyncPopulation`, which runs at hour 0 from `StepDaily` and at the start of the seasonal step, but before the save:
- In the straight run, `_hhHours` and `SeedDebtG` grow together at the next sync.
- In the resumed run, `_hhHours` already has the new length, so the guard is false. `SeedDebtG` keeps the old length for good.

The section bytes and `FarmingState.ComputeHash` differ. Worse, the new household's seed loan (`FarmingSystem.Seasonal.cs:322`) or repayment (`FarmingSystem.Seasonal.cs:398`) indexes past the end. That throws on the tick worker and faults the runtime. This affects the home region too; it was seen in neighbour 125 because a household was founded there between hour 0 and the save at N = 13.

**Proposed fix:** move the `SeedDebtG` growth out of the `_hhHours` guard in `SyncPopulation`:
```csharp
if (_hhHours.Length < nh) { Array.Resize(ref _hhHours, nh); Array.Resize(ref _isHolder, nh); }
if (State.SeedDebtG.Length < nh) { var sd = State.SeedDebtG; Array.Resize(ref sd, nh); State.SeedDebtG = sd; }
```
Do not grow it in the constructor instead: a capture taken right after a load must still equal the capture taken at the save, and the straight run grows the table only at the sync. It would be cleaner still to guard every `SeedDebtG[h]` access or to size the table from `Pop.Households.Count` at each use.

A related, harmless difference: the constructor builds `_householdsOf` including empty households (`FarmingSystem.cs:77-79`), but `SyncPopulation` excludes them (`:306-315`). Every reader runs after a sync, so this does not diverge today. Making the two agree would remove the trap.

### 3. Phase 4 wounds: the day cursor of an empty ledger (latent)
- `WorldSave.cs:405` writes the wound section only when the ledger is not empty.
- `WorldRuntime.cs:114` builds a missing ledger with `LastDay` taken from `state.Clock.Now`, which is the live clock at the save.
- In a straight run, the ledger's `LastDay` stays wherever its last `AdvanceTo` left it: the new game's clock, or the live tier's last step (`LiveWorld.cs:206`). `WoundSystem.Step` returns early on an empty ledger (`WoundSystem.cs:21`), so the abstract tick never moves it.
- `CaravanAmbush` (`src/Ravensguard.Sim/Caravans/CaravanAmbush.cs:59`) and `FightWorld` (`src/Ravensguard.Sim/Combat/Melee2/FightWorld.cs:256`) insert a new book with `Books[key] = book` and no `AdvanceTo` first. The next `WoundLedger.AdvanceTo` (`WoundLedger.cs:48-52`) then runs `DailyStep` for every day from the stale `LastDay + 1`, including days before the wound existed.

In isolation, the same muscle cut was inserted at day 15,020 into a ledger made at day 15,000 and into one made at day 15,019. One hour later the first had healed to a scar (`HealX10` 45 of 45). The second was open (10 of 45). The ledger hashes differ.

The 48-hour runs had no ambush, so this did not take part in the reported drift. A resumed game that later has an ambush or a fight would diverge, and even a straight run ages new wounds by days they never lived.

**Proposed fix:**
- Give each book its own day cursor: its birth day, set when it is made or inserted. `WoundLedger.AdvanceTo` then runs `DailyStep(d)` only for days after that cursor. This needs a new `WoundSave` field, with old saves defaulting to the ledger's `LastDay`.
- A smaller fix for the resume difference alone: always write the wound section when the ledger exists, so `LastDay` survives, and call `ledger.AdvanceTo(nowSec)` before the two direct inserts.

The other Phase 4 state in a no-hero world was checked:
- **Sites:** the `sites` section was present and identical in every carried run.
- **Captivity and succession:** not exercised, because they need a hero or captives. They are captured unconditionally once non-empty and were not examined further.

## Why CI did not catch it
- **`ResumeDeterminismTests`** (`tests/Ravensguard.Sim.Tests/Abstract/ResumeDeterminismTests.cs`) works one level down, on a `RegionWorld` and its `RegionSectionData`. Its restore, `RegionWorld.Restore` → `sv.EnsureContinent` (`RegionWorld.cs:201`), hands back the **same live `ContinentState` object** that the run being resumed used. The comment at line 53 says so ("the continent … is world state, saved with the world"), but the world never saves it. The test also steps regions with `RegionWorld.AdvanceTo` directly, with no scheduler, so the continent's season tick never runs. A continent that is never saved is invisible there.
- **The `WorldRuntimeTests` save → load → save test** (`tests/Ravensguard.Sim.Tests/RegionsC/WorldRuntimeTests.cs:98-115`) compares two saves byte for byte. A section that is never written is identical in both. It also never continues the loaded game against an uninterrupted one.
- **The `RuntimeAndSaveTests` worker-against-synchronous test** compares two uninterrupted runs, with no save.
- **`ContinentAndCaravanTests.ContinentSection_SavesLoadsAndSavesAgain_ByteForByte_WithACaravanInFlight`** round-trips the DTO by itself, not through `WorldSave`.
- **The `SeedDebtG` case** needs a household founded between the day's sync and the save, in a region that is then resumed. None of the existing N values hit that in the home region of `RegionRig`. Even if one had, `RecordsHash` does not include the farming state. Only the byte comparison of the farming section would have shown it, and that comparison is in the test.

The test below would have caught both causes. It fails at `1a10645` in all six cases, and it compares the continent directly rather than through the section, so a capture that is never written cannot hide. With fix 1 alone, the four synchronous cases and the worker case without neighbours are expected to pass, as shown by carrying the continent. The neighbour case also needs fix 2.

The test was deliberately not committed with the diagnosis (CI runs every category, so it would have turned red until the fixes landed). It is now committed with the fixes as `tests/Ravensguard.Sim.Tests/Abstract/WorldRuntimeResumeTests.cs` (see **Fixed** above; the committed copy adds a seventh case for the Phase 5 step 1 sections). It takes about 60 s for the six cases on the dev container.

## Regression test (to commit with the fixes)
```csharp
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// Save, load, continue at the level of a whole <see cref="WorldRuntime"/> (the plan's rule, section 6, one level above <see cref="ResumeDeterminismTests"/>): a new game runs N hours,
/// is saved with <see cref="WorldSave.Capture"/> at a barrier, goes through the codec, is loaded with <see cref="WorldSave.Restore"/> into a fresh runtime and runs M more hours. Every save
/// section, the abstract continent (compared directly, so a section that is never written cannot hide it) and every resident region's records must equal one uninterrupted run of N + M hours.
/// </summary>
public class WorldRuntimeResumeTests(ITestOutputHelper output)
{
    private const string Hash = "h";

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
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, null, null));
    }

    /// <summary>The ring-1 neighbours, built now and taken in (they are then stepped hourly, in parallel with home).</summary>
    private static void AddNeighbours(WorldRuntime rt)
    {
        rt.Manager.Synchronous = true;
        var home = rt.State.HomeRegion;
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1) }) { var rc = home.Offset(dx, dy); if (rc.IsValid) rt.Manager.GenerateNow(rc); }
        rt.Records.Sync();
    }

    /// <summary>Steps to <paramref name="to"/>: on the calling thread, or on the tick worker with barriers, pumped the way the street view pumps it.</summary>
    private static void Run(WorldRuntime rt, long to, bool worker)
    {
        if (!worker) { rt.AdvanceTo(GameTime.FromGameHours(to)); return; }
        rt.Start();
        var home = rt.State.HomeRegion; var t = GameTime.FromGameHours(to);
        while (rt.HourDone < to) { rt.Pump(t, home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(1); }
        rt.Quiesce();
        Assert.Null(rt.Fault);
    }

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        d["~continent"] = SaveCodec.SerializeCompressed(ContinentSave.Capture(rt.State.ContinentState!));
        foreach (var r in rt.Records.Resident) d[$"~records.{r.Coord.Id:D3}"] = System.Text.Encoding.ASCII.GetBytes(r.RecordsHash());
        return d;
    }).GetAwaiter().GetResult();

    [Theory]
    [InlineData(13, 48, false, false)]    // mid-day
    [InlineData(48, 48, false, false)]    // on a day boundary
    [InlineData(385, 48, false, false)]   // the continent's season tick (hour 0 of day 2 of summer) falls inside M
    [InlineData(1430, 48, false, false)]  // across the year boundary (a game year is 1,440 hours)
    [InlineData(48, 48, true, false)]     // on the tick worker with barriers
    [InlineData(13, 48, true, true)]      // the worker, five resident neighbours stepped in parallel
    public void SaveLoadContinue_OfAWholeRuntime_EqualsTheUninterruptedRun(int n, int m, bool worker, bool neighbours)
    {
        var (w, a) = Game();
        using var straight = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        if (neighbours) AddNeighbours(straight);
        long h0 = straight.HourDone;
        Run(straight, h0 + n, worker);
        var bytes = SaveCodec.Encode(straight.AtBarrier(() => WorldSave.Capture(straight.State, Hash, "0.1")).GetAwaiter().GetResult());
        Run(straight, h0 + n + m, worker);
        var want = Sections(straight);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, Hash, null, w.Continent, w.History);
        st.Content = w.Content;
        using var resumed = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        if (neighbours) resumed.Manager.Synchronous = true; // the saved neighbours page back in at the first barrier
        Run(resumed, h0 + n + m, worker);
        var got = Sections(resumed);

        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        output.WriteLine($"N {n} M {m} worker {worker} neighbours {neighbours}: {want.Count} sections, differing [{string.Join(", ", bad)}]");
        Assert.Empty(bad);
    }
}
```

A focused unit test for cause 3, to go with its fix, would sit next to `WoundTests` (collection `resolver2`, `V2Fixture`):
- Make two `WoundLedger`s, at day 15,000 and at day 15,019.
- Insert the same freshly cut book into each at day 15,020 with `Books[key] = book`.
- Call `AdvanceTo` one hour later on both.
- Assert that the two `Hash()` values are equal and the wound is still `Open`. This fails today.
