using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace PcDashApp;

/// <summary>
/// T96: the WPF app's first settings <em>reader + writer</em>. Every other hub call this
/// app makes is outbound-only telemetry (<see cref="HubClient"/> PUTs pcState and never
/// reads anything back); this one both reads and writes the hub-global settings doc.
///
/// Backs two user-facing toggles in <see cref="MainWindow"/> -- knobBeepEnabled /
/// phoneBeepEnabled -- which gate the two-beep attention/completion chime on the knob and
/// the phone deck respectively. They are hub-global (one value each, not per-device):
/// canonical store is GET/PUT /api/settings (confirmed with pc-dash-hub, T96). We read the
/// canonical doc directly rather than the copy echoed into device-state, since these
/// aren't device-scoped.
///
/// PUT /api/settings is a partial field-level deep-merge on the hub side (built for T89):
/// a body of just {"knobBeepEnabled": false} leaves attentionRenagMinutes and every other
/// settings field untouched, so we only ever send the single field that changed. buzzerEnabled
/// stays alive hub-side as a deprecated alias mirrored from knobBeepEnabled for current knob
/// firmware -- not something this app touches.
///
/// Trim-safe the same way <see cref="HubClient"/> is: JsonNode parsing/serialization goes
/// through JsonNode's own converter, not reflection, so no source-gen context entry is needed.
/// </summary>
public sealed class SettingsClient : IDisposable
{
    private readonly HttpClient _http;

    public SettingsClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>Reads the current global settings. Returns null if the hub is unreachable or
    /// the response can't be parsed -- the caller shows "settings unavailable" rather than
    /// presenting a guessed state as truth (same principle as the T46 don't-make-a-failure-
    /// look-like-success gap). A missing field in an otherwise-valid response falls back to
    /// the documented default (true), since the hub omits unset fields from the doc.</summary>
    public async Task<BeepSettings?> GetAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("/api/settings", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var raw = await response.Content.ReadAsStringAsync(ct);
            if (JsonNode.Parse(raw) is not JsonObject obj)
                return null;

            return new BeepSettings(
                KnobBeepEnabled: ReadBool(obj, "knobBeepEnabled", fallback: true),
                PhoneBeepEnabled: ReadBool(obj, "phoneBeepEnabled", fallback: true));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>PUTs a single changed field. Returns false on any failure; the caller reverts
    /// the checkbox to its pre-toggle state so the UI never claims a write that didn't land.</summary>
    public async Task<bool> PutFieldAsync(string fieldName, bool value, CancellationToken ct)
    {
        var body = new JsonObject { [fieldName] = value };
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync("/api/settings", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ReadBool(JsonObject obj, string key, bool fallback)
    {
        if (obj.TryGetPropertyValue(key, out var node) && node is not null)
        {
            try { return node.GetValue<bool>(); }
            catch (Exception) { /* wrong type on the wire -- fall through to the default */ }
        }
        return fallback;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>The subset of the global settings doc this app reads/writes (T96).</summary>
public sealed record BeepSettings(bool KnobBeepEnabled, bool PhoneBeepEnabled);
