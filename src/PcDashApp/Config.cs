using System.Text.Json;

namespace PcDashApp;

public sealed class AppConfig
{
    public string HubBaseUrl { get; init; } = "http://pcdashhub.lan:8015";
    public string DeviceId { get; init; } = "t-encoder-pro";
    public int ReportIntervalMs { get; init; } = 5000;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AppConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }
}
