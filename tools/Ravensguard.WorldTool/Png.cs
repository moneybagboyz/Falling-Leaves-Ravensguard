using System.Buffers.Binary;
using System.IO.Compression;

namespace Ravensguard.WorldTool;

/// <summary>Minimal dependency-free 8-bit RGB PNG writer (pixels are 0xRRGGBB).</summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        Span<byte> w = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(w, (uint)data.Length);
        s.Write(w); s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFFu;
        foreach (var b in t) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(w, c ^ 0xFFFFFFFFu);
        s.Write(w);
    }

    public static void Write(string path, int w, int h, uint[] rgb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(fs, "IHDR", ihdr);
        var raw = new byte[h * (1 + w * 3)];
        int p = 0;
        for (int y = 0; y < h; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < w; x++)
            {
                uint c = rgb[y * w + x];
                raw[p++] = (byte)(c >> 16); raw[p++] = (byte)(c >> 8); raw[p++] = (byte)c;
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }
}
