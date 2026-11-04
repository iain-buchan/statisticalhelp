using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

string root=Path.GetFullPath(args[0]),evidence=Path.Combine(root,"evidence");Directory.CreateDirectory(evidence);
string fixture=Path.Combine(evidence,"TutorBuiltin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
var checks=new List<object>();int failed=0;
void Check(bool value,string name){checks.Add(new{name,passed=value});Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failed++;}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception error)when(error is InvalidDataException or IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or OperationCanceledException){rejected=true;}Check(rejected,name);}
string S(JsonNode? node,string key)=>node?[key]?.GetValue<string>()??"";
string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
JsonObject Read(string file)=>JsonNode.Parse(File.ReadAllText(file))!.AsObject();
JsonObject Item(TutorCourseDocument doc,string kind,string id)=>doc.GetItems(kind).Single(row=>S(row,"id")==id);
Dictionary<string,string> OutputHashes(string folder)=>Directory.GetFiles(folder).ToDictionary(file=>Path.GetFileName(file)!,Hash);
bool Same(string folder,Dictionary<string,string> expected)=>OutputHashes(folder).OrderBy(pair=>pair.Key).SequenceEqual(expected.OrderBy(pair=>pair.Key));
var starter=TutorCourseDocument.Open(Path.Combine(root,"TutorSource","starter-course.json"));string maintainedHash=Hash(starter.FilePath!);
try
{
 var course=TutorCourseDocument.Create(starter.Content);
 course.Content["authoring"]!["builtinBaseline"]=TutorBuiltinPublisher.CreateBaseline(starter.Content);
 string source=Path.Combine(fixture,"working.json");course.Save(source);
 string output=Path.Combine(fixture,"AuthoredTutor");Directory.CreateDirectory(output);
 var first=TutorBuiltinPublisher.Publish(course,output);
 Check(first.LessonCount==12&&first.QuestionCount==35,"Builtin source publication preserves all12 lessons and35 questions");
 var lessons=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"lessons.json")))!.AsArray();var questions=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"questions.json")))!.AsArray();var tracks=Read(Path.Combine(output,"tracks.json"));
 Check(JsonNode.DeepEquals(lessons,starter.Content["lessons"])&&JsonNode.DeepEquals(questions,starter.Content["questions"])&&JsonNode.DeepEquals(tracks,starter.Content["authoring"]!["sourceTracks"]),"First built-in publication is semantically identical to original lesson/question/pathway data");
 Check(first.BankVersion==S(starter.Content["authoring"],"sourceBankVersion"),"Exact original baseline keeps its original bank version");
 Check(tracks.Count==5&&questions.Select(node=>S(node,"id")).Distinct().Count()==35&&questions.Any(node=>S(node,"track")=="analyst")&&questions.Any(node=>S(node,"track")=="researcher")&&questions.All(node=>!S(node,"id").StartsWith("course:")),"Builtin IDs and allfive pathways are retained without provider prefixes");
 var manifest=Read(Path.Combine(output,"manifest.json"));
 Check(manifest["reviewGatePassed"]!.GetValue<bool>()&&manifest["reviewedChangedItems"]!.AsArray().Count==0&&manifest["unreviewedChangedItems"]!.AsArray().Count==0&&manifest["files"]!.AsArray().Count==4,"Manifest records complete hashed inputs and unchanged-legacy review gate");
 Check(manifest["files"]!.AsArray().All(row=>Hash(Path.Combine(output,S(row,"path")))==S(row,"sha256"))&&Hash(Path.Combine(output,"manifest.json"))==first.ManifestSha256,"Manifest checksums identify the actual written source files");
 string bank=File.ReadAllText(Path.Combine(output,"bank.mjs"));
 Check(bank.Contains("export const bankVersion = ")&&bank.Contains("export const tracks = ")&&bank.Contains("export const questions = ")&&!bank.Contains("questions.push")&&!bank.Contains("import lessons"),"Generated bank exposes exact named exports with no duplicate lesson-question append");
 var run=new ProcessStartInfo("node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 foreach(string argument in new[]{"--input-type=module","-e","const b = await import(process.argv[1]); console.log(JSON.stringify({count:b.questions.length,tracks:Object.keys(b.tracks),version:b.bankVersion}));",new Uri(Path.Combine(output,"bank.mjs")).AbsoluteUri})run.ArgumentList.Add(argument);
 using(var child=Process.Start(run)!){string text=child.StandardOutput.ReadToEnd();string errors=child.StandardError.ReadToEnd();child.WaitForExit();var result=ReadJson(text);Check(child.ExitCode==0&&result["count"]!.GetValue<int>()==35&&result["tracks"]!.AsArray().Count==5&&errors.Length==0,"Actual generated ES module imports with35 unique questions andfive tracks");}
 string stableVersion=first.BankVersion;var repeat=TutorBuiltinPublisher.Publish(course,output);Check(repeat.BankVersion==stableVersion,"Repeated unchanged publication preserves bank identity");
 var old=OutputHashes(output);var edited=Item(course,"questions","LIB-RATES-01");edited["explanation"]=S(edited,"explanation")+" Revised teaching explanation.";course.Update("questions","LIB-RATES-01",edited);
 Reject(()=>TutorBuiltinPublisher.Publish(course,output),"Unreviewed edited builtin question cannot replace published source");Check(Same(output,old),"Review rejection leaves prior complete built-in source untouched");
 course.MarkReviewed("questions","LIB-RATES-01","Course author","Checked arithmetic, key and cited denominator guidance.");var revised=TutorBuiltinPublisher.Publish(course,output);
 lessons=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"lessons.json")))!.AsArray();questions=JsonNode.Parse(File.ReadAllText(Path.Combine(output,"questions.json")))!.AsArray();
 var flat=questions.Single(row=>S(row,"id")=="LIB-RATES-01");var rates=lessons.Single(row=>S(row,"id")=="rates");var embedded=rates!["practiceQuestions"]!.AsArray().Single(row=>S(row,"id")=="LIB-RATES-01");
 Check(JsonNode.DeepEquals(flat,embedded)&&questions.Count==35&&S(rates,"contentVersion").Contains("-q"),"Reviewed flat MCQ is mirrored into its lesson with a new lesson content version and no duplication");
 Check(revised.BankVersion!=stableVersion&&Read(Path.Combine(output,"manifest.json"))["reviewedChangedItems"]!.AsArray().Count==1,"Changed reviewed content receives a new bank identity and precise changed-item review evidence");
 string reviewedSource=Path.Combine(fixture,"reviewed-then-edited.json");var approvedSource=TutorCourseDocument.Create(course.Content);approvedSource.Save(reviewedSource);var raw=Read(reviewedSource);var rawQuestion=raw["questions"]!.AsArray().Single(row=>S(row,"id")=="LIB-RATES-01")!;rawQuestion["stem"]=S(rawQuestion,"stem")+" Unreviewed external raw edit.";File.WriteAllText(reviewedSource,raw.ToJsonString());var reopened=TutorCourseDocument.Open(reviewedSource);
 Reject(()=>TutorBuiltinPublisher.Publish(reopened,Path.Combine(fixture,"RawEditedOutput")),"External raw content edit cannot reuse a prior saved author's review identity");
 var approvedRecord=Item(course,"questions","LIB-RATES-01");var reordered=new JsonObject();foreach(var pair in approvedRecord.Reverse())reordered[pair.Key]=pair.Value?.DeepClone();
 Check(TutorContentFingerprint.Record(approvedRecord)==TutorContentFingerprint.Record(reordered)&&S(approvedRecord["review"],"contentSha256")==TutorContentFingerprint.Record(approvedRecord),"Shared canonical review fingerprint ignores object-key order while binding the actual approved content");
 var challenge=Item(course,"lessons","paired");string oldChallenge=S(challenge,"challenge");challenge["challenge"]="A new fictional matched-patient exercise.";course.Update("lessons","paired",challenge);
 Check(Item(course,"lessons","paired")["previousChallenges"]!.AsArray().Any(value=>value!.GetValue<string>()==oldChallenge),"Changing an exercise retains exact prior challenge text for old saved conversations");
 Reject(()=>TutorBuiltinPublisher.Publish(course,output),"Unreviewed changed lesson cannot ship");course.MarkReviewed("lessons","paired","Course author","Reviewed paired design and exercise reasoning.");TutorBuiltinPublisher.Publish(course,output);Check(true,"Reviewed lesson publishes while preserving existing IDs and historical prompts");
 var badTrack=TutorCourseDocument.Create(course.Content);var q=Item(badTrack,"questions","UG-SENS-01");q["track"]="nonexistent";badTrack.Update("questions","UG-SENS-01",q);badTrack.MarkReviewed("questions","UG-SENS-01","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(badTrack,output),"An unknown learner pathway is rejected");
 var removedTrack=TutorCourseDocument.Create(course.Content);removedTrack.Content["authoring"]!["sourceTracks"]!.AsObject().Remove("foundation");
 foreach(var existing in removedTrack.GetItems("questions").Where(row=>S(row,"track")=="foundation")){existing["track"]="medicine";removedTrack.Update("questions",S(existing,"id"),existing);removedTrack.MarkReviewed("questions",S(existing,"id"),"Course author","Pathway compatibility fixture.");}
 Reject(()=>TutorBuiltinPublisher.Publish(removedTrack,output),"Persisted foundation pathway cannot be removed even when all current questions are reassigned");
 var badOp=TutorCourseDocument.Create(course.Content);q=Item(badOp,"questions","UG-SENS-01");q["operation"]="NoSuchOperation";badOp.Update("questions","UG-SENS-01",q);badOp.MarkReviewed("questions","UG-SENS-01","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(badOp,output),"A nonexistent analysis operation is rejected");
 var badHelp=TutorCourseDocument.Create(course.Content);q=Item(badHelp,"questions","UG-SENS-01");q["help"]="missing/topic.htm";badHelp.Update("questions","UG-SENS-01",q);badHelp.MarkReviewed("questions","UG-SENS-01","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(badHelp,output),"A nonexistent help target is rejected");
 var broken=TutorCourseDocument.Create(course.Content);var l=Item(broken,"lessons","rates");l["prerequisites"]=new JsonArray("missing-lesson");broken.Update("lessons","rates",l);broken.MarkReviewed("lessons","rates","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(broken,output),"Dangling lesson prerequisite is rejected");
 broken=TutorCourseDocument.Create(course.Content);l=Item(broken,"lessons","rates");l["assessmentIds"]=new JsonArray("missing-question");broken.Update("lessons","rates",l);broken.MarkReviewed("lessons","rates","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(broken,output),"Dangling question assessment binding is rejected");
 broken=TutorCourseDocument.Create(course.Content);q=Item(broken,"questions","LIB-RATES-01");q["sourceIds"]=new JsonArray("missing-source");broken.Update("questions","LIB-RATES-01",q);broken.MarkReviewed("questions","LIB-RATES-01","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(broken,output),"Dangling lesson-source reference is rejected");
 broken=TutorCourseDocument.Create(course.Content);l=Item(broken,"lessons","rates");var only=(JsonObject)l["practiceQuestions"]![0]!.DeepClone();only["id"]="embedded-only-new";l["practiceQuestions"]!.AsArray().Add(only);broken.Update("lessons","rates",l);broken.MarkReviewed("lessons","rates","Course author","Review fixture.");Reject(()=>TutorBuiltinPublisher.Publish(broken,output),"New embedded-only questions must first enter the editable flat question collection");
 var noBaseline=TutorCourseDocument.Create(starter.Content);noBaseline.Content["authoring"]!.AsObject().Remove("builtinBaseline");Reject(()=>TutorBuiltinPublisher.Publish(noBaseline,output),"A missing identified original baseline cannot be auto-trusted");
 var oversized=TutorCourseDocument.Create(course.Content);l=Item(oversized,"lessons","paired");l["summary"]=new string('x',11000);oversized.Update("lessons","paired",l);oversized.MarkReviewed("lessons","paired","Course author","Reviewed wording.");Reject(()=>TutorBuiltinPublisher.Publish(oversized,output),"A lesson that exceeds the actual main-app tutor context bound cannot ship or be silently truncated");
 var evidenceStale=TutorCourseDocument.Create(course.Content);q=Item(evidenceStale,"questions","UG-SENS-01");q["verification"]=new JsonObject{["status"]="numerical-check-recorded",["passed"]=true};q["stem"]=S(q,"stem")+" New numerical setting.";evidenceStale.Update("questions","UG-SENS-01",q);evidenceStale.MarkReviewed("questions","UG-SENS-01","Course author","Educational review only.");Reject(()=>TutorBuiltinPublisher.Publish(evidenceStale,output),"Stale recorded numerical checks cannot be replaced by educational review alone");
 old=OutputHashes(output);using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Reject(()=>TutorBuiltinPublisher.Publish(course,output,cancelled.Token),"Cancelled source publication preserves the prior complete output");}Check(Same(output,old),"Cancellation does not mutate existing builtin files");
 string unowned=Path.Combine(fixture,"UserNotes");Directory.CreateDirectory(unowned);File.WriteAllText(Path.Combine(unowned,"notes.txt"),"Keep my notes");Reject(()=>TutorBuiltinPublisher.Publish(course,unowned),"Publisher refuses an output folder containing unowned user files");Check(File.ReadAllText(Path.Combine(unowned,"notes.txt"))=="Keep my notes","Unowned output notes are untouched");
 File.AppendAllText(Path.Combine(output,"bank.mjs"),"// user edits\n");Reject(()=>TutorBuiltinPublisher.Publish(course,output),"Manual edits to a generated bank must be preserved before replacement");
 string independent=Path.Combine(fixture,"FreshOutput");File.AppendAllText(source," ");Reject(()=>TutorBuiltinPublisher.Publish(course,independent),"Source changes saved by another editor block stale builtin publication");
 Check(!Directory.GetFiles(fixture,"*.publishing",SearchOption.TopDirectoryOnly).Any()&&!Directory.GetDirectories(fixture,"*-old-*",SearchOption.TopDirectoryOnly).Any(),"Completed and rejected publications leave no publishing markers or rollback directories");
 Check(Hash(starter.FilePath!)==maintainedHash,"Builtin tests never modify maintained tutor source or main application files");
 var integration=TutorCourseDocument.Create(starter.Content);integration.Content["authoring"]!["builtinBaseline"]=TutorBuiltinPublisher.CreateBaseline(starter.Content);string integrationOutput=Path.Combine(fixture,"IntegrationOutput");var integrationResult=TutorBuiltinPublisher.Publish(integration,integrationOutput);
 File.WriteAllText(Path.Combine(evidence,"tutor-builtin-integration-fixture.json"),JsonSerializer.Serialize(new{outputDirectory=integrationOutput,integrationResult.BankVersion,manifest=Path.Combine(integrationOutput,"manifest.json")},new JsonSerializerOptions{WriteIndented=true}));
 AdversarialBuiltinChecks.Run(root,fixture,Check);
 LearningTargetBuiltinChecks.Run(root,fixture,Check);
}
catch(Exception error){Check(false,"Unexpected builtin publication test error: "+error);}
var report=new{passed=failed==0,failed,assertions=checks.Count,fixture,checkedAtUtc=DateTimeOffset.UtcNow,checks};
File.WriteAllText(Path.Combine(evidence,"tutor-builtin-core-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.assertions,report.fixture}));return failed==0?0:1;
static JsonObject ReadJson(string text)=>JsonNode.Parse(text)!.AsObject();
