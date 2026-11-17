using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ravensguard.Content;

public static class ContentHasher
{
    /// <summary>SHA-256 over the load order and, per mod, the sorted (relative path, bytes) of every json/jsonc file.</summary>
    public static string Compute(IEnumerable<ModInfo> modsInLoadOrder)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Feed(string s) { var b = Encoding.UTF8.GetBytes(s); sha.AppendData(BitConverter.GetBytes(b.Length)); sha.AppendData(b); }
        foreach (var mod in modsInLoadOrder)
        {
            Feed(mod.Manifest.Id);
            foreach (var rel in ModLoader.EnumerateFiles(mod.Folder))
            {
                Feed(rel);
                var bytes = File.ReadAllBytes(Path.Combine(mod.Folder, rel));
                sha.AppendData(BitConverter.GetBytes(bytes.Length));
                sha.AppendData(bytes);
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Cache file key: the content hash plus fingerprints of the type registry and the vocabulary.</summary>
    public static string CacheKey(string contentHash, ContentTypeRegistry registry, ContentVocabulary? vocabulary)
    {
        var text = $"{contentHash}|{registry.Fingerprint()}|{(vocabulary ?? new ContentVocabulary()).Fingerprint()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

/// <summary>Stores merged, validated JSON (not CLR objects) gzip-compressed, one file per content hash.</summary>
public sealed class ContentCache(string directory)
{
    private string PathFor(string hash) => Path.Combine(directory, hash + ".cache");
    public bool Exists(string hash) => File.Exists(PathFor(hash));

    public void Store(string hash, ContentDatabase db)
    {
        Directory.CreateDirectory(directory);
        var root = new JsonObject
        {
            ["entries"] = new JsonArray(db.Entries.Select(e => (JsonNode)new JsonObject
            {
                ["type"] = e.Type, ["id"] = e.Id, ["mod"] = e.Mod,
                ["file"] = e.Location.File, ["line"] = e.Location.Line, ["column"] = e.Location.Column,
                ["json"] = e.Json.DeepClone(),
            }).ToArray()),
            ["migrations"] = new JsonArray(db.Migrations.SelectMany(t => t.Value.Select(m => (JsonNode)new JsonObject
                { ["type"] = t.Key, ["from"] = m.Key, ["to"] = m.Value })).ToArray()),
            ["overrides"] = new JsonArray(db.Overrides.Select(o => (JsonNode)new JsonObject
                { ["type"] = o.Type, ["id"] = o.Id, ["previous"] = o.PreviousMod, ["new"] = o.NewMod }).ToArray()),
        };
        var target = PathFor(hash);
        var tmp = $"{target}.{Guid.NewGuid():N}.tmp"; // unique per call: concurrent writers never share a temp file
        try
        {
            using (var fs = File.Create(tmp))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var w = new Utf8JsonWriter(gz))
                root.WriteTo(w);
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Returns null on a miss or a corrupt/unusable cache file.</summary>
    public ContentDatabase? TryLoad(string hash, ContentTypeRegistry registry)
    {
        var path = PathFor(hash);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            var root = JsonNode.Parse(gz)!.AsObject();
            var db = new ContentDatabase(registry);
            foreach (var n in root["entries"]!.AsArray())
            {
                var o = n!.AsObject();
                db.Put(new ContentEntry
                {
                    Type = (string)o["type"]!, Id = (string)o["id"]!, Mod = (string)o["mod"]!,
                    Location = new((string)o["file"]!, (int)o["line"]!, (int)o["column"]!),
                    Json = o["json"]!.AsObject(),
                });
            }
            foreach (var n in root["migrations"]!.AsArray())
                db.AddMigration((string)n!["type"]!, (string)n["from"]!, (string)n["to"]!);
            foreach (var n in root["overrides"]!.AsArray())
                db.AddOverride(new((string)n!["type"]!, (string)n["id"]!, (string)n["previous"]!, (string)n["new"]!));
            var report = new ContentReport();
            db.Materialize(report);
            return report.HasErrors ? null : db;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NullReferenceException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
