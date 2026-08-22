using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PcDashApp;

/// <summary>
/// T24: best-effort resolution of a browser tab's site name (e.g. "YouTube"), layered on top
/// of <see cref="AppNameResolver"/>'s app-level name (e.g. "Google Chrome"). SMTC has no
/// tab-level identity at all (see <see cref="MediaMonitor"/>'s doc comment) -- there is no API
/// that hands this back directly. Instead this correlates the SMTC-reported media Title
/// against the window titles of that browser's own top-level windows: most browsers append
/// " - {Browser Name}" to their window title, and sites that set their own document.title
/// often append "- SiteName" before that (e.g. YouTube: "{video title} - YouTube", so the
/// browser's window title ends up "{video title} - YouTube - Google Chrome"). Only trusted
/// when a window title actually starts with the SMTC Title -- otherwise this returns null
/// rather than guessing. Heuristic and best-effort by nature: title formats vary by browser
/// and by site (many sites don't append a site name at all), and if two open tabs from the
/// same site have identical page titles this can't tell them apart -- untested on real
/// Windows, needs verification across multiple browsers/sites before being trusted.
/// </summary>
public static class BrowserTabResolver
{
    // Extend as needed -- deliberately small and explicit rather than trying to detect "is
    // this a browser" generically, since a false positive here just means a wasted window
    // enumeration, not a wrong answer (parsing only trusts an exact Title-prefix match).
    private static readonly HashSet<string> KnownBrowserProcessNames = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave"
    };

    /// <param name="aumid">Raw SMTC SourceAppUserModelId -- used to find the owning process by
    /// image name, same bare-name-or-path handling as <see cref="AppNameResolver"/>.</param>
    /// <param name="mediaTitle">Title as reported by SMTC for the currently playing session.</param>
    /// <param name="resolvedAppName">The app-level friendly name already resolved (e.g. "Google
    /// Chrome") -- stripped off the end of a matching window title, if present.</param>
    public static string? TryResolveSiteName(string aumid, string? mediaTitle, string resolvedAppName)
    {
        if (string.IsNullOrWhiteSpace(mediaTitle))
            return null;

        try
        {
            var processName = Path.GetFileNameWithoutExtension(aumid);
            if (!KnownBrowserProcessNames.Contains(processName))
                return null;

            foreach (var title in EnumerateWindowTitles(processName))
            {
                if (!title.StartsWith(mediaTitle, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var remainder = title.Substring(mediaTitle.Length);

                var appSuffix = " - " + resolvedAppName;
                if (remainder.EndsWith(appSuffix, System.StringComparison.OrdinalIgnoreCase))
                    remainder = remainder.Substring(0, remainder.Length - appSuffix.Length);

                remainder = remainder.Trim(' ', '-');
                if (!string.IsNullOrWhiteSpace(remainder))
                    return remainder;
            }
        }
        catch
        {
            // Best-effort only -- any interop/enumeration failure just means no site name,
            // never something that should break the app-level Source that already resolved.
        }

        return null;
    }

    private static IEnumerable<string> EnumerateWindowTitles(string processName)
    {
        var titles = new List<string>();

        bool EnumProc(nint hWnd, nint _)
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!string.Equals(process.ProcessName, processName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true; // Process exited between enumeration and lookup, or inaccessible.
            }

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.Length > 0)
                titles.Add(sb.ToString());

            return true;
        }

        EnumWindows(EnumProc, 0);
        return titles;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
}
