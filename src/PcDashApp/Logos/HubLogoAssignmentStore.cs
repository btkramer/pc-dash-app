using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PcDashApp.Logos;

/// <summary>
/// Hub-backed implementation of <see cref="ILogoAssignmentStore"/>, built against
/// pc-dash-hub's real implementation of LOGO_SCHEMA.md v2 (contract details
/// confirmed via the coordinator 2026-08-27 -- multipart field name `file`, 404
/// with a detail body for an unset key, Content-Type stored verbatim from upload).
/// Not yet wired into MainWindow -- held until pc-dash-hub's rebuild/redeploy is
/// confirmed live, per this project's "always verify before calling it ready" norm.
///
/// Two things this store does NOT get from the hub, both deliberate:
/// - <see cref="GetKnownProjectIdsAsync"/>: there is no "list all assignment keys"
///   endpoint (agent-monitor mode's own project tracking, not yet built, is meant
///   to be the real source of that list). This store instead keeps a small local
///   "keys this instance has seen" file purely so ProjectLogosWindow's demo list
///   has something to show -- it is NOT authoritative, just UI convenience, and
///   is expected to be dropped once a real project list exists to iterate instead.
/// - Uploaded-image bytes for rendering: <see cref="ResolveLibraryFilePath"/> is a
///   synchronous, interface-mandated call (LogoRenderer draws synchronously), so
///   this store maintains its own local disk cache under a `cache/` subfolder --
///   populated eagerly on upload (immediate local render, no round-trip needed)
///   and lazily in the background whenever <see cref="GetLibraryAsync"/> notices a
///   library entry this instance hasn't cached yet. A cache miss on first render
///   (background download still in flight) falls back to a generic icon via
///   LogoRenderer, same graceful-degradation path an unknown icon id already uses.
/// </summary>
public sealed class HubLogoAssignmentStore : ILogoAssignmentStore
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _knownKeysPath;
    private readonly SemaphoreSlim _knownKeysLock = new(1, 1);
    private readonly HashSet<string> _downloadsInFlight = [];

    public HubLogoAssignmentStore(string baseUrl, string? stateDir = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.BaseAddress = new Uri(baseUrl);

        var root = stateDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "pc-dash-app", "logos");
        _cacheDir = Path.Combine(root, "cache");
        Directory.CreateDirectory(_cacheDir);
        _knownKeysPath = Path.Combine(root, "known-keys.json");
    }

    public async Task<ProjectLogoAssignment> GetOrAutoAssignAsync(string key)
    {
        await RememberKeyAsync(key);

        using var getResponse = await _http.GetAsync($"/api/logo-assignments/{Uri.EscapeDataString(key)}");
        if (getResponse.StatusCode != HttpStatusCode.NotFound)
        {
            getResponse.EnsureSuccessStatusCode();
            var json = await getResponse.Content.ReadAsStringAsync();
            var parsed = ParseAssignment(json);
            if (parsed is not null)
                return parsed;
            // Fall through to auto-assign if the body didn't parse as expected --
            // treated the same as "no assignment yet" rather than throwing on a
            // shape this app doesn't recognize.
        }

        var (iconId, _) = GenericIconIds.All[Random.Shared.Next(GenericIconIds.All.Count)];
        var assigned = new ProjectLogoAssignment(LogoKind.Generic, iconId, InitialsOverlay: false);
        await SetAssignmentAsync(key, assigned);
        return assigned;
    }

    public async Task SetAssignmentAsync(string key, ProjectLogoAssignment assignment)
    {
        await RememberKeyAsync(key);

        var body = new JsonObject
        {
            ["kind"] = assignment.Kind,
            ["ref"] = assignment.Ref,
            ["initialsOverlay"] = assignment.InitialsOverlay
        };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PutAsync($"/api/logo-assignments/{Uri.EscapeDataString(key)}", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetKnownProjectIdsAsync()
    {
        await _knownKeysLock.WaitAsync();
        try
        {
            return LoadKnownKeys().OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            _knownKeysLock.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryLogo>> GetLibraryAsync()
    {
        using var response = await _http.GetAsync("/api/logos");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var array = JsonNode.Parse(json)?.AsArray() ?? [];

        var library = new List<LibraryLogo>();
        foreach (var item in array)
        {
            var id = item?["id"]?.GetValue<string>();
            var url = item?["url"]?.GetValue<string>();
            // fileName can legitimately be null (entries uploaded before pc-dash-hub
            // started capturing it, confirmed live 2026-08-28) -- id+url alone is
            // enough for this app to show and cache the entry, so only those two
            // are required. An earlier version of this code wrongly treated a null
            // fileName as a malformed entry and silently dropped it from the list.
            var fileName = item?["fileName"]?.GetValue<string>();
            var contentType = item?["contentType"]?.GetValue<string>();
            if (id is null || url is null)
                continue;
            library.Add(new LibraryLogo(id, url, fileName));

            // Best-effort background prefetch -- don't block the picker's library
            // list on every image downloading first. Errors are swallowed here;
            // ResolveLibraryFilePath just stays a cache miss (-> generic-icon
            // fallback) until a later call succeeds.
            _ = PrefetchAsync(id, fileName, contentType);
        }
        return library;
    }

    public async Task<LibraryLogo> AddToLibraryAsync(string sourceFilePath)
    {
        var bytes = await File.ReadAllBytesAsync(sourceFilePath);
        var fileName = Path.GetFileName(sourceFilePath);

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(fileName));
        form.Add(fileContent, "file", fileName); // field name "file" per pc-dash-hub's upload_logo(file: UploadFile)

        using var response = await _http.PostAsync("/api/logos", form);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        var id = node?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Upload response missing id");
        var url = node?["url"]?.GetValue<string>() ?? $"/api/logos/{id}";
        var returnedFileName = node?["fileName"]?.GetValue<string>() ?? fileName;

        // Cache immediately with the bytes already in hand -- the picker previews
        // the just-uploaded image right away, no round-trip download needed.
        await File.WriteAllBytesAsync(CachePathFor(id, returnedFileName, GuessMimeType(fileName)), bytes);

        return new LibraryLogo(id, url, returnedFileName);
    }

    public string? ResolveLibraryFilePath(string libraryLogoId)
    {
        // We don't know the original extension without an index lookup, so scan
        // for any cached file whose name starts with the id -- CachePathFor always
        // writes exactly one file per id.
        var match = Directory.EnumerateFiles(_cacheDir, $"{libraryLogoId}.*").FirstOrDefault();
        return match;
    }

    public void Dispose()
    {
        _http.Dispose();
        _knownKeysLock.Dispose();
    }

    private async Task PrefetchAsync(string id, string? fileName, string? contentType)
    {
        // Cache path can't be pinned down without knowing the extension, and a
        // pre-fix entry can have neither a fileName nor (from GET /api/logos'
        // list view) a contentType we've already seen -- ResolveLibraryFilePath
        // scans by id prefix regardless of extension, so a wrong/generic guess
        // here still gets found again later, it just risks a duplicate cached
        // file under a different extension if the guess changes between calls.
        // Good enough for a best-effort cache; not worth a HEAD request first.
        if (Directory.EnumerateFiles(_cacheDir, $"{id}.*").Any())
            return; // already cached under some extension

        lock (_downloadsInFlight)
        {
            if (!_downloadsInFlight.Add(id))
                return; // already downloading
        }

        try
        {
            using var response = await _http.GetAsync($"/api/logos/{Uri.EscapeDataString(id)}");
            if (!response.IsSuccessStatusCode)
                return;
            // The single-item GET reliably has a real Content-Type (confirmed live,
            // see LOGO_SCHEMA.md) even when the list view's entry didn't -- prefer
            // it over the caller-supplied contentType/fileName guesses.
            var actualContentType = response.Content.Headers.ContentType?.MediaType ?? contentType;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var destPath = CachePathFor(id, fileName, actualContentType);
            await File.WriteAllBytesAsync(destPath, bytes);
        }
        catch (Exception)
        {
            // Transient network issue -- leave as a cache miss, LogoRenderer falls
            // back to a generic icon. A later GetLibraryAsync call retries.
        }
        finally
        {
            lock (_downloadsInFlight)
                _downloadsInFlight.Remove(id);
        }
    }

    /// <summary>Picks a cache file extension: the original filename's, if it has
    /// one; otherwise derived from Content-Type; otherwise a generic fallback.
    /// Never fails outright -- an unrecognized/missing hint still produces a
    /// loadable-enough file, since WPF's BitmapImage largely sniffs real image
    /// formats by content rather than trusting the extension.</summary>
    private string CachePathFor(string id, string? originalFileName, string? contentType)
    {
        var ext = string.IsNullOrEmpty(originalFileName) ? "" : Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext))
            ext = ExtensionFromContentType(contentType);
        return Path.Combine(_cacheDir, id + ext);
    }

    private static string ExtensionFromContentType(string? contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => ".img" // unknown/missing -- still a valid, findable filename
    };

    private static ProjectLogoAssignment? ParseAssignment(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var kind = node?["kind"]?.GetValue<string>();
            var reference = node?["ref"]?.GetValue<string>();
            var initials = node?["initialsOverlay"]?.GetValue<bool>() ?? false;
            return kind is null || reference is null ? null : new ProjectLogoAssignment(kind, reference, initials);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GuessMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private async Task RememberKeyAsync(string key)
    {
        await _knownKeysLock.WaitAsync();
        try
        {
            var keys = LoadKnownKeys();
            if (keys.Add(key))
                SaveKnownKeys(keys);
        }
        finally
        {
            _knownKeysLock.Release();
        }
    }

    private HashSet<string> LoadKnownKeys()
    {
        if (!File.Exists(_knownKeysPath))
            return [];
        try
        {
            var json = File.ReadAllText(_knownKeysPath);
            var array = JsonNode.Parse(json)?.AsArray();
            return array is null
                ? []
                : array.Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveKnownKeys(HashSet<string> keys)
    {
        var array = new JsonArray(keys.Select(k => JsonValue.Create(k) as JsonNode).ToArray());
        File.WriteAllText(_knownKeysPath, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
