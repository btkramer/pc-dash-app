using PcDashApp;

var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
Console.WriteLine($"pc-dash-app starting -- reporting to {config.HubBaseUrl} as device '{config.DeviceId}' every {config.ReportIntervalMs}ms");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
var hubClient = new HubClient(http, config.HubBaseUrl, config.DeviceId);
using var audioMonitor = new AudioMonitor();
var mediaMonitor = new MediaMonitor();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

while (!cts.IsCancellationRequested)
{
    var volume = audioMonitor.ReadVolumeAndMute();
    var media = await mediaMonitor.ReadNowPlayingAsync();

    var update = new PcStateUpdate
    {
        VolumePercent = volume?.VolumePercent,
        SpeakerMuted = volume?.Muted,
        NowPlayingIsPlaying = media.Status switch
        {
            MediaReadStatus.Playing => media.Info!.IsPlaying,
            MediaReadStatus.NoSession => false,
            _ => null // Unknown -- omit, don't clobber last-known state on a read hiccup
        },
        NowPlaying = media.Status == MediaReadStatus.Playing
            ? new NowPlayingPayload(media.Info!.Title, media.Info.Artist, media.Info.Source,
                media.Info.PositionMs, media.Info.DurationMs)
            : null,
        ClearNowPlaying = media.Status == MediaReadStatus.NoSession
    };

    var ok = await hubClient.ReportStateAsync(update, cts.Token);
    Console.WriteLine(ok
        ? $"[{DateTime.Now:T}] reported: vol={update.VolumePercent}% muted={update.SpeakerMuted} playing={update.NowPlayingIsPlaying} track={media.Info?.Title ?? "(none)"}"
        : $"[{DateTime.Now:T}] report failed (hub unreachable?)");

    try
    {
        await Task.Delay(config.ReportIntervalMs, cts.Token);
    }
    catch (TaskCanceledException)
    {
        break;
    }
}

Console.WriteLine("pc-dash-app stopped.");
