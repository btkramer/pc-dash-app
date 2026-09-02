namespace PcDashApp.HookForwarder;

/// <summary>
/// Maps a Claude Code hook_event_name to one of the five agent-session statuses
/// confirmed against pc-dash-hub's real T57 schema (idle/working/needs-input/error/
/// done, a fixed set -- not a free string like agentProfile). Deliberately a single
/// pure function, not a class with state -- the whole pipeline is one-shot per process
/// invocation (see Program.cs).
///
/// SessionEnd is deliberately NOT in this table -- pc-dash-hub's DELETE
/// /api/agent-sessions/{id} is the real cleanup path for a normal exit, so
/// Program.cs special-cases SessionEnd before calling this mapper at all (see
/// HOOKS_SCHEMA.md's "Session cleanup" answer). "done" is reserved for Stop
/// (turn finished, waiting on you) rather than idle, since idle/done need to mean
/// two different things for the fixed 5-state set to make sense at all -- resolves
/// this doc's own earlier open question, not something pc-dash-hub dictated.
///
/// error still has no dedicated hook event and is NOT detected in this v1 (still
/// open, see HOOKS_SCHEMA.md).
///
/// T85: `Notification`'s payload carries its own `notification_type`, which turned out
/// to distinguish real pending-input moments (`permission_prompt`/`agent_needs_input`/
/// `elicitation_dialog`) from Claude Code's own generic `idle_prompt` (fires just from
/// sitting quiet, nothing actually pending) -- and other types like `auth_success` that
/// aren't a "waiting on you" signal either. Before this, every `Notification` mapped to
/// `needs-input`, which falsely flagged attention during ordinary idle stretches (the
/// root problem T84 was originally scoped to work around via focus-tracking). Only the
/// confirmed real-pending types map to `needs-input` now; everything else `Notification`
/// sends (including types this app doesn't recognize yet) gets the same treatment as
/// `SubagentStop`/`PreCompact` -- not forwarded, status left unchanged -- rather than
/// guessing a new type is safe to flag.
/// </summary>
internal static class HookEventMapper
{
    private static readonly HashSet<string> RealPendingInputNotificationTypes = new(StringComparer.Ordinal)
    {
        "permission_prompt",
        "agent_needs_input",
        "elicitation_dialog",
    };

    /// <summary>Null return means "don't forward a status PUT for this event"
    /// (SessionEnd -- handled as a DELETE by the caller; SubagentStop/PreCompact/any
    /// event name this app doesn't recognize yet -- treated as nothing-to-report, not
    /// an error, since Claude Code's own hook event set can grow over time). `Notification`
    /// additionally needs <paramref name="notificationType"/> to tell a real pending-input
    /// moment from a generic idle nudge -- see the type's doc comment.</summary>
    public static string? MapStatus(string? hookEventName, string? notificationType = null) => hookEventName switch
    {
        "SessionStart" => "idle",
        "UserPromptSubmit" => "working",
        "PreToolUse" => "working",
        "PostToolUse" => "working",
        "Notification" => notificationType is { } t && RealPendingInputNotificationTypes.Contains(t)
            ? "needs-input"
            : null, // idle_prompt, auth_success, or any other/unrecognized type -- not a real "waiting on you" signal
        "Stop" => "done",
        _ => null,
    };
}
