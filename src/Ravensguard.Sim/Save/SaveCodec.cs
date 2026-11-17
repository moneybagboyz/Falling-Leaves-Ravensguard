using MessagePack;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class SectionRecord
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public byte[] Data { get; set; } = Array.Empty<byte>();
}

[MessagePackObject]
public sealed class SaveBody
{
    [Key(0)] public List<SectionRecord> Sections { get; set; } = new();
}

/// <summary>
/// Layout: magic(4) | headerLen(int32 LE) | header (MessagePack, uncompressed) | body (MessagePack, LZ4 block array).
/// Sections are sorted by ordinal name before writing.
/// </summary>
public static class SaveCodec
{
    private static readonly MessagePackSerializerOptions Plain = MessagePackSerializerOptions.Standard;
    private static readonly MessagePackSerializerOptions Lz4 =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Plain);
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) => MessagePackSerializer.Deserialize<T>(data, Plain);
    public static byte[] SerializeCompressed<T>(T value) => MessagePackSerializer.Serialize(value, Lz4);
    public static T DeserializeCompressed<T>(ReadOnlyMemory<byte> data) => MessagePackSerializer.Deserialize<T>(data, Lz4);

    public static byte[] Encode(SaveFile file)
    {
        var header = Serialize(file.Header);
        var body = new SaveBody();
        foreach (var k in file.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal))
            body.Sections.Add(new SectionRecord { Name = k, Data = file.Sections[k] });
        var bodyBytes = SerializeCompressed(body);

        var result = new byte[4 + 4 + header.Length + bodyBytes.Length];
        SaveFormat.Magic.CopyTo(result, 0);
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), header.Length);
        header.CopyTo(result, 8);
        bodyBytes.CopyTo(result, 8 + header.Length);
        return result;
    }

    /// <summary>Parses only the header from a stream (leaves the rest unread).</summary>
    public static SaveHeader ReadHeader(Stream s)
    {
        Span<byte> pre = stackalloc byte[8];
        ReadExactly(s, pre);
        if (!pre[..4].SequenceEqual(SaveFormat.Magic)) throw new SaveException("Not a Ravensguard save file (bad magic).");
        int len = BitConverter.ToInt32(pre[4..]);
        if (len < 0 || len > 16 * 1024 * 1024) throw new SaveException("Corrupt save header length.");
        var buf = new byte[len];
        ReadExactly(s, buf);
        try { return Deserialize<SaveHeader>(buf); }
        catch (Exception e) when (e is not SaveException) { throw new SaveException("Corrupt save header.", e); }
    }

    public static SaveFile Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        var header = ReadHeader(ms);
        try
        {
            var body = DeserializeCompressed<SaveBody>(bytes.AsMemory((int)ms.Position));
            return new SaveFile(header, body.Sections.Select(r => new KeyValuePair<string, byte[]>(r.Name, r.Data)));
        }
        catch (Exception e) when (e is not SaveException) { throw new SaveException("Corrupt save body.", e); }
    }

    private static void ReadExactly(Stream s, Span<byte> buf)
    {
        try { s.ReadExactly(buf); }
        catch (EndOfStreamException e) { throw new SaveException("Save file truncated.", e); }
    }
}
