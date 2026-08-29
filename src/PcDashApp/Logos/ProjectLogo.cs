using System.Text.Json.Serialization;

namespace PcDashApp.Logos;

/// <summary>
/// Known values for <see cref="ProjectLogoAssignment.Kind"/>. Open string, not an
/// enum -- same forward-compat reasoning MacroDefinition.Type/Target already use
/// (an unrecognized kind should mean "don't know how to render this yet", not a
/// deserialization failure).
/// </summary>
public static class LogoKind
{
    public const string Generic = "generic";
    public const string Upload = "upload";
}

/// <summary>
/// A project's current logo choice. This is the local (and, once
/// GET/PUT /api/logo-assignments/{key} is built hub-side, the wire) shape -- see
/// LOGO_SCHEMA.md's "Logo assignment, keyed by an opaque string" section, confirmed
/// with pc-dash-hub 2026-08-27 (originally drafted as a project_id-shaped endpoint;
/// revised to an opaque key since pc-dash-hub has no `projects` collection).
///
/// - Kind "generic": Ref is one of <see cref="GenericIconIds"/>'s stable string ids.
/// - Kind "upload": Ref is the library logo's id (see <see cref="LibraryLogo"/>),
///   not a raw file path or URL -- the same id every other project referencing the
///   same uploaded image would use, matching the "permanent, reusable library"
///   framing from the mockup's 2026-08-22 revision.
/// InitialsOverlay is independent of Kind -- either a generic icon or an uploaded
/// logo can have the initials badge toggled on top of it (T39's "explicit toggle,
/// not an automatic side effect").
/// </summary>
public sealed record ProjectLogoAssignment(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("initialsOverlay")] bool InitialsOverlay);

/// <summary>
/// One entry in the shared, cross-project logo library -- an uploaded image that,
/// once added, stays available for any project to pick (mockup's 2026-08-22
/// revision: "a logo becomes a permanent part of that library for future reuse
/// across any tracked project, not a one-off tied to the project that uploaded
/// it"). Storage/serving is pc-dash-hub's piece (LOGO_SCHEMA.md); this record is
/// the shape this app reads back, not where the bytes live.
/// </summary>
/// <summary>FileName is nullable -- confirmed live 2026-08-28 that pc-dash-hub
/// can return null for entries uploaded before it started capturing the
/// original filename (T52's live-test findings, LOGO_SCHEMA.md). Every reader
/// of this type needs a fallback (e.g. Id) rather than assuming it's always
/// present.</summary>
public sealed record LibraryLogo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("fileName")] string? FileName);
