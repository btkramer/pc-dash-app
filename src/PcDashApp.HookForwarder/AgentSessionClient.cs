using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace PcDashApp.HookForwarder;

/// <summary>
/// Sends one agent-session status update to pc-dash-hub, per HOOKS_SCHEMA.md's
/// proposed (NOT yet confirmed by pc-dash-hub) PUT /api/agent-sessions/{sessionId}.
///
/// Built against an interface, not a concrete class directly used everywhere, per
/// T58's instruction to keep the pipeline "generic enough that control can be added
/// later without a rework" -- a future version could extend this interface with a
/// method to poll for pending approve/deny/interrupt commands without restructuring
/// how Program.cs drives it.
/// </summary>
internal interface IAgentSessionClient
{
    Task<bool> ReportAsync(string sessionId, JsonObject body, CancellationToken ct);

    /// <summary>Real cleanup path for a normal SessionEnd -- see HookEventMapper's
    /// doc comment for why SessionEnd is a DELETE, not a status PUT.</summary>
    Task<bool> DeleteAsync(string sessionId, CancellationToken ct);
}

internal sealed class AgentSessionClient : IAgentSessionClient
{
    private readonly HttpClient _http;

    public AgentSessionClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        // Deliberately NOT setting _http.Timeout here. An earlier version did (2s,
        // hard), which raced against Program.cs's own 3s CancellationTokenSource --
        // two independent timeout budgets on one call, the tighter one always winning
        // silently. Caught live 2026-08-28: a SessionEnd DELETE timed out on a cold
        // process start (first connection: DNS + TCP + first request) under back-to-
        // back test invocations, logged as "delete failed" even though a plain curl
        // immediately after succeeded in well under a second -- the hub was never slow,
        // this client's own inner timeout was just tighter than it needed to be. The
        // caller's CancellationToken (passed into every call below) is now the single
        // source of truth for how long one hook invocation is allowed to take.
    }

    public async Task<bool> ReportAsync(string sessionId, JsonObject body, CancellationToken ct)
    {
        try
        {
            // JsonObject serializes via its own converter (not reflection) -- same
            // trim-safe pattern as HubClient.cs's PUT, even though this project isn't
            // published trimmed today; costs nothing to stay consistent.
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync($"/api/agent-sessions/{Uri.EscapeDataString(sessionId)}", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Hub unreachable, DNS failure, timeout, TLS -- whatever it is, this must
            // never throw out of here. Program.cs logs the failure to a local file;
            // the hook event itself is simply not reported, no retry (each hook fire
            // is one-shot, the next real event is the natural retry).
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.DeleteAsync($"/api/agent-sessions/{Uri.EscapeDataString(sessionId)}", ct);
            // A 404 here just means nothing was ever successfully PUT for this session
            // (e.g. every prior event this session fired also failed to reach the hub)
            // -- not a real failure worth logging distinctly from any other non-2xx.
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
