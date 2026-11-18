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
    private static readonly Dictionary<ulong, string[]> Expected = new()
    {
        [1] =
        [
        "seed 1 continent D5EEA79D51E94F1B6FC4265F1D6ED1A7ABD0BB5619621F3E08FA1A0B7292B944",
        "seed 1 history 57C88F0A21F5FBDDD38FA23607021879E9C902E4F5F03DD50511C7C90C96AA52",
        "seed 1 embark 128,96 score 931",
        "seed 1 region C3DCCE55D04543D2B6B7A4099CA1668DB15E187A807B9504E281DD0FAD1F970B",
        "seed 1 landmarks 4 083D553F43A151A96738004C194BA4CB20A4EC1960F5367D34567613F8B7797D",
        "seed 1 weather D277983325156D3E05192DD3FB5660B5E7ED0DA536D5D02C9B16425CC70C08F0",
        "seed 1 codex 125B11FAC8577D03E5500A85920F783F29E360B8894C23FF666B42FA15FCD9DF",
        "seed 1 people 54CC5943B192A7B32A299C5B73EAE0B357D20F2F66D3ABB6E614409F624897F4",
        "seed 1 chunk 223,655 1C8647BC979C9D563B6F42AD8B99AFD4",
        "seed 1 chunk 100,100 99640E563D05BAEC4E396FA0083A38C8",
        "seed 1 chunk 500,500 ADF8E9443120668705809D8875176E11",
        "seed 1 chunk 900,300 7A5329B682D73457D4D7E7C9AB46FC7D",
        "seed 1 gates 297 3605F4389964D04725F6832E64DC26CA190CCFA94F1D6078A3EE1FAD6FE6D5DA",
        "seed 1 block R(8,6) region C3DCCE55D04543D2B6B7A4099CA1668DB15E187A807B9504E281DD0FAD1F970B landmarks 4 083D553F43A151A96738004C194BA4CB20A4EC1960F5367D34567613F8B7797D people 54CC5943B192A7B32A299C5B73EAE0B357D20F2F66D3ABB6E614409F624897F4",
        "seed 1 block R(9,6) region A698D33F020EA2CB16B3CC2D1B442B4C081D74BBB8292DE19930947F4B6DE061 landmarks 4 8D7C09457432B3F0261899941314608B767DA0658E101A40F64F18A44A9B6048 people 8439B0BB4BDD3FEC0E33A56622C4F5C4B5067928A9333F82EC53504337348832",
        "seed 1 block R(8,7) region 859B83F82B0916CBB0EFF11A37AF91FBE51E66F311977C0100ED1F007209BCCC landmarks 4 17A23387C0DE4339B519CC7E82486B12784D574864A8AE29E282CEE6555D4000 people 9F6CF812C620D823C9F6653840EAADDED03A215E842297E2D2FE67675F38B37D",
        "seed 1 block R(9,7) region 3506BA753331D5093C7946F7E394D71758ECD183A682B03D8F14AD83CE0CCE54 landmarks 4 992670E7A2454A706C28BC49DFD14F568D7C33FFE0E168FAC5A78E21586C0E9B people DB1E6EA3ECAFA469CACC384F52F9BF0B7DC7650857996C9DD618F248B66B6EB7",
        ],
        [1234] =
        [
        "seed 1234 continent ABEA0BC4D62AA53DB1B0ED57333D00E041281C0EC38D168C9F37702580B52326",
        "seed 1234 history D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9",
        "seed 1234 embark 208,128 score 993",
        "seed 1234 region 8ED6EFFE49C3A56BE84688F16BEC5E563E0E66C8C40D246C887B82EB26D1DFBD",
        "seed 1234 landmarks 4 2A122A16746796B316373DA9EDCA7B8EB9D26773939E21D77E458B30747CC053",
        "seed 1234 weather B221DC56D4D2A37D4E4B1021441760F12D99417360812465DE461A56769A5D3F",
        "seed 1234 codex F2BE18F278C55162B95A35E3545198D45061468FAAB17C54B6B7D6901B5CA474",
        "seed 1234 people B7D229696F90057F82AE471C2D546D7DEA07CD04FD93A0ED5A2E79A2D0E6F204",
        "seed 1234 chunk 531,653 DF4B085F3E2F499A98C10DE583F1B127",
        "seed 1234 chunk 100,100 79889339BE01CF0A01F4D0FDD5F8ED87",
        "seed 1234 chunk 500,500 43B81C622C35BB74DA83C3353D71CF82",
        "seed 1234 chunk 900,300 30727D3D1ECABAA1BE2E412E3EEEEBBE",
        "seed 1234 gates 409 18E56F16887D6FE69F4BD7A04711E53B508AACAFEE3F33F160CEE3CF9038A12F",
        "seed 1234 block R(13,8) region 8ED6EFFE49C3A56BE84688F16BEC5E563E0E66C8C40D246C887B82EB26D1DFBD landmarks 4 2A122A16746796B316373DA9EDCA7B8EB9D26773939E21D77E458B30747CC053 people B7D229696F90057F82AE471C2D546D7DEA07CD04FD93A0ED5A2E79A2D0E6F204",
        "seed 1234 block R(14,8) region 24F6037CF4F841859BA30E98043F5A3F72B9A3A3172B62D58A1AB6A2A6E941B8 landmarks 4 004CCCA87402388ECEDF3C6E80D8338E494AEA4DA68D46084FF3339234863B66 people 755AB75DCF6C90FE034D71F3DBB2A5939BA883D8802107B6E03F6ADB32584E43",
        "seed 1234 block R(13,9) region 2503A84717A0CD25CBA18340B67A74E0CA5F145A5CAC0EB29AC3843D7FFFA240 landmarks 4 8179FE796793D49ABE674802E6029E9FDED0F360945CDD25CE9447BFECD9E129 people 8DA1A2E6916FDC829B0043B99B00193CDA8F455A402B193C7E3AFC2763B06168",
        "seed 1234 block R(14,9) region BDD0F12C6CA528657C8D6090D70D37622F28F60CBA9641E3D4BE16A48101D76F landmarks 2 0A7EEDC4AC1FD8B60995854EBD45C790BEDF1C9E4F4945D1CE88B4DD7367A29D people D1F5FAF2F1EB4A7ABD85FE2B1D02CED3E547060818F4B88B9E5E0077E2AE34DD",
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
