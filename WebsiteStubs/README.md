# WebsiteStubs

Small pages that sit at FORMER addresses of www.statsdirect.com (and www.statsdirect.co.uk, which is the same site) and
send the reader on to the page's present address, so that links from Wikipedia, papers, documentation sites and old
bookmarks keep working. They are not part of the Flare project: Flare ignores this folder, and nothing here is built
into the CHM or the web help.

The folder is laid out like the web root. To install or restore the stubs, copy its CONTENTS into
`inetpub\statsdirect` on the server and let the `help` folder MERGE (never replace). No stub sits at an address where a
real page exists, so nothing is overwritten.

| Part | What it holds |
|---|---|
| `help\...\*.htm` (140) | Former names of help topics, e.g. `help\parametric_methods\utt.htm` sends the reader to `unpaired_t.htm`. Plain pages with an instant refresh, a visible link, and "noindex, follow" for search engines. |
| `help\resources\images\*.gif` (3) | Pictures that other sites display directly; the present help has them as .png. |
| root `*.aspx` (4) | `sd3technology.aspx` to `Technology.aspx`; `buy.aspx`, `buyoptions.aspx`, `licence.aspx` to `Download.aspx`. They answer with a permanent (301) redirect. |
| root `*.htm` (10) | `buy.htm`, `licence.htm` to `Download.aspx`; `info.htm` to `Specifications.aspx`; `try.htm`, `faq.htm`, `support.htm`, `uses.htm`, `contacts.htm`, `reviews.htm`, `revisions.htm` to the pages of the same name. |

Where the list came from (September 2026): a search of 836 Wikimedia wikis, the full text of open-access papers, the
general web and the Internet Archive's record of every address the site ever had, plus the author's own list of
relocated help pages. 34 of the help stubs mend addresses that other sites are known to link to (the most linked:
`utt.htm`, `chi_good.htm`, `gini_coefficient.htm`, `content/survival_analysis/logrank.htm`, `kend.htm`, `mwt.htm`); the
rest are old addresses that were prominent or that search engines still list. Each target was checked against the old
page as the Internet Archive last captured it.

If the help on the server is ever uploaded by REPLACING the `help` folder instead of merging into it, copy this folder
over again afterwards. If the site ever moves to a static host such as GitHub Pages, which cannot redirect, the
deployment step can copy this folder into the published site (the `.aspx` stubs would then need the folder trick
described in the move plan: a folder named e.g. `buy.aspx` holding an `index.html`).
