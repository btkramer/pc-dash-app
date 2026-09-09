using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using PcDashApp.Logos;

namespace PcDashApp;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 200;

    private readonly CancellationTokenSource _cts = new();
    private readonly StateReporter _reporter;
    private readonly DispatcherTimer _agoTimer;
    // T52: HubLogoAssignmentStore, live-verified against pc-dash-hub 2026-08-28
    // (test/LogoSmoke) -- see ILogoAssignmentStore's doc comment for the swap history.
    private readonly ILogoAssignmentStore _logoStore;
    // T96: the app's first settings reader+writer -- two hub-global beep toggles.
    private readonly SettingsClient _settingsClient;
    // Suppresses the Checked/Unchecked handlers while we set the boxes programmatically
    // (initial load, or a revert after a failed PUT) so those don't fire a PUT of their own.
    private bool _suppressBeepToggleEvents;
    private DateTime? _lastSuccessAt;
    private int _logLineCount;

    public MainWindow()
    {
        InitializeComponent();

        var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        DeviceIdText.Text = config.DeviceId;
        HubUrlText.Text = config.HubBaseUrl;
        BuildTimeText.Text = GetBuildTimeLabel();

        _logoStore = new HubLogoAssignmentStore(config.HubBaseUrl);
        _settingsClient = new SettingsClient(config.HubBaseUrl);
        _reporter = new StateReporter(config);
        _reporter.Ticked += OnTicked;

        // Refreshes the "Xs ago" text between ticks too, not just when a new tick lands --
        // otherwise "last sync" would visibly freeze for the whole report interval.
        _agoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _agoTimer.Tick += (_, _) => UpdateLastSyncText();
        _agoTimer.Start();

        Closed += OnClosed;

        _ = _reporter.RunAsync(_cts.Token);
        _ = LoadBeepSettingsAsync();
    }

    /// <summary>Reads the current knob/phone beep toggles from the hub on window open.
    /// On failure the checkboxes stay disabled with an "unavailable" note -- we never show a
    /// guessed state as if it were the real setting.</summary>
    private async Task LoadBeepSettingsAsync()
    {
        var settings = await _settingsClient.GetAsync(_cts.Token);
        if (settings is null)
        {
            BeepSettingsNote.Text = "Beep settings unavailable (hub unreachable).";
            return;
        }

        _suppressBeepToggleEvents = true;
        KnobBeepCheckBox.IsChecked = settings.KnobBeepEnabled;
        PhoneBeepCheckBox.IsChecked = settings.PhoneBeepEnabled;
        _suppressBeepToggleEvents = false;

        KnobBeepCheckBox.IsEnabled = true;
        PhoneBeepCheckBox.IsEnabled = true;
        BeepSettingsNote.Text = "Applies to every session; shared with the phone deck.";
    }

    private void OnKnobBeepToggled(object sender, RoutedEventArgs e)
        => _ = PushBeepToggleAsync(KnobBeepCheckBox, "knobBeepEnabled");

    private void OnPhoneBeepToggled(object sender, RoutedEventArgs e)
        => _ = PushBeepToggleAsync(PhoneBeepCheckBox, "phoneBeepEnabled");

    /// <summary>PUTs the single toggled field. On failure, reverts the checkbox to its
    /// previous state (with events suppressed so the revert doesn't re-trigger a PUT) and
    /// notes it -- the UI must not claim a write that didn't land.</summary>
    private async Task PushBeepToggleAsync(System.Windows.Controls.CheckBox box, string fieldName)
    {
        if (_suppressBeepToggleEvents)
            return;

        var desired = box.IsChecked == true;
        box.IsEnabled = false;
        var ok = await _settingsClient.PutFieldAsync(fieldName, desired, _cts.Token);
        box.IsEnabled = true;

        if (ok)
        {
            BeepSettingsNote.Text = $"Saved: {box.Content} {(desired ? "on" : "off")}.";
        }
        else
        {
            _suppressBeepToggleEvents = true;
            box.IsChecked = !desired;
            _suppressBeepToggleEvents = false;
            BeepSettingsNote.Text = $"Couldn't save {box.Content} (hub unreachable?). Reverted.";
        }
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
        InputIdleText.Text = tick.LastInputSecondsAgo is { } idle ? $"{idle}s ago" : "unknown";

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

    private void OnProjectLogosClick(object sender, RoutedEventArgs e)
    {
        new ProjectLogosWindow(_logoStore) { Owner = this }.Show();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _agoTimer.Stop();
        _cts.Cancel();
        _reporter.Dispose();
        _logoStore.Dispose();
        _settingsClient.Dispose();
    }

    /// <summary>T27: which build is actually running has cost real debugging time twice
    /// (a stale dist/ during T13, a stale running process during T25) with no way to check
    /// from inside the app itself. Uses the main DLL's own last-write time as a build-time
    /// proxy -- accurate enough to answer "did this get relaunched after I republished," the
    /// actual question that mattered both times, without needing new MSBuild-time codegen.</summary>
    private static string GetBuildTimeLabel()
    {
        try
        {
            var dllPath = Assembly.GetExecutingAssembly().Location;
            var buildTimeUtc = File.GetLastWriteTimeUtc(dllPath);
            return $"Build: {buildTimeUtc:yyyy-MM-dd HH:mm} UTC";
        }
        catch
        {
            return "Build: unknown";
        }
    }
}
