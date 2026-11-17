# Phase 1: open issues found during the build

This list gets folded into the Phase 1 code and bug check (plan §6).

- **Text-server RIDs leaked at exit:** the headless smoke boot reports `4 ShapedTextDataAdvanced` and `1 FontAdvanced` RIDs leaked at exit. This appeared once the new-game screens existed, and freeing all screens before quitting doesn't clear it. It is probably a font or shaped text held by managed code past shutdown. CI treats this one message as a warning. Find the holder and remove the CI exception.
- **Generated towns look sparse:** the market square is a huge empty cobbled plaza, and the comb of parallel streets feels thin. The settlement look pass is planned.
- **Bridge decks:** they look wedge-shaped where roads converge.
- **Building styles:** chunk generation picks the building style by biome, not from `CultureDef`.
- **Snow overlay:** green strips show at hedge and road edges under snow.
- **Origins:** an origin's skills, kit and ambition are stored in the run info but not yet applied to a hero entity. That arrives with the hero record in Phase 3, or earlier if it's needed for Quick Start.
- **Legends viewer:** cause links haven't been exercised in a screenshot or test.
