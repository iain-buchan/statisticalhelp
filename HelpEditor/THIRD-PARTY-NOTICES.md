# Third-party components

StatsDirect Help Editor and its supplied help source are MIT licensed; see LICENSE.
The source and installer retain these additional notices beside the components:

| Component | Licence | Included notice |
| --- | --- | --- |
| StatsDirect dockable help host snapshots | MIT, Iain Buchan | `AppHelp/LICENSE.StatsDirect.txt`, `AppHelp/provenance.json` |
| MathLive 0.111.0 | MIT, Arno Gourdol and contributors | `Web/mathlive/LICENSE.txt` |
| KaTeX fonts | MIT, Khan Academy | `Web/mathlive/fonts/LICENSE.txt` |
| MathJax | Apache-2.0, MathJax Consortium | `Web/mathjax/LICENSE` |
| OpenAI client/runtime components | Apache-2.0 and upstream notices | `Web/OpenAI/LICENSE`, `Web/OpenAI/NOTICE` |
| Microsoft .NET runtime | MIT and upstream component terms | `ThirdParty/NETCore-runtime/LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` |
| Microsoft Windows Desktop runtime | MIT | `ThirdParty/WindowsDesktop-runtime/LICENSE` |
| Microsoft WebView2 SDK | Microsoft terms and upstream notices | `ThirdParty/WebView2-SDK/LICENSE.txt`, `NOTICE.txt` |

MathLive, fonts, MathJax and the StatsDirect host snapshots are included locally.
The installer includes the .NET runtime. The signed Microsoft Evergreen WebView2
bootstrapper installs the shared browser runtime only when missing. The optional
OpenAI runtime is obtained separately on first requested use; no account,
credentials or authentication state are distributed with the editor.

WiX Toolset 7.0.1 builds the MSI and setup executable. Its own source/build terms
remain at https://github.com/wixtoolset/wix and https://docs.firegiant.com/wix/osmf/.
No Flare executable, licence or generated MadCap runtime is needed by the editor.

Original help topics, mathematical artwork and StatsDirect branding are supplied
from this project's MIT-licensed help source. The source copy retains its LICENSE.
