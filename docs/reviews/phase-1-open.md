# Phase 1: open issues found during the build

This list gets folded into the Phase 1 code and bug check (plan §6).

- **Generated towns look sparse:** the market square is a huge empty cobbled plaza, and the comb of parallel streets feels thin. The settlement look pass is planned.
- **Bridge decks:** they look wedge-shaped where roads converge.
- **Building styles:** chunk generation picks the building style by biome, not from `CultureDef`.
- **Snow overlay:** green strips show at hedge and road edges under snow.
- **Origins:** an origin's skills, kit and ambition are stored in the run info but not yet applied to a hero entity. That arrives with the hero record in Phase 3, or earlier if it's needed for Quick Start.
- **Legends viewer:** cause links haven't been exercised in a screenshot or test.
- **Settlements:** town cross lanes now jog at the main street and burgage plots have back lanes, but the parallel streets are still dead straight (no bends) and villages keep their single street. Farmsteads now have a fenced holding with a gate.
- **Possible save bug, check first in the Phase 1 review:** 12 `MsgPack017` warnings say that properties with an `init` accessor and an initializer are reset to the type default when deserialised. A saved field with a non-default value could silently reload as its default. The byte-identical round-trip test passes, possibly only because those fields hold their defaults in the test world. Fix by removing initializers or using constructors on the save DTOs, and add a round-trip test with non-default values.
- **Test style warnings:** 14 xUnit analyzer warnings (`Assert.True` where `Assert.Contains` is meant, and similar). Clean them up so new warnings stand out.
