using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private TutorEditorForm? tutorEditor;

    public Task OpenTutorAuthoringAsync()
    {
        if (tutorEditor is { IsDisposed: false }) { tutorEditor.Activate(); return Task.CompletedTask; }
        PrepareTutorSeed();
        var sourceProject = CurrentProject;
        async Task<HelpPolishContext?> SavedHelpContext() => await Task.Run(() =>
        {
            var topic = sourceProject.ListTopics().FirstOrDefault();
            return topic == null ? null : HelpPolishContext.Create(sourceProject, HelpPolishSnapshot.Create(topic.Path, sourceProject.ReadTopic(topic.Path)));
        });
        tutorEditor = new TutorEditorForm(Path.Combine(artifactRoot, "TutorSource", "starter-course.json"), artifactRoot, SavedHelpContext, externalLinkLauncher);
        EditorApplicationContext.ShowDocument(tutorEditor);
        status.Text = "Tutor authoring opened · save drafts, then publish reviewed built-in sources for the next StatsDirect build";
        return Task.CompletedTask;
    }

    private void PrepareTutorSeed()
    {
        string targetRoot = Path.Combine(artifactRoot, "TutorSource"); Directory.CreateDirectory(targetRoot);
        foreach (string name in new[] { "starter-course.json", "health-biomed-draft-pack.json", "LICENSE", "provenance.json", "README.md" })
        {
            string destination = Path.Combine(targetRoot, name);
            if (File.Exists(destination)) continue;
            string source = Path.Combine(AppContext.BaseDirectory, "TutorSeed", name);
            if (!File.Exists(source)) { if (name == "starter-course.json") throw new FileNotFoundException("Restore the packaged TutorSeed folder before opening course authoring.", source); continue; }
            string temporary = Path.Combine(targetRoot, ".tutor-seed-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary, false);
                try { File.Move(temporary, destination, false); }
                catch (IOException) when (File.Exists(destination)) { } // Another authoring window completed the seed first.
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
