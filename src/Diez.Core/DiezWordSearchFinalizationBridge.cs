using System.Text.Json;

namespace DiezPublishingStudio;

public sealed record DiezWordSearchFinalCheck(
    string Code,
    bool Passed,
    string Message);

public sealed record DiezWordSearchFinalizationState(
    bool Ready,
    int ExpectedPuzzles,
    int PresentPuzzles,
    int ApprovedPuzzles,
    int InvalidPuzzleCount,
    int UnsafeWordOccurrences,
    int DuplicateWords,
    IReadOnlyList<DiezWordSearchFinalCheck> Checks);

public sealed record DiezWordSearchFinalExportResult(
    bool Exported,
    string Message,
    string? OutputPath);

/// <summary>
/// Final handoff gate for Word Search. Working exports may be produced at any time,
/// but a final database is released only when the whole configured book is complete,
/// globally unique (when NoDuplicates is enabled), individually valid and approved.
/// </summary>
public static class DiezWordSearchFinalizationBridge
{
    public static DiezWordSearchFinalizationState Readiness(string projectJson)
    {
        var project = Parse(projectJson);
        return Readiness(project);
    }

    public static async Task<DiezWordSearchFinalExportResult> ExportFinalDatabaseAsync(
        string projectJson,
        string outputPath)
    {
        var project = Parse(projectJson);
        var state = Readiness(project);
        if (!state.Ready)
        {
            var failed = state.Checks.Where(check => !check.Passed).Take(3).Select(check => check.Message);
            return new DiezWordSearchFinalExportResult(
                false,
                "Export finale Word Search bloccato: " + string.Join(" ", failed),
                null);
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            return new DiezWordSearchFinalExportResult(false, "Percorso di esportazione non valido.", null);

        var fullPath = Path.GetFullPath(outputPath);
        var result = await WordSearchFullDatabaseExportService.ExportAsync(project, fullPath);
        return new DiezWordSearchFinalExportResult(
            result.Success,
            result.Message,
            result.Success ? EnsureExtension(fullPath, ".xlsx") : null);
    }

    internal static DiezWordSearchFinalizationState Readiness(PreviewProject project)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        var book = DiezWordSearchBookGuard.Analyze(project);
        var invalid = new List<(WordSearchRecord Record, WordSearchValidationResult Validation)>();
        foreach (var record in records)
        {
            var validation = WordSearchValidationService.Analyze(project, record);
            if (validation.HasProblems || validation.PresentWords != validation.ExpectedWords)
                invalid.Add((record, validation));
        }

        var approved = records.Count(record =>
            string.Equals(record.Status, WordSearchWorkspaceService.StatusApproved, StringComparison.OrdinalIgnoreCase));

        var unsafeWords = WordSearchLexiconService.GetEntries(project)
            .Where(entry => entry.KdpSafe == false)
            .Select(entry => Key(entry.Word))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unsafeOccurrences = records
            .SelectMany(record => record.Words)
            .Select(Key)
            .Count(unsafeWords.Contains);

        var checks = new List<DiezWordSearchFinalCheck>
        {
            new(
                "PUZZLE_COUNT",
                book.PuzzleCountMatches,
                book.PuzzleCountMatches
                    ? $"Quantità completa: {book.PresentPuzzles}/{book.ExpectedPuzzles} puzzle."
                    : $"Quantità incompleta: {book.PresentPuzzles}/{book.ExpectedPuzzles} puzzle."),
            new(
                "WHOLE_BOOK_UNIQUE",
                book.DuplicateCheckPassed,
                book.DuplicateCheckPassed
                    ? "Nessun duplicato vietato nell'intero libro."
                    : $"{book.DuplicateWords} parole duplicate nell'intero libro ({book.ExtraOccurrences} occorrenze in eccesso)."),
            new(
                "PUZZLES_VALID",
                invalid.Count == 0,
                invalid.Count == 0
                    ? "Ogni puzzle contiene esattamente il numero di parole previsto e non ha errori strutturali."
                    : $"{invalid.Count} puzzle hanno quantità parole o dati strutturali da correggere."),
            new(
                "PUZZLES_APPROVED",
                records.Count > 0 && approved == records.Count,
                records.Count > 0 && approved == records.Count
                    ? $"Tutti i {approved} puzzle sono approvati."
                    : $"Puzzle approvati: {approved}/{records.Count}."),
            new(
                "KDP_SAFE",
                unsafeOccurrences == 0,
                unsafeOccurrences == 0
                    ? "Nessuna parola marcata KDPSAFE=NO è usata nei puzzle."
                    : $"{unsafeOccurrences} occorrenze usano parole marcate KDPSAFE=NO nel lessico.")
        };

        return new DiezWordSearchFinalizationState(
            checks.All(check => check.Passed),
            book.ExpectedPuzzles,
            book.PresentPuzzles,
            approved,
            invalid.Count,
            unsafeOccurrences,
            book.DuplicateWords,
            checks);
    }

    private static PreviewProject Parse(string projectJson)
    {
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal finalizzatore Word Search.");
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

    private static string Key(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
