using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezVisionRequirement(
    string Key,
    string Label,
    string Expected,
    bool Required);

public sealed record DiezVisionCheckInput(
    string Key,
    string Status,
    string Evidence = "");

public sealed record DiezVisionApprovalResult(
    string ProjectJson,
    string Status,
    string Message,
    bool Approved,
    IReadOnlyList<DiezVisionRequirement> Requirements,
    IReadOnlyList<string> BlockingKeys,
    DiezAiFrontendVersion? Version,
    DiezAiFrontendJob? Job);

/// <summary>
/// Public, UI-neutral Vision approval boundary. A frontend may report semantic checks,
/// but the Core determines which checks are mandatory for the current image candidate.
/// Missing, REVIEW, NA or FAIL on a required HARD gate blocks approval.
/// </summary>
public static class DiezVisionFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private const string VisionEntityKind = "DiezVisionValidation";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static IReadOnlyList<DiezVisionRequirement> Requirements(string projectJson, Guid workUnitId)
    {
        var (_, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var unit = exchange.WorkUnits.FirstOrDefault(x => x.WorkUnitId == workUnitId);
        return unit is null ? [] : BuildRequirements(project, unit);
    }

    public static DiezVisionApprovalResult ApproveImageVersion(
        string projectJson,
        Guid versionId,
        IEnumerable<DiezVisionCheckInput>? checks,
        string? summary = null,
        double confidence = 1.0)
    {
        var (root, project) = Parse(projectJson);
        var exchange = AiExchangeStateStore.Load(project);
        var version = exchange.Versions.FirstOrDefault(v => v.VersionId == versionId);
        var unit = version is null ? null : exchange.WorkUnits.FirstOrDefault(w => w.WorkUnitId == version.WorkUnitId);
        if (version is null || unit is null)
            return BuildResult(root, project, exchange, "INVALID", "Versione immagine non trovata.", false, [], [], version, unit);
        if (!string.Equals(unit.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            return BuildResult(root, project, exchange, "INVALID", "La versione selezionata non è un'immagine.", false, [], [], version, unit);

        var requirements = BuildRequirements(project, unit);
        var recheckAfterVisionFailure =
            version.Status == AiExchangeVersionStatuses.Incomplete &&
            string.Equals(version.DescriptionStatus, AiExchangeDescriptionStatuses.NeedsVerification, StringComparison.OrdinalIgnoreCase) &&
            version.MaterialId.HasValue &&
            !string.IsNullOrWhiteSpace(version.Description);
        if ((!recheckAfterVisionFailure && version.Status == AiExchangeVersionStatuses.Incomplete) ||
            !version.MaterialId.HasValue ||
            string.IsNullOrWhiteSpace(version.Description) ||
            string.Equals(version.DescriptionStatus, AiExchangeDescriptionStatuses.Missing, StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(
                root,
                project,
                exchange,
                "INCOMPLETE",
                "Prima di Vision servono sia l'immagine sia una descrizione valida della candidate.",
                false,
                requirements,
                ["candidate_complete"],
                version,
                unit);
        }

        var submitted = (checks ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var selectedStyle = SelectedStyle(project);
        var normalizedChecks = new List<(DiezVisionRequirement Requirement, VisionHardGateCheck Check, string Evidence)>();
        var blocking = new List<string>();
        foreach (var requirement in requirements.Where(x => x.Required))
        {
            if (!submitted.TryGetValue(requirement.Key, out var input))
            {
                blocking.Add(requirement.Key);
                normalizedChecks.Add((
                    requirement,
                    VisionHardGatePolicy.Enforce(requirement.Key, VisionHardGatePolicy.Fail, VisionHardGatePolicy.Hard, selectedStyle),
                    "Controllo obbligatorio non fornito."));
                continue;
            }

            var enforced = VisionHardGatePolicy.Enforce(
                requirement.Key,
                input.Status,
                VisionHardGatePolicy.Hard,
                selectedStyle);
            normalizedChecks.Add((requirement, enforced, input.Evidence ?? string.Empty));
            if (!string.Equals(enforced.Status, VisionHardGatePolicy.Pass, StringComparison.Ordinal))
                blocking.Add(requirement.Key);
        }

        foreach (var optional in submitted.Values.Where(x => requirements.All(r => !string.Equals(r.Key, x.Key, StringComparison.OrdinalIgnoreCase))))
        {
            var pseudo = new DiezVisionRequirement(optional.Key, optional.Key, string.Empty, false);
            normalizedChecks.Add((
                pseudo,
                VisionHardGatePolicy.Enforce(optional.Key, optional.Status, VisionHardGatePolicy.Soft, selectedStyle),
                optional.Evidence ?? string.Empty));
        }

        var approved = blocking.Count == 0;
        if (!approved)
        {
            version.Status = AiExchangeVersionStatuses.Incomplete;
            version.DescriptionStatus = AiExchangeDescriptionStatuses.NeedsVerification;
            if (unit.LegacyAiJobId is Guid jobId)
            {
                var legacy = project.AiProductionJobs.FirstOrDefault(j => j.JobId == jobId);
                if (legacy is not null)
                {
                    legacy.Status = AiProductionService.StatusNeedsRevision;
                    legacy.UpdatedAtLocal = DateTimeOffset.Now.ToString("G");
                }
            }
            AiExchangeStateStore.Save(project, exchange);
        }
        else
        {
            // A previous failed/reviewed Vision attempt may have marked the same complete asset as
            // INCOMPLETE/NEEDS_VERIFICATION. A full PASS restores candidacy before approval.
            version.Status = AiExchangeVersionStatuses.Candidate;
            version.DescriptionStatus = AiExchangeDescriptionStatuses.Valid;
            var ok = AiExchangeResultIngestor.Approve(project, exchange, version.VersionId, out var approveMessage);
            if (!ok)
            {
                SaveVisionAudit(root, project, version, unit, normalizedChecks, false, summary, confidence);
                MergeAiProductionJobs(root, project);
                MergeExchangeEntity(root, project);
                return BuildResult(root, project, exchange, "BLOCKED", approveMessage, false, requirements, ["approval"], version, unit);
            }
            AiExchangeStateStore.Save(project, exchange);
        }

        SaveVisionAudit(root, project, version, unit, normalizedChecks, approved, summary, confidence);
        MergeAiProductionJobs(root, project);
        MergeExchangeEntity(root, project);

        return BuildResult(
            root,
            project,
            exchange,
            approved ? "APPROVED" : "VISION_FAILED",
            approved
                ? "Vision PASS: candidate immagine approvata."
                : "Vision ha bloccato l'approvazione: uno o più controlli HARD non sono PASS.",
            approved,
            requirements,
            blocking,
            version,
            unit);
    }

    private static IReadOnlyList<DiezVisionRequirement> BuildRequirements(PreviewProject project, AiExchangeWorkUnit unit)
    {
        var result = new List<DiezVisionRequirement>
        {
            new(VisionHardGatePolicy.SubjectMatch, "Soggetto corretto", "Il soggetto visibile deve corrispondere a quello previsto per questa immagine.", true),
            new(VisionHardGatePolicy.SingleComposition, "Una sola composizione", "Una sola scena/composizione principale, non collage o layout multipli.", true)
        };

        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var hard = ColoringIndependentHardProfileService.Resolve(project);
            result.Add(new(VisionHardGatePolicy.StyleMatch, "Stile", hard.Style, true));
            result.Add(new(VisionHardGatePolicy.BoldEasyMatch, "Bold & Easy", hard.BoldEasy ? "ON" : "OFF", true));
            result.Add(new(VisionHardGatePolicy.CozyMatch, "Cozy", hard.Cozy ? "ON" : "OFF", true));
            result.Add(new(VisionHardGatePolicy.LineWeightMatch, "Spessore linee", hard.LineWeight, true));
        }
        else if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
        {
            var profile = ImageCollectionPromptProfileService.Load(project);
            if (!string.IsNullOrWhiteSpace(profile.RenderingStyle))
                result.Add(new(VisionHardGatePolicy.StyleMatch, "Stile di resa", profile.RenderingStyle, true));
            if (!string.IsNullOrWhiteSpace(profile.LineTreatment))
                result.Add(new(VisionHardGatePolicy.LineWeightMatch, "Trattamento linee", profile.LineTreatment, true));
        }

        var scene = StructuredSceneProfileService.SceneForPosition(project, unit.Position);
        if (scene is not null)
        {
            var participants = StructuredSceneProfileService.Participants(project, scene);
            if (participants.Count > 0)
            {
                result.Add(new(
                    VisionHardGatePolicy.SceneParticipantsMatch,
                    "Soggetti presenti nella scena",
                    string.Join(", ", participants.Select(x => x.Name)),
                    true));
            }
        }

        return result;
    }

    private static string SelectedStyle(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return ColoringIndependentHardProfileService.Resolve(project).Style;
        if (string.Equals(type, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            return ImageCollectionPromptProfileService.Load(project).RenderingStyle ?? string.Empty;
        return string.Empty;
    }

    private static void SaveVisionAudit(
        JsonObject root,
        PreviewProject project,
        AiExchangeVersion version,
        AiExchangeWorkUnit unit,
        IReadOnlyList<(DiezVisionRequirement Requirement, VisionHardGateCheck Check, string Evidence)> checks,
        bool approved,
        string? summary,
        double confidence)
    {
        var entities = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = entities;
        var entity = entities.OfType<JsonObject>().FirstOrDefault(e =>
            string.Equals(ReadString(e, "Kind"), VisionEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new JsonObject
            {
                ["EntityId"] = Guid.NewGuid().ToString(),
                ["Kind"] = VisionEntityKind,
                ["Name"] = "Controllo Vision semantico",
                ["IsCandidate"] = false,
                ["Notes"] = ""
            };
            entities.Add(entity);
        }

        JsonObject state;
        try { state = JsonNode.Parse(ReadString(entity, "Notes")) as JsonObject ?? new JsonObject(); }
        catch { state = new JsonObject(); }
        state["SchemaVersion"] ??= 1;
        var records = state["Records"] as JsonArray ?? new JsonArray();
        state["Records"] = records;
        state["Packs"] ??= new JsonArray();
        state["ImportedPackageIds"] ??= new JsonArray();

        var record = records.OfType<JsonObject>().FirstOrDefault(r =>
            Guid.TryParse(ReadString(r, "VersionId"), out var id) && id == version.VersionId);
        if (record is null)
        {
            record = new JsonObject();
            records.Add(record);
        }

        record["VersionId"] = version.VersionId.ToString();
        record["WorkUnitId"] = unit.WorkUnitId.ToString();
        record["CandidateVersion"] = version.VersionNumber;
        record["ContentSha256"] = version.ContentSha256;
        record["ProviderId"] = "uno-manual";
        record["OverallStatus"] = approved ? VisionHardGatePolicy.Pass : VisionHardGatePolicy.Fail;
        record["BlocksApproval"] = !approved;
        record["Confidence"] = Math.Clamp(confidence, 0, 1);
        record["ObservedDescription"] = version.Description;
        record["Summary"] = (summary ?? string.Empty).Trim();
        record["CheckedAtLocal"] = DateTimeOffset.Now.ToString("O");
        var rawChecks = new JsonArray();
        foreach (var item in checks)
        {
            rawChecks.Add(new JsonObject
            {
                ["Key"] = item.Requirement.Key,
                ["Status"] = item.Check.Status,
                ["Severity"] = item.Requirement.Required ? VisionHardGatePolicy.Hard : item.Check.Severity,
                ["Confidence"] = Math.Clamp(confidence, 0, 1),
                ["Evidence"] = item.Evidence
            });
        }
        record["Checks"] = rawChecks;
        entity["Notes"] = state.ToJsonString(JsonOptions);
    }

    private static DiezVisionApprovalResult BuildResult(
        JsonObject root,
        PreviewProject project,
        AiExchangeState exchange,
        string status,
        string message,
        bool approved,
        IReadOnlyList<DiezVisionRequirement> requirements,
        IReadOnlyList<string> blocking,
        AiExchangeVersion? version,
        AiExchangeWorkUnit? unit)
    {
        var legacy = unit?.LegacyAiJobId is Guid jobId
            ? project.AiProductionJobs.FirstOrDefault(j => j.JobId == jobId)
            : null;
        return new DiezVisionApprovalResult(
            Write(root),
            status,
            message,
            approved,
            requirements,
            blocking,
            version is null || unit is null ? null : ToVersionDto(unit, version),
            legacy is null ? null : ToJobDto(legacy, unit));
    }

    private static DiezAiFrontendJob ToJobDto(AiProductionJob job, AiExchangeWorkUnit? workUnit)
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

    private static DiezAiFrontendVersion ToVersionDto(AiExchangeWorkUnit unit, AiExchangeVersion version) =>
        new(
            version.VersionId,
            version.WorkUnitId,
            version.VersionNumber,
            version.Status ?? string.Empty,
            version.Status switch
            {
                AiExchangeVersionStatuses.Candidate => "Candidato da controllare",
                AiExchangeVersionStatuses.Approved => "Approvato",
                AiExchangeVersionStatuses.Incomplete => "Incompleto",
                AiExchangeVersionStatuses.Rejected => "Scartato",
                AiExchangeVersionStatuses.Stale => "Superato da una versione più recente",
                _ => version.Status ?? string.Empty
            },
            version.TextContent ?? string.Empty,
            version.Description ?? string.Empty,
            version.MaterialId,
            version.ContentSha256 ?? string.Empty,
            version.DescriptionStatus ?? string.Empty,
            false);

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
