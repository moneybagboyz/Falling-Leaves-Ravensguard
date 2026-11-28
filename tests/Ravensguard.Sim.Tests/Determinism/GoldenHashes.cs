using Ravensguard.Content;
using Ravensguard.Sim.WorldGen;
using Ravensguard.WorldTool;

namespace Ravensguard.Sim.Tests.Determinism;

/// <summary>
/// Pinned hashes of everything the generators make for seeds 1 and 1234: continent, history, embark pick, region, landmarks, weather, Codex, people,
/// four sample chunks, the continental road gates and a 2 x 2 block of region squares (seamless continent). They are the same lines the `hashes` tool prints (and CI compares between Linux and Windows).
///
/// If one of these fails you either broke determinism (a floating-point or ordering bug: fix the code) or changed world generation on purpose
/// (new rule, new content, a tuned setting). In the second case run
///
///     dotnet run --project tools/Ravensguard.WorldTool -c Release -- hashes --seeds 1,1234
///
/// and paste the printed lines for each seed into <see cref="Expected"/> below. Say in the commit that existing saves and worlds stop loading
/// (the world is regenerated from the seed and checked against the saved hashes), and bump the generator version if chunks changed.
/// </summary>
public class GoldenHashes
{
    // phase5_trail and phase6_trail re-pinned at P7-AE3 (both seeds; were E00B88F78BCCB91A / 17D04C5193FE18F6 and D2A2DC97BBDDA99F / 82CA4287A0B0A30F): content only. The new craft recipe forge_scrap
    // (10 kg of scrap_iron and 6 kg of charcoal into 8 kg of bar, recipes_munition.jsonc; AE-18) joins the recipe table the trail's crafting and price days read, so the days that follow it move. Reverting the recipe alone restores
    // both old values; every generated and history line is unchanged, and the warsupply section's new optional key (the arms book, absent while empty) moved nothing.
    // phase6_trail re-pinned at P7-AW-D (both seeds): the armies section carries three appended optional keys (17 count hosts, 18 next id, 19 ledger), so its saved bytes (a MessagePack
    // array of 20 where there were 17) differ; nothing generated and no game state moved (phase5_trail and every generated and history line are unchanged, ArmyHash too: the count book is absent while empty).
    // phase5_trail and phase6_trail re-pinned at P7-SG-E (both seeds): the content gained five vehicle parts, two vehicle templates (war wagon, war chariot) and five craft recipes, and the two trails run the
    // vehicle and craft content, so their hashes follow the catalogs. No generated or history line moved, and nothing in a save changed (v8, no new section or key).
    // landmarks re-pinned at P8-LM-1 (both seeds; were 29C6388C290BE1D1 / 7CDDE132D5D5FF67): new worlds use landmark placer v2 (2 to 5 a region, 3 km apart, 40 templates, draws on stream 0x820; landmark spec 3.1).
    // The old placer is its own pinned line, landmarks_v1, which holds the old values and never moves (a world saved before P8-LM-1 regenerates its landmarks with it). The codex line and the four block
    // lines move with it only because they hold the placed landmarks (a Codex page each; the block lines carry the landmarks hash). Region, people, history, continent, chunk and both trail lines are unchanged.
    private static readonly Dictionary<ulong, string[]> Expected = new()
    {
        [1] =
        [
        "seed 1 continent D5EEA79D51E94F1B6FC4265F1D6ED1A7ABD0BB5619621F3E08FA1A0B7292B944",
        "seed 1 history 57C88F0A21F5FBDDD38FA23607021879E9C902E4F5F03DD50511C7C90C96AA52",
        "seed 1 embark 128,96 score 931",
        "seed 1 region 58DA40004189BFD39AD3D841FA1DFA111832280D096C607FA0AD99F39334B443",
        "seed 1 landmarks 2 CBF093A18F855BDD35257E8EDBFD8F3BE9089148BAFA034720324CC47540D963",
        "seed 1 landmarks_v1 4 29C6388C290BE1D100AFAF0E1D27F0BE6A9E598A526B75DD17BB5DFFDAD74208",
        "seed 1 weather D277983325156D3E05192DD3FB5660B5E7ED0DA536D5D02C9B16425CC70C08F0",
        "seed 1 codex BE1E6B9884D0A38BCA446FA8EE9B13E3A746646227E88BFF10F514ACC935DCA5",
        "seed 1 people A5D02BE736F4FA6D87AE701202A472E1B9757F09EB49823BEE60504192ABFCC4",
        "seed 1 chunk 223,655 1C8647BC979C9D563B6F42AD8B99AFD4",
        "seed 1 chunk 100,100 99640E563D05BAEC4E396FA0083A38C8",
        "seed 1 chunk 500,500 EB4F5AE3D7DEE0CBCB3DA03656153DE0",
        "seed 1 chunk 900,300 7A5329B682D73457D4D7E7C9AB46FC7D",
        "seed 1 gates 297 3605F4389964D04725F6832E64DC26CA190CCFA94F1D6078A3EE1FAD6FE6D5DA",
        "seed 1 block R(8,6) region 58DA40004189BFD39AD3D841FA1DFA111832280D096C607FA0AD99F39334B443 landmarks 2 CBF093A18F855BDD35257E8EDBFD8F3BE9089148BAFA034720324CC47540D963 people A5D02BE736F4FA6D87AE701202A472E1B9757F09EB49823BEE60504192ABFCC4",
        "seed 1 block R(9,6) region D0A65481E7E01F6D01E35CABA33654E83D45EC1D3B7E6AD8F0DF6DB2339B15E1 landmarks 5 C2E2A169237B66A9AB633A6CE40BFD3160CAB013EA0DFABBA432AEC0049BE402 people BBDE2C6000F7FA2D2058BB256FD1180A8600B61D4F32E6CB8D415A26EA86AA42",
        "seed 1 block R(8,7) region 57DC24C144738C27DDEEA5DBE79BBF699F490A09792617B775DC4205122D1E47 landmarks 5 6FBABEB58919070D95F06412B446B87B983BACF6F1BA3F241D3CBEB596C6540E people 213ED33D4506A8C3D4F1D4973894804CF284EE26FF1695F92671CCF1C8A4C64F",
        "seed 1 block R(9,7) region FFD5935A11F52DE6FDC9F0EB416A995F94D27C84D22257BC36E80AABBC3CE122 landmarks 3 D842120B1BDC6E42E0BC674AF7F98BA9306A686A919C8ECCF56FD370D332DF44 people EFDD99C79D681F65DB41C2C6DD0D66311FC19DC21E47C7786E3ECE142C430B00",
        "seed 1 fight 1001F8BDF105658D active 7",
        "seed 1 captive 2BF23152278072D8 estate 20F5B004168F4EF4",
        "seed 1 vehicles 57BDDD5370F59C1F",
        "seed 1 phase5_trail 9358DF331A267C3F",       // P8-LM-2 (was 07BA01F25700F032): the unique NPCs join a new game (each a person row with a household of its own, so the people, the ownership and the chronicle the trail hashes differ), and the trail now runs on a copy of the region as generated (Population.CloneOnGeneratedRegion): a trail no longer meets the lots the trail before it grew into the shared region, which a marriage vacancy search over a changed matching exposed (an index past the per-lot arrays); every generated and history line is unchanged; then P8-LM-3 (was 586CFD04DA17B127): four monster plans, their materials and eight site templates join the content, and lair states join the site book; then P8-LW-4 (was BD1B560EDB67AA49): content (the fire_arrow ammo, four ship parts with recipes) and the running sea (a coastal village's fishing boat and sailors, ferries at wide fords and barges on shuttle voyages make real vehicles, fish and fares) and soldiers who drill gain Strength and Toughness; no generated or history line moved
        "seed 1 phase6_trail 6C79AAEAFC7B1045",       // P8-LM-2 (was 5E0AA673F30D4909): the unique NPCs join a new game (each a person row with a household of its own, so the people, the ownership and the chronicle the trail hashes differ), and the trail now runs on a copy of the region as generated (Population.CloneOnGeneratedRegion): a trail no longer meets the lots the trail before it grew into the shared region, which a marriage vacancy search over a changed matching exposed (an index past the per-lot arrays); every generated and history line is unchanged; then P8-LM-3 (was B0220EB1B9C38791): four monster plans, their materials and eight site templates join the content, and lair states join the site book; then P8-PF-2 + P8-BL-1 re-pin (PF-2 alone 5711C10C93AAFB57 / 368E8E5630FECA71): the people section gains optional key 47 (dropped dead rows); the households' own hands do the soil work of their fields in idle hours (D13). PF-2: 
        // P6-F (history v1, HR-10): a new world is made with the Phase 6 history rules; its own pinned lines, added after the frozen v0 lines above (which did not move). Seeds 7 and 42 ride on seed 1 so CI compares three seeds.
        "seed 1 history_v1 46690A566E512EC4FC1D99E8CE0961BA4164AA58C8E222C9808783F0232FA8B2",
        "seed 7 history_v1 36D906FA56393D75832B7797F357BFCC37AA08E8FF257070504F7F70CDE796DC",
        "seed 42 history_v1 792B714AA1603F294A3ADC86817894CF960F96550D3DD32C6A450AE9231EB7EC",
        ],
        [1234] =
        [
        "seed 1234 continent ABEA0BC4D62AA53DB1B0ED57333D00E041281C0EC38D168C9F37702580B52326",
        "seed 1234 history D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9",
        "seed 1234 embark 208,128 score 993",
        "seed 1234 region 0B578B53359028747E77BA81567410184B8D79F492B52AFF7C476FED3A45E978",
        "seed 1234 landmarks 3 881044E6C348D253D611649C718121DA0D6524A5D41ED7F44A195DB561AD7550",
        "seed 1234 landmarks_v1 4 7CDDE132D5D5FF67F6606C15F9F5BF10DF47E0289884340759627F311937EF32",
        "seed 1234 weather B221DC56D4D2A37D4E4B1021441760F12D99417360812465DE461A56769A5D3F",
        "seed 1234 codex CDD23617C7FE05096161C5BDA1FE409AB07DA5B18A563D8F5EC1990B85732AB9",
        "seed 1234 people 581E4F035CDA1F384F95EBB204A42F81B69AE8BF081A04042197EB26BA08B643",
        "seed 1234 chunk 531,653 DF4B085F3E2F499A98C10DE583F1B127",
        "seed 1234 chunk 100,100 79889339BE01CF0A01F4D0FDD5F8ED87",
        "seed 1234 chunk 500,500 43B81C622C35BB74DA83C3353D71CF82",
        "seed 1234 chunk 900,300 30727D3D1ECABAA1BE2E412E3EEEEBBE",
        "seed 1234 gates 409 18E56F16887D6FE69F4BD7A04711E53B508AACAFEE3F33F160CEE3CF9038A12F",
        "seed 1234 block R(13,8) region 0B578B53359028747E77BA81567410184B8D79F492B52AFF7C476FED3A45E978 landmarks 3 881044E6C348D253D611649C718121DA0D6524A5D41ED7F44A195DB561AD7550 people 581E4F035CDA1F384F95EBB204A42F81B69AE8BF081A04042197EB26BA08B643",
        "seed 1234 block R(14,8) region 62528FD6165AC9EFA690F88DC6EFA62C47FB4C198573B0E4958E2A5D8B9B1696 landmarks 2 B43405A93967A1C916980EE0766627A34B435021A23E5626E73A4BEFCBDC9256 people B22BE80E55F9CAF51702B31C6B44CCADA7D218043E0441425933DC18E3405ACA",
        "seed 1234 block R(13,9) region FF9DFE462D0D8F48490BD35C407E35AABD5447801855F76DF1AA13D4DE7313EA landmarks 3 FA97E48572364B7AEEB55A2B7EE698911F0D530C879FB7202107BDE910BF8FA5 people D5249ED385A3392ED000F5980A065E9F556329BB23A51E62AA659613D2495BD9",
        "seed 1234 block R(14,9) region D2051676FDF8CE71302EB500D368655146DC77F1F50431FFA0076F7D6EE3E810 landmarks 3 5BABE002884B2503FA159888C1EABA4C834D66A12E7984A0CE2B316433A86EFC people 85BE62E38A7556DC0E1A88F6312DB8BCE4FEDE2C2A3B5834391B801963F4CE3B",
        "seed 1234 fight 8BAC3738BE098914 active 7",
        "seed 1234 captive 05B4A3623841628E estate AE4BCB2FFDF537FD",
        "seed 1234 vehicles 1BDD9643F168AD56",
        "seed 1234 phase5_trail 11C27E324C666C26",       // P8-LM-2 (was 985B05BBDE17A2C3): the unique NPCs join a new game (each a person row with a household of its own, so the people, the ownership and the chronicle the trail hashes differ), and the trail now runs on a copy of the region as generated (Population.CloneOnGeneratedRegion): a trail no longer meets the lots the trail before it grew into the shared region, which a marriage vacancy search over a changed matching exposed (an index past the per-lot arrays); every generated and history line is unchanged; then P8-LM-3 (was 9399CBF29D0D3EA5): four monster plans, their materials and eight site templates join the content, and lair states join the site book; then P8-LW-4 (was 58D9CA77051B212F): content (the fire_arrow ammo, four ship parts with recipes) and the running sea (a coastal village's fishing boat and sailors, ferries at wide fords and barges on shuttle voyages make real vehicles, fish and fares) and soldiers who drill gain Strength and Toughness; no generated or history line moved
        "seed 1234 phase6_trail 1B21F564D7A796A8",       // P8-LM-2 (was 7CB765BDE73AF1FC): the unique NPCs join a new game (each a person row with a household of its own, so the people, the ownership and the chronicle the trail hashes differ), and the trail now runs on a copy of the region as generated (Population.CloneOnGeneratedRegion): a trail no longer meets the lots the trail before it grew into the shared region, which a marriage vacancy search over a changed matching exposed (an index past the per-lot arrays); every generated and history line is unchanged; then P8-LM-3 (was 4F9C8DA572571592): four monster plans, their materials and eight site templates join the content, and lair states join the site book; then P8-PF-2 + P8-BL-1 re-pin (PF-2 alone 5711C10C93AAFB57 / 368E8E5630FECA71): the people section gains optional key 47 (dropped dead rows); the households' own hands do the soil work of their fields in idle hours (D13). PF-2: 
        "seed 1234 history_v1 8CF81C29ACB043EC9A3983F884C4AFD4A09E4025FB9B0A6AA4D0738B79866BC2",     // P6-F: see seed 1
        ],
    };

    public static IEnumerable<object[]> Seeds => Expected.Keys.Select(s => new object[] { s });

    [Theory, MemberData(nameof(Seeds))]
    public void Generated_layers_match_the_pinned_hashes(ulong seed)
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var db = new ModLoader(ContentTypeRegistry.Default).Load([dir]).Database;
        var actual = HashesCommand.Lines(seed, dir, db);
        var expected = Expected[seed];
        var diff = actual.Zip(expected, (a, e) => (a, e)).Where(p => p.a != p.e).ToList();
        string how = "\nWorld generation changed. If that is intended, run\n  dotnet run --project tools/Ravensguard.WorldTool -c Release -- hashes --seeds 1,1234\n"
            + "and paste the lines for this seed into the Expected table in tests/Ravensguard.Sim.Tests/Determinism/GoldenHashes.cs (see the comment above it). "
            + "If it is not intended, a generator lost determinism: find what changed.\n";
        Assert.True(diff.Count == 0 && actual.Count == expected.Length,
            how + "First differences:\n" + string.Join("\n", diff.Take(4).Select(p => $"  expected {p.e}\n  actual   {p.a}")) + "\nAll lines now:\n" + string.Join("\n", actual));
    }
}
