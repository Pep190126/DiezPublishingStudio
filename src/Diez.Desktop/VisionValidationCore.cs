using System.Text.Json;

namespace DiezPublishingStudio;

internal static class VisionValidationStatuses
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Review = "REVIEW";
    public const string NotRun = "NOT_RUN";
}

internal static class VisionCheckStatuses
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Warn = "WARN";
    public const string NotApplicable = "NA";
}

internal static class VisionSeverity
{
    public const string Hard = "HARD";
    public const string Soft = "SOFT";
}

internal sealed class VisionValidationCheck
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = VisionCheckStatuses.Pass;
    public string Severity { get; set; } = VisionSeverity.Soft;
    public double Confidence { get; set; }
    public string Evidence { get; set; } = string.Empty;
}

internal sealed class VisionValidationResult
{
    public Guid VersionId { get; set; }
    public Guid WorkUnitId { get; set; }
    public int CandidateVersion { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = VisionValidationStatuses.Review;
    public double Confidence { get; set; }
    public string ObservedDescription { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<VisionValidationCheck> Checks { get; set; } = [];

    public bool BlocksApproval =>
        string.Equals(OverallStatus, VisionValidationStatuses.Fail, StringComparison.OrdinalIgnoreCase) ||
        Checks.Any(c => string.Equals(c.Severity, VisionSeverity.Hard, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.Status, VisionCheckStatuses.Fail, StringComparison.OrdinalIgnoreCase));
}

internal sealed class VisionExpectedSpecification
{
    public string BookType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int SeriesPosition { get; set; }
    public int SeriesCount { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string MustDo { get; set; } = string.Empty;
    public string MustNotDo { get; set; } = string.Empty;
    public string ItemSubject { get; set; } = string.Empty;
    public string ItemEnvironment { get; set; } = string.Empty;
    public string ItemMustDo { get; set; } = string.Empty;
    public string ItemMustNotDo { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string ColorMode { get; set; } = string.Empty;
    public string DetailLevel { get; set; } = string.Empty;
    public string LineWeightOrTreatment { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Viewpoint { get; set; } = string.Empty;
    public string ConsistencyRules { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public string PixelWidth { get; set; } = string.Empty;
    public string PixelHeight { get; set; } = string.Empty;
    public string Dpi { get; set; } = string.Empty;
    public List<string> HardCriteria { get; set; } = [];
    public List<string> QualityCriteria { get; set; } = [];
}

internal sealed class VisionValidationRequest
{
    public Guid ProjectId { get; set; }
    public Guid ValidationPackId { get; set; }
    public Guid VersionId { get; set; }
    public Guid WorkUnitId { get; set; }
    public int CandidateVersion { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string CandidateFile { get; set; } = string.Empty;
    public string ProviderTarget { get; set; } = string.Empty;
    public VisionExpectedSpecification Expected { get; set; } = new();
    public string GenerationContract { get; set; } = string.Empty;
}

internal static class VisionValidationStore
{
    private const string EntityKind = "DiezVisionValidation";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    internal sealed class State
    {
        public int SchemaVersion { get; set; } = 1;
        public List<Record> Records { get; set; } = [];
        public List<PackRecord> Packs { get; set; } = [];
        public List<string> ImportedPackageIds { get; set; } = [];
    }

    internal sealed class Record
    {
        public Guid VersionId { get; set; }
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string OverallStatus { get; set; } = VisionValidationStatuses.NotRun;
        public bool BlocksApproval { get; set; }
        public double Confidence { get; set; }
        public string ObservedDescription { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<VisionValidationCheck> Checks { get; set; } = [];
        public string CheckedAtLocal { get; set; } = string.Empty;
    }

    internal sealed class PackRecord
    {
        public Guid ValidationPackId { get; set; }
        public Guid ProjectId { get; set; }
        public string ProviderTarget { get; set; } = string.Empty;
        public string CreatedAtLocal { get; set; } = string.Empty;
        public List<PackItem> Items { get; set; } = [];
    }

    internal sealed class PackItem
    {
        public Guid VersionId { get; set; }
        public Guid WorkUnitId { get; set; }
        public int CandidateVersion { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
    }

    public static State Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new State();
        try
        {
            var state = JsonSerializer.Deserialize<State>(entity.Notes, JsonOptions) ?? new State();
            state.Records ??= [];
            state.Packs ??= [];
            state.ImportedPackageIds ??= [];
            foreach (var record in state.Records) record.Checks ??= [];
            foreach (var pack in state.Packs) pack.Items ??= [];
            return state;
        }
        catch { return new State(); }
    }

    public static Record? Get(PreviewProject project, Guid versionId) =>
        Load(project).Records.FirstOrDefault(r => r.VersionId == versionId);

    public static PackRecord? GetPack(PreviewProject project, Guid packId) =>
        Load(project).Packs.FirstOrDefault(p => p.ValidationPackId == packId);

    public static void SavePack(PreviewProject project, PackRecord pack)
    {
        var state = Load(project);
        state.Packs.RemoveAll(p => p.ValidationPackId == pack.ValidationPackId);
        state.Packs.Add(pack);
        SaveState(project, state);
    }

    public static bool IsImportedPackage(PreviewProject project, string packageId) =>
        Load(project).ImportedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);

    public static void MarkImportedPackage(PreviewProject project, string packageId)
    {
        var state = Load(project);
        if (!state.ImportedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            state.ImportedPackageIds.Add(packageId);
        SaveState(project, state);
    }

    public static void Apply(
        PreviewProject project,
        AiExchangeState exchange,
        VisionValidationResult result)
    {
        var state = Load(project);
        var previous = state.Records.FirstOrDefault(r => r.VersionId == result.VersionId);
        var record = previous ?? new Record { VersionId = result.VersionId };
        if (previous is null) state.Records.Add(record);

        record.WorkUnitId = result.WorkUnitId;
        record.CandidateVersion = result.CandidateVersion;
        record.ContentSha256 = result.ContentSha256 ?? string.Empty;
        record.ProviderId = result.ProviderId ?? string.Empty;
        record.OverallStatus = NormalizeOverall(result.OverallStatus);
        record.Confidence = Math.Clamp(result.Confidence, 0, 1);
        record.ObservedDescription = (result.ObservedDescription ?? string.Empty).Trim();
        record.Summary = (result.Summary ?? string.Empty).Trim();
        record.Checks = (result.Checks ?? []).Select(NormalizeCheck).Select(c => ApplyHardPolicy(project, c)).ToList();

        var hardFail = record.Checks.Any(c => c.Severity == VisionSeverity.Hard && c.Status == VisionCheckStatuses.Fail);
        if (hardFail) record.OverallStatus = VisionValidationStatuses.Fail;
        record.BlocksApproval = string.Equals(record.OverallStatus, VisionValidationStatuses.Fail, StringComparison.Ordinal) || hardFail;
        record.CheckedAtLocal = DateTimeOffset.Now.ToString("O");

        var version = exchange.Versions.FirstOrDefault(v => v.VersionId == result.VersionId);
        if (version is not null)
        {
            if (record.BlocksApproval)
            {
                version.Status = AiExchangeVersionStatuses.Incomplete;
                version.DescriptionStatus = AiExchangeDescriptionStatuses.NeedsVerification;
            }
            else if (previous?.BlocksApproval == true &&
                     version.MaterialId.HasValue &&
                     !string.IsNullOrWhiteSpace(version.Description) &&
                     VisualAssetValidationStore.Get(project, version.VersionId)?.BlocksApproval != true)
            {
                version.Status = AiExchangeVersionStatuses.Candidate;
                version.DescriptionStatus = AiExchangeDescriptionStatuses.Valid;
            }
        }

        SaveState(project, state);
    }

    public static string UserStatus(PreviewProject project, Guid versionId)
    {
        var record = Get(project, versionId);
        if (record is null) return "Vision: non ancora eseguita";
        return record.OverallStatus switch
        {
            VisionValidationStatuses.Pass => $"Vision: ✓ coerente ({record.Confidence:P0})",
            VisionValidationStatuses.Fail => $"Vision: ✗ non coerente ({record.Confidence:P0})",
            VisionValidationStatuses.Review => $"Vision: ⚠ richiede revisione ({record.Confidence:P0})",
            _ => "Vision: non ancora eseguita"
        };
    }

    private static void SaveState(PreviewProject project, State state)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity
            {
                Kind = EntityKind,
                Name = "Controllo Vision semantico",
                IsCandidate = false
            };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    private static string NormalizeOverall(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        VisionValidationStatuses.Pass => VisionValidationStatuses.Pass,
        VisionValidationStatuses.Fail => VisionValidationStatuses.Fail,
        VisionValidationStatuses.Review => VisionValidationStatuses.Review,
        _ => VisionValidationStatuses.Review
    };

    private static VisionValidationCheck NormalizeCheck(VisionValidationCheck source) => new()
    {
        Key = (source.Key ?? string.Empty).Trim(),
        Status = (source.Status ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            VisionCheckStatuses.Pass => VisionCheckStatuses.Pass,
            VisionCheckStatuses.Fail => VisionCheckStatuses.Fail,
            VisionCheckStatuses.Warn => VisionCheckStatuses.Warn,
            VisionCheckStatuses.NotApplicable => VisionCheckStatuses.NotApplicable,
            _ => VisionCheckStatuses.Warn
        },
        Severity = string.Equals(source.Severity, VisionSeverity.Hard, StringComparison.OrdinalIgnoreCase)
            ? VisionSeverity.Hard
            : VisionSeverity.Soft,
        Confidence = Math.Clamp(source.Confidence, 0, 1),
        Evidence = (source.Evidence ?? string.Empty).Trim()
    };

    private static VisionValidationCheck ApplyHardPolicy(PreviewProject project, VisionValidationCheck check)
    {
        if (string.Equals(check.Key, "style_match", StringComparison.OrdinalIgnoreCase))
            check.Severity = VisionSeverity.Hard;

        // Compatibility with older validators that used style_quality for both match and taste:
        // an explicit FAIL on style_quality becomes HARD when the project has an explicit selected style.
        if (string.Equals(check.Key, "style_quality", StringComparison.OrdinalIgnoreCase) &&
            check.Status == VisionCheckStatuses.Fail &&
            !string.IsNullOrWhiteSpace(SelectedStyle(project)))
            check.Severity = VisionSeverity.Hard;

        if (string.Equals(check.Key, "single_composition", StringComparison.OrdinalIgnoreCase))
            check.Severity = VisionSeverity.Hard;
        return check;
    }

    private static string SelectedStyle(PreviewProject project)
    {
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return BookTypePromptProfileService.LoadColoring(project).Style ?? string.Empty;
        return ImageCollectionPromptProfileService.Load(project).RenderingStyle ?? string.Empty;
    }
}

internal static class VisionValidationSpecificationBuilder
{
    public static VisionValidationRequest Build(
        PreviewProject project,
        AiExchangeState state,
        AiExchangeWorkUnit unit,
        AiExchangeVersion version,
        Guid validationPackId,
        string candidateFile,
        int seriesCount,
        string providerTarget)
    {
        var settings = PromptPreparationSettingsStore.Load(project);
        var master = PromptMasterStateStore.LoadForCurrentBook(project);
        var request = PromptEngineeringEngine.BuildRequest(
            project,
            Math.Max(1, seriesCount),
            master?.MustDo ?? string.Empty,
            master?.MustNotDo ?? string.Empty,
            providerTarget,
            settings.PreferAdvancedModel);
        var position = Math.Max(1, unit.Position);
        var item = request.ItemOverrides.FirstOrDefault(x => x.ItemIndex == position);
        var atomicSubject = PromptPackProviderFacingService.ResolveAtomicSubject(request, position);
        var generationContract = PromptPackProviderFacingService.BuildImageGenerationPrompt(
            project, unit, Math.Max(1, seriesCount), position, settings);

        return new VisionValidationRequest
        {
            ProjectId = project.ProjectId,
            ValidationPackId = validationPackId,
            VersionId = version.VersionId,
            WorkUnitId = unit.WorkUnitId,
            CandidateVersion = version.VersionNumber,
            ContentSha256 = version.ContentSha256,
            CandidateFile = candidateFile,
            ProviderTarget = providerTarget,
            GenerationContract = generationContract,
            Expected = new VisionExpectedSpecification
            {
                BookType = request.BookType,
                Code = unit.Code,
                SeriesPosition = position,
                SeriesCount = Math.Max(1, seriesCount),
                Subject = request.Subject,
                Environment = request.Environment,
                MustDo = request.MustDo,
                MustNotDo = request.MustNotDo,
                ItemSubject = atomicSubject,
                ItemEnvironment = item?.Environment ?? request.Environment,
                ItemMustDo = item?.MustDo ?? string.Empty,
                ItemMustNotDo = item?.MustNotDo ?? string.Empty,
                Style = request.Style,
                Audience = request.Audience,
                Difficulty = request.Difficulty,
                ColorMode = request.ColorMode,
                DetailLevel = request.DetailLevel,
                LineWeightOrTreatment = string.IsNullOrWhiteSpace(request.LineWeight) ? request.LineTreatment : request.LineWeight,
                Background = request.Background,
                Viewpoint = request.Viewpoint,
                ConsistencyRules = request.ConsistencyRules,
                AspectRatio = request.Technical.AspectRatio,
                PixelWidth = request.Technical.PixelWidth,
                PixelHeight = request.Technical.PixelHeight,
                Dpi = request.Technical.Dpi,
                HardCriteria = HardCriteria(request),
                QualityCriteria = QualityCriteria(request.BookType)
            }
        };
    }

    private static List<string> HardCriteria(PromptEngineeringRequest request)
    {
        var criteria = new List<string>();
        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            criteria.AddRange(
            [
                "The visible primary subject must match expected.item_subject for this exact Work Unit. The series-level subject/theme must never justify showing multiple sibling subjects in the same image.",
                "Exactly one unified primary composition is allowed. A triptych, grid, contact sheet, collage, split screen, multiple panels or multiple alternatives is a HARD failure unless this exact Work Unit explicitly requests it.",
                "The visible environment must not contradict requested environment or exclusions.",
                "The image must actually be a usable coloring-book illustration, not a photograph, rendered landscape, logo, icon sheet, collage or unrelated scene.",
                "No visible text, letters, numbers, watermark, signature, UI or filename unless explicitly requested.",
                "No obviously malformed or impossible anatomy/geometry that makes the main subject unusable.",
                "All explicit MUST NOT DO constraints are hard exclusions.",
                "All item-specific overrides have priority over general series wording."
            ]);
        }
        else
        {
            criteria.AddRange(
            [
                "The visible primary subject must match expected.item_subject for this exact Work Unit.",
                "Exactly one unified primary composition is allowed unless this exact Work Unit explicitly requests a multi-panel layout.",
                "The visible environment must not contradict requested environment or exclusions.",
                "The image must serve the selected Book Type/editorial use rather than be an unrelated stock-like visual.",
                "No visible text, labels, watermark, signature or filename unless explicitly requested.",
                "No obviously malformed anatomy/geometry or duplicated critical objects that make the asset unusable.",
                "All explicit MUST NOT DO constraints are hard exclusions.",
                "All item-specific overrides have priority over general series wording."
            ]);
        }

        var style = PromptEnglishNormalizer.NormalizeProviderFacing(
            string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
                ? request.Style
                : request.RenderingStyle);
        if (!string.IsNullOrWhiteSpace(style))
        {
            criteria.Add($"STYLE MATCH IS HARD: the visible artwork must materially match the selected style '{style}'. A polished image in a different style is a HARD failure, not a taste preference.");
            if (style.Contains("kawaii", StringComparison.OrdinalIgnoreCase) || style.Contains("cartoon", StringComparison.OrdinalIgnoreCase))
                criteria.Add("For Kawaii / Cartoon, realistic natural-history rendering, engraving/etching, dense cross-hatching, photographic/anatomically literal proportions or heavy realistic texture is a HARD style mismatch. The image must visibly use cute/cartoon simplification, rounded/stylized forms, expressive friendly features and simplified proportions/details.");
        }
        return criteria;
    }

    private static List<string> QualityCriteria(string bookType)
    {
        if (string.Equals(bookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Strong recognizable silhouette and clear focal point.",
                "Professional coloring-book drawing rather than crude clipart or primitive geometric iconography.",
                "Clean coherent anatomy, pose, contour relationships and perspective appropriate to the selected style.",
                "Colorable regions and detail density appropriate to audience/difficulty.",
                "Background supports the subject without meaningless filler or visual clutter.",
                "No random floating symbols, pseudo-writing, decorative artifacts or accidental duplicated details.",
                "Within the required selected style, execution quality and aesthetic preference may be reviewed softly unless publication fitness is obviously broken.",
                "Overall page quality should be credible for a commercially published coloring book."
            ];
        }

        return
        [
            "Clear focal hierarchy and subject readability at publication size.",
            "Coherent composition rather than a literal collage of prompt keywords.",
            "Plausible anatomy/geometry/perspective for the chosen visual style.",
            "No random symbols, pseudo-writing, duplicated objects or generation artifacts.",
            "Within the required selected style, color treatment and detail should fit the selected editorial use.",
            "Overall asset quality should be credible for professional publication."
        ];
    }
}

internal interface IAiVisionValidationAdapter
{
    string ProviderId { get; }
    AiExchangeApiCapabilities Capabilities { get; }
    Task<VisionValidationResult> ValidateAsync(
        VisionValidationRequest request,
        string imagePath,
        CancellationToken cancellationToken = default);
}

internal static class AiExchangeApprovalService
{
    public static bool CanApprove(
        PreviewProject project,
        AiExchangeState state,
        Guid versionId,
        out string message)
    {
        var version = state.Versions.FirstOrDefault(v => v.VersionId == versionId);
        if (version is null)
        {
            message = "Versione non trovata.";
            return false;
        }

        var deterministic = VisualAssetValidationStore.Get(project, versionId);
        if (deterministic?.BlocksApproval == true)
        {
            message = "Approvazione bloccata dal controllo tecnico del file reale: " + deterministic.Message;
            return false;
        }

        var vision = VisionValidationStore.Get(project, versionId);
        if (vision?.BlocksApproval == true)
        {
            message = "Approvazione bloccata dal controllo Vision: " +
                      (string.IsNullOrWhiteSpace(vision.Summary) ? "il contenuto visivo non rispetta uno o più vincoli HARD." : vision.Summary);
            return false;
        }

        message = vision is null
            ? "Controlli tecnici superati; Vision non ancora eseguita. La revisione umana resta necessaria."
            : vision.OverallStatus == VisionValidationStatuses.Review
                ? "Vision richiede revisione umana: puoi approvare solo dopo aver controllato personalmente gli avvisi."
                : "Controlli tecnici e Vision non bloccano l'approvazione.";
        return true;
    }

    public static bool Approve(
        PreviewProject project,
        AiExchangeState state,
        Guid versionId,
        out string message)
    {
        if (!CanApprove(project, state, versionId, out var gate))
        {
            message = gate;
            return false;
        }

        if (!AiExchangeResultIngestor.Approve(project, state, versionId, out var approved))
        {
            message = approved;
            return false;
        }

        var vision = VisionValidationStore.Get(project, versionId);
        message = vision?.OverallStatus == VisionValidationStatuses.Review
            ? approved + " · Vision aveva richiesto revisione manuale: approvazione confermata dall'utente."
            : approved;
        return true;
    }
}
