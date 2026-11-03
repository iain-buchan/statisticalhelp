using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static readonly List<object> checks = [];
    private static string root = "", fixture = "";
    private const string Source = "https://www.cdc.gov/csels/dsepd/ss1978/lesson3/section2.html";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--fake")
        {
            try { FakeAsync(args[1], args[2]).GetAwaiter().GetResult(); return 0; }
            catch (Exception error) { File.WriteAllText(args[2] + ".error.txt", error.ToString()); return 1; }
        }
        root = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        fixture = Path.Combine(root, "evidence", "TutorResearch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        try
        {
            if (args.Contains("--dialog")) DialogChecks();
            else if (args.Contains("--live-research")) LiveAsync().GetAwaiter().GetResult();
            else TestsAsync(args.Contains("--real-startup")).GetAwaiter().GetResult();
            Report(true); return 0;
        }
        catch (Exception error) { checks.Add(new { name = error.ToString(), passed = false }); Console.WriteLine("FAIL " + error); Report(false); return 1; }
    }
    private static void Check(bool passed, string name)
    { checks.Add(new { name, passed }); Console.WriteLine((passed ? "PASS " : "FAIL ") + name); if (!passed) throw new InvalidOperationException(name); }
    private static void Refuses(Action action, string name)
    { try { action(); throw new Exception("NOT REFUSED"); } catch (Exception error) when (error is IOException or ArgumentException) { Check(true, name); } }
    private static JsonObject Question() => JsonNode.Parse("""
        {"id":"UG-SENS-01","version":2,"topic":"Sensitivity","objective":"Choose the denominator for sensitivity.","stem":"A fictional study includes90 people with the condition and810 without it. The test identifies72 cases and81 false positives. What is sensitivity?","options":[{"id":"A","text":"20%","feedback":"This is the false-negative proportion."},{"id":"B","text":"47.1%","feedback":"This is positive predictive value."},{"id":"C","text":"80%","feedback":"72/90=0.8."},{"id":"D","text":"90%","feedback":"This is specificity."}],"correct":"C","hint":"Start with the people with the condition.","lesson":"Sensitivity conditions on disease status.","explanation":"Sensitivity =72/90=80%.","r":"stopifnot(72/90==0.8)","customMetadata":{"preserve":"Original author field"},"review":{"status":"Previously reviewed","reviewer":"Course author"}}
        """)!.AsObject();
    private static JsonObject Course() => new() { ["schemaVersion"] = 2, ["id"] = "research-check", ["version"] = "1.0", ["title"] = "Health statistics", ["learningObjectives"] = new JsonArray("Explain screening denominators"), ["authoring"] = new JsonObject { ["audience"] = "Health professionals" }, ["documents"] = new JsonArray(new JsonObject { ["id"] = "course-note", ["title"] = "Additional reading", ["text"] = "Attributed course note about denominators." }), ["lessons"] = new JsonArray(new JsonObject { ["id"] = "intro", ["title"] = "Study design", ["summary"] = "Use the correct denominator." }), ["questions"] = new JsonArray(Question()) };
    private static TutorResearchRequest Request(bool web = true) => new("questions", "refine", Question(), "Clarify this fictional screening example and check its denominator.", "Health and biomedicine undergraduates", web);
    private static string Reply(JsonObject? proposed = null, string source = Source) => JsonSerializer.Serialize(new { proposedRecordJson = (proposed ?? Question()).ToJsonString(), summary = "Clarified denominator.", statisticalNotes = "72/90=80%; numerical checks require author review. This is fictional educational material.", rValidationCode = "stopifnot(abs(72/90-0.8)<1e-12)", sources = new[] { new { title = "CDC epidemiology teaching", url = source, relevance = "Definitions of screening proportions and their denominators." } } });
    private static PolishChatGptClient Client(string scenario)
    {
        string path = Path.Combine(fixture, scenario + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        return new PolishChatGptClient(Environment.ProcessPath!, path, "--fake", scenario, Path.Combine(path, "protocol.json"));
    }
    private static async Task TestsAsync(bool realStartup)
    {
        var course = Course(); var context = TutorResearchContext.Create(course);
        Check(context.TutorItemCount == 3 && context.CatalogJson.Contains("UG-SENS-01") && context.CatalogJson.Contains("documents/0"), "Complete tutor catalog includes lessons, questions and attributed course readings");
        course["questions"]![0]!["stem"] = "Mutated after snapshot";
        Check(context.Execute("read_tutor_item", new() { ["path"] = "questions/0", ["offset"] = 0, ["length"] = 16000 }).Contains("fictional study"), "Read-only context is frozen against caller changes");
        Check(context.Execute("search_tutor", new() { ["query"] = "denominator", ["maxResults"] = 10 }).Contains("questions/0"), "Search reaches the full tutor corpus");
        Check(context.Execute("read_tutor_item", new() { ["path"] = "documents/0", ["offset"] = 0, ["length"] = 16000 }).Contains("Attributed course note"), "Author-added source documents can be read for context");
        Check(context.Execute("read_course_metadata", new() { ["offset"] = 0, ["length"] = 16000 }).Contains("Explain screening denominators"), "Course objectives and audience are available through a bounded read-only metadata tool");
        var helpProject = HelpProject.Open(Path.Combine(root, "HelpSource"));
        var helpSnapshot = HelpPolishSnapshot.Create("contents.htm", helpProject.ReadTopic("contents.htm"));
        var helpContext = HelpPolishContext.Create(helpProject, helpSnapshot);
        var fullContext = TutorResearchContext.Create(Course(), helpContext);
        Check(fullContext.HelpPageCount == helpProject.ListTopics().Count && fullContext.HelpPageCount == 404 && fullContext.CatalogJson.Contains("contents.htm"), "Research catalog exposes all 404 frozen help pages alongside the full tutor course");
        Check(fullContext.Execute("read_help_page", new() { ["path"] = "contents.htm", ["offset"] = 0, ["length"] = 16000 }).Contains("StatisticalHelp") && fullContext.Execute("search_help", new() { ["query"] = "variance", ["maxResults"] = 12 }).Contains("results"), "Research dispatcher serves existing help read/search tools without filesystem access");
        Refuses(() => context.Execute("read_tutor_item", new() { ["path"] = "../secrets", ["offset"] = 0, ["length"] = 100 }), "Context cannot read arbitrary file paths");
        Refuses(() => context.Execute("search_tutor", new() { ["query"] = "x", ["maxResults"] = 10, ["command"] = "run" }), "Unknown tool arguments are rejected");
        Refuses(() => context.Execute("exec", new()), "Execution is unavailable in research context");
        var plain = PolishChatGptClient.ParseTutorResearch(Request(), Reply(), []);
        Check(!plain.Sources[0].ObservedOpened && plain.EvidenceNote.Contains("no observed"), "A plausible source URL alone is never treated as opened or verified");
        Check(TutorResearchContext.Text(plain.ProposedRecord["review"], "status").Contains("draft") && TutorResearchContext.Text(plain.ProposedRecord["review"], "previousStatus") == "Previously reviewed", "AI content returns to draft and preserves earlier review provenance");
        var checkedQuestion = Question(); checkedQuestion["verification"] = new JsonObject { ["status"] = "passed", ["numericallyChecked"] = true };
        var uncheckedDraft = PolishChatGptClient.ParseTutorResearch(Request() with { SelectedRecord = checkedQuestion }, Reply(checkedQuestion), []);
        Check(uncheckedDraft.ProposedRecord["verification"]!["current"]!.GetValue<bool>() == false && uncheckedDraft.ProposedRecord["verification"]!["rCheckExecuted"]!.GetValue<bool>() == false && uncheckedDraft.ProposedRecord["verification"]!["priorEvidence"]!["status"]!.GetValue<string>() == "passed", "Previous numerical evidence is retained as history and is never inherited as a current passed check");
        Check(plain.BeforeJson == Question().ToJsonString() && plain.ProposedRecord["customMetadata"]!.ToJsonString() == Question()["customMetadata"]!.ToJsonString(), "Review retains exact frozen before record and unknown author fields");
        var opened = PolishChatGptClient.ParseTutorResearch(Request(), Reply(), [new("search", "screening", ""), new("openPage", "", Source, DateTimeOffset.UtcNow)]);
        Check(opened.Sources.Single().ObservedOpened && opened.Sources.Single().AccessedAtUtc != null, "Only observed page retrieval marks a source as opened with an access date");
        foreach (string invalid in new[] { "javascript:alert(1)", "file:///C:/data", "https://name:secret@example.org/", "http://example.org/" })
            Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(source: invalid), []), "Unsafe/non-HTTPS source URL refused: " + invalid);
        var changedId = Question(); changedId["id"] = "other"; Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(changedId), []), "Refinement cannot change the selected item identity");
        var dropped = Question(); dropped.Remove("customMetadata"); Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(dropped), []), "Refinement cannot silently discard original author fields");
        var nested = Question(); nested["customMetadata"] = new JsonObject(); Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(nested), []), "Refinement preserves nested author metadata rather than only top-level field names");
        var invalidOption = Question(); invalidOption["options"]![0]!["id"] = "Z"; Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(invalidOption), []), "MCQ option IDs obey runtime A-H constraints");
        var bound = Question(); bound["operation"] = "MiscDiagnostic"; bound["track"] = "foundation"; bound["help"] = "clinical_epidemiology/diagnostic.htm"; bound["lessonId"] = "diagnostic"; bound["sourceIds"] = new JsonArray("cdc-screening"); bound["assessmentIds"] = new JsonArray("UG-SENS-01"); bound["prerequisites"] = new JsonArray("epidemiology"); bound["practiceQuestions"] = new JsonArray(new JsonObject { ["id"] = "nested-id", ["stem"] = "Preserved nested question" });
        var rebound = (JsonObject)bound.DeepClone(); foreach (string key in new[] { "operation", "track", "help", "lessonId", "sourceIds", "assessmentIds", "prerequisites", "practiceQuestions" }) rebound.Remove(key);
        rebound["operation"] = "TUnpaired"; rebound["track"] = "researcher"; rebound["sourceIds"] = new JsonArray("unrelated-source");
        var protectedResult = PolishChatGptClient.ParseTutorResearch(Request() with { SelectedRecord = bound }, Reply(rebound), []);
        Check(new[] { "operation", "track", "help", "lessonId", "sourceIds", "assessmentIds", "prerequisites", "practiceQuestions" }.All(key => JsonNode.DeepEquals(bound[key], protectedResult.ProposedRecord[key])), "AI refinement cannot discard or change bundled runtime bindings and nested question ownership");
        var created = Question(); created["id"] = "NEW-SENS-01"; created["track"] = "foundation";
        var createRequest = Request() with { Mode = "create", SelectedRecord = new JsonObject() };
        Check(PolishChatGptClient.ParseTutorResearch(createRequest, Reply(created), []).ProposedRecord["track"]!.GetValue<string>() == "foundation", "A new question retains its explicit valid learner pathway");
        created["track"] = "invented-pathway";
        Refuses(() => PolishChatGptClient.ParseTutorResearch(createRequest, Reply(created), []), "New questions require one of the actual five pathways or shared core");
        var badKey = Question(); badKey["correct"] = "Z"; Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), Reply(badKey), []), "MCQ requires a correct key matching exactly one option");
        await AdversarialChecksAsync();
        using (var client = Client("normal"))
        {
            var result = await client.ResearchTutorAsync(context, Request());
            Check(client.IsSignedIn && result.ProposedRecord["id"]!.GetValue<string>() == "UG-SENS-01", "Authenticated mock protocol returns a validated proposal without source writes");
            Check(result.SearchActivity.Count == 2 && result.Sources.Single().ObservedOpened, "Only matching thread/turn web actions appear in browsing evidence");
            Check(result.ProposedRecord["review"]!["status"]!.GetValue<string>().Contains("draft"), "Forged model review approval cannot survive host validation");
        }
        using (var client = Client("no-search"))
        {
            var result = await client.ResearchTutorAsync(context, Request(false));
            Check(!result.WebResearchObserved && !result.Sources.Single().ObservedOpened, "Disabled web search never gets an invented evidence label");
        }
        using (var client = Client("search-only"))
        {
            var result = await client.ResearchTutorAsync(context, Request());
            Check(result.WebResearchObserved && result.SearchActivity.Count == 1 && !result.Sources.Single().ObservedOpened, "Search discovery and stale/foreign page events cannot be promoted into evidence that a cited page was opened");
        }
        using (var client = Client("cancel"))
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await client.ResearchTutorAsync(context, Request(), stop.Token); throw new Exception("NOT CANCELLED"); }
            catch (OperationCanceledException) { Check(client.Status.Contains("stopped"), "Stop cancels a running research request without a proposal"); }
            await Task.Delay(100);
        }
        var logs = Directory.GetFiles(fixture, "protocol.json", SearchOption.AllDirectories).Select(ReadProtocol).ToArray();
        Check(logs.Any(log => TutorResearchContext.Text(log, "scenario") == "normal" && log["live"]!.GetValue<bool>() && log["toolsRead"]!.GetValue<bool>()), "Research thread really requests live hosted search and serves read-only tutor tools");
        Check(logs.Any(log => TutorResearchContext.Text(log, "scenario") == "no-search" && !log["live"]!.GetValue<bool>()), "Per-request disabled search is sent to the server");
        Check(logs.Any(log => TutorResearchContext.Text(log, "scenario") == "cancel" && log["interrupted"]!.GetValue<bool>()), "Stop sends turn/interrupt to the provider");
        Check(PolishChatGptClient.IsolatedConfig.Contains("web_search = \"disabled\""), "Ordinary help polishing retains its original disabled-search configuration");
        if (realStartup)
        {
            using var client = new PolishChatGptClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await client.InitializeAsync(timeout.Token);
            var thread = await client.StartTutorResearchThreadAsync(fullContext, true, timeout.Token);
            Check(thread["thread"]?["id"]?.GetValue<string>().Length > 0, "Actual pinned runtime accepts ephemeral research config and tutor tools with no inference");
        }
    }
    private static void DialogChecks()
    {
        ApplicationConfiguration.Initialize(); var fake = new FakeClient(); var source = Question(); string original = source.ToJsonString();
        using var dialog = new TutorResearchDialog(TutorResearchContext.Create(Course()), source, "questions", fake, _ => { }) { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1150, 850) };
        dialog.Shown += async (_, _) =>
        {
            try
            {
                // Account initialization runs from Shown before the test continuation.
                await Task.Yield();
                var first = await dialog.ResearchAsync("Clarify this question", "Masters health students", "refine", true);
                Check(first.ProposedRecord["id"]!.GetValue<string>() == "UG-SENS-01" && dialog.AcceptedProposal == null, "Native dialog displays a proposal without automatically accepting it");
                await dialog.ResearchAsync("Explain the distractors more clearly", "Masters health students", "refine", true);
                Check(fake.LastRequest!.PreviousDiscussion.Contains("Clarify this question"), "A follow-up request includes the previous proposal and author instructions");
                Check(fake.LastRequest.PreviousDiscussion.Contains(Source) && fake.LastRequest.PreviousDiscussion.Contains("stopifnot"), "Follow-up context retains earlier source citations and the proposed R check, still labelled unverified/not run");
                Check(source.ToJsonString() == original, "Research and follow-up leave the caller's tutor record unchanged");
                Check(dialog.HumanReviewSummary.Contains("not independent") || dialog.HumanReviewSummary.Contains("not independently"), "Dialog review summary does not claim independent verification");
                var previous = dialog.Proposal; fake.HoldNext = true;
                var pending = dialog.ResearchAsync("Wait for cancellation", "Masters health students", "refine", true);
                dialog.StopResearch();
                try { await pending; throw new Exception("Expected dialog cancellation."); } catch (OperationCanceledException) { }
                Check(ReferenceEquals(dialog.Proposal, previous) && dialog.AcceptedProposal == null, "Native Stop preserves the earlier draft without accepting a partial reply");
                fake.FailNext = true;
                try { await dialog.ResearchAsync("Simulate provider error", "Masters health students", "refine", true); throw new Exception("Expected provider failure."); } catch (IOException) { }
                Check(ReferenceEquals(dialog.Proposal, previous) && dialog.LastError == "Mock provider failure", "Failed follow-up keeps the previous proposal and reports the error");
                fake.HoldNext = true; fake.IgnoreCancellation = true;
                var late = dialog.ResearchAsync("A slow provider ignoring Stop", "Masters health students", "refine", true);
                dialog.StopResearch(); fake.Held!.SetResult(true);
                try { await late; throw new Exception("Expected late reply suppression."); } catch (OperationCanceledException) { }
                Check(ReferenceEquals(dialog.Proposal, previous) && dialog.AcceptedProposal == null, "A provider reply arriving after Stop cannot replace or accept the earlier proposal");
                var disposableClient = new FakeClient { HoldNext = true, IgnoreCancellation = true };
                using (var disposableDialog = new TutorResearchDialog(TutorResearchContext.Create(Course()), source, "questions", disposableClient, _ => { }) { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0) })
                {
                    disposableDialog.Show(dialog);
                    var disposedReply = disposableDialog.ResearchAsync("Dispose while researching", "Masters health students", "refine", true);
                    disposableDialog.Dispose();
                    Check(disposableClient.LastCancellationToken.IsCancellationRequested, "Disposing an active research window cancels its request before controls are destroyed");
                    disposableClient.Held!.SetResult(true);
                    try { await disposedReply; throw new Exception("Expected disposed reply suppression."); } catch (OperationCanceledException) { }
                    Check(disposableDialog.Proposal == null && disposableDialog.AcceptedProposal == null && !disposableClient.Disposed, "A reply after disposal cannot mutate a dead dialog or dispose its caller-owned client");
                }
                dialog.AcceptProposal();
            }
            catch (Exception error) { checks.Add(new { name = error.ToString(), passed = false }); dialog.Close(); }
        };
        Application.Run(dialog);
        Check(dialog.AcceptedProposal != null, "Explicit acceptance returns one draft for parent validation");
        Check(!fake.Disposed, "The dialog does not dispose an injected client owned by its caller"); fake.Dispose();
    }
    private static async Task LiveAsync()
    {
        using var client = new PolishChatGptClient(); using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await client.InitializeAsync(stop.Token);
        if (!client.IsSignedIn) throw new InvalidOperationException("The Help Editor's existing ChatGPT connection is not signed in; no sign-in was started.");
        var request = Request() with { Instructions = "Refine this ONE small fictional screening MCQ. Use live web search and OPEN a CDC Principles of Epidemiology primary teaching page to check sensitivity versus positive predictive value. Keep the numerical values and one best answer; provide the simplest base-R arithmetic check. Explain which retrieved source supports the denominator. This is original practice material, not an official exam question." };
        var result = await client.ResearchTutorAsync(TutorResearchContext.Create(Course()), request, stop.Token);
        File.WriteAllText(Path.Combine(fixture, "live-proposal.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Check(result.WebResearchObserved, "Actual live turn reports hosted web-search activity");
        Check(result.Sources.Count > 0 && result.Sources.Any(source => source.ObservedOpened), "Actual live source citations include at least one observed page retrieval");
        Check(result.SearchActivity.Any(action => action.Url.Contains("cdc.gov", StringComparison.OrdinalIgnoreCase)), "Actual live research consulted a CDC primary source");
        Check(result.RValidationCode.Contains("72") || result.RValidationCode.Contains("0.8"), "Actual live proposal supplies a usable simple numerical R check");
        Check(result.BeforeJson == Question().ToJsonString(), "Live research never rewrites the selected source record");
    }
    private static void Report(bool passed)
    {
        var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = passed && !checks.Any(item => item.GetType().GetProperty("passed")?.GetValue(item) is false), count = checks.Count, fixture, checks };
        string reportPath = Path.Combine(fixture, "checks.json"); File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.count, reportPath }));
    }
    private static async Task FakeAsync(string scenario, string path)
    {
        var state = new JsonObject { ["scenario"] = scenario, ["live"] = false, ["toolsRead"] = false, ["interrupted"] = false, ["threadRequested"] = false, ["turnRequested"] = false, ["unsubscribed"] = false };
        object? replyId = null; string thread = "tutor-thread", turn = "tutor-turn";
        void Snapshot()
        {
            // Polling uses shared reads and retries an incomplete JSON snapshot;
            // it must never lock out this mock's next observation write.
            File.WriteAllText(path, state.ToJsonString());
        }
        async Task AfterCancellationAsync()
        {
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(path + ".release"))
            {
                if (timeout.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Test did not release the late acknowledgement after cancelling.");
                await Task.Delay(10);
            }
        }
        async Task Send(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
        async Task Complete()
        {
            var proposed = Question(); proposed["review"] = new JsonObject { ["status"] = "Independently verified and accredited" };
            await Send(new { method = "item/completed", @params = new { threadId = thread, turnId = turn, item = new { type = "agentMessage", id = "message", text = Reply(proposed) } } });
            await Send(new { method = "turn/completed", @params = new { threadId = thread, turn = new { id = turn, status = "completed" } } });
        }
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var value = JsonNode.Parse(line)!; string method = TutorResearchContext.Text(value, "method"); var parameters = value["params"];
            object? id = value["id"]?.DeepClone();
            if (method.Length == 0 && value["id"]?.GetValue<int>() == 9001)
            {
                state["toolsRead"] = value["result"]?["success"]?.GetValue<bool>() == true;
                await Send(new { id = 9002, method = "item/tool/call", @params = new { threadId = thread, turnId = turn, callId = "read-1", tool = "read_tutor_item", arguments = new { path = "questions/0", offset = 0, length = 16000 } } });
            }
            else if (method.Length == 0 && value["id"]?.GetValue<int>() == 9002) await Complete();
            else if (method == "initialize") await Send(new { id, result = new { } });
            else if (method == "account/read") await Send(new { id, result = new { account = new { type = "chatgpt" } } });
            else if (method == "thread/start")
            {
                state["threadRequested"] = true; Snapshot();
                if (scenario == "late-thread") await AfterCancellationAsync();
                state["live"] = TutorResearchContext.Text(parameters?["config"], "web_search") == "live";
                await Send(new { id, result = new { thread = new { id = thread } } });
            }
            else if (method == "turn/start")
            {
                state["turnRequested"] = true; Snapshot();
                if (scenario == "late-turn") await AfterCancellationAsync();
                replyId = id; await Send(new { id, result = new { turn = new { id = turn, status = "inProgress" } } });
                await Send(new { method = "turn/started", @params = new { threadId = thread, turn = new { id = turn } } });
                if (scenario is not ("cancel" or "late-thread" or "late-turn" or "dispose"))
                {
                    if (scenario != "no-search")
                    {
                        await Send(new { method = "item/completed", @params = new { threadId = "wrong-thread", turnId = turn, item = new { type = "webSearch", id = "wrong", query = "private", action = new { type = "openPage", url = "https://wrong.example/" } } } });
                        await Send(new { method = "item/completed", @params = new { threadId = thread, turnId = "previous-turn", item = new { type = "webSearch", id = "stale", query = "private", action = new { type = "openPage", url = Source } } } });
                        await Send(new { method = "item/completed", @params = new { threadId = thread, turnId = turn, item = new { type = "webSearch", id = "search", query = "CDC sensitivity", action = new { type = "search", query = "CDC sensitivity" } } } });
                        if (scenario != "search-only") await Send(new { method = "item/completed", @params = new { threadId = thread, turnId = turn, item = new { type = "webSearch", id = "opened", query = "", action = new { type = "openPage", url = Source } } } });
                    }
                    await Send(new { id = 9001, method = "item/tool/call", @params = new { threadId = thread, turnId = turn, callId = "search-1", tool = "search_tutor", arguments = new { query = "sensitivity", maxResults = 10 } } });
                }
            }
            else if (method == "turn/interrupt") { state["interrupted"] = true; await Send(new { id, result = new { } }); }
            else if (method == "thread/unsubscribe") { state["unsubscribed"] = true; await Send(new { id, result = new { } }); }
            else if (method == "account/login/cancel") await Send(new { id, result = new { } });
            Snapshot();
        }
    }
    private static JsonNode ReadProtocol(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!;
    }
    private sealed class FakeClient : ITutorResearchChatGptClient
    {
        public bool IsSignedIn => true; public string Status => "Connected to mock ChatGPT"; public string? LoginUrl => null; public bool Disposed; public event Action? Changed;
        public bool HoldNext, FailNext, IgnoreCancellation;
        public TaskCompletionSource<bool>? Held;
        public CancellationToken LastCancellationToken;
        public TutorResearchRequest? LastRequest;
        public Task InitializeAsync(CancellationToken cancellationToken = default) { Changed?.Invoke(); return Task.CompletedTask; }
        public Task<string?> SignInAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task CancelSignInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HelpPolishReview> PolishAsync(HelpPolishContext context, HelpPolishSnapshot snapshot, string instructions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<TutorResearchProposal> ResearchTutorAsync(TutorResearchContext context, TutorResearchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request; LastCancellationToken = cancellationToken;
            if (HoldNext)
            {
                HoldNext = false;
                if (IgnoreCancellation) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously); await Held.Task; }
                else await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (FailNext) { FailNext = false; throw new IOException("Mock provider failure"); }
            return PolishChatGptClient.ParseTutorResearch(request, Reply(), []);
        }
        public void Dispose() => Disposed = true;
    }
}
