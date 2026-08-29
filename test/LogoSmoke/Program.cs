using PcDashApp.Logos;

var baseUrl = args.Length > 0 ? args[0] : "http://192.168.1.5:8015";
var testKey = "smoke-test-t52-" + Guid.NewGuid().ToString("N")[..8];

Console.WriteLine($"Live T52 logo-endpoint smoke test against {baseUrl}, test key '{testKey}'.");
Console.WriteLine("Writes clearly-labeled synthetic data; a real logo image is also uploaded to the shared library.\n");

var failures = 0;
void Check(string label, bool pass)
{
    Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label}");
    if (!pass) failures++;
}

// --- Step A: brand-new key auto-assigns a random generic icon ---
Console.WriteLine("Step A: GetOrAutoAssignAsync on a never-before-seen key (expect 404 -> auto-assign)...");
using var store = new HubLogoAssignmentStore(baseUrl);
var assignedA = await store.GetOrAutoAssignAsync(testKey);
Check("kind == generic", assignedA.Kind == LogoKind.Generic);
Check("ref is a known generic icon id", GenericIconIds.IsKnown(assignedA.Ref));
Check("initialsOverlay == false by default", assignedA.InitialsOverlay == false);
Console.WriteLine($"  assigned: kind={assignedA.Kind} ref={assignedA.Ref}");

var knownKeys = await store.GetKnownProjectIdsAsync();
Check("known-keys (local, non-authoritative) includes the test key", knownKeys.Contains(testKey));

// --- Step B: upload a tiny synthetic PNG to the shared library ---
Console.WriteLine("\nStep B: upload a synthetic 1x1 PNG to /api/logos...");
var tempPngPath = Path.Combine(Path.GetTempPath(), $"smoke-test-t52-logo-{Guid.NewGuid():N}.png");
await File.WriteAllBytesAsync(tempPngPath, OnePixelPngBytes());
LibraryLogo uploaded;
try
{
    uploaded = await store.AddToLibraryAsync(tempPngPath);
    Check("upload returned an id", !string.IsNullOrEmpty(uploaded.Id));
    Console.WriteLine($"  uploaded: id={uploaded.Id} url={uploaded.Url} fileName={uploaded.FileName}");

    var library = await store.GetLibraryAsync();
    Check("uploaded logo appears in GET /api/logos", library.Any(l => l.Id == uploaded.Id));

    // Cache was written synchronously as part of AddToLibraryAsync -- should resolve immediately.
    var cachedPath = store.ResolveLibraryFilePath(uploaded.Id);
    Check("uploaded logo resolves to a local cached file", cachedPath is not null && File.Exists(cachedPath));

    // --- Step C: assign the uploaded logo + initials overlay, confirm round-trip ---
    Console.WriteLine("\nStep C: SetAssignmentAsync to the uploaded logo, initialsOverlay=true...");
    await store.SetAssignmentAsync(testKey, new ProjectLogoAssignment(LogoKind.Upload, uploaded.Id, true));

    // --- Step D: fresh store instance (new HttpClient) confirms it's persisted hub-side, not just locally ---
    Console.WriteLine("\nStep D: re-read via a FRESH store instance (proves hub persistence, not local memory)...");
    using var freshStore = new HubLogoAssignmentStore(baseUrl);
    var reread = await freshStore.GetOrAutoAssignAsync(testKey);
    Check("kind == upload (not re-randomized)", reread.Kind == LogoKind.Upload);
    Check("ref == uploaded logo id", reread.Ref == uploaded.Id);
    Check("initialsOverlay == true", reread.InitialsOverlay == true);

    // Fresh store has an empty local cache -- GetLibraryAsync should kick off a
    // background prefetch; give it a moment then check the download landed.
    _ = await freshStore.GetLibraryAsync();
    await Task.Delay(2000);
    var freshCachedPath = freshStore.ResolveLibraryFilePath(uploaded.Id);
    Check("fresh store background-downloads the library image", freshCachedPath is not null && File.Exists(freshCachedPath));
}
finally
{
    File.Delete(tempPngPath);
}

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

// Smallest possible valid PNG: 1x1 transparent pixel.
static byte[] OnePixelPngBytes() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
