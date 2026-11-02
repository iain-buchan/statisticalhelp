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
        using var context = new EditorApplicationContext(new MainForm(project, initialTopic: topic));
        Application.Run(context);
    }
}
