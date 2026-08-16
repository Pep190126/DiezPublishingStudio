using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezPublicationCheckDto(string Code, string Severity, bool Passed, string Message);

public sealed record DiezPublicationStateDto(
    bool HasFreeze,
    bool FreezeCurrent,
    bool PreflightReady,
    bool HasPublicationCandidate,
    bool PublicationCandidateCurrent,
    IReadOnlyList<DiezPublicationCheckDto> Checks,
    string Summary);

public sealed record DiezPublicationMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezPublicationStateDto State);

public sealed record DiezFileExportResult(bool Success, string Message, string? OutputPath, int ItemCount);

/// <summary>
/// Migration-safe frontend boundary for freeze, preflight, publication candidate and final visual handoff.
/// Unknown JSON extensions are preserved while Core-owned arrays are merged by stable IDs.
/// </summary>
public static class DiezPublicationFrontendBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezPublicationStateDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return State(project);
    }

    public static DiezPublicationMutation CreateFreeze(string projectJson, string? note = null)
    {
        var (root, project) = Parse(projectJson);
        var result = EditionFreezeService.CreateFreeze(project, note);
        MergeProject(root, project);
        var status = result.Freeze is null ? "BLOCKED" : "FROZEN";
        return new DiezPublicationMutation(Write(root), status, result.Message, State(project));
    }

    public static DiezPublicationMutation CreatePublicationCandidate(string projectJson)
    {
        var (root, project) = Parse(projectJson);
        var result = PublicationCandidateService.Create(project);
        MergeProject(root, project);
        var status = result.Candidate is null ? "BLOCKED" : "CREATED";
        return new DiezPublicationMutation(Write(root), status, result.Message, State(project));
    }

    public static string SuggestedPublicationPackageName(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return PublicationCandidateService.SuggestedPackageName(project);
    }

    public static string SuggestedVisualImagesZipName(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return VisualHandoffExportService.SuggestedFileName(project);
    }

    public static async Task<DiezFileExportResult> ExportPublicationPackageAsync(string projectJson, string outputPath)
    {
        var (_, project) = Parse(projectJson);
        var result = await PublicationCandidateService.ExportPackageAsync(project, outputPath);
        return new DiezFileExportResult(result.Exported, result.Message, result.OutputPath, result.Exported ? 1 : 0);
    }

    public static async Task<DiezFileExportResult> ExportFinalVisualImagesAsync(
        string projectJson,
        string projectPath,
        string outputPath)
    {
        var (_, project) = Parse(projectJson);
        var result = await VisualHandoffExportService.ExportFinalImagesZipAsync(project, projectPath, outputPath);
        return new DiezFileExportResult(result.Exported, result.Message, result.OutputPath, result.ItemCount);
    }

    private static DiezPublicationStateDto State(PreviewProject project)
    {
        var freeze = EditionFreezeService.GetLatestFreeze(project);
        var preflight = EditionFreezeService.RunPreflight(project);
        var candidate = PublicationCandidateService.GetLatest(project);
        return new DiezPublicationStateDto(
            freeze is not null,
            freeze is not null && EditionFreezeService.IsLatestFreezeCurrent(project),
            preflight.Ready,
            candidate is not null,
            candidate is not null && PublicationCandidateService.IsLatestCandidateCurrent(project),
            preflight.Checks.Select(c => new DiezPublicationCheckDto(c.Code, c.Severity, c.Passed, c.Message)).ToList(),
            preflight.Summary);
    }

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
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

    private static void MergeProject(JsonObject root, PreviewProject project)
    {
        MergeArray(root, "ContentNodes", project.ContentNodes, "ContentId");
        MergeArray(root, "IllustrationPlacements", project.IllustrationPlacements, "PlacementId");
        MergeArray(root, "Entities", project.Entities, "EntityId");
        MergeArray(root, "Relations", project.Relations, "RelationId");
        MergeArray(root, "BibleEntries", project.BibleEntries, "BibleEntryId");
        MergeArray(root, "ConsistencyFacts", project.ConsistencyFacts, "FactId");
        MergeArray(root, "ConsistencyIssues", project.ConsistencyIssues, "IssueId");
        MergeArray(root, "ConsistencyResolutions", project.ConsistencyResolutions, "ResolutionId");
        MergeArray(root, "RevisionCandidates", project.RevisionCandidates, "CandidateId");
    }

    private static void MergeArray<T>(JsonObject root, string property, IEnumerable<T> typedItems, string idProperty)
    {
        var raw = root[property] as JsonArray ?? new JsonArray();
        root[property] = raw;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in typedItems)
        {
            if (JsonSerializer.SerializeToNode(item, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed[idProperty]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            ids.Add(id);
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x => string.Equals(Scalar(x[idProperty]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                raw.Add(typed);
                continue;
            }
            foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            if (raw[i] is not JsonObject obj) continue;
            var id = Scalar(obj[idProperty]);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id)) raw.RemoveAt(i);
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
