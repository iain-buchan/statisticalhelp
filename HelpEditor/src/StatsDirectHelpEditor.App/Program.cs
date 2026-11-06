using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? project = null;
        string? topic = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i].Equals("--project", StringComparison.OrdinalIgnoreCase)) project = args[++i];
            else if (args[i].Equals("--topic", StringComparison.OrdinalIgnoreCase)) topic = args[++i];
        }
        HelpEditorWorkspace workspace;
        try { workspace = HelpEditorWorkspace.Resolve(AppContext.BaseDirectory); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(error.Message, "StatsDirect Help Editor workspace", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var main = new MainForm(project, initialTopic: topic, workspaceOverride: workspace);
        if (args.Contains("--tutor", StringComparer.OrdinalIgnoreCase)) main.Shown += async (_, _) =>
        {
            try { await main.Initialization; await main.OpenTutorAuthoringAsync(); }
            catch (Exception error) { MessageBox.Show(main, error.Message, "Tutor authoring", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        using var context = new EditorApplicationContext(main);
        Application.Run(context);
    }
}
