namespace StatsDirect.HelpEditor.Core;

internal static class ViewerFiles
{
    internal const string Html = """
<!doctype html>
<html lang="en" data-help-skin="__HELP_SKIN__"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>StatsDirect Help</title>__HELP_ICON__<link rel="stylesheet" href="help-viewer.css"></head>
<body><header><div class="help-identity">__HELP_IDENTITY__<span>Statistics, methods and software</span></div></header><main><aside id="help-navigation"><form id="search-form" role="search"><label for="search">Search help</label><div class="search-row"><input id="search" type="search" placeholder="Method, concept or R function"><button type="submit">Search</button></div></form>__HELP_NAVIGATION__<section id="results" aria-live="polite" hidden><p>Enter words to search all help pages.</p></section></aside><iframe id="topic" title="Help topic" src="contents.htm"></iframe></main><script src="contents-data.js"></script><script src="search-index.js"></script><script src="keyword-index.js"></script><script src="help-viewer.js"></script></body></html>
""";

    // The original Flare target uses a fluid side-navigation shell. Keep the
    // clearer prototype navigation as a separate, author-selected design.
    internal const string FlareHtml = """
<!doctype html>
<html lang="en" data-help-skin="__HELP_SKIN__"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>StatisticalHelp from StatsDirect</title>__HELP_ICON__<link rel="stylesheet" href="help-viewer.css"></head>
<body><header><a class="help-identity" href="contents.htm" aria-label="StatsDirect Help home">__HELP_IDENTITY__</a><form id="search-form" role="search"><label class="visually-hidden" for="search">Search help</label><div class="search-row"><input id="search" type="search" placeholder="Search"><button type="submit" aria-label="Search"><svg aria-hidden="true" viewBox="0 0 16 16"><circle cx="6.5" cy="6.5" r="4.5"/><path d="m10 10 4.5 4.5"/></svg></button></div></form>__HELP_DESKTOP_TITLE__<button id="navigation-toggle" type="button" aria-controls="help-navigation" aria-expanded="false" aria-label="__HELP_NAVIGATION_NAME__"><svg aria-hidden="true" viewBox="0 0 22 17"><path d="M0 1.5h22M0 8.5h22M0 15.5h22"/></svg>__HELP_NAVIGATION_TEXT__</button></header>
<main><aside id="help-navigation">__HELP_NAVIGATION__</aside><section id="results" aria-live="polite" hidden><p>Enter words to search all help pages.</p></section><iframe id="topic" title="Help topic" src="contents.htm"></iframe></main><button class="navigation-shade" type="button" aria-label="Close Navigation Panel" hidden></button><script src="contents-data.js"></script><script src="search-index.js"></script><script src="keyword-index.js"></script><script src="help-viewer.js"></script></body></html>
""";

    private const string NavigationHtml = """
<nav class="tabs" aria-label="Help navigation"><button id="contents-tab" class="active" aria-pressed="true">Contents</button><button id="index-tab" aria-pressed="false">Index</button><button id="results-tab" aria-pressed="false">Search</button></nav><nav id="contents" aria-label="Contents"></nav><section id="keyword-index" aria-label="Keyword index" hidden><label for="index-filter">Type a keyword</label><input id="index-filter" aria-controls="index-entries" type="search" placeholder="Keyword or topic title" autocomplete="off"><p id="index-status" role="status" aria-live="polite"></p><ul id="index-entries" aria-label="Index entries"></ul></section>
""";

    internal static string HtmlForAppearance(string key, string? logoPath, string? iconPath, bool desktop = false) => (key == "flare" ? FlareHtml : Html)
        .Replace("__HELP_NAVIGATION__", NavigationHtml, StringComparison.Ordinal)
        .Replace("__HELP_SKIN__", key, StringComparison.Ordinal)
        .Replace("__HELP_ICON__", IconLink(iconPath), StringComparison.Ordinal)
        .Replace("__HELP_DESKTOP_TITLE__", desktop ? "<strong class=\"desktop-help-title\">StatsDirect Help</strong>" : "", StringComparison.Ordinal)
        .Replace("__HELP_NAVIGATION_NAME__", desktop ? "Browse topics" : "Show Navigation Panel", StringComparison.Ordinal)
        .Replace("__HELP_NAVIGATION_TEXT__", desktop ? "Browse topics" : "", StringComparison.Ordinal)
        .Replace("__HELP_IDENTITY__", logoPath == null ? "<strong>StatsDirect Help</strong>" :
            "<img class=\"help-logo\" src=\"" + System.Net.WebUtility.HtmlEncode(logoPath) + "\" width=\"228\" height=\"33\" alt=\"StatsDirect StatisticalHelp\">", StringComparison.Ordinal);

    internal static string IconLink(string? path) => path == null ? "" :
        "<link rel=\"icon\" type=\"image/x-icon\" href=\"" + System.Net.WebUtility.HtmlEncode(path) + "\">";

