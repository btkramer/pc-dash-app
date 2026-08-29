using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PcDashApp.Logos;

/// <summary>
/// Where project logo assignments + the uploaded-logo library live. Two
/// implementations exist: <see cref="LocalLogoAssignmentStore"/> (a local JSON
/// file + folder, the original interim stand-in) and <see cref="HubLogoAssignmentStore"/>
/// (real HTTP calls per LOGO_SCHEMA.md, live-verified 2026-08-28 -- MainWindow
/// uses this one now). This interface is the seam that swap happened through
/// without the picker UI needing to change at all, same reasoning as HubClient's
/// own interface-free-but-swappable shape.
/// </summary>
public interface ILogoAssignmentStore : IDisposable
{
    /// <summary>Returns the project's current assignment, or auto-assigns a
    /// random generic icon (never a random pick from the uploaded library --
    /// that rule was explicit in Brian's 2026-08-22 decision) if this is the
    /// project's first appearance.</summary>
    Task<ProjectLogoAssignment> GetOrAutoAssignAsync(string projectId);

    Task SetAssignmentAsync(string projectId, ProjectLogoAssignment assignment);

    Task<IReadOnlyList<string>> GetKnownProjectIdsAsync();

    Task<IReadOnlyList<LibraryLogo>> GetLibraryAsync();

    /// <summary>Copies <paramref name="sourceFilePath"/> into the library and
    /// returns its new permanent entry. Re-uploading the same bytes returns the
    /// existing entry rather than duplicating it (content-hash filename).</summary>
    Task<LibraryLogo> AddToLibraryAsync(string sourceFilePath);

    /// <summary>Resolves a library logo's id to a local file path this process
    /// can load an image from (the local store's own copy today; a future
    /// hub-backed store would download-and-cache here instead).</summary>
    string? ResolveLibraryFilePath(string libraryLogoId);
}

