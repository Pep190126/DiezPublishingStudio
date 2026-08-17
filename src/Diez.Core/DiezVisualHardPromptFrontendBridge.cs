using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezVisualHardPromptMutation(
    string ProjectJson,
    bool Success,
    string Status,
    string Message,
    int Recompiled);

/// <summary>
/// Recompiles the provider-facing visual instructions immediately before a real Prompt Pack is frozen.
/// This is deliberately later than the UI draft: Scene/Subject/Consistent edits made after the initial
/// job creation must still become authoritative in the delivered Prompt Pack.
/// </summary>
public static class DiezVisualHardPromptFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezVisualHardPromptMutation Recompile(
        string projectJson,
        IEnumerable<Guid>? workUnitIds = null)
    {
        var (root, project) = Parse(projectJson);
        if (!VisualBookPlanService.IsVisualFamily(project))
            return new(projectJson, false, "NOT_VISUAL", "Il progetto non è un libro con immagini.", 0);

        var state = AiExchangeStateStore.Load(project);
        var requested = workUnitIds?.Where(x => x != Guid.Empty).Distinct().ToHashSet();
        var units = state.WorkUnits
            .Where(x => string.Equals(x.ContentType, AiExchangeContentTypes.Image, StringComparison.OrdinalIgnoreCase))
            .Where(x => requested is not { Count: > 0 } || requested.Contains(x.WorkUnitId))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (units.Count == 0)
            return new(projectJson, false, "NO_WORK_UNITS", "Non ci sono Work Unit immagine da ricompilare.", 0);

        foreach (var unit in units)
        {
            var prompt = VisualHardPromptContractCompiler.Build(project, unit);
            unit.Instruction = prompt;

            if (unit.LegacyAiJobId is Guid legacyId)
            {
                var job = project.AiProductionJobs.FirstOrDefault(x => x.JobId == legacyId);
                if (job is not null)
                {
                    job.Prompt = prompt;
                    job.Request = prompt;
                    job.UpdatedAtLocal = DateTimeOffset.Now.ToString("G");
                }
            }
        }

        AiExchangeStateStore.Save(project, state);
        MergeExchangeEntity(root, project);
        MergeAiJobs(root, project);
        return new(
            Write(root),
            true,
            "RECOMPILED",
            $"Ricompilate {units.Count} Work Unit con i vincoli HARD correnti di soggetti, Scene, Consistent e profilo visuale.",
            units.Count);
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

    private static void MergeAiJobs(JsonObject root, PreviewProject project)
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

    private static string ReadString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result ?? string.Empty
            : string.Empty;

    private static string Write(JsonObject root) =>
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// Core port of the strongest renderer-facing rules from the Avalonia Coloring pipeline.
/// The compiler emits visual semantics only: no WorkUnitId, routing, retry/session metadata or package IDs.
/// </summary>
internal static class VisualHardPromptContractCompiler
{
    public static string Build(PreviewProject project, AiExchangeWorkUnit unit)
    {
        var plan = VisualBookPlanService.Load(project);
        var settings = PromptPreparationSettingsStore.Load(project);
        var master = PromptMasterStateStore.LoadForCurrentBook(project);
        var request = PromptEngineeringEngine.BuildRequest(
            project,
            Math.Max(1, plan.ImageCount),
            master?.MustDo ?? string.Empty,
            master?.MustNotDo ?? string.Empty,
            settings.ProviderId,
            settings.PreferAdvancedModel);

        var position = unit.Position > 0 ? unit.Position : 1;
        var item = request.ItemOverrides.FirstOrDefault(x => x.ItemIndex == position);
        var scene = StructuredSceneProfileService.SceneForPosition(project, position);
        var participants = scene is null
            ? Array.Empty<MultiSubjectDefinition>()
            : StructuredSceneProfileService.Participants(project, scene).ToArray();

        MultiSubjectDefinition? focal = null;
        var multi = MultiSubjectProfileService.Load(project);
        if (multi.Enabled && MultiSubjectProfileService.ActiveSubjects(multi).Count > 0)
            focal = MultiSubjectProfileService.SubjectForItem(project, position);
        if (participants.Length > 0 &&
            (focal is null || participants.All(x => !string.Equals(x.SubjectId, focal.SubjectId, StringComparison.OrdinalIgnoreCase))))
            focal = participants[0];

        var subject = focal?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(subject)) subject = (item?.Subject ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(subject)) subject = (request.Subject ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(subject)) subject = "the requested focal subject";

        var environment = !string.IsNullOrWhiteSpace(item?.Environment)
            ? item!.Environment.Trim()
            : VisualPromptIntentSynthesizer.SeriesEnvironment(project, request.Environment).Trim();
        var consistent = plan.Consistent || !string.IsNullOrWhiteSpace(request.ConsistencyRules);

        var sb = new StringBuilder();
        sb.AppendLine(string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? "Create ONE finished, publication-quality coloring-book illustration."
            : "Create ONE finished, publication-quality editorial image.");
        sb.AppendLine(VisualPromptIntentSynthesizer.BuildWorkUnitDirection(project, request, subject, scene, participants));
        sb.AppendLine($"PRIMARY SUBJECT — HARD LOCK: {subject}. The subject must be dominant, large, immediately recognizable, anatomically coherent for the selected style and more visually important than the background.");
        AppendSubject(sb, focal, consistent);
        AppendScene(sb, project, scene, focal, participants, consistent);
        sb.AppendLine("COMPOSITION — HARD LOCK: exactly ONE unified continuous primary scene filling the canvas. No collage, grid, contact sheet, split panel, stacked alternatives or visual representation of the series count.");
        if (!string.IsNullOrWhiteSpace(environment))
            sb.AppendLine($"SETTING — SUPPORTING ONLY: {environment}. Use only scene elements that clarify place, action or mood; keep them subordinate to the required subjects and avoid unrelated filler.");

        var required = Join(request.MustDo, item?.MustDo);
        var excluded = Join(request.MustNotDo, item?.MustNotDo);
        if (!string.IsNullOrWhiteSpace(required)) sb.AppendLine("USER REQUIREMENT — HARD: " + required);
        if (!string.IsNullOrWhiteSpace(excluded)) sb.AppendLine("USER EXCLUSION — HARD: " + excluded);

        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            AppendColoring(sb, project, request);
        else
            AppendImageBook(sb, request);

        AppendTechnical(sb, request.Technical);
        var sceneCheck = participants.Length > 0 ? ", SCENE PARTICIPANTS" : string.Empty;
        sb.AppendLine(string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? $"FINAL CHECK — HARD: before returning the asset, visibly verify PRIMARY SUBJECT{sceneCheck}, STYLE, BOLD & EASY ON/OFF, COZY ON/OFF, LINE WEIGHT, pure black/white coloring output, professional drawing craft and one unified composition. If any one fails, regenerate instead of returning the asset."
            : $"FINAL CHECK — HARD: before returning the asset, visibly verify PRIMARY SUBJECT{sceneCheck}, rendering style, requested line/edge treatment, editorial clarity and one unified composition. If a HARD requirement fails, regenerate instead of returning the asset.");

        return PromptEnglishNormalizer.NormalizeProviderFacing(sb.ToString()).Trim();
    }

    private static void AppendColoring(StringBuilder sb, PreviewProject project, PromptEngineeringRequest request)
    {
        var profile = BookTypePromptProfileService.LoadColoring(project);
        var hard = ColoringIndependentHardProfileService.Resolve(project);
        var style = string.IsNullOrWhiteSpace(hard.Style) ? "Clean Line Art" : hard.Style.Trim();

        sb.AppendLine($"STYLE — HARD LOCK: {style}. {BookTypePromptProfileService.StyleHardDirectiveEnglish(style)} A polished image in another visual style is still non-compliant and must be regenerated.");
        sb.AppendLine(ColoringIndependentHardProfileService.BoldEasyDirective(hard.BoldEasy));
        sb.AppendLine(ColoringIndependentHardProfileService.CozyDirective(hard.Cozy));
        sb.AppendLine($"EDITORIAL TARGET: audience {Value(request.Audience, "general audience")}; difficulty {Value(request.Difficulty, "Medium")}; line weight {Value(hard.LineWeight, "Medium")}; visual complexity {Value(request.Complexity, "Medium")}; element density {Value(request.Density, "Low to medium")}; background {Value(request.Background, "Simple contextual background")}; white space {Value(request.WhiteSpace, "Balanced")}.");
        sb.AppendLine("LINE WEIGHT — HARD: the selected line weight is authoritative and must be visibly consistent. Thick/Bold requires confident strong contours without crude blobs; Thin/Fine must remain genuinely fine and must never be converted into a Bold & Easy-like heavy outline.");
        sb.AppendLine("COLORING OUTPUT — HARD: the final raster must contain exactly pure black #000000 and pure white #FFFFFF. No gray pixels, grayscale, antialiasing gray, color, gradients, shadows, glow, halftones, tonal texture or intermediate values. Threshold/binarize the final asset if necessary.");
        sb.AppendLine("DRAWING CRAFT — HARD: use smooth intentional organic contours, coherent anatomy/structure, plausible limbs/faces/paws/tails/joints for the selected style, a strong readable silhouette, clean continuous contours and a balanced professional composition. Simple child-friendly art must never look like crude geometric primitives, unfinished clip-art, an icon sheet, placeholder art or a diagram.");
        sb.AppendLine("SEMANTIC CLEANLINESS — HARD: every visible element must have a scene/story/colorability reason to exist. No random floating circles, bars, diamonds, symbols, confetti, abstract filler, meaningless repeated marks or unrelated decorative motifs used merely to fill empty space.");
        if (profile.ClosedAreas)
            sb.AppendLine("COLORABLE REGIONS — HARD: prefer clearly CLOSED, comfortably fillable regions; avoid confusing overlaps and open boundaries that make coloring ambiguous.");
        if (profile.AvoidTinyAreas)
            sb.AppendLine("MICRO-DETAIL — HARD: avoid tiny enclosed cells, tangled crossings and micro-details unsuitable for the selected audience and difficulty.");
        if (profile.CleanContours)
            sb.AppendLine("CONTOURS — HARD: contours must be clean, continuous, deliberate and print-legible; no broken, doubled, dirty, dangling or accidental lines.");
        if (profile.SubjectClearlySeparated)
            sb.AppendLine("SUBJECT READABILITY — HARD: the main subject and every required scene participant must remain clearly separated from the background and recognizable at thumbnail size.");
        if (profile.NoTextInsideImage)
            sb.AppendLine("VISIBLE CONTENT — HARD: no text, letters, numbers, labels, logos, signatures, watermarks, pseudo-text, IDs, filenames, prompt fragments or UI elements inside the artwork.");
        if (!string.IsNullOrWhiteSpace(profile.CustomStyleNotes))
            sb.AppendLine("CUSTOM STYLE NOTES — HARD WHEN APPLICABLE: " + profile.CustomStyleNotes.Trim());
    }

    private static void AppendImageBook(StringBuilder sb, PromptEngineeringRequest request)
    {
        sb.AppendLine($"RENDERING STYLE — HARD LOCK: {Value(request.RenderingStyle, "professional illustration")}. The visible rendering must materially match the selected style; a professional image in a different style is not equivalent.");
        sb.AppendLine($"COLOR TREATMENT — HARD: {Value(request.ColorMode, "appropriate professional color treatment")}.");
        sb.AppendLine($"LINE / EDGE TREATMENT — HARD: {Value(request.LineTreatment, "appropriate to the selected style")}.");
        sb.AppendLine($"DETAIL LEVEL — HARD: {Value(request.DetailLevel, "Medium")}.");
        sb.AppendLine($"VIEWPOINT: {Value(request.Viewpoint, "appropriate to the subject")}.");
        sb.AppendLine($"BACKGROUND: {Value(request.Background, "contextually appropriate")}.");
        sb.AppendLine("CRAFT — HARD: coherent composition, plausible geometry/anatomy/perspective, clean edges, no accidental duplicates or malformed structures, and a publication-ready finish.");
        if (request.SubjectClearlySeparated)
            sb.AppendLine("SUBJECT READABILITY — HARD: keep the principal subject immediately distinguishable from the background.");
        if (request.NoTextInsideImage)
            sb.AppendLine("VISIBLE CONTENT — HARD: no text, labels, captions, IDs, signatures or watermarks unless this exact Work Unit explicitly requires them.");
        if (request.EditorialClarity)
            sb.AppendLine("EDITORIAL CLARITY — HARD: communicative value and semantic accuracy take priority over ornamental complexity.");
    }

    private static void AppendSubject(StringBuilder sb, MultiSubjectDefinition? subject, bool consistent)
    {
        if (subject is null) return;
        if (!string.IsNullOrWhiteSpace(subject.Description))
            sb.AppendLine("SUBJECT IDENTITY — HARD LOCK: " + subject.Description.Trim() + " Preserve these identifying traits whenever this subject appears.");
        if (!consistent) return;
        var rules = MultiSubjectProfileService.BuildConsistencyRules(subject);
        if (string.IsNullOrWhiteSpace(rules)) return;
        sb.AppendLine("SUBJECT-SPECIFIC CONSISTENT — AUTHORITATIVE:");
        foreach (var line in Lines(rules)) sb.AppendLine("- " + line);
    }

    private static void AppendScene(
        StringBuilder sb,
        PreviewProject project,
        StructuredSceneDefinition? scene,
        MultiSubjectDefinition? focal,
        IReadOnlyList<MultiSubjectDefinition> participants,
        bool consistent)
    {
        if (scene is null) return;
        var defaultItalian = $"Scena {scene.Number}";
        var defaultEnglish = $"Scene {scene.Number}";
        var name = string.Equals(scene.Name, defaultItalian, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scene.Name, defaultEnglish, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : scene.Name.Trim();
        var intent = string.Join(" — ", new[] { name, scene.Description?.Trim() ?? string.Empty }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(intent))
            sb.AppendLine("SCENE INTENT — HARD LOCK: " + intent + ". Keep this action/relationship inside the same unified composition.");
        if (participants.Count == 0) return;

        sb.AppendLine("SCENE PARTICIPANTS — HARD LOCK: " + string.Join(", ", participants.Select(x => x.Name)) + ". Every listed participant must visibly appear in this SAME scene. Do not omit, merge, replace or substitute any listed participant with another subject.");
        foreach (var participant in participants)
        {
            if (focal is not null && string.Equals(participant.SubjectId, focal.SubjectId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(participant.Description))
                sb.AppendLine($"PARTICIPANT IDENTITY — HARD LOCK [{participant.Name}]: {participant.Description.Trim()} Preserve these identifying traits in this scene.");
            if (!consistent) continue;
            var rules = MultiSubjectProfileService.BuildConsistencyRules(participant);
            if (string.IsNullOrWhiteSpace(rules)) continue;
            sb.AppendLine($"PARTICIPANT CONSISTENT — AUTHORITATIVE [{participant.Name}]:");
            foreach (var line in Lines(rules)) sb.AppendLine("- " + line);
        }
    }

    private static void AppendTechnical(StringBuilder sb, PromptEngineeringTechnicalSpec technical)
    {
        if (!string.IsNullOrWhiteSpace(technical.AspectRatio))
            sb.AppendLine("ASPECT RATIO — HARD: " + technical.AspectRatio.Trim() + ". Preserve the ratio without stretching or geometric deformation.");
        if (!string.IsNullOrWhiteSpace(technical.PixelWidth) && !string.IsNullOrWhiteSpace(technical.PixelHeight))
            sb.AppendLine($"RASTER TARGET — HARD: {technical.PixelWidth.Trim()} × {technical.PixelHeight.Trim()} px; preserve aspect ratio.");
        if (!string.IsNullOrWhiteSpace(technical.Dpi))
            sb.AppendLine("PRINT TARGET: " + technical.Dpi.Trim() + " DPI metadata/context.");
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Join(string? first, string? second)
    {
        var values = new[] { first, second }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join("; ", values);
    }

    private static string Value(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
