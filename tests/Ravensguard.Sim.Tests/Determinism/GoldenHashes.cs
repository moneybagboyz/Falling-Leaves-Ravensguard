using Ravensguard.Content;
using Ravensguard.Sim.WorldGen;
using Ravensguard.WorldTool;

namespace Ravensguard.Sim.Tests.Determinism;

/// <summary>
/// Pinned hashes of everything the generators make for seeds 1 and 1234: continent, history, embark pick, region, landmarks, weather, Codex, people and
/// four sample chunks. They are the same lines the `hashes` tool prints (and CI compares between Linux and Windows).
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
        "seed 1 embark 114,212 score 1115",
        "seed 1 region 98F627C161A958FEA0C8EE0547FA41ED6FA67EDA5BDD164231E2C2A523CB9E62",
        "seed 1 landmarks 4 87759CDE47AB94C194A1FA8724A6AB5AA01368D343578287C22612FB28DA15EA",
        "seed 1 weather 6333585694B064D9E5ED07C6ED37183772FA09BCF1A8C419BD064A030876DA8B",
        "seed 1 codex F976535D5587EF1113A243143922DFABB13F5BB2F91EFB8A42CD764FC827AF4E",
        "seed 1 people 9C7838180C503AC8FF1B7610843FE7F95B80F0AB9DDC2B6F3AB53372F98E35F8",
        "seed 1 chunk 223,605 886AC87DEF23C35FF376B0A015C47C83",
        "seed 1 chunk 100,100 BF1C6B73FCEF46363A1C54D5CD5F9539",
        "seed 1 chunk 500,500 7F15B2AD876EAD2E4AA844607E63B72B",
        "seed 1 chunk 900,300 9940A5FA58D21DCA9CDFB2698E302A07",
        ],
        [1234] =
        [
        "seed 1234 continent ABEA0BC4D62AA53DB1B0ED57333D00E041281C0EC38D168C9F37702580B52326",
        "seed 1234 history D0753933E4B3CFCE5B08089AC6BEC39AE846446B3EFEEEEE17026899DC8912F9",
        "seed 1234 embark 199,96 score 1104",
        "seed 1234 region 77B9E729CFDF717F8FC77024180D5C167B1AFB7D7BBD903D5A79C8F5999C2358",
        "seed 1234 landmarks 4 38F5CCCDA5D3D0377F939E1AD0E9F805B7B9CF3113D9D38F2D4737FDFBAB18EB",
        "seed 1234 weather EA6ACF4ABD65896BDA0E47D93403132761E1647EEE179BB3EBCD39624A7D9BD7",
        "seed 1234 codex B8BB03CBA67C9BB67E51987E5106E74F41536B1859174C6655D80EA7F7C2ED72",
        "seed 1234 people 4DC41549C20552400579ACB76B9D51468231A6451503412D8522FB9AB11BBCDE",
        "seed 1234 chunk 273,337 CC92C6F2B23E08FFB9C35032BEA0B937",
        "seed 1234 chunk 100,100 991BAC2BDCD87299F94B4FCF57CB8F20",
        "seed 1234 chunk 500,500 1889260CDE7A69A412A117C687664BE5",
        "seed 1234 chunk 900,300 0481ED0DFB4D5B6C8F84957F0BDE0CFB",
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
