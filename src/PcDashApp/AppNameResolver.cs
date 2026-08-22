using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel;

namespace PcDashApp;

/// <summary>
/// Resolves SMTC's raw SourceAppUserModelId (AUMID) -- e.g. "Chrome.exe", "Spotify.exe", or a
/// packaged app's PackageFamilyName!AppId -- to a human-readable app/player name. Two-tier:
/// first tries the WinRT <c>AppInfo.GetFromAppUserModelId</c> API (works only for packaged/
/// MSIX/UWP apps with a real registered AUMID), then falls back to reading FileDescription/
/// ProductName off the source app's own exe via <see cref="FileVersionInfo"/> (works for any
/// Win32 executable -- covers classic desktop apps like Chrome, whose SMTC AUMID is just their
/// raw exe name/path and was never resolvable via the WinRT API at all). This resolves the
/// *app*, not the page/tab: a YouTube tab playing in Chrome still resolves to "Google Chrome",
/// not "YouTube" -- SMTC has no tab-level identity to resolve from (see MediaMonitor.cs's doc
/// comment). Falls back to the raw AUMID whenever both tiers fail -- this is a display nicety,
/// never something that should blank out now-playing info.
/// </summary>
public sealed class AppNameResolver
{
    private readonly Dictionary<string, string> _cache = new();

    public string Resolve(string aumid)
    {
        if (string.IsNullOrEmpty(aumid))
            return aumid;

        if (_cache.TryGetValue(aumid, out var cached))
            return cached;

        var resolved = TryResolvePackaged(aumid) ?? TryResolveWin32(aumid) ?? aumid;
        _cache[aumid] = resolved;
        return resolved;
    }

    private static string? TryResolvePackaged(string aumid)
    {
        // AppInfo.GetFromAppUserModelId needs Windows 10.0.19041.0+ -- higher than this
        // app's overall SupportedOSPlatformVersion (10.0.17763.0, set for SMTC). Gate
        // explicitly rather than lower the whole app's floor for a cosmetic nicety; falls
        // through to the Win32 tier (still gated by its own checks) on older Windows.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return null;

        try
        {
            var info = AppInfo.GetFromAppUserModelId(aumid);
            var name = info?.DisplayInfo?.DisplayName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            // Not a real/registered AUMID -- the common case for classic Win32 apps, which
            // just report their exe name/path as the SMTC source with nothing registered
            // against it. Not a failure worth logging; the Win32 tier below is the expected
            // path for exactly this case.
            return null;
        }
    }

    /// <summary>
    /// Fallback for classic Win32 apps: SMTC reports either the app's bare exe name (e.g.
    /// "Chrome.exe") or its full path depending on whether the app called
    /// SetCurrentProcessExplicitAppUserModelID. A full path can be read directly; a bare name
    /// has no path info at all in the AUMID itself, so it's matched against currently running
    /// processes by image name (SMTC gives no PID to correlate by) and read from there.
    /// </summary>
    private static string? TryResolveWin32(string aumid)
    {
        try
        {
            var exePath = ResolveExePath(aumid);
            if (exePath is null || !File.Exists(exePath))
                return null;

            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            // Malformed path, or a locked-down/elevated process this app can't read version
            // info from -- same "just fall back to the raw AUMID" contract as the packaged
            // tier above.
            return null;
        }
    }

    private static string? ResolveExePath(string aumid)
    {
        if (aumid.Contains(Path.DirectorySeparatorChar) || aumid.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(aumid) ? aumid : null;

        var wantedName = Path.GetFileNameWithoutExtension(aumid);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.Equals(process.ProcessName, wantedName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                return process.MainModule?.FileName;
            }
            catch
            {
                // Cross-process access can fail for elevated/protected processes -- skip that
                // one and keep checking the rest rather than aborting the whole lookup.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }
}
