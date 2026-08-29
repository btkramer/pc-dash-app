namespace PcDashApp.Logos;

/// <summary>
/// T52: the flat, unscoped generic-icon fallback set. "Flat" per Brian's 2026-08-22
/// decision (relayed in the T39 mockup revision history) means no category/theme
/// scoping -- every project's random auto-assign and every picker's generic-icon
/// row draws from this exact same list, regardless of what kind of project it is.
///
/// These ids are the wire value too (see LOGO_SCHEMA.md's ProjectLogoAssignment.Ref
/// for kind "generic") -- stable strings, not array indices, so adding/removing an
/// icon later never silently reassigns what an already-picked project renders.
/// Rendering itself (<see cref="GenericIconFactory"/>) is local to whichever client
/// draws it (this app today, the future phone UI later) -- the id is the only thing
/// that crosses the wire, same pattern MacroDefinition.Icon already uses.
/// </summary>
public static class GenericIconIds
{
    public const string Folder = "folder";
    public const string Terminal = "terminal";
    public const string Grid = "grid";
    public const string Cube = "cube";
    public const string Chip = "chip";
    public const string Gear = "gear";
    public const string Layers = "layers";
    public const string Flask = "flask";

    /// <summary>Display label for the picker's generic-icon row. Order here is also
    /// the order shown in the picker -- stable, not alphabetized, so it doesn't
    /// reshuffle across builds.</summary>
    public static readonly IReadOnlyList<(string Id, string Label)> All =
    [
        (Folder, "Folder"),
        (Terminal, "Terminal"),
        (Grid, "Grid"),
        (Cube, "Cube"),
        (Chip, "Chip"),
        (Gear, "Gear"),
        (Layers, "Layers"),
        (Flask, "Flask"),
    ];

    public static bool IsKnown(string id) => All.Any(i => i.Id == id);
}
