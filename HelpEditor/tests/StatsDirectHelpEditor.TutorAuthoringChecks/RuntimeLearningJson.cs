using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace StatsDirect.UI.Learning;

internal static class LearningJson
{
    internal static string S(this JsonNode node, string key, string fallback = "") => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    internal static int I(this JsonNode node, string key, int fallback = 0) => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<int>(out int number) ? number : fallback;
    internal static bool B(this JsonNode node, string key) => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<bool>(out bool yes) && yes;
    internal static JsonArray A(this JsonNode node, string key) => node is JsonObject obj && obj[key] is JsonArray array ? array : new JsonArray();
    internal static string Limit(this string text, int length) => text.Length <= length ? text : text[..length];
    internal static JsonArray Rows(params JsonNode[] rows) => new(rows);
    internal static JsonNode Read(string path, int limit)
    {
        if (new FileInfo(path).Length > limit) throw new IOException("This file is too large to open.");
        return JsonNode.Parse(File.ReadAllText(path)) ?? throw new IOException("This file is empty.");
    }
    internal static void Save(string path, JsonNode value, int limit = 10_000_000)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value.ToJsonString());
        if (bytes.Length > limit) throw new IOException("The learning record is too large. Export a copy before continuing.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
