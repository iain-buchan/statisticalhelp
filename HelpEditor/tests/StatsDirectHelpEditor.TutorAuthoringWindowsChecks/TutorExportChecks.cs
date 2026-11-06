using System.Reflection;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;

internal static partial class Program
{
    private static void CheckTutorExportPreferences()
    {
        string workspace = Path.Combine(fixture, "TutorExportPreferences"), settings = Path.Combine(workspace, "Settings/authoring-preferences.json");
        Directory.CreateDirectory(workspace);
        Type type = typeof(TutorEditorForm).Assembly.GetType("StatsDirect.HelpEditor.App.AuthoringPreferences", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        string Read() => (string)type.GetMethod("ReadTutorExportDirectory", flags)!.Invoke(null, [workspace])!;
        void Save(string destination) => type.GetMethod("SaveTutorExportDirectory", flags)!.Invoke(null, [workspace, destination]);
        string documents = (string)type.GetProperty("DefaultDirectory", flags)!.GetValue(null)!;
        Check(Read() == documents && !File.Exists(settings), "A first tutor export suggests Documents without creating preferences or selecting a personal temp folder");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "{\"futureSetting\":{\"keep\":true},\"websiteZipPath\":\"unchanged website choice\"}");
        string chosen = Path.Combine(workspace, "Chosen"), output = Path.Combine(chosen, "course.json"); Directory.CreateDirectory(chosen);
        Save(output);
        var savedPreferences = JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
        Check(Read() == Path.GetFullPath(chosen), "The next tutor export reads the last successful export directory from workspace preferences");
        Check(savedPreferences["futureSetting"]?["keep"]?.GetValue<bool>() == true && savedPreferences["websiteZipPath"]?.GetValue<string>() == "unchanged website choice",
            "Remembering tutor export preserves unknown settings and the separate website ZIP destination");
        string beforeInvalid = File.ReadAllText(settings); bool rejected = false;
        try { Save("relative.json"); } catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { rejected = true; }
        Check(rejected && File.ReadAllText(settings) == beforeInvalid, "An invalid relative export destination cannot overwrite remembered preferences");
        rejected = false; try { Save(Path.Combine(chosen, "course.txt")); } catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { rejected = true; }
        Check(rejected && File.ReadAllText(settings) == beforeInvalid, "A non-JSON export destination leaves remembered preferences unchanged");
        Directory.Delete(chosen);
        Check(Read() == documents && File.ReadAllText(settings) == beforeInvalid, "An unavailable remembered directory falls back to Documents without rewriting the user's settings");
        foreach (string badValue in new[] { "\"relative/folder\"", "{\"not\":\"a path\"}" })
        {
            string bad = "{\"tutorCourseExportDirectory\":" + badValue + ",\"future\":1}"; File.WriteAllText(settings, bad);
            Check(Read() == documents && File.ReadAllText(settings) == bad, "Invalid saved tutor export preferences use Documents and preserve the original bytes");
        }
        string malformed = "{ unfinished preferences"; File.WriteAllText(settings, malformed);
        Check(Read() == documents && File.ReadAllText(settings) == malformed, "Malformed settings fall back safely without replacing the settings file");
        rejected = false; try { Save(output); } catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(settings) == malformed, "Saving an export location does not discard malformed user settings");
    }
}
