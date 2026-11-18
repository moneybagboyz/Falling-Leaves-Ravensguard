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
        "seed 1 region C2C53D3617AB995EC2510EFA41D1A8433D865A609946380BBE4AE5164A3D81B5",
        "seed 1 landmarks 4 5B6024F8A3496EA30364E053BD2F9ADAF0C2BCD6D8AFA51E734024BB178937C5",
        "seed 1 weather D277983325156D3E05192DD3FB5660B5E7ED0DA536D5D02C9B16425CC70C08F0",
        "seed 1 codex 4C1E4244338B4134DFFA9EF5880129C3892490DB6C01141C6F2519900B61913C",
        "seed 1 people 9C5BEA9F75B1D6BC9E1ED11259D5A1B08501E2AE0294F2860A43A5EFC3762CFE",
        "seed 1 chunk 223,655 1C8647BC979C9D563B6F42AD8B99AFD4",
        "seed 1 chunk 100,100 99640E563D05BAEC4E396FA0083A38C8",
        "seed 1 chunk 500,500 ADF8E9443120668705809D8875176E11",
        "seed 1 chunk 900,300 7A5329B682D73457D4D7E7C9AB46FC7D",
        "seed 1 gates 297 3605F4389964D04725F6832E64DC26CA190CCFA94F1D6078A3EE1FAD6FE6D5DA",
        "seed 1 block R(8,6) region C2C53D3617AB995EC2510EFA41D1A8433D865A609946380BBE4AE5164A3D81B5 landmarks 4 5B6024F8A3496EA30364E053BD2F9ADAF0C2BCD6D8AFA51E734024BB178937C5 people 9C5BEA9F75B1D6BC9E1ED11259D5A1B08501E2AE0294F2860A43A5EFC3762CFE",
        "seed 1 block R(9,6) region 98B3AE041AF89DC85687EB21335E1495D515E9FCE08AC5D813142DAB3F36945A landmarks 4 8D7C09457432B3F0261899941314608B767DA0658E101A40F64F18A44A9B6048 people 8439B0BB4BDD3FEC0E33A56622C4F5C4B5067928A9333F82EC53504337348832",
        "seed 1 block R(8,7) region C0DF89F7B5C6B30869C9508CC7AAC8557C82EFA5E90002CE7B53A0896D45DD1D landmarks 4 8CFD800F363BED72E3D1916D50C811563090B3ED811803FDF2E5C96139DEEE9B people 9795EF971E27097C42DA735DDFA71230103884BC97B0F74013D47F447ACE3513",
        "seed 1 block R(9,7) region 849D72C9CA5FC7A1C1E0008A65376923A0DEBDA6026CC63D0CA0B0904598782E landmarks 4 992670E7A2454A706C28BC49DFD14F568D7C33FFE0E168FAC5A78E21586C0E9B people 59548BEFE41DBF1846EF5368C82D26B4C7688E7B66634FAE1F7C29577E9EEAF1",
        ],
        [1234] =
        [
        "seed 1234 continent ABEA0BC4D62AA53DB1B0ED57333D00E041281C0EC38D168C9F37702580B52326",
        "seed 1234 history D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9",
        "seed 1234 embark 208,128 score 993",
        "seed 1234 region 8415BE9C1082FEC3703A41F1853FE6D39CFB17280FAA10A6801C4D7927CF3A00",
        "seed 1234 landmarks 4 0EB8464327918636445BC139C87B42C2CC53E47AB886C5F9A3A986EF8B616687",
        "seed 1234 weather B221DC56D4D2A37D4E4B1021441760F12D99417360812465DE461A56769A5D3F",
        "seed 1234 codex A06B789A9B7D43E337C2912A7DFED664984EA0B1D896ADB11D2115DB55782990",
        "seed 1234 people 73988181CDFDB5D4EE85A847141ED577B6214B2CF7DB56016888988ABDDEDF6A",
        "seed 1234 chunk 531,653 DF4B085F3E2F499A98C10DE583F1B127",
        "seed 1234 chunk 100,100 79889339BE01CF0A01F4D0FDD5F8ED87",
        "seed 1234 chunk 500,500 43B81C622C35BB74DA83C3353D71CF82",
        "seed 1234 chunk 900,300 30727D3D1ECABAA1BE2E412E3EEEEBBE",
        "seed 1234 gates 409 18E56F16887D6FE69F4BD7A04711E53B508AACAFEE3F33F160CEE3CF9038A12F",
        "seed 1234 block R(13,8) region 8415BE9C1082FEC3703A41F1853FE6D39CFB17280FAA10A6801C4D7927CF3A00 landmarks 4 0EB8464327918636445BC139C87B42C2CC53E47AB886C5F9A3A986EF8B616687 people 73988181CDFDB5D4EE85A847141ED577B6214B2CF7DB56016888988ABDDEDF6A",
        "seed 1234 block R(14,8) region 3425A0192F571616ED22B0E7E52C3756B084A20888354F5616CF45CD557570E9 landmarks 4 004CCCA87402388ECEDF3C6E80D8338E494AEA4DA68D46084FF3339234863B66 people 755AB75DCF6C90FE034D71F3DBB2A5939BA883D8802107B6E03F6ADB32584E43",
        "seed 1234 block R(13,9) region 4C0747B2631419263CAAFADAF0EBEA1A1E2EB1CCFD1C606C4903054037EBE880 landmarks 4 DE62D0B48A40333C88B961B04825461C422B5AC45DD478768FA7A8B39640630D people 9341B98ED294713489373D8BB224F117C261E2E84ECF0AFBBC0C832D10B4888E",
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