    // rootRelative is a directory prefix ("" at the root, "../" for a nested
    // topic). Topic CSS is already scoped to the inner body by the builder.
    internal static string FlareTopicHtml(string title, string currentTopic, string headHtml, string bodyHtml,
        string rootRelative, string? logoRelative, string? iconRelative, bool desktop)
    {
        string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var prefix = string.IsNullOrEmpty(rootRelative) ? "" : rootRelative.TrimEnd('/') + "/";
        var html = HtmlForAppearance("flare", logoRelative, iconRelative, desktop)
            .Replace("data-help-skin=\"flare\"", "data-help-skin=\"flare\" data-help-root=\"" + Encode(prefix.Length == 0 ? "./" : prefix) + "\" data-current-topic=\"" + Encode(currentTopic) + "\"", StringComparison.Ordinal)
            .Replace("<title>StatisticalHelp from StatsDirect</title>", "<title>" + Encode(title) + "</title>", StringComparison.Ordinal)
            .Replace("href=\"contents.htm\"", "href=\"" + Encode(prefix + "contents.htm") + "\"", StringComparison.Ordinal)
            .Replace("href=\"help-viewer.css\"", "href=\"" + Encode(prefix + "help-viewer.css") + "\"", StringComparison.Ordinal)
            .Replace("</head>", headHtml + "</head>", StringComparison.Ordinal);
        foreach (var script in new[] { "contents-data.js", "search-index.js", "keyword-index.js", "help-viewer.js" })
            html = html.Replace("src=\"" + script + "\"", "src=\"" + Encode(prefix + script) + "\"", StringComparison.Ordinal);
        html = html.Replace("<script src=\"" + Encode(prefix + "contents-data.js") + "\"></script>", ContentOnlyScript + "<script src=\"" + Encode(prefix + "contents-data.js") + "\"></script>", StringComparison.Ordinal);
        return html.Replace("<iframe id=\"topic\" title=\"Help topic\" src=\"contents.htm\"></iframe>",
            "<article id=\"topic\" class=\"help-article\" aria-label=\"Help topic\"><div id=\"help-topic-body\" class=\"help-math-ignore\">" + bodyHtml + "</div></article>", StringComparison.Ordinal);
    }

    private const string ContentOnlyScript = """
<script>if(new URLSearchParams(location.search).get('content')==='1'){const topic=document.getElementById('help-topic-body');document.documentElement.classList.add('help-content-only');document.body.replaceChildren(...topic.childNodes);document.body.id='help-topic-body';document.body.className=topic.className;}</script>
""";

