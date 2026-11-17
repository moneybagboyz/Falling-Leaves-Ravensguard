using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ravensguard.Content;

internal sealed record RawDefinition(JsonObject Node, SourceLocation Location, Dictionary<string, SourceLocation> PropertyLocations);

/// <summary>Parses a content file (JSON array of objects, comments and trailing commas allowed) with positions.</summary>
internal static class ContentFileParser
{
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static List<RawDefinition> Parse(byte[] bytes, string displayPath, ContentReport report)
    {
        var result = new List<RawDefinition>();
        int bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var data = bytes.AsSpan(bom);

        // line starts (byte offsets into data)
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < data.Length; i++) if (data[i] == (byte)'\n') lineStarts.Add(i + 1);
        SourceLocation Loc(long offset)
        {
            int idx = lineStarts.BinarySearch((int)offset);
            if (idx < 0) idx = ~idx - 1;
            return new SourceLocation(displayPath, idx + 1, (int)offset - lineStarts[idx] + 1);
        }

        var elements = new List<(long Offset, bool IsObject, Dictionary<string, SourceLocation> Props)>();
        try
        {
            var reader = new Utf8JsonReader(data, ReaderOptions);
            while (reader.Read())
            {
                int depth = reader.CurrentDepth;
                var tt = reader.TokenType;
                if (depth == 0)
                {
                    if (tt == JsonTokenType.EndArray) continue;
                    if (tt != JsonTokenType.StartArray)
                    {
                        report.Error(Loc(reader.TokenStartIndex), "Content file must contain a JSON array of definition objects");
                        return result;
                    }
                }
                else if (depth == 1 && tt is not (JsonTokenType.EndObject or JsonTokenType.EndArray))
                {
                    elements.Add((reader.TokenStartIndex, tt == JsonTokenType.StartObject, []));
                }
                else if (depth == 2 && tt == JsonTokenType.PropertyName && elements.Count > 0 && elements[^1].IsObject)
                {
                    elements[^1].Props[reader.GetString()!] = Loc(reader.TokenStartIndex);
                }
            }
        }
        catch (JsonException ex)
        {
            report.Error(new SourceLocation(displayPath, (int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1),
                $"Invalid JSON: {ex.Message}");
            return result;
        }

        JsonArray arr;
        try { arr = JsonNode.Parse(data.ToArray(), null, DocOptions)!.AsArray(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            report.Error(new SourceLocation(displayPath, 1, 1), $"Invalid JSON: {ex.Message}");
            return result;
        }

        for (int i = 0; i < arr.Count && i < elements.Count; i++)
        {
            var loc = Loc(elements[i].Offset);
            if (arr[i] is JsonObject obj) result.Add(new RawDefinition(obj, loc, elements[i].Props));
            else report.Error(loc, "Array element is not an object");
        }
        return result;
    }
}
