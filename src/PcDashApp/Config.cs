using System.IO;
using System.Text.Json;

namespace PcDashApp;

public sealed class AppConfig
{
    public string HubBaseUrl { get; init; } = "http://pcdashhub.lan:8015";
    public string DeviceId { get; init; } = "t-encoder-pro";

    /// <summary>Heartbeat ceiling (T17): the max time between hub PUTs even with no state
    /// change, so pc-dash-hub's staleness detection (PC_STATE_STALE_AFTER_SECONDS, tuned in
    /// T5/T10 around this exact cadence) keeps working during quiet stretches. Same config
    /// key/default as before T17 -- only its role changed, from "how often we always report"
    /// to "how often we report at minimum."</summary>
    public int ReportIntervalMs { get; init; } = 5000;

    /// <summary>How often (T17) local Core Audio/SMTC state is read and checked for a
    /// change worth pushing immediately. Also the natural floor on push frequency, since a
    /// change can only be noticed once per poll -- Brian's suggested "1s floor" for T17.</summary>
    public int PollIntervalMs { get; init; } = 1000;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, PcDashJsonContext.Default.AppConfig) ?? new AppConfig();
    }
}