    internal const string Css = """
*{box-sizing:border-box}
:root{--help-header:#164969;--help-header-text:#fff;--help-nav:#f6f8fa;--help-border:#a8bdcc;--help-link:#123f5d;--help-selected:#dcebf5;--help-hover:#e3eef6;--help-background:#fff;--help-text:#1d3343;--help-height:60px}
:root[data-help-skin="clean-blue"]{--help-header:#163f61;--help-nav:#edf5fc;--help-border:#9bbbd3;--help-link:#124f7c;--help-selected:#d1e6f7;--help-hover:#dcecf9;--help-background:#f4f8fc;--help-text:#17334a}
:root[data-help-skin="warm-paper"]{--help-header:#4f4536;--help-nav:#f6efdf;--help-border:#bfb09a;--help-link:#704310;--help-selected:#eadbc0;--help-hover:#f0e3cc;--help-background:#fffaf0;--help-text:#382f25}
body{margin:0;font:16px Arial,sans-serif;color:var(--help-text);background:var(--help-background)}
header{height:var(--help-height);display:flex;align-items:center;gap:18px;padding:0 20px;background:var(--help-header);color:var(--help-header-text)}
.help-identity{display:flex;align-items:center;gap:24px;flex:1;min-width:0}
.help-logo{display:block;width:228px;max-width:100%;height:auto;max-height:33px;min-width:0;flex:0 1 auto;object-fit:contain;object-position:left center}
header strong{font-size:22px}header span{font-size:14px;color:#edf5fa}
main{display:flex;height:calc(100vh - var(--help-height))}
aside{width:360px;min-width:270px;max-width:48%;border-right:1px solid var(--help-border);overflow:auto;background:var(--help-nav);padding:16px;resize:horizontal}
#search-form label{display:block;font-weight:bold;margin-bottom:8px}
.search-row{display:flex;gap:6px}
input{min-width:0;width:100%;min-height:42px;padding:9px;border:1px solid #7f99ad;border-radius:4px;font:inherit;color:#1d3343;background:white}
input::placeholder{color:#536573;opacity:1}
button{font:inherit;cursor:pointer;min-height:42px;border:1px solid #8fa8ba;background:white;padding:9px 12px;border-radius:4px;color:var(--help-link)}
button:hover{background:var(--help-hover)}button:focus-visible,a:focus-visible,input:focus-visible{outline:3px solid #d99000;outline-offset:2px}
.tabs{display:flex;gap:4px;margin:0 0 16px}.tabs button{flex:1;min-width:0;padding:8px}.tabs .active{background:var(--help-header);color:white}
#keyword-index[hidden],#contents[hidden]{display:none!important}
#keyword-index label{display:block;font-weight:bold;margin-bottom:8px}
#index-status{font-size:14px;line-height:1.4;overflow-wrap:anywhere}
#index-entries{list-style:none;padding:0;margin:0}
#index-entries a{display:block;padding:8px 6px;color:var(--help-link);text-decoration:none;line-height:1.4;overflow-wrap:anywhere;border-radius:3px}
#index-entries a:hover,#index-entries a.index-selected{background:var(--help-selected)}
#contents ul{list-style:none;padding-left:16px;margin:3px 0}#contents>ul{padding-left:0}
#contents a{display:block;text-decoration:none;color:var(--help-link);padding:7px 6px;border-radius:3px;line-height:1.4;overflow-wrap:break-word}
#contents a:hover,#contents a.selected{background:var(--help-selected)}
#contents summary{cursor:pointer;padding:6px 0;line-height:1.4}#contents summary a{display:inline;padding:6px 2px}
#topic{flex:1;min-width:0;border:0;background:var(--help-background)}
#results p{line-height:1.5}.result{display:block;padding:13px 3px;border-bottom:1px solid var(--help-border);color:var(--help-link);text-decoration:none;overflow-wrap:break-word}
.result:hover{background:var(--help-hover)}.result strong{display:block;margin-bottom:6px;line-height:1.4}
.result span{font-size:14px;line-height:1.5;color:#334b5c}.search-count{font-size:14px;color:#3e5261}
@media(max-width:980px){header span{display:none}}
@media(max-width:700px){:root{--help-height:52px}header{padding:9px 14px}.help-identity{flex-basis:100%}header strong{font-size:20px}aside{width:44%;min-width:190px;padding:12px}.search-row{display:block}.search-row button{width:100%;margin-top:7px}.tabs{display:grid;grid-template-columns:1fr;gap:7px}.tabs button{width:100%;text-align:left}}
@media(max-width:480px){aside{width:46%;min-width:170px;padding:10px}#contents ul{padding-left:12px}}
.visually-hidden{position:absolute!important;width:1px!important;height:1px!important;padding:0!important;margin:-1px!important;overflow:hidden!important;clip-path:inset(50%);white-space:nowrap;border:0!important}
/* Independently implemented shell, measured against the published Flare
   website. The copied topic stylesheet remains responsible for topic type. */
:root[data-help-skin="flare"]{--help-height:90px;--help-header:#070764;--help-nav:#45494c;--help-background:#fff;--help-text:#000}
html[data-help-skin="flare"] body{font:13.333333px sans-serif;overflow:hidden}
html[data-help-skin="flare"] header{height:90px;padding:25px 13.333333px;gap:0}
html[data-help-skin="flare"] .help-identity{height:40px;flex:1;align-items:center;text-decoration:none;color:#fff}
html[data-help-skin="flare"] #search-form{flex:0 0 400px;width:400px;position:relative;margin:0}
html[data-help-skin="flare"] .search-row{display:block;position:relative;height:26.666667px}
html[data-help-skin="flare"] #search{display:block;height:26.666667px;min-height:0;width:100%;border:0;border-radius:0;padding:0 50px 0 10px;background:#fff;color:#5f5f5f;font:13.333333px/13.333333px sans-serif;appearance:none}
html[data-help-skin="flare"] #search::placeholder{color:#5f5f5f}
html[data-help-skin="flare"] #search-form button{display:flex;align-items:center;justify-content:center;position:absolute;right:0;top:0;width:36px;height:26.666667px;min-height:0;padding:0;margin:0;border:0;border-radius:0;background:transparent url("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABEAAAASCAYAAAC9+TVUAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAZdEVYdFNvZnR3YXJlAEFkb2JlIEltYWdlUmVhZHlxyWU8AAABD0lEQVQ4T6XRMU4CYRAFYBJqLE2sLOnsvADJK158tQ09sfEAJB6A1gOQ2FlYQi9XkJPYmdAsecmQzD8LaqCYBHZmvp2dGXRdN7g0eg/OieYPqGtQz6DeQH2AegX1AGpYG48ioCagvkH9gFqBWoLagupAbUCNanODxAQGvkDd5gJQU1A7UO+1uSL+BE/QAAl6iYnGNZcR72BVkwm5CeSp5jLiJS5rMiHDQOY1lxFfYVuTCbkP5LHmMuIzumhaC2KKz1j8Vc1nxIU+o6/gJXoHfuYJDPgF69rcIAGNfMZoyOEJ1vF7UYEGSdjYV/ASvYPDJxg4BfWQ3+IU1Cv8K45BvaL/RIJmZyMBzUDdXYTk2ANbcQT+Sb2GGgAAAABJRU5ErkJggg==") no-repeat center center;color:#45494c}
html[data-help-skin="flare"] #search-form button svg{display:none}
html[data-help-skin="flare"] #navigation-toggle{display:none}
html[data-help-skin="flare"] aside{flex:0 0 20%;width:20%;min-width:0;max-width:none;border:0;padding:20px 20px 0;resize:none;overflow:auto;background:#45494c}
html[data-help-skin="flare"] .tabs{display:flex;gap:0;margin:0 0 14px;border-bottom:1px solid #a1a8ac}
html[data-help-skin="flare"] .tabs button{min-height:36px;border:0;border-radius:0;padding:7px 3px;color:#fff;background:transparent;font:13.333333px Arial,sans-serif}
html[data-help-skin="flare"] .tabs button:hover{background:#303335}
html[data-help-skin="flare"] .tabs button.active{background:#303335;box-shadow:inset 0 -3px #fff;font-weight:bold}
html[data-help-skin="flare"] #keyword-index{color:#fff}
html[data-help-skin="flare"] #index-filter{min-height:32px;padding:6px;font:13.333333px Arial,sans-serif}
html[data-help-skin="flare"] #index-status{font-size:12px}
html[data-help-skin="flare"] #index-entries a{color:#fff;border-radius:0;border-bottom:1px solid #303335}
html[data-help-skin="flare"] #index-entries a:hover,html[data-help-skin="flare"] #index-entries a.index-selected{background:#303335}
html[data-help-skin="flare"] #contents ul{padding:0;margin:0;list-style:none}
html[data-help-skin="flare"] #contents li{margin:0;padding:0}
html[data-help-skin="flare"] #contents a,html[data-help-skin="flare"] #contents summary{display:block;margin:0;border:0;border-bottom:1px solid #303335;border-radius:0;padding:10.72px;font:13.333333px/21.333333px Arial,sans-serif;color:#fff;text-decoration:none;overflow-wrap:break-word;cursor:pointer}
html[data-help-skin="flare"] #contents summary{position:relative;padding:0;list-style:none}
html[data-help-skin="flare"] #contents summary::-webkit-details-marker{display:none}
html[data-help-skin="flare"] #contents summary a{margin-right:26.666667px;padding-right:0;border-bottom:0}
html[data-help-skin="flare"] #contents summary:not(:has(a)){padding:10.72px 26.666667px 10.72px 10.72px}
html[data-help-skin="flare"] #contents summary::after{content:"";position:absolute;right:10.666667px;top:50%;margin-top:-4px;border:6px solid transparent;border-bottom-width:0;border-top-color:#a1a8ac;pointer-events:none}
html[data-help-skin="flare"] #contents details[open]>summary::after{transform:rotate(180deg)}
html[data-help-skin="flare"] #contents a:hover,html[data-help-skin="flare"] #contents summary:hover{background:#303335}
html[data-help-skin="flare"] #contents a.selected{font-weight:bold;background:transparent}
html[data-help-skin="flare"] #contents a.selected:hover{background:#303335}
html[data-help-skin="flare"] #contents ul ul a,html[data-help-skin="flare"] #contents ul ul summary{margin-left:26.666667px;padding-top:4px;padding-bottom:4px;border:0}
html[data-help-skin="flare"] #contents ul ul summary{padding-top:0;padding-bottom:0}
html[data-help-skin="flare"] #contents ul ul summary a{margin-left:0}
html[data-help-skin="flare"] #contents ul ul summary:not(:has(a)){padding-top:4px;padding-bottom:4px}
html[data-help-skin="flare"] #contents ul ul ul a,html[data-help-skin="flare"] #contents ul ul ul summary{margin-left:53.333333px}
html[data-help-skin="flare"] #contents ul ul ul ul a,html[data-help-skin="flare"] #contents ul ul ul ul summary{margin-left:80px}
html[data-help-skin="flare"] #contents ul ul ul summary a,html[data-help-skin="flare"] #contents ul ul ul ul summary a{margin-left:0}
html[data-help-skin="flare"] #results{flex:1;min-width:0;overflow:auto;padding:13.333333px;font:13.333333px/1.6 Arial,sans-serif;color:#000;background:white}
html[data-help-skin="flare"] #results h1{font:bold 20px sans-serif;padding-bottom:10px;margin:15px 0 20px;background:#f5f5f5;border-bottom:1px solid currentColor}
html[data-help-skin="flare"] #results .search-count{color:#393f45}
html[data-help-skin="flare"] #results .result{color:#0185d1;padding:0;margin-bottom:16px;border:0;line-height:13.333333px}
html[data-help-skin="flare"] #results .result strong{font:normal 13.333333px/14.666667px sans-serif;margin:0 0 2px;text-decoration:none}
html[data-help-skin="flare"] #results .result:hover strong{text-decoration:underline}
html[data-help-skin="flare"] #results .result span{display:block;margin-top:0;color:#000;font:10.666667px/13.333333px Arial,sans-serif}
html[data-help-skin="flare"] #results .result:hover{background:transparent}
html[data-help-skin="flare"] #results .result-url{display:block;color:#0e774a;font:10.666667px/13.333333px Arial,sans-serif}
html[data-help-skin="flare"] #topic[hidden]{display:none}
html[data-help-skin="flare"] article#topic{display:block;overflow:auto}
html.help-content-only body#help-topic-body{overflow:visible!important}
html[data-help-skin="flare"] .navigation-shade{display:none}
@media(max-width:1279px){
 :root[data-help-skin="flare"]{--help-height:111.666667px}
 html[data-help-skin="flare"] header{height:111.666667px;display:flex;flex-wrap:wrap;align-content:flex-start;padding:15px 13.333333px;position:relative}
 html[data-help-skin="flare"] .help-identity{flex:0 0 100%;height:40px}
 html[data-help-skin="flare"] #search-form{flex:0 0 100%;width:100%;margin-top:15px}
 html[data-help-skin="flare"] #search-form button{width:36px}
 html[data-help-skin="flare"] #navigation-toggle{position:absolute;top:11px;right:0;display:flex;align-items:center;justify-content:center;width:48px;height:48px;min-height:0;padding:0;border:0;border-radius:0;color:#fff;background:transparent url("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABYAAAARCAYAAADZsVyDAAAABGdBTUEAALGPC/xhBQAAACBjSFJNAAB6JgAAgIQAAPoAAACA6AAAdTAAAOpgAAA6mAAAF3CculE8AAAACXBIWXMAAAsQAAALEAGtI711AAAAGHRFWHRTb2Z0d2FyZQBwYWludC5uZXQgNC4wLjVlhTJlAAAAQ0lEQVQ4T9WRoQ0AMAzDesi+2BM7adcn5cE1qCWTAJOUISqHKbiwpEdY+7B9CbnzcpiCC0s6hPnlAmx/Qu68HKbAwg262zLjr+iw2gAAAABJRU5ErkJggg==") no-repeat center center}
 html[data-help-skin="flare"] #navigation-toggle svg{display:none}
 html[data-help-skin="flare"] aside{display:none;position:fixed;right:0;top:0;bottom:0;width:250px;z-index:5;background:#333;padding:0;max-width:85vw}
 html[data-help-skin="flare"] #keyword-index{padding:10px}
 html[data-help-skin="flare"] body.navigation-open header,html[data-help-skin="flare"] body.navigation-open main{transform:translateX(-250px)}
 html[data-help-skin="flare"] body.navigation-open aside{display:block;transform:translateX(250px);top:calc(0px - var(--help-height));bottom:auto;height:100vh}
 html[data-help-skin="flare"] body.navigation-open .navigation-shade{display:block;position:fixed;inset:0 250px 0 0;z-index:4;border:0;border-radius:0;margin:0;padding:0;background:rgba(255,255,255,.25)}
 html[data-help-skin="flare"] #contents a,html[data-help-skin="flare"] #contents summary{border-bottom-color:#262626;font:13.333333px sans-serif;padding:10.6672px}
 html[data-help-skin="flare"] #contents summary{padding:0}
 html[data-help-skin="flare"] #contents a.selected{font-weight:bold}
 html[data-help-skin="flare"] #contents summary a{padding-right:0}
 html[data-help-skin="flare"] #contents summary:not(:has(a)){padding:10.6672px 26.666667px 10.6672px 10.6672px}
 html[data-help-skin="flare"] #contents a:hover,html[data-help-skin="flare"] #contents a.selected:hover,html[data-help-skin="flare"] #contents summary:hover{background:#242424}
 html[data-help-skin="flare"] #contents ul ul a,html[data-help-skin="flare"] #contents ul ul summary{margin-left:13.333333px}
 html[data-help-skin="flare"] #contents ul ul ul a,html[data-help-skin="flare"] #contents ul ul ul summary{margin-left:26.666667px}
 html[data-help-skin="flare"] #contents ul ul ul ul a,html[data-help-skin="flare"] #contents ul ul ul ul summary{margin-left:40px}
 html[data-help-skin="flare"] #contents ul ul summary a,html[data-help-skin="flare"] #contents ul ul ul summary a,html[data-help-skin="flare"] #contents ul ul ul ul summary a{margin-left:0}
 html[data-help-skin="flare"] #results{padding:13.333333px}
}
@media print{body{color:#000;background:#fff}header,aside,html[data-help-skin="flare"] header,html[data-help-skin="flare"] aside,html[data-help-skin="flare"] .navigation-shade{display:none!important}main{display:block;height:auto}#topic{display:block;width:100%;height:100vh;background:#fff}html[data-help-skin="flare"] body{overflow:visible}html[data-help-skin="flare"] article#topic{height:auto;overflow:visible}html[data-help-skin="flare"] body.navigation-open main{transform:none}}
""";

