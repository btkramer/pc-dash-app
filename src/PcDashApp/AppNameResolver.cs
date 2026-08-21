using System.Collections.Generic;
using Windows.ApplicationModel;

namespace PcDashApp;

/// <summary>
/// Resolves SMTC's raw SourceAppUserModelId (AUMID) -- e.g. "Chrome", "Spotify.exe", or a
/// packaged app's PackageFamilyName!AppId -- to a human-readable app/player name, via the
/// WinRT <c>AppInfo.GetFromAppUserModelId</c> API (same TFM/UseWinRT mechanism SMTC itself
/// already relies on -- no extra package). This resolves the *app*, not the page/tab: a
/// YouTube tab playing in Chrome still resolves to "Google Chrome", not "YouTube" -- SMTC
/// has no tab-level identity to resolve from (see MediaMonitor.cs's doc comment). Falls back
/// to the raw AUMID whenever resolution fails or returns nothing usable -- this is a display
/// nicety, never something that should blank out now-playing info.
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

        var resolved = TryResolve(aumid) ?? aumid;
        _cache[aumid] = resolved;
        return resolved;
    }

    private static string? TryResolve(string aumid)
    {
        // AppInfo.GetFromAppUserModelId needs Windows 10.0.19041.0+ -- higher than this
        // app's overall SupportedOSPlatformVersion (10.0.17763.0, set for SMTC). Gate
        // explicitly rather than lower the whole app's floor for a cosmetic nicety; falls
        // back to the raw AUMID on older Windows, same as any other resolution failure.
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
            // Not a real/registered AUMID (common for classic Win32 apps that just set an
            // arbitrary SMTC source string), or the lookup itself failed -- fall back to the
            // raw value rather than erroring out the whole now-playing read over a cosmetic
            // lookup.
            return null;
        }
    }
}
