namespace PcDashApp.HookForwarder;

/// <summary>
/// Failure-only local log, deliberately not stdout/stderr -- this process's output
/// streams are Claude Code's own hook I/O channel (stdout can carry control-decision
/// JSON in future versions; stderr may surface to the user in some contexts). A
/// diagnosable trace belongs in a file, same "never let the error-reporting path
/// itself be invisible" lesson PcDashApp.csproj's own crash handler already learned
/// (see PROGRESS.md's T13 entries) -- applied here from the start rather than found
/// the hard way a second time.
/// </summary>
internal static class HookLog
{
    private static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "pc-dash-hook-forwarder.log");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(Path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // If even the log write fails (read-only temp dir, disk full, whatever),
            // there is nowhere left to report it -- swallow, never let logging itself
            // be the thing that makes a hook invocation fail visibly.
        }
    }
}
