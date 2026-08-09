namespace DiezPublishingStudio;

internal static class WordSearchWorkspaceChecks
{
    public static WordSearchIssueSummary Analyze(PreviewProject project, WordSearchRecord record)
    {
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var indexed = record.Words
            .Select((word, index) => new WordPosition(index + 1, word ?? string.Empty, Normalize(word ?? string.Empty)))
            .Where(p => p.Key.Length > 0)
            .ToList();

        var duplicateGroups = indexed
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        var duplicateInside = duplicateGroups.Sum(g => g.Count() - 1);

        var allOthers = WordSearchWorkspaceService.GetRecords(project)
            .Where(r => r.ContentId != record.ContentId)
            .SelectMany(r => r.Words.Select((word, index) => new OtherWordPosition(
                r.Id,
                index + 1,
                word ?? string.Empty,
                Normalize(word ?? string.Empty))))
            .Where(p => p.Key.Length > 0)
            .ToList();

        var repeatedElsewhere = indexed
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Local = g.First(),
                Matches = allOthers.Where(p => string.Equals(p.Key, g.Key, StringComparison.OrdinalIgnoreCase)).ToList()
            })
            .Where(x => x.Matches.Count > 0)
            .ToList();
        var usedElsewhere = repeatedElsewhere.Count;

        var messages = new List<string>();
        var missingTitle = string.IsNullOrWhiteSpace(record.Title);
        var missingTheme = string.IsNullOrWhiteSpace(record.Theme);
        var tooFew = record.Words.Count < expected;

        if (missingTitle) messages.Add($"{record.Id} → Titolo: manca.");
        if (missingTheme) messages.Add($"{record.Id} → Tema: manca.");

        if (tooFew)
        {
            var firstMissing = record.Words.Count + 1;
            var missingRange = firstMissing == expected
                ? $"Parola {firstMissing:D2}"
                : $"Parole {firstMissing:D2}–{expected:D2}";
            messages.Add($"{record.Id} → {missingRange}: mancanti. Presenti {record.Words.Count}/{expected}.");
        }

        if (record.Words.Count > expected)
        {
            var firstExtra = expected + 1;
            var extraRange = firstExtra == record.Words.Count
                ? $"Parola {firstExtra:D2}"
                : $"Parole {firstExtra:D2}–{record.Words.Count:D2}";
            messages.Add($"{record.Id} → {extraRange}: in più rispetto alle {expected} richieste.");
        }

        foreach (var group in duplicateGroups)
        {
            var positions = string.Join(", ", group.Select(p => $"Parola {p.Position:D2}"));
            messages.Add($"{record.Id} → {positions}: “{group.First().DisplayWord}” è ripetuta nello stesso puzzle.");
        }

        foreach (var repeated in repeatedElsewhere)
        {
            var where = string.Join(", ", repeated.Matches
                .Take(8)
                .Select(p => $"{p.PuzzleId} → Parola {p.Position:D2}"));
            if (repeated.Matches.Count > 8) where += $" e altre {repeated.Matches.Count - 8} posizioni";
            messages.Add($"{record.Id} → Parola {repeated.Local.Position:D2}: “{repeated.Local.DisplayWord}” compare anche in {where}.");
        }

        if (messages.Count == 0)
            messages.Add($"{record.Id}: completo, {record.Words.Count}/{expected} parole e nessun problema trovato.");

        return new WordSearchIssueSummary(duplicateInside, usedElsewhere, missingTitle, missingTheme, tooFew, messages);
    }

    public static bool HasProblems(PreviewProject project, WordSearchRecord record)
    {
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var summary = Analyze(project, record);
        return record.Words.Count != expected || summary.DuplicateWordsInside > 0 || summary.WordsUsedElsewhere > 0 ||
               summary.MissingTitle || summary.MissingTheme;
    }

    private static string Normalize(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed record WordPosition(int Position, string DisplayWord, string Key);
    private sealed record OtherWordPosition(string PuzzleId, int Position, string DisplayWord, string Key);
}
