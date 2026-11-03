using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

if (args.FirstOrDefault() == "--fake") { await FakeAsync(args[1], args[2]); return; }
if (args.Contains("--live-polish"))
{ await LivePolishAsync(Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory, "../../../../../"))); return; }
string repo = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string fixture = Path.Combine(repo, "evidence", "polish-transport-fixture", "run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var checks = new List<object>();
void Check(bool success, string name) { checks.Add(new { name, passed = success }); if (!success) throw new Exception(name); }
async Task Refuses(Func<Task> work, string name, string? missing = null)
{
    try { await work(); throw new Exception("Expected refusal: " + name); }
    catch (Exception ex) when (!ex.Message.StartsWith("Expected refusal:")) { Check(missing == null || !ex.Message.Contains(missing), name); }
}
void RefusesSync(Action action, string name) { try { action(); throw new Exception("Expected refusal: " + name); } catch (Exception ex) when (!ex.Message.StartsWith("Expected refusal:")) { Check(true, name); } }
string source = "<html><head><title>Example</title></head><body><h1>Example</h1><p>This wording is rather unclear.</p><pre>do_not_change()</pre></body></html>";
string projectPath = Path.Combine(fixture, "source");
Directory.CreateDirectory(Path.Combine(projectPath, "Content")); Directory.CreateDirectory(Path.Combine(projectPath, "Project", "TOCs"));
File.WriteAllText(Path.Combine(projectPath, HelpProject.MarkerFile), "{}");
File.WriteAllText(Path.Combine(projectPath, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Example' Link='/Content/example.htm'/></CatapultToc>");
File.WriteAllText(Path.Combine(projectPath, "Content", "example.htm"), source);
File.WriteAllText(Path.Combine(projectPath, "Content", "unlisted.htm"), "<html><head><title>Unlisted context</title></head><body><p>Unlisted statistical terminology.</p></body></html>");
var project = HelpProject.Open(projectPath); var snapshot = HelpPolishSnapshot.Create("example.htm", source); var context = HelpPolishContext.Create(project, snapshot);
var block = snapshot.Blocks.Single(b => b.Text == "This wording is rather unclear.");
string Response(string before, string after) => JsonSerializer.Serialize(new { changes = new[] { new { blockId = block.Id, before, after, reason = "Clearer wording" } } });
var direct = PolishChatGptClient.ParseReview(snapshot, Response(block.Text, "This wording is unclear."));
Check(direct.Changes.Count == 1, "strict JSON yields one tracked proposal");
Check(direct.Apply(direct.Changes.Select(c => c.Id)).Contains("This wording is unclear."), "parsed proposal applies through protected core review");
Check(PolishChatGptClient.ParseReview(snapshot, "{\"changes\":[]}").Changes.Count == 0, "empty suggestion set is valid");
RefusesSync(() => PolishChatGptClient.ParseReview(snapshot, "```json\n{}\n```"), "markdown output rejected");
RefusesSync(() => PolishChatGptClient.ParseReview(snapshot, Response("stale", "Clearer")), "stale before text rejected without source writes");
RefusesSync(() => PolishChatGptClient.ParseReview(snapshot, Response(block.Text, block.Text)), "unchanged block rejected");
RefusesSync(() => PolishChatGptClient.ParseReview(snapshot, "{\"changes\":[],\"shell\":\"run\"}"), "extra output properties rejected");
var numericSnapshot = HelpPolishSnapshot.Create("example.htm", "<html><body><p>Report P &lt; 0.05 as specified.</p></body></html>");
var numericBlock = numericSnapshot.Blocks.Single();
string reversedComparison = JsonSerializer.Serialize(new { changes = new[] { new { blockId = numericBlock.Id, before = numericBlock.Text, after = numericBlock.Text.Replace("<", ">"), reason = "Clearer wording" } } });
RefusesSync(() => PolishChatGptClient.ParseReview(numericSnapshot, reversedComparison), "structured AI output cannot reverse a numerical comparison before tracked review");
Check(PolishChatGptClient.IsLoginUrl("https://auth.openai.com/authorize?code=x"), "official HTTPS auth URL accepted");
foreach (string invalid in new[] { "http://auth.openai.com/", "https://auth.openai.com.evil.test/", "https://user@auth.openai.com/", "https://auth.openai.com:123/", "file:///C:/auth" })
    Check(!PolishChatGptClient.IsLoginUrl(invalid), "untrusted login URL rejected: " + invalid);
Check(PolishChatGptClient.ExecuteContextTool(context, "search_help", new() { ["query"] = "Unlisted", ["maxResults"] = 12 }).Contains("unlisted.htm"), "search reaches non-TOC pages");
Check(PolishChatGptClient.ExecuteContextTool(context, "read_help_page", new() { ["path"] = "unlisted.htm", ["offset"] = 0, ["length"] = 16000 }).Contains("statistical terminology"), "read tool returns context for arbitrary listed page");
RefusesSync(() => PolishChatGptClient.ExecuteContextTool(context, "read_help_page", new() { ["path"] = "../secret", ["offset"] = 0, ["length"] = 16000 }), "path traversal refused");
RefusesSync(() => PolishChatGptClient.ExecuteContextTool(context, "exec", new()), "unknown tool refused");
RefusesSync(() => PolishChatGptClient.ExecuteContextTool(context, "search_help", new() { ["query"] = "x", ["maxResults"] = 12, ["command"] = "secret" }), "unknown tool arguments refused");
RefusesSync(() => PolishChatGptClient.ExecuteContextTool(context, "read_help_page", new() { ["path"] = "example.htm", ["offset"] = 0, ["length"] = 16001 }), "oversized tool output request refused");

PolishChatGptClient Client(string scenario)
{
    string dir = Path.Combine(fixture, scenario + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    string executable = Environment.ProcessPath!;
    return new PolishChatGptClient(executable, dir, "--fake", scenario, Path.Combine(dir, "protocol.json"));
}
using (var client = Client("normal"))
{
    await client.InitializeAsync(); Check(client.IsSignedIn, "account/read verifies signed-in ChatGPT account");
    var review = await client.PolishAsync(context, snapshot, "Keep terminology precise.");
    Check(review.Changes.Count == 1, "stdio request receives structured tracked edits after context calls");
    Check(client.Status.Contains("review complete"), "completion status reported");
    await client.SignOutAsync(); Check(!client.IsSignedIn, "explicit logout clears isolated connection");
}
using (var client = Client("auth"))
{
    await client.InitializeAsync(); Check(!client.IsSignedIn, "unauthenticated account not mistaken for sign-in");
    string? url = await client.SignInAsync(); Check(url?.StartsWith("https://auth.openai.com/") == true, "sign-in returns official browser URL");
    var timeout = Stopwatch.StartNew(); while (!client.IsSignedIn && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(30);
    Check(client.IsSignedIn && client.LoginUrl == null, "matching login completion refreshes account");
}
using (var client = Client("cancel-auth"))
{
    await client.SignInAsync(); await client.CancelSignInAsync();
    Check(!client.IsSignedIn && client.LoginUrl == null, "cancel pending browser sign-in clears URL");
}
using (var client = Client("late-auth"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
    await Refuses(() => client.SignInAsync(stop.Token), "Stop cancels sign-in before server acknowledgement");
    await Task.Delay(850);
    Check(client.LoginUrl == null && !client.IsSignedIn, "late login acknowledgement cannot reopen stopped sign-in");
}
using (var client = Client("generation-auth"))
{
    var signingIn = client.SignInAsync();
    await Task.Delay(200); await client.CancelSignInAsync();
    await Refuses(() => signingIn, "Cancel sign-in invalidates in-flight acknowledgement");
    Check(client.LoginUrl == null, "late invalidated login URL is discarded");
}
using (var client = Client("bad-login"))
{
    await Refuses(() => client.SignInAsync(), "untrusted server auth URL rejected");
    Check(client.LoginUrl == null, "rejected URL never exposed to browser");
}
using (var client = Client("unsigned"))
    await Refuses(() => client.PolishAsync(context, snapshot, ""), "review cannot send without own account sign-in");
using (var client = Client("failed"))
    await Refuses(() => client.PolishAsync(context, snapshot, ""), "provider failures sanitized", "SECRET");
using (var client = Client("bad-proposal"))
    await Refuses(() => client.PolishAsync(context, snapshot, ""), "unsafe model edits rejected as complete review");
using (var client = Client("cancel"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    await Refuses(() => client.PolishAsync(context, snapshot, "", stop.Token), "Stop cancels outstanding review");
    Check(client.Status.Contains("stopped"), "Stop status confirms unchanged page");
    await Task.Delay(80); Check(!client.Status.Contains("complete"), "late completion after Stop ignored");
}
using (var client = Client("wrong-thread"))
    Check((await client.PolishAsync(context, snapshot, "")).Changes.Count == 1, "unrelated thread and turn notifications ignored");
using (var client = Client("cancel-late"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    await Refuses(() => client.PolishAsync(context, snapshot, "", stop.Token), "Stop cancels before turn/start acknowledgement");
    await Task.Delay(200);
    Check(client.Status.Contains("stopped"), "late turn acknowledgement leaves stopped status");
}
using (var client = Client("cancel-started"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    await Refuses(() => client.PolishAsync(context, snapshot, "", stop.Token), "Stop handles turn/started before acknowledgement");
}
using (var client = Client("parallel"))
{
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
    var first = client.PolishAsync(context, snapshot, "", stop.Token);
    await Task.Delay(100);
    await Refuses(() => client.PolishAsync(context, snapshot, ""), "concurrent reviews refused");
    try { await first; } catch (OperationCanceledException) { }
}
Check(File.ReadAllText(Path.Combine(projectPath, "Content", "example.htm")) == source, "all transport requests leave source unchanged");
var protocolFiles = Directory.GetFiles(fixture, "protocol.json", SearchOption.AllDirectories);
var normal = protocolFiles.Select(p => JsonNode.Parse(File.ReadAllText(p))!).First(p => p["scenario"]!.GetValue<string>() == "normal");
Check(normal["isolated"]!.GetValue<bool>(), "dedicated CODEX_HOME used without inherited API keys");
Check(normal["strictConfig"]!.GetValue<bool>(), "strict config passed on process command line");
Check(normal["shellDisabled"]!.GetValue<bool>(), "built-in shell and script tools disabled in config");
Check(normal["ephemeral"]!.GetValue<bool>() && normal["readOnly"]!.GetValue<bool>(), "ephemeral thread and read-only sandbox configured");
Check(normal["catalogPresent"]!.GetValue<bool>(), "prompt includes all-page catalog and current editable text");
Check(normal["schema"]!.GetValue<bool>(), "turn requests exact structured JSON output schema");
Check(normal["searchSucceeded"]!.GetValue<bool>() && normal["readSucceeded"]!.GetValue<bool>(), "dynamic search and page read served by host");
Check(normal["unknownRefused"]!.GetValue<bool>(), "unavailable command approval RPC rejected");
var cancelled = protocolFiles.Select(p => JsonNode.Parse(File.ReadAllText(p))!).First(p => p["scenario"]!.GetValue<string>() == "cancel");
Check(cancelled["interrupted"]!.GetValue<bool>(), "Stop sends turn/interrupt to provider");
foreach (string scenario in new[] { "cancel-late", "cancel-started" })
    Check(protocolFiles.Select(p => JsonNode.Parse(File.ReadAllText(p))!).First(p => p["scenario"]!.GetValue<string>() == scenario)["interrupted"]!.GetValue<bool>(), "late-created turn explicitly interrupted: " + scenario);
foreach (string scenario in new[] { "late-auth", "generation-auth" })
    Check(protocolFiles.Select(p => JsonNode.Parse(File.ReadAllText(p))!).First(p => p["scenario"]!.GetValue<string>() == scenario)["loginCancelled"]!.GetValue<bool>(), "late-created browser login explicitly cancelled: " + scenario);
if (args.Contains("--real-startup"))
{
    using var client = new PolishChatGptClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await client.InitializeAsync(timeout.Token);
    Check(client.Status is "Not connected to ChatGPT" or "Connected to ChatGPT", "reviewed actual runtime starts and reads only its own account (no inference or login)");
    var thread = await client.StartHelpThreadAsync(timeout.Token);
    Check(thread["thread"]?["id"]?.GetValue<string>().Length > 0, "actual pinned runtime accepts isolated ephemeral thread and dynamic help tool schemas (no inference)");
}
Directory.CreateDirectory(Path.Combine(repo, "evidence"));
File.WriteAllText(Path.Combine(repo, "evidence", "polish-transport-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, passed = true, count = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} ChatGPT transport checks");

static async Task LivePolishAsync(string repo)
{
    string root = Path.Combine(repo, "HelpSource"), page = "basics/basics.htm";
    Dictionary<string, string> Hashes() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.Ordinal);
    var before = Hashes();
    var project = HelpProject.Open(root);
    var snapshot = HelpPolishSnapshot.Create(page, project.ReadTopic(page));
    var context = HelpPolishContext.Create(project, snapshot);
    bool connected = false, sourceUnchanged = false;
    int count = 0, calls = 0, checkedApply = 0;
    string? error = null;
    try
    {
        using var client = new PolishChatGptClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await client.InitializeAsync(timeout.Token);
        connected = client.IsSignedIn;
        if (!connected) throw new InvalidOperationException("The HelpEditor ChatGPT account is not signed in.");
        var reviewing = client.PolishAsync(context, snapshot,
            "Suggest at most five useful wording improvements to this page, using clear British English while preserving the precise statistical meaning and every number. Before returning suggestions, use read_help_page to consult basics/statistics.htm and parametric/paired_t.htm (search the catalog for the paired t-test page if its path differs), and search_help for relevant terminology. Treat these other pages as context only. Keep existing good wording unchanged. Return only changed editableText records, copying the exact before text and blockId.", timeout.Token);
        // Test-only observation: do not add diagnostics or retain tool/page contents in the production client.
        var activeField = typeof(PolishChatGptClient).GetField("reply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        while (!reviewing.IsCompleted)
        {
            var active = activeField.GetValue(client);
            var called = active?.GetType().GetField("Calls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(active) as HashSet<string>;
            if (called != null) lock (called) calls = Math.Max(calls, called.Count);
            await Task.Delay(50);
        }
        var review = await reviewing;
        count = review.Changes.Count;
        if (count > 5) throw new InvalidOperationException("The bounded live review returned more than five changes.");
        if (calls == 0) throw new InvalidOperationException("The live review did not serve any context tool calls.");
        string accepted = review.Apply(review.Changes.Select(c => c.Id));
        _ = HelpPolishSnapshot.Create(page, accepted); // re-validate complete XHTML after acceptance, in memory only.
        checkedApply = review.Changes.Count;
        if (review.Apply([]) != snapshot.Source) throw new InvalidOperationException("Rejecting all changes did not preserve the exact page source.");
    }
    catch (Exception ex)
    {
        // Client/core exceptions are sanitized. Do not retain raw server messages, accounts, proposals or tokens.
        error = ex is OperationCanceledException ? "The live verification was stopped or timed out."
            : ex is IOException ? ex.Message
            : ex is InvalidOperationException ? ex.Message
            : "The live verification could not be completed (" + ex.GetType().Name + ").";
    }
    var after = Hashes();
    sourceUnchanged = before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out var value) && value == p.Value);
    bool passed = error == null && connected && calls > 0 && sourceUnchanged;
    var receipt = new { checkedAtUtc = DateTimeOffset.UtcNow, passed, page, contextPageCount = context.PageCount, proposedChanges = count, contextToolCalls = calls,
        acceptedInMemoryForVerification = checkedApply, sourceFilesUnchanged = sourceUnchanged, sourceFileCount = before.Count, actualPagesSaved = 0, publishedHelpChanged = false,
        installersChanged = false, authenticatedAccountDetailsRecorded = false, error };
    Directory.CreateDirectory(Path.Combine(repo, "evidence"));
    File.WriteAllText(Path.Combine(repo, "evidence", "polish-live-check.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(receipt));
    if (!passed) Environment.ExitCode = 1;
}

static async Task FakeAsync(string scenario, string recordPath)
{
    object outputLock = new();
    var record = new JsonObject { ["scenario"] = scenario, ["interrupted"] = false };
    void Save() => File.WriteAllText(recordPath, record.ToJsonString());
    void Send(JsonObject message) { lock (outputLock) { Console.Out.WriteLine(message.ToJsonString()); Console.Out.Flush(); } }
    void Result(JsonNode? id, JsonNode? result = null) => Send(new() { ["id"] = id?.DeepClone(), ["result"] = result ?? new JsonObject() });
    void Notification(string method, JsonObject parameters) => Send(new() { ["method"] = method, ["params"] = parameters });
    bool signedIn = scenario is not ("auth" or "cancel-auth" or "bad-login" or "unsigned" or "late-auth" or "generation-auth");
    JsonObject? proposal = null;
    void Finish(string thread = "thread1", string turn = "turn1", string? body = null)
    {
        Notification("item/completed", new() { ["threadId"] = thread, ["turnId"] = turn, ["item"] = new JsonObject { ["type"] = "agentMessage", ["id"] = "answer", ["text"] = body ?? proposal!.ToJsonString() } });
        Notification("turn/completed", new() { ["threadId"] = thread, ["turn"] = new JsonObject { ["id"] = turn, ["status"] = "completed" } });
    }
    string? line;
    while ((line = await Console.In.ReadLineAsync()) != null)
    {
        var message = JsonNode.Parse(line)!; string method = message["method"]?.GetValue<string>() ?? ""; var id = message["id"]; var p = message["params"];
        if (method == "initialize")
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? "";
            string config = File.ReadAllText(Path.Combine(home, "config.toml"));
            record["isolated"] = home == Path.GetDirectoryName(recordPath) && Environment.GetEnvironmentVariable("OPENAI_API_KEY") == null;
            record["strictConfig"] = Environment.GetCommandLineArgs().Contains("--strict-config");
            record["shellDisabled"] = config.Contains("shell_tool = false") && config.Contains("unified_exec = false") && config.Contains("plugins = false") && config.Contains("hooks = false");
            Result(id); Save();
        }
        else if (method == "initialized") { }
        else if (method == "account/read") Result(id, new JsonObject { ["account"] = signedIn ? new JsonObject { ["type"] = "chatgpt", ["email"] = "test@example.invalid" } : null });
        else if (method == "account/login/start")
        {
            if (scenario is "late-auth" or "generation-auth") await Task.Delay(750);
            Result(id, new JsonObject { ["loginId"] = "login1", ["authUrl"] = scenario == "bad-login" ? "https://evil.invalid/" : "https://auth.openai.com/authorize?test=true" });
            if (scenario == "auth") _ = Task.Run(async () => { await Task.Delay(150); signedIn = true; Notification("account/login/completed", new() { ["loginId"] = "login1", ["success"] = true }); });
        }
        else if (method == "account/login/cancel") { record["loginCancelled"] = true; Save(); Result(id); }
        else if (method == "account/logout") { signedIn = false; Result(id); }
        else if (method == "thread/start")
        {
            record["ephemeral"] = p?["ephemeral"]?.GetValue<bool>() == true;
            record["readOnly"] = p?["sandbox"]?.GetValue<string>() == "read-only" && p?["approvalPolicy"]?.GetValue<string>() == "never";
            record["tools"] = p?["dynamicTools"]?.DeepClone(); Save();
            Result(id, new JsonObject { ["thread"] = new JsonObject { ["id"] = "thread1" } });
        }
        else if (method == "turn/start")
        {
            string prompt = p!["input"]![0]!["text"]!.GetValue<string>();
            var data = JsonNode.Parse(prompt[(prompt.IndexOf('\n') + 1)..])!;
            var block = data["editableText"]!.AsArray().First(b => b!["before"]!.GetValue<string>() == "This wording is rather unclear.")!;
            proposal = new() { ["changes"] = new JsonArray(new JsonObject { ["blockId"] = block["blockId"]!.DeepClone(), ["before"] = scenario == "bad-proposal" ? "stale" : block["before"]!.DeepClone(), ["after"] = "This wording is unclear.", ["reason"] = "Clearer wording" }) };
            record["catalogPresent"] = data["helpCatalog"]!.GetValue<string>().Contains("unlisted.htm") && data["pageCount"]!.GetValue<int>() == 2;
            record["schema"] = p["outputSchema"]?["additionalProperties"]?.GetValue<bool>() == false;
            Save();
            if (scenario == "cancel-started") Notification("turn/started", new() { ["threadId"] = "thread1", ["turn"] = new JsonObject { ["id"] = "turn1" } });
            if (scenario is "cancel-late" or "cancel-started") await Task.Delay(750);
            Result(id, new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn1" } });
            if (scenario is "cancel" or "parallel" or "cancel-late" or "cancel-started") continue;
            if (scenario == "failed") Notification("turn/completed", new() { ["threadId"] = "thread1", ["turn"] = new JsonObject { ["id"] = "turn1", ["status"] = "failed", ["error"] = new JsonObject { ["message"] = "SECRET 401 token" } } });
            else if (scenario == "wrong-thread") { Finish("other-thread", "other-turn", "WRONG"); Finish(); }
            else if (scenario == "normal") Send(new() { ["id"] = 900, ["method"] = "item/commandExecution/requestApproval", ["params"] = new JsonObject() });
            else Finish();
        }
        else if (method == "turn/interrupt") { record["interrupted"] = true; Save(); Result(id); if (proposal != null) Finish(); }
        else if (method == "thread/unsubscribe") { Save(); Result(id); }
        else if (id?.GetValue<int>() == 900)
        {
            record["unknownRefused"] = message["error"]?["code"]?.GetValue<int>() == -32601; Save();
            Send(new() { ["id"] = 901, ["method"] = "item/tool/call", ["params"] = new JsonObject { ["threadId"] = "thread1", ["turnId"] = "turn1", ["tool"] = "search_help", ["callId"] = "call1", ["arguments"] = new JsonObject { ["query"] = "Unlisted", ["maxResults"] = 12 } } });
        }
        else if (id?.GetValue<int>() == 901)
        {
            record["searchSucceeded"] = message["result"]?["success"]?.GetValue<bool>() == true && message.ToJsonString().Contains("unlisted.htm"); Save();
            Send(new() { ["id"] = 902, ["method"] = "item/tool/call", ["params"] = new JsonObject { ["threadId"] = "thread1", ["turnId"] = "turn1", ["tool"] = "read_help_page", ["callId"] = "call2", ["arguments"] = new JsonObject { ["path"] = "unlisted.htm", ["offset"] = 0, ["length"] = 16000 } } });
        }
        else if (id?.GetValue<int>() == 902) { record["readSucceeded"] = message["result"]?["success"]?.GetValue<bool>() == true && message.ToJsonString().Contains("statistical terminology"); Save(); Finish(); }
        else Result(id);
    }
}
