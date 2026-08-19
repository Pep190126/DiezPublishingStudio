using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezPromptPackBuildResult(
    string ProjectJson,
    bool Success,
    string Status,
    string Message,
    Guid PromptPackId,
    Guid RequestSnapshotId,
    int WorkUnitCount,
    string OutputPath,
    string Transport);

public sealed record DiezPromptPackItemDto(
    Guid WorkUnitId,
    string Code,
    string ContentType,
    string Prompt,
    int CandidateVersion);

/// <summary>
/// Public, UI-neutral Prompt Pack boundary for Uno and future frontends.
///
/// The visual/book-family frontend is responsible for preparing canonical AI Exchange Work Units;
/// this bridge freezes those Work Units into a request snapshot and writes the real transport ZIP.
/// It intentionally mutates only the DiezAiExchangeState entity in the supplied JSON so unknown
/// project fields survive the migration from older/newer .diez schemas.
/// </summary>
public static class DiezPromptPackFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private const string ManifestName = "prompt-manifest.json";
    private const string InstructionsName = "instructions.md";

    private sealed record PublisherMaterialTransport(
        Guid MaterialId,
        string FileName,
        string Kind,
        string IntentCode,
        string IntentLabel,
        string Instruction,
        string AiUsePolicy,
        string Fidelity,
        string Scope,
        string InputPath);

    private static readonly JsonSerializerOptions ProjectJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static IReadOnlyList<DiezPromptPackItemDto> Preview(string projectJson, IEnumerable<Guid>? workUnitIds = null)
    {
        var (_, project) = Parse(projectJson);
        var state = AiExchangeStateStore.Load(project);
        var units = SelectUnits(state, workUnitIds);
        return units.Select(unit => new DiezPromptPackItemDto(
                unit.WorkUnitId,
                unit.Code,
                unit.ContentType,
                unit.Instruction,
                AiExchangeStateStore.NextVersionNumber(state, unit.WorkUnitId)))
            .ToList();
    }

    /// <summary>
    /// Creates the real Prompt Pack used by the manual AI route. Internal routing identifiers live
    /// in prompt-manifest.json only; the provider-facing instruction remains exactly the prepared
    /// Work Unit prompt and is never enriched with Job/WorkUnit/session metadata.
    /// Publisher materials are transported only when their structured intent explicitly allows AI use.
    /// </summary>
    public static async Task<DiezPromptPackBuildResult> BuildManualAsync(
        string projectJson,
        string? projectPackagePath,
        IEnumerable<Guid>? workUnitIds,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return Failure(projectJson, "INVALID_PATH", "Scegli dove salvare il Prompt Pack ZIP.");

        var (root, project) = Parse(projectJson);
        var state = AiExchangeStateStore.Load(project);
        var units = SelectUnits(state, workUnitIds);
        if (units.Count == 0)
            return Failure(projectJson, "NO_WORK_UNITS", "Non ci sono attività AI pronte da inserire nel Prompt Pack.");

        var jobIds = units.Select(x => x.JobId).Distinct().ToList();
        if (jobIds.Count != 1)
            return Failure(projectJson, "MULTIPLE_JOBS", "Un Prompt Pack deve contenere Work Unit appartenenti allo stesso Job Diez.");

        var publisherMaterials = SelectPublisherMaterials(root, project);
        var promptPackId = Guid.NewGuid();
        var snapshot = BuildSnapshot(state, units, promptPackId, jobIds[0], "PROMPT_PACK_MANUAL");
        var fullPath = EnsureZip(outputPath);
        var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            await using (var stream = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteTextAsync(zip, ManifestName,
                    JsonSerializer.Serialize(BuildManifest(project, state, units, snapshot, publisherMaterials), ManifestJsonOptions));
                await WriteTextAsync(zip, InstructionsName, ManualInstructions(publisherMaterials.Count));

                foreach (var paradigm in state.Paradigms.Where(p => units.Any(u => u.ParadigmIds.Contains(p.ParadigmId))))
                {
                    var material = project.Materials.FirstOrDefault(m => m.MaterialId == paradigm.MaterialId);
                    if (material is null) continue;
                    var bytes = await ReadMaterialBytesAsync(projectPackagePath, material);
                    if (bytes is null) continue;
                    await WriteBytesAsync(zip,
                        $"inputs/paradigms/{paradigm.ParadigmId:D}/{SafeName(material.FileName)}",
                        bytes);
                }

                foreach (var item in snapshot.Items.Where(i => i.BaseVersionId.HasValue))
                {
                    var version = state.Versions.FirstOrDefault(v => v.VersionId == item.BaseVersionId);
                    if (version?.MaterialId is not Guid materialId) continue;
                    var material = project.Materials.FirstOrDefault(m => m.MaterialId == materialId);
                    if (material is null) continue;
                    var bytes = await ReadMaterialBytesAsync(projectPackagePath, material);
                    if (bytes is null) continue;
                    await WriteBytesAsync(zip,
                        $"inputs/current/{item.WorkUnitId:D}/{SafeName(material.FileName)}",
                        bytes);
                }

                foreach (var publisherMaterial in publisherMaterials)
                {
                    var material = project.Materials.FirstOrDefault(m => m.MaterialId == publisherMaterial.MaterialId);
                    if (material is null) continue;
                    var bytes = await ReadMaterialBytesAsync(projectPackagePath, material);
                    if (bytes is null) continue;
                    await WriteBytesAsync(zip, publisherMaterial.InputPath, bytes);
                }
            }

            File.Move(tempPath, fullPath, true);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return Failure(projectJson, "WRITE_FAILED", "Creazione Prompt Pack non riuscita: " + ex.GetBaseException().Message);
        }

        state.RequestSnapshots.Add(snapshot);
        state.PromptPacks.Add(new AiExchangePromptPackRecord
        {
            PromptPackId = promptPackId,
            JobId = snapshot.JobId,
            SnapshotId = snapshot.SnapshotId,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        });
        AiExchangeStateStore.Save(project, state);
        MergeExchangeEntity(root, project);

        return new DiezPromptPackBuildResult(
            Write(root),
            true,
            "CREATED",
            $"Prompt Pack creato: {units.Count} Work Unit · {publisherMaterials.Count} materiali publisher inviabili · {Path.GetFileName(fullPath)}.",
            promptPackId,
            snapshot.SnapshotId,
            units.Count,
            fullPath,
            "MANUAL");
    }

    private static AiExchangeRequestSnapshot BuildSnapshot(
        AiExchangeState state,
        IReadOnlyList<AiExchangeWorkUnit> units,
        Guid promptPackId,
        Guid jobId,
        string transport)
    {
        var snapshot = new AiExchangeRequestSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            JobId = jobId,
            PromptPackId = promptPackId,
            Transport = transport,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };

        foreach (var unit in units)
        {
            var baseVersion = ResolveBaseVersion(state, unit);
            snapshot.Items.Add(new AiExchangeSnapshotItem
            {
                WorkUnitId = unit.WorkUnitId,
                TargetCandidateVersion = AiExchangeStateStore.NextVersionNumber(state, unit.WorkUnitId),
                BaseVersionId = baseVersion?.VersionId,
                ParadigmIds = unit.ParadigmIds.ToList(),
                SharedContexts = unit.SharedContextIds
                    .Select(id => state.SharedContexts.FirstOrDefault(c => c.SharedContextId == id))
                    .Where(c => c is not null)
                    .Select(c => new AiExchangeContextRef
                    {
                        SharedContextId = c!.SharedContextId,
                        Version = c.Version
                    })
                    .ToList()
            });
        }

        return snapshot;
    }

    private static object BuildManifest(
        PreviewProject project,
        AiExchangeState state,
        IReadOnlyList<AiExchangeWorkUnit> units,
        AiExchangeRequestSnapshot snapshot,
        IReadOnlyList<PublisherMaterialTransport> publisherMaterials)
    {
        return new
        {
            protocol = "diez-prompt-pack",
            protocol_version = 1,
            transport = "MANUAL",
            project_id = project.ProjectId,
            book_type = BookTypeProfileService.Get(project),
            job_id = snapshot.JobId,
            prompt_pack_id = snapshot.PromptPackId,
            request_snapshot_id = snapshot.SnapshotId,
            partial_results_allowed = true,
            publisher_materials = publisherMaterials.Select(material => new
            {
                material_id = material.MaterialId,
                file_name = material.FileName,
                kind = material.Kind,
                intent_code = material.IntentCode,
                intent_label = material.IntentLabel,
                instruction = material.Instruction,
                ai_use_policy = material.AiUsePolicy,
                fidelity = material.Fidelity,
                scope = material.Scope,
                file = material.InputPath
            }),
            shared_contexts = state.SharedContexts
                .Where(c => units.Any(u => u.SharedContextIds.Contains(c.SharedContextId)))
                .Select(c => new
                {
                    id = c.SharedContextId,
                    c.Version,
                    c.Scope,
                    consistent = c.ConsistentEnabled,
                    rules = c.Rules.Select(r => new { r.Key, r.Level, r.Value })
                }),
            paradigms = state.Paradigms
                .Where(p => units.Any(u => u.ParadigmIds.Contains(p.ParadigmId)))
                .Select(p => new
                {
                    id = p.ParadigmId,
                    p.Scope,
                    roles = p.Roles,
                    p.Description,
                    file = $"inputs/paradigms/{p.ParadigmId:D}/"
                }),
            work_units = units.Select(unit =>
            {
                var snap = snapshot.Items.Single(i => i.WorkUnitId == unit.WorkUnitId);
                var baseVersion = snap.BaseVersionId.HasValue
                    ? state.Versions.FirstOrDefault(v => v.VersionId == snap.BaseVersionId.Value)
                    : null;
                return new
                {
                    id = unit.WorkUnitId,
                    code = unit.Code,
                    kind = unit.Kind,
                    content_type = unit.ContentType,
                    mode = unit.Mode,
                    instruction = unit.Instruction,
                    position = unit.Position,
                    target_candidate_version = snap.TargetCandidateVersion,
                    base_version = baseVersion is null ? null : new
                    {
                        version_id = baseVersion.VersionId,
                        version_number = baseVersion.VersionNumber,
                        file = baseVersion.MaterialId.HasValue ? $"inputs/current/{unit.WorkUnitId:D}/" : null
                    },
                    paradigm_ids = unit.ParadigmIds,
                    shared_context_ids = unit.SharedContextIds,
                    preserve = unit.Preserve,
                    change = unit.Change,
                    add = unit.Add,
                    remove = unit.Remove,
                    expected_output = new
                    {
                        primary_asset = true,
                        description = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                    }
                };
            })
        };
    }

    private static IReadOnlyList<PublisherMaterialTransport> SelectPublisherMaterials(JsonObject root, PreviewProject project)
    {
        if (root["Materials"] is not JsonArray rawMaterials) return [];
        var selected = new List<PublisherMaterialTransport>();
        foreach (var raw in rawMaterials.OfType<JsonObject>())
        {
            var materialId = ReadGuid(raw, "MaterialId");
            if (!materialId.HasValue || materialId.Value == Guid.Empty) continue;
            var typed = project.Materials.FirstOrDefault(m => m.MaterialId == materialId.Value);
            if (typed is null || raw["PublisherIntent"] is not JsonObject intent) continue;

            var intentCode = ReadString(intent, "IntentCode");
            var policy = ReadString(intent, "AiUsePolicy");
            if (string.IsNullOrWhiteSpace(intentCode) ||
                string.Equals(intentCode, "UNASSIGNED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(policy, "NEVER_SEND", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(policy, "DIRECT_ASSET", StringComparison.OrdinalIgnoreCase))
                continue;

            var inputPath = $"inputs/publisher/{materialId.Value:D}/{SafeName(typed.FileName)}";
            selected.Add(new PublisherMaterialTransport(
                materialId.Value,
                typed.FileName,
                ReadString(raw, "Kind"),
                intentCode,
                ReadString(intent, "IntentLabel"),
                ReadString(intent, "Instruction"),
                string.IsNullOrWhiteSpace(policy) ? "REFERENCE_ONLY" : policy,
                ReadString(intent, "Fidelity"),
                ReadString(intent, "Scope"),
                inputPath));
        }
        return selected;
    }

    private static IReadOnlyList<AiExchangeWorkUnit> SelectUnits(AiExchangeState state, IEnumerable<Guid>? requested)
    {
        var ids = requested?.Where(id => id != Guid.Empty).Distinct().ToHashSet();
        var query = state.WorkUnits.AsEnumerable();
        if (ids is { Count: > 0 }) query = query.Where(u => ids.Contains(u.WorkUnitId));
        return query
            .OrderBy(u => u.Position)
            .ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AiExchangeVersion? ResolveBaseVersion(AiExchangeState state, AiExchangeWorkUnit unit)
    {
        if (unit.ApprovedVersionId.HasValue)
            return state.Versions.FirstOrDefault(v => v.VersionId == unit.ApprovedVersionId.Value);
        return state.Versions
            .Where(v => v.WorkUnitId == unit.WorkUnitId &&
                        !string.Equals(v.Status, AiExchangeVersionStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
    }

    private static async Task<byte[]?> ReadMaterialBytesAsync(string? packagePath, MaterialEntry material)
    {
        if (!string.IsNullOrWhiteSpace(packagePath) && File.Exists(packagePath) &&
            ProjectFileStore.IsPackageFile(packagePath) && !string.IsNullOrWhiteSpace(material.EmbeddedPath))
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entry = archive.GetEntry(material.EmbeddedPath);
            if (entry is not null)
            {
                await using var source = entry.Open();
                await using var memory = new MemoryStream();
                await source.CopyToAsync(memory);
                return memory.ToArray();
            }
        }

        if (!string.IsNullOrWhiteSpace(material.SourcePath) && File.Exists(material.SourcePath))
            return await File.ReadAllBytesAsync(material.SourcePath);
        return null;
    }

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

    private static DiezPromptPackBuildResult Failure(string projectJson, string status, string message) =>
        new(projectJson, false, status, message, Guid.Empty, Guid.Empty, 0, string.Empty, "MANUAL");

    private static string ManualInstructions(int publisherMaterialCount) => $"""
# Diez ∞ Publishing Studio — Prompt Pack manuale

Questo ZIP è il passaggio manuale ufficiale fra Diez e il sistema AI scelto dall'utente.

1. Leggi `prompt-manifest.json`.
2. Esegui ogni `work_unit` usando ESATTAMENTE il relativo campo `instruction` come prompt provider-facing.
3. Gli ID, i codici e i numeri di versione nel manifest servono solo a Diez per ricomporre il lavoro: non inserirli nel contenuto generato.
4. Usa eventuali file sotto `inputs/` soltanto per i ruoli dichiarati dal manifest. I materiali del publisher sono {publisherMaterialCount} e si trovano sotto `inputs/publisher/`; per ciascuno rispetta `intent_code`, `instruction`, `ai_use_policy` e `fidelity`.
5. Non usare né inventare materiali che il manifest non dichiara inviabili; gli asset diretti e i materiali `NEVER_SEND` restano fuori dal Prompt Pack.
6. Per immagini restituisci anche una descrizione fedele del risultato.
7. Non approvare implicitamente nulla: ogni risultato rientra in Diez come Candidate e passa dalla review prevista per quel tipo di contenuto.
8. La strada Manuale e la strada Via API devono produrre Candidate sulle stesse Work Unit. Cambia il trasporto, non il modello editoriale.

Formato di risposta previsto: protocollo `diez-response` v1, con `work_unit_id`, `candidate_version`, `content_type`, `status`, eventuale `primary_asset` e `description`.
""";

    private static string EnsureZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? path : path + ".zip";

    private static string SafeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "asset.bin" : Path.GetFileName(name);
        return string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }

    private static async Task WriteTextAsync(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text);
    }

    private static async Task WriteBytesAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static string ReadString(JsonObject obj, string name)
    {
        var node = obj[name];
        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result ?? string.Empty
            : string.Empty;
    }

    private static Guid? ReadGuid(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return null;
        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty) return guid;
        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }
}
