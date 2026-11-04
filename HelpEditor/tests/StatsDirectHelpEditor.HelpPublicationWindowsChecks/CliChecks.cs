using System.Diagnostics;
using System.Text.Json;

internal static class CliChecks
{
    internal static async Task Run(string root, string fixture, string source,
        Action<bool, string, object?> check)
    {
        string executable = Path.Combine(root, "src", "StatsDirectHelpEditor.Cli", "bin", "Release", "net10.0", "StatsDirectHelpBuilder.exe");
        var basic = new Dictionary<string, string>
        {
            ["--project"] = source, ["--zip"] = Path.Combine(fixture, "CliOutputs", "NeverCreated.zip"),
            ["--github-url"] = "https://github.com/statsdirect-tests/never-contact",
            ["--github-branch"] = "authoring-check", ["--author-name"] = "Parser Author",
            ["--author-email"] = "parser@example.invalid", ["--statsdirect-source"] = Path.Combine(fixture, "NeverCreatedStatsDirect")
        };
        var cases = new List<(string Name, string[] Arguments, string Error)>();
        string[] Args(IEnumerable<KeyValuePair<string, string>> pairs) => ["publish-help", .. pairs.SelectMany(pair => new[] { pair.Key, pair.Value })];
        foreach (string missing in basic.Keys)
            cases.Add(("Missing required " + missing, Args(basic.Where(pair => pair.Key != missing)), "Missing " + missing));
        cases.Add(("Unknown option", [.. Args(basic), "--not-a-publication-option", "refuse"], "Unknown option"));
        cases.Add(("Duplicate valued option", [.. Args(basic), "--zip", basic["--zip"]], "Duplicate option"));
        cases.Add(("Duplicate boolean flag", [.. Args(basic), "--prepare-only", "--prepare-only"], "Duplicate option"));
        cases.Add(("Boolean flag cannot consume a value", [.. Args(basic), "--prepare-only", "false"], "Unknown option"));
        cases.Add(("Missing optional value", [.. Args(basic), "--assets"], "Missing value"));
        var results = new List<object>();
        foreach (var test in cases)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = fixture, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string arg in test.Arguments) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            string stdout = await output, stderr = await error;
            bool passed = process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout) && stderr.Contains(test.Error, StringComparison.Ordinal);
            var row = new { test.Name, arguments = test.Arguments, exitCode = process.ExitCode, stdout, stderr, passed };
            results.Add(row);
            check(passed, "CLI refuses " + test.Name + " before publication", row);
        }
        check(!Directory.Exists(Path.Combine(fixture, "CliOutputs")) && !Directory.Exists(basic["--statsdirect-source"]),
            "Strict CLI parsing creates no ZIP destination, desktop repository or network publication", null);
        File.WriteAllText(Path.Combine(fixture, "cli-parser-cases.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
