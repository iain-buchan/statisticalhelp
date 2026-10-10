This is the help system for www.statsdirect.com statistical software, and a knowledge-support resource for those learning or applying biostatistical methods.

The HTML5 source is integrated with navigation and indexing across pages using Madcap Flare software: www.madcapsoftware.com/products/flare/.

The StatisticalHelp project is supported by The University of Liverpool: www.liverpool.ac.uk/statisticalhelp.

Professor Iain E. Buchan (buchan@liverpool.ac.uk)

The three Flare targets all generate HTML5, with names matching their destinations:

| Target | Purpose |
| --- | --- |
| `GitHub` | Public help for GitHub Pages, published into `Docs`. The `publish-to-github.cmd` helper reads `Output/<username>/GitHub`. |
| `Website` | Public help for `statsdirect.com/help`; zip the contents of `Output/<username>/Website` for upload. |
| `DesktopHelp` | Offline help bundled with the Windows and Mac applications. |

Build a target with `madbuild.exe -project StatsDirect.flprj -target <name>`.
The `WEBHELP` and `SWHELP` condition names are retained for the existing source
topics; they select the public and desktop content respectively, not output formats.

Desktop help is built with the `DesktopHelp` target. It includes the
software-only content and publishes locally to the StatsDirect application's
`StatsDirectUI/Assets/Help` directory. The Windows application displays this
offline bundle in WebView2; it no longer uses a CHM file. Build the target with
MadCap Flare or `madbuild.exe -project StatsDirect.flprj -target DesktopHelp` (the
configured destination publishes automatically). Adjust the local destination
path for other checkouts. Do not substitute the web target: it uses different
conditions and publishes the public website.

`desktop.flmsp` and `desktop.css` provide the compact desktop presentation,
without website analytics. Context IDs remain in `STATSDIRECT.flali` and
`SD3ContextID.h`; Flare emits their resolved map as `Data/Alias.xml`.
