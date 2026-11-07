# Help Editor browser prototype

Run the browser host from the `statisticalhelp` checkout with the .NET 10 SDK.

```sh
dotnet run --project HelpEditor/src/StatsDirectHelpEditor.Browser -c Release --no-launch-profile
```

Open [the local editor](http://127.0.0.1:5848/) in your browser. The same command
works in PowerShell. To choose a different port or draft folder, add
`-- --port 5849 --workspace /path/to/an/empty/folder` after the command.

Search for a topic, edit it using the existing visual editor or XHTML view,
then save it in the local draft workspace. Website and Desktop help previews
run the existing builder with the current edit, so you can review a change
before saving it. The same Core validates and saves drafts on both platforms. Browser saves retain each existing topic’s line endings and UTF-8 BOM policy. A no-op save keeps its original bytes and file timestamp.

The host listens on `127.0.0.1`. Its first run copies the supplied HelpSource
into `HelpEditor/WorkingCopies/BrowserDraft/HelpSource` and subsequent runs
reopen that copy. The workspace accepts one server at a time. If two browser
tabs edit the same page, the second save checks the saved revision before
writing. Stop the host with Ctrl+C. Saved drafts stay in the workspace, and the
host removes its temporary build previews when it shuts down.

This pilot covers existing help topics and local previews. The Windows editor
continues to provide the tutor authoring and publication workflows.

The focused save checks run against a separate browser workspace. For example,
start the host with `-- --port 5853 --workspace /path/to/browser-save-format-checks`,
then run the following from the repository root with Node.js and the existing
Playwright test dependencies.

```sh
node HelpEditor/tests/browser-save-format-checks.cjs http://127.0.0.1:5853 /path/to/browser-save-format-checks
dotnet run --project HelpEditor/tests/StatsDirectHelpEditor.TopicFormatChecks -c Release -- HelpEditor
```

The browser test temporarily changes an existing topic in that disposable
workspace and restores its original bytes when finished.
