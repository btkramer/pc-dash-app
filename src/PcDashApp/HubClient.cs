using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PcDashApp;

/// <summary>
/// Contract confirmed live by the pc-dash-hub session 2026-08-19 (matches what was
/// proposed): PUT /api/devices/{id}/state accepts an optional "pcState" block.
/// Field-level semantics that drove the JsonObject-based builder below, per their
/// confirmation -- these are NOT uniform "always send the value or omit it":
///   - Omitting a field entirely leaves the server's last-known value untouched.
///   - Sending it as JSON null explicitly clears it (only meaningful for nowPlaying).
///   - volumePercent is server-validated 0-100 (422 if out of range).
///   - pcConnected/lastSeenSecondsAgo must never be sent -- server computes both from
///     a server-stamped lastSeenAt, ignores those keys if present.
/// </summary>
public sealed record NowPlayingPayload(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("artist")] string? Artist,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("positionMs")] long? PositionMs,
    [property: JsonPropertyName("durationMs")] long? DurationMs);

/// <summary>
/// What to report this tick. Null on a property means "omit -- leave server's
/// last-known value alone" (e.g. a transient audio/media read failure), NOT "clear it".
/// ClearNowPlaying is the one explicit "send JSON null" case, for when playback has
/// genuinely stopped.
/// </summary>
public sealed class PcStateUpdate
{
    public int? VolumePercent { get; init; }
    public bool? SpeakerMuted { get; init; }
    public bool? NowPlayingIsPlaying { get; init; }
    public NowPlayingPayload? NowPlaying { get; init; }
    public bool ClearNowPlaying { get; init; }
}

public sealed class HubClient
{
    private readonly HttpClient _http;
    private readonly string _deviceId;

    public HubClient(HttpClient http, string baseUrl, string deviceId)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _deviceId = deviceId;
    }

    public async Task<bool> ReportStateAsync(PcStateUpdate update, CancellationToken ct)
    {
        var pcState = new JsonObject();

        if (update.VolumePercent is { } vol)
            pcState["volumePercent"] = vol;
        if (update.SpeakerMuted is { } muted)
            pcState["speakerMuted"] = muted;
        if (update.NowPlayingIsPlaying is { } playing)
            pcState["nowPlayingIsPlaying"] = playing;

        if (update.ClearNowPlaying)
            pcState["nowPlaying"] = null;
        else if (update.NowPlaying is { } nowPlaying)
            pcState["nowPlaying"] = JsonSerializer.SerializeToNode(nowPlaying, PcDashJsonContext.Default.NowPlayingPayload);

        if (pcState.Count == 0)
            return true; // nothing known this tick -- skip the request rather than PUT an empty pcState

        var body = new JsonObject { ["pcState"] = pcState };

        try
        {
            // JsonObject/JsonNode serializes via its own converter, not reflection -- ToJsonString()
            // (rather than PutAsJsonAsync<JsonObject>) keeps this call trim-safe without a source-gen entry.
            using var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync($"/api/devices/{_deviceId}/state", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Hub unreachable/timed out -- caller just logs and retries next tick.
            return false;
        }
    }
}
