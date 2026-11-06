using Microsoft.Web.WebView2.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>Shared settings for the editor's offline WebViews; navigation rules remain with each host.</summary>
internal static class HelpWebViewSettings
{
    internal static void Apply(CoreWebView2 core, bool authoring = false)
    {
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = authoring;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
    }

    internal static void MapLocalFolder(CoreWebView2 core, string host, string folder) =>
        core.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
}
