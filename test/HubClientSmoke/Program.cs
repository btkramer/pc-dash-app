using System.Text.Json;
using PcDashApp;

var baseUrl = args.Length > 0 ? args[0] : "http://192.168.1.5:8015";
const string deviceId = "t-encoder-pro";

Console.WriteLine($"Live wire-format smoke test against {baseUrl}, device '{deviceId}'.");
Console.WriteLine("This will briefly write synthetic pcState onto the real device record.\n");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var hubClient = new HubClient(http, baseUrl, deviceId);
var failures = 0;

void Check(string label, bool pass)
{
    Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label}");
    if (!pass) failures++;
}

async Task<JsonElement> GetState()
{
    var json = await http.GetStringAsync($"{baseUrl}/api/devices/{deviceId}/state");
    Console.WriteLine($"  GET -> {json}");
    return JsonDocument.Parse(json).RootElement.GetProperty("pcState");
}

// --- Step A: full synthetic state ---
Console.WriteLine("Step A: PUT full synthetic pcState (volume/mute/nowPlaying)...");
var okA = await hubClient.ReportStateAsync(new PcStateUpdate
{
    VolumePercent = 37,
    SpeakerMuted = false,
    NowPlayingIsPlaying = true,
    NowPlaying = new NowPlayingPayload("[SMOKE TEST] pc-dash-app wire check", "pc-dash-app", "smoke-test", 1000, 200000)
}, CancellationToken.None);
Check("PUT succeeded", okA);

var stateA = await GetState();
Check("volumePercent == 37", stateA.GetProperty("volumePercent").GetInt32() == 37);
Check("speakerMuted == false", stateA.GetProperty("speakerMuted").GetBoolean() == false);
Check("nowPlayingIsPlaying == true", stateA.GetProperty("nowPlayingIsPlaying").GetBoolean() == true);
Check("nowPlaying.title == smoke test title",
    stateA.GetProperty("nowPlaying").GetProperty("title").GetString() == "[SMOKE TEST] pc-dash-app wire check");
Check("pcConnected == true", stateA.GetProperty("pcConnected").GetBoolean() == true);
Check("lastSeenSecondsAgo is small (<=5)", stateA.GetProperty("lastSeenSecondsAgo").GetInt32() <= 5);

// --- Step B: clear nowPlaying only, confirm omitted fields stay untouched ---
Console.WriteLine("\nStep B: PUT with ClearNowPlaying=true, everything else omitted...");
var okB = await hubClient.ReportStateAsync(new PcStateUpdate
{
    ClearNowPlaying = true
}, CancellationToken.None);
Check("PUT succeeded", okB);

var stateB = await GetState();
Check("nowPlaying == null (explicit clear worked)", stateB.GetProperty("nowPlaying").ValueKind == JsonValueKind.Null);
Check("volumePercent still 37 (omitted field left untouched)", stateB.GetProperty("volumePercent").GetInt32() == 37);
Check("nowPlayingIsPlaying still true (omitted field left untouched)", stateB.GetProperty("nowPlayingIsPlaying").GetBoolean() == true);

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;
