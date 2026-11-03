# StatsDirect Help Editor prototype

A separate C#/.NET Windows authoring application. It edits the **HelpSource working copy**, independently of the original Flare project. No Flare licence, Node installation, web server or internet connection is required to use the built editor or rebuild help.

## Start

Double-click `Start-StatsDirectHelpEditor.cmd`, or open `App/StatsDirectHelpEditor.exe`. Both open `HelpSource` automatically. The .NET 10 Windows Desktop runtime and Microsoft Edge WebView2 runtime are required; both are already present on this PC.

The application is a prototype for evaluation. Its default window opens maximized to suit this PC's display scaling.

Version 0.1.1 adds the turquoise StatsDirect delta with a book-and-pencil badge to the executable, window/taskbar, installed-app entry and setup. The installer creates both Start-menu and desktop shortcuts. The icon includes ten sizes from 16 to 256 pixels for different display scaling settings.

Version 0.1.2 uses the independent [StatsDirect help schema](Schema/StatsDirectHelp.xsd) for authoring extensions, with the `sdh` prefix. Existing Flare source can still be imported and edited. Equations, conditional text, R dropdowns and the generated help retain their appearance; the builder resolves everything locally and never downloads a schema to build help. See [the schema documentation](Schema/README.md).

## Windows installer and public source

