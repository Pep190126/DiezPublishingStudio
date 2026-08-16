using System.Text.Json;

namespace DiezPublishingStudio;

public sealed record DiezLongFormCheck(string Code, string Severity, bool Passed, string Message);

public sealed record DiezLongFormFinalizationState(
    bool EditoriallyReady,
    string BookType,
    int EditableContentCount,
    int ChapterCount,
    int SceneCount,
    int EmptyContentCount,
    int BlockingConsistencyIssues,
    int ActiveRevisionProposals,
    IReadOnlyList<DiezLongFormCheck> Checks);

/// <summary>
/// Family-level editorial gate for Novel/Story and Essay/Manual.
/// It deliberately avoids enforcing indicative targets (chapter/page/word counts) as hard rules.
/// Generic Edition Freeze / Preflight / Publication Candidate remain the final publication authority.
/// </summary>
public static class DiezLongFormFinalizationBridge
{
    public static DiezLongFormFinalizationState Readiness(string projectJson)
    {
        var project = Parse(projectJson);
        return Readiness(project);
    }

    internal static DiezLongFormFinalizationState Readiness(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        var supported = string.Equals(type, BookTypeProfileService.Novel, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type, BookTypeProfileService.EssayManual, StringComparison.OrdinalIgnoreCase);

        var editable = project.ContentNodes.Where(node => EditableMasterService.CanEdit(project, node)).ToList();
        var empty = editable.Count(node => string.IsNullOrWhiteSpace(node.Body));
        var chapters = project.ContentNodes.Count(node => string.Equals(node.Kind, "Chapter", StringComparison.OrdinalIgnoreCase));
        var scenes = project.ContentNodes.Count(node => string.Equals(node.Kind, "Scene", StringComparison.OrdinalIgnoreCase));
        var blockingIssues = project.ConsistencyIssues.Count(issue =>
            string.Equals(issue.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(issue.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)));
        var activeProposals = project.RevisionCandidates.Count(candidate =>
            candidate.Key != EditionFreezeService.FreezeKey &&
            candidate.Status is "Proposed" or "Approved");
        var metadata = project.EditionMetadata ?? new EditionMetadata();

        var configuredChapterTarget = ReadPositiveIntOption(project, "ChapterCount");
        var structureDescription = chapters > 0 || scenes > 0
            ? $"Struttura canonica presente: {chapters} capitoli · {scenes} scene."
            : editable.Count > 0
                ? $"Master editoriale presente: {editable.Count} contenuti modificabili."
                : "Manca una struttura editoriale utilizzabile.";

        var checks = new List<DiezLongFormCheck>
        {
            new("BOOK_TYPE", "Error", supported,
                supported ? $"Famiglia long-form: {type}." : $"Il tipo '{type}' non appartiene a Romanzo/Racconto o Saggio/Manuale."),
            new("CONTENT_PRESENT", "Error", editable.Count > 0,
                editable.Count > 0 ? structureDescription : "Nessun capitolo, sezione o documento editoriale modificabile nel Master."),
            new("NO_EMPTY_CONTENT", "Error", empty == 0,
                empty == 0 ? "Nessun contenuto editoriale vuoto." : $"{empty} contenuti editoriali sono vuoti."),
            new("NO_BLOCKING_CONSISTENCY", "Error", blockingIssues == 0,
                blockingIssues == 0 ? "Nessuna contraddizione o issue bloccante aperta." : $"{blockingIssues} issue Critical/Error sono ancora aperte."),
            new("NO_ACTIVE_PROPOSALS", "Error", activeProposals == 0,
                activeProposals == 0 ? "Nessuna revisione ancora in attesa di decisione/applicazione." : $"{activeProposals} revisioni sono ancora Proposed/Approved."),
            new("EDITION_TITLE", "Error", !string.IsNullOrWhiteSpace(metadata.Title),
                !string.IsNullOrWhiteSpace(metadata.Title) ? $"Titolo: {metadata.Title}." : "Manca il titolo dell'edizione."),
            new("EDITION_LANGUAGE", "Error", !string.IsNullOrWhiteSpace(metadata.Language),
                !string.IsNullOrWhiteSpace(metadata.Language) ? $"Lingua: {metadata.Language}." : "Manca la lingua dell'edizione.")
        };

        if (configuredChapterTarget.HasValue)
        {
            checks.Add(new DiezLongFormCheck(
                "CHAPTER_TARGET",
                "Info",
                true,
                chapters == configuredChapterTarget.Value
                    ? $"Capitoli: {chapters}, uguale al valore indicativo impostato."
                    : $"Capitoli: {chapters}; valore indicativo impostato: {configuredChapterTarget.Value}. Non è un vincolo HARD."));
        }

        var ready = checks.Where(check => string.Equals(check.Severity, "Error", StringComparison.OrdinalIgnoreCase)).All(check => check.Passed);
        return new DiezLongFormFinalizationState(
            ready, type, editable.Count, chapters, scenes, empty, blockingIssues, activeProposals, checks);
    }

    private static int? ReadPositiveIntOption(PreviewProject project, string key)
    {
        var definition = BookTypeAiOptionsCoreService.Definitions(project)
            .FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return null;
        var value = BookTypeAiOptionsCoreService.Get(project, definition);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static PreviewProject Parse(string json)
    {
        var project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal gate long-form.");
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
        return project;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
