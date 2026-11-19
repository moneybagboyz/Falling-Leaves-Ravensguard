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
        "seed 1 region 58DA40004189BFD39AD3D841FA1DFA111832280D096C607FA0AD99F39334B443",
        "seed 1 landmarks 4 29C6388C290BE1D100AFAF0E1D27F0BE6A9E598A526B75DD17BB5DFFDAD74208",
        "seed 1 weather D277983325156D3E05192DD3FB5660B5E7ED0DA536D5D02C9B16425CC70C08F0",
        "seed 1 codex E9B3676EF37F126B1AD7E15049C35CDE9F674605E13CD1ED9B512DBD0E741335",
        "seed 1 people A5D02BE736F4FA6D87AE701202A472E1B9757F09EB49823BEE60504192ABFCC4",
        "seed 1 chunk 223,655 1C8647BC979C9D563B6F42AD8B99AFD4",
        "seed 1 chunk 100,100 99640E563D05BAEC4E396FA0083A38C8",
        "seed 1 chunk 500,500 EB4F5AE3D7DEE0CBCB3DA03656153DE0",
        "seed 1 chunk 900,300 7A5329B682D73457D4D7E7C9AB46FC7D",
        "seed 1 gates 297 3605F4389964D04725F6832E64DC26CA190CCFA94F1D6078A3EE1FAD6FE6D5DA",
        "seed 1 block R(8,6) region 58DA40004189BFD39AD3D841FA1DFA111832280D096C607FA0AD99F39334B443 landmarks 4 29C6388C290BE1D100AFAF0E1D27F0BE6A9E598A526B75DD17BB5DFFDAD74208 people A5D02BE736F4FA6D87AE701202A472E1B9757F09EB49823BEE60504192ABFCC4",
        "seed 1 block R(9,6) region D0A65481E7E01F6D01E35CABA33654E83D45EC1D3B7E6AD8F0DF6DB2339B15E1 landmarks 4 1A983651BD1DD62F26ABE86BE40017F2F6E146CA1573425733D28F0F8EC05402 people BBDE2C6000F7FA2D2058BB256FD1180A8600B61D4F32E6CB8D415A26EA86AA42",
        "seed 1 block R(8,7) region 57DC24C144738C27DDEEA5DBE79BBF699F490A09792617B775DC4205122D1E47 landmarks 4 A03C32AA22B7BB0AD6392967F5A516769B00D16304B9C12719A40721A3BB3074 people 213ED33D4506A8C3D4F1D4973894804CF284EE26FF1695F92671CCF1C8A4C64F",
        "seed 1 block R(9,7) region FFD5935A11F52DE6FDC9F0EB416A995F94D27C84D22257BC36E80AABBC3CE122 landmarks 4 CBFA560CE0C90B432A05FBB09435A79B673803F915948DAE3597BFDED63A8133 people EFDD99C79D681F65DB41C2C6DD0D66311FC19DC21E47C7786E3ECE142C430B00",
        "seed 1 fight 1001F8BDF105658D active 7",
        "seed 1 captive 2BF23152278072D8 estate 20F5B004168F4EF4",
        ],
        [1234] =
        [
        "seed 1234 continent ABEA0BC4D62AA53DB1B0ED57333D00E041281C0EC38D168C9F37702580B52326",
        "seed 1234 history D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9",
        "seed 1234 embark 208,128 score 993",
        "seed 1234 region 0B578B53359028747E77BA81567410184B8D79F492B52AFF7C476FED3A45E978",
        "seed 1234 landmarks 4 7CDDE132D5D5FF67F6606C15F9F5BF10DF47E0289884340759627F311937EF32",
        "seed 1234 weather B221DC56D4D2A37D4E4B1021441760F12D99417360812465DE461A56769A5D3F",
        "seed 1234 codex F6F80AB24461515C48D734CCA8629D7DC55DBC2D6DF635736E759C5D6E933CE0",
        "seed 1234 people 581E4F035CDA1F384F95EBB204A42F81B69AE8BF081A04042197EB26BA08B643",
        "seed 1234 chunk 531,653 DF4B085F3E2F499A98C10DE583F1B127",
        "seed 1234 chunk 100,100 79889339BE01CF0A01F4D0FDD5F8ED87",
        "seed 1234 chunk 500,500 43B81C622C35BB74DA83C3353D71CF82",
        "seed 1234 chunk 900,300 30727D3D1ECABAA1BE2E412E3EEEEBBE",
        "seed 1234 gates 409 18E56F16887D6FE69F4BD7A04711E53B508AACAFEE3F33F160CEE3CF9038A12F",
        "seed 1234 block R(13,8) region 0B578B53359028747E77BA81567410184B8D79F492B52AFF7C476FED3A45E978 landmarks 4 7CDDE132D5D5FF67F6606C15F9F5BF10DF47E0289884340759627F311937EF32 people 581E4F035CDA1F384F95EBB204A42F81B69AE8BF081A04042197EB26BA08B643",
        "seed 1234 block R(14,8) region 62528FD6165AC9EFA690F88DC6EFA62C47FB4C198573B0E4958E2A5D8B9B1696 landmarks 4 7DC65D2F52917AD983C23D7353346CA5688B5A5C3BE73E0AA564645663B1527E people B22BE80E55F9CAF51702B31C6B44CCADA7D218043E0441425933DC18E3405ACA",
        "seed 1234 block R(13,9) region FF9DFE462D0D8F48490BD35C407E35AABD5447801855F76DF1AA13D4DE7313EA landmarks 4 16C84CE2957578CC02E1A73B91E4BD3B44A8A75C57622193776D79BFB204F1C3 people D5249ED385A3392ED000F5980A065E9F556329BB23A51E62AA659613D2495BD9",
        "seed 1234 block R(14,9) region D2051676FDF8CE71302EB500D368655146DC77F1F50431FFA0076F7D6EE3E810 landmarks 2 E07C27564381A04B2A17AE1011487020874066533A56A0B0BAACA0BC2B510FA8 people 85BE62E38A7556DC0E1A88F6312DB8BCE4FEDE2C2A3B5834391B801963F4CE3B",
        "seed 1234 fight 8BAC3738BE098914 active 7",
        "seed 1234 captive 05B4A3623841628E estate AE4BCB2FFDF537FD",
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