The Help Editor is a separate application from StatsDirect. Its source is maintained in the public [statisticalhelp repository](https://github.com/iain-buchan/statisticalhelp/tree/master/HelpEditor). Signed evaluation downloads are published under [Help Editor releases](https://github.com/iain-buchan/statisticalhelp/releases).

The x64 setup installs for the current user, includes the .NET runtime, and installs Microsoft's Evergreen WebView2 runtime if needed. Program files go under `%LOCALAPPDATA%\Programs\StatsDirectHelpEditor`. Editable help, generated output and browser profiles go under `%LOCALAPPDATA%\StatsDirect\HelpEditor`. The initial help working copy is created once; upgrades and uninstalling the application leave that user's work intact. The included help is an independent working copy. GitHub connection and publication are explicit commands.

The installer and public source exclude browser profiles, sign-in state, generated build/test output, recovery archives and personal Flare history/publishing destinations. Required third-party licences remain alongside their assets. The editable source already on this PC and the StatsDirect release installers are separate from this package.

For development, install the .NET 10 SDK, then run `dotnet build StatsDirectHelpEditor.slnx -c Release` from the Help Editor source directory. Start the source build with `dotnet run -c Release --project src/StatsDirectHelpEditor.App`. `Build-Help.ps1 -CompileBuilder -Target All` also works from a fresh checkout before the GUI is packaged. Installer builds use WiX 7.0.1 through its pinned NuGet SDK; see `installer/README.md` and `Build-Installer.ps1`. Signing is optional for local builds and requires a certificate in the builder's Windows certificate store; private keys are never part of the source or installer.

## Edit help

- Choose a page in **Contents**, or use the **All pages** title/filename filter. All 404 copied topics are available, including pages absent from Contents.
- **Formatted** has a grouped formatting ribbon: font and point size, bold/italic/underline, text and highlight colours, headings, alignment, lists and clear formatting. Select text before applying formatting. Undo and Redo include formatting, links and equation changes.
- **Link** or **Ctrl+K** opens a searchable list of help pages, with a separate Website/email mode. Find a page by title or filename, choose it and apply the link. An optional section anchor targets a position within the page.
- Double-click an existing link, or right-click it and choose **Edit link**, to change its destination or label. Changing only the destination preserves the link's formatted text. **Remove link** keeps the text. Right-click **Open linked page in new window** opens an independent editor window; external web links open in the system browser. Closing one editor window leaves other editor windows open.
- Double-click an equation to open MathLive, or choose **+ Equation** to insert one. Apply or Cancel an open link/equation dialog before navigating or saving that page.
- **HTML source** edits the complete XHTML file. R-code dropdowns and specialised markup are protected during formatted editing; use this view to edit their source. Invalid XHTML is reported without overwriting the saved file.
- **Save** writes only the selected file in the working copy. An unchanged page is acknowledged without rewriting it, rescanning all topic titles or invalidating previews. Changes made by another editor while the current draft is being read are checked before writing. Changing pages or closing prompts for unsaved changes.
- **Edit > Find and replace in all pages…** (**Ctrl+Shift+H**) searches every saved `.htm`/`.html` help page, including pages absent from Contents. Review the page, line and before/after context for each occurrence, tick the matches you want, then choose **Replace selected** or **Replace all**. **Match case** and **Whole words** are optional. The default **Page text** scope preserves markup and skips equations, R/code blocks and link URLs; **HTML source** also searches tags, attributes and embedded code. Text replacements handle XML entities and phrases spanning inline formatting. **Open page** selects the exact match in HTML source.
- **Undo last replace** restores the latest replacement batch during the current editor session; it is also available in the Edit menu after closing the dialog. It refuses to overwrite pages edited since that batch. All proposed pages are validated and checked against the reviewed snapshot before a replacement starts; a changed, locked or read-only page blocks the batch. Encoding, BOM and line endings are preserved. Resolve an unsaved current page through the usual Save/Discard/Cancel prompt before opening the batch dialog. Replacing updates the page list and visible previews and marks saved output out of date. Source changes still require an explicit GitHub commit/push to publish.
- **New page** creates a page and adds it to Contents. **Delete page** keeps a local recovery copy. Use **File > Deleted pages…** to restore the original page and its Contents entries, even after restarting the editor. Restoration preserves later Contents edits and refuses to overwrite an existing page. Topics still referenced by another page or an application context ID cannot be deleted.
- In Contents, use **Page +** to add an existing page, **Section +**, **Rename**, **Link**, arrows to reorder/nest/outdent, and **Remove**. These navigation changes save automatically. Removing a Contents entry keeps the page file. Commands are also available in the **Contents** menu when the toolbar is narrow.

The editor preserves source paths, anchors, Flare equation wrappers, R whitespace, metadata and the context-help mapping. In Formatted, both Desktop/Public source variants remain editable; their condition is shown in a tooltip.

There is one row of page-view tabs: **Formatted**, **HTML source**, **Browser view** and **App view**. Tabs have bold labels, dark two-pixel borders and pale blue inactive backgrounds. The selected tab is navy with white text and a gold underline; keyboard focus and High Contrast mode remain visible. Formatted is the editing surface, with visible editing aids and both conditional variants. Browser view shows the complete generated Public website, with its navigation, search, artwork, equations and shared footer. It includes the current unsaved page snapshot without writing it to source or replacing a saved build. Following a preview link asks about unsaved changes before navigating; Cancel keeps the draft and preview intact.

**App view** opens the real StatsDirect Windows help host in a resizable pane on the right, alongside the current editor. It uses the complete Desktop output, with StatsDirect's Back/Forward/Contents/Print toolbar and Search. **Pop out** opens the same help browser in a separate window; **Dock** returns it without losing its reading position, history or expanded R sections. **Close** hides the pane. Formatted and HTML source remain available while the pane is open; select App view to refresh it after further typing. Saving or selecting another page refreshes visible previews. At a narrow window size, the author's Contents pane collapses to keep the editor usable, then returns when space permits. Drafts, caret/scroll position and Undo history survive view switches.

Browser and App previews reuse the generated site when you navigate between saved topics. Revisiting an unchanged view also keeps its live browser document, including scroll position, expanded R code and search state. Changes to source, Contents, artwork, styles, design or the current draft regenerate the affected preview; ordinary Contents clicks do not rebuild all pages. Missing critical preview assets trigger repair. Contents section links retain their anchors; navigation within the current page preserves the draft and Undo history.

## Build and search

Choose **App view** or **Browser view** to select the Desktop or Public target. Use **File > Build help and search (Ctrl+B)** or **File > Preview site in browser (F5)**. Saved outputs go only to `Build/Desktop` or `Build/Public` beside this README. F5 rebuilds the selected target and opens the complete local site, with Contents and full-text search, in your default browser. It asks about unsaved page edits before building. Returning to Formatted or HTML source keeps the last selected build target. **File > Preview site in editor** offers the separate whole-site viewer. The in-editor previews use independent `Build/EditorPreview` folders so a draft preview cannot replace a saved output.

The independent C# builder converts supported topic-level MadCap features into ordinary HTML, includes local MathJax for equations, preserves R-code dropdowns as HTML details, writes context IDs to `Data/Alias.xml`, and rebuilds a title/keyword/body search index. Desktop/Public conditions and the corresponding shared footer are respected. The footer comes from the selected target's existing master page, including its links; scripts are excluded. Shared-footer editing still requires editing the master-page source externally. The builder does not run Flare or its publishing destinations.

**Flare original** is the default output appearance, reproducing the published Flare header, navigation, responsive layout and topic styling. **Readable Contents** keeps the prototype's larger, clearer navigation as an optional design. The editor's **Design** selector also offers **Clean blue** and **Warm paper** colour schemes, separately for app and website help. A choice previews immediately and is saved in the working project's optional `Project/HelpEditorDesign.json`; rebuild to apply it to the whole site. Generated help has a fixed design, with no reader-facing appearance selector or saved browser preference. Designs do not rewrite topic source, mathematics or artwork. Navigation and search are independently implemented, with no Flare runtime dependency. The original design keeps each topic and its navigation in one document at its canonical `.htm` URL; direct links, new tabs, anchors and browser history work offline. The optional designs retain the prototype's framed topic pane. Exact visual comparisons against the live site and local Flare outputs are recorded in `evidence/FlareAppearance` and `evidence/FlareAllTopicPixels`. Search is rebuilt from current source, so result ranking and snippets can differ from Flare.

Search uses a compact inverted index built in C#, with title and keyword weighting. No additional search library or service is needed. The new output is for prototype evaluation; it has not replaced any published help or been added to the StatsDirect installers.

The default design has been checked at desktop, tablet and phone widths, including content flow after large equations. Original artwork is byte-identical. Small browser raster and scroll-rounding differences are recorded separately; exact pixel identity is reported only for the screenshots that achieve it. Updated reference links and the restored Incidence and Prevalence entry remain intentional source improvements. Long topics print in full, including when the mobile Contents pane is open.

The output header reuses Flare's original StatsDirect/StatisticalHelp logo at 228 × 33 pixels. The existing StatsDirect icon also appears in browser tabs, including when a topic is opened directly. Both assets are copied unchanged and work offline.

Unchanged equations reuse their original, self-contained Flare SVG artwork. A content-addressed catalogue verifies the exact source and SVG hashes; an edited or new equation automatically uses the bundled offline MathJax renderer. Preserved source is also rendered to visually hidden MathML for screen readers. No mathematics is flattened in the editable source. Equation wrappers allow complete SVG glyphs to remain visible and do not add vertical scroll controls.

## GitHub

The **GitHub** menu offers Clone, Status/review changes, Fetch, fast-forward Pull, New working branch, **Rebuild website and commit**, and Push. It uses the Git for Windows installation and its existing credentials; no credentials are stored in the editor.

Git for Windows is required only for these GitHub operations. Sign in once under the same Windows account as the editor, using Git Credential Manager or GitHub CLI (`gh auth login --hostname github.com`, followed by `gh auth setup-git`). The editor reuses that saved login; it does not open an account/password prompt during a background Git command. Your account needs write permission to the selected repository. Clone and edit a fork if you do not have permission to push to the original repository.

Use **GitHub > Status / review changes…** after reviewing the generated output. It opens an on-demand, read-only dialog with the working folder, branch/upstream, source versus other file changes, cached ahead/behind comparison and last successful fetch time. Unsaved drafts are explicitly excluded from commits. Opening or closing this dialog keeps your page, caret, preview and build target unchanged. A compact, clickable status-bar badge keeps local/cached status visible during editing. Only explicit Fetch checks the remote; neither opening the dialog nor refreshing local status makes a network request.

The supplied HelpSource is deliberately a plain independent copy. To enable Git sync, use **GitHub → Clone project**: this creates a separate checkout under `WorkingCopies`, opens a new `help-editor/...` branch, and leaves HelpSource intact. A clone starts with the repository's version of the help; it does not silently overwrite that version with this working copy.

After checking your changes in the Website preview, choose **GitHub > Rebuild website and commit…**. This always rebuilds the Public target from saved source, including all pages, Contents, full-text search, artwork, equations and the website footer. It stages the build privately before refreshing the checkout's capital-D **`Docs`** folder. The commit review shows page/search counts and any build warnings. Author name and email come from that working copy's Git configuration and can be reviewed or entered in the dialog. The commit includes `Content`, `Project`, `RCode`, `StatsDirect.flprj` and the regenerated `Docs` together. Local recovery copies, private build staging and unrelated files are excluded. Existing `CNAME` and `.nojekyll` settings are retained; obsolete generated pages are removed. Unknown or hand-edited output files are refused instead of silently overwritten.

Push is explicit and limited to working branches. It checks that the committed website still matches the source and pushes the exact verified commit; saved source or output changes require a new build and commit. A working-branch push prepares the changes for review. **Merging the reviewed branch into `master` triggers the existing GitHub Pages workflow**, which deploys `Docs` to the live StatisticalHelp site. The editor does not merge or push the default branch automatically, and does not replace the separate StatsDirect website or installers. Pull refuses dirty or divergent work rather than stashing, resetting or automatically merging it.

Deleted-page recovery copies stay in the working copy's `.help-editor-recovery` folder. They are retained locally and are excluded from generated help and normal Git commits. Keep that folder with the working copy when moving it to another location.

Git sync was tested against local bare repositories, including two independent collaborators and regeneration/commit/push of all 404 real help topics, without changing the live GitHub repository. Publication checks cover source/output freshness, failed builds, removed pages, metadata, recovery exclusions and Git line-ending normalization.

Verified help commits pin the reviewed source and generated website to one Git tree, so a later save cannot be mixed into an older website build. The branch update checks its expected previous commit, and later working-file or staged changes remain separate. This editor-managed commit does not run local commit hooks or sign the commit; push and merge remain separate actions. Custom Git content filters and `ident` expansion on help files are refused because they can alter the verified text during staging. Ordinary Git line-ending normalization is supported.

## ChatGPT text review

Choose **Edit > Polish current page with ChatGPT…**. This reviews the current draft, including unsaved edits. Use **Sign in with ChatGPT** for the editor's own connection, then **Suggest changes**. Signing in opens the official browser authentication page. The verified OpenAI component is downloaded on first use if a compatible reviewed component is not already installed. AI review requires an internet connection and ChatGPT account access; ordinary help editing and building remain offline.

ChatGPT receives the current page's editable prose and a catalog of every help page, with read-only tools to search and read any of them for context, including pages outside Contents. The current page uses your draft; other pages use a snapshot of their saved text. It has no execution or publishing tools. Help text is shared only when you request suggestions. Its connection is separate from StatsDirect and the Codex desktop application; signing out here leaves those applications alone.

The review shows deletions in red strike-through and insertions in green underline, with a reason for each change. **Accept change**, **Reject change**, **Accept all** and **Reject all** record your decisions. **Apply accepted changes** puts only accepted suggestions into the unsaved page, as one normal Undo step. Pending and rejected changes are omitted. **Close** discards this review without editing the page; **Stop** cancels a running request. Save and GitHub publishing remain explicit. Reviews are not retained after closing the dialog.

Suggestions can change ordinary visible prose only. Equations, R/code, markup, link destinations, metadata, numeric values and visible URLs are protected. The exact original text must still match before an accepted change is applied. These checks preserve the document structure; the author still decides whether suggested statistical wording is correct.

## Source and build

Open `StatsDirectHelpEditor.slnx` in Visual Studio, or use the .NET 10 SDK:

```powershell
dotnet publish src/StatsDirectHelpEditor.App/StatsDirectHelpEditor.App.csproj -c Release --no-self-contained -o App
```

Close the running prototype before republishing its executable. The Core library and CLI are platform-neutral; the current authoring shell uses WinForms/WebView2 on Windows.

Build help without the GUI or Flare, from any working directory:

```powershell
& 'C:\Develop\StatsDirectHelp\Build-Help.ps1' -Target All
```

The packaged **`App/Builder/StatsDirectHelpBuilder.exe`** uses the installed .NET 10 runtime. Ordinary XHTML builds do not compile C# and do not need the SDK. The editor can remain open; only saved source is included, so unsaved drafts remain untouched. Codex and Claude should run this command after editing raw XHTML instead of asking Iain to run Flare; `AGENTS.md` and `CLAUDE.md` record that workflow.

`All` (or the default `Both`) generates **`Build/Desktop`** for DesktopHelp and **`Build/Public`** for Website/GitHub. Website and GitHub have the same authored public content, conditions, styles and footer, so the public HTML is generated once. `-Target DesktopHelp`, `Website` or `GitHub` selects one variant. Builds regenerate pages, Contents, search, equations, artwork and R sections; no Flare destinations or publishing commands are run. The script returns JSON with output paths, page/search counts, warnings and target mappings; failures return a nonzero exit status.

Use `-ProjectPath <marked-working-copy>` and `-OutputRoot <separate-folder>` for another source checkout. Direct console use is also supported:

```powershell
& 'C:\Develop\StatsDirectHelp\App\Builder\StatsDirectHelpBuilder.exe' build `
    --project 'C:\Develop\StatsDirectHelp\HelpSource' `
    --output 'C:\Develop\StatsDirectHelp\Build\Public' --target Website
```

The console automatically finds its sibling `App/Web` assets, or accepts `--assets`. It also supports `list --project <copy>` and `--help`. Only after changing the builder's C# implementation, run `Build-Help.ps1 -CompileBuilder -Target All`; that development step needs the SDK. When publishing a new GUI package, also publish `src/StatsDirectHelpEditor.Cli/StatsDirectHelpEditor.Cli.csproj` into `App/Builder` so both use the current Core library. Keep `App/Web` and the complete Builder folder together.

## Checks and provenance

The supplied copy originally contained 834 source/project/R-code files, based on Flare source commit `2b5e68a`, including the local equation and navigation edits present when copied. `evidence/sample-copy-manifest.json` records the complete snapshot and hashes.

The later reference-link audit corrected two stale Springer book URLs in `HelpSource/Content/references/reference_list.htm`, preserving their original editions, wording and anchors. The exact substitutions and supporting publisher/catalogue records are in `evidence/reference-link-corrections.json`; the original Flare project has not been edited. Comparisons against the old output recognize only these documented URL substitutions, while still checking every other reference character and link.

A subsequent relevance review replaced twelve regional library-holdings links with English Google Books or WorldCat records for the cited editions. Publisher pages for newer editions were rejected. Bland's 1996 reprint retains its citation while linking to the same 1995 second edition and ISBN. These changes are also listed in the correction manifest, bringing the total to fourteen documented substitutions. Useful archives of actual original works are retained.

The completed full-bibliography review covers all **363 references**. Following Iain's permission to use later-edition DOIs, **72 references** were improved in that pass; **296 references now link to DOIs**, and all Amazon links have been replaced. Later editions are labelled in the hyperlink text without changing the original citation. Other destinations use publishers, original author/institutional archives or reputable English-language catalogue records. The complete per-reference review and evidence are `evidence/all-reference-review.csv` and `.json`. There are now 79 documented link corrections relative to the original Flare baseline.

Two bibliographic details remain explicitly flagged: Abramowitz/Stegun's cited printing differs from the available scan (the link now uses NIST's authoritative handbook record), and Vives 2001 is listed under a different title in the coauthor's bibliography. The latter link identifies that bibliography rather than pretending to provide the original paper. Long reference URLs wrap within narrow help panes.

