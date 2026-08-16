using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static BookTypeAiOptionDefinition Option(PreviewProject project, string key) =>
    BookTypeAiOptionsCoreService.Definitions(project)
        .Single(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-wordsearch-book-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Word Search · 100 puzzle");
    BookTypeProfileService.Set(project, BookTypeProfileService.WordSearch);
    BookTypeAiOptionsCoreService.Set(project, Option(project, "PuzzleCount"), "100");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "WordsPerPuzzle"), "5");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "NoDuplicates"), "true");

    var records = new List<WordSearchRecord>();
    for (var puzzle = 1; puzzle <= 100; puzzle++)
    {
        var record = WordSearchWorkspaceService.AddNew(project);
        record.Title = $"Puzzle {puzzle:D3}";
        record.Theme = puzzle % 2 == 0 ? "Tema pari" : "Tema dispari";
        record.Words = Enumerable.Range(1, 5)
            .Select(word => $"TERMINE{puzzle:D3}{word:D2}")
            .ToList();
        WordSearchWorkspaceService.SaveRecord(project, record);
        records.Add(record);
    }

    var complete = DiezWordSearchBookGuard.Analyze(project);
    Require(complete.ExpectedPuzzles == 100 && complete.PresentPuzzles == 100,
        "Il controllo del libro deve usare i 100 puzzle configurati, non soltanto il puzzle aperto.");
    Require(complete.TotalWords == 500 && complete.DistinctWords == 500,
        "Le 500 parole dei 100 puzzle devono entrare nello stesso indice globale.");
    Require(complete.DuplicateCheckPassed && complete.Passed && complete.Duplicates.Count == 0,
        "Un libro di 100 puzzle con parole uniche deve superare il controllo globale.");

    // Duplicate a word between the first and the hundredth puzzle.
    var first = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-001");
    var last = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-100");
    var originalLastWord = last.Words[4];
    last.Words[4] = first.Words[0].ToLowerInvariant(); // also verify case-insensitive normalization.
    WordSearchWorkspaceService.SaveRecord(project, last);

    var duplicate = DiezWordSearchBookGuard.Analyze(project);
    Require(!duplicate.DuplicateCheckPassed && !duplicate.Passed,
        "Una parola ripetuta tra PUZ-001 e PUZ-100 deve bloccare il controllo dell'intero libro.");
    Require(duplicate.DuplicateWords == 1 && duplicate.ExtraOccurrences == 1,
        "La singola collisione globale deve essere contata una sola volta come occorrenza in eccesso.");
    var group = duplicate.Duplicates.Single();
    Require(group.PuzzleCount == 2 &&
            group.Locations.Select(location => location.PuzzleId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(new[] { "PUZ-001", "PUZ-100" }),
        "Il report deve indicare tutti i puzzle coinvolti, anche se sono agli estremi del libro.");

    // Restore uniqueness and verify the gate recovers.
    last = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-100");
    last.Words[4] = originalLastWord;
    WordSearchWorkspaceService.SaveRecord(project, last);
    Require(DiezWordSearchBookGuard.Analyze(project).Passed,
        "Rimossa la collisione, lo stesso libro deve tornare valido senza ricreare i puzzle.");

    // Stale replacement: candidate was unused when suggested, then another puzzle takes it.
    var lexicon = string.Join('\n',
        "WORD;CATEGORY;SUBCATEGORY;DECADE;RELEVANCE;KDPSAFE",
        $"{first.Words[0]};Test;Comune;1990;0.50;YES",
        "ALTERNATIVAGLOBALE;Test;Comune;1990;0.90;YES");
    var imported = WordSearchLexiconService.ImportDelimitedText(project, lexicon, "Pianista libro");
    Require(imported.Recognized, "Il lessico per il test di sostituzione deve essere importato.");

    first = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-001");
    var suggestions = WordSearchReplacementService.Suggest(project, first, 1, maxLength: 30, maxResults: 20);
    var candidate = suggestions.Single(item => item.Word == "ALTERNATIVAGLOBALE");

    var second = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-002");
    var secondOriginal = second.Words[0];
    second.Words[0] = candidate.Word;
    WordSearchWorkspaceService.SaveRecord(project, second);

    var stale = WordSearchReplacementService.Replace(project, first, 1, candidate);
    Require(!stale.Success,
        "Una suggestion diventata duplicata in un altro puzzle deve essere respinta al momento dell'applicazione.");
    first = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-001");
    Require(first.Words[0] != candidate.Word,
        "Il rifiuto di una sostituzione stale non deve modificare il puzzle bersaglio.");

    // Restore and verify all 100 puzzle still form one clean duplicate domain after package round-trip.
    second = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-002");
    second.Words[0] = secondOriginal;
    WordSearchWorkspaceService.SaveRecord(project, second);
    Require(DiezWordSearchBookGuard.Analyze(project).Passed,
        "Il libro deve tornare unico dopo il ripristino della parola usata nel test stale.");

    var package = Path.Combine(tempRoot, "word-search-100.diez");
    await ProjectFileStore.SaveAsync(package, project);
    var reloaded = await ProjectFileStore.LoadAsync(package);
    var afterRoundTrip = DiezWordSearchBookGuard.Analyze(reloaded);
    Require(afterRoundTrip.Passed && afterRoundTrip.PresentPuzzles == 100 && afterRoundTrip.TotalWords == 500,
        "Il controllo globale su 100 puzzle deve sopravvivere al round-trip del pacchetto .diez.");

    // Quantity is part of the whole-book guard too: 99/100 is not a complete book.
    var removed = WordSearchWorkspaceService.GetRecords(reloaded).Single(record => record.Id == "PUZ-100");
    WordSearchWorkspaceService.DeleteRecord(reloaded, removed.ContentId);
    var incomplete = DiezWordSearchBookGuard.Analyze(reloaded);
    Require(!incomplete.Passed && !incomplete.PuzzleCountMatches && incomplete.PresentPuzzles == 99,
        "Un libro configurato per 100 puzzle non deve risultare completo con soli 99.");

    Console.WriteLine("WORD SEARCH BOOK PIANIST PASS: 100 puzzles shared one global duplicate domain, stale replacement could not create a cross-puzzle duplicate, and quantity/uniqueness survived package round-trip.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
