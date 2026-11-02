namespace StatsDirect.HelpEditor.App;

/// <summary>Keep independent editor windows alive until the last one closes.</summary>
public sealed class EditorApplicationContext : ApplicationContext
{
    [ThreadStatic] private static EditorApplicationContext? current;
    private readonly HashSet<MainForm> windows = [];

    public EditorApplicationContext(MainForm initialWindow)
    {
        if (current != null) throw new InvalidOperationException("An editor application context is already running on this thread.");
        current = this;
        Add(initialWindow);
    }

    public static void ShowEditor(MainForm window)
    {
        if (current != null) current.Add(window);
        else window.Show();
    }

    private void Add(MainForm window)
    {
        windows.Add(window);
        window.FormClosed += (_, _) =>
        {
            windows.Remove(window);
            if (windows.Count == 0) ExitThread();
        };
        window.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (ReferenceEquals(current, this)) current = null;
        base.Dispose(disposing);
    }
}
