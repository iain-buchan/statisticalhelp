using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

string editorRoot = Path.GetFullPath(args[0]);
string evidence = Path.Combine(editorRoot, "evidence/SharedContentChecks-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(evidence);
var checks = new List<object>(); int failures = 0, sequence = 0;
void Check(bool passed, string name, object? detail = null)
{ checks.Add(new { name, passed, detail }); if (!passed) failures++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
void Reject(BundleFixture sample, string name, string? expected = null)
{
    var before = sample.AllHashes();
    try { SharedContentPublisher.Verify(sample.Root, expected); Check(false, name, "Validator unexpectedly accepted the fixture"); }
    catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException or System.Xml.XmlException)
    { Check(true, name, new { error = error.GetType().Name, error.Message }); }
    catch (Exception error) { Check(false, name, new { unexpectedError = error.GetType().Name, error.Message }); }
    Check(before.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(sample.AllHashes().OrderBy(x => x.Key, StringComparer.Ordinal)), name + " leaves every input byte unchanged");
}
BundleFixture seed = null!;
BundleFixture Case(string name) => seed.Copy(Path.Combine(evidence, "Cases", (++sequence).ToString("D2") + "-" + name));
try
{
    string malformedCourse = Path.Combine(evidence, "MalformedTutor.json"); File.WriteAllText(malformedCourse, "{\"schemaVersion\":99}");
    try
    {
        var inspectedMalformed = SharedContentPublisher.InspectTutor(malformedCourse, Path.Combine(evidence, "NeverAnApplication"), Path.Combine(evidence, "NeverRuntimeAssets"));
        Check(!inspectedMalformed.CanPublish && inspectedMalformed.ReviewIssues.Any(issue => issue.Severity == "Error"),
            "Malformed tutor source inspection returns an explicit non-publishable result instead of throwing out of a UI handler");
    }
    catch (Exception error) { Check(false, "Malformed tutor source inspection returns an explicit non-publishable result instead of throwing out of a UI handler", error.ToString()); }
    seed = BundleFixture.Create(Path.Combine(evidence, "Seed"));
    string pin = BundleFixture.Hash(seed.ManifestPath); var seedBefore = seed.AllHashes();
    var baseline = SharedContentPublisher.Verify(seed.Root, pin);
    Check(baseline.ManifestSha256 == pin && Path.GetFullPath(baseline.ManifestPath) == Path.GetFullPath(seed.ManifestPath),
        "An independently assembled complete bundle verifies offline with its externally pinned manifest hash");
    Check(seedBefore.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(seed.AllHashes().OrderBy(x => x.Key, StringComparer.Ordinal)),
        "Successful verification performs no writes, repairs, builds or publication");
    var tutorChild = BundleFixture.Read(Path.Combine(seed.ArtifactRoot("tutor"), "manifest.json"));
    Check(tutorChild["reviewWarningCount"]!.GetValue<int>() > 0 && tutorChild["reviewedChangedItems"]!.AsArray().Count == 0,
        "Unchanged original draft material remains honestly identified rather than relabelled expert-reviewed");

    var optional = Case("producer-without-code-sha"); optional.Manifest["producer"]!["sourceCommit"] = null; optional.Save();
    Check(SharedContentPublisher.Verify(optional.Root).ManifestSha256 == BundleFixture.Hash(optional.ManifestPath), "A nullable producer code SHA remains distinct from the externally pinned publication commit");
    Reject(seed, "A different trusted manifest hash cannot certify this bundle", new string('f', 64));

    foreach (var test in new (string Name, Action<JsonObject> Edit)[]
    {
        ("unsupported-schema", m => m["schemaVersion"] = 2),
        ("wrong-format", m => m["format"] = "another-format"),
        ("wrong-publisher", m => m["publisher"] = "UnknownPublisher"),
        ("missing-publication-id", m => m.Remove("publicationId")),
        ("noncanonical-publication-id", m => m["publicationId"] = Guid.NewGuid().ToString("N")),
        ("malformed-publication-date", m => m["publishedAtUtc"] = "yesterday"),
        ("timezone-free-publication-date", m => m["publishedAtUtc"] = "2026-11-04T12:00:00"),
        ("invalid-source-sha", m => m["helpSourceSha256"] = "not-a-hash"),
        ("tutor-source-proof-mismatch", m => m["tutorSourceSha256"] = new string('c', 64)),
        ("invalid-producer-commit", m => m["producer"]!["sourceCommit"] = "latest"),
        ("missing-tutor-artifact", m => m["artifacts"]!.AsObject().Remove("tutor")),
        ("extra-artifact", m => m["artifacts"]!["unreviewed"] = new JsonObject()),
        ("wrong-website-path", m => m["artifacts"]!["website"]!["path"] = "DocsElsewhere"),
        ("traversal-artifact-root", m => m["artifacts"]!["desktopHelp"]!["path"] = "../DesktopHelp"),
        ("missing-source-proof-binding", m => m["artifacts"]!["tutor"]!.AsObject().Remove("reviewProofBinding")),
        ("wrong-source-proof-binding", m => m["artifacts"]!["tutor"]!["reviewProofBinding"] = "rehash-final-records")
    })
    { var sample = Case(test.Name); test.Edit(sample.Manifest); sample.Save(); Reject(sample, "Reject " + test.Name); }

    var missingManifest = Case("missing-manifest"); File.Delete(missingManifest.ManifestPath); Reject(missingManifest, "A partial checkout without the shared manifest cannot be consumed");
    var truncated = Case("truncated-manifest"); File.WriteAllText(truncated.ManifestPath, "{\"schemaVersion\":1,"); Reject(truncated, "Truncated manifest JSON is rejected");
    var duplicatedKey = Case("duplicate-json-key"); File.WriteAllText(duplicatedKey.ManifestPath, File.ReadAllText(duplicatedKey.ManifestPath).Insert(1, "\"format\":\"another-parser-might-use-this\","));
    Reject(duplicatedKey, "Duplicate JSON member names cannot produce different cross-platform interpretations");
    var stalePin = Case("pin-after-rehash"); stalePin.Manifest["producer"]!["version"] = "Changed after the pinned fetch"; stalePin.Save();
    Reject(stalePin, "Rewriting an otherwise coherent manifest cannot bypass the external trusted manifest pin", pin);

    foreach (string name in new[] { "website", "desktopHelp", "tutor" })
    {
        var altered = Case(name + "-altered"); string relative = BundleFixture.Text(altered.Files(name)[0], "path"); File.AppendAllText(Path.Combine(altered.ArtifactRoot(name), relative), "Changed bytes");
        Reject(altered, "Reject altered " + name + " payload bytes");
        var missing = Case(name + "-missing"); relative = BundleFixture.Text(missing.Files(name)[0], "path"); File.Delete(Path.Combine(missing.ArtifactRoot(name), relative));
        Reject(missing, "Reject missing " + name + " payload file");
        var extra = Case(name + "-extra"); File.WriteAllText(Path.Combine(extra.ArtifactRoot(name), "unlisted.txt"), "Not part of the committed payload");
        Reject(extra, "Reject unlisted " + name + " payload file");
        var count = Case(name + "-filecount"); count.Artifact(name)["fileCount"] = count.Files(name).Count + 1; count.Save();
        Reject(count, "Reject incorrect " + name + " file count");
    }
    foreach (string badPath in new[] { "../outside.txt", "/absolute.txt", "C:/outside.txt", "nested\\outside.txt", "nested/./x.txt", "nested//x.txt", "nested/../../x.txt" })
    { var sample = Case("invalid-relative-path"); sample.Files("website")[0]!["path"] = badPath; sample.Save(); Reject(sample, "Reject unsafe relative artifact path " + badPath); }

    var duplicate = Case("duplicate-file"); duplicate.Files("website").Add(duplicate.Files("website")[0]!.DeepClone()); duplicate.Artifact("website")["fileCount"] = duplicate.Files("website").Count; duplicate.Save();
    Reject(duplicate, "Duplicate inventory paths cannot count as two payload files");
    var collision = Case("case-collision"); var first = collision.Files("website")[0]!.DeepClone(); first["path"] = BundleFixture.Text(first, "path").ToUpperInvariant(); collision.Files("website").Add(first); collision.Artifact("website")["fileCount"] = collision.Files("website").Count; collision.Save();
    Reject(collision, "Case-colliding inventories are rejected even on a case-insensitive host");
    var pathCase = Case("path-case"); var aliasRow = pathCase.Files("desktopHelp").Single(row => BundleFixture.Text(row, "path") == "Data/Alias.xml")!; aliasRow["path"] = "data/alias.xml"; pathCase.Save();
    Reject(pathCase, "Manifest filename case must match the actual tree exactly");

    foreach (var pair in new[] { ("website", "topicCount"), ("website", "searchDocumentCount"), ("desktopHelp", "topicCount"), ("desktopHelp", "searchDocumentCount"), ("desktopHelp", "contextCount"), ("tutor", "lessonCount"), ("tutor", "questionCount"), ("tutor", "trackCount") })
    { var sample = Case("wrong-count"); sample.Artifact(pair.Item1)[pair.Item2] = 999; sample.Save(); Reject(sample, "Declared " + pair.Item1 + "." + pair.Item2 + " must match its actual payload"); }
    var bankVersion = Case("bank-version"); bankVersion.Artifact("tutor")["bankVersion"] = "a-different-bank"; bankVersion.Save(); Reject(bankVersion, "Shared and child tutor bank versions must agree");
    var receipt = Case("desktop-snapshot-proof"); receipt.RewriteDesktopReceipt(child => child["publicationSnapshotSha256"] = new string('c', 64));
    Reject(receipt, "Desktop delivery receipt must bind the shared saved-help snapshot");
    var wrongNumeric = Case("numeric-context"); string alias = Path.Combine(wrongNumeric.ArtifactRoot("desktopHelp"), "Data/Alias.xml");
    File.WriteAllText(alias, File.ReadAllText(alias).Replace("ResolvedId=\"1001\"", "ResolvedId=\"9999\"", StringComparison.Ordinal));
    RefreshDesktopChildHashes(wrongNumeric); Reject(wrongNumeric, "Numeric context IDs must match the exact child receipt after payload hashes are refreshed");
    var dtd = Case("dtd-context"); alias = Path.Combine(dtd.ArtifactRoot("desktopHelp"), "Data/Alias.xml");
    File.WriteAllText(alias, "<!DOCTYPE CatapultAliasFile [<!ENTITY benign 'synthetic'>]>\n" + File.ReadAllText(alias));
    RefreshDesktopChildHashes(dtd); Reject(dtd, "Context XML rejects DTD processing without invoking external resources");
    var pendingWebsite = Case("pending-website"); File.WriteAllText(Path.Combine(pendingWebsite.ArtifactRoot("website"), ".statsdirect-desktop-help.publishing.json"), "{}"); pendingWebsite.Refresh("website"); pendingWebsite.Save();
    Reject(pendingWebsite, "A pending producer marker cannot become published website content even if inventoried");
    var pendingRoot = Case("pending-published-root"); File.WriteAllText(Path.Combine(pendingRoot.Root, "Published/.AuthoredTutor.publishing"), "synthetic marker");
    Reject(pendingRoot, "An incomplete-publication marker cannot be hidden beside the published artifact roots");

    var staticBank = Case("executable-bank"); File.AppendAllText(Path.Combine(staticBank.ArtifactRoot("tutor"), "bank.mjs"), "\nglobalThis.__unexpectedExecution = true;\n");
    RefreshTutorChildHashes(staticBank); Reject(staticBank, "Tutor static bank is parsed as data, never accepted as an executable program");
    var mismatch = Case("bank-json-mismatch"); string bankPath = Path.Combine(mismatch.ArtifactRoot("tutor"), "bank.mjs");
    string bankBefore = File.ReadAllText(bankPath), changedBank = bankBefore.Replace("FIXTURE-Q1", "DIFFERENT-Q1", StringComparison.Ordinal);
    if (changedBank == bankBefore) throw new InvalidOperationException("The intended mismatched-bank fixture did not change any bytes.");
    File.WriteAllText(bankPath, changedBank);
    RefreshTutorChildHashes(mismatch); Reject(mismatch, "Static bank and questions JSON must match even when all inventory hashes were refreshed");
    var embedded = Case("embedded-mirror"); embedded.RewriteTutor((lessons, _, _, _) => lessons[0]!["practiceQuestions"]![0]!["stem"] = "A stale embedded copy");
    Reject(embedded, "An embedded lesson question cannot diverge from the authoritative flat bank");
    var unreviewed = Case("unreviewed-changes"); unreviewed.RewriteTutor((_, _, _, child) => child["unreviewedChangedItems"] = new JsonArray(new JsonObject { ["kind"] = "questions", ["id"] = "FIXTURE-Q1" }));
    Reject(unreviewed, "Unreviewed changed material cannot be represented as publishable curriculum");
    var gate = Case("review-gate-false"); gate.RewriteTutor((_, _, _, child) => child["reviewGatePassed"] = false); Reject(gate, "A failed tutor author-review gate blocks the whole bundle");
    var fakeReview = Case("draft-labelled-reviewed"); fakeReview.RewriteTutor((_, _, _, child) => child["reviewedChangedItems"] = new JsonArray(new JsonObject { ["kind"] = "questions", ["id"] = "FIXTURE-Q1", ["reviewer"] = "Fixture reviewer", ["date"] = "2026-11-05", ["notes"] = "Synthetic test only", ["contentSha256"] = new string('d', 64) }));
    Reject(fakeReview, "A declared reviewed item cannot still have a draft record or unrelated review proof");
    var malformedReview = Case("nonobject-reviewed-item"); malformedReview.RewriteTutor((_, _, _, child) => child["reviewedChangedItems"] = new JsonArray(7));
    Reject(malformedReview, "Malformed changed-item review entries cannot be silently skipped by the shared validator");
    var targets = Case("invalid-target"); targets.RewriteTutor((lessons, questions, _, _) => { questions[0]!["difficulty"] = "expert-certified"; lessons[0]!["practiceQuestions"]![0]!["difficulty"] = "expert-certified"; });
    Reject(targets, "Invalid optional tutor classifications cannot bypass validation through a coherent mirror");
    var metadata = BundleFixture.Create(Path.Combine(evidence, "ReviewedMetadataSeed"), reviewedMetadata: true);
    string metadataPin = BundleFixture.Hash(metadata.ManifestPath); var beforeMetadata = metadata.AllHashes();
    Check(SharedContentPublisher.Verify(metadata.Root, metadataPin).ManifestSha256 == metadataPin && beforeMetadata.OrderBy(x => x.Key, StringComparer.Ordinal).SequenceEqual(metadata.AllHashes().OrderBy(x => x.Key, StringComparer.Ordinal)),
        "Validation preserves reviewed synthetic targets, Unicode and unknown question/option metadata without rewriting them");
    var syncedLesson = JsonNode.Parse(File.ReadAllText(Path.Combine(metadata.ArtifactRoot("tutor"), "lessons.json")))!.AsArray()[0]!.AsObject();
    Check(BundleFixture.Text(syncedLesson["review"], "contentSha256") != TutorContentFingerprint.Record(syncedLesson)
        && BundleFixture.Text(metadata.Artifact("tutor"), "reviewProofBinding") == BundleFixture.ReviewBinding,
        "Legitimate source-bound lesson review survives generated question synchronisation without claiming a final-record rehash");
    var reviewMismatch = metadata.Copy(Path.Combine(evidence, "Cases", (++sequence).ToString("D2") + "-review-metadata-mismatch"));
    reviewMismatch.RewriteTutor((lessons, questions, _, _) => { questions[0]!["review"]!["reviewer"] = "Different reviewer"; lessons[0]!["practiceQuestions"]![0]!["review"]!["reviewer"] = "Different reviewer"; });
    Reject(reviewMismatch, "Published records cannot contradict their declared source-bound changed-item review metadata");
    var invalidReviewDate = metadata.Copy(Path.Combine(evidence, "Cases", (++sequence).ToString("D2") + "-invalid-review-date"));
    invalidReviewDate.RewriteTutor((lessons, questions, _, child) =>
    {
        foreach (var item in child["reviewedChangedItems"]!.AsArray()) item!["date"] = "not-a-date";
        lessons[0]!["review"]!["date"] = "not-a-date"; questions[0]!["review"]!["date"] = "not-a-date";
        lessons[0]!["practiceQuestions"] = new JsonArray(questions[0]!.DeepClone());
    });
    Reject(invalidReviewDate, "Invalid review dates cannot pass the shared validator while failing a consumer's tutor contract");
}
catch (Exception error) { Check(false, "Independent shared-bundle checks completed", error.ToString()); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failures == 0, assertions = checks.Count, failed = failures, fixture = evidence, noNetwork = true, noActualPublication = true, checks };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.assertions, report.failed, report.fixture }));
return failures == 0 ? 0 : 1;

