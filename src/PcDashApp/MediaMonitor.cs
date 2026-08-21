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
/// Reads now-playing state via SMTC (Windows.Media.Control). Known, accepted limitation
/// (see ../../arduino/lilygo-rotary-knob/ECOSYSTEM_PLAN.md "YouTube-aware pause"): SMTC
/// reports "a media session is active" per source app, not per browser tab -- a YouTube
/// tab still resolves to "Google Chrome"/"Microsoft Edge" (T15 resolves the AUMID to that
/// friendly app name via <see cref="AppNameResolver"/>) unless the site's own Media Session
/// metadata happens to populate Title/Artist -- there's no tab-level identity in SMTC to
/// resolve a friendly name from, no clean way around this short of a browser extension.
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

            long? positionMs = timeline is null ? null : (long)timeline.Position.TotalMilliseconds;
            long? durationMs = timeline is null ? null : (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds;

            var rawSource = session.SourceAppUserModelId;

            return MediaReadResult.PlayingResult(new NowPlayingInfo(
                IsPlaying: isPlaying,
                Title: props?.Title,
                Artist: props?.Artist,
                Source: rawSource is null ? null : _appNameResolver.Resolve(rawSource),
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
}
