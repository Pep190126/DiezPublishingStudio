using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

/// <summary>
/// Accepts either ordinary Diez Response ZIPs or one outer Response Bundle ZIP containing N partial
/// Response ZIPs under parts/. Inner packages remain independently audited by AiExchangeResponseImportV2.
/// </summary>
internal static class AiExchangeResponseBundleService
{
    public const string Protocol = "diez-response-bundle";
    public const int ProtocolVersion = 1;
    public const string ManifestFileName = "response-bundle-manifest.json";
    public const string PartsDirectory = "parts/";
    private const int MaxParts = 1000;
    private const long MaxInnerZipBytes = 512L * 1024 * 1024;

    public static async Task<AiExchangeImportV2Report> ImportAsync(
        PreviewProject project,
        string projectPath,
        AiExchangeState state,
        IEnumerable<string> zipPaths)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezResponseBundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var expanded = new List<string>();
        var notes = new List<string>();

        try
        {
            foreach (var path in zipPaths.Where(File.Exists))
            {
                if (!TryExpandBundle(project, path, tempRoot, expanded, notes))
                    expanded.Add(path);
            }

            var report = await AiExchangeResponseImportV2.ImportAsync(project, projectPath, state, expanded);
            if (notes.Count > 0)
                report.Details.InsertRange(0, notes);
            return report;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    internal static bool TryExpandBundle(
        PreviewProject project,
        string outerZipPath,
        string tempRoot,
        ICollection<string> expanded,
        ICollection<string> notes)
    {
        var label = Path.GetFileName(outerZipPath);
        try
        {
            using var archive = ZipFile.OpenRead(outerZipPath);
            if (FindEntry(archive, "response-manifest.json") is not null)
                return false; // ordinary Response ZIP: let V2 importer handle it directly.

            var manifestEntry = FindEntry(archive, ManifestFileName);
            if (manifestEntry is null)
                return false;

            BundleManifest? manifest;
            using (var stream = manifestEntry.Open())
                manifest = JsonSerializer.Deserialize<BundleManifest>(stream, JsonOptions);

            if (manifest is null ||
                !string.Equals(manifest.Protocol, Protocol, StringComparison.OrdinalIgnoreCase) ||
                manifest.ProtocolVersion != ProtocolVersion)
            {
                notes.Add($"{label}: Response Bundle riconosciuto ma manifest bundle non valido; verrà trattato come package non importabile.");
                return false;
            }
            if (manifest.ProjectId != Guid.Empty && manifest.ProjectId != project.ProjectId)
            {
                notes.Add($"{label}: Response Bundle appartiene a un project_id diverso dal progetto aperto.");
                return false;
            }

            var declared = (manifest.Parts ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.FileName))
                .Select(p => Normalize(p.FileName!))
                .ToList();
            if (declared.Count == 0)
            {
                notes.Add($"{label}: Response Bundle senza parti dichiarate.");
                return false;
            }
            if (declared.Count > MaxParts)
            {
                notes.Add($"{label}: Response Bundle supera il limite di {MaxParts} parti.");
                return false;
            }
            if (manifest.ExpectedParts > 0 && manifest.ExpectedParts != declared.Count)
            {
                notes.Add($"{label}: expected_parts={manifest.ExpectedParts}, ma il manifest dichiara {declared.Count} parti.");
                return false;
            }
            if (declared.Distinct(StringComparer.OrdinalIgnoreCase).Count() != declared.Count)
            {
                notes.Add($"{label}: Response Bundle contiene nomi parte duplicati.");
                return false;
            }

            var extracted = new List<string>();
            foreach (var declaredPath in declared)
            {
                if (!IsSafePartPath(declaredPath))
                {
                    notes.Add($"{label}: path parte non sicuro/non valido: '{declaredPath}'.");
                    return false;
                }
                var entry = FindEntry(archive, declaredPath);
                if (entry is null)
                {
                    notes.Add($"{label}: parte dichiarata realmente assente: '{declaredPath}'.");
                    return false;
                }
                if (entry.Length <= 0 || entry.Length > MaxInnerZipBytes)
                {
                    notes.Add($"{label}: parte '{declaredPath}' ha dimensione non ammessa ({entry.Length} byte).");
                    return false;
                }

                var target = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(declaredPath));
                using var source = entry.Open();
                using var destination = File.Create(target);
                source.CopyTo(destination);
                extracted.Add(target);
            }

            foreach (var path in extracted)
                expanded.Add(path);
            notes.Add($"{label}: Response Bundle {ProtocolVersion} aperto · {extracted.Count} Response parziali interni inoltrati all'importer auditato.");
            return true;
        }
        catch (InvalidDataException ex)
        {
            notes.Add($"{label}: Response Bundle/ZIP non valido: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            notes.Add($"{label}: impossibile espandere il Response Bundle: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = Normalize(path);
        return archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.Ordinal))
               ?? archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static bool IsSafePartPath(string path) =>
        path.StartsWith(PartsDirectory, StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("../", StringComparison.Ordinal) &&
        !path.Contains("..\\", StringComparison.Ordinal) &&
        !Path.IsPathRooted(path.Replace('/', Path.DirectorySeparatorChar));

    private sealed class BundleManifest
    {
        public string Protocol { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public Guid ProjectId { get; set; }
        public Guid PromptPackId { get; set; }
        public string BundleId { get; set; } = string.Empty;
        public int ExpectedParts { get; set; }
        public List<BundlePart> Parts { get; set; } = [];
    }

    private sealed class BundlePart
    {
        public int Order { get; set; }
        public Guid WorkUnitId { get; set; }
        public string? FileName { get; set; }
    }
}
