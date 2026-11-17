using System.Buffers.Binary;
using System.IO.Compression;

namespace Ravensguard.Art;

/// <summary>Minimal dependency-free 8-bit RGBA PNG writer.</summary>
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

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc(t, data));
        s.Write(len);
    }

    public static void Write(string path, PixelBuffer b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)b.W);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)b.H);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        var raw = new byte[b.H * (1 + b.W * 4)];
        int p = 0;
        for (int y = 0; y < b.H; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < b.W; x++)
            {
                uint c = b.Data[y * b.W + x];
                raw[p++] = (byte)(c >> 16); raw[p++] = (byte)(c >> 8); raw[p++] = (byte)c; raw[p++] = (byte)(c >> 24);
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    /// <summary>
    /// Decodes an 8-bit RGBA or RGB PNG to (width, height, RGBA bytes). Compressed bytes differ between zlib builds on
    /// different machines, so committed atlases are compared by pixels, not by file bytes.
    /// </summary>
    public static (int W, int H, byte[] Rgba) ReadPixels(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8 || data[0] != 137 || data[1] != 80) throw new InvalidDataException($"{path}: not a PNG");
        int w = 0, h = 0, colorType = 0, pos = 8;
        using var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, len);
            if (type == "IHDR")
            {
                w = (int)BinaryPrimitives.ReadUInt32BigEndian(body); h = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                if (body[8] != 8 || (body[9] != 6 && body[9] != 2) || body[12] != 0) throw new InvalidDataException($"{path}: only 8-bit RGB/RGBA non-interlaced PNGs are supported");
                colorType = body[9];
            }
            else if (type == "IDAT") idat.Write(body);
            else if (type == "IEND") break;
            pos += 12 + len;
        }
        int bpp = colorType == 6 ? 4 : 3, stride = w * bpp;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var rawMs = new MemoryStream();
        z.CopyTo(rawMs);
        var raw = rawMs.ToArray();
        var cur = new byte[stride]; var prev = new byte[stride];
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                int pred = f switch
                {
                    0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"{path}: bad filter {f}"),
                };
                cur[i] = (byte)(cur[i] + pred);
            }
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                rgba[o] = cur[x * bpp]; rgba[o + 1] = cur[x * bpp + 1]; rgba[o + 2] = cur[x * bpp + 2]; rgba[o + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
            }
            (cur, prev) = (prev, cur);
        }
        return (w, h, rgba);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
