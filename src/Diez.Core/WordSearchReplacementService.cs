namespace DiezPublishingStudio;

internal sealed record WordSearchReplacementCandidate(
    string Word,
    string EntryId,
    int Rank,
    double? Relevance,
    string Reason,
    string Category,
    string Subcategory,
    string Series,
    string Decade,
    string Year);

internal readonly record struct WordSearchReplacementResult(bool Success, string Message);

internal static class WordSearchReplacementService
{
    public static IReadOnlyList<WordSearchReplacementCandidate> Suggest(
        PreviewProject project,
        WordSearchRecord puzzle,
        int wordPosition,
        int? maxLength = null,
        int maxResults = 20)
    {
        if (wordPosition < 1 || wordPosition > puzzle.Words.Count) return [];
        var originalWord = puzzle.Words[wordPosition - 1];
        var originalKey = Key(originalWord);
        var lexicon = WordSearchLexiconService.GetEntries(project);
        if (lexicon.Count == 0) return [];

        var originals = lexicon.Where(e => Key(e.Word) == originalKey).ToList();
        var original = ChooseOriginalContext(originals, puzzle);
        if (original is null) return [];

        var allPuzzleWords = WordSearchWorkspaceService.GetRecords(project)
            .SelectMany(r => r.Words)
            .Select(Key)
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentPuzzleWords = puzzle.Words
            .Where((_, index) => index != wordPosition - 1)
            .Select(Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var limit = maxLength.GetValueOrDefault(int.MaxValue);
        if (limit <= 0) limit = int.MaxValue;

        var candidates = new List<WordSearchReplacementCandidate>();
        foreach (var entry in lexicon)
        {
            var candidateKey = Key(entry.Word);
            if (candidateKey.Length == 0 || candidateKey == originalKey) continue;
            if (entry.Word.Length > limit) continue;
            if (currentPuzzleWords.Contains(candidateKey)) continue;

            // Il controllo storico richiedeva alternative non ancora usate nei puzzle generati.
            // Il dominio di unicità è l'intero libro, non soltanto il puzzle aperto.
            if (allPuzzleWords.Contains(candidateKey)) continue;

            // Se il database espone KDPSAFE, NO è sempre escluso. Il valore assente resta utilizzabile
            // perché database generici possono non avere questa colonna.
            if (entry.KdpSafe == false) continue;
            if (original.Relevance.HasValue && entry.Relevance.HasValue && entry.Relevance.Value < original.Relevance.Value) continue;

            var rank = ContextRank(original, entry);
            if (rank <= 0) continue;
            candidates.Add(new WordSearchReplacementCandidate(
                entry.Word,
                entry.Id,
                rank,
                entry.Relevance,
                Reason(original, entry, rank),
                entry.Category,
                entry.Subcategory,
                entry.Series,
                entry.Decade,
                entry.Year));
        }

        return candidates
            .GroupBy(c => Key(c.Word), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Rank).ThenByDescending(c => c.Relevance ?? double.MinValue).First())
            .OrderByDescending(c => c.Rank)
            .ThenByDescending(c => c.Relevance ?? double.MinValue)
            .ThenBy(c => c.Word.Length)
            .ThenBy(c => c.Word, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    public static WordSearchReplacementResult Replace(
        PreviewProject project,
        WordSearchRecord puzzle,
        int wordPosition,
        WordSearchReplacementCandidate candidate)
    {
        if (wordPosition < 1 || wordPosition > puzzle.Words.Count)
            return new(false, "La posizione della parola non esiste più nel puzzle.");
        if (string.IsNullOrWhiteSpace(candidate.Word))
            return new(false, "La parola sostitutiva è vuota.");

        var replacementKey = Key(candidate.Word);
        if (puzzle.Words.Where((_, index) => index != wordPosition - 1).Any(w => Key(w) == replacementKey))
            return new(false, $"“{candidate.Word}” è già presente nello stesso puzzle.");

        // Recheck at apply time as well as suggestion time. A stale suggestion must never create
        // a duplicate if another puzzle used the candidate after the suggestion list was built.
        if (DiezWordSearchBookGuard.IsWordUsedOutside(project, puzzle.ContentId, wordPosition, candidate.Word))
            return new(false, $"“{candidate.Word}” nel frattempo è stata usata in un altro puzzle. Scegli una nuova alternativa.");

        var old = puzzle.Words[wordPosition - 1];
        puzzle.Words[wordPosition - 1] = candidate.Word.Trim();
        puzzle.Status = WordSearchWorkspaceService.StatusToReview;
        puzzle.Origin = AppendModified(puzzle.Origin);
        puzzle.Notes = AppendNote(puzzle.Notes,
            $"Sostituzione: Parola {wordPosition:D2} “{old}” → “{candidate.Word}” ({candidate.Reason}).");
        WordSearchWorkspaceService.SaveRecord(project, puzzle);
        return new(true,
            $"{puzzle.Id} → Parola {wordPosition:D2}: “{old}” sostituita con “{candidate.Word}”. Gli altri puzzle non sono stati modificati.");
    }

    private static WordSearchLexiconEntry? ChooseOriginalContext(IReadOnlyList<WordSearchLexiconEntry> originals, WordSearchRecord puzzle)
    {
        if (originals.Count == 0) return null;
        if (originals.Count == 1) return originals[0];
        return originals
            .OrderByDescending(e => Same(e.Category, puzzle.Theme) ? 1 : 0)
            .ThenByDescending(e => e.Relevance ?? double.MinValue)
            .First();
    }

    private static int ContextRank(WordSearchLexiconEntry original, WordSearchLexiconEntry candidate)
    {
        var sameSeries = MatchKnown(original.Series, candidate.Series);
        var sameSubcategory = MatchKnown(original.Subcategory, candidate.Subcategory);
        var sameCategory = MatchKnown(original.Category, candidate.Category);
        var sameDecade = MatchKnown(original.Decade, candidate.Decade);
        var sameYear = MatchKnown(original.Year, candidate.Year);

        if (sameSeries && sameSubcategory && sameCategory && (sameYear || sameDecade)) return 600;
        if (sameSubcategory && sameCategory && sameDecade) return 500;
        if (sameSubcategory && sameCategory && sameYear) return 490;
        if (sameCategory && sameDecade) return 400;
        if (sameCategory && sameYear) return 390;
        if (sameDecade) return 300;
        if (sameYear) return 290;
        if (TemporalCompatible(original, candidate)) return 200;
        if (sameSeries && sameCategory) return 180;
        if (sameSubcategory && sameCategory) return 170;
        if (sameCategory) return 100;
        return 0;
    }

    private static string Reason(WordSearchLexiconEntry original, WordSearchLexiconEntry candidate, int rank)
    {
        var parts = new List<string>();
        if (MatchKnown(original.Series, candidate.Series)) parts.Add("stessa serie");
        if (MatchKnown(original.Subcategory, candidate.Subcategory)) parts.Add("stessa sottocategoria");
        if (MatchKnown(original.Category, candidate.Category)) parts.Add("stessa categoria");
        if (MatchKnown(original.Year, candidate.Year)) parts.Add("stesso anno");
        else if (MatchKnown(original.Decade, candidate.Decade)) parts.Add("stessa decade");
        else if (TemporalCompatible(original, candidate)) parts.Add("periodo compatibile");
        if (candidate.KdpSafe == true) parts.Add("KDPSAFE");
        if (candidate.Relevance.HasValue) parts.Add($"rilevanza {candidate.Relevance:0.##}");
        parts.Add("non usata nei puzzle");
        return string.Join(" · ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TemporalCompatible(WordSearchLexiconEntry a, WordSearchLexiconEntry b)
    {
        if (MatchKnown(a.Decade, b.Decade) || MatchKnown(a.Year, b.Year)) return true;
        if (TryYear(a.Year, out var ay) && TryYear(b.Year, out var by)) return ay / 10 == by / 10;
        if (TryYear(a.Year, out ay) && TryDecade(b.Decade, out var bd)) return ay / 10 * 10 == bd;
        if (TryDecade(a.Decade, out var ad) && TryYear(b.Year, out by)) return by / 10 * 10 == ad;
        return false;
    }

    private static bool TryYear(string? value, out int year)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length >= 4) digits = digits[..4];
        return int.TryParse(digits, out year) && year >= 1000 && year <= 9999;
    }

    private static bool TryDecade(string? value, out int decade)
    {
        decade = 0;
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 4 || !int.TryParse(digits[..4], out var year)) return false;
        decade = year / 10 * 10;
        return true;
    }

    private static bool MatchKnown(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Same(a, b);
    private static bool Same(string? a, string? b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
    private static string Key(string? value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string AppendModified(string? origin)
    {
        var value = string.IsNullOrWhiteSpace(origin) ? "Modificato in Diez" : origin.Trim();
        return value.Contains("modificat", StringComparison.OrdinalIgnoreCase) ? value : value + " · modificato";
    }

    private static string AppendNote(string? notes, string note)
    {
        var value = (notes ?? string.Empty).Trim();
        return value.Length == 0 ? note : value + Environment.NewLine + note;
    }
}
