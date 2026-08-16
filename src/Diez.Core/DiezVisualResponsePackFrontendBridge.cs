using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezVisualResponsePackItem(
    Guid WorkUnitId,
    string Code,
    int CandidateVersion,
    string Status,
    string Description,
    string FailureReason,
    string AssetEntryPath,
    string AssetFileName,
    long AssetLength,
    string RenderRequestId,
    string RenderPromptSha256);

public sealed record DiezVisualResponsePackReadResult(
    bool Success,
    string Status,
    string Message,
    string PackageId,
    Guid PromptPackId,
    Guid RequestSnapshotId,
    bool Partial,
    IReadOnlyList<DiezVisualResponsePackItem> Items);

public sealed record DiezVisualResponsePackMutation(
    string ProjectJson,
    bool Success,
    string Status,
    string Message);

/// <summary>
/// Audited, UI-neutral boundary for one manual visual Response ZIP.
/// The bridge validates identities and ZIP entry paths but intentionally does not load image bytes:
/// the package frontend extracts one accepted asset at a time, keeping large books memory-safe.
/// </summary>
public static class DiezVisualResponsePackFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private const string ManifestName = "response-manifest.json";
    private const string ProviderFailedMarker = "DIEZ_PROVIDER_FAILED_V1:";
    private const long MaxAssetBytes = 150L * 1024L * 1024L;

    private static readonly JsonSerializerOptions ProjectJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<DiezVisualResponsePackReadResult> ReadAsync(string projectJson, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return Failure("FILE_NOT_FOUND", "Response ZIP non trovato.");

        var (_, project) = Parse(projectJson);
        var state = AiExchangeStateStore.Load(project);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var manifestEntry = FindEntry(archive, ManifestName);
            if (manifestEntry is null)
                return Failure("MANIFEST_MISSING", "Il Response ZIP non contiene response-manifest.json.");

            ResponseManifest? manifest;
            await using (var stream = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<ResponseManifest>(stream, ManifestJsonOptions);

            if (manifest is null ||
                !string.Equals(manifest.Protocol, "diez-response", StringComparison.OrdinalIgnoreCase) ||
                manifest.ProtocolVersion != 1)
                return Failure("INVALID_PROTOCOL", "Protocollo Response non valido: atteso diez-response v1.");
            if (manifest.ProjectId != project.ProjectId)
                return Failure("PROJECT_MISMATCH", "Il Response ZIP appartiene a un altro progetto Diez.");
            if (string.IsNullOrWhiteSpace(manifest.PackageId))
                return Failure("PACKAGE_ID_MISSING", "package_id mancante nel Response ZIP.");
            if (state.ImportedPackageIds.Contains(manifest.PackageId, StringComparer.OrdinalIgnoreCase))
                return new(false, "PACKAGE_ALREADY_IMPORTED", "Questo Response ZIP è già stato importato.",
                    manifest.PackageId, manifest.PromptPackId, Guid.Empty, manifest.Partial, []);

            var pack = state.PromptPacks.FirstOrDefault(p => p.PromptPackId == manifest.PromptPackId);
            var snapshot = pack is null
                ? null
                : state.RequestSnapshots.FirstOrDefault(s => s.SnapshotId == pack.SnapshotId);
            if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)
                return Failure("PROMPT_PACK_MISMATCH", "Prompt Pack, snapshot o Job non corrispondono allo stato del progetto aperto.");

            var result = new List<DiezVisualResponsePackItem>();
            var seen = new HashSet<Guid>();
            foreach (var item in manifest.Items ?? [])
            {
                if (!seen.Add(item.WorkUnitId))
                    return Failure("DUPLICATE_WORK_UNIT", "Una Work Unit compare più volte nello stesso response-manifest.");

                var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == item.WorkUnitId);
                var requested = snapshot.Items.FirstOrDefault(x => x.WorkUnitId == item.WorkUnitId);
                if (unit is null || requested is null)
                    return Failure("WORK_UNIT_MISMATCH", "Il Response ZIP contiene una Work Unit non appartenente allo snapshot del Prompt Pack.");
                if (!string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                    return Failure("NON_IMAGE_WORK_UNIT", $"{unit.Code} non è una Work Unit immagine.");
                if (!string.IsNullOrWhiteSpace(item.ContentType) &&
                    !string.Equals(item.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                    return Failure("CONTENT_TYPE_MISMATCH", $"{unit.Code}: content_type del Response non corrisponde a Image.");
                if (requested.TargetCandidateVersion != item.CandidateVersion)
                    return Failure("CANDIDATE_VERSION_MISMATCH",
                        $"{unit.Code}: Candidate attesa v{requested.TargetCandidateVersion}, ricevuta v{item.CandidateVersion}.");

                var failed = string.Equals(item.Status, "FAILED", StringComparison.OrdinalIgnoreCase);
                var entryPath = string.Empty;
                var fileName = string.Empty;
                var length = 0L;
                if (!failed)
                {
                    if (string.IsNullOrWhiteSpace(item.PrimaryAsset))
                        return Failure("ASSET_MISSING", $"{unit.Code}: primary_asset mancante.");
                    var entry = ResolveAsset(archive, item.PrimaryAsset);
                    if (entry is null)
                        return Failure("ASSET_NOT_FOUND", $"{unit.Code}: asset '{item.PrimaryAsset}' non trovato o ambiguo nel Response ZIP.");
                    if (entry.Length <= 0)
                        return Failure("ASSET_EMPTY", $"{unit.Code}: asset vuoto.");
                    if (entry.Length > MaxAssetBytes)
                        return Failure("ASSET_TOO_LARGE", $"{unit.Code}: asset oltre il limite di sicurezza di 150 MB.");
                    entryPath = Normalize(entry.FullName);
                    fileName = string.IsNullOrWhiteSpace(entry.Name) ? $"{unit.Code}.bin" : entry.Name;
                    length = entry.Length;
                }

                result.Add(new(
                    item.WorkUnitId,
                    unit.Code,
                    item.CandidateVersion,
                    string.IsNullOrWhiteSpace(item.Status) ? "COMPLETE" : item.Status,
                    item.Description ?? string.Empty,
                    item.FailureReason ?? string.Empty,
                    entryPath,
                    fileName,
                    length,
                    item.RenderRequestId ?? string.Empty,
                    item.RenderPromptSha256 ?? string.Empty));
            }

            if (result.Count == 0)
                return Failure("EMPTY_RESPONSE", "Il Response ZIP non contiene risultati visuali.");

            if (!manifest.Partial)
            {
                var returned = result.Select(x => x.WorkUnitId).ToHashSet();
                var missing = snapshot.Items.Where(x => !returned.Contains(x.WorkUnitId)).ToList();
                if (missing.Count > 0)
                {
                    var codes = missing.Select(x =>
                        state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == x.WorkUnitId)?.Code ?? x.WorkUnitId.ToString("D"));
                    return Failure("INCOMPLETE_RESPONSE", "Il Response è dichiarato completo ma mancano: " + string.Join(", ", codes) + ".");
                }
            }

            return new(
                true,
                "READY",
                $"Response ZIP verificato: {result.Count} risultati collegati al Prompt Pack.",
                manifest.PackageId,
                manifest.PromptPackId,
                snapshot.SnapshotId,
                manifest.Partial,
                result);
        }
        catch (InvalidDataException ex)
        {
            return Failure("INVALID_ZIP", "Response ZIP non valido: " + ex.Message);
        }
        catch (Exception ex)
        {
            return Failure("READ_FAILED", "Lettura Response ZIP non riuscita: " + ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Persists a provider-declared FAILED attempt in the same central AiExchangeVersion stream used
    /// by the historical desktop reader. It creates no Material and cannot be approved by Vision.
    /// </summary>
    public static DiezVisualResponsePackMutation RecordProviderFailure(
        string projectJson,
        string packageId,
        Guid promptPackId,
        Guid requestSnapshotId,
        DiezVisualResponsePackItem item)
    {
        var (root, project) = Parse(projectJson);
        var state = AiExchangeStateStore.Load(project);
        var unit = state.WorkUnits.FirstOrDefault(x => x.WorkUnitId == item.WorkUnitId);
        if (unit is null)
            return new(projectJson, false, "WORK_UNIT_MISSING", "Work Unit non trovata per il FAILED provider.");

        var version = state.Versions.FirstOrDefault(v =>
            v.WorkUnitId == item.WorkUnitId && v.VersionNumber == item.CandidateVersion);
        if (version?.MaterialId.HasValue == true)
            return new(projectJson, false, "VERSION_HAS_ASSET", $"{unit.Code}: la versione possiede già un asset reale e non viene sovrascritta da FAILED.");

        version ??= new AiExchangeVersion
        {
            VersionId = Guid.NewGuid(),
            WorkUnitId = item.WorkUnitId,
            VersionNumber = item.CandidateVersion,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        if (!state.Versions.Contains(version)) state.Versions.Add(version);

        var importedAt = DateTimeOffset.Now.ToString("O");
        var audit = new
        {
            PackageId = packageId,
            PromptPackId = promptPackId,
            WorkUnitId = item.WorkUnitId,
            CandidateVersion = item.CandidateVersion,
            Status = "FAILED",
            Description = (item.Description ?? string.Empty).Trim(),
            FailureReason = (item.FailureReason ?? string.Empty).Trim(),
            RenderRequestId = (item.RenderRequestId ?? string.Empty).Trim(),
            RenderPromptSha256 = (item.RenderPromptSha256 ?? string.Empty).Trim().ToLowerInvariant(),
            ImportedAtLocal = importedAt
        };

        version.Status = AiExchangeVersionStatuses.Incomplete;
        version.Origin = AiExchangeOrigins.AiPromptPack;
        version.MaterialId = null;
        version.TextContent = ProviderFailedMarker + JsonSerializer.Serialize(audit);
        version.Description = audit.Description;
        version.DescriptionStatus = AiExchangeDescriptionStatuses.NeedsVerification;
        version.ContentSha256 = string.Empty;
        version.SourceSnapshotId = requestSnapshotId;
        if (string.IsNullOrWhiteSpace(version.CreatedAtLocal)) version.CreatedAtLocal = importedAt;
        if (!unit.CandidateVersionIds.Contains(version.VersionId)) unit.CandidateVersionIds.Add(version.VersionId);

        AiExchangeStateStore.Save(project, state);
        MergeExchangeEntity(root, project);
        return new(Write(root), true, "FAILED_RECORDED", $"{unit.Code}: FAILED provider registrato come tentativo incompleto.");
    }

    public static DiezVisualResponsePackMutation MarkPackageImported(string projectJson, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return new(projectJson, false, "PACKAGE_ID_MISSING", "package_id mancante.");

        var (root, project) = Parse(projectJson);
        var state = AiExchangeStateStore.Load(project);
        if (!state.ImportedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            state.ImportedPackageIds.Add(packageId);
        AiExchangeStateStore.Save(project, state);
        MergeExchangeEntity(root, project);
        return new(Write(root), true, "RECORDED", "Response Package registrato come importato.");
    }

    private static DiezVisualResponsePackReadResult Failure(string status, string message) =>
        new(false, status, message, string.Empty, Guid.Empty, Guid.Empty, false, []);

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = Normalize(path);
        return archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.Ordinal))
               ?? archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static ZipArchiveEntry? ResolveAsset(ZipArchive archive, string requested)
    {
        string normalized;
        try { normalized = Normalize(Uri.UnescapeDataString(requested)); }
        catch { normalized = Normalize(requested); }
        if (!IsSafe(normalized)) return null;

        var exact = archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.Ordinal))
                    ?? archive.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null && IsSafe(Normalize(exact.FullName))) return exact;

        var name = Path.GetFileName(normalized.Replace('/', Path.DirectorySeparatorChar));
        var byName = archive.Entries.Where(e =>
                Normalize(e.FullName).StartsWith("content/", StringComparison.OrdinalIgnoreCase) &&
                IsSafe(Normalize(e.FullName)) &&
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim().TrimStart('/');

    private static bool IsSafe(string normalized) =>
        !string.IsNullOrWhiteSpace(normalized) &&
        !normalized.StartsWith("..", StringComparison.Ordinal) &&
        !normalized.Contains("../", StringComparison.Ordinal) &&
        !Path.IsPathRooted(normalized.Replace('/', Path.DirectorySeparatorChar));

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, ProjectJsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeExchangeEntity(JsonObject root, PreviewProject project)
    {
        var typed = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, ExchangeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (typed is null) return;
        var entities = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = entities;
        var raw = entities.OfType<JsonObject>().FirstOrDefault(e =>
            string.Equals(ReadString(e, "Kind"), ExchangeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (raw is null)
        {
            raw = new JsonObject();
            entities.Add(raw);
        }
        raw["EntityId"] = typed.EntityId.ToString();
        raw["Kind"] = typed.Kind;
        raw["Name"] = typed.Name;
        raw["IsCandidate"] = typed.IsCandidate;
        raw["Notes"] = typed.Notes;
        if (typed.SourceMaterialId.HasValue) raw["SourceMaterialId"] = typed.SourceMaterialId.Value.ToString();
        if (typed.FirstSourceContentId.HasValue) raw["FirstSourceContentId"] = typed.FirstSourceContentId.Value.ToString();
    }

    private static string ReadString(JsonObject obj, string name)
    {
        var node = obj[name];
        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result ?? string.Empty
            : string.Empty;
    }

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private sealed class ResponseManifest
    {
        public string Protocol { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public Guid ProjectId { get; set; }
        public Guid JobId { get; set; }
        public Guid PromptPackId { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public bool Partial { get; set; }
        public List<ResponseItem> Items { get; set; } = [];
    }

    private sealed class ResponseItem
    {
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Status { get; set; } = "COMPLETE";
        public string PrimaryAsset { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RenderRequestId { get; set; } = string.Empty;
        public string RenderPromptSha256 { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
    }
}
