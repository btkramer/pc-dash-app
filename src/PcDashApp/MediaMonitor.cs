using Windows.Media.Control;

namespace PcDashApp;

public sealed record NowPlayingInfo(
    bool IsPlaying,
    string? Title,
    string? Artist,
    string? Source,
    long? PositionMs,
    long? DurationMs);

/// <summary>
/// Three-way read outcome, not just nullable -- pc-dash-hub's PUT semantics distinguish
/// "omit the field" (leave last-known value untouched) from "send an explicit null"
/// (clear it now). A transient SMTC read failure must omit (Unknown); an actual
/// session-ended must clear (NoSession). Conflating those into one null would clobber
/// good last-known now-playing data on every transient hiccup.
/// </summary>
public enum MediaReadStatus { Unknown, NoSession, Playing }

public sealed record MediaReadResult(MediaReadStatus Status, NowPlayingInfo? Info)
{
    public static readonly MediaReadResult UnknownResult = new(MediaReadStatus.Unknown, null);
    public static readonly MediaReadResult NoSessionResult = new(MediaReadStatus.NoSession, null);
    public static MediaReadResult PlayingResult(NowPlayingInfo info) => new(MediaReadStatus.Playing, info);
}

/// <summary>
/// Reads now-playing state via SMTC (Windows.Media.Control). SMTC reports "a media session is
/// active" per source app, not per browser tab -- a YouTube tab still resolves to "Google
/// Chrome"/"Microsoft Edge" (T15 resolves the AUMID to that friendly app name via
/// <see cref="AppNameResolver"/>) unless the site's own Media Session metadata happens to
/// populate Title/Artist. T24 layers a best-effort site-name resolution on top for known
/// browsers (<see cref="BrowserTabResolver"/>, correlating the SMTC Title against that
/// browser's window titles) -- when it finds a confident match, Source becomes the site name
/// (e.g. "YouTube") instead of the browser's app name; otherwise falls back to the app name as
/// before. Still no true tab-level identity from SMTC itself, no clean way around that short
/// of a browser extension -- this is a heuristic on top, not a real fix to the limitation.
/// T25 follow-up: <see cref="ExtrapolatePosition"/> -- SMTC's own Position is a snapshot as of
/// TimelineProperties.LastUpdatedTime, not a live clock; most apps only push timeline updates
/// sporadically (often only on play/pause/seek), so reading Position raw on every poll returns
/// a value that sits static between those real updates even while genuinely playing.
/// </summary>
public sealed class MediaMonitor
{
    private readonly AppNameResolver _appNameResolver = new();

    public async Task<MediaReadResult> ReadNowPlayingAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session is null)
                return MediaReadResult.NoSessionResult;

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            long? durationMs = timeline is null ? null : (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds;
            long? positionMs = timeline is null ? null : ClampPositionMs(
                ExtrapolatePosition(timeline, playback, isPlaying), durationMs);

            var rawSource = session.SourceAppUserModelId;
            var appName = rawSource is null ? null : _appNameResolver.Resolve(rawSource);
            var siteName = rawSource is null || appName is null
                ? null
                : BrowserTabResolver.TryResolveSiteName(rawSource, props?.Title, appName);

            return MediaReadResult.PlayingResult(new NowPlayingInfo(
                IsPlaying: isPlaying,
                Title: props?.Title,
                Artist: props?.Artist,
                Source: siteName ?? appName,
                PositionMs: positionMs,
                DurationMs: durationMs));
        }
        catch (Exception)
        {
            // Transient/unreadable -- caller must NOT treat this as "nothing playing"
            // (that would clear good last-known state on every hiccup).
            return MediaReadResult.UnknownResult;
        }
    }

    /// <summary>
    /// Extrapolates a live position forward from SMTC's last-reported snapshot, since the
    /// source app may not have pushed a fresh Position value this poll (confirmed via
    /// pc-dash-hub's live logs, 2026-08-21: raw Position sat static for stretches up to 13.4s
    /// during confirmed continuous playback). While playing, adds elapsed wall-clock time since
    /// <c>LastUpdatedTime</c> (scaled by the reported playback rate) to the last snapshot;
    /// while paused/stopped, or if the source never set a real <c>LastUpdatedTime</c>, returns
    /// the raw snapshot unchanged -- there's nothing to extrapolate from a genuinely static
    /// position.
    /// </summary>
    private static TimeSpan ExtrapolatePosition(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback,
        bool isPlaying)
    {
        var position = timeline.Position;
        if (!isPlaying)
            return position;

        var lastUpdated = timeline.LastUpdatedTime;
        if (lastUpdated == default)
            return position;

        var elapsed = DateTimeOffset.Now - lastUpdated;
        if (elapsed <= TimeSpan.Zero)
            return position;

        var rate = playback?.PlaybackRate ?? 1.0;
        if (rate <= 0)
            return position;

        return position + TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
    }

    /// <summary>Keeps extrapolation from overshooting past the track's own reported end --
    /// worth guarding since a real SMTC update can lag slightly behind a track's actual end.</summary>
    private static long ClampPositionMs(TimeSpan position, long? durationMs)
    {
        var ms = (long)position.TotalMilliseconds;
        if (ms < 0)
            return 0;
        if (durationMs is { } d && d > 0 && ms > d)
            return d;
        return ms;
    }
}
