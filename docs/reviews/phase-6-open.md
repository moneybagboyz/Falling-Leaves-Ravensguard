# Phase 6: open items for the close-out (P6-G)

Collected while the steps are built. The close-out fixes each item or moves it into `docs/reviews/phase-6.md` with a reason, then deletes this file.

## From step 1
- **A3 standing, hooks that later steps must wire:**
  - `Standing.PriceMites` is not called by the market book yet (RP-4 tests only the function). It needs a merchant-side rebate transfer to keep the ledger exact.
  - The readers for hiring, guards, courts, church and petitions exist but are not hooked in. They belong to B2/C1 (crime and justice), B4/E2 (faith) and C3 (rule).
  - Religion groups are not counted. B4 must supply membership through `IGroupMap`.
  - Nothing calls `StandingSystem.Moved` when a household changes settlement (RP-9).
  - A2 should fold `StandingBook.Hash()` into the daily hash trail.
  - Renown and `Heard` use the best single deed, not the union of everyone who heard any deed.
  - The hero is person 0, which equals `EntityId.None`'s value, so `DeedTable.DoerOf` returns a nullable. This same id clash has caused bugs before (Phase 4 S13); consider giving the hero a non-zero id in a later save step.
- **A4 difficulty, not wired yet:**
  - Bandit band spawns, slaver raids (D1) and the great plague (C4) must read `BanditPct` and the plague accessors when they are built.
  - Fight-only wound books, abstract battle books and resident regions other than the home economy keep the default 100.
  - The Advanced panel's difficulty rows were not rendered at 960x540.
  - For the close-out: run the 100-year population soak at aging pace 1, 2, 3 and 4 (only a one-year 4x check was run).