    // Desktop help is hosted beside native search and navigation. Keep the
    // reader DOM for offline results and retain the narrow Contents toggle.
    internal const string DesktopShellCss = """

:root[data-help-skin]{--help-height:0px}
html[data-help-skin] > body > header,
html[data-help-skin] > body > header > .help-identity,
html[data-help-skin] #search-form{display:none}
html[data-help-skin] .tabs,html[data-help-skin] #keyword-index{display:none!important}
@media screen and (max-width:1279px){
 :root[data-help-skin="flare"]{--help-height:32px}
 html[data-help-skin="flare"] > body > header{display:flex;flex-wrap:nowrap;align-items:center;justify-content:space-between;height:var(--help-height);padding:0 10px;background:var(--help-background);color:var(--help-text);border-bottom:1px solid var(--help-border)}
 html[data-help-skin="flare"] .desktop-help-title{font-size:13px;white-space:nowrap}
 html[data-help-skin="flare"] #navigation-toggle{position:static;width:auto;height:100%;padding:0;gap:6px;white-space:nowrap;background:none;color:var(--help-text)}
 html[data-help-skin="flare"] #navigation-toggle svg{display:block;width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:1.5}
}
""";

    internal static string JavaScriptForTarget(HelpBuildTarget target) => JavaScript.Replace(
        "__HELP_TOPIC_BRANCH__", target == HelpBuildTarget.Desktop ? DesktopTopicBranch : WebsiteTopicBranch, StringComparison.Ordinal);

