# Shared content contract checks

Run `dotnet run -c Release --project tests/StatsDirectHelpEditor.PublishedContentChecks -- <editor-root>` after the Core implementation is built. This is a `net10.0` console test with no Windows UI, network, Git operations, installer, learner-data import or real publication.

The suite constructs a small marked help project and a clearly synthetic tutor baseline under a fresh evidence folder. It generates the child artifacts with their public producer APIs, then independently constructs the shared manifest and invokes the public offline `SharedContentPublisher.Verify` API. Every rejection also checks that validation left all input bytes unchanged. It covers partial/malformed bundles, external manifest pinning, path containment and exact case, full payload inventories, counts and numeric contexts, static-bank/JSON coherence, classification and source-bound review metadata. Generated source synchronisation is deliberately tested without incorrectly rehashing the final lesson as a source-review proof.

`Schema/StatsDirectPublishedContent.schema.json` documents the structural JSON contract. Complete directory inventories, hashes, count/content relationships and tutor review/bank consistency need the semantic validator in addition to the schema. An external resolved publication Git commit binds the manifest and all artifacts; `producer.sourceCommit` is the code provenance and may be null. Neither an unsigned manifest nor these synthetic tests proves independent educational approval or awards CPD credit.

The production bundle must come from the author's saved help and tutor source. Test fixtures must never be copied into `Docs`, `Published` in the real repository, application resources or a release.