Detailed JSON reports are in `evidence` and reusable checks in `tests`. Validation covers C# page/contents/build operations, native Windows editing and navigation, Git with two isolated collaborators, browser editing, all 404 topics round-tripped without edits, every generated help page loaded offline, equations and R-code preservation. `MORNING-HANDOVER.md` records the latest complete results and remaining limitations.

Developer checks (requires the .NET SDK, Node and Playwright):

```powershell
.\Test-HelpEditor.ps1 -CompareFlare
```

The browser-print regression check is `node tests/FlarePrintChecks.cjs`. It prints the long References and ANOVA topics, checks complete paragraphs and footers, and repeats with the mobile Contents pane open. It uses Python with `pypdf`, plus Poppler's `pdfinfo` and `pdftoppm`, from the bundled developer runtime. Set `STATSDIRECT_PYTHON` to another Python executable and `STATSDIRECT_POPPLER_PATH` to its Poppler binary folder when needed. These tools are test dependencies only.

The copied Flare publishing destination files retain the old project's settings for provenance. Do not publish them from Flare: they point outside this independent working copy. The new editor's builder ignores these destinations and writes only local evaluation output.

The three existing Flare outputs (DesktopHelp, Website and GitHub) are compared with the prototype, including all 404 topic titles, artwork links, existing anchors, 404 context mappings, R blocks and each target's footer text/links. The one expected text/link difference is the new Incidence and prevalence link in the Rates overview; Contents contains the corresponding extra entry (261 versus 260). Rendered typography, charts, equations and the complete default navigation shell are checked separately, including desktop, tablet and phone layouts. Reports and side-by-side captures are in `evidence/flare-target-comparison.json` and `evidence/visual-fidelity-checks.json`.

Three legacy LaTeX forms used by the existing help are adapted for the MathLive editing view. All 90 affected expressions retain their original source unless you explicitly apply an equation edit. The builder adapts legacy bare line breaks for multiline rendering without rewriting source.

The original source, published help, current signed installers and verification records are protected by before/after hashes. This prototype does not sign, install or publish anything.

## Licences

The prototype source uses the MIT licence. The copied help retains its own `HelpSource/LICENSE`. Vendored MathLive 0.111.0 (MIT) and MathJax 3.2.2 (Apache-2.0) include their licence files in the Web assets. The WebView2 SDK licence and notices are included with the runnable application. The optional official OpenAI Codex app-server component retains its Apache-2.0 licence and notices under `Web/OpenAI`; the ChatGPT connection and component-installation code adapts StatsDirect's MIT implementation with attribution.

App view snapshots the six engine-independent StatsDirect Windows help/docking classes under `src/StatsDirectHelpEditor.App/AppHelp`, retaining their MIT notice. `AppHelp/provenance.json` records their original repository paths, commit and hashes, and the limited integration changes. The preview uses its own browser profile and does not modify the installed StatsDirect application or its help.
