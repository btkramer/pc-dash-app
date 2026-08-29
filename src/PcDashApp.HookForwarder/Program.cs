using System.Net.Http;
using System.Text.Json.Nodes;
using PcDashApp.HookForwarder;

// T58: invoked once per Claude Code hook event, hook JSON on stdin. Must NEVER hang or
// exit non-zero in a way that disrupts the calling Claude Code session -- everything
// below is wrapped so the worst case is "silently didn't report this one event,"
// logged to a local file instead of stderr. See HOOKS_SCHEMA.md for the wire shape
// (unconfirmed by pc-dash-hub as of this writing) and the "Rollout" section for why
// this isn't wired into any real settings.json yet.

try
{
    await RunAsync();
}
catch (Exception ex)
{
    HookLog.Write($"unhandled: {ex}");
}
// Always exit 0 -- a non-zero exit from a hook command can itself surface to the user
// in ways this notify-only v1 has no business causing.
return 0;

static async Task RunAsync()
{
    string raw;
    using (var reader = new StreamReader(Console.OpenStandardInput()))
    {
        raw = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrWhiteSpace(raw))
    {
        HookLog.Write("empty stdin, nothing to parse");
        return;
    }

    // Parsed leniently via JsonNode, not a strict record: this project has no way to
    // exercise a real Claude Code hook invocation from this container to confirm the
    // exact field set against memory, so missing/extra/renamed fields must degrade
    // gracefully rather than throw. See HOOKS_SCHEMA.md.
    JsonNode? node;
    try
    {
        node = JsonNode.Parse(raw);
    }
    catch (Exception ex)
    {
        HookLog.Write($"stdin was not valid JSON: {ex.Message}");
        return;
    }

    if (node is not JsonObject payload)
    {
        HookLog.Write("stdin JSON was not an object");
        return;
    }

    var sessionId = payload["session_id"]?.GetValue<string>();
    var hookEvent = payload["hook_event_name"]?.GetValue<string>();
    var cwd = payload["cwd"]?.GetValue<string>();

    if (string.IsNullOrEmpty(sessionId))
    {
        HookLog.Write($"no session_id in payload (hook_event_name={hookEvent ?? "?"}), skipping");
        return;
    }

    var baseUrl = Environment.GetEnvironmentVariable("PC_DASH_HUB_URL") ?? "http://pcdashhub.lan:8015";
    using var http = new HttpClient();
    IAgentSessionClient client = new AgentSessionClient(http, baseUrl);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    // SessionEnd is the real cleanup path (DELETE), not a status PUT -- see
    // HookEventMapper's doc comment / HOOKS_SCHEMA.md's "Session cleanup" answer.
    if (hookEvent == "SessionEnd")
    {
        var deleted = await client.DeleteAsync(sessionId, cts.Token);
        if (!deleted)
            HookLog.Write($"delete failed: session={sessionId}");
        return;
    }

    var status = HookEventMapper.MapStatus(hookEvent);
    if (status is null)
    {
        // Not an error -- SubagentStop/PreCompact/unrecognized events are deliberately
        // not forwarded. See HOOKS_SCHEMA.md's mapping table.
        return;
    }

    // Field names match pc-dash-hub's confirmed T57 model exactly. Their PUT body is
    // strict (extra="forbid" bit this app once already -- see T46 in PROGRESS.md), so
    // this deliberately sends ONLY the four confirmed fields, nothing extra like a
    // "host" or raw hookEvent debug field, however useful those would be locally.
    var body = new JsonObject
    {
        ["agentProfile"] = "claude-code",
        ["status"] = status,
        ["project"] = DeriveProjectName(cwd),
    };
    if (hookEvent == "SessionStart")
        body["startedAt"] = DateTime.UtcNow.ToString("O");

    var ok = await client.ReportAsync(sessionId, body, cts.Token);
    if (!ok)
        HookLog.Write($"report failed: session={sessionId} status={status} hookEvent={hookEvent}");
}

// Last non-empty path segment of cwd, e.g. "/personal/.../pc-dash-app" -> "pc-dash-app".
// Deliberately simple (no slugification) -- project folder names in this ecosystem are
// already kebab-case. Falls back to the raw cwd (or "unknown") if it can't derive one,
// rather than throwing.
static string DeriveProjectName(string? cwd)
{
    if (string.IsNullOrEmpty(cwd))
        return "unknown";

    var trimmed = cwd.TrimEnd('/', '\\');
    var lastSep = trimmed.LastIndexOfAny(['/', '\\']);
    var segment = lastSep >= 0 ? trimmed[(lastSep + 1)..] : trimmed;
    return string.IsNullOrEmpty(segment) ? cwd : segment;
}
