namespace DiezPublishingStudio;

internal static class WordSearchWorkspaceChecks
{
    public static WordSearchIssueSummary Analyze(PreviewProject project, WordSearchRecord record)
    {
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var normalized = record.Words.Select(Normalize).Where(w => w.Length > 0).ToList();
        var duplicateInside = normalized
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Sum(g => Math.Max(0, g.Count() - 1));

        var otherWords = WordSearchWorkspaceService.GetRecords(project)
            .Where(r => r.ContentId != record.ContentId)
            .SelectMany(r => r.Words)
            .Select(Normalize)
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedElsewhere = normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count(otherWords.Contains);

        var messages = new List<string>();
        var missingTitle = string.IsNullOrWhiteSpace(record.Title);
        var missingTheme = string.IsNullOrWhiteSpace(record.Theme);
        var tooFew = record.Words.Count < expected;

        if (missingTitle) messages.Add("Manca il titolo.");
        if (missingTheme) messages.Add("Manca il tema.");
        if (tooFew) messages.Add($"Mancano {expected - record.Words.Count} parole: {record.Words.Count}/{expected} presenti.");
        if (record.Words.Count > expected) messages.Add($"Ci sono {record.Words.Count - expected} parole in più: {record.Words.Count}/{expected}.");
        if (duplicateInside > 0) messages.Add($"{duplicateInside} parole duplicate dentro questo puzzle.");
        if (usedElsewhere > 0) messages.Add($"{usedElsewhere} parole compaiono anche in altri puzzle.");
        if (messages.Count == 0) messages.Add($"Puzzle completo: {record.Words.Count}/{expected} parole.");

        return new WordSearchIssueSummary(duplicateInside, usedElsewhere, missingTitle, missingTheme, tooFew, messages);
    }

    private static string Normalize(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
