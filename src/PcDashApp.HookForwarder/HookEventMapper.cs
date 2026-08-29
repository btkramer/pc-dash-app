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
/// </summary>
internal static class HookEventMapper
{
    /// <summary>Null return means "don't forward a status PUT for this event"
    /// (SessionEnd -- handled as a DELETE by the caller; SubagentStop/PreCompact/any
    /// event name this app doesn't recognize yet -- treated as nothing-to-report, not
    /// an error, since Claude Code's own hook event set can grow over time).</summary>
    public static string? MapStatus(string? hookEventName) => hookEventName switch
    {
        "SessionStart" => "idle",
        "UserPromptSubmit" => "working",
        "PreToolUse" => "working",
        "PostToolUse" => "working",
        "Notification" => "needs-input",
        "Stop" => "done",
        _ => null,
    };
}
