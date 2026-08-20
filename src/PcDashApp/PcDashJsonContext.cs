using System.Text.Json.Serialization;

namespace PcDashApp;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(NowPlayingPayload))]
[JsonSerializable(typeof(AppConfig))]
internal partial class PcDashJsonContext : JsonSerializerContext
{
}
