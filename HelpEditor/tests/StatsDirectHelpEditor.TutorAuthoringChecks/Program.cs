using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;
using StatsDirect.UI.Learning;

string root=Path.GetFullPath(args[0]);
string evidence=Path.Combine(root,"evidence");Directory.CreateDirectory(evidence);
string fixture=Path.Combine(evidence,"TutorAuthoring-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
var checks=new List<object>();int failed=0;
void Check(bool value,string name){checks.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failed++;}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception error)when(error is InvalidDataException or IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException){rejected=true;}Check(rejected,name);}
string Digest(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
string Text(JsonNode node,string key)=>node?[key]?.GetValue<string>()??"";
bool Errors(TutorCourseDocument course)=>course.Validate().Any(issue=>issue.Severity=="Error");
JsonObject Load(string file)=>(JsonObject)JsonNode.Parse(File.ReadAllText(file));
string starter=Path.Combine(root,"TutorSource","starter-course.json");string originalHash=Digest(starter);
try
{
 var original=TutorCourseDocument.Open(starter);
 Check(original.GetItems("lessons").Count==12&&original.GetItems("questions").Count==35,"Independent starter imports all12 current lessons and35 original questions");
 Check(!Errors(original),"Complete starter passes authoring runtime validation");
 Check(original.Validate().Any(issue=>issue.Severity=="Warning"&&issue.Path.EndsWith(".review")),"Imported AI-assisted draft status is not presented as expert approval");
 string file=Path.Combine(fixture,"working-course.json");var course=TutorCourseDocument.Create(original.Content);course.Save(file);
 var saved=TutorCourseDocument.Open(file);string initialVersion=Text(saved.Content,"version");
 saved.Content["extraProviderData"]=new JsonObject{["retained"]="unknown metadata"};
 var lesson=saved.GetItems("lessons")[0];string lessonId=Text(lesson,"id");lesson["summary"]="An edited explanation for health students.";lesson["extraLessonData"]=42;
 saved.Update("lessons",lessonId,lesson);saved.Save();
 Check(Text(saved.Content,"version")!=initialVersion,"Saving edited course content advances the course key version");
 Check(Text(saved.GetItems("lessons")[0]["review"],"status").Contains("Draft"),"Editing a reviewed lesson invalidates its review status");
 string exported=Path.Combine(fixture,"course-pack.json");saved.Export(exported);var runtime=CoursePack.Validate(Load(exported));
 Check(runtime["lessons"].AsArray().Count==12&&runtime["questions"].AsArray().Count==35,"Export passes the exact current Windows runtime CoursePack.Validate");
 Check(runtime["authoring"]==null&&runtime["lessons"][0]["authoring"]==null&&Text(runtime["extraProviderData"],"retained")=="unknown metadata"&&runtime["lessons"][0]["extraLessonData"].GetValue<int>()==42,"Runtime export removes private authoring state while retaining unknown course and lesson fields");
 var draftExport=TutorCourseDocument.Open(file);var firstDraft=draftExport.GetItems("lessons")[0];firstDraft["summary"]="First unsaved exported exercise.";draftExport.Update("lessons",lessonId,firstDraft);
 string firstOutput=Path.Combine(fixture,"unsaved-first.json"),secondOutput=Path.Combine(fixture,"unsaved-second.json");draftExport.Export(firstOutput);string firstExportVersion=Text(Load(firstOutput),"version");
 draftExport.Export(secondOutput);Check(Text(Load(secondOutput),"version")==firstExportVersion,"Repeated unchanged unsaved export keeps a stable course version");
 firstDraft["summary"]="Second different unsaved exported exercise.";draftExport.Update("lessons",lessonId,firstDraft);draftExport.Export(secondOutput);
 Check(Text(Load(secondOutput),"version")!=firstExportVersion,"Different unsaved exports receive distinct content-based course versions");
 string sourceBeforeExplicit=Digest(file);draftExport.Content["version"]="teacher-explicit-version";draftExport.Export(firstOutput);firstExportVersion=Text(Load(firstOutput),"version");firstDraft["summary"]="Third different unsaved exercise.";draftExport.Update("lessons",lessonId,firstDraft);draftExport.Export(secondOutput);
 Check(Text(Load(secondOutput),"version")!=firstExportVersion&&Digest(file)==sourceBeforeExplicit,"Explicit unsaved version plus follow-on edits still yields distinct exports without saving source");
 var question=saved.GetItems("questions")[0];string questionId=Text(question,"id");int beforeVersion=question["version"].GetValue<int>();question["explanation"]="An edited key explanation.";saved.Update("questions",questionId,question);
 Check(saved.GetItems("questions")[0]["version"].GetValue<int>()==beforeVersion+1&&Text(saved.GetItems("questions")[0]["review"],"status").Contains("Draft"),"Question edit advances its item version and invalidates prior review");
 int editedVersion=saved.GetItems("questions")[0]["version"].GetValue<int>();saved.MarkReviewed("questions",questionId,"Course author","Checked the key and independent numerical result.");
 Check(Text(saved.GetItems("questions")[0]["review"],"status")=="Reviewed by course author"&&saved.GetItems("questions")[0]["version"].GetValue<int>()==editedVersion,"Author review can be recorded without invalidating itself or changing the item version");
 var reviewOnly=saved.GetItems("questions")[0];reviewOnly["review"]["notes"]="Updated review notes only.";saved.Update("questions",questionId,reviewOnly);
 Check(Text(saved.GetItems("questions")[0]["review"],"status")=="Reviewed by course author"&&saved.GetItems("questions")[0]["version"].GetValue<int>()==editedVersion,"Review-only metadata edits preserve the review status and item version");
 var newReviewNotes=saved.GetItems("questions")[0];newReviewNotes["stem"]="A newly revised question with explicit check notes.";newReviewNotes["review"]["notes"]="Recheck the event denominator against the source.";saved.Update("questions",questionId,newReviewNotes);
 Check(Text(saved.GetItems("questions")[0]["review"],"notes")=="Recheck the event denominator against the source."&&saved.GetItems("questions")[0]["review"]?["reviewer"]==null&&Text(saved.GetItems("questions")[0]["review"],"status").Contains("Draft"),"Content edits retain review notes while clearing the prior reviewer claim and approval");
 var duplicate=saved.Duplicate("questions",questionId);Check(Text(duplicate,"id")!=questionId&&duplicate["options"].AsArray().Count==question["options"].AsArray().Count,"Duplicate produces a new question identity with its choices intact");
 var newLesson=saved.NewLesson();var newQuestion=saved.NewQuestion();var doc=saved.NewDocument();
 doc["title"]="Open educational source";doc["text"]="A course-author summary with attribution.";doc["url"]="https://www.cdc.gov/";doc["scope"]="Retained source scope";saved.Update("documents",Text(doc,"id"),doc);
 Check(saved.GetItems("lessons").Count==13&&saved.GetItems("questions").Count==37&&saved.GetItems("documents").Count==1,"Add lesson, original MCQ and attributed source document");
 Check(Text(newQuestion,"track")=="foundation","New original questions start on a valid built-in learner pathway");
 string newId=Text(newLesson,"id");saved.Move("lessons",newId,-1000);Check(Text(saved.GetItems("lessons")[0],"id")==newId,"Reorder clamps safely and retains every item");
 string deletion=saved.Delete("questions",questionId);saved.Save();saved=TutorCourseDocument.Open(file);
 Check(saved.DeletedItems().Any(item=>item.Id==deletion)&&saved.GetItems("questions").All(item=>Text(item,"id")!=questionId),"Deleted question recovery survives saving and reopening");
 saved.Restore(deletion);Check(saved.GetItems("questions").Any(item=>Text(item,"id")==questionId)&&saved.DeletedItems().All(item=>item.Id!=deletion),"Restore returns the exact recovered question and removes its recovery entry");
 string docDelete=saved.Delete("documents",Text(doc,"id"));saved.Restore(docDelete);Check(Text(saved.GetItems("documents")[0],"scope")=="Retained source scope","Source-document recovery preserves unknown fields");
 string collision=saved.Delete("questions",questionId);var collisionRecord=(JsonObject)question.DeepClone();saved.Content["questions"].AsArray().Add(collisionRecord);
 Reject(()=>saved.Restore(collision),"Recovery refuses to overwrite a current question with the same ID");saved.Content["questions"].AsArray().Remove(collisionRecord);saved.Restore(collision);
 saved.Save();saved.Export(exported);runtime=CoursePack.Validate(Load(exported));
 Check(runtime["questions"].AsArray().Count==37&&runtime["documents"].AsArray().Count==1&&runtime["authoring"]==null,"Expanded and restored course remains importable by actual runtime");
 string archiveBefore=Digest(exported);var invalid=saved.GetItems("questions")[0];string invalidId=Text(invalid,"id");invalid["correct"]="Z";saved.Update("questions",invalidId,invalid);
 Check(Errors(saved),"Invalid answer keys are identified before export");Reject(()=>saved.Export(exported),"Invalid answer key blocks runtime export");Check(Digest(exported)==archiveBefore,"Failed export preserves the previous complete pack");
 invalid["correct"]="A";invalid["options"][1]["id"]="A";saved.Update("questions",invalidId,invalid);Check(Errors(saved),"Duplicate option identifiers cannot be exported");
 invalid["options"][1]["id"]="B";invalid["options"][0]["feedback"]="";saved.Update("questions",invalidId,invalid);
 Check(saved.Validate().Any(issue=>issue.Severity=="Warning"&&issue.Path.EndsWith(".options")),"Missing optional per-option feedback gives a helpful warning");
 saved.Save();saved.Export(exported);CoursePack.Validate(Load(exported));Check(true,"Missing optional feedback remains compatible with existing runtime behavior");
 var unfinished=saved.GetItems("questions")[0];unfinished["stem"]="";saved.Update("questions",Text(unfinished,"id"),unfinished);saved.Save();
 Check(Errors(TutorCourseDocument.Open(file)),"Incomplete draft can be saved and resumed while export remains blocked");
 Reject(()=>saved.Export(file),"An incomplete draft cannot overwrite its source during export");
 var first=TutorCourseDocument.Open(file);var second=TutorCourseDocument.Open(file);first.Content["title"]="Changed by first editor";first.Save();string firstHash=Digest(file);second.Content["title"]="Changed by second editor";
 Reject(()=>second.Save(),"Stale editor save refuses to overwrite newer disk changes");Check(Digest(file)==firstHash&&Text(second.Content,"title")=="Changed by second editor","Conflict preserves both newer file and open unsaved draft");
 first.Save();string noChangeHash=Digest(file);DateTime timestamp=File.GetLastWriteTimeUtc(file);first.Save();Check(Digest(file)==noChangeHash&&File.GetLastWriteTimeUtc(file)==timestamp,"Unchanged save does not rewrite the source file");
 string locked=Path.Combine(fixture,"locked-export.json");File.WriteAllText(locked,"prior output");using(var hold=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None)){Reject(()=>original.Export(locked),"Locked destination preserves the prior output");}Check(File.ReadAllText(locked)=="prior output","Failed filesystem replacement retains old contents");
 var excessive=TutorCourseDocument.Create(original.Content);var q=excessive.GetItems("questions")[0];q["stem"]=new string('x',6001);excessive.Update("questions",Text(q,"id"),q);Check(Errors(excessive),"Runtime field size limits are enforced before student import");
 var links=TutorCourseDocument.Create(original.Content);links.Content["resourceURLs"]=new JsonArray("http://example.org");Check(Errors(links),"Insecure or invalid reference URLs cannot enter exported resource links");
 var ledger=TutorCourseDocument.Create(original.Content);ledger.Content["authoring"]!["researchLedger"]=new string('x',2_100_000);string ledgerFile=Path.Combine(fixture,"large-author-ledger.json");ledger.Save(ledgerFile);var reopenedLedger=TutorCourseDocument.Open(ledgerFile);string compactExport=Path.Combine(fixture,"compact-runtime.json");reopenedLedger.Export(compactExport);CoursePack.Validate(Load(compactExport));
 Check(new FileInfo(ledgerFile).Length>2_000_000&&new FileInfo(compactExport).Length<2_000_000&&reopenedLedger.Content["authoring"]!["researchLedger"]!.GetValue<string>().Length==2_100_000,"Large local research/recovery ledger survives authoring save while runtime export remains bounded and compact");
 Check(Digest(starter)==originalHash,"All authoring checks leave the maintained starter byte-identical");
 var sourceCheck=TutorCourseDocument.Create(original.Content);var sourcedLesson=sourceCheck.GetItems("lessons").First(row=>row["sources"] is JsonArray rows&&rows.Count>0);string sourcedId=Text(sourcedLesson,"id");var originalSource=(JsonObject)sourcedLesson["sources"]![0]!;string previousCheckedAt=Text(originalSource,"checkedAt");originalSource["url"]="https://www.cdc.gov/revised-reference";sourceCheck.Update("lessons",sourcedId,sourcedLesson);
 var changedSourceLesson=sourceCheck.GetItems("lessons").Single(row=>Text(row,"id")==sourcedId);
 Check(changedSourceLesson["sources"]![0]!["checkedAt"]==null&&Text(changedSourceLesson["authoring"]!["previousSourceChecks"]![0]!["previousSource"],"checkedAt")==previousCheckedAt,"Changing a cited source removes its stale check date and preserves prior source evidence locally");
 string examples=Path.Combine(root,"TutorExamples","health-biomed-draft-pack.json");
 if(File.Exists(examples))
 {
  string exampleHash=Digest(examples);var sample=TutorCourseDocument.Open(examples);
  Check(sample.GetItems("lessons").Count==4&&sample.GetItems("questions").Count==4&&!Errors(sample),"Health and biomedical example pack passes authoring validation");
  string sampleExport=Path.Combine(fixture,"biomedical-examples.json");sample.Export(sampleExport);CoursePack.Validate(Load(sampleExport));
  Check(Digest(examples)==exampleHash,"New original biomedical examples pass exact runtime import without modifying their source");
  sample.Content["questions"]![0]!["verification"]=new JsonObject{["status"]="numerical-check-recorded",["passed"]=true,["checkedAt"]="2026-11-03",["script"]="x <- 1 + 1"};
  var checkedQuestion=sample.GetItems("questions")[0];string checkedId=Text(checkedQuestion,"id");checkedQuestion["stem"]="An edited numerical exercise after earlier calculations.";sample.Update("questions",checkedId,checkedQuestion);
  var invalidated=sample.GetItems("questions")[0];
  Check(Text(invalidated["verification"],"status")=="not-rechecked-after-edit"&&invalidated["verification"]!["passed"]!.GetValue<bool>()==false&&invalidated["verification"]!["checkedAt"]==null&&invalidated["authoring"]!["previousVerification"]!["passed"]!.GetValue<bool>(),"Editing a checked question invalidates active numerical approval and preserves the exact prior evidence locally");
  Check(Text(sample.Content["verification"],"status")=="not-rechecked-after-edit"&&Text(sample.Content["authoring"]!["previousVerification"],"status")=="numerical-check-recorded","Course-level numerical verification is invalidated when lesson or question content changes");
  invalidated["verification"]!["status"]="numerical-check-recorded";invalidated["verification"]!["checkedAt"]="2026-11-04";sample.Update("questions",checkedId,invalidated);
  Check(Text(sample.GetItems("questions")[0]["verification"],"status")=="numerical-check-recorded","Explicit verification-only metadata updates can record a new author check without invalidating themselves");
  var added=(JsonObject)invalidated.DeepClone();added["id"]="new-authored-question";added["review"]=new JsonObject{["status"]="Claimed reviewed",["reviewer"]="Unverified model claim",["notes"]="Recheck the numerical denominator."};sample.AddRecord("questions",added);
  Check(Text(sample.GetItems("questions").Last()["verification"],"status")=="not-rechecked-after-edit"&&Text(sample.GetItems("questions").Last()["review"],"status").Contains("Draft")&&Text(sample.GetItems("questions").Last()["review"],"notes")=="Recheck the numerical denominator."&&sample.GetItems("questions").Last()["review"]!["reviewer"]==null,"New externally proposed records enter as unverified drafts with useful notes but no claimed reviewer");
 }
 AdversarialChecks.Run(root,fixture,Check);
 LearningTargetChecks.Run(root,fixture,Check);
}
catch(Exception error){Check(false,"Unexpected authoring test error: "+error);}
var report=new{passed=failed==0,failed,assertions=checks.Count,fixture,checkedAtUtc=DateTimeOffset.UtcNow,starterSha256=originalHash,sourceUntouched=Digest(starter)==originalHash,checks};
File.WriteAllText(Path.Combine(evidence,"tutor-authoring-core-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.assertions,report.fixture}));return failed==0?0:1;
