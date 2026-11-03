using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;

internal static partial class Program
{
    private static JsonObject RichLesson() => JsonNode.Parse("""
        {"id":"paired","title":"Paired observations","objective":"Analyse within-person changes.","summary":"Use differences between paired measurements.","steps":"Match observations by person.","challenge":"Explain the direction and uncertainty of change.","r":"d <- c(1,2,3); t.test(d)","operation":"TPaired","help":"parametric_methods/ttest_paired.htm","columns":[{"title":"Before","values":[100,120,110]},{"title":"After","values":[105,124,112]}],"learningObjectives":["Describe pairing","Check differences"],"keyConcepts":[{"term":"Pairing","explanation":"Repeated measurements in the same person.","authorNote":{"retain":"Local teaching note"}}],"review":{"status":"Draft"}}
        """)!.AsObject();
    private static async Task AdversarialChecksAsync()
    {
        var lesson = RichLesson(); var editedLesson = (JsonObject)lesson.DeepClone(); editedLesson["summary"] = "Use each person's difference, retaining the stated measurement direction.";
        var lessonRequest = new TutorResearchRequest("lessons", "refine", lesson, "Clarify the summary.", "Medical students", false);
        var reviewedLesson = PolishChatGptClient.ParseTutorResearch(lessonRequest, Reply(editedLesson), []);
        Check(reviewedLesson.ProposedRecord["learningObjectives"]!.ToJsonString() == lesson["learningObjectives"]!.ToJsonString() && reviewedLesson.ProposedRecord["columns"]!.ToJsonString() == lesson["columns"]!.ToJsonString(), "Enriched lesson refinement preserves string objective arrays and numeric example columns");
        var droppedNested = (JsonObject)editedLesson.DeepClone(); droppedNested["keyConcepts"]![0]!.AsObject().Remove("authorNote");
        Refuses(() => PolishChatGptClient.ParseTutorResearch(lessonRequest, Reply(droppedNested), []), "Lesson refinement cannot discard nested local teaching metadata");
        string builtinPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos", "statsdirect", "Learning", "upstream", "Learn", "lessons.json");
        if (File.Exists(builtinPath))
        {
            var builtinLessons = JsonNode.Parse(File.ReadAllText(builtinPath))!.AsArray();
            foreach (var source in builtinLessons.OfType<JsonObject>())
            {
                string original = source.ToJsonString(); var revised = (JsonObject)source.DeepClone(); revised["summary"] = TutorResearchContext.Text(source, "summary") + " Teaching clarity check.";
                var parsedLesson = PolishChatGptClient.ParseTutorResearch(new("lessons", "refine", source, "Clarify this original teaching summary", "Medical students", false), Reply(revised), []);
                Check(source.ToJsonString() == original && parsedLesson.ProposedRecord["id"]!.GetValue<string>() == TutorResearchContext.Text(source, "id"), "Current bundled lesson with real enriched arrays refines without source mutation: " + TutorResearchContext.Text(source, "id"));
            }
        }
        var invalidId = Question(); invalidId["id"] = "NEW-QUESTION\n"; invalidId["track"] = "core";
        Refuses(() => PolishChatGptClient.ParseTutorResearch(Request() with { Mode = "create", SelectedRecord = new() }, Reply(invalidId), []), "An item identifier cannot contain a trailing newline");
        var duplicateJson = JsonSerializer.Serialize(new { proposedRecordJson = "{\"id\":\"NEW-DUP\",\"track\":\"core\",\"stem\":\"Question\",\"explanation\":\"Answer\",\"options\":[{\"id\":\"A\",\"text\":\"A\"},{\"id\":\"B\",\"text\":\"B\"}],\"correct\":\"A\",\"extra\":{\"claim\":\"first\",\"claim\":\"second\"}}", summary = "Draft", statisticalNotes = "Review needed", rValidationCode = "", sources = Array.Empty<object>() });
        Refuses(() => PolishChatGptClient.ParseTutorResearch(Request() with { Mode = "create", SelectedRecord = new() }, duplicateJson, []), "Ambiguous nested duplicate JSON properties are rejected before review");
        foreach (string malformed in new[] { "```json\n{}\n```", "null", "[]", "{}", Reply().Replace("\"sources\":[", "\"sources\":null,\"extra\":["), Reply().Replace("\"summary\":\"Clarified denominator.\"", "\"summary\":10") })
            Refuses(() => PolishChatGptClient.ParseTutorResearch(Request(), malformed, []), "Malformed or unexpected AI output is refused without a proposal");
        var claims = JsonNode.Parse(Reply())!.AsObject(); claims["summary"] = "CPD credit approved; independently verified.";
        var unverifiedClaim = PolishChatGptClient.ParseTutorResearch(Request(), claims.ToJsonString(), [new("search", "A result referring to the source", Source)]);
        Check(!unverifiedClaim.Sources.Single().ObservedOpened && unverifiedClaim.EvidenceNote.Contains("not independent verification") && unverifiedClaim.ProposedRecord["verification"]!["current"]!.GetValue<bool>() == false,
            "AI claims about approval or robustness cannot create current verification or prove that a search result was read");
        var longCourse = Course(); string longText = "Beginning. " + new string('x', 45000) + " TAIL_CONTEXT_MARKER";
        longCourse["documents"]![0]!["text"] = longText; var longContext = TutorResearchContext.Create(longCourse);
        string assembled = ""; int offset = 0;
        for (int chunk = 0; chunk < 20; chunk++)
        {
            using var part = JsonDocument.Parse(longContext.Execute("read_tutor_item", new() { ["path"] = "documents/0", ["offset"] = offset, ["length"] = 7000 }));
            assembled += part.RootElement.GetProperty("text").GetString();
            if (part.RootElement.GetProperty("nextOffset").ValueKind == JsonValueKind.Null) break;
            int next = part.RootElement.GetProperty("nextOffset").GetInt32(); Check(next > offset, "Long-context chunk retrieval advances to the next unread character"); offset = next;
        }
        Check(JsonNode.Parse(assembled)!["text"]!.GetValue<string>() == longText, "Bounded context retrieval reconstructs a complete long source document without silent truncation");
        Check(longContext.Execute("search_tutor", new() { ["query"] = "TAIL_CONTEXT_MARKER", ["maxResults"] = 10 }).Contains("TAIL_CONTEXT_MARKER"), "A source search returns a useful snippet around a match late in the document");
        foreach (var args in new[] { new JsonObject { ["path"] = "documents/0", ["offset"] = -1, ["length"] = 1 }, new JsonObject { ["path"] = "documents/0", ["offset"] = 0, ["length"] = 16001 }, new JsonObject { ["path"] = "documents/0", ["offset"] = 999999, ["length"] = 1 } })
            Refuses(() => longContext.Execute("read_tutor_item", args), "Invalid context offsets and oversized chunks are refused");
        using (var lateThread = Client("late-thread"))
        {
            using var stopping = new CancellationTokenSource();
            var pending = lateThread.ResearchTutorAsync(TutorResearchContext.Create(Course()), Request(), stopping.Token);
            string protocol = await UntilProtocolAsync("late-thread", "threadRequested", pending);
            stopping.Cancel(); File.WriteAllText(protocol + ".release", "cancelled");
            try { await pending; throw new Exception("Expected stopped late thread."); } catch (OperationCanceledException) { }
            await UntilProtocolAsync("late-thread", "unsubscribed");
            Check(ReadProtocol(protocol)["turnRequested"]!.GetValue<bool>() == false, "A thread allocated after Stop is explicitly unsubscribed and never starts inference");
        }
        using (var lateTurn = Client("late-turn"))
        {
            using var stopping = new CancellationTokenSource();
            var pending = lateTurn.ResearchTutorAsync(TutorResearchContext.Create(Course()), Request(), stopping.Token);
            string protocol = await UntilProtocolAsync("late-turn", "turnRequested", pending);
            stopping.Cancel(); File.WriteAllText(protocol + ".release", "cancelled");
            try { await pending; throw new Exception("Expected stopped late turn."); } catch (OperationCanceledException) { }
            await UntilProtocolAsync("late-turn", "interrupted");
            Check(true, "A turn allocated after Stop is explicitly interrupted even when its acknowledgement is late");
        }
        using (var disposed = Client("dispose"))
        {
            var pending = disposed.ResearchTutorAsync(TutorResearchContext.Create(Course()), Request());
            await UntilProtocolAsync("dispose", "turnRequested", pending); disposed.Dispose();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Expected disposed research failure."); }
            catch (IOException) { Check(true, "Disposing a research client promptly ends its pending reply without a draft"); }
        }
        using (var mutableSource = Client("normal"))
        {
            var request = Request(); string original = request.SelectedRecord.ToJsonString();
            var pending = mutableSource.ResearchTutorAsync(TutorResearchContext.Create(Course()), request);
            request.SelectedRecord["stem"] = "A later author edit while research was running.";
            var result = await pending;
            Check(result.BeforeJson == original && request.SelectedRecord["stem"]!.GetValue<string>().Contains("later author edit"), "A research reply retains its exact before snapshot without overwriting a later caller draft");
        }
    }
    private static async Task<string> UntilProtocolAsync(string scenario, string flag, Task? pending = null)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (pending?.IsFaulted == true || pending?.IsCanceled == true) await pending;
            foreach (string path in Directory.GetFiles(fixture, "protocol.json", SearchOption.AllDirectories))
            {
                try { var value = ReadProtocol(path); if (TutorResearchContext.Text(value, "scenario") == scenario && value[flag]?.GetValue<bool>() == true) return path; }
                catch (IOException) { } catch (JsonException) { }
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Expected protocol observation did not arrive: " + scenario + "/" + flag);
    }
}
