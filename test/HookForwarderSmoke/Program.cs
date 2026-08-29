using System.Text.Json;
using System.Text.Json.Nodes;
using PcDashApp.HookForwarder;

var baseUrl = args.Length > 0 ? args[0] : "http://192.168.1.5:8015";
var sessionId = "smoke-test-t58-" + Guid.NewGuid().ToString("N")[..8];

Console.WriteLine($"Live T58 agent-session smoke test against {baseUrl}, session '{sessionId}'.");
Console.WriteLine("Writes and deletes a clearly-labeled synthetic session.\n");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
IAgentSessionClient client = new AgentSessionClient(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, baseUrl);
var failures = 0;

void Check(string label, bool pass)
{
    Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label}");
    if (!pass) failures++;
}

async Task<(int status, JsonElement? body)> Get()
{
    var response = await http.GetAsync($"{baseUrl}/api/agent-sessions/{sessionId}");
    var text = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"  GET -> {(int)response.StatusCode} {text}");
    return response.IsSuccessStatusCode
        ? ((int)response.StatusCode, JsonDocument.Parse(text).RootElement)
        : ((int)response.StatusCode, null);
}

// --- Step A: brand-new session doesn't exist yet ---
Console.WriteLine("Step A: GET a never-before-seen session id (expect 404)...");
var (statusA, _) = await Get();
Check("404 before any PUT", statusA == 404);

// --- Step B: SessionStart-equivalent PUT (matches HookEventMapper's SessionStart case) ---
Console.WriteLine("\nStep B: PUT agentProfile/status=idle/project/startedAt (SessionStart shape)...");
var startedAt = DateTime.UtcNow.ToString("O");
var okB = await client.ReportAsync(sessionId, new JsonObject
{
    ["agentProfile"] = "claude-code",
    ["status"] = "idle",
    ["project"] = "[SMOKE TEST] hookforwarder",
    ["startedAt"] = startedAt,
}, CancellationToken.None);
Check("PUT succeeded", okB);

var (statusB, bodyB) = await Get();
Check("200 after PUT", statusB == 200);
Check("agentProfile == claude-code", bodyB is { } b1 && b1.GetProperty("agentProfile").GetString() == "claude-code");
Check("status == idle", bodyB is { } b2 && b2.GetProperty("status").GetString() == "idle");
Check("project round-trips", bodyB is { } b3 && b3.GetProperty("project").GetString() == "[SMOKE TEST] hookforwarder");
Check("sessionId in response == what we sent", bodyB is { } b4 && b4.GetProperty("sessionId").GetString() == sessionId);
Check("updatedAt present (server-stamped)", bodyB is { } b5 && b5.TryGetProperty("updatedAt", out _));
Check("lastUpdateSecondsAgo is small (<=5)", bodyB is { } b6 && b6.GetProperty("lastUpdateSecondsAgo").GetInt32() <= 5);
Check("stale == false (just updated)", bodyB is { } b7 && b7.GetProperty("stale").GetBoolean() == false);

// --- Step C: partial PUT (status only) -- confirm deep-merge, project/startedAt untouched ---
Console.WriteLine("\nStep C: PUT status=working only, everything else omitted (PreToolUse shape)...");
var okC = await client.ReportAsync(sessionId, new JsonObject
{
    ["status"] = "working",
}, CancellationToken.None);
Check("PUT succeeded", okC);

var (_, bodyC) = await Get();
Check("status == working", bodyC is { } c1 && c1.GetProperty("status").GetString() == "working");
Check("project still round-trips (omitted field left untouched)",
    bodyC is { } c2 && c2.GetProperty("project").GetString() == "[SMOKE TEST] hookforwarder");
Check("agentProfile still claude-code (omitted field left untouched)",
    bodyC is { } c3 && c3.GetProperty("agentProfile").GetString() == "claude-code");

// --- Step D: Stop-equivalent PUT ---
Console.WriteLine("\nStep D: PUT status=done (Stop shape)...");
var okD = await client.ReportAsync(sessionId, new JsonObject { ["status"] = "done" }, CancellationToken.None);
Check("PUT succeeded", okD);
var (_, bodyD) = await Get();
Check("status == done", bodyD is { } d1 && d1.GetProperty("status").GetString() == "done");

// --- Step E: appears in the list while present ---
Console.WriteLine("\nStep E: GET /api/agent-sessions includes our session while it exists...");
var listText = await http.GetStringAsync($"{baseUrl}/api/agent-sessions");
var list = JsonDocument.Parse(listText).RootElement;
Check("session appears in the list", list.EnumerateArray().Any(e => e.GetProperty("sessionId").GetString() == sessionId));

// --- Step F: DELETE (SessionEnd shape), confirm gone ---
Console.WriteLine("\nStep F: DELETE (SessionEnd shape), then confirm 404...");
var okF = await client.DeleteAsync(sessionId, CancellationToken.None);
Check("DELETE succeeded", okF);
var (statusF, _) = await Get();
Check("404 after DELETE", statusF == 404);

// --- Step G: DELETE is idempotent (our client treats a second DELETE's 404 as success) ---
Console.WriteLine("\nStep G: DELETE again on the now-gone session (client should treat 404 as success)...");
var okG = await client.DeleteAsync(sessionId, CancellationToken.None);
Check("second DELETE still reports success (idempotent)", okG);

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;
