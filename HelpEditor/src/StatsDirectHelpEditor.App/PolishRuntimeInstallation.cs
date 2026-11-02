// Adapted from StatsDirect's MIT-licensed TutorRuntimeInstallation; copyright Iain Buchan.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace StatsDirect.HelpEditor.App;

internal readonly record struct PolishInstallProgress(string Stage, long Received = 0, long? Total = null);

/// <summary>Installs the reviewed official runtime on demand, separately from account data.
/// Update the archive and extracted-file pins together after reviewing a new release.</summary>
internal sealed class PolishRuntimeInstallation
{
    internal const string Version = "0.157.1";
    internal const string Sha256 = "7ca926f4fc443c0ead83109314e9a97bf43d2da276be147f1919a4b5e62dfa8b";
    internal const long DownloadBytes = 122840599;
    internal static readonly Uri DownloadUri = new("https://github.com/openai/codex/releases/download/rust-v" + Version + "/codex-app-server-package-x86_64-pc-windows-msvc.tar.gz");
    private static readonly Dictionary<string, (long Size, string Hash)> Files = new(StringComparer.Ordinal)
    {
        ["codex-package.json"] = (237, "4a7b6492b16fcf2f59653bbece5c2dccd7fd18bfe83821f04bb3dfed6bbcf436"),
        ["bin/codex-app-server.exe"] = (246319408, "f768e4edc401cfe9c40c70e6175b046a48e017e7b20c28ff833196e30c84827a"),
        ["bin/codex-code-mode-host.exe"] = (74631472, "5583872360541856fe6207eba0fb8486d9967083fbf4d40e2126ec9bb9c06ead"),
        ["codex-path/rg.exe"] = (4218880, "14231169855ec5205cf5a1b6f1db358ff4aed4247c86b69ce8aae647c77f6680"),
        ["codex-resources/codex-command-runner.exe"] = (8200496, "3834fec990e505577888605f11b3c9820d1384784b8709861ac7e61122aa2c1c"),
        ["codex-resources/codex-windows-sandbox-setup.exe"] = (17670960, "c6a5ec12dee7f9563c5d0f2f6e8b82a0528e5cedd4d330dbda091dbb91d58b78"),
        ["LICENSE"] = (10926, "d17f227e4df5da1600391338865ce0f3055211760a36688f816941d58232d8dc"),
        ["NOTICE"] = (242, "9d71575ecfd9a843fc1677b0efb08053c6ba9fd686a0de1a6f5382fd3c220915")
    };
    private readonly string root, notices;
    internal string DirectoryPath => Path.Combine(root, Version + "-" + Sha256[..12]);
    internal string Executable => Path.Combine(DirectoryPath, "bin", "codex-app-server.exe");
    private string Marker => Path.Combine(DirectoryPath, "STATSDIRECT-RUNTIME.sha256");
    internal PolishRuntimeInstallation(string root, string notices)
    { this.root = Path.GetFullPath(root); this.notices = notices; }

