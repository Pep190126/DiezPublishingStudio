using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezWordSearchPuzzleDto(
    Guid ContentId,
    string PuzzleId,
    string Title,
    string Theme,
    IReadOnlyList<string> Words,
    string Status,
    string Origin,
    string Notes,
    IReadOnlyList<string> Issues);

public sealed record DiezWordSearchLexiconDto(
    string Id,
    string Word,
    string Category,
    string Subcategory,
    string Series,
    string Decade,
    string Year,
    double? Relevance,
    bool? KdpSafe,
    string Origin);

public sealed record DiezWordSearchWorkspaceDto(
    IReadOnlyList<DiezWordSearchPuzzleDto> Puzzles,
    IReadOnlyList<DiezWordSearchLexiconDto> Lexicon,
    string LegacyDatabaseDraft,
    string LegacyLexiconDraft);

public sealed record DiezCrosswordEntryDto(
    Guid EntityId,
    string Word,
    string Definition1,
    string Definition2,
    string Definition3,
    string Definition4,
    string Notes,
    string Approved);

public sealed record DiezCrosswordWorkspaceDto(
    string Theme,
    string PrimaryLanguage,
    bool Adaptive,
    int MissingDefinitions,
    IReadOnlyList<DiezCrosswordEntryDto> Entries,
    string LegacyWordsDraft,
    string LegacyQxwDraft);

public sealed record DiezPuzzleMutationResult(
    string ProjectJson,
    string Status,
    string Message,
    bool Changed,
    Guid? SelectedId = null);

