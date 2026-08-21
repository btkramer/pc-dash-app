using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PcDashApp;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 200;

    private readonly CancellationTokenSource _cts = new();
    private readonly StateReporter _reporter;
    private readonly DispatcherTimer _agoTimer;
    private DateTime? _lastSuccessAt;
    private int _logLineCount;

    public MainWindow()
    {
        InitializeComponent();

        var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        DeviceIdText.Text = config.DeviceId;
        HubUrlText.Text = config.HubBaseUrl;

        _reporter = new StateReporter(config);
        _reporter.Ticked += OnTicked;

        // Refreshes the "Xs ago" text between ticks too, not just when a new tick lands --
        // otherwise "last sync" would visibly freeze for the whole report interval.
        _agoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _agoTimer.Tick += (_, _) => UpdateLastSyncText();
        _agoTimer.Start();

        Closed += OnClosed;

        _ = _reporter.RunAsync(_cts.Token);
    }

    private void OnTicked(ReportTickResult tick)
    {
        // Ticked fires from the reporter's background loop, off the UI thread.
        Dispatcher.Invoke(() => Render(tick));
    }

    private void Render(ReportTickResult tick)
    {
        // These reflect the latest local read every poll (~1s, T17), independent of whether
        // this tick actually sent anything to the hub -- the window stays live even on ticks
        // that were skipped as unchanged.
        VolumeText.Text = tick.VolumePercent is { } v ? $"{v}%" : "unknown";
        MutedText.Text = tick.Muted switch { true => "Muted", false => "Unmuted", null => "unknown" };

        PlayStateText.Text = tick.NowPlayingIsPlaying switch
        {
            true => "Playing",
            false => "Paused / none",
            null => "unknown"
        };
        TrackText.Text = tick.Title is { } title
            ? (tick.Artist is { } artist ? $"{title} - {artist}" : title)
            : "(none)";
        SourceText.Text = tick.Source ?? "(none)";

        // tick.ReportOk is null on a tick that skipped the hub PUT entirely (T17: no change,
        // heartbeat not yet due) -- leave hub status/last-sync/log alone on those, they only
        // reflect an actual send attempt.
        if (tick.ReportOk is not { } reportOk)
            return;

        if (reportOk)
            _lastSuccessAt = tick.Timestamp;
        HubStatusText.Text = reportOk ? "Connected" : "Report failed (hub unreachable?)";
        UpdateLastSyncText();

        AppendLog(
            $"[{tick.Timestamp:T}] vol={VolumeText.Text} muted={MutedText.Text} " +
            $"playing={PlayStateText.Text} track={TrackText.Text} report={(reportOk ? "ok" : "FAILED")}");
    }

    private void UpdateLastSyncText()
    {
        LastSyncText.Text = _lastSuccessAt is { } t
            ? $"{t:T} ({(DateTime.Now - t).TotalSeconds:F0}s ago)"
            : "never";
    }

    private void AppendLog(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        if (++_logLineCount > MaxLogLines)
        {
            var firstNewline = LogBox.Text.IndexOf('\n');
            if (firstNewline >= 0)
                LogBox.Text = LogBox.Text[(firstNewline + 1)..];
            _logLineCount--;
        }
        LogBox.ScrollToEnd();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _agoTimer.Stop();
        _cts.Cancel();
        _reporter.Dispose();
    }
}
