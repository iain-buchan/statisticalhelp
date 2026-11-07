using System.Net;
using System.Xml;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using StatsDirect.HelpEditor.Core;

var builder = WebApplication.CreateBuilder(args);
int port = builder.Configuration.GetValue("port", 5848);
if (port is < 1 or > 65535) throw new ArgumentException("Choose a port from 1 to 65535.");
var origin = $"http://127.0.0.1:{port}";
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);
    options.Limits.MaxRequestBodySize = 4_000_000;
});

var projectDirectory = builder.Environment.ContentRootPath;
var helpEditorDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "..", ".."));
var editorAssets = Path.GetFullPath(Path.Combine(projectDirectory, "..", "StatsDirectHelpEditor.App", "Web"));
var helpSource = Path.Combine(helpEditorDirectory, "HelpSource");
var workspacePath = builder.Configuration["workspace"] ?? Path.Combine(helpEditorDirectory, "WorkingCopies", "BrowserDraft");
var editorHtml = File.ReadAllText(Path.Combine(editorAssets, "index.html"));
const string editorModule = "<script type=\"module\" src=\"./editor.js\"></script>";
if (!editorHtml.Contains(editorModule, StringComparison.Ordinal))
    throw new InvalidDataException("The shared editor entry point has changed. Update the browser bridge.");
editorHtml = editorHtml.Replace(editorModule, "<script src=\"/editor-bridge.js\"></script>\n  " + editorModule, StringComparison.Ordinal);

using var workspace = new DraftWorkspace(workspacePath, helpSource, editorAssets);
using var editorFiles = new PhysicalFileProvider(editorAssets);
// This pilot edits topics only. The editor's images and styles come from the supplied seed.
using var sourceFiles = new PhysicalFileProvider(Path.Combine(helpSource, "Content"));
using var previewFiles = new PhysicalFileProvider(workspace.PreviewPath);
var app = builder.Build();

app.Use(async (context, next) =>
{
    // A website opened in another tab must not be able to modify the local draft.
    if (context.Request.Host.Value != $"127.0.0.1:{port}")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.Origin != origin)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    var policy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; frame-src 'self'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
    if (context.Request.Path.StartsWithSegments("/editor"))
        policy = policy.Replace("script-src 'self'", "script-src 'self' 'unsafe-inline'");
    if (context.Request.Path.StartsWithSegments("/preview"))
        policy = "sandbox allow-scripts; " + policy.Replace("script-src 'self'", "script-src 'self' 'unsafe-inline'");
    context.Response.Headers.ContentSecurityPolicy = policy;
    await next(context);
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions { FileProvider = editorFiles, RequestPath = "/editor" });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = sourceFiles,
    RequestPath = "/source",
    ContentTypeProvider = AssetTypes(".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".woff", ".woff2")
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = previewFiles,
    RequestPath = "/preview",
    ContentTypeProvider = AssetTypes(".htm", ".html", ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".woff", ".woff2", ".json")
});

app.MapGet("/editor/", () => Results.Content(editorHtml, "text/html; charset=utf-8"));
app.MapGet("/api/workspace", () => Handle(workspace.Summary));
app.MapGet("/api/topic", (string path) => Handle(() => workspace.Read(path)));
app.MapPost("/api/topic", (DraftRequest draft) => Handle(() => workspace.Save(draft)));
app.MapPost("/api/preview", (PreviewRequest draft) => Handle(() => workspace.Preview(draft)));
app.Run();

static IResult Handle(Func<object> action)
{
    try { return Results.Ok(action()); }
    catch (DraftConflictException error) { return Results.Conflict(new { error = error.Message }); }
    catch (HelpTopicConflictException error) { return Results.Conflict(new { error = error.Message }); }
    catch (FileNotFoundException) { return Results.NotFound(new { error = "That topic could not be found." }); }
    catch (Exception error) when (error is XmlException or InvalidDataException or ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = error.Message });
    }
}

static FileExtensionContentTypeProvider AssetTypes(params string[] extensions)
{
    var defaults = new FileExtensionContentTypeProvider();
    var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (string extension in extensions) types.Add(extension, defaults.Mappings[extension]);
    return new(types);
}
