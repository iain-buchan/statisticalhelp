using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

static class SharedPublicationRaceChecks
{
    public static async Task Run(string editorRoot,string fixtures,Action<bool,string,object?> check)
    {
        async Task<Exception> Fail(Fixture f,HelpPublicationOptions options,Action<HelpPublicationProgress>? callback=null)
        {
            try { await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(f.Source),f.Assets,options,
                progress:callback==null?null:new InlineProgress(callback)); }
            catch(Exception error) when(error is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return error; }
            throw new Exception("The shared race fixture unexpectedly published.");
        }
        bool Undelivered(Fixture f)=>!File.Exists(f.Zip)&&!Directory.Exists(f.Help)&&!Directory.Exists(Path.Combine(f.Application,"Learning/AuthoredTutor"));
        var (draft,options)=await SharedGitChecks.NewFixture(editorRoot,fixtures);string head=await draft.Head();
        var content=JsonNode.Parse(File.ReadAllText(options.TutorCoursePath!))!.AsObject();
        content["questions"]![0]!["stem"]="Unreviewed changed teaching material";
        File.WriteAllText(options.TutorCoursePath!,content.ToJsonString());
        var error=await Fail(draft,options);
        check(await draft.Head()==head && Undelivered(draft),"A saved unreviewed tutor edit is refused before any local or Git target changes",error.Message);
        // A user-editable baseline is never accepted as authority for a new
        // shared publication: the packaged original remains the comparison.
        content["authoring"]!["builtinBaseline"]=TutorBuiltinPublisher.CreateBaseline(content);
        File.WriteAllText(options.TutorCoursePath!,content.ToJsonString());error=await Fail(draft,options);
        check(await draft.Head()==head && Undelivered(draft),"An edited course cannot self-approve by replacing its own mutable baseline",error.Message);

        var (source,sourceOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await source.Head();
        error=await Fail(source,sourceOptions,progress=>{if(progress.Phase=="Build")File.AppendAllText(sourceOptions.TutorCoursePath!,"\n");});
        check(error is HelpPublicationException && await source.Head()==head && Undelivered(source),
            "A saved tutor source changed after snapshot is detected before deliveries or Git push",error.Message);

        var (seed,seedOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await seed.Head();
        error=await Fail(seed,seedOptions,progress=>{if(progress.Phase=="Build")File.AppendAllText(Path.Combine(seed.Root,"TutorSeed/starter-course.json"),"\n");});
        check(error is HelpPublicationException && await seed.Head()==head && Undelivered(seed),
            "A changed packaged tutor baseline invalidates the frozen publication before delivery",error.Message);

        var (appeared,appearedOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await appeared.Head();
        string note=Path.Combine(appeared.Application,"Learning/AuthoredTutor/user-note.txt");
        error=await Fail(appeared,appearedOptions,progress=>{if(progress.Phase=="Build")Write(note,"External tutor output must be preserved");});
        var partial=error as HelpPublicationException;
        check(partial!=null && !partial.Result.Completed && !partial.Result.GitPushed && partial.Result.Tutor==null &&
            partial.Result.TargetStates["WebsiteZip"]=="Verified" && partial.Result.TargetStates["DesktopHelp"]=="Verified" &&
            File.ReadAllText(note)=="External tutor output must be preserved" && await appeared.Head()==head,
            "A tutor destination appearing after inspection is preserved and completed ZIP/Desktop targets are reported as partial",partial?.Result);
        check(!File.Exists(Path.Combine(appeared.Application,"Learning/.AuthoredTutor.publishing")),
            "A pre-activation tutor refusal removes only its own marker and leaves external content untouched",null);

        var (changed,changedOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await changed.Head();
        string changedBank=Path.Combine(changed.Application,"Learning/AuthoredTutor/bank.mjs");
        error=await Fail(changed,changedOptions,progress=>{if(progress.Phase=="Push")File.AppendAllText(changedBank,"// External change before push\n");});partial=error as HelpPublicationException;
        check(partial!=null && !partial.Result.GitPushed && await changed.Head()==head && File.ReadAllText(changedBank).EndsWith("// External change before push\n",StringComparison.Ordinal),
            "A local tutor output changed after delivery is rechecked before Git publication, preserving the newer local edit",partial?.Result);

        var (desktop,desktopOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await desktop.Head();
        string desktopNote=Path.Combine(desktop.Help,"user-note.txt");
        error=await Fail(desktop,desktopOptions,progress=>{if(progress.Phase=="WebsiteZip")Write(desktopNote,"External desktop output");});partial=error as HelpPublicationException;
        check(partial!=null && partial.Result.WebsiteZip!=null && partial.Result.DesktopHelp==null && partial.Result.Tutor==null &&
            !partial.Result.GitPushed && File.ReadAllText(desktopNote)=="External desktop output" && await desktop.Head()==head &&
            !Directory.Exists(Path.Combine(desktop.Application,"Learning/AuthoredTutor")),
            "A later Desktop refusal retains the verified ZIP and private shared commit without installing tutor or pushing Git",partial?.Result);

        var (overlap,overlapOptions)=await SharedGitChecks.NewFixture(editorRoot,fixtures);head=await overlap.Head();
        string raw=Path.Combine(overlap.Source,"Content/raw-course.json");File.Copy(overlapOptions.TutorCoursePath!,raw);string rawHash=Hash(raw);
        error=await Fail(overlap,overlapOptions with {TutorCoursePath=raw});
        check(await overlap.Head()==head && Undelivered(overlap) && Hash(raw)==rawHash,
            "Tutor authoring JSON inside help inputs is refused rather than leaked into reader output",error.Message);
        string zipSource=Path.Combine(overlap.Root,"TutorSource.zip");File.Copy(overlapOptions.TutorCoursePath!,zipSource);rawHash=Hash(zipSource);
        error=await Fail(overlap,overlapOptions with {TutorCoursePath=zipSource,WebsiteZipPath=zipSource});
        check(await overlap.Head()==head && Hash(zipSource)==rawHash && !Directory.Exists(overlap.Help),
            "The website ZIP can never replace the selected saved tutor course even with a misleading filename",error.Message);
    }
    static void Write(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text,new UTF8Encoding(false));}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
