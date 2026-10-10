This is the help system for www.statsdirect.com statistical software, and a knowledge-support resource for those learning or applying biostatistical methods.

The HTML5 source is integrated with navigation and indexing across pages using Madcap Flare software: www.madcapsoftware.com/products/flare/.

The StatisticalHelp project is supported by The University of Liverpool: www.liverpool.ac.uk/statisticalhelp.

Professor Iain E. Buchan (buchan@liverpool.ac.uk)

Desktop help is now HTML5, built with the `SWHelp` target. It includes the
software-only content and publishes locally to the StatsDirect application's
`StatsDirectUI/Assets/Help` directory. The Windows application displays this
offline bundle in WebView2; it no longer uses a CHM file. Build the target with
MadCap Flare or `madbuild.exe -project StatsDirect.flprj -target SWHelp` (the
configured destination publishes automatically). Adjust the local destination
path for other checkouts. Do not substitute the web target: it uses different
conditions and publishes the public website.

`desktop.flmsp` and `desktop.css` provide the compact desktop presentation,
without website analytics. Context IDs remain in `STATSDIRECT.flali` and
`SD3ContextID.h`; Flare emits their resolved map as `Data/Alias.xml`.
