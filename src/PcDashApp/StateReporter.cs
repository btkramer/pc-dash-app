using System.Net.Http;

namespace PcDashApp;

/// <summary>One completed poll tick, for the UI to display. Mirrors what was actually read
/// this tick -- null fields mean the same "unknown, omitted" thing they mean in
/// <see cref="PcStateUpdate"/>, not "known to be zero/empty". <see cref="ReportOk"/> is null
/// when this tick didn't attempt a hub PUT at all (T17: no change and heartbeat not yet due)
/// -- distinct from a PUT that was attempted and failed.</summary>
public sealed record ReportTickResult(
    DateTime Timestamp,
    int? VolumePercent,
    bool? Muted,
    bool? NowPlayingIsPlaying,
    string? Title,
    string? Artist,
    string? Source,
    bool? ReportOk);

/// <summary>Last-successfully-sent values, for change comparison (T17). Deliberately excludes
/// PositionMs/DurationMs -- those change on essentially every poll during playback, so
/// including them would make "change-triggered" fire every tick and defeat the point; they
/// still ride along in the payload whenever a report goes out for another reason. Fields here
/// carry forward across polls that didn't have fresh reads (merged, not overwritten by nulls)
/// so an unrelated field going briefly unreadable doesn't look like "reverted to unknown".</summary>
internal sealed record ComparableState(
    int? VolumePercent,
    bool? Muted,
    bool? IsPlaying,
    string? Title,
    string? Artist,
    string? Source,
    bool HasTrack)
{
    public static ComparableState MergeAfterSend(ComparableState? previous, PcStateUpdate sent) => new(
        VolumePercent: sent.VolumePercent ?? previous?.VolumePercent,
        Muted: sent.SpeakerMuted ?? previous?.Muted,
        IsPlaying: sent.NowPlayingIsPlaying ?? previous?.IsPlaying,
        Title: sent.ClearNowPlaying ? null : sent.NowPlaying?.Title ?? previous?.Title,
        Artist: sent.ClearNowPlaying ? null : sent.NowPlaying?.Artist ?? previous?.Artist,
        Source: sent.ClearNowPlaying ? null : sent.NowPlaying?.Source ?? previous?.Source,
        HasTrack: !sent.ClearNowPlaying && (sent.NowPlaying is not null || (previous?.HasTrack ?? false)));

    /// <summary>True if this tick's (non-null/known) fields differ from what was last sent --
    /// only fields actually read this tick are compared, so a transient unread field never
    /// counts as a change either way.</summary>
    public bool DiffersFrom(PcStateUpdate candidate) =>
        (candidate.VolumePercent is { } v && v != VolumePercent) ||
        (candidate.SpeakerMuted is { } m && m != Muted) ||
        (candidate.NowPlayingIsPlaying is { } p && p != IsPlaying) ||
        (candidate.ClearNowPlaying && HasTrack) ||
        (candidate.NowPlaying is { } np && (np.Title != Title || np.Artist != Artist || np.Source != Source));
}

/// <summary>
/// The read-Core-Audio/SMTC-then-PUT-to-hub loop. T17: reports push on state change
/// (rate-limited to once per <see cref="AppConfig.PollIntervalMs"/>, since a change can only
/// be noticed once per poll) rather than blindly on a fixed timer, but keeps a heartbeat PUT
/// at <see cref="AppConfig.ReportIntervalMs"/> regardless of change -- pc-dash-hub's staleness
/// detection (T5/T10) was tuned around a steady ~5s heartbeat, and a quiet stretch (paused
/// media, stable volume) must not make the hub think the PC went offline. Position/duration
/// deliberately don't count as a "change" (see <see cref="ComparableState"/>) -- only the
/// heartbeat or an actual volume/mute/play-state/track change triggers a send.
/// </summary>
public sealed class StateReporter : IDisposable
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;
    private readonly HubClient _hubClient;
    private readonly AudioMonitor _audioMonitor = new();
    private readonly MediaMonitor _mediaMonitor = new();

    public event Action<ReportTickResult>? Ticked;

    public StateReporter(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _hubClient = new HubClient(_http, config.HubBaseUrl, config.DeviceId);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        ComparableState? lastSent = null;
        var lastSentAt = DateTime.MinValue;
        var heartbeat = TimeSpan.FromMilliseconds(_config.ReportIntervalMs);

        while (!ct.IsCancellationRequested)
        {
            var volume = _audioMonitor.ReadVolumeAndMute();
            var media = await _mediaMonitor.ReadNowPlayingAsync();

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

            var changed = lastSent is null || lastSent.DiffersFrom(update);
            var heartbeatDue = DateTime.UtcNow - lastSentAt >= heartbeat;

            bool? reportOk = null;
            if (changed || heartbeatDue)
            {
                reportOk = await _hubClient.ReportStateAsync(update, ct);
                if (reportOk == true)
                {
                    lastSent = ComparableState.MergeAfterSend(lastSent, update);
                    lastSentAt = DateTime.UtcNow;
                }
            }

            Ticked?.Invoke(new ReportTickResult(
                DateTime.Now,
                update.VolumePercent,
                update.SpeakerMuted,
                update.NowPlayingIsPlaying,
                media.Info?.Title,
                media.Info?.Artist,
                media.Info?.Source,
                reportOk));

            try
            {
                await Task.Delay(_config.PollIntervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _audioMonitor.Dispose();
        _http.Dispose();
    }
}