    // This cheap check never downloads or starts a process. Full hashes are checked
    // before launching the runtime, and incomplete installations have no marker.
    internal bool IsInstalled
    {
        get
        {
            try
            {
                if (!File.Exists(Marker) || new FileInfo(Marker).Length != Sha256.Length || File.ReadAllText(Marker) != Sha256) return false;
                foreach (var (name, pin) in Files)
                    if (!File.Exists(Path.Combine(DirectoryPath, name)) || new FileInfo(Path.Combine(DirectoryPath, name)).Length != pin.Size) return false;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
    internal async Task<bool> IsValidAsync(CancellationToken token)
    {
        if (!IsInstalled) return false;
        try { await VerifyFilesAsync(DirectoryPath, token).ConfigureAwait(false); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    internal static async Task VerifyHashAsync(string path, string hash, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("The ChatGPT component failed its integrity check. Please retry the download.");
    }
    private static async Task VerifyFilesAsync(string folder, CancellationToken token)
    {
        foreach (var (name, pin) in Files)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path) || new FileInfo(path).Length != pin.Size) throw new IOException("The ChatGPT component is incomplete. Please retry the download.");
            await VerifyHashAsync(path, pin.Hash, token).ConfigureAwait(false);
        }
    }
    internal static bool AllowedDownloadUri(Uri uri) => uri != null && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
        (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");

    internal async Task InstallAsync(HttpClient client, IProgress<PolishInstallProgress> progress, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        progress?.Report(new("Checking ChatGPT component…"));
        // A file lock also serialises independent StatsDirect processes. Cancellation
        // while waiting never interrupts another process's installation.
        await using var installLock = await AcquireLockAsync(token).ConfigureAwait(false);
        if (await IsValidAsync(token).ConfigureAwait(false)) return;
        string stage = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(stage, "runtime.tar.gz"), unpacked = Path.Combine(stage, "unpacked");
        Directory.CreateDirectory(unpacked);
        try
        {
            await DownloadAsync(client, archive, progress, token).ConfigureAwait(false);
            progress?.Report(new("Verifying download…"));
            await VerifyHashAsync(archive, Sha256, token).ConfigureAwait(false);
            progress?.Report(new("Installing ChatGPT component…"));
            await ExtractAsync(archive, unpacked, token).ConfigureAwait(false);
            foreach (string name in new[] { "LICENSE", "NOTICE" }) File.Copy(Path.Combine(notices, name), Path.Combine(unpacked, name));
            await VerifyFilesAsync(unpacked, token).ConfigureAwait(false);
            progress?.Report(new("Checking OpenAI publisher…"));
            VerifySignature(Path.Combine(unpacked, "bin", "codex-app-server.exe"));
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(unpacked, "STATSDIRECT-RUNTIME.sha256"), Sha256, token).ConfigureAwait(false);
            // Only a complete, verified tree becomes visible. Keep the previous tree
            // until the move succeeds; never touch credentials or a different version.
            string previous = Path.Combine(stage, "previous");
            bool replaced = Directory.Exists(DirectoryPath);
            if (replaced) Directory.Move(DirectoryPath, previous);
            try { Directory.Move(unpacked, DirectoryPath); }
            catch { if (replaced) Directory.Move(previous, DirectoryPath); throw; }
            progress?.Report(new("ChatGPT component ready"));
        }
        finally { DeleteStage(stage); }
    }
    private async Task<FileStream> AcquireLockAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }
    internal static async Task DownloadAsync(HttpClient client, string path, IProgress<PolishInstallProgress> progress, CancellationToken token)
    {
        // GitHub release assets redirect to a signed CDN URL. Follow only reviewed
        // HTTPS hosts; the archive pin remains the authority for the downloaded bytes.
        Uri uri = DownloadUri;
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!AllowedDownloadUri(response.RequestMessage?.RequestUri ?? uri)) throw new IOException("Unexpected ChatGPT download location.");
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                Uri next = response.Headers.Location;
                if (next != null && !next.IsAbsoluteUri) next = new Uri(uri, next);
                if (!AllowedDownloadUri(next)) throw new IOException("Unexpected ChatGPT download location.");
                uri = next; continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("The ChatGPT component could not be downloaded. Check your internet connection or organisation's download policy, then retry.");
            long? total = response.Content.Headers.ContentLength;
            if (total.HasValue && total != DownloadBytes) throw new IOException("The ChatGPT download has an unexpected size. Please retry.");
            progress?.Report(new("Downloading ChatGPT component", 0, DownloadBytes));
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            byte[] buffer = new byte[81920]; long received = 0;
            var throttle = Stopwatch.StartNew();
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                received += count;
                if (received > DownloadBytes) throw new IOException("The ChatGPT download has an unexpected size.");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                if (throttle.ElapsedMilliseconds >= 100) { progress?.Report(new("Downloading ChatGPT component", received, DownloadBytes)); throttle.Restart(); }
            }
            if (received != DownloadBytes) throw new IOException("The ChatGPT download was incomplete. Please retry.");
            progress?.Report(new("Downloading ChatGPT component", received, DownloadBytes));
            return;
        }
        throw new IOException("Too many redirects downloading the ChatGPT component. Please retry.");
    }
    internal static async Task ExtractAsync(string archive, string destination, CancellationToken token)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int entries = 0;
        TarEntry entry;
        while ((entry = await reader.GetNextEntryAsync(false, token).ConfigureAwait(false)) != null)
        {
            if (++entries > 16) throw new IOException("Unexpected files in the ChatGPT package.");
            string name = entry.Name;
            if (entry.EntryType == TarEntryType.Directory && name is "bin/" or "codex-path/" or "codex-resources/") continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !Files.TryGetValue(name, out var pin) || name is "LICENSE" or "NOTICE" || !seen.Add(name) || entry.Length != pin.Size)
                throw new IOException("Unexpected file in the ChatGPT package.");
            string path = Path.Combine(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await entry.DataStream.CopyToAsync(output, token).ConfigureAwait(false);
        }
        if (seen.Count != Files.Count - 2) throw new IOException("The ChatGPT package is incomplete.");
    }
    private void DeleteStage(string path)
    {
        // Cleanup is restricted to this attempt's direct child of the runtime root.
        if (Path.GetDirectoryName(Path.GetFullPath(path)) != root || !Path.GetFileName(path).StartsWith(".install-", StringComparison.Ordinal)) throw new IOException("Invalid installation cleanup path.");
        try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static void VerifySignature(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, memory, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, UnionChoice = 1, File = memory, Revocation = 1, Flags = 0x80 };
            Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (result != 0 || certificate.GetNameInfo(X509NameType.SimpleName, false) != "OpenAI OpCo, LLC")
                throw new IOException("The ChatGPT component's OpenAI publisher signature could not be verified. Please retry.");
        }
        finally { Marshal.DestroyStructure<TrustFile>(memory); Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string Path; internal IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustData { internal uint Size; internal IntPtr Policy, Client; internal uint Ui, Revocation, UnionChoice; internal IntPtr File; internal uint State; internal IntPtr StateData, Url; internal uint Flags, Context; internal IntPtr SignatureSettings; }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
