using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Stable frontend-facing projection of one AI production job and its AI Exchange work unit.
/// The bridge intentionally exposes only simple DTOs so UI projects do not depend on the
/// internal persistence model of PreviewProject/AiExchangeState.
/// </summary>
public sealed record DiezAiFrontendJob(
    Guid JobId,
    Guid? WorkUnitId,
    string Code,
    string OutputType,
    string DisplayType,
    string Status,
    string DisplayStatus,
    string Title,
    string Prompt);

public sealed record DiezAiFrontendVersion(
    Guid VersionId,
    Guid WorkUnitId,
    int VersionNumber,
    string Status,
    string DisplayStatus,
    string TextContent,
    string Description,
    bool CanApprove);

public sealed record DiezAiFrontendMutation(
    string ProjectJson,
    DiezAiFrontendJob Job,
    int ExchangeWorkUnitCount);

public sealed record DiezAiFrontendResultMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezAiFrontendVersion? Version,
    DiezAiFrontendJob? Job);

/// <summary>
/// Compatibility bridge used while Uno still owns the ZIP/package shell.
/// It mutates only the AI sections of the supplied project JSON, preserving unknown
/// root/entity/job properties that a typed round-trip could otherwise discard.
/// </summary>
public static class DiezAiExchangeBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezAiFrontendMutation CreateReadyJob(
        string projectJson,
        string title,
        string outputType,
        string preparedPrompt,
        string? projectBrief = null)
    {
        var (root, project) = Parse(projectJson);

        if (projectBrief is not null)
        {
            AiProductionService.SetProjectBrief(project, projectBrief);
            MergeAiProduction(root, project);
        }

        var job = AiProductionService.CreateJob(
            project,
            outputType,
            title,
            preparedPrompt);

        // A prompt coming from Prompt Compiler / a user-edited provider-facing box is already
        // prepared. Do not re-inject JOB DIEZ, routing or other internal metadata into it.
        job.Prompt = (preparedPrompt ?? string.Empty).Trim();
        job.Request = (preparedPrompt ?? string.Empty).Trim();

        AppendRawJob(root, job);

        var exchange = AiExchangeStateStore.Load(project);
        AiExchangeStateStore.Save(project, exchange);
        MergeExchangeEntity(root, project);

        var workUnit = exchange.WorkUnits.FirstOrDefault(w => w.LegacyAiJobId == job.JobId);
        var dto = ToDto(job, workUnit);
        return new DiezAiFrontendMutation(
            Write(root),
            dto,
            exchange.WorkUnits.Count);
    }

    public static IReadOnlyList<DiezAiFrontendJob> ReadJobs(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        return project.AiProductionJobs
            .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .Select(job => ToDto(job, exchange.WorkUnits.FirstOrDefault(w => w.LegacyAiJobId == job.JobId)))
            .ToList();
    }

    public static IReadOnlyList<DiezAiFrontendVersion> ReadVersions(string projectJson, Guid workUnitId)
    {
        var (_, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var unit = exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == workUnitId);
        if (unit is null) return [];
        return exchange.Versions
            .Where(v => v.WorkUnitId == workUnitId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => ToVersionDto(unit, v))
            .ToList();
    }

    /// <summary>
    /// Imports a pasted text/data response as an AI Exchange candidate. Supplying candidateVersion
    /// is useful for Prompt Pack responses and for duplicate/conflict detection; when omitted the
    /// next version number is assigned by the Core.
    /// </summary>
    public static async Task<DiezAiFrontendResultMutation> IngestTextResultAsync(
        string projectJson,
        Guid workUnitId,
        string? textContent,
        int? candidateVersion = null,
        string resultStatus = "COMPLETE")
    {
        var (root, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var unit = exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == workUnitId);
        if (unit is null)
            return Result(root, project, exchange, "INVALID", "Contenuto AI non trovato.", null, null);
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            return Result(root, project, exchange, "INVALID", "Per un risultato immagine usa il flusso immagini e Vision.", null, unit);

        var versionNumber = candidateVersion.GetValueOrDefault();
        if (versionNumber <= 0) versionNumber = AiExchangeStateStore.NextVersionNumber(exchange, unit.WorkUnitId);

        var incomingText = textContent ?? string.Empty;
        var existing = exchange.Versions.FirstOrDefault(v =>
            v.WorkUnitId == unit.WorkUnitId && v.VersionNumber == versionNumber);
        if (existing is not null &&
            !string.IsNullOrWhiteSpace(existing.TextContent) &&
            !string.IsNullOrWhiteSpace(incomingText) &&
            !string.Equals(existing.TextContent, incomingText, StringComparison.Ordinal))
        {
            return Result(
                root,
                project,
                exchange,
                "CONFLICT",
                "Stessa Work Unit e stessa versione, ma il testo ricevuto è differente.",
                existing,
                unit);
        }

        var ingest = await AiExchangeResultIngestor.IngestAsync(project, exchange, new AiExchangeNormalizedResultItem
        {
            WorkUnitId = unit.WorkUnitId,
            CandidateVersion = versionNumber,
            ContentType = unit.ContentType,
            ResultStatus = resultStatus,
            TextContent = incomingText,
            Origin = AiExchangeOrigins.Import
        });

        AiExchangeStateStore.Save(project, exchange);
        MergeAiProductionJobs(root, project);
        MergeExchangeEntity(root, project);
        var version = ingest.VersionId.HasValue
            ? exchange.Versions.FirstOrDefault(v => v.VersionId == ingest.VersionId.Value)
            : null;
        var legacy = unit.LegacyAiJobId.HasValue
            ? project.AiProductionJobs.FirstOrDefault(j => j.JobId == unit.LegacyAiJobId.Value)
            : null;
        return new DiezAiFrontendResultMutation(
            Write(root),
            ingest.Status,
            ingest.Message,
            version is null ? null : ToVersionDto(unit, version),
            legacy is null ? null : ToDto(legacy, unit));
    }

    /// <summary>
    /// Generic approval is deliberately limited to non-image results. Image approval is a
    /// separate Vision-gated operation so no frontend can bypass the HARD semantic checks.
    /// </summary>
    public static DiezAiFrontendResultMutation ApproveVersion(string projectJson, Guid versionId)
    {
        var (root, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var version = exchange.Versions.FirstOrDefault(v => v.VersionId == versionId);
        var unit = version is null ? null : exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
        if (version is null || unit is null)
            return Result(root, project, exchange, "INVALID", "Versione AI non trovata.", version, unit);
        if (string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            return Result(
                root,
                project,
                exchange,
                "VISION_REQUIRED",
                "Le immagini si approvano solo dopo Vision e tutti i controlli HARD applicabili.",
                version,
                unit);

        var approved = AiExchangeResultIngestor.Approve(project, exchange, versionId, out var message);
        if (approved) AiExchangeStateStore.Save(project, exchange);
        MergeAiProductionJobs(root, project);
        MergeExchangeEntity(root, project);
        var legacy = unit.LegacyAiJobId.HasValue
            ? project.AiProductionJobs.FirstOrDefault(j => j.JobId == unit.LegacyAiJobId.Value)
            : null;
        return new DiezAiFrontendResultMutation(
            Write(root),
            approved ? "APPROVED" : "BLOCKED",
            message,
            ToVersionDto(unit, version),
            legacy is null ? null : ToDto(legacy, unit));
    }

    public static string SetProjectBrief(string projectJson, string? projectBrief)
    {
        var (root, project) = Parse(projectJson);
        AiProductionService.SetProjectBrief(project, projectBrief);
        MergeAiProduction(root, project);
        return Write(root);
    }

    private static DiezAiFrontendResultMutation Result(
        JsonObject root,
        PreviewProject project,
        AiExchangeState exchange,
        string status,
        string message,
        AiExchangeVersion? version,
        AiExchangeWorkUnit? unit)
    {
        MergeAiProductionJobs(root, project);
        MergeExchangeEntity(root, project);
        var legacy = unit?.LegacyAiJobId is Guid id
            ? project.AiProductionJobs.FirstOrDefault(j => j.JobId == id)
            : null;
        return new DiezAiFrontendResultMutation(
            Write(root),
            status,
            message,
            version is null || unit is null ? null : ToVersionDto(unit, version),
            legacy is null ? null : ToDto(legacy, unit));
    }

    private static DiezAiFrontendJob ToDto(AiProductionJob job, AiExchangeWorkUnit? workUnit)
    {
        var outputType = job.OutputType ?? string.Empty;
        var status = job.Status ?? string.Empty;
        return new DiezAiFrontendJob(
            job.JobId,
            workUnit?.WorkUnitId,
            job.Code ?? string.Empty,
            outputType,
            AiProductionService.DisplayType(outputType),
            status,
            AiProductionService.DisplayStatus(status),
            job.Title ?? string.Empty,
            job.Prompt ?? string.Empty);
    }

    private static DiezAiFrontendVersion ToVersionDto(AiExchangeWorkUnit unit, AiExchangeVersion version)
    {
        var image = string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase);
        var canApprove = !image && version.Status != AiExchangeVersionStatuses.Incomplete;
        return new DiezAiFrontendVersion(
            version.VersionId,
            version.WorkUnitId,
            version.VersionNumber,
            version.Status ?? string.Empty,
            DisplayVersionStatus(version.Status),
            version.TextContent ?? string.Empty,
            version.Description ?? string.Empty,
            canApprove);
    }

    private static string DisplayVersionStatus(string? status) => status switch
    {
        AiExchangeVersionStatuses.Candidate => "Candidato da controllare",
        AiExchangeVersionStatuses.Approved => "Approvato",
        AiExchangeVersionStatuses.Incomplete => "Incompleto",
        AiExchangeVersionStatuses.Rejected => "Scartato",
        AiExchangeVersionStatuses.Stale => "Superato da una versione più recente",
        _ => status ?? string.Empty
    };

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");

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

    private static void MergeAiProduction(JsonObject root, PreviewProject project)
    {
        var ai = root["AiProduction"] as JsonObject ?? new JsonObject();
        root["AiProduction"] = ai;
        ai["SchemaVersion"] = project.AiProduction.SchemaVersion;
        ai["ProjectBrief"] = project.AiProduction.ProjectBrief ?? string.Empty;
    }

    private static void AppendRawJob(JsonObject root, AiProductionJob job)
    {
        var jobs = root["AiProductionJobs"] as JsonArray ?? new JsonArray();
        root["AiProductionJobs"] = jobs;
        jobs.Add(JsonSerializer.SerializeToNode(job, JsonOptions));
    }

    private static void MergeAiProductionJobs(JsonObject root, PreviewProject project)
    {
        var jobs = root["AiProductionJobs"] as JsonArray ?? new JsonArray();
        root["AiProductionJobs"] = jobs;
        foreach (var typed in project.AiProductionJobs)
        {
            var raw = jobs.OfType<JsonObject>().FirstOrDefault(x =>
                Guid.TryParse(ReadString(x, "JobId"), out var id) && id == typed.JobId);
            if (raw is null)
            {
                raw = new JsonObject();
                jobs.Add(raw);
            }
            raw["JobId"] = typed.JobId.ToString();
            raw["Code"] = typed.Code;
            raw["OutputType"] = typed.OutputType;
            raw["Title"] = typed.Title;
            raw["Request"] = typed.Request;
            raw["Prompt"] = typed.Prompt;
            raw["Status"] = typed.Status;
            raw["ResultText"] = typed.ResultText;
            raw["ResultMaterialId"] = typed.ResultMaterialId?.ToString();
            raw["TargetContentId"] = typed.TargetContentId?.ToString();
            raw["CreatedAtLocal"] = typed.CreatedAtLocal;
            raw["UpdatedAtLocal"] = typed.UpdatedAtLocal;
        }
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

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static string ReadString(JsonObject obj, string name)
    {
        var node = obj[name];
        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result ?? string.Empty
            : string.Empty;
    }
}
