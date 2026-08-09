namespace DiezPublishingStudio;

internal sealed record WordSearchValidationResult(
    bool HasProblems,
    int ExpectedWords,
    int PresentWords,
    IReadOnlyList<string> Messages);

internal static class WordSearchValidationService
{
    public static WordSearchValidationResult Analyze(PreviewProject project, WordSearchRecord record)
    {
        var baseResult = WordSearchWorkspaceService.Analyze(project, record);
        var expected = WordSearchDatabaseService.ExpectedWordCount(project, record);
        var present = record.Words.Count;
        var messages = baseResult.Messages
            .Where(m => !m.StartsWith("Ci sono solo ", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (present < expected)
            messages.Insert(0, $"Mancano {expected - present} parole: {present}/{expected} presenti.");
        else if (present > expected)
            messages.Insert(0, $"Ci sono {present - expected} parole in più: {present}/{expected}. Scegli quali tenere.");

        var hasProblems = baseResult.HasProblems || present != expected;
        if (!hasProblems && messages.Count == 0)
            messages.Add($"Completo: {present}/{expected} parole.");
        else if (!hasProblems && messages.Count == 1 && messages[0].StartsWith("Nessun problema evidente", StringComparison.OrdinalIgnoreCase))
            messages[0] = $"Completo: {present}/{expected} parole.";

        return new WordSearchValidationResult(hasProblems, expected, present, messages);
    }

    public static int SuggestedExpectedCount(PreviewProject project)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return 20;
        return records
            .Select(r => WordSearchDatabaseService.ExpectedWordCount(project, r))
            .Where(n => n > 0)
            .GroupBy(n => n)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault(20);
    }
}