    private const string WebsiteTopicBranch = "if (node.link) s.append(makeLink(node.title,node.link)); else s.textContent=node.title; d.append(s,renderTree(node.children));";

    // The whole summary toggles the branch. Keep the parent page as a
    // separate link so browsing children never takes the reader away early.
    private const string DesktopTopicBranch = """
        s.textContent = node.title;
        const children = renderTree(node.children);
        if (node.link) {
          const overview = document.createElement('li');
          const link = makeLink('Overview', node.link);
          link.setAttribute('aria-label', node.title + ' overview');
          overview.append(link);
          children.prepend(overview);
        }
        d.append(s, children);
        """;

    private const string JavaScript = """
(() => {
  'use strict';
  const contents = document.getElementById('contents'), results = document.getElementById('results'), frame = document.getElementById('topic');
  if(!contents||!results||!frame)return; // Explicit ?content=1 diagnostic view.
  const articleMode=frame.tagName==='ARTICLE';
  const index = window.StatsDirectSearch, tree = window.StatsDirectContents;
  const flare = document.documentElement.dataset.helpSkin === 'flare';
  const viewerRoot = new URL(document.documentElement.dataset.helpRoot||'.',location.href);
  function decodedPath(value) { try{return decodeURIComponent(value);}catch{return value;} }
  function localTopicPath(value) {
    if(typeof value!=='string'||!value||value.trim()!==value)return false;
    const path=decodedPath(value);
    return !/[:\\?#\x00-\x1f\x7f]/.test(path)&&!path.split('/').some(part=>!part||part==='.'||part==='..')&&/\.html?$/i.test(path);
  }
  const documents=Array.isArray(window.StatsDirectTopics)?window.StatsDirectTopics.filter(doc=>doc&&localTopicPath(doc.url)&&typeof doc.title==='string'):index.documents;
  const catalogue = new Map(documents.map(doc=>[decodedPath(doc.url).toLowerCase(),doc]));
  function resolveTopic(value) {
    if(typeof value!=='string'||!value)return null;
    try {
      const target=new URL(value,viewerRoot);
      if(target.origin!==viewerRoot.origin||target.host!==viewerRoot.host||!target.pathname.startsWith(viewerRoot.pathname))return null;
      const doc=catalogue.get(decodedPath(target.pathname.slice(viewerRoot.pathname.length)).toLowerCase());
      if(!doc)return null;
      const link=doc.url+target.search+target.hash;
      return {path:doc.url,title:doc.title,fragment:decodedPath(target.hash),link,url:new URL(link,viewerRoot).href};
    }catch{return null;}
  }
  function scrollSelection() { const selected=document.getElementById('keyword-index')?.hidden===false?document.querySelector('#index-entries a.index-selected'):contents.querySelector('a.selected');if(selected?.offsetHeight)(selected.closest('li')||selected).scrollIntoView({block:'nearest',inline:'nearest'}); }
  const menu = document.getElementById('navigation-toggle'), shade = document.querySelector('.navigation-shade');
  function navigation(open) { document.body.classList.toggle('navigation-open',open); if(menu)menu.setAttribute('aria-expanded',String(open)); if(shade)shade.hidden=!open;if(open)scrollSelection(); }
  if(menu)menu.addEventListener('click',()=>navigation(!document.body.classList.contains('navigation-open')));
  if(shade)shade.addEventListener('click',()=>navigation(false));
  document.addEventListener('keydown',e=>{
    if(e.key!=='Escape')return;
    const fromNavigation=contents.contains(document.activeElement)||document.getElementById('help-navigation')?.contains(document.activeElement);
    if(!document.body.classList.contains('navigation-open')&&!fromNavigation)return;
    navigation(false);
    // Native hosts hide the reader header; the wide reader also hides its toggle.
    // Return focus to the topic instead of a button that cannot receive it.
    if(menu?.getClientRects().length)menu.focus({preventScroll:true});
    if(!menu?.getClientRects().length||document.activeElement!==menu){
      if(articleMode&&!frame.hasAttribute('tabindex'))frame.tabIndex=-1;
      frame.focus({preventScroll:true});
    }
    e.preventDefault();
  });
  matchMedia('(max-width:1279px)').addEventListener('change',()=>navigation(false));
  const tabs = [document.getElementById('contents-tab'), document.getElementById('index-tab'), document.getElementById('results-tab')];
  const keywordPanel=document.getElementById('keyword-index'), keywordFilter=document.getElementById('index-filter'), keywordList=document.getElementById('index-entries'), keywordStatus=document.getElementById('index-status');
  let keywordRows=[], selectedKeyword=0, keywordEntries=null;
  let activeTopic=(articleMode?resolveTopic(location.href):null)||resolveTopic(document.documentElement.dataset.currentTopic||frame.getAttribute('src')||index.documents[0]?.url);
  function showPanel(mode) {
    contents.hidden=mode==='index'||(!flare&&mode==='search');keywordPanel.hidden=mode!=='index';results.hidden=mode!=='search';
    if(flare)frame.hidden=mode==='search';
    tabs.forEach((tab,position)=>{const active=position===['contents','index','search'].indexOf(mode);tab.classList.toggle('active',active);tab.setAttribute('aria-pressed',String(active));});
    document.title=mode==='search'?'Search':activeTopic?.title||'StatsDirect Help';
  }
  function showResults(on) { showPanel(on?'search':'contents'); }
  const keywordCollator=new Intl.Collator('en',{sensitivity:'base',numeric:true});
  const foldKeyword=value=>value.normalize('NFD').replace(/\p{M}/gu,'').toLocaleLowerCase('en');
  function loadKeywords() {
    const data=window.STATSDIRECT_HELP_KEYWORDS;
    if(!data){keywordStatus.textContent='The keyword index is not included in this help build.';return false;}
    try {
      if(data.schemaVersion!==1||!Array.isArray(data.entries)||!Array.isArray(window.StatsDirectTopics))throw Error('Unsupported or incomplete keyword data.');
      const seen=new Set(), entries=[];
      for(const entry of data.entries){
        if(!entry||typeof entry.term!=='string'||!entry.term.trim()||typeof entry.title!=='string'||!entry.title.trim()||!localTopicPath(entry.path))throw Error('Invalid keyword entry.');
        const target=catalogue.get(decodedPath(entry.path).toLowerCase());
        if(!target)throw Error('A keyword refers to a topic not included in this help build.');
        const key=entry.term.toLocaleLowerCase('en')+'\0'+target.url.toLocaleLowerCase('en');
        if(!seen.has(key)){seen.add(key);entries.push({term:entry.term,title:entry.title,path:target.url});}
      }
      entries.sort((a,b)=>keywordCollator.compare(a.term,b.term)||keywordCollator.compare(a.title,b.title)||keywordCollator.compare(a.path,b.path));
      const groups=new Map();
      for(const entry of entries){const key=entry.term.toLocaleLowerCase('en');if(!groups.has(key))groups.set(key,[]);groups.get(key).push(entry);}
      keywordEntries=entries.map(entry=>{const group=groups.get(entry.term.toLocaleLowerCase('en')),ambiguous=group.length>1,sameTitle=group.filter(other=>other.title.toLocaleLowerCase('en')===entry.title.toLocaleLowerCase('en')).length>1;return {...entry,label:entry.term+(ambiguous?' — '+entry.title:'')+(ambiguous&&sameTitle?' ('+entry.path+')':'')};});
      return true;
    }catch(error){keywordEntries=null;keywordStatus.textContent='Could not open the help index: '+error.message;return false;}
  }
  function selectKeyword(position,focus=false) {
    selectedKeyword=Math.max(0,Math.min(position,keywordRows.length-1));
    keywordRows.forEach((row,i)=>{row.classList.toggle('index-selected',i===selectedKeyword);row.tabIndex=i===selectedKeyword?0:-1;});
    if(focus&&keywordRows[selectedKeyword]){keywordRows[selectedKeyword].focus({preventScroll:true});keywordRows[selectedKeyword].scrollIntoView({block:'nearest'});}
  }
  function renderKeywords() {
    const previous=keywordRows[selectedKeyword]?.dataset.link;
    keywordList.replaceChildren();keywordRows=[];
    if(keywordEntries===null&&!loadKeywords())return;
    const query=foldKeyword(keywordFilter.value.trim()),matches=keywordEntries.filter(entry=>foldKeyword(entry.term+' '+entry.title).includes(query));
    keywordStatus.textContent=matches.length?`${matches.length} index entr${matches.length===1?'y':'ies'}`:query?'No matching keywords. Try fewer words or a topic title.':'No keywords have been authored for this help build.';
    for(const entry of matches){const li=document.createElement('li'),link=makeLink(entry.label,entry.path);link.addEventListener('focus',()=>selectKeyword(keywordRows.indexOf(link)));li.append(link);keywordList.append(li);keywordRows.push(link);}
    selectKeyword(Math.max(0,keywordRows.findIndex(row=>row.dataset.link===previous)));
  }
  keywordFilter.addEventListener('input',renderKeywords);
  keywordFilter.addEventListener('keydown',event=>{
    if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();selectKeyword(event.key==='ArrowUp'?keywordRows.length-1:selectedKeyword,true);}
    else if(event.key==='Enter'){event.preventDefault();keywordRows[selectedKeyword]?.click();}
  });
  keywordList.addEventListener('keydown',event=>{
    const position=keywordRows.indexOf(event.target.closest('a'));
    if(position<0)return;
    if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();selectKeyword(event.key==='Home'?0:event.key==='End'?keywordRows.length-1:position+(event.key==='ArrowDown'?1:-1),true);}
    else if(event.key==='Enter'){event.preventDefault();keywordRows[position].click();}
  });
  function selectTopic(link) { const target=resolveTopic(link);if(!target)return;const links=[...document.querySelectorAll('#contents a')].map(a=>({a,topic:resolveTopic(a.dataset.link)}));const exact=target.fragment?links.find(item=>item.topic?.path===target.path&&item.topic.fragment===target.fragment):null,current=(exact||links.find(item=>item.topic?.path===target.path))?.a;links.forEach(({a})=>{const selected=a===current;a.classList.toggle('selected',selected);if(selected){a.setAttribute('aria-current','page');for(let p=a.parentElement;p&&p!==contents;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;}else a.removeAttribute('aria-current');});scrollSelection(); }
  function openTopic(link, anchor, updateHash=true) { const target=resolveTopic(link);if(!target)return;if(articleMode){navigation(false);location[updateHash?'assign':'replace'](target.url);return;}activeTopic=target;const hash=new URLSearchParams({topic:target.link}).toString();if(updateHash&&location.hash.slice(1)!==hash)history.pushState(null,'','#'+hash);try{frame.contentWindow.location.replace(target.url);}catch{frame.src=target.url;} selectTopic(target.link); if(flare)showResults(false);else document.title=target.title;navigation(false); }
  function makeLink(title, link) { const a = document.createElement('a'); a.href = resolveTopic(link)?.url||link; a.dataset.link = link; a.textContent = title; a.addEventListener('click', e => { if(e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;if(articleMode){navigation(false);return;}e.preventDefault();e.stopPropagation();openTopic(link,a); }); return a; }
  function renderTree(nodes) { const ul = document.createElement('ul'); for (const node of nodes) { const li = document.createElement('li'); if (node.children.length) { const d = document.createElement('details'), s = document.createElement('summary'); __HELP_TOPIC_BRANCH__ li.append(d); } else if (node.link) li.append(makeLink(node.title,node.link)); else { const span=document.createElement('span');span.textContent=node.title;li.append(span); } ul.append(li); } return ul; }
  contents.append(renderTree(tree));
  if(activeTopic)selectTopic(activeTopic.link);
  const home=document.querySelector('a.help-identity'),homeTopic=resolveTopic('contents.htm')||resolveTopic(index.documents[0]?.url);if(home&&homeTopic){home.href=homeTopic.url;home.addEventListener('click',e=>{if(articleMode||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;e.preventDefault();showResults(false);openTopic(homeTopic.link);});}
  tabs[0].addEventListener('click',()=>{showResults(false);if(new URLSearchParams(location.hash.slice(1)).has('searchQuery'))history.replaceState(null,'',location.pathname+location.search);scrollSelection();});
  tabs[1].addEventListener('click',()=>{showPanel('index');renderKeywords();keywordFilter.focus({preventScroll:true});keywordFilter.select();});
  tabs[2].addEventListener('click',()=>{showResults(true);navigation(false);const search=document.getElementById('search');if(search?.getClientRects().length)search.focus({preventScroll:true});});
  function tokenize(value) { return value.normalize('NFD').replace(/\p{M}/gu,'').toLowerCase().match(/[\p{L}\p{N}]+/gu)?.filter(x=>x.length>1||/^\d$/.test(x)) || []; }
  function search(query) {
    const tokens=[...new Set(tokenize(query))], scores=new Map(), hits=new Map();
    for(const token of tokens) { const perWord=new Map(); const exact=index.terms[token]; if(exact)for(const [id,weight]of exact)perWord.set(id,weight*3);
      if(token.length>=3) for(const [term,postings]of Object.entries(index.terms)) if(term!==token&&term.startsWith(token)) for(const [id,weight]of postings)perWord.set(id,Math.max(perWord.get(id)||0,weight));
      for(const[id,weight]of perWord){scores.set(id,(scores.get(id)||0)+weight);hits.set(id,(hits.get(id)||0)+1);} }
    const found=[...scores].filter(([id])=>hits.get(id)===tokens.length).sort((a,b)=>b[1]-a[1]||index.documents[a[0]].title.localeCompare(index.documents[b[0]].title));
    results.replaceChildren();const count=document.createElement(flare?'h1':'p');count.className='search-count';count.textContent=flare?`Your search for "${query}" returned ${found.length} result(s).`:tokens.length?`${found.length} page${found.length===1?'':'s'} found`:'Enter words to search all help pages.';results.append(count);
    for(const[id]of found.slice(0,100)){const doc=index.documents[id],a=makeLink('',doc.url);a.className='result';const title=document.createElement('strong'),excerpt=document.createElement('span');title.textContent=doc.title;excerpt.textContent=doc.excerpt;a.append(title,excerpt);if(flare){const url=document.createElement('cite');url.className='result-url';url.textContent=doc.url;a.append(url);}results.append(a);}
    if(tokens.length&&!found.length){const p=document.createElement('p');p.textContent='No matching pages. Try fewer words or the name of the method.';results.append(p);}showResults(true);navigation(false);
    return found.map(([id,score])=>({url:index.documents[id].url,title:index.documents[id].title,score}));
  }
  document.getElementById('search-form').addEventListener('submit',e=>{e.preventDefault();const query=document.getElementById('search').value,hash=new URLSearchParams({searchQuery:query}).toString();if(location.hash.slice(1)!==hash)history.pushState(null,'','#'+hash);search(query);});
  window.StatsDirectHelp={search,openTopic};
  function readSearchHash(){const state=new URLSearchParams(location.hash.slice(1)),query=state.get('searchQuery'),topic=state.get('topic');if(query!==null){document.getElementById('search').value=query;search(query);}else{if(articleMode&&!topic){const current=resolveTopic(location.href);if(current){activeTopic=current;selectTopic(current.link);}}showResults(false);if(topic)openTopic(topic,null,false);}}
  window.addEventListener('hashchange',readSearchHash);readSearchHash();
  if(!articleMode)frame.addEventListener('load',()=>{try{const current=resolveTopic(frame.contentWindow.location.href);if(current){activeTopic=current;selectTopic(current.link);if(results.hidden&&!new URLSearchParams(location.hash.slice(1)).has('searchQuery')){document.title=current.title;history.replaceState(null,'','#'+new URLSearchParams({topic:current.link}).toString());}frame.contentDocument.addEventListener('click',e=>{const a=e.target.closest?.('a[href]');if(!a||e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey||a.target==='_blank'||a.closest('svg,math'))return;const target=resolveTopic(a.href);if(!target)return;e.preventDefault();openTopic(target.link);});}}catch{/* Local file browsers may isolate iframe documents; navigation still works. */}});
})();
""";

    // Default styling intentionally adds no type, spacing or artwork overrides.
    // Author-selected palettes affect the page background and HTML
    // links only. SVG/MathML links retain their own artwork colours.
    internal const string TopicAppearanceCss = """
.help-embedded .help-topic-nav{display:none}
html[data-help-skin] *,html[data-help-skin] *::before,html[data-help-skin] *::after{box-sizing:border-box}
@media screen{html[data-help-skin] body{transform:translateZ(0);padding-top:calc(1em + .1px)}}
html[data-help-skin="flare"] body img{max-width:none}
html[data-help-skin="clean-blue"] body{background:#f4f8fc!important}
html[data-help-skin="clean-blue"] body a:not(svg a):not(math a){color:#124f7c!important}
html[data-help-skin="warm-paper"] body{background:#fffaf0!important}
html[data-help-skin="warm-paper"] body a:not(svg a):not(math a){color:#704310!important}
@media print{html[data-help-skin] body{background:#fff!important}html[data-help-skin] body a:not(svg a):not(math a){color:#000!important}.help-topic-nav{display:none}}
""";

    internal const string TopicNavigationJavaScript = """
(() => {
  'use strict';
  if (window.parent !== window) document.documentElement.classList.add('help-embedded');
})();
""";
}
