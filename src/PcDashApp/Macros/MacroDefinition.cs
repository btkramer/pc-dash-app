using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PcDashApp.Macros;

/// <summary>
/// First-draft wire shape for T8 (macros field), not final -- see MACROS_SCHEMA.md at the
/// repo root for the full design writeup and open questions. No executor exists yet; this
/// is the type contract only, kept trim-safe (payload stays a raw JsonObject, same pattern
/// HubClient.cs already uses for pcState) since per-type payload shapes aren't settled.
/// </summary>
public sealed record MacroDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("payload")] JsonObject? Payload);

/// <summary>
/// Known values for <see cref="MacroDefinition.Target"/>. Not a closed set enforced anywhere
/// -- an unrecognized target should just mean "not for this app to execute", same as an
/// unrecognized Type.
/// </summary>
public static class MacroTarget
{
    public const string PcDashApp = "pc-dash-app";
    public const string PcDashHub = "pc-dash-hub";
}

/// <summary>
/// Known values for <see cref="MacroDefinition.Type"/> that this app is expected to execute
/// (MacroTarget.PcDashApp). Deliberately plain strings, not an enum -- MACROS_SCHEMA.md
/// treats `type` as an open dotted namespace so new types don't require a shared enum across
/// every participant (pc-dash-hub, lilygo-rotary-knob, the future phone deck).
/// </summary>
public static class MacroType
{
    public const string ObsSceneSwitch = "obs.sceneSwitch";
    public const string ObsSourceToggle = "obs.sourceToggle";
    public const string ObsStreamControl = "obs.streamControl";
    public const string DiscordCallAction = "discord.callAction";
    public const string KeystrokeSend = "keystroke.send";
    public const string ScreenshotCapture = "screenshot.capture";
    public const string WindowFocus = "window.focus";
}
