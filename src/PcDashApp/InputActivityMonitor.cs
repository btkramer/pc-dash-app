using System.Runtime.InteropServices;

namespace PcDashApp;

/// <summary>
/// T76: real keyboard/mouse idle time via Windows' <c>GetLastInputInfo</c>, so pc-dash-hub/
/// lilygo-rotary-knob can tell "PC is on but untouched" from "Brian is actively at the
/// keyboard" -- the knob was sleeping during active use because nothing else in pcState
/// (volume/now-playing) reliably changes on every real interaction. GetLastInputInfo reports
/// the tick count of the last keyboard/mouse input system-wide (not scoped to this process),
/// which is exactly the standard Windows idle-time API for this question -- no per-device
/// polling needed.
/// </summary>
public static class InputActivityMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Seconds since the last real keyboard/mouse input, or null if the OS call
    /// failed -- reported unknown (omit this tick), never coerced to 0, so a failed read can
    /// never masquerade as "just used".</summary>
    public static int? SecondsSinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return null;

        // Both dwTime and Environment.TickCount are 32-bit ms-since-boot, wrapping at ~49.7
        // days; unchecked uint subtraction resolves the wraparound correctly as long as the
        // idle gap itself is under ~24.8 days -- true for any real "how long idle" reading.
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return (int)(idleMs / 1000);
    }
}
