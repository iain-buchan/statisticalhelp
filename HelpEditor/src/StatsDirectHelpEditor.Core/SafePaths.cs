using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StatsDirect.HelpEditor.Core;

internal static class SafePaths
{
    internal static bool Same(string a, string b) => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    internal static bool IsWithin(string path, string parent)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Resolve(string root, string relative, bool allowDot = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.IndexOfAny(['\0', '#', '?']) >= 0)
            throw new ArgumentException("Use a path within the working copy.");
        var clean = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (!allowDot && clean.Split(Path.DirectorySeparatorChar).Any(p => p is ".." or ".")) throw new ArgumentException("Parent-directory paths are not allowed here.");
        var path = Path.GetFullPath(Path.Combine(root, clean));
        if (!IsWithin(path, root)) throw new ArgumentException("The path is outside the working copy.");
        CheckNoLinks(path);
        return path;
    }

    internal static void CheckNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;
            // Cloud placeholders do not redirect path names. Do not infer this
            // from LinkTarget=null: unknown tags or query failures stay blocked.
            if (!OperatingSystem.IsWindows() || !AllowedCloudTag(ReadReparseTag(current)))
                throw new IOException("Linked or unsupported reparse files/folders are not supported: " + current);
        }
    }

    internal static bool AllowedCloudTag(uint tag) => (tag & 0x20000000) == 0 &&
        ((tag & ~0x0000F000u) == 0x9000001Au || tag is 0x80000015u or 0x80000021u);

    private static uint ReadReparseTag(string path)
    {
        string nativePath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path :
            path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        using var find = FindFirstFileW(nativePath, out var data);
        if (find.IsInvalid || (data.Attributes & (uint)FileAttributes.ReparsePoint) == 0)
            throw new IOException("The reparse-point type could not be checked: " + path, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        return data.ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        internal uint SizeHigh, SizeLow, ReparseTag, Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] internal string AlternateName;
    }
    private sealed class FindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public FindHandle() : base(true) { }
        protected override bool ReleaseHandle() => FindClose(handle);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern FindHandle FindFirstFileW(string path, out FindData data);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);

    /// <summary>Published outputs, receipts and manifests use LF whatever the platform, so their bytes depend on the source alone.</summary>
    internal static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    internal static void AtomicWriteLf(string path, string text) => AtomicWrite(path, NormalizeNewlines(text));

    internal static void AtomicWrite(string path, string text)
    {
        CheckNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CheckNoLinks(path);
        var temp = path + ".helpeditor-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { CheckNoLinks(temp); File.WriteAllText(temp, text, new UTF8Encoding(false)); CheckNoLinks(path); CheckNoLinks(temp); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) { CheckNoLinks(temp); File.Delete(temp); } }
    }
}