/// <summary>
/// Public UI-neutral boundary for Word Search and Crossword workspaces.
/// Frontends read/write the canonical Core model; old UnoUiState values are exposed only as
/// migration drafts and are never written by this bridge.
/// </summary>
public static class DiezPuzzleFrontendBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static DiezWordSearchWorkspaceDto ReadWordSearch(string projectJson)
    {
        var (root, project) = Parse(projectJson);
        var puzzles = WordSearchWorkspaceService.GetRecords(project)
            .Select(record =>
            {
                var analysis = WordSearchWorkspaceService.Analyze(project, record);
                return new DiezWordSearchPuzzleDto(
                    record.ContentId,
                    record.Id,
                    record.Title,
                    record.Theme,
                    record.Words.ToList(),
                    record.Status,
                    record.Origin,
                    record.Notes,
                    analysis.Messages.ToList());
            })
            .ToList();
        var lexicon = WordSearchLexiconService.GetEntries(project)
            .Select(entry => new DiezWordSearchLexiconDto(
                entry.Id,
                entry.Word,
                entry.Category,
                entry.Subcategory,
                entry.Series,
                entry.Decade,
                entry.Year,
                entry.Relevance,
                entry.KdpSafe,
                entry.Origin))
            .ToList();
        var legacy = root["UnoUiState"] as JsonObject;
        return new DiezWordSearchWorkspaceDto(
            puzzles,
            lexicon,
            ScalarString(legacy?["WordSearch.Database"]),
            ScalarString(legacy?["WordSearch.Lexicon"]));
    }

    public static DiezPuzzleMutationResult SaveWordSearchPuzzle(
        string projectJson,
        Guid? contentId,
        string? puzzleId,
        string? title,
        string? theme,
        IEnumerable<string>? words,
        string? status,
        string? notes)
    {
        var (root, project) = Parse(projectJson);
        var existing = contentId is Guid id && id != Guid.Empty
            ? WordSearchWorkspaceService.GetRecords(project).FirstOrDefault(r => r.ContentId == id)
            : null;
        var record = existing ?? new WordSearchRecord
        {
            ContentId = Guid.NewGuid(),
            Origin = "Creato in Diez",
            Status = WordSearchWorkspaceService.StatusToReview
        };
        record.Id = puzzleId ?? record.Id;
        record.Title = title ?? string.Empty;
        record.Theme = theme ?? string.Empty;
        record.Words = NormalizeWords(words);
        record.Status = string.IsNullOrWhiteSpace(status) ? record.Status : status!;
        record.Notes = notes ?? string.Empty;
        if (existing is not null) record.Origin = AppendModified(existing.Origin);
        WordSearchWorkspaceService.SaveRecord(project, record);
        MergeProject(root, project);
        return Result(root, "SAVED", $"{record.Id} salvato nel database Word Search canonico.", true, record.ContentId);
    }

    public static DiezPuzzleMutationResult DeleteWordSearchPuzzle(string projectJson, Guid contentId)
    {
        var (root, project) = Parse(projectJson);
        var exists = WordSearchWorkspaceService.GetRecords(project).Any(r => r.ContentId == contentId);
        if (!exists) return Result(root, "NOT_FOUND", "Puzzle Word Search non trovato.", false);
        WordSearchWorkspaceService.DeleteRecord(project, contentId);
        MergeProject(root, project);
        return Result(root, "DELETED", "Puzzle rimosso dal database Word Search canonico.", true);
    }

    public static DiezPuzzleMutationResult ImportWordSearchLexiconText(string projectJson, string? text)
    {
        var (root, project) = Parse(projectJson);
        var merge = WordSearchLexiconService.ImportDelimitedText(project, text ?? string.Empty, "Importato da Uno");
        if (!merge.Recognized) return Result(root, "NOT_RECOGNIZED", merge.Message, false);
        MergeProject(root, project);
        return Result(root, "IMPORTED", merge.Message, merge.Added > 0 || merge.Updated > 0);
    }

    public static string BuildWordSearchCsv(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        var records = WordSearchWorkspaceService.GetRecords(project);
        var maxWords = Math.Max(1, records.Select(r => r.Words.Count).DefaultIfEmpty(0).Max());
        var builder = new StringBuilder();
        var headers = new List<string> { "ID", "Titolo", "Tema", "Stato", "Origine", "Note" };
        headers.AddRange(Enumerable.Range(1, maxWords).Select(i => $"Parola {i:00}"));
        AppendCsv(builder, headers);
        foreach (var record in records)
        {
            var row = new List<string> { record.Id, record.Title, record.Theme, record.Status, record.Origin, record.Notes };
            row.AddRange(Enumerable.Range(0, maxWords).Select(i => i < record.Words.Count ? record.Words[i] : string.Empty));
            AppendCsv(builder, row);
        }
        return builder.ToString();
    }

    public static DiezCrosswordWorkspaceDto ReadCrossword(string projectJson)
    {
        var (root, project) = Parse(projectJson);
        var entries = CrosswordService.DefinitionRows(project)
            .Select(row =>
            {
                var entity = CrosswordService.FindWord(project, row.Word);
                return new DiezCrosswordEntryDto(
                    entity?.EntityId ?? Guid.Empty,
                    row.Word,
                    row.Definition1,
                    row.Definition2,
                    row.Definition3,
                    row.Definition4,
                    row.Notes,
                    row.Approved);
            })
            .ToList();
        var legacy = root["UnoUiState"] as JsonObject;
        var adaptiveText = CrosswordService.GetSetting(project, "Adaptive", "true");
        var adaptive = !string.Equals(adaptiveText, "false", StringComparison.OrdinalIgnoreCase) && adaptiveText != "0";
        return new DiezCrosswordWorkspaceDto(
            CrosswordService.GetSetting(project, "Theme", string.Empty),
            CrosswordService.GetSetting(project, "PrimaryLanguage", "Italiano"),
            adaptive,
            CrosswordService.MissingDefinitions(project),
            entries,
            ScalarString(legacy?["Crossword.Words"]),
            ScalarString(legacy?["Crossword.Qxw"]));
    }

    public static DiezPuzzleMutationResult SaveCrosswordSettings(
        string projectJson,
        string? theme,
        string? primaryLanguage,
        bool adaptive)
    {
        var (root, project) = Parse(projectJson);
        CrosswordService.SetSetting(project, "Theme", theme);
        CrosswordService.SetSetting(project, "PrimaryLanguage", string.IsNullOrWhiteSpace(primaryLanguage) ? "Italiano" : primaryLanguage);
        CrosswordService.SetSetting(project, "Adaptive", adaptive ? "true" : "false");
        MergeProject(root, project);
        return Result(root, "SAVED", "Impostazioni Cruciverba salvate nel Core.", true);
    }

    public static DiezPuzzleMutationResult SaveCrosswordEntry(
        string projectJson,
        Guid? entityId,
        string? word,
        string? definition1,
        string? definition2,
        string? definition3,
        string? definition4,
        string? notes,
        string? approved)
    {
        var (root, project) = Parse(projectJson);
        var normalized = CrosswordService.NormalizeGridWord(word);
        if (normalized.Length < 2)
            return Result(root, "INVALID", "Inserisci una parola di almeno due caratteri.", false);

        GraphEntity? entity = null;
        if (entityId is Guid id && id != Guid.Empty)
            entity = project.Entities.FirstOrDefault(e => e.EntityId == id && string.Equals(e.Kind, "CrosswordWord", StringComparison.OrdinalIgnoreCase));

        var collision = CrosswordService.FindWord(project, normalized);
        if (collision is not null && (entity is null || collision.EntityId != entity.EntityId))
            return Result(root, "CONFLICT", $"La parola {normalized} esiste già nel vocabolario Cruciverba.", false, collision.EntityId);

        if (entity is null)
            entity = CrosswordService.EnsureWord(project, normalized, "Uno Platform");
        else
            entity.Name = normalized;

        CrosswordService.SetDefinitionCell(project, entity.EntityId, 1, definition1);
        CrosswordService.SetDefinitionCell(project, entity.EntityId, 2, definition2);
        CrosswordService.SetDefinitionCell(project, entity.EntityId, 3, definition3);
        CrosswordService.SetDefinitionCell(project, entity.EntityId, 4, definition4);
        CrosswordService.SetNotes(project, entity.EntityId, notes);
        CrosswordService.SetApproved(project, entity.EntityId, approved);
        MergeProject(root, project);
        return Result(root, "SAVED", $"{normalized} salvata nel vocabolario Cruciverba canonico.", true, entity.EntityId);
    }

    public static DiezPuzzleMutationResult DeleteCrosswordEntry(string projectJson, Guid entityId)
    {
        var (root, project) = Parse(projectJson);
        var entity = project.Entities.FirstOrDefault(e => e.EntityId == entityId && string.Equals(e.Kind, "CrosswordWord", StringComparison.OrdinalIgnoreCase));
        if (entity is null) return Result(root, "NOT_FOUND", "Parola Cruciverba non trovata.", false);
        project.Entities.Remove(entity);
        project.BibleEntries.RemoveAll(b => b.SubjectEntityId == entityId);
        project.Relations.RemoveAll(r => r.FromId == entityId || r.ToId == entityId);
        MergeProject(root, project);
        return Result(root, "DELETED", $"{entity.Name} rimossa dal vocabolario Cruciverba canonico.", true);
    }

    public static string BuildCrosswordQxwText(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return string.Join(Environment.NewLine, CrosswordService.Words(project).Select(w => w.Name));
    }

    private static List<string> NormalizeWords(IEnumerable<string>? words) =>
        (words ?? [])
            .SelectMany(word => (word ?? string.Empty).Split(new[] { '\r', '\n', ';', ',', '|', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(word => word.Trim())
            .Where(word => word.Length > 0)
            .ToList();

    private static string AppendModified(string? origin)
    {
        var value = (origin ?? string.Empty).Trim();
        if (value.Length == 0) return "Modificato in Diez";
        return value.Contains("Modificato in Diez", StringComparison.OrdinalIgnoreCase) ? value : value + " · Modificato in Diez";
    }

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

    private static void MergeProject(JsonObject root, PreviewProject project)
    {
        MergeArray(root, "ContentNodes", project.ContentNodes, "ContentId", true);
        MergeArray(root, "Entities", project.Entities, "EntityId", true);
        MergeArray(root, "Relations", project.Relations, "RelationId", true);
        MergeArray(root, "BibleEntries", project.BibleEntries, "BibleEntryId", true);
    }

    private static void MergeArray<T>(JsonObject root, string property, IEnumerable<T> typedItems, string idProperty, bool removeMissing)
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
            foreach (var pair in typed)
                existing[pair.Key] = pair.Value?.DeepClone();
        }
        if (!removeMissing) return;
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            if (raw[i] is not JsonObject obj) continue;
            var id = Scalar(obj[idProperty]);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id)) raw.RemoveAt(i);
        }
    }

    private static DiezPuzzleMutationResult Result(JsonObject root, string status, string message, bool changed, Guid? selectedId = null) =>
        new(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), status, message, changed, selectedId);

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string ScalarString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return string.Empty;
    }

    private static void AppendCsv(StringBuilder builder, IEnumerable<string> values) =>
        builder.AppendLine(string.Join(';', values.Select(value => '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"')));
}