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

public sealed record DiezAiFrontendMutation(
    string ProjectJson,
    DiezAiFrontendJob Job,
    int ExchangeWorkUnitCount);

/// <summary>
/// Compatibility bridge used while Uno still owns the ZIP/package shell.
/// It mutates only the AI sections of the supplied project JSON, preserving unknown
/// root/entity properties that a typed round-trip could otherwise discard.
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
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
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

    public static string SetProjectBrief(string projectJson, string? projectBrief)
    {
        var (root, project) = Parse(projectJson);
        AiProductionService.SetProjectBrief(project, projectBrief);
        MergeAiProduction(root, project);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static DiezAiFrontendJob ToDto(AiProductionJob job, AiExchangeWorkUnit? workUnit) =>
        new(
            job.JobId,
            workUnit?.WorkUnitId,
            job.Code ?? string.Empty,
            job.OutputType ?? string.Empty,
            AiProductionService.DisplayType(job.OutputType),
            job.Status ?? string.Empty,
            AiProductionService.DisplayStatus(job.Status),
            job.Title ?? string.Empty,
            job.Prompt ?? string.Empty);

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
}
