using System.Text.Json;

namespace DiezPublishingStudio;

public sealed record DiezWordSearchDuplicateOccurrence(
    Guid ContentId,
    string PuzzleId,
    int WordPosition,
    string Word);

public sealed record DiezWordSearchDuplicateGroup(
    string Word,
    int Occurrences,
    int PuzzleCount,
    IReadOnlyList<DiezWordSearchDuplicateOccurrence> Locations);

public sealed record DiezWordSearchBookCheck(
    int ExpectedPuzzles,
    int PresentPuzzles,
    int TotalWords,
    int DistinctWords,
    bool NoDuplicatesEnabled,
    bool PuzzleCountMatches,
    bool DuplicateCheckPassed,
    bool Passed,
    int DuplicateWords,
    int ExtraOccurrences,
    IReadOnlyList<DiezWordSearchDuplicateGroup> Duplicates,
    IReadOnlyList<string> Messages);

/// <summary>
/// Whole-book Word Search invariant. The configured book is one duplicate domain:
/// if PuzzleCount is 100, every word occurrence across all 100 puzzles participates in
/// the same duplicate check. Editing/replacement can still target a single puzzle.
/// </summary>
public static class DiezWordSearchBookGuard
{
    public static DiezWordSearchBookCheck Analyze(string projectJson)
    {
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal controllo Word Search.");
        Normalize(project);
        return Analyze(project);
    }

    internal static DiezWordSearchBookCheck Analyze(PreviewProject project)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        var expected = ReadPositiveIntOption(project, "PuzzleCount", 100);
        var noDuplicates = ReadBoolOption(project, "NoDuplicates", true);

        var occurrences = records
            .SelectMany(record => record.Words.Select((word, index) => new
            {
                Record = record,
                Position = index + 1,
                Word = (word ?? string.Empty).Trim(),
                Key = Key(word)
            }))
            .Where(item => item.Key.Length > 0)
            .ToList();

        var duplicates = occurrences
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var locations = group
                    .OrderBy(item => item.Record.Order)
                    .ThenBy(item => item.Record.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Position)
                    .Select(item => new DiezWordSearchDuplicateOccurrence(
                        item.Record.ContentId,
                        item.Record.Id,
                        item.Position,
                        item.Word))
                    .ToList();
                return new DiezWordSearchDuplicateGroup(
                    locations[0].Word,
                    locations.Count,
                    locations.Select(location => location.PuzzleId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    locations);
            })
            .OrderByDescending(group => group.Occurrences)
            .ThenBy(group => group.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var puzzleCountMatches = records.Count == expected;
        var duplicateCheckPassed = !noDuplicates || duplicates.Count == 0;
        var messages = new List<string>();

        if (puzzleCountMatches)
            messages.Add($"Quantità completa: {records.Count}/{expected} puzzle.");
        else if (records.Count < expected)
            messages.Add($"Mancano {expected - records.Count} puzzle: {records.Count}/{expected} presenti.");
        else
            messages.Add($"Ci sono {records.Count - expected} puzzle in più: {records.Count}/{expected}.");

        if (!noDuplicates)
        {
            messages.Add("Il controllo duplicati tra puzzle è disattivato nelle opzioni del libro.");
        }
        else if (duplicates.Count == 0)
        {
            messages.Add($"Nessuna parola duplicata nell'intero libro ({occurrences.Count} occorrenze controllate in {records.Count} puzzle).");
        }
        else
        {
            var extra = duplicates.Sum(group => group.Occurrences - 1);
            messages.Add($"Duplicati globali: {duplicates.Count} parole ripetute, {extra} occorrenze in eccesso nell'intero libro.");
            foreach (var group in duplicates.Take(20))
            {
                var where = string.Join(", ", group.Locations.Select(location => $"{location.PuzzleId}/Parola {location.WordPosition:D2}"));
                messages.Add($"{group.Word}: {where}.");
            }
            if (duplicates.Count > 20)
                messages.Add($"…e altre {duplicates.Count - 20} parole duplicate.");
        }

        return new DiezWordSearchBookCheck(
            expected,
            records.Count,
            occurrences.Count,
            occurrences.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            noDuplicates,
            puzzleCountMatches,
            duplicateCheckPassed,
            puzzleCountMatches && duplicateCheckPassed,
            duplicates.Count,
            duplicates.Sum(group => group.Occurrences - 1),
            duplicates,
            messages);
    }

    internal static bool IsWordUsedOutside(
        PreviewProject project,
        Guid currentContentId,
        int currentWordPosition,
        string? candidateWord)
    {
        var candidate = Key(candidateWord);
        if (candidate.Length == 0) return false;

        foreach (var record in WordSearchWorkspaceService.GetRecords(project))
        {
            for (var index = 0; index < record.Words.Count; index++)
            {
                if (record.ContentId == currentContentId && index + 1 == currentWordPosition) continue;
                if (string.Equals(Key(record.Words[index]), candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static int ReadPositiveIntOption(PreviewProject project, string key, int fallback)
    {
        var definition = BookTypeAiOptionsCoreService.Definitions(project)
            .FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return fallback;
        var value = BookTypeAiOptionsCoreService.Get(project, definition);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ReadBoolOption(PreviewProject project, string key, bool fallback)
    {
        var definition = BookTypeAiOptionsCoreService.Definitions(project)
            .FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return fallback;
        var value = BookTypeAiOptionsCoreService.Get(project, definition);
        if (bool.TryParse(value, out var parsed)) return parsed;
        return fallback;
    }

    private static string Key(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static void Normalize(PreviewProject project)
    {
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
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