static void RefreshTutorChildHashes(BundleFixture sample)
{
    string directory = sample.ArtifactRoot("tutor"); var child = BundleFixture.Read(Path.Combine(directory, "manifest.json"));
    foreach (var row in child["files"]!.AsArray().Select(node => node!.AsObject())) { string relative = BundleFixture.Text(row, "path"); row["bytes"] = new FileInfo(Path.Combine(directory, relative)).Length; row["sha256"] = BundleFixture.Hash(Path.Combine(directory, relative)); }
    File.WriteAllText(Path.Combine(directory, "manifest.json"), child.ToJsonString() + "\n"); sample.Refresh("tutor"); sample.Save();
}

static void RefreshDesktopChildHashes(BundleFixture sample)
{
    string directory = sample.ArtifactRoot("desktopHelp"), receiptPath = Path.Combine(directory, DesktopHelpPublisher.ReceiptFileName);
    var receipt = BundleFixture.Read(receiptPath); var rows = new JsonArray();
    foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(path => path != receiptPath).OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal))
        rows.Add(new JsonObject { ["path"] = Path.GetRelativePath(directory, path).Replace('\\', '/'), ["bytes"] = new FileInfo(path).Length, ["sha256"] = BundleFixture.Hash(path) });
    receipt["files"] = rows; File.WriteAllText(receiptPath, receipt.ToJsonString() + "\n"); sample.Refresh("desktopHelp"); sample.Save();
}
