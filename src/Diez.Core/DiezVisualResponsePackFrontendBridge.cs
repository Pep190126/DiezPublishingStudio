using System.IO.Compression;
using System.Security.Cryptography;
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
/// It accepts the canonical Diez response-manifest.json dialect and the provider-produced
/// diez-response.json compatibility dialect observed in physical testing. Field aliases are
/// normalized, but Project/Job/PromptPack/WorkUnit/version identity checks are never relaxed.
/// Image bytes are not loaded by this bridge; the package frontend extracts one accepted asset
/// at a time so large books remain memory-safe.
/// </summary>
public static class DiezVisualResponsePackFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private const string CanonicalManifestName = "response-manifest.json";
    private const string CompatibilityManifestName = "diez-response.json";
    private const string ProviderFailedMarker = "DIEZ_PROVIDER_FAILED_V1:";
    private const long MaxAssetBytes = 150L * 1024L * 1024L;

    private static readonly JsonSerializerOptions ProjectJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
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
            var canonicalEntry = FindEntry(archive, CanonicalManifestName);
            var compatibilityEntry = FindEntry(archive, CompatibilityManifestName);
            var manifestEntry = canonicalEntry ?? compatibilityEntry;
            if (manifestEntry is null)
                return Failure(
                    "MANIFEST_MISSING",
                    $"Il Response ZIP non contiene {CanonicalManifestName} né {CompatibilityManifestName}.");

            var dialect = canonicalEntry is not null ? "canonical" : "provider-compat";
            JsonObject manifestRoot;
            await using (var stream = manifestEntry.Open())
            {
                manifestRoot = await JsonNode.ParseAsync(stream) as JsonObject
                    ?? throw new InvalidDataException("Il manifest Response non contiene un oggetto JSON valido.");
            }

            var manifest = NormalizeManifest(manifestRoot);
            if (!string.Equals(manifest.Protocol, "diez-response", StringComparison.OrdinalIgnoreCase) ||
                manifest.ProtocolVersion != 1)
                return Failure("INVALID_PROTOCOL", "Protocollo Response non valido: atteso diez-response v1.");
            if (manifest.ProjectId == Guid.Empty || manifest.ProjectId != project.ProjectId)
                return Failure("PROJECT_MISMATCH", "Il Response ZIP appartiene a un altro progetto Diez.");
            if (manifest.JobId == Guid.Empty || manifest.PromptPackId == Guid.Empty)
                return Failure("HEADER_INCOMPLETE", "Job o Prompt Pack non identificabili nel Response ZIP.");

            var packageId = manifest.PackageId;
            if (string.IsNullOrWhiteSpace(packageId))
                packageId = await DerivePackageIdAsync(zipPath);
            if (state.ImportedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                return new(false, "PACKAGE_ALREADY_IMPORTED", "Questo Response ZIP è già stato importato.",
                    packageId, manifest.PromptPackId, Guid.Empty, manifest.Partial, []);

            var pack = state.PromptPacks.FirstOrDefault(p => p.PromptPackId == manifest.PromptPackId);
            var snapshot = pack is null
                ? null
                : state.RequestSnapshots.FirstOrDefault(s => s.SnapshotId == pack.SnapshotId);
            if (pack is null || snapshot is null || snapshot.JobId != manifest.JobId)
                return Failure("PROMPT_PACK_MISMATCH", "Prompt Pack, snapshot o Job non corrispondono allo stato del progetto aperto.");

            var result = new List<DiezVisualResponsePackItem>();
            var seen = new HashSet<Guid>();
            foreach (var item in manifest.Items)
            {
                if (item.WorkUnitId == Guid.Empty || !seen.Add(item.WorkUnitId))
                    return Failure("DUPLICATE_WORK_UNIT", "Una Work Unit è mancante o compare più volte nello stesso Response.");

                var unit = state.WorkUnits.FirstOrDefault(w => w.WorkUnitId == item.WorkUnitId);
                var requested = snapshot.Items.FirstOrDefault(x => x.WorkUnitId == item.WorkUnitId);
                if (unit is null || requested is null)
                    return Failure("WORK_UNIT_MISMATCH", "Il Response ZIP contiene una Work Unit non appartenente allo snapshot del Prompt Pack.");
                if (!string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                    return Failure("NON_IMAGE_WORK_UNIT", $"{unit.Code} non è una Work Unit immagine.");
                if (!string.IsNullOrWhiteSpace(item.ContentType) &&
                    !string.Equals(item.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
                    return Failure("CONTENT_TYPE_MISMATCH", $"{unit.Code}: content_type del Response non corrisponde a IMAGE.");
                if (requested.TargetCandidateVersion != item.CandidateVersion)
                    return Failure("CANDIDATE_VERSION_MISMATCH",
                        $"{unit.Code}: Candidate attesa v{requested.TargetCandidateVersion}, ricevuta v{item.CandidateVersion}.");

                var normalizedStatus = NormalizeResultStatus(item.Status);
                if (normalizedStatus.Length == 0)
                    return Failure("RESULT_STATUS_INVALID", $"{unit.Code}: status '{item.Status}' non riconosciuto.");
                var failed = string.Equals(normalizedStatus, "FAILED", StringComparison.Ordinal);
                var entryPath = string.Empty;
                var fileName = string.Empty;
                var length = 0L;
                if (!failed)
                {
                    if (string.IsNullOrWhiteSpace(item.PrimaryAsset))
                        return Failure("ASSET_MISSING", $"{unit.Code}: primary_asset mancante.");
                    var entry = ResolveAsset(archive, item.PrimaryAsset);
                    if (entry is null)
                        return Failure("ASSET_NOT_FOUND", $"{unit.Code}: asset '{item.PrimaryAsset}' non trovato o non sicuro nel Response ZIP.");
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
                    normalizedStatus,
                    item.Description,
                    item.FailureReason,
                    entryPath,
                    fileName,
                    length,
                    item.RenderRequestId,
                    item.RenderPromptSha256));
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
                $"Response ZIP verificato ({dialect}): {result.Count} risultati collegati al Prompt Pack.",
                packageId,
                manifest.PromptPackId,
                snapshot.SnapshotId,
                manifest.Partial,
                result);
        }
        catch (InvalidDataException ex)
        {
            return Failure("INVALID_ZIP", "Response ZIP non valido: " + ex.Message);
        }
        catch (JsonException ex)
        {
            return Failure("INVALID_JSON", "Manifest Response JSON non valido: " + ex.Message);
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

    private static NormalizedManifest NormalizeManifest(JsonObject root)
    {
        var itemsNode = root["items"] as JsonArray ?? root["results"] as JsonArray ?? new JsonArray();
        var items = new List<NormalizedItem>();
        foreach (var node in itemsNode.OfType<JsonObject>())
        {
            items.Add(new NormalizedItem(
                ReadGuid(node, "work_unit_id"),
                ReadInt(node, "candidate_version"),
                ReadString(node, "content_type"),
                ReadString(node, "status"),
                ReadString(node, "primary_asset"),
                ReadString(node, "description"),
                ReadString(node, "render_request_id"),
                ReadString(node, "render_prompt_sha256"),
                ReadString(node, "failure_reason")));
        }

        var promptPackId = ReadGuid(root, "prompt_pack_id");
        if (promptPackId == Guid.Empty) promptPackId = ReadGuid(root, "source_prompt_pack_id");
        return new NormalizedManifest(
            ReadString(root, "protocol"),
            ReadInt(root, "protocol_version"),
            ReadGuid(root, "project_id"),
            ReadGuid(root, "job_id"),
            promptPackId,
            ReadString(root, "package_id"),
            ReadBool(root, "partial"),
            items);
    }

    private static string NormalizeResultStatus(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "" => "COMPLETE",
        "COMPLETE" => "COMPLETE",
        "COMPLETED" => "COMPLETE",
        "SUCCESS" => "COMPLETE",
        "SUCCEEDED" => "COMPLETE",
        "FAILED" => "FAILED",
        "FAIL" => "FAILED",
        _ => string.Empty
    };

    private static async Task<string> DerivePackageIdAsync(string zipPath)
    {
        await using var source = File.OpenRead(zipPath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(source);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
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
                (Normalize(e.FullName).StartsWith("content/", StringComparison.OrdinalIgnoreCase) ||
                 Normalize(e.FullName).StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) &&
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
        if (node is JsonValue value && value.TryGetValue<string>(out var result)) return result ?? string.Empty;
        return node?.ToString() ?? string.Empty;
    }

    private static Guid ReadGuid(JsonObject obj, string name) =>
        Guid.TryParse(ReadString(obj, name), out var value) ? value : Guid.Empty;

    private static int ReadInt(JsonObject obj, string name)
    {
        if (obj[name] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var number)) return number;
            if (value.TryGetValue<long>(out var longNumber)) return checked((int)longNumber);
        }
        return int.TryParse(ReadString(obj, name), out var parsed) ? parsed : 0;
    }

    private static bool ReadBool(JsonObject obj, string name)
    {
        if (obj[name] is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        return bool.TryParse(ReadString(obj, name), out var parsed) && parsed;
    }

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private sealed record NormalizedManifest(
        string Protocol,
        int ProtocolVersion,
        Guid ProjectId,
        Guid JobId,
        Guid PromptPackId,
        string PackageId,
        bool Partial,
        IReadOnlyList<NormalizedItem> Items);

    private sealed record NormalizedItem(
        Guid WorkUnitId,
        int CandidateVersion,
        string ContentType,
        string Status,
        string PrimaryAsset,
        string Description,
        string RenderRequestId,
        string RenderPromptSha256,
        string FailureReason);
}