/// <summary>
/// Local-disk-only implementation: <c>%LocalAppData%\pc-dash-app\logos\</c> holds
/// <c>assignments.json</c> (projectId -> ProjectLogoAssignment), <c>library.json</c>
/// (the LibraryLogo list), and a <c>library\</c> subfolder with the actual uploaded
/// image bytes. Nothing here talks to pc-dash-hub -- that's the point, this is the
/// stand-in until LOGO_SCHEMA.md's endpoints exist. Swapping to a hub-backed store
/// later should be able to reuse assignments.json's shape as a one-time seed/import
/// if useful, but that's not designed yet.
/// </summary>
public sealed class LocalLogoAssignmentStore : ILogoAssignmentStore
{
    private readonly string _rootDir;
    private readonly string _assignmentsPath;
    private readonly string _libraryIndexPath;
    private readonly string _libraryFilesDir;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalLogoAssignmentStore(string? rootDir = null)
    {
        _rootDir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "pc-dash-app", "logos");
        _assignmentsPath = Path.Combine(_rootDir, "assignments.json");
        _libraryIndexPath = Path.Combine(_rootDir, "library.json");
        _libraryFilesDir = Path.Combine(_rootDir, "library");
        Directory.CreateDirectory(_libraryFilesDir);
    }

    public async Task<ProjectLogoAssignment> GetOrAutoAssignAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            var assignments = LoadAssignments();
            if (assignments.TryGetValue(projectId, out var existing))
                return existing;

            var (iconId, _) = GenericIconIds.All[Random.Shared.Next(GenericIconIds.All.Count)];
            var assigned = new ProjectLogoAssignment(LogoKind.Generic, iconId, InitialsOverlay: false);
            assignments[projectId] = assigned;
            SaveAssignments(assignments);
            return assigned;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAssignmentAsync(string projectId, ProjectLogoAssignment assignment)
    {
        await _lock.WaitAsync();
        try
        {
            var assignments = LoadAssignments();
            assignments[projectId] = assignment;
            SaveAssignments(assignments);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetKnownProjectIdsAsync() =>
        Task.FromResult<IReadOnlyList<string>>(LoadAssignments().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<IReadOnlyList<LibraryLogo>> GetLibraryAsync() =>
        Task.FromResult<IReadOnlyList<LibraryLogo>>(LoadLibrary());

    public async Task<LibraryLogo> AddToLibraryAsync(string sourceFilePath)
    {
        await _lock.WaitAsync();
        try
        {
            var bytes = await File.ReadAllBytesAsync(sourceFilePath);
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant();
            var ext = Path.GetExtension(sourceFilePath);
            var storedFileName = id + ext;
            var destPath = Path.Combine(_libraryFilesDir, storedFileName);

            var library = LoadLibrary();
            var existing = library.FirstOrDefault(l => l.Id == id);
            if (existing is not null)
                return existing; // same bytes already uploaded -- reuse, don't duplicate

            if (!File.Exists(destPath))
                await File.WriteAllBytesAsync(destPath, bytes);

            var entry = new LibraryLogo(id, storedFileName, Path.GetFileName(sourceFilePath));
            library.Add(entry);
            SaveLibrary(library);
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string? ResolveLibraryFilePath(string libraryLogoId)
    {
        var entry = LoadLibrary().FirstOrDefault(l => l.Id == libraryLogoId);
        if (entry is null)
            return null;
        var path = Path.Combine(_libraryFilesDir, entry.Url);
        return File.Exists(path) ? path : null;
    }

    public void Dispose() => _lock.Dispose();

    private Dictionary<string, ProjectLogoAssignment> LoadAssignments()
    {
        if (!File.Exists(_assignmentsPath))
            return new Dictionary<string, ProjectLogoAssignment>();

        try
        {
            var json = File.ReadAllText(_assignmentsPath);
            var node = JsonNode.Parse(json)?.AsObject();
            var result = new Dictionary<string, ProjectLogoAssignment>();
            if (node is null)
                return result;
            foreach (var (key, value) in node)
            {
                if (value is null) continue;
                var kind = value["kind"]?.GetValue<string>();
                var reference = value["ref"]?.GetValue<string>();
                var initials = value["initialsOverlay"]?.GetValue<bool>() ?? false;
                if (kind is null || reference is null) continue;
                result[key] = new ProjectLogoAssignment(kind, reference, initials);
            }
            return result;
        }
        catch (JsonException)
        {
            // Corrupt/hand-edited file -- treat as empty rather than crash the app on startup.
            return new Dictionary<string, ProjectLogoAssignment>();
        }
    }

    private void SaveAssignments(Dictionary<string, ProjectLogoAssignment> assignments)
    {
        var obj = new JsonObject();
        foreach (var (projectId, a) in assignments)
        {
            obj[projectId] = new JsonObject
            {
                ["kind"] = a.Kind,
                ["ref"] = a.Ref,
                ["initialsOverlay"] = a.InitialsOverlay
            };
        }
        File.WriteAllText(_assignmentsPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private List<LibraryLogo> LoadLibrary()
    {
        if (!File.Exists(_libraryIndexPath))
            return [];
        try
        {
            var json = File.ReadAllText(_libraryIndexPath);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return [];
            var result = new List<LibraryLogo>();
            foreach (var item in arr)
            {
                if (item is null) continue;
                var id = item["id"]?.GetValue<string>();
                var url = item["url"]?.GetValue<string>();
                var fileName = item["fileName"]?.GetValue<string>();
                if (id is null || url is null || fileName is null) continue;
                result.Add(new LibraryLogo(id, url, fileName));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveLibrary(List<LibraryLogo> library)
    {
        var arr = new JsonArray();
        foreach (var l in library)
        {
            arr.Add(new JsonObject
            {
                ["id"] = l.Id,
                ["url"] = l.Url,
                ["fileName"] = l.FileName
            });
        }
        File.WriteAllText(_libraryIndexPath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
