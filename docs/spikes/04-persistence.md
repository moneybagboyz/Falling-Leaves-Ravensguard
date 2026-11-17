# Spike 4: persistence across content change

Test: `tests/Ravensguard.Sim.Tests/Spikes/PersistenceSpike.cs`. Uses the real `ChunkStore` / `ChunkSnapshot` / `ChunkDelta` / `SaveCodec`; only the generator and "mod" are fakes (a weighted tree-type list, 64 candidate slots per chunk, ids derived from seed + chunk + slot via `DetRng`).

## What was exercised
1. Generate chunk A from content v1 (oak, birch) and record the snapshot on first visit (25 objects, 519 B compressed).
2. Apply a delta by stable id: remove one tree, add a wall object, set a tile (3 ops). Save with `SaveCodec.Encode` (774 B for the one-chunk save).
3. Change content (add `pine` weight 20) and bump generator version 1 to 2; reload the save.
4. Results:
   - Visited chunk A is **byte-identical** to its pre-change state; removed tree still gone, wall present; its snapshot still reports generator 1 and the v1 content hash.
   - Control: regenerating A under the new content would have produced pines (the test proves the change is real).
   - Unvisited chunk B generates with the new content (19 of 26 trees are pine) and is stamped generator 2 and the v2 hash.
   - Save, load, save is byte-identical.
   - A delta op addressed to an already-removed id is ignored after reload.

## Observations
- Because ids come from (seed, chunk, slot) and not from content, all 26 object ids in B were identical between v1 and v2 generation. Unvisited chunks keep stable identity even as their appearance changes, which keeps ID-keyed deltas written against future-generated chunks meaningful.
- Costs are small: ~0.5 KB per snapshot for a 25-object chunk; the plan's 2-6 KB for real chunks is plausible.

## Decision: GO
The snapshot-on-first-visit plus ID-keyed delta design works as specified. Rules to carry into Phase 1:
- Generator object ids must be a pure function of (seed, chunk, slot/feature index), never of content or iteration order.
- Never rebuild a visited chunk from the generator; only `Rebuild` = snapshot + delta.
- Keep generator version and content hash on every snapshot (already in `ChunkSnapshot`); a later "reconcile" tool may use them to offer opt-in upgrades, never automatic ones.
- A mod that removes a content id referenced by a snapshot needs a placeholder/unknown-def path at load time (not covered by this spike; objects here store type ids as data).
