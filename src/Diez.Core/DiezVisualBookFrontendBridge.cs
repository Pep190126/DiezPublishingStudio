using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezColoringProfileDto(
    string Style,
    bool BoldEasy,
    bool Cozy,
    string TargetAudience,
    string Difficulty,
    string LineWeight,
    string Complexity,
    string ElementDensity,
    string Background,
    string WhiteSpace,
    bool ClosedAreas,
    bool AvoidTinyAreas,
    bool CleanContours,
    bool NoTextInsideImage,
    bool SubjectClearlySeparated,
    string Notes);

public sealed record DiezImageProfileDto(
    string EditorialUse,
    string ColorMode,
    string DetailLevel,
    string LineTreatment,
    string RenderingStyle,
    string Background,
    string Viewpoint,
    bool KeepSubjectReadable,
    bool AvoidTextInsideImage,
    bool EditorialClarity,
    bool SameScaleWhenSeries,
    string Notes);

public sealed record DiezVisualBookSetupDto(
    string BookType,
    int ImageCount,
    string Subject,
    string Environment,
    bool Consistent,
    string ConsistencyRules,
    DiezColoringProfileDto? Coloring,
    DiezImageProfileDto? Image);

public sealed record DiezVisualBookMutation(string ProjectJson, string Status, string Message, DiezVisualBookSetupDto Setup);

public sealed record DiezVisualPromptItem(int Position, string Code, string Title, string Prompt);

public sealed record DiezVisualPromptPack(
    string ProjectJson,
    string MasterPrompt,
    IReadOnlyList<DiezVisualPromptItem> Items);

public sealed record DiezVisualBookProgress(
    string BookType,
    int ExpectedImages,
    int ImageJobs,
    int AppliedImages,
    int DistinctAppliedMaterials,
    bool ReadyForPublication,
    IReadOnlyList<string> Problems);

