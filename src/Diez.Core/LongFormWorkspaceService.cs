namespace DiezPublishingStudio;

internal sealed record LongFormEntityItem(Guid EntityId, string Kind, string Name, string Notes);

internal sealed record LongFormWorkspaceSnapshot(
    string BookType,
    bool IsIllustrated,
    bool StructureIsKnown,
    IReadOnlyList<MaterialEntry> TextMaterials,
    IReadOnlyList<MaterialEntry> ImageMaterials,
    IReadOnlyList<LongFormEntityItem> Characters,
    IReadOnlyList<LongFormEntityItem> Places,
    IReadOnlyList<LongFormEntityItem> Events,
    IReadOnlyList<LongFormEntityItem> Threads,
    IReadOnlyList<ContentNode> Chapters,
    IReadOnlyList<ContentNode> Scenes,
    IReadOnlyList<ConsistencyIssue> OpenIssues,
    IReadOnlyList<ConsistencyIssue> Contradictions,
    IReadOnlyList<AiProductionJob> AiJobs,
    IReadOnlyList<IllustrationPlacement> IllustrationPlacements)
{
    public int TextMaterialCount => TextMaterials.Count;
    public int ImageMaterialCount => ImageMaterials.Count;
    public int ChapterCount => Chapters.Count;
    public int SceneCount => Scenes.Count;
}

/// <summary>
/// UI-neutral long-form editorial view of a Diez project. Novel/story,
/// essay/manual and illustrated-book frontends consume the same shared project
/// state while retaining book-type-specific options through BookTypeAiOptionsCoreService.
/// </summary>
internal static class LongFormWorkspaceService
{
    private static readonly HashSet<string> CharacterKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Character", "Person", "Persona", "Personaggio"
    };

    private static readonly HashSet<string> PlaceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Place", "Location", "Luogo", "Ambientazione"
    };

    private static readonly HashSet<string> EventKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Event", "Evento"
    };

    private static readonly HashSet<string> ThreadKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlotThread", "Thread", "StoryArc", "Arc", "FiloNarrativo"
    };

    public static bool Supports(string? bookType)
    {
        var type = BookTypeProfileService.Normalize(bookType);
        return string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, BookTypeProfileService.EssayManual, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase);
    }

    public static LongFormWorkspaceSnapshot Build(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        if (!Supports(type))
            throw new InvalidOperationException($"Il tipo libro '{type}' non appartiene alla famiglia long-form.");

        var textMaterials = project.Materials
            .Where(m => !IllustrationPlanService.IsImage(m))
            .OrderBy(m => m.ImportedAtLocal, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var imageMaterials = project.Materials
            .Where(IllustrationPlanService.IsImage)
            .OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chapters = project.ContentNodes
            .Where(n => string.Equals(n.Kind, "Chapter", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Ordinal)
            .ToList();
        var scenes = project.ContentNodes
            .Where(n => string.Equals(n.Kind, "Scene", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.Ordinal)
            .ToList();
        var openIssues = project.ConsistencyIssues
            .Where(i => string.Equals(i.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var contradictions = openIssues
            .Where(i => (i.Code ?? string.Empty).Contains("contrad", StringComparison.OrdinalIgnoreCase) ||
                        (i.Message ?? string.Empty).Contains("contrad", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new LongFormWorkspaceSnapshot(
            type,
            string.Equals(type, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase),
            BookTypeAiOptionsCoreService.StructureIsKnown(project),
            textMaterials,
            imageMaterials,
            Entities(project, CharacterKinds),
            Entities(project, PlaceKinds),
            Entities(project, EventKinds),
            Entities(project, ThreadKinds),
            chapters,
            scenes,
            openIssues,
            contradictions,
            project.AiProductionJobs.OrderBy(j => j.CreatedAtLocal, StringComparer.OrdinalIgnoreCase).ToList(),
            project.IllustrationPlacements.OrderBy(p => p.Ordinal).ToList());
    }

    public static void SetStructureDecision(PreviewProject project, bool known)
    {
        EnsureSupported(project);
        BookTypeAiOptionsCoreService.SetStructureDecision(project, known);
    }

    public static string AutomaticStructureStatus(PreviewProject project)
    {
        var snapshot = Build(project);
        var current = snapshot.ChapterCount > 0 || snapshot.SceneCount > 0
            ? $"Struttura attualmente riconosciuta: {snapshot.ChapterCount} capitoli · {snapshot.SceneCount} scene."
            : "La struttura non è ancora stata proposta.";
        if (snapshot.IsIllustrated) current += $" Immagini presenti: {snapshot.ImageMaterialCount}.";
        return $"Manoscritti/materiali testuali presenti: {snapshot.TextMaterialCount}. {current}";
    }

    private static IReadOnlyList<LongFormEntityItem> Entities(PreviewProject project, HashSet<string> kinds) =>
        project.Entities
            .Where(e => kinds.Contains(e.Kind ?? string.Empty))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new LongFormEntityItem(e.EntityId, e.Kind ?? string.Empty, e.Name ?? string.Empty, e.Notes ?? string.Empty))
            .ToList();

    private static void EnsureSupported(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        if (!Supports(type))
            throw new InvalidOperationException($"Il tipo libro '{type}' non appartiene alla famiglia long-form.");
    }
}
