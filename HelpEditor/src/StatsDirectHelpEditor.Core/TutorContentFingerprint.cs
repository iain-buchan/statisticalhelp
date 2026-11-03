using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.Core;

/// <summary>Bind an author's approval to the exact learner-facing record, independently of JSON property order.</summary>
public static class TutorContentFingerprint
{
    public const string Algorithm = "sha256-canonical-tutor-record-v1";
    public static string Record(JsonObject record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) Write(record, true, writer);
        return Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant();
    }
    private static void Write(JsonNode? node, bool root, Utf8JsonWriter writer)
    {
        if (node is JsonObject row)
        {
            writer.WriteStartObject();
            foreach (var pair in row.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Key == "authoring" || root && pair.Key is "review" or "verification") continue;
                writer.WritePropertyName(pair.Key); Write(pair.Value, false, writer);
            }
            writer.WriteEndObject();
        }
        else if (node is JsonArray array) { writer.WriteStartArray(); foreach (var item in array) Write(item, false, writer); writer.WriteEndArray(); }
        else if (node == null) writer.WriteNullValue();
        else node.WriteTo(writer);
    }
}
