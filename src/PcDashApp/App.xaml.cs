using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PcDashApp;

/// <summary>
/// Global unhandled-exception handling is the WPF equivalent of the old console app's
/// top-level try/catch (see HARDWARE.md "double-click-launched console app is invisible"):
/// a WPF window that throws before it's shown (or off the UI thread) just silently exits
/// with no console to print to, unless these are wired up -- same invisible-crash risk,
/// different mechanism.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowFatalError(ex);
    }

    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "pc-dash-app-crash.log");

    private static void ShowFatalError(Exception ex)
    {
        // Log to disk FIRST, before attempting any UI. A crash inside WPF's own
        // rendering/text pipeline (e.g. the InvariantGlobalization incompatibility found
        // 2026-08-21 -- WPF's font cache needs a real CultureInfo, throws under invariant
        // globalization) can make MessageBox.Show itself throw, which would otherwise
        // swallow the original exception with no trace at all.
        try
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best effort -- fall through to the MessageBox attempt regardless.
        }

        try
        {
            MessageBox.Show(
                $"pc-dash-app hit an unhandled error and needs to close:\n\n{ex}\n\n(also logged to {CrashLogPath})",
                "pc-dash-app - fatal error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If even the error dialog can't render, the log file above is the last
            // remaining way to diagnose what happened.
        }
    }
}