/// <summary>
/// UI-neutral contract for Coloring Book, Raccolta immagini and Libro illustrato.
/// The frontend never owns production quantity/profile state: it reads and writes the canonical Core model.
/// </summary>
public static class DiezVisualBookFrontendBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezVisualBookSetupDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return ProjectSetup(project);
    }

    public static DiezVisualBookMutation SaveColoring(
        string projectJson,
        int imageCount,
        string? subject,
        string? environment,
        bool consistent,
        string? consistencyRules,
        DiezColoringProfileDto profile)
    {
        var (root, project) = Parse(projectJson);
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        VisualBookPlanService.Save(project, imageCount, consistent);
        ImageCollectionWorkspaceService.SetConsistencyRules(project, consistent ? consistencyRules : string.Empty);

        var p = BookTypePromptProfileService.LoadColoring(project);
        p.SubjectDescription = (subject ?? string.Empty).Trim();
        p.EnvironmentDescription = (environment ?? string.Empty).Trim();
        p.Style = profile.Style;
        p.BoldEasy = profile.BoldEasy;
        p.TargetAudience = profile.TargetAudience;
        p.Difficulty = profile.Difficulty;
        p.LineWeight = profile.LineWeight;
        p.Complexity = profile.Complexity;
        p.ElementDensity = profile.ElementDensity;
        p.Background = profile.Background;
        p.WhiteSpace = profile.WhiteSpace;
        p.ClosedAreas = profile.ClosedAreas;
        p.AvoidTinyAreas = profile.AvoidTinyAreas;
        p.CleanContours = profile.CleanContours;
        p.BlackAndWhiteOnly = true;
        p.NoGray = true;
        p.NoShadows = true;
        p.NoTextInsideImage = profile.NoTextInsideImage;
        p.SubjectClearlySeparated = profile.SubjectClearlySeparated;
        p.CustomStyleNotes = profile.Notes ?? string.Empty;
        BookTypePromptProfileService.SaveColoring(project, p);
        ColoringIndependentHardProfileService.PersistResolvedState(
            project,
            profile.Style,
            profile.LineWeight,
            profile.BoldEasy,
            profile.Cozy);

        MergeProject(root, project);
        var setup = ProjectSetup(project);
        return new DiezVisualBookMutation(Write(root), "SAVED", "Piano Coloring salvato nel Core.", setup);
    }

    public static DiezVisualBookMutation SaveImageBook(
        string projectJson,
        string bookType,
        int imageCount,
        string? subject,
        string? environment,
        bool consistent,
        string? consistencyRules,
        DiezImageProfileDto profile)
    {
        var normalized = BookTypeProfileService.Normalize(bookType);
        if (!string.Equals(normalized, BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Il tipo deve essere Raccolta immagini o Libro illustrato.", nameof(bookType));

        var (root, project) = Parse(projectJson);
        BookTypeProfileService.Set(project, normalized);
        VisualBookPlanService.Save(project, imageCount, consistent);
        ImageCollectionWorkspaceService.SetConsistencyRules(project, consistent ? consistencyRules : string.Empty);

        ImageCollectionPromptProfileService.Save(project, new ImageCollectionPromptProfileService.Profile
        {
            SubjectDescription = (subject ?? string.Empty).Trim(),
            EnvironmentDescription = (environment ?? string.Empty).Trim(),
            EditorialUse = profile.EditorialUse,
            ColorMode = profile.ColorMode,
            DetailLevel = profile.DetailLevel,
            LineTreatment = profile.LineTreatment,
            RenderingStyle = profile.RenderingStyle,
            Background = profile.Background,
            Viewpoint = profile.Viewpoint,
            KeepSubjectReadable = profile.KeepSubjectReadable,
            AvoidTextInsideImage = profile.AvoidTextInsideImage,
            EditorialClarity = profile.EditorialClarity,
            SameScaleWhenSeries = profile.SameScaleWhenSeries,
            Notes = profile.Notes ?? string.Empty
        });

        MergeProject(root, project);
        var setup = ProjectSetup(project);
        return new DiezVisualBookMutation(Write(root), "SAVED", "Piano immagini salvato nel Core.", setup);
    }

    public static DiezVisualPromptPack BuildPromptPack(
        string projectJson,
        string? mustDo = null,
        string? mustNotDo = null,
        string providerId = "generic",
        bool preferAdvancedModel = true)
    {
        var (root, project) = Parse(projectJson);
        if (!VisualBookPlanService.IsVisualFamily(project))
            throw new InvalidOperationException("Il progetto non è un libro con immagini.");

        var plan = VisualBookPlanService.Load(project);
        var master = PromptEngineeringEngine.BuildSeriesPrompt(
            project,
            plan.ImageCount,
            mustDo,
            mustNotDo,
            providerId,
            preferAdvancedModel);
        PromptMasterStateStore.SaveDraft(project, plan.ImageCount, mustDo, mustNotDo, master);

        var items = new List<DiezVisualPromptItem>();
        for (var position = 1; position <= plan.ImageCount; position++)
        {
            var code = $"IMG-{position:D3}";
            var source = PromptEngineeringEngine.BuildItemPrompt(
                project,
                master,
                plan.ImageCount,
                position,
                code,
                providerId,
                preferAdvancedModel);
            var prompt = PromptPackRendererVisualBriefService.Build(source);
            items.Add(new DiezVisualPromptItem(position, code, $"Immagine {position:D3}", prompt));
        }

        MergeProject(root, project);
        return new DiezVisualPromptPack(Write(root), master, items);
    }

    public static DiezVisualBookProgress Progress(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        var plan = VisualBookPlanService.Load(project);
        var jobs = project.AiProductionJobs.Count(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase));
        var applied = VisualBookPlanService.AppliedImageJobs(project).Count;
        var materials = VisualBookPlanService.AppliedImageMaterials(project).Count;
        var problems = VisualBookPlanService.ProductionProblems(project).ToList();
        return new DiezVisualBookProgress(
            BookTypeProfileService.Get(project),
            plan.ImageCount,
            jobs,
            applied,
            materials,
            problems.Count == 0,
            problems);
    }

    private static DiezVisualBookSetupDto ProjectSetup(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var plan = VisualBookPlanService.Load(project);
        var rules = ImageCollectionWorkspaceService.GetConsistencyRules(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            var hard = ColoringIndependentHardProfileService.Resolve(project);
            return new DiezVisualBookSetupDto(
                type,
                plan.ImageCount,
                p.SubjectDescription,
                p.EnvironmentDescription,
                plan.Consistent,
                rules,
                new DiezColoringProfileDto(
                    hard.Style,
                    hard.BoldEasy,
                    hard.Cozy,
                    p.TargetAudience,
                    p.Difficulty,
                    hard.LineWeight,
                    p.Complexity,
                    p.ElementDensity,
                    p.Background,
                    p.WhiteSpace,
                    p.ClosedAreas,
                    p.AvoidTinyAreas,
                    p.CleanContours,
                    p.NoTextInsideImage,
                    p.SubjectClearlySeparated,
                    p.CustomStyleNotes),
                null);
        }

        var image = ImageCollectionPromptProfileService.Load(project);
        return new DiezVisualBookSetupDto(
            type,
            plan.ImageCount,
            image.SubjectDescription,
            image.EnvironmentDescription,
            plan.Consistent,
            rules,
            null,
            new DiezImageProfileDto(
                image.EditorialUse,
                image.ColorMode,
                image.DetailLevel,
                image.LineTreatment,
                image.RenderingStyle,
                image.Background,
                image.Viewpoint,
                image.KeepSubjectReadable,
                image.AvoidTextInsideImage,
                image.EditorialClarity,
                image.SameScaleWhenSeries,
                image.Notes));
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
        MergeArray(root, "Entities", project.Entities, "EntityId");
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
