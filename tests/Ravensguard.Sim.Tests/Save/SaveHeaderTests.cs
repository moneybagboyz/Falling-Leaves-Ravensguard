using MessagePack;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>Phase 1 review: the header DTOs had init accessors with initializers (MsgPack017), which deserialise to the type default when a key is missing.</summary>
public class SaveHeaderTests
{
    private static byte[] Wrap(byte[] header, byte[]? body = null)
    {
        var file = SaveCodec.Encode(new SaveFile(new SaveHeader(), [new KeyValuePair<string, byte[]>("a", [1])]));
        // keep the real body, swap the header
        var ms = new MemoryStream(file); SaveCodec.ReadHeader(ms);
        var realBody = file.AsSpan((int)ms.Position).ToArray();
        var res = new byte[8 + header.Length + realBody.Length];
        SaveFormat.Magic.CopyTo(res, 0);
        BitConverter.TryWriteBytes(res.AsSpan(4, 4), header.Length);
        header.CopyTo(res, 8); realBody.CopyTo(res, 8 + header.Length);
        return res;
    }

    private static byte[] Header(params object?[] fields) => MessagePackSerializer.Serialize(fields);

    [Fact]
    public void Non_default_header_values_round_trip()
    {
        var h = new SaveHeader
        {
            FormatVersion = SaveFormat.CurrentVersion, GameVersion = "9.9.9", ContentHash = "deadbeef", WorldSeed = 0xFFFF_FFFF_FFFFUL, GameSeconds = 123456789012L, Ironman = true,
            Mods = [new ModEntry("alpha", "1.2.3"), new ModEntry("beta", "0.0.1")],
        };
        var back = SaveCodec.ReadHeader(new MemoryStream(SaveCodec.Encode(new SaveFile(h, []))));
        Assert.Equal(h.FormatVersion, back.FormatVersion);
        Assert.Equal("9.9.9", back.GameVersion);
        Assert.Equal("deadbeef", back.ContentHash);
        Assert.Equal(h.WorldSeed, back.WorldSeed);
        Assert.Equal(h.GameSeconds, back.GameSeconds);
        Assert.True(back.Ironman);
        Assert.Equal(["alpha", "beta"], back.Mods.Select(m => m.Id));
        Assert.Equal(["1.2.3", "0.0.1"], back.Mods.Select(m => m.Version));
    }

    [Fact]
    public void A_missing_or_nil_mod_list_reads_as_empty_and_null_strings_as_empty()
    {
        var bytes = Wrap(Header(SaveFormat.CurrentVersion, "0.1", "hash", null, 7UL, 5L, false));
        var h = SaveCodec.ReadHeader(new MemoryStream(bytes));
        Assert.NotNull(h.Mods);
        Assert.Empty(h.Mods);
        Assert.Equal(7UL, h.WorldSeed);
        // a mod entry with a missing version
        var m = MessagePackSerializer.Deserialize<ModEntry>(Header("only-id"));
        Assert.Equal("only-id", m.Id);
        Assert.Equal("", m.Version);
    }

    [Fact]
    public void A_header_with_missing_keys_reports_an_incompatible_version_not_a_corrupt_body()
    {
        // an older/other build wrote only the first three keys
        var bytes = Wrap(Header(1, "0.0.1", "hash"));
        var ex = Assert.Throws<SaveException>(() => SaveCodec.Decode(bytes));
        Assert.DoesNotContain("Corrupt save body", ex.Message);
        Assert.Contains("header", ex.Message);
        Assert.Contains("incomplete or from an incompatible version", ex.Message);
        Assert.Throws<SaveException>(() => new SaveReader().CheckCompatibility(new SaveReader().ReadHeader(WriteTemp(bytes)), "hash", []));
    }

    [Fact]
    public void A_zero_format_version_is_rejected_clearly()
    {
        var bytes = Wrap(Header(0, "0.1", "h", new object[0], 1UL, 0L, false));
        var ex = Assert.Throws<SaveException>(() => SaveCodec.Decode(bytes));
        Assert.Contains("format version", ex.Message);
    }

    private static string WriteTemp(byte[] bytes)
    {
        var p = Path.Combine(Path.GetTempPath(), "hdr_" + Guid.NewGuid().ToString("N") + ".sav");
        File.WriteAllBytes(p, bytes);
        return p;
    }
}
