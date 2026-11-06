using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;

internal static partial class Program
{
    private static readonly List<object> checks=[];
    private static int failed;
    private static TutorEditorForm form=null!;
    private static string root="",fixture="",file="",evidence="";
    [STAThread]
    private static int Main(string[] args)
    {
        root=Path.GetFullPath(args[0]);evidence=Path.Combine(root,"evidence");Directory.CreateDirectory(evidence);
        fixture=Path.Combine(evidence,"TutorAuthoringWindows-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);file=Path.Combine(fixture,"course.json");
        File.WriteAllText(file,"""
        {"schemaVersion":2,"id":"native-health-course","title":"Native health course","version":"1.0","provider":{"name":"Course author","reviewEmail":"support@statisticalhelp.org","assessmentType":"self"},"documents":[{"id":"source-1","title":"Source note","text":"Attributed course text.","url":"https://www.cdc.gov/","unknownSource":"keep"}],"lessons":[{"id":"lesson-1","title":"Health lesson","objective":"Distinguish risk and rate.","summary":"Original explanation.","review":{"status":"Reviewed by course author","notes":"Original review"},"unknownLesson":{"keep":true},"sources":[{"title":"CDC","url":"https://www.cdc.gov/","scope":"Definitions","unknownReference":"keep"}]}],"questions":[{"id":"question-1","version":3,"topic":"Rates","objective":"Calculate an incidence rate.","stem":"What is 12 divided by 600?","options":[{"id":"A","text":"0.02","feedback":"12/600 = 0.02.","unknownOption":"keep"},{"id":"B","text":"50","feedback":"This inverts the rate."}],"correct":"A","explanation":"Divide events by person-time.","review":{"status":"Reviewed by course author"},"unknownQuestion":"keep"}],"authoring":{"audience":"Health students"},"unknownCourse":"keep"}
        """);
        ApplicationConfiguration.Initialize();Application.ThreadException+=(_,e)=>Check(false,"Unhandled UI exception: "+e.Exception);
        form=new TutorEditorForm(file,fixture,externalLinkLauncher:_=>{}){SuppressDialogs=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,0),Size=new Size(1500,1000)};
        bool previewOnly=args.Contains("--preview-only"), classificationOnly=args.Contains("--classification-only"), exportOnly=args.Contains("--export-preferences-only");
        form.Shown+=async (_,_)=>{try{if(previewOnly)await CheckEmbeddedPreviewAsync();else if(classificationOnly)CheckQuestionClassification();else if(exportOnly)CheckTutorExportPreferences();else Run();}catch(Exception error){Check(false,"Unexpected native tutor error: "+error);}finally{form.Close();}};
        Application.Run(form);
        var report=new{passed=failed==0,failed,assertions=checks.Count,fixture,checkedAtUtc=DateTimeOffset.UtcNow,checks};
        File.WriteAllText(Path.Combine(evidence,previewOnly?"tutor-preview-windows-checks.json":classificationOnly?"tutor-classification-windows-checks.json":exportOnly?"tutor-export-preferences-checks.json":"tutor-authoring-windows-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.assertions,report.fixture}));return failed==0?0:1;
    }
    private static void Check(bool value,string name){checks.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failed++;}
    private static string S(JsonNode? node,string key)=>node?[key]?.GetValue<string>()??"";
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonObject Row(string kind,string id)=>(JsonObject)form.Document.GetItems(kind).Single(row=>S(row,"id")==id).DeepClone();
    private static TutorResearchProposal Proposal(JsonObject before,JsonObject after)=>new("questions","Refine",before.ToJsonString(),after,"Draft refinement","Check the denominator.","12 / 600",[],[],false);
    private static void Run()
    {
        CheckTutorExportPreferences();
        string beforeFile=Hash(file);
        Check(form.SelectedKind=="course"&&!form.HasUnsavedChanges,"Authoring window opens the course metadata without dirtying source");
        form.SelectItem("lessons","lesson-1");form.SetField("review.notes","Updated review notes only.");form.CommitCurrent();
        Check(S(Row("lessons","lesson-1")["review"],"status")=="Reviewed by course author","Native review-only edit preserves author review status on minimal imported lesson");
        form.SetField("summary","Updated health teaching explanation.");form.CommitCurrent();
        Check(S(Row("lessons","lesson-1"),"summary")=="Updated health teaching explanation."&&S(Row("lessons","lesson-1")["review"],"status").Contains("Draft"),"Native lesson edit updates content and invalidates review");
        form.SetField("summary","Original explanation.");form.CommitCurrent();Check(S(Row("lessons","lesson-1"),"summary")=="Original explanation.","Changing a field back after a prior commit restores the original text");
        form.SetField("summary","Updated health teaching explanation.");form.CommitCurrent();
        Check(Row("lessons","lesson-1")["unknownLesson"]?["keep"]?.GetValue<bool>()==true&&S(Row("lessons","lesson-1")["sources"]?[0],"unknownReference")=="keep","Lesson and source-grid edits retain unknown fields");
        form.UndoItem();Check(S(Row("lessons","lesson-1"),"summary")=="Original explanation.","Undo restores previous lesson content");
        form.RedoItem();Check(S(Row("lessons","lesson-1"),"summary")=="Updated health teaching explanation.","Redo restores the edited lesson content");
        form.PasteExampleData("Before\tAfter\r\n12\t10\r\n15\t11\r\n");form.CommitCurrent();
        var example=Row("lessons","lesson-1")["columns"]!.AsArray();Check(example.Count==2&&S(example[0],"title")=="Before"&&example[1]!["values"]![1]!.GetValue<double>()==11,"Excel-style paired numeric columns paste with headers and row order intact");
        form.PasteExampleData("PEFR\n312\n242\n");form.CommitCurrent();example=Row("lessons","lesson-1")["columns"]!.AsArray();Check(example.Count==1&&example[0]!["values"]!.AsArray().Count==2,"Single-column numeric exercise data can be pasted");
        string previousColumns=form.GetField("columns");bool badData=false;try{form.PasteExampleData("Before\tAfter\n12\n");}catch(InvalidDataException){badData=true;}
        Check(badData&&form.GetField("columns")==previousColumns,"Malformed pasted exercise data does not alter the previous example");
        form.SelectItem("documents","source-1");form.SetField("text","A revised attributed note.");form.CommitCurrent();
        Check(S(Row("documents","source-1"),"text")=="A revised attributed note."&&S(Row("documents","source-1"),"unknownSource")=="keep","Course-document editing retains source metadata");
        form.SelectItem("questions","question-1");form.SetField("stem","What rate results from 12 events in 600 person-years?");form.CommitCurrent();
        Check(Row("questions","question-1")["version"]!.GetValue<int>()==4&&S(Row("questions","question-1"),"unknownQuestion")=="keep","MCQ edit increments version while preserving unknown question fields");
        var options=(DataGridView)typeof(TutorEditorForm).GetField("options",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
        options.Rows[1].Cells[1].Value="0.2";options.Rows[1].Cells[2].Value="This is ten times the rate.";form.SetField("correct","B");form.CommitCurrent();
        Check(S(Row("questions","question-1"),"correct")=="B"&&S(Row("questions","question-1")["options"]?[1],"text")=="0.2"&&S(Row("questions","question-1")["options"]?[0],"unknownOption")=="keep","Answer grid, feedback and selected key commit together without losing option metadata");
        form.SetField("stem","Literal <script>alert('x')</script> & denominator question");form.CommitCurrent();string html=form.BuildPreviewHtml();
        Check(html.Contains("&lt;script&gt;")&&!html.Contains("<script>alert('x')</script>")&&html.Contains("value='B'")&&html.Contains("\"correct\":\"B\"")&&html.Contains("This is ten times the rate."),"Learner preview escapes authored text and includes the chosen answer/feedback");
        string diskBeforeProposal=Hash(file);var frozen=Row("questions","question-1");var proposed=(JsonObject)frozen.DeepClone();proposed["explanation"]="An author-reviewed AI draft explanation.";
        form.ApplyResearchProposal(Proposal(frozen,proposed));
        Check(S(Row("questions","question-1"),"explanation")=="An author-reviewed AI draft explanation."&&form.HasUnsavedChanges&&Hash(file)==diskBeforeProposal,"Accepted research proposal remains an unsaved authoring draft");
        var aiRecord=Row("questions","question-1");Check(S(aiRecord["review"],"notes")=="Check the denominator."&&S(aiRecord["authoring"]?["lastAiReview"],"proposedRCheck")=="12 / 600"&&S(aiRecord["authoring"]?["lastAiReview"],"rCheckStatus").StartsWith("Not executed"),"Accepted draft retains statistical review notes and R checks without claiming execution");
        bool staleRejected=false;try{form.ApplyResearchProposal(Proposal(frozen,proposed));}catch(InvalidOperationException){staleRejected=true;}
        Check(staleRejected&&S(Row("questions","question-1"),"explanation")=="An author-reviewed AI draft explanation.","A stale research proposal is refused without replacing newer edits");
        string beforeMalformed=form.Document.Content.ToJsonString();form.SetJsonSource("{invalid JSON");bool malformedRejected=false;
        try{form.SelectItem("lessons","lesson-1");}catch(JsonException){malformedRejected=true;}catch(InvalidDataException){malformedRejected=true;}
        Check(malformedRejected&&form.SelectedKind=="questions"&&form.HasUnsavedChanges&&form.Document.Content.ToJsonString()==beforeMalformed,"Malformed raw JSON blocks navigation and preserves the live draft and course");
        var valid=Row("questions","question-1");form.SetJsonSource(valid.ToJsonString());form.CommitCurrent();
        var tabs=(TabControl)typeof(TutorEditorForm).GetField("tabs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;tabs.SelectedIndex=0;
        form.SelectItem("course","");form.SetField("authoring.audience","NHS commissioning analysts");form.CommitCurrent();form.SaveCourse();
        Check(!form.HasUnsavedChanges&&Hash(file)!=beforeFile&&S(form.Document.Content["authoring"],"audience")=="NHS commissioning analysts"&&S(form.Document.Content,"unknownCourse")=="keep","Native Save persists course edits and local audience while keeping unknown course metadata");
        Check(S(form.Document.Content,"version")!="1.0","Saving changed native course advances the runtime course key");
        form.SelectItem("questions","question-1");string savedVersion=S(form.Document.Content,"version");form.SetField("hint","Identify the correct denominator first.");form.CommitCurrent();
        File.AppendAllText(file," ");bool conflict=false;try{form.SaveCourse();}catch(IOException){conflict=true;}
        Check(conflict&&form.HasUnsavedChanges&&S(Row("questions","question-1"),"hint")=="Identify the correct denominator first.","Native Save conflict retains unsaved content instead of overwriting external source");
        Check(form.LastError==null,"Direct native authoring operations have no unexpected reported UI errors");
        CheckActiveGridSaves();
        CheckBuiltinPublishing();
        CheckDeepNativeWorkflows();
        CheckQuestionClassification();
        CheckNativeNavigationSoak();
        WritePreviewFixtures();
    }
    private static void CheckBuiltinPublishing()
    {
        string repo=Path.Combine(fixture,"Isolated StatsDirect Source"),assets=Path.Combine(repo,"StatsDirectUI","Assets");
        Directory.CreateDirectory(Path.Combine(repo,"Learning"));Directory.CreateDirectory(Path.Combine(assets,"Operations"));Directory.CreateDirectory(Path.Combine(assets,"Help"));
        File.WriteAllText(Path.Combine(repo,"Learning","build.mjs"),"// Isolated source-root marker; this file is never run.\n");
        File.WriteAllText(Path.Combine(repo,"StatsDirectUI","StatsDirectUI.csproj"),"<Project Sdk='Microsoft.NET.Sdk'/>\n");
        string source=Path.Combine(fixture,"builtin-authoring-source.json");
        File.Copy(Path.Combine(root,"TutorSource","starter-course.json"),source);
        var starter=JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var records=starter["lessons"]!.AsArray().Concat(starter["questions"]!.AsArray()).OfType<JsonObject>().ToArray();
        foreach(string operation in records.Select(row=>S(row,"operation")).Where(value=>value.Length>0).Distinct())File.WriteAllText(Path.Combine(assets,"Operations",operation+".xml"),"<Operation/>\n");
        foreach(string help in records.Select(row=>S(row,"help")).Where(value=>value.Length>0).Distinct())
        {string path=Path.Combine(assets,"Help",help.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"<html><body>Isolated help fixture</body></html>\n");}
        string sourceHash=Hash(source);
        using var window=new TutorEditorForm(source,fixture,externalLinkLauncher:_=>{}){SuppressDialogs=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,0),Size=new Size(1500,1000)};
        window.Show();
        var initial=window.PublishBuiltinTo(repo);string output=initial.OutputDirectory;
        var manifest=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"manifest.json")))!;
        Check(initial.LessonCount==12&&initial.QuestionCount==35&&initial.BankVersion==S(starter["authoring"],"sourceBankVersion")&&Directory.GetFiles(output).Length==6,"Native built-in publish writes the exact original12lesson35question source set and original bank version");
        Check(Hash(source)==sourceHash&&manifest["reviewGatePassed"]!.GetValue<bool>(),"Native publishing does not save the source automatically and records its review gate");
        Dictionary<string,string> Published()=>Directory.GetFiles(output).ToDictionary(path=>Path.GetFileName(path)!,Hash);
        bool Same(Dictionary<string,string> old)=>Published().OrderBy(pair=>pair.Key).SequenceEqual(old.OrderBy(pair=>pair.Key));
        string id=S(starter["questions"]![0],"id");window.SelectItem("questions",id);
        window.SetField("explanation",window.GetField("explanation")+" Revised teaching wording.");window.CommitCurrent();var previous=Published();string draft=window.Document.Content.ToJsonString();bool rejected=false;
        try{window.PublishBuiltinTo(repo);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&Same(previous)&&window.Document.Content.ToJsonString()==draft,"Unreviewed native MCQ edit cannot publish and preserves prior build output plus open draft");
        window.RecordReview("Fixture course author","Checked the revised wording, key, distractors and appropriate source.");var approved=window.PublishBuiltinTo(repo);
        Check(approved.BankVersion!=initial.BankVersion&&JsonNode.Parse(File.ReadAllText(Path.Combine(output,"manifest.json")))!["reviewedChangedItems"]!.AsArray().Count==1,"Explicit native author review permits the changed builtin question with a new bank identity");
        var frozen=window.Document.GetItems("questions").Single(row=>S(row,"id")==id);var proposal=(JsonObject)frozen.DeepClone();proposal["explanation"]=S(proposal,"explanation")+" AI-suggested wording.";proposal["review"]=new JsonObject{["status"]="Reviewed by an AI model",["reviewer"]="Claimed model reviewer",["notes"]="Claimed review"};
        window.ApplyResearchProposal(Proposal(frozen,proposal));previous=Published();rejected=false;try{window.PublishBuiltinTo(repo);}catch(InvalidDataException){rejected=true;}
        var ai=window.Document.GetItems("questions").Single(row=>S(row,"id")==id);
        Check(rejected&&Same(previous)&&ai["review"]?["reviewer"]==null&&S(ai["review"],"status").Contains("Draft"),"Accepting an AI proposal alone cannot claim human review or replace built-in source");
        window.RecordReview("Fixture course author","Reviewed the AI proposal against the source and answer key.");window.PublishBuiltinTo(repo);
        var checkedRecord=window.Document.GetItems("questions").Single(row=>S(row,"id")==id);checkedRecord["verification"]=new JsonObject{["status"]="numerical-check-recorded",["passed"]=true,["checkedAt"]="2026-11-03"};window.Document.Update("questions",id,checkedRecord);
        window.SelectItem("questions",id);window.SetField("explanation",window.GetField("explanation")+" Different numerical setting.");window.CommitCurrent();
        window.RecordReview("Fixture course author","Educational wording checked; no new calculation has been recorded.");rejected=false;try{window.PublishBuiltinTo(repo);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&S(window.Document.GetItems("questions").Single(row=>S(row,"id")==id)["verification"],"status")=="not-rechecked-after-edit","Default native RecordReview does not turn a stale numerical check into a pass");
        bool evidenceRequired=false;try{window.RecordReview("Fixture course author","Reviewed",true,"");}catch(ArgumentException){evidenceRequired=true;}
        Check(evidenceRequired,"Recording numerical verification requires actual evidence text");
        window.RecordReview("Fixture course author","Checked the fictional arithmetic and its limits.",true,"Fixture evidence: independent R4.6.2 calculation reviewed; actual code/result recorded by the author, not executed by this editor.");
        var numerical=window.Document.GetItems("questions").Single(row=>S(row,"id")==id);window.PublishBuiltinTo(repo);
        Check(S(numerical["verification"],"status")=="Author-recorded numerical check"&&S(numerical["verification"],"evidence").Contains("not executed"),"Explicit native numerical review records author evidence and permits publication without auto-executing R");
        bool checkboxSeen=false;using(var timer=new System.Windows.Forms.Timer{Interval=40})
        {
            int ticks=0;
            timer.Tick+=(_,_)=>
            {
                var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(item=>item.Text=="Record author review");
                if(dialog==null){if(++ticks>100){timer.Stop();Check(false,"Author review dialog opened for default-checkbox inspection");}return;}
                IEnumerable<Control> Descendants(Control control){foreach(Control child in control.Controls){yield return child;foreach(Control nested in Descendants(child))yield return nested;}}
                var boxes=Descendants(dialog).OfType<CheckBox>().ToArray();checkboxSeen=boxes.Length==1&&!boxes[0].Checked;timer.Stop();dialog.DialogResult=DialogResult.Cancel;dialog.Close();
            };
            timer.Start();typeof(TutorEditorForm).GetMethod("RecordAuthorReview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
        }
        Check(checkboxSeen,"Author-review dialog opens with independent numerical checking unchecked");
        string preferencePath=Path.Combine(fixture,"Settings","authoring-preferences.json");Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);
        var preferences=new JsonObject{["websiteZipPath"]=Path.Combine(fixture,"Existing website destination.zip"),["statsDirectSourceRoot"]=repo,["futureSetting"]=new JsonObject{["preserve"]="unchanged"}};
        File.WriteAllText(preferencePath,preferences.ToJsonString());
        var publishMenu=window.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().SelectMany(parent=>parent.DropDownItems.OfType<ToolStripMenuItem>()).Single(item=>item.Text=="&Publish built-in tutor for StatsDirect builds…");
        publishMenu.PerformClick();
        Check(window.LastError==null&&window.LastBuiltinPublish?.OutputDirectory==output,"The actual File Publish command uses the remembered source repository without a folder prompt");
        var savedPreferences=JsonNode.Parse(File.ReadAllText(preferencePath))!;
        Check(S(savedPreferences,"websiteZipPath")==S(preferences,"websiteZipPath")&&S(savedPreferences["futureSetting"],"preserve")=="unchanged"&&S(savedPreferences,"statsDirectSourceRoot")==repo,"Remembering the publish destination preserves the website ZIP location and unknown authoring preferences");
        Check(Hash(source)==sourceHash,"The complete native built-in publication workflow leaves its authoring file unsaved until the user chooses Save");
        window.Close();
    }
    private static void CheckActiveGridSaves()
    {
        var course=(JsonObject)form.Document.Content.DeepClone();
        course["lessons"]![0]!["teachingPrompts"]=new JsonArray(new JsonObject{["level"]="beginner",["prompt"]="Original teaching prompt",["expectedReasoning"]="Original expected reasoning"});
        string path=Path.Combine(fixture,"active-grid-course.json");File.WriteAllText(path,course.ToJsonString());
        using var window=new TutorEditorForm(path,fixture,externalLinkLauncher:_=>{}){SuppressDialogs=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,0),Size=new Size(1500,1000)};
        window.Show();
        DataGridView PrivateGrid(string name)=>(DataGridView)typeof(TutorEditorForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        void EditWithoutLeaving(DataGridView grid,int row,int column,string text,string label)
        {
            Check(!window.HasUnsavedChanges,label+" begins from a saved source");
            grid.CurrentCell=grid.Rows[row].Cells[column];
            if(!grid.BeginEdit(true)||grid.EditingControl is not TextBox input)throw new InvalidOperationException(label+" did not enter native cell editing.");
            input.Text=text;
            Check(grid.IsCurrentCellInEditMode&&window.HasUnsavedChanges,label+" marks unsaved changes while its text editor remains active");
            // Save directly, without focusing another control or ending the cell.
            window.SaveCourse();
            Check(!window.HasUnsavedChanges,label+" Save flushes the active cell and clears the unsaved indicator");
        }
        window.SelectItem("questions","question-1");EditWithoutLeaving(PrivateGrid("options"),0,1,"Typed answer without leaving cell","Answer-choice cell");
        var saved=JsonNode.Parse(File.ReadAllText(path))!;
        Check(S(saved["questions"]![0]!["options"]![0],"text")=="Typed answer without leaving cell","Save persists exact active answer-choice text without a focus change");
        window.SelectItem("lessons","lesson-1");EditWithoutLeaving(PrivateGrid("references"),0,2,"Typed reference scope without leaving cell","Reference-source cell");
        saved=JsonNode.Parse(File.ReadAllText(path))!;
        Check(S(saved["lessons"]![0]!["sources"]![0],"scope")=="Typed reference scope without leaving cell","Save persists exact active source-grid text without a focus change");
        var teaching=(Dictionary<string,DataGridView>)typeof(TutorEditorForm).GetField("teachingGrids",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        EditWithoutLeaving(teaching["teachingPrompts"],0,1,"Typed teaching prompt without leaving cell","Teaching-prompt cell");
        saved=JsonNode.Parse(File.ReadAllText(path))!;
        Check(S(saved["lessons"]![0]!["teachingPrompts"]![0],"prompt")=="Typed teaching prompt without leaving cell","Save persists exact active teaching-grid text without a focus change");
        window.Close();
    }
    private static void WritePreviewFixtures()
    {
        string output=Path.Combine(evidence,"TutorPreviewFixtures");Directory.CreateDirectory(output);
        var course=(JsonObject)form.Document.Content.DeepClone();
        var question=(JsonObject)course["questions"]![0]!;question["stem"]="Literal <script>alert('x')</script> & denominator question: what is 12 / 60?";question["correct"]="A";question["explanation"]="Divide the numerator by its stated denominator.";
        question["options"]=new JsonArray(new JsonObject{["id"]="A",["text"]="0.2",["feedback"]="Correct arithmetic: 12 / 60 = 0.2."},new JsonObject{["id"]="B",["text"]="5",["feedback"]="This inverts the requested fraction."});
        string script="x <- c(1, 2, 3)\nmean(x)\nplot(x, main = \"Example <chart>\")";
        course["lessons"]![0]!["r"]=script;course["lessons"]![0]!["summary"]="A lesson containing literal <script> tags & readable text.";
        string previewSource=Path.Combine(fixture,"preview-course.json");File.WriteAllText(previewSource,course.ToJsonString());
        using var window=new TutorEditorForm(previewSource,fixture,externalLinkLauncher:_=>{}){SuppressDialogs=true,ShowInTaskbar=false};
        window.SelectItem("questions","question-1");File.WriteAllText(Path.Combine(output,"question.html"),window.BuildPreviewHtml());
        window.SelectItem("lessons","lesson-1");File.WriteAllText(Path.Combine(output,"lesson.html"),window.BuildPreviewHtml());
        File.WriteAllText(Path.Combine(output,"expectation.json"),JsonSerializer.Serialize(new{correctID="A",wrongID="B",correctFeedback="Correct arithmetic: 12 / 60 = 0.2.",wrongFeedback="This inverts the requested fraction.",explanation="Divide the numerator by its stated denominator.",stem=S(question,"stem"),r=script},new JsonSerializerOptions{WriteIndented=true}));
        Check(File.Exists(Path.Combine(output,"question.html"))&&File.Exists(Path.Combine(output,"lesson.html")),"Actual native learner-preview HTML fixtures generated for independent browser checks");
    }
}
