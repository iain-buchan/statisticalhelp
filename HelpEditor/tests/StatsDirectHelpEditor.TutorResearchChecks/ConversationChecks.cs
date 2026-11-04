using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;

internal static partial class Program
{
    private static string ConversationalReply(bool draft, JsonObject? row = null)
    {
        var reply = JsonNode.Parse(Reply(row))!.AsObject(); reply["hasDraft"] = draft;
        reply["conversationReply"] = draft ? "I kept the denominator among people with the condition. Shall we make the distractors more challenging?" : "Would you like students to distinguish sensitivity from predictive value, or calculate both? We can discuss the learning objective before drafting.";
        if (!draft) reply["proposedRecordJson"] = "";
        return reply.ToJsonString();
    }
    private static async Task ConversationChecksAsync()
    {
        var discussion = Request() with { Mode = "discuss" };
        var reply = PolishChatGptClient.ParseTutorResearch(discussion, ConversationalReply(false), []);
        Check(!reply.HasDraft && reply.ProposedRecord.Count == 0 && reply.ConversationReply.Contains("learning objective") && reply.Sources.Count == 1,
            "A discussion can ask a useful clarification and cite sources without manufacturing an MCQ");
        var clarification = PolishChatGptClient.ParseTutorResearch(Request(), ConversationalReply(false), []);
        Check(!clarification.HasDraft && clarification.BeforeJson == Question().ToJsonString(), "Drafting may ask for clarification while retaining the exact untouched before record");
        Refuses(() => PolishChatGptClient.ParseTutorResearch(discussion, ConversationalReply(true), []), "Discussion mode refuses a manufactured draft");
        var hiddenDraft = JsonNode.Parse(ConversationalReply(false))!.AsObject(); hiddenDraft["proposedRecordJson"] = Question().ToJsonString();
        Refuses(() => PolishChatGptClient.ParseTutorResearch(discussion, hiddenDraft.ToJsonString(), []), "A discussion reply cannot hide an unreviewed record in the no-draft envelope");
        var badEnvelope = JsonNode.Parse(ConversationalReply(false))!.AsObject(); badEnvelope["hasDraft"] = "false";
        Refuses(() => PolishChatGptClient.ParseTutorResearch(discussion, badEnvelope.ToJsonString(), []), "The no-draft flag must be an actual JSON boolean");
        Check(PolishChatGptClient.ParseTutorResearch(Request(), Reply(), []).HasDraft, "Existing five-field structured proposals remain backward compatible");
        var tagged = Question(); tagged["difficulty"] = "beginner"; tagged["learnerGoals"] = new JsonArray("undergraduateMedicine"); tagged["learnerGoalNotes"] = "Original course goal";
        var relabelled = (JsonObject)tagged.DeepClone(); relabelled["difficulty"] = "advanced"; relabelled["learnerGoals"] = new JsonArray("usMedicalBoards");
        var preserved = PolishChatGptClient.ParseTutorResearch(Request() with { SelectedRecord = tagged }, ConversationalReply(true, relabelled), []);
        Check(new[] { "difficulty", "learnerGoals", "learnerGoalNotes" }.All(key => JsonNode.DeepEquals(preserved.ProposedRecord[key], tagged[key])), "AI suggestions cannot silently relabel the original difficulty or learner goals");
        var targets = Request() with { SelectedRecord = tagged, Difficulty = "intermediate", LearnerGoals = new[] { "mastersStatistics", "usMedicalBoards" }, LearnerGoalNotes = "Statistical module for postgraduate examination preparation" };
        var classified = PolishChatGptClient.ParseTutorResearch(targets, ConversationalReply(true, relabelled), []);
        Check(classified.ProposedRecord["difficulty"]!.GetValue<string>() == "intermediate" && classified.ProposedRecord["learnerGoals"]!.AsArray().Count == 2 && classified.ProposedRecord["learnerGoalNotes"]!.GetValue<string>() == targets.LearnerGoalNotes,
            "Explicit author-selected difficulty, multiple learner goals and qualification notes override model classifications");
        var unassigned = PolishChatGptClient.ParseTutorResearch(Request() with { SelectedRecord = tagged, LearnerGoals = Array.Empty<string>() }, ConversationalReply(true, tagged), []);
        Check(new[] { "difficulty", "learnerGoals", "learnerGoalNotes" }.All(key => !unassigned.ProposedRecord.ContainsKey(key)), "Explicit unassigned classification removes prior tags without inferring tags from a qualification");
        var invented = Question(); invented["difficulty"] = "advanced"; invented["learnerGoals"] = new JsonArray("usMedicalBoards");
        var legacy = PolishChatGptClient.ParseTutorResearch(Request(), ConversationalReply(true, invented), []);
        Check(!legacy.ProposedRecord.ContainsKey("difficulty") && !legacy.ProposedRecord.ContainsKey("learnerGoals"), "Untagged legacy material remains untagged when the model invents a learner classification");
        foreach (var invalid in new[] { targets with { Difficulty = "expert" }, targets with { LearnerGoals = new[] { "invented" } }, targets with { LearnerGoals = new[] { "usMedicalBoards", "usMedicalBoards" } }, targets with { LearnerGoalNotes = new string('x', 3001) } })
            Refuses(() => PolishChatGptClient.ParseTutorResearch(invalid, ConversationalReply(true, tagged), []), "Invalid author targets are refused rather than published in a draft");
        using (var client = Client("discuss"))
        {
            var observations = new List<TutorResearchActivity>(); client.ResearchActivity += observations.Add;
            var actual = await client.ResearchTutorAsync(TutorResearchContext.Create(Course()), discussion);
            Check(!actual.HasDraft && actual.Sources.Single().ObservedOpened && observations.Count == 2 && observations[0].Action == "search" && observations[1].Action == "openPage",
                "The real transport path emits live filtered search/open events and validates a no-draft discussion reply");
        }
        using (var client = Client("normal"))
        {
            var mutableGoals = new List<string> { "mastersStatistics" }; var request = Request() with { Difficulty = "beginner", LearnerGoals = mutableGoals };
            var pending = client.ResearchTutorAsync(TutorResearchContext.Create(Course()), request); mutableGoals.Clear(); mutableGoals.Add("invented");
            var result = await pending;
            Check(result.ProposedRecord["learnerGoals"]![0]!.GetValue<string>() == "mastersStatistics", "Classification is frozen before an asynchronous provider request can observe later caller edits");
        }
    }
    private static async Task ConversationDialogChecksAsync(TutorResearchDialog dialog, FakeClient fake)
    {
        await QueueRemovalStatusChecksAsync(dialog, fake);
        await ConversationFailureHistoryChecksAsync(dialog, fake);
        Check(dialog.Controls.OfType<TableLayoutPanel>().First().Controls.OfType<TabControl>().Single().SelectedTab!.Text == "Conversation", "Conversation is the primary view rather than a secondary raw-JSON discussion tab");
        var earlier = dialog.Proposal;
        var noDraft = await dialog.ResearchAsync("Which learning objective would best suit a screening lesson?", "Medical students", "discuss", true);
        Check(!noDraft.HasDraft && ReferenceEquals(earlier, dialog.Proposal) && dialog.ConversationText.Contains("ChatGPT") && dialog.ConversationText.Contains("sensitivity from predictive value"), "Natural-language discussion preserves an earlier complete draft for explicit acceptance");
        dialog.SetLearningTargets("intermediate", new[] { "mastersStatistics", "usMedicalBoards" }, "Teaching diagnostic-test denominators");
        Check(dialog.Proposal!.ProposedRecord["difficulty"]!.GetValue<string>() == "intermediate" && dialog.Proposal.ProposedRecord["learnerGoals"]!.AsArray().Count == 2, "Changing classification after a proposal immediately updates the visible draft before acceptance");
        var controls = Descendants(dialog).ToArray(); var composer = controls.OfType<TextBox>().Single(item => item.AccessibleName == "Tutor research instructions");
        var chat = controls.OfType<RichTextBox>().Single(item => item.AccessibleName == "Research discussion");
        var send = controls.OfType<Button>().Single(item => item.Text == "Send"); var accept = controls.OfType<Button>().Single(item => item.Text == "Accept this draft");
        Rectangle BoundsOf(Control control) => new(dialog.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
        var geometry = new { dialog.DeviceDpi, dialog.Size, dialog.ClientSize, workingArea = Screen.FromControl(dialog).WorkingArea, composer = BoundsOf(composer), chat = BoundsOf(chat), send = BoundsOf(send), accept = BoundsOf(accept) };
        File.WriteAllText(Path.Combine(fixture, "conversation-geometry.json"), System.Text.Json.JsonSerializer.Serialize(geometry, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Check(new Control[] { composer, chat, send, accept }.All(control => control.Visible && dialog.ClientRectangle.Contains(BoundsOf(control))) && chat.Height >= dialog.Font.Height * 5,
            "Native conversation, composer, Send and Accept remain visible within the current screen/DPI bounds");
        using (var screenshot = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size)); screenshot.Save(Path.Combine(fixture, "conversation-native.png")); }
        var previousSize = dialog.Size; dialog.Size = new Size((int)(900 * dialog.DeviceDpi / 96f), (int)(700 * dialog.DeviceDpi / 96f)); dialog.PerformLayout(); await Task.Yield();
        Check(new Control[] { composer, chat, send, accept }.All(control => control.Visible && dialog.ClientRectangle.Contains(BoundsOf(control))) && chat.Height >= dialog.Font.Height * 5,
            "The conversation remains usable at 900 by 700 logical pixels at this PC's current DPI"); dialog.Size = previousSize;
        using (var empty = new TutorResearchDialog(TutorResearchContext.Create(Course()), new(), "questions", new FakeClient(), _ => { }) { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0) })
        {
            empty.Show(dialog); await empty.ResearchAsync("Discuss first, before creating anything", "Course context", "discuss", false);
            bool refused = false; try { empty.AcceptProposal(); } catch (InvalidOperationException) { refused = true; }
            Check(refused && empty.Proposal == null && empty.AcceptedProposal == null, "A discussion-only window cannot accept or return a manufactured course record"); empty.Close();
        }
        fake.HoldNext = true; fake.IgnoreCancellation = true;
        var first = dialog.ResearchAsync("Research the assumptions before drafting", "Postgraduate medical students", "discuss", true);
        fake.EmitActivity(new("search", "screening definitions", ""));
        Check(dialog.LiveActivityText.Contains("Search: screening definitions"), "Observed web actions are visible in the conversation while a reply is still in progress");
        var queued = dialog.SendMessageAsync("Now draft an intermediate exercise with those goals", "Postgraduate medical students", "refine", false);
        Check(dialog.HasQueuedFollowUp && !queued.IsCompleted, "One follow-up can be queued while the composer remains usable during research");
        try { _ = dialog.SendMessageAsync("Do not overwrite the queued question", "Other audience", "discuss", false); throw new Exception("Expected queue refusal"); } catch (InvalidOperationException) { }
        fake.Held!.SetResult(true); await first; await queued;
        Check(!dialog.HasQueuedFollowUp && fake.LastRequest!.Instructions.Contains("Now draft") && fake.LastRequest.Difficulty == "intermediate" && fake.LastRequest.LearnerGoals!.Count == 2 && fake.LastRequest.Audience == "Postgraduate medical students" && !fake.LastRequest.UseWebResearch,
            "A queued follow-up sends its frozen message, task, audience, web choice and author-selected targets");
        Check(fake.LastRequest!.PreviousDiscussion.Contains("Research the assumptions") && fake.LastRequest.PreviousDiscussion.Contains("learning objective"), "A queued follow-up sees the completed preceding conversational answer, not an empty or stale transcript");
        fake.HoldNext = true; fake.IgnoreCancellation = true;
        var stopping = dialog.ResearchAsync("Hold this question", "Medical students", "discuss", true);
        var cancelledQueue = dialog.SendMessageAsync("This must not run after Stop", "Medical students", "refine", true); int requests = fake.Requests.Count;
        dialog.StopResearch(); fake.Held!.SetResult(true);
        try { await stopping; throw new Exception("Expected stop"); } catch (OperationCanceledException) { }
        try { await cancelledQueue; throw new Exception("Expected queued stop"); } catch (OperationCanceledException) { }
        Check(fake.Requests.Count == requests && !dialog.HasQueuedFollowUp && dialog.AcceptedProposal == null, "Stop cancels both the active request and queued message without sending or accepting a late draft");
        dialog.SetLearningTargets("advanced", new[] { "healthcareBusinessAnalyst" }, "CPD learning objective; credit requires provider assessment");
        Check(dialog.Proposal!.ProposedRecord["difficulty"]!.GetValue<string>() == "advanced" && dialog.Proposal.ProposedRecord["learnerGoals"]![0]!.GetValue<string>() == "healthcareBusinessAnalyst", "The final draft carries the last explicit author-selected classification before acceptance");
        var audience = controls.OfType<ComboBox>().Single(item => item.AccessibleName == "Tutor audience");
        dialog.SetLearningTargets("intermediate", new[] { "undergraduateMedicine" });
        Check(audience.Text == "Undergraduate medicine", "An automatically suggested audience follows the selected undergraduate learning goal");
        dialog.SetLearningTargets("intermediate", new[] { "postgraduateMedicine", "mastersStatistics", "usMedicalBoards" });
        await dialog.SendMessageAsync("Discuss the reclassified exercise", audience.Text, "discuss", false);
        Check(fake.LastRequest!.Audience.Contains("Postgraduate medicine") && fake.LastRequest.Audience.Contains("Master's-level statistics") && fake.LastRequest.Audience.Contains("US medical board") && !fake.LastRequest.Audience.Contains("Undergraduate") && fake.LastRequest.Difficulty == "intermediate",
            "Retagging undergraduate material sends the refreshed postgraduate audience without changing statistical difficulty");
        audience.Text = "Liverpool CPD course for mixed clinical teams";
        dialog.SetLearningTargets("advanced", new[] { "healthcareBusinessAnalyst" });
        await dialog.SendMessageAsync("Discuss the author's intended audience", audience.Text, "discuss", false);
        Check(fake.LastRequest!.Audience == "Liverpool CPD course for mixed clinical teams", "Changing learning goals preserves an explicitly typed audience in the actual research request");
        dialog.SetLearningTargets(null, Array.Empty<string>());
        Check(audience.Text == "Liverpool CPD course for mixed clinical teams", "Clearing automatic goal tags also preserves the author's explicitly typed audience");
    }
    private static async Task QueueRemovalStatusChecksAsync(TutorResearchDialog dialog, FakeClient fake)
    {
        fake.HoldNext = true; fake.IgnoreCancellation = true;
        var current = dialog.ResearchAsync("Continue the first discussion while removing only its queue", "Course context", "discuss", false);
        var composer = Descendants(dialog).OfType<TextBox>().Single(item => item.AccessibleName == "Tutor research instructions");
        composer.Text = "Queued request to remove";
        var sendComposer = typeof(TutorResearchDialog).GetMethod("SendComposerAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var uiSend = (Task)sendComposer.Invoke(dialog, null)!;
        Check(dialog.HasQueuedFollowUp, "The actual composer pathway queues a follow-up while the first request is running");
        dialog.RemoveQueuedMessage(); await uiSend;
        var summary = (Label)typeof(TutorResearchDialog).GetField("summary", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
        bool correctStatus = !current.IsCompleted && !fake.LastCancellationToken.IsCancellationRequested && !dialog.HasQueuedFollowUp && !summary.Text.StartsWith("Stopped", StringComparison.Ordinal);
        fake.Held!.SetResult(true); await current;
        Check(correctStatus, "Removing only a queued message keeps the first request running and never falsely reports it stopped");
    }
    private static async Task ConversationFailureHistoryChecksAsync(TutorResearchDialog dialog, FakeClient fake)
    {
        var priorDraft = dialog.Proposal;
        const string failedQuestion = "FAILURE_HISTORY_04NOV: Can you clarify the denominator when the outcome is uncommon?";
        fake.FailNext = true;
        try { await dialog.ResearchAsync(failedQuestion, "Course context", "discuss", false); throw new Exception("Expected failed research request"); } catch (IOException) { }
        await dialog.ResearchAsync("Please retry the previous question", "Course context", "discuss", false);
        Check(fake.LastRequest!.PreviousDiscussion.Contains(failedQuestion) && fake.LastRequest.PreviousDiscussion.Contains("REQUEST STATUS: Failed; no complete AI reply received") && !fake.LastRequest.PreviousDiscussion.Contains("Mock provider failure") && ReferenceEquals(priorDraft, dialog.Proposal),
            "A retry receives the failed question with an accurate no-reply status while preserving the prior complete draft");
        var pathFailure = new PathFailureClient();
        using (var isolated = new TutorResearchDialog(TutorResearchContext.Create(Course()), Question(), "questions", pathFailure, _ => { }) { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0) })
        {
            isolated.Show(dialog);
            try { await isolated.ResearchAsync("Clarify the sampling assumption", "Course context", "discuss", false); throw new Exception("Expected path-bearing error"); } catch (IOException) { }
            bool uiShowsError = isolated.LastError == PathFailureClient.LocalError;
            await isolated.ResearchAsync("Please retry", "Course context", "discuss", false);
            Check(uiShowsError && pathFailure.LastRequest!.PreviousDiscussion.Contains("Clarify the sampling assumption") && !pathFailure.LastRequest.PreviousDiscussion.Contains("ERROR:") && !pathFailure.LastRequest.PreviousDiscussion.Contains(PathFailureClient.LocalError) && !pathFailure.LastRequest.PreviousDiscussion.Contains("C:\\private"),
                "Local provider error details remain in the UI and are never sent in retry conversation history"); isolated.Close();
        }
        const string stoppedQuestion = "CANCELLATION_HISTORY_04NOV: Could you compare sensitivity with predictive value?";
        fake.HoldNext = true; fake.IgnoreCancellation = true;
        var stopped = dialog.ResearchAsync(stoppedQuestion, "Course context", "discuss", true);
        dialog.StopResearch(); fake.Held!.SetResult(true);
        try { await stopped; throw new Exception("Expected stopped research request"); } catch (OperationCanceledException) { }
        await dialog.ResearchAsync("Discuss the question I stopped, without searching again", "Course context", "discuss", false);
        string history = fake.LastRequest!.PreviousDiscussion; int start = history.IndexOf("AUTHOR: " + stoppedQuestion, StringComparison.Ordinal);
        int end = start < 0 ? -1 : history.IndexOf("\n\n", start, StringComparison.Ordinal);
        string attempt = start >= 0 && end >= start ? history[start..end] : "";
        Check(attempt.Contains("REQUEST STATUS: Stopped; no complete AI reply received") && !attempt.Contains("CHATGPT:") && !attempt.Contains("SOURCE CITATIONS") && ReferenceEquals(priorDraft, dialog.Proposal),
            "A follow-up sees the stopped question without a fabricated answer, inherited source evidence or replacement draft");
    }
    private sealed class PathFailureClient : ITutorResearchChatGptClient
    {
        public const string LocalError = "Provider diagnostic ERROR: C:\\private\\account-runtime\\diagnostic.log";
        public bool IsSignedIn => true; public string Status => "Connected to isolated mock"; public string? LoginUrl => null;
        public event Action? Changed { add { } remove { } }
        public TutorResearchRequest? LastRequest; private bool fail = true;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> SignInAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task CancelSignInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StatsDirect.HelpEditor.Core.HelpPolishReview> PolishAsync(StatsDirect.HelpEditor.Core.HelpPolishContext context, StatsDirect.HelpEditor.Core.HelpPolishSnapshot snapshot, string instructions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TutorResearchProposal> ResearchTutorAsync(TutorResearchContext context, TutorResearchRequest request, CancellationToken cancellationToken = default)
        { LastRequest = request; if (fail) { fail = false; throw new IOException(LocalError); } return Task.FromResult(PolishChatGptClient.ParseTutorResearch(request, ConversationalReply(false), [])); }
        public void Dispose() { }
    }
    private static IEnumerable<Control> Descendants(Control control)
    { foreach (Control child in control.Controls) { yield return child; foreach (var item in Descendants(child)) yield return item; } }

    // Optional, purposeful two-turn smoke: existing account only, synthetic
    // teaching data, no author record acceptance, source writes or publication.
    private static async Task LiveConversationAsync()
    {
        using var client = new PolishChatGptClient(); using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await client.InitializeAsync(stop.Token);
        if (!client.IsSignedIn) throw new InvalidOperationException("The existing Help Editor ChatGPT account is not signed in; no sign-in was started.");
        var request = Request() with { Mode = "discuss", Audience = "Master's-level statistics; US medical board examinations", Difficulty = "intermediate", LearnerGoals = new[] { "mastersStatistics", "usMedicalBoards" }, LearnerGoalNotes = "Original educational material for diagnostic-test interpretation; no examination endorsement.",
            Instructions = "Discuss the learning objective for this fictional screening example before drafting anything. Is sensitivity or predictive value the better first objective, and why? Use web search and open a CDC Principles of Epidemiology primary teaching page for the denominator definitions. Ask one short clarification if useful. This is a discussion, so do not manufacture an MCQ draft." };
        var first = await client.ResearchTutorAsync(TutorResearchContext.Create(Course()), request, stop.Token);
        File.WriteAllText(Path.Combine(fixture, "live-conversation-discussion.json"), System.Text.Json.JsonSerializer.Serialize(first, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Check(!first.HasDraft && first.ProposedRecord.Count == 0 && first.ConversationReply.Length > 0, "Actual authenticated discussion answers naturally without manufacturing a draft");
        Check(first.WebResearchObserved && first.Sources.Any(source => source.ObservedOpened), "Actual discussion distinguishes an observed source retrieval from model-suggested URLs");
        var followUp = request with { Mode = "refine", UseWebResearch = false,
            Instructions = "Thank you. Keep sensitivity as the single-best-answer objective. Now refine the existing complete MCQ, preserving its ID, all metadata and fictional counts. Keep the explanation and distractor feedback clear for the selected difficulty and learner goals. Supply a simple base-R arithmetic check. Do not browse again.",
            PreviousDiscussion = "AUTHOR: " + request.Instructions + "\nCHATGPT: " + first.ConversationReply + "\nREVIEW: " + first.StatisticalNotes + "\nSOURCES (browsing, not independent verification): " + System.Text.Json.JsonSerializer.Serialize(first.Sources) };
        var second = await client.ResearchTutorAsync(TutorResearchContext.Create(Course()), followUp, stop.Token);
        File.WriteAllText(Path.Combine(fixture, "live-conversation-draft.json"), System.Text.Json.JsonSerializer.Serialize(second, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Check(second.HasDraft && second.ProposedRecord["id"]!.GetValue<string>() == "UG-SENS-01" && second.BeforeJson == Question().ToJsonString(), "Actual conversational follow-up returns a complete matching draft without changing its source");
        Check(second.ProposedRecord["difficulty"]!.GetValue<string>() == "intermediate" && second.ProposedRecord["learnerGoals"]!.AsArray().Count == 2 && second.RValidationCode.Length > 0, "Actual follow-up draft carries author-selected difficulty, multiple goals and proposed R checks");
        Check(!second.WebResearchObserved && second.Sources.All(source => !source.ObservedOpened), "Earlier conversation citations are not promoted into current-turn source retrieval evidence");
    }
}
