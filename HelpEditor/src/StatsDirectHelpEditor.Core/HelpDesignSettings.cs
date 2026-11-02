using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.Core;

/// <summary>Optional, source-controlled build design settings for this independent editor.</summary>
internal static class HelpDesignSettings
{
    private const string RelativePath = "Project/HelpEditorDesign.json";
    private const string DefaultAppearance = "flare";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string GetAppearance(string root, HelpBuildTarget target)
    {
        var targetKey = TargetKey(target);
        var settings = Read(SafePaths.Resolve(root, RelativePath));
        return ReadAppearance(settings, targetKey);
    }

    internal static void SetAppearance(string root, HelpBuildTarget target, string key)
    {
        var targetKey = TargetKey(target);
        if (!IsAppearance(key)) throw new ArgumentException("Choose Flare original, Readable Contents, Clean blue or Warm paper.", nameof(key));
        var path = SafePaths.Resolve(root, RelativePath);
        var settings = Read(path);
        if (ReadAppearance(settings, targetKey) == key) return;
        settings ??= new JsonObject { ["schemaVersion"] = 1 };
        var appearances = settings["appearance"] as JsonObject;
        if (appearances == null) settings["appearance"] = appearances = new JsonObject();
        appearances[targetKey] = key;
        SafePaths.AtomicWrite(path, settings.ToJsonString(JsonOptions) + Environment.NewLine);
    }

    private static JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException(RelativePath + " must contain a JSON object.");
            if (settings["schemaVersion"] is { } schema && (schema is not JsonValue value || !value.TryGetValue<int>(out var version) || version != 1))
                throw new InvalidDataException(RelativePath + " uses an unsupported schemaVersion; expected 1.");
            if (settings["appearance"] is { } appearances && appearances is not JsonObject)
                throw new InvalidDataException(RelativePath + " appearance must contain a JSON object.");
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Cannot read " + RelativePath + ": " + ex.Message, ex);
        }
    }

    private static string ReadAppearance(JsonObject? settings, string targetKey)
    {
        var node = (settings?["appearance"] as JsonObject)?[targetKey];
        if (node == null) return DefaultAppearance;
        if (node is JsonValue value && value.TryGetValue<string>(out var key) && IsAppearance(key)) return key;
        throw new InvalidDataException(RelativePath + " appearance." + targetKey + " must be flare, readable, clean-blue or warm-paper.");
    }

    private static bool IsAppearance(string? key) => key is "flare" or "readable" or "clean-blue" or "warm-paper";

    private static string TargetKey(HelpBuildTarget target) => target switch
    {
        HelpBuildTarget.Desktop => "desktop",
        HelpBuildTarget.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(target), "Choose a Desktop or Public build target.")
    };
}
