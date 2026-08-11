using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal static class PromptEngineeringProviderIds
{
    public const string Generic = "generic";
    public const string OpenAi = "openai";
    public const string Gemini = "gemini";
    public const string Other = "other";
}

internal sealed class PromptEngineeringRequest
{
    public string BookType { get; set; } = string.Empty;
    public string ProviderId { get; set; } = PromptEngineeringProviderIds.Generic;
    public bool PreferAdvancedModel { get; set; } = true;
    public int SeriesCount { get; set; } = 1;
    public string ProjectBrief { get; set; } = string.Empty;
    public string MustDo { get; set; } = string.Empty;
    public string MustNotDo { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ConsistencyRules { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string LineWeight { get; set; } = string.Empty;
    public string Complexity { get; set; } = string.Empty;
    public string Density { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string WhiteSpace { get; set; } = string.Empty;
    public string EditorialUse { get; set; } = string.Empty;
    public string ColorMode { get; set; } = string.Empty;
    public string DetailLevel { get; set; } = string.Empty;
    public string LineTreatment { get; set; } = string.Empty;
    public string RenderingStyle { get; set; } = string.Empty;
    public string Viewpoint { get; set; } = string.Empty;
    public string CustomNotes { get; set; } = string.Empty;
    public bool ClosedAreas { get; set; }
    public bool AvoidTinyAreas { get; set; }
    public bool CleanContours { get; set; }
    public bool NoTextInsideImage { get; set; }
    public bool SubjectClearlySeparated { get; set; }
    public bool EditorialClarity { get; set; }
    public bool SameScaleWhenSeries { get; set; }
    public PromptEngineeringTechnicalSpec Technical { get; set; } = new();
    public List<PromptEngineeringItemOverride> ItemOverrides { get; set; } = [];
}

internal sealed class PromptEngineeringTechnicalSpec
{
    public string PresetId { get; set; } = string.Empty;
    public string Width { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public string ResolutionClassId { get; set; } = string.Empty;
    public string PixelWidth { get; set; } = string.Empty;
    public string PixelHeight { get; set; } = string.Empty;
    public string Dpi { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string TechnicalDetail { get; set; } = string.Empty;
}

internal sealed class PromptEngineeringItemOverride
{
    public int ItemIndex { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string MustDo { get; set; } = string.Empty;
    public string MustNotDo { get; set; } = string.Empty;
}

internal sealed class PromptPreparationSettings
{
    public string ProviderId { get; set; } = PromptEngineeringProviderIds.Generic;
    public bool PreferAdvancedModel { get; set; } = true;
}

internal static class PromptPreparationSettingsStore
{
    private const string EntityKind = "DiezPromptPreparationSettings";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static PromptPreparationSettings Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new PromptPreparationSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<PromptPreparationSettings>(entity.Notes, JsonOptions) ?? new PromptPreparationSettings();
            settings.ProviderId = NormalizeProvider(settings.ProviderId);
            return settings;
        }
        catch { return new PromptPreparationSettings(); }
    }

    public static void Save(PreviewProject project, PromptPreparationSettings settings)
    {
        settings.ProviderId = NormalizeProvider(settings.ProviderId);
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Preparazione prompt AI", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(settings, JsonOptions);
    }

    public static string NormalizeProvider(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        PromptEngineeringProviderIds.OpenAi => PromptEngineeringProviderIds.OpenAi,
        PromptEngineeringProviderIds.Gemini => PromptEngineeringProviderIds.Gemini,
        PromptEngineeringProviderIds.Other => PromptEngineeringProviderIds.Other,
        _ => PromptEngineeringProviderIds.Generic
    };
}

internal sealed class PromptMasterState
{
    public int SchemaVersion { get; set; } = 1;
    public string BookType { get; set; } = string.Empty;
    public string ProviderId { get; set; } = PromptEngineeringProviderIds.Generic;
    public bool PreferAdvancedModel { get; set; } = true;
    public int SeriesCount { get; set; } = 1;
    public string MustDo { get; set; } = string.Empty;
    public string MustNotDo { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string UpdatedAtLocal { get; set; } = string.Empty;
}

internal static class PromptMasterStateStore
{
    private const string EntityKind = "DiezPromptMasterState";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static PromptMasterState? LoadForCurrentBook(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<PromptMasterState>(entity.Notes, JsonOptions);
            if (state is null) return null;
            return string.Equals(BookTypeProfileService.Normalize(state.BookType), BookTypeProfileService.Get(project), StringComparison.OrdinalIgnoreCase)
                ? state
                : null;
        }
        catch { return null; }
    }

    public static void SaveDraft(PreviewProject project, int count, string? mustDo, string? mustNotDo, string? prompt)
    {
        var settings = PromptPreparationSettingsStore.Load(project);
        Save(project, new PromptMasterState
        {
            BookType = BookTypeProfileService.Get(project),
            ProviderId = settings.ProviderId,
            PreferAdvancedModel = settings.PreferAdvancedModel,
            SeriesCount = Math.Max(1, count),
            MustDo = mustDo ?? string.Empty,
            MustNotDo = mustNotDo ?? string.Empty,
            Prompt = prompt ?? string.Empty,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        });
    }

    public static void Save(PreviewProject project, PromptMasterState state)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Master prompt attivo", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }
}

/// <summary>
/// Canonical prompt compiler. UI controls only populate persisted project parameters; prompt power
/// lives here in stable book-type quality profiles. Adding/removing optional UI parameters therefore
/// enriches the prompt without weakening the professional core specification.
/// </summary>
internal static class PromptEngineeringEngine
{
    public const string EngineVersion = "3.0";
    private static readonly Regex ItemLine = new(
        @"^\s*(?:immagine|image)\s*(?<n>\d+)\s*[:\-–—]\s*(?<text>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static PromptEngineeringRequest BuildRequest(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        var request = new PromptEngineeringRequest
        {
            BookType = BookTypeProfileService.Get(project),
            ProviderId = PromptPreparationSettingsStore.NormalizeProvider(providerId),
            PreferAdvancedModel = preferAdvancedModel,
            SeriesCount = Math.Clamp(count, 1, 500),
            ProjectBrief = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim(),
            ConsistencyRules = (ImageCollectionWorkspaceService.GetConsistencyRules(project) ?? string.Empty).Trim(),
            Technical = ReadTechnical(project)
        };

        var must = SplitOverrides(mustDo);
        var mustNot = SplitOverrides(mustNotDo);
        request.MustDo = must.General;
        request.MustNotDo = mustNot.General;
        MergeOverrides(request.ItemOverrides, must.Overrides, (target, text) => target.MustDo = Join(target.MustDo, text));
        MergeOverrides(request.ItemOverrides, mustNot.Overrides, (target, text) => target.MustNotDo = Join(target.MustNotDo, text));

        if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            var subject = SplitOverrides(p.SubjectDescription);
            var environment = SplitOverrides(p.EnvironmentDescription);
            request.Subject = subject.General;
            request.Environment = environment.General;
            MergeOverrides(request.ItemOverrides, subject.Overrides, (target, text) => target.Subject = Join(target.Subject, text));
            MergeOverrides(request.ItemOverrides, environment.Overrides, (target, text) => target.Environment = Join(target.Environment, text));
            request.Style = p.Style;
            request.Audience = p.TargetAudience;
            request.Difficulty = p.Difficulty;
            request.LineWeight = p.LineWeight;
            request.Complexity = p.Complexity;
            request.Density = p.ElementDensity;
            request.Background = p.Background;
            request.WhiteSpace = p.WhiteSpace;
            request.CustomNotes = p.CustomStyleNotes;
            request.ClosedAreas = p.ClosedAreas;
            request.AvoidTinyAreas = p.AvoidTinyAreas;
            request.CleanContours = p.CleanContours;
            request.NoTextInsideImage = p.NoTextInsideImage;
            request.SubjectClearlySeparated = p.SubjectClearlySeparated;
        }
        else
        {
            var p = ImageCollectionPromptProfileService.Load(project);
            var subject = SplitOverrides(p.SubjectDescription);
            var environment = SplitOverrides(p.EnvironmentDescription);
            request.Subject = subject.General;
            request.Environment = environment.General;
            MergeOverrides(request.ItemOverrides, subject.Overrides, (target, text) => target.Subject = Join(target.Subject, text));
            MergeOverrides(request.ItemOverrides, environment.Overrides, (target, text) => target.Environment = Join(target.Environment, text));
            request.EditorialUse = p.EditorialUse;
            request.ColorMode = p.ColorMode;
            request.DetailLevel = p.DetailLevel;
            request.LineTreatment = p.LineTreatment;
            request.RenderingStyle = p.RenderingStyle;
            request.Background = p.Background;
            request.Viewpoint = p.Viewpoint;
            request.CustomNotes = p.Notes;
            request.SubjectClearlySeparated = p.KeepSubjectReadable;
            request.NoTextInsideImage = p.AvoidTextInsideImage;
            request.EditorialClarity = p.EditorialClarity;
            request.SameScaleWhenSeries = p.SameScaleWhenSeries;
        }

        request.ItemOverrides = request.ItemOverrides
            .Where(x => x.ItemIndex >= 1 && x.ItemIndex <= request.SeriesCount)
            .OrderBy(x => x.ItemIndex)
            .ToList();
        return request;
    }

    public static string BuildSeriesPrompt(
        PreviewProject project,
        int count,
        string? mustDo,
        string? mustNotDo,
        string? providerId,
        bool preferAdvancedModel)
    {
        var request = BuildRequest(project, count, mustDo, mustNotDo, providerId, preferAdvancedModel);
        return RenderSeries(request);
    }

    public static string BuildItemPrompt(
        PreviewProject project,
        string masterPrompt,
        int totalCount,
        int itemIndex,
        string code,
        string? providerId,
        bool preferAdvancedModel)
    {
        var masterState = PromptMasterStateStore.LoadForCurrentBook(project);
        var request = BuildRequest(
            project,
            Math.Max(1, totalCount),
            masterState?.MustDo ?? string.Empty,
            masterState?.MustNotDo ?? string.Empty,
            providerId,
            preferAdvancedModel);
        var item = request.ItemOverrides.FirstOrDefault(x => x.ItemIndex == itemIndex);
        var sb = new StringBuilder();
        sb.AppendLine((masterPrompt ?? string.Empty).Trim());
        sb.AppendLine();
        sb.AppendLine("=== DIEZ ITEM EXECUTION CONTRACT — HIGHEST PRIORITY ===");
        sb.AppendLine("Generate EXACTLY ONE image for this work unit. Do not generate a grid, contact sheet, collage, triptych, multiple alternatives, or the entire series.");
        sb.AppendLine($"Series position: item {itemIndex} of {Math.Max(1, totalCount)}.");
        sb.AppendLine($"Work-unit code: {code}. This code is metadata only; NEVER draw, print, caption, watermark, or embed it inside the image.");
        sb.AppendLine("If any series-level wording appears to request multiple images, this item execution contract overrides that wording for the current work unit.");
        sb.AppendLine("Before rendering, internally resolve all constraints, then produce one coherent final composition rather than mechanically placing checklist symbols.");

        if (item is not null)
        {
            sb.AppendLine();
            sb.AppendLine("ITEM-SPECIFIC OVERRIDES:");
            AppendIf(sb, "Subject override", item.Subject);
            AppendIf(sb, "Environment override", item.Environment);
            AppendIf(sb, "Required for this item", item.MustDo);
            AppendIf(sb, "Forbidden for this item", item.MustNotDo);
            sb.AppendLine("Item-specific overrides take precedence over the corresponding series-level fields only for this item.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("ITEM-SPECIFIC DIFFERENTIATION:");
            if (string.Equals(request.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("Choose one concrete, recognizable subject that satisfies the series theme. Keep it distinct from sibling work units and avoid repeating the same species/pose/composition unless the user explicitly requested repetition.");
            else
                sb.AppendLine("Create a distinct composition appropriate to this series position while preserving all LOCKED/consistent constraints.");
        }

        sb.AppendLine();
        sb.AppendLine("RETURN CONTRACT:");
        sb.AppendLine("- Return one final primary image asset for this work unit.");
        sb.AppendLine("- Also return a concise factual description of what is actually visible in the final image; do not describe an intended image that differs from the asset.");
        sb.AppendLine("- If a hard constraint cannot be satisfied, mark the item INCOMPLETE/FAILED rather than silently substituting a different visual concept.");
        return sb.ToString().Trim();
    }

    public static string RenderSeries(PromptEngineeringRequest r)
    {
        var sb = new StringBuilder();
        AppendProviderPreamble(sb, r);
        sb.AppendLine($"DIEZ PROMPT ENGINEERING SPECIFICATION v{EngineVersion}");
        sb.AppendLine();
        sb.AppendLine("ROLE AND EXECUTION MODEL");
        sb.AppendLine("Act as a senior commercial image-generation art director and production illustrator. Translate the specification into a polished, publication-ready image, not a literal arrangement of prompt tokens.");
        sb.AppendLine($"This master specification describes a series of {r.SeriesCount} image asset{(r.SeriesCount == 1 ? string.Empty : "s")}. It is a shared specification, NOT an instruction to render the whole series in one image.");
        sb.AppendLine("When Diez appends an ITEM EXECUTION CONTRACT, generate exactly the single asset requested by that contract.");
        sb.AppendLine("Hard constraints are non-negotiable. Optional user parameters enrich the specification; missing optional parameters must NEVER weaken the professional quality baseline defined below.");
        sb.AppendLine();

        if (string.Equals(r.BookType, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            RenderColoring(sb, r);
        else
            RenderIllustration(sb, r);

        RenderShared(sb, r);
        return sb.ToString().Trim();
    }

    private static void RenderColoring(StringBuilder sb, PromptEngineeringRequest r)
    {
        sb.AppendLine("BOOK-TYPE MISSION — COMMERCIAL COLORING BOOK");
        sb.AppendLine("Create a genuinely colorable, professionally illustrated page suitable for a commercial children's/adult coloring book according to the selected audience and difficulty. The result must look intentionally drawn by a skilled coloring-book illustrator, not like clip-art, an icon sheet, a diagram, a logo, or a crude geometric approximation.");
        sb.AppendLine();
        sb.AppendLine("CONTENT INTENT");
        AppendField(sb, "General subject/theme", r.Subject, "Select a concrete, recognizable subject consistent with the user's theme and the current item position.");
        AppendField(sb, "General environment/scenario", r.Environment, "Use only a contextually meaningful environment; keep it subordinate to the main subject.");
        AppendField(sb, "User MUST DO", r.MustDo, "No additional user MUST DO text was provided.");
        AppendField(sb, "User MUST NOT DO", r.MustNotDo, "No additional user exclusion was provided; all Diez hard exclusions below still apply.");
        if (!string.IsNullOrWhiteSpace(r.ProjectBrief)) AppendField(sb, "Project brief", r.ProjectBrief, string.Empty);
        sb.AppendLine();

        sb.AppendLine("HARD COLORING CONSTRAINTS — NON-NEGOTIABLE");
        sb.AppendLine("- Final raster must contain only pure black #000000 and pure white #FFFFFF. No gray pixels, grayscale, antialiasing gray, color, gradients, shadows, glow, transparency effects, halftones, tonal textures, or intermediate values in the final deliverable.");
        sb.AppendLine("- White background; black line work. If the model renders antialiasing internally, threshold/binarize the final deliverable to pure black/white.");
        sb.AppendLine("- No photorealism, photographic lighting, cinematic rendering, 3D render look, painterly shading, charcoal, pencil gray, watercolor, or filled tonal modeling.");
        sb.AppendLine("- Use clean, intentional, continuous line art with smooth organic curves. Avoid broken contours, doubled lines, accidental tangencies, line collisions, dangling strokes, malformed geometry, and mechanically repeated decorative marks.");
        sb.AppendLine("- Preserve recognizable anatomy/structure for animals, people and objects. Limbs, faces, paws/hands, tails, ears, joints and perspective must be coherent and visually plausible for the selected style.");
        sb.AppendLine("- Prefer colorable CLOSED regions. Avoid confusing overlaps and micro-cells. Do not use large solid-black masses when the same feature can be represented as an outlined region available for coloring.");
        sb.AppendLine("- Do not add random floating diamonds, bars, symbols, confetti, abstract filler, pseudo-text, decorative glyphs or unrelated motifs merely to fill empty space.");
        sb.AppendLine("- Do not draw text, letters, numbers, labels, signatures, watermarks, logos, UI elements, prompt fragments, IDs or file names.");
        sb.AppendLine();

        sb.AppendLine("PROFESSIONAL QUALITY GATE");
        sb.AppendLine("- Strong silhouette and immediate subject recognition at thumbnail size.");
        sb.AppendLine("- Balanced composition with an intentional focal point; no awkward empty zones and no meaningless filler.");
        sb.AppendLine("- Main subject must feel expressive and appealing for the selected audience without becoming an emoji/icon caricature unless that style is explicitly requested.");
        sb.AppendLine("- Background elements must support the scene and theme, remain secondary, and use the same line language as the main subject.");
        sb.AppendLine("- Every visible element must have a semantic reason to exist. Remove artifacts and decorative clutter that do not improve story, recognition or colorability.");
        sb.AppendLine("- Treat the result as a page a parent would expect to find in a professionally published coloring book, not as a quick draft.");
        sb.AppendLine();

        sb.AppendLine("COLORING STYLE PROFILE");
        AppendField(sb, "Style", r.Style, "Clean professional coloring-book line art");
        AppendField(sb, "Audience", r.Audience, "General audience");
        AppendField(sb, "Difficulty", r.Difficulty, "Medium");
        AppendField(sb, "Line weight", r.LineWeight, "Medium, clean, consistent");
        AppendField(sb, "Visual complexity", r.Complexity, "Medium");
        AppendField(sb, "Element density", r.Density, "Low to medium");
        AppendField(sb, "Background", r.Background, "Simple and contextually meaningful");
        AppendField(sb, "White space", r.WhiteSpace, "Balanced");
        foreach (var rule in ColoringStyleRules(r)) sb.AppendLine("- " + rule);
        if (r.ClosedAreas) sb.AppendLine("- Favor clearly closed shapes that can be filled comfortably by hand.");
        if (r.AvoidTinyAreas) sb.AppendLine("- Avoid tiny enclosed areas and micro-details unsuitable for the selected audience/difficulty.");
        if (r.CleanContours) sb.AppendLine("- Contours must be clean, continuous and print-legible throughout the page.");
        if (r.SubjectClearlySeparated) sb.AppendLine("- Keep the main subject clearly separated from the background and readable at reduced size.");
        if (!string.IsNullOrWhiteSpace(r.CustomNotes)) AppendField(sb, "User style notes", r.CustomNotes, string.Empty);
        sb.AppendLine();

        sb.AppendLine("NEGATIVE VISUAL CONSTRAINTS");
        sb.AppendLine("Avoid: low-effort clipart; primitive iconography; crude geometric body construction; malformed anatomy; duplicated body parts; random symbols; floating decoration; pseudo-writing; heavy black blobs; excessive solid fills; gray antialiasing; shadows; photorealistic backgrounds; scenic photography; sunset photography; glossy 3D; stock-vector look; logo-like composition; collage; contact sheet; multiple panels unless explicitly requested.");
        sb.AppendLine();
    }

    private static void RenderIllustration(StringBuilder sb, PromptEngineeringRequest r)
    {
        var illustrated = string.Equals(r.BookType, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase);
        sb.AppendLine(illustrated
            ? "BOOK-TYPE MISSION — ILLUSTRATED BOOK"
            : "BOOK-TYPE MISSION — IMAGE COLLECTION / EDITORIAL VISUAL SERIES");
        sb.AppendLine(illustrated
            ? "Create a publication-ready illustration that supports the book's narrative/editorial content. It must add meaning, atmosphere or explanation rather than behave like an unrelated stock image."
            : "Create a publication-ready image that serves the selected editorial use. Visual choices must support comprehension, consistency and intended use rather than decorative novelty alone.");
        sb.AppendLine();
        sb.AppendLine("CONTENT INTENT");
        AppendField(sb, "General subject/theme", r.Subject, "Select a concrete subject that serves the editorial use.");
        AppendField(sb, "General environment/scenario", r.Environment, "Choose an environment that supports the subject without introducing irrelevant content.");
        AppendField(sb, "User MUST DO", r.MustDo, "No additional user MUST DO text was provided.");
        AppendField(sb, "User MUST NOT DO", r.MustNotDo, "No additional user exclusion was provided.");
        if (!string.IsNullOrWhiteSpace(r.ProjectBrief)) AppendField(sb, "Project brief", r.ProjectBrief, string.Empty);
        sb.AppendLine();

        sb.AppendLine("VISUAL / EDITORIAL PROFILE");
        AppendField(sb, "Editorial use", r.EditorialUse, illustrated ? "Narrative/editorial illustration" : "Editorial image series");
        AppendField(sb, "Color treatment", r.ColorMode, "Use the most appropriate professional color treatment for the selected use");
        AppendField(sb, "Detail level", r.DetailLevel, "Medium");
        AppendField(sb, "Line/edge treatment", r.LineTreatment, "Appropriate to the selected rendering style");
        AppendField(sb, "Rendering style", r.RenderingStyle, "Clear professional illustration");
        AppendField(sb, "Background", r.Background, "Contextually appropriate");
        AppendField(sb, "Viewpoint", r.Viewpoint, "Choose the viewpoint that best communicates the subject");
        if (!string.IsNullOrWhiteSpace(r.CustomNotes)) AppendField(sb, "User notes", r.CustomNotes, string.Empty);
        sb.AppendLine();

        sb.AppendLine("PROFESSIONAL QUALITY GATE");
        sb.AppendLine("- Produce a coherent final composition, not a literal collage of keywords.");
        sb.AppendLine("- Maintain plausible geometry/anatomy/perspective for the chosen visual style.");
        sb.AppendLine("- Avoid accidental artifacts, duplicated objects, malformed hands/limbs, impossible overlaps, pseudo-text and meaningless decorative filler.");
        sb.AppendLine("- Ensure clear focal hierarchy, clean edges and sufficient local contrast for the intended publication size.");
        if (r.SubjectClearlySeparated) sb.AppendLine("- Keep the principal subject immediately readable and distinguishable from the background.");
        if (r.NoTextInsideImage) sb.AppendLine("- Do not insert text, labels, captions, IDs or watermarks inside the image unless the user explicitly requires them.");
        if (r.EditorialClarity) sb.AppendLine("- Prefer editorial clarity and communicative value over ornamental complexity.");
        if (r.SameScaleWhenSeries) sb.AppendLine("- For comparable series items, preserve useful scale/viewpoint continuity unless an item-specific instruction requires a change.");
        sb.AppendLine();

        sb.AppendLine("NEGATIVE VISUAL CONSTRAINTS");
        sb.AppendLine("Avoid: irrelevant stock-photo clichés; visual artifacts; random symbols; pseudo-writing; accidental logos/watermarks; duplicate subjects; inconsistent style changes; arbitrary color shifts; malformed anatomy; nonsensical geometry; clutter that competes with the editorial subject.");
        sb.AppendLine();
    }

    private static void RenderShared(StringBuilder sb, PromptEngineeringRequest r)
    {
        sb.AppendLine("SERIES CONSISTENCY AND CONTROL");
        if (string.IsNullOrWhiteSpace(r.ConsistencyRules))
            sb.AppendLine("- No explicit Consistent rules are active. Keep only the visual consistency naturally implied by the selected book profile; allow meaningful variation between items.");
        else
        {
            sb.AppendLine("- Apply the following Diez consistency rules exactly according to their LOCKED / PREFERRED / FREE and USER / AI / MIXED semantics:");
            foreach (var line in Lines(r.ConsistencyRules)) sb.AppendLine("  - " + line);
        }
        sb.AppendLine("- Series consistency must never force the same pose/composition repeatedly unless the user explicitly locked it.");
        sb.AppendLine();

        sb.AppendLine("TECHNICAL OUTPUT SPECIFICATION");
        AppendField(sb, "Page/trim preset", r.Technical.PresetId, "Use the currently selected Diez page format");
        if (!string.IsNullOrWhiteSpace(r.Technical.Width) && !string.IsNullOrWhiteSpace(r.Technical.Height))
            sb.AppendLine($"- Page dimensions: {r.Technical.Width} × {r.Technical.Height} {r.Technical.Unit}. This is page context, not permission to distort the image.");
        AppendField(sb, "Image aspect ratio", r.Technical.AspectRatio, "Preserve the selected ratio without stretching or geometric deformation");
        AppendField(sb, "Resolution class", ResolutionLabel(r.Technical.ResolutionClassId), "Use the highest practical quality supported by the platform");
        if (!string.IsNullOrWhiteSpace(r.Technical.PixelWidth) && !string.IsNullOrWhiteSpace(r.Technical.PixelHeight))
            sb.AppendLine($"- Target raster: {r.Technical.PixelWidth} × {r.Technical.PixelHeight} px. Preserve aspect ratio; do not stretch to page trim.");
        if (!string.IsNullOrWhiteSpace(r.Technical.Dpi)) sb.AppendLine($"- Print target metadata/context: {r.Technical.Dpi} DPI.");
        AppendField(sb, "Rendering quality", r.Technical.Quality, "Publication quality");
        AppendField(sb, "Technical detail", r.Technical.TechnicalDetail, "Appropriate to the selected visual profile");
        sb.AppendLine("- Bleed, safety margins and final page imposition are layout-stage concerns and are intentionally excluded from image-generation instructions.");
        sb.AppendLine();

        sb.AppendLine("FAIL-SAFE / SELF-CHECK BEFORE RETURNING");
        sb.AppendLine("Silently inspect the final asset before returning it. Verify subject correctness, item-specific overrides, book-type hard constraints, visual quality, technical aspect ratio, and prohibited-content rules. If a hard requirement is violated, correct/regenerate the asset instead of describing it as compliant.");
    }

    private static void AppendProviderPreamble(StringBuilder sb, PromptEngineeringRequest r)
    {
        switch (r.ProviderId)
        {
            case PromptEngineeringProviderIds.OpenAi:
                sb.AppendLine("TARGET RENDERER: OPENAI IMAGE GENERATION");
                sb.AppendLine(r.PreferAdvancedModel
                    ? "Use the highest-quality OpenAI image-generation capability available in the current environment."
                    : "Use the OpenAI image-generation capability selected by the environment.");
                sb.AppendLine("Follow the hierarchy and hard constraints literally, while using visual judgment to make the final composition natural and professionally illustrated. Do not turn bullet points into visible symbols or text.");
                break;
            case PromptEngineeringProviderIds.Gemini:
                sb.AppendLine("TARGET RENDERER: GEMINI IMAGE GENERATION");
                sb.AppendLine(r.PreferAdvancedModel
                    ? "Use the highest-quality Gemini image-generation capability available in the current environment."
                    : "Use the Gemini image-generation capability selected by the environment.");
                sb.AppendLine("Resolve the specification hierarchically: item-specific instructions > hard book constraints > consistency rules > style preferences > creative freedom. Render a single coherent scene, not a checklist visualization.");
                break;
            case PromptEngineeringProviderIds.Other:
                sb.AppendLine("TARGET RENDERER: OTHER / USER-SELECTED IMAGE MODEL");
                sb.AppendLine("Use the strongest image-generation model available on the selected platform. This prompt is deliberately model-agnostic: interpret section headings as instruction priority, not text to render.");
                sb.AppendLine("Hard constraints are mandatory; satisfy them before exercising style or composition freedom.");
                break;
            default:
                sb.AppendLine("TARGET RENDERER: MODEL-AGNOSTIC IMAGE GENERATION");
                sb.AppendLine("Use a capable image-generation model. Treat the specification as a professional production brief with explicit priority and quality gates.");
                break;
        }
        sb.AppendLine();
    }

    private static IEnumerable<string> ColoringStyleRules(PromptEngineeringRequest r)
    {
        var style = r.Style ?? string.Empty;
        if (style.Contains("Bold & Easy", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Bold & Easy: large organic colorable regions, low visual clutter, clear friendly expression and strong silhouette; avoid icon-like geometric simplification.";
            yield return "Use a strong primary contour and fewer, lighter internal details; keep the number of major color regions manageable for the selected child audience.";
        }
        else if (style.Contains("Line Art dettagliata", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Detailed line art: richer meaningful detail with fine but fully black continuous lines; preserve colorability and visual hierarchy instead of filling the page with texture noise.";
            yield return "Fine internal detail must not create gray hatching, shading, tangled line crossings or tiny unusable cells.";
        }
        else if (style.Contains("Line Art", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Clean line art: confident professional contour drawing, consistent curves and deliberate line hierarchy; no painterly rendering.";
        }
        else if (style.Contains("Kawaii", StringComparison.OrdinalIgnoreCase) || style.Contains("Cartoon", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Kawaii/cartoon: appealing simplified proportions and expressive face, while preserving coherent anatomy and avoiding emoji/icon construction.";
        }
        else if (style.Contains("Mandala", StringComparison.OrdinalIgnoreCase) || style.Contains("Pattern", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Mandala/pattern: intentional rhythm or symmetry with clean closed cells; decorative structure must remain thematically coherent and comfortably colorable.";
        }
        else if (style.Contains("realistico", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Simplified realism: credible anatomy/proportions translated into clean coloring-book contours; omit photographic texture, tonal modeling and unnecessary micro-detail.";
        }

        if ((r.LineWeight ?? string.Empty).StartsWith("Molto spesso", StringComparison.OrdinalIgnoreCase))
            yield return "Line-weight interpretation: very bold primary contours with minimal internal line detail.";
        else if ((r.LineWeight ?? string.Empty).StartsWith("Spesso", StringComparison.OrdinalIgnoreCase))
            yield return "Line-weight interpretation: bold, confident primary contours; internal detail slightly lighter but still clearly black.";
        else if ((r.LineWeight ?? string.Empty).StartsWith("Molto sottile", StringComparison.OrdinalIgnoreCase))
            yield return "Line-weight interpretation: very fine but crisp continuous black lines; maintain print legibility and never simulate fineness with gray.";
        else if ((r.LineWeight ?? string.Empty).StartsWith("Sottile", StringComparison.OrdinalIgnoreCase))
            yield return "Line-weight interpretation: fine, crisp black lines suitable for detail; keep primary silhouette stronger than minor internal detail.";
        else if ((r.LineWeight ?? string.Empty).StartsWith("Variabile", StringComparison.OrdinalIgnoreCase))
            yield return "Line-weight interpretation: deliberate hierarchy—stronger outer silhouette, finer internal details, no accidental thickness changes.";
        else
            yield return "Line-weight interpretation: medium, clean and consistent with a slightly stronger outer silhouette.";
    }

    private static PromptEngineeringTechnicalSpec ReadTechnical(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, "DiezImageGenerationSpecs", StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new PromptEngineeringTechnicalSpec();
        try
        {
            using var doc = JsonDocument.Parse(entity.Notes);
            var root = doc.RootElement;
            return new PromptEngineeringTechnicalSpec
            {
                PresetId = Value(root, "PresetId"),
                Width = Value(root, "Width"),
                Height = Value(root, "Height"),
                Unit = Value(root, "Unit"),
                AspectRatio = Value(root, "AspectRatio"),
                ResolutionClassId = Value(root, "ResolutionClassId"),
                PixelWidth = Value(root, "PixelWidth"),
                PixelHeight = Value(root, "PixelHeight"),
                Dpi = Value(root, "Dpi"),
                Quality = Value(root, "Quality"),
                TechnicalDetail = Value(root, "LineDetail")
            };
        }
        catch { return new PromptEngineeringTechnicalSpec(); }
    }

    private static string Value(JsonElement root, string name)
    {
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString();
        return string.Empty;
    }

    private static (string General, Dictionary<int, string> Overrides) SplitOverrides(string? text)
    {
        var general = new List<string>();
        var overrides = new Dictionary<int, string>();
        foreach (var raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var match = ItemLine.Match(line);
            if (match.Success && int.TryParse(match.Groups["n"].Value, out var n))
                overrides[n] = Join(overrides.TryGetValue(n, out var old) ? old : string.Empty, match.Groups["text"].Value.Trim());
            else
                general.Add(line);
        }
        return (string.Join(Environment.NewLine, general), overrides);
    }

    private static void MergeOverrides(
        List<PromptEngineeringItemOverride> target,
        Dictionary<int, string> values,
        Action<PromptEngineeringItemOverride, string> apply)
    {
        foreach (var pair in values)
        {
            var item = target.FirstOrDefault(x => x.ItemIndex == pair.Key);
            if (item is null)
            {
                item = new PromptEngineeringItemOverride { ItemIndex = pair.Key };
                target.Add(item);
            }
            apply(item, pair.Value);
        }
    }

    private static string Join(string? a, string? b)
    {
        var left = (a ?? string.Empty).Trim();
        var right = (b ?? string.Empty).Trim();
        if (left.Length == 0) return right;
        if (right.Length == 0) return left;
        return left + Environment.NewLine + right;
    }

    private static void AppendField(StringBuilder sb, string label, string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) text = fallback;
        if (text.Length > 0) sb.AppendLine($"- {label}: {text}");
    }

    private static void AppendIf(StringBuilder sb, string label, string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length > 0) sb.AppendLine($"- {label}: {text}");
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static string ResolutionLabel(string? id) => (id ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "hd" => "HD — long side 1280 px",
        "fhd" or "fullhd" => "Full HD — long side 1920 px",
        "2k" => "2K — long side 2560 px",
        "4k" => "4K UHD — long side 3840 px",
        "8k" => "8K UHD — long side 7680 px",
        "print" or "print300" => "Print — physical dimensions × DPI",
        "custom" => "Custom — explicit pixel dimensions",
        { Length: > 0 } value => value,
        _ => string.Empty
    };
}
