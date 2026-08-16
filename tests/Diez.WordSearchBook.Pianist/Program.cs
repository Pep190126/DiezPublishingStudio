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
        record.Status = WordSearchWorkspaceService.StatusApproved;
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
    var initialFinal = DiezWordSearchFinalizationBridge.Readiness(project);
    Require(initialFinal.Ready && initialFinal.ApprovedPuzzles == 100 && initialFinal.InvalidPuzzleCount == 0,
        "Cento puzzle completi, unici e approvati devono essere pronti per la consegna finale.");

    // Duplicate a word between the first and the hundredth puzzle.
    var first = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-001");
    var last = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-100");
    var originalLastWord = last.Words[4];
    last.Words[4] = first.Words[0].ToLowerInvariant();
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

    var blockedFinal = DiezWordSearchFinalizationBridge.Readiness(project);
    Require(!blockedFinal.Ready && blockedFinal.DuplicateWords == 1,
        "Un duplicato globale deve bloccare anche il finalizzatore del libro.");
    var blockedPath = Path.Combine(tempRoot, "must-not-exist.xlsx");
    var blockedExport = await DiezWordSearchFinalizationBridge.ExportFinalDatabaseAsync(
        System.Text.Json.JsonSerializer.Serialize(project), blockedPath);
    Require(!blockedExport.Exported && !File.Exists(blockedPath),
        "L'export finale non deve creare un file quando esiste un duplicato globale.");

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

    // Restore and produce the actual final handoff.
    second = WordSearchWorkspaceService.GetRecords(project).Single(record => record.Id == "PUZ-002");
    second.Words[0] = secondOriginal;
    WordSearchWorkspaceService.SaveRecord(project, second);
    Require(DiezWordSearchBookGuard.Analyze(project).Passed,
        "Il libro deve tornare unico dopo il ripristino della parola usata nel test stale.");

    var finalPath = Path.Combine(tempRoot, "word-search-final.xlsx");
    var finalExport = await DiezWordSearchFinalizationBridge.ExportFinalDatabaseAsync(
        System.Text.Json.JsonSerializer.Serialize(project), finalPath);
    Require(finalExport.Exported && File.Exists(finalPath) && new FileInfo(finalPath).Length > 0,
        "Il database finale deve essere creato quando tutti i 100 puzzle sono completi, unici e approvati.");

    var fresh = ProjectFileStore.Create("Reimport finale");
    BookTypeProfileService.Set(fresh, BookTypeProfileService.WordSearch);
    var reimport = await WordSearchDatabaseService.ImportDatabaseAsync(fresh, finalPath, Guid.Empty, replaceExisting: false);
    Require(reimport.Recognized && reimport.Added == 100,
        "Il file finale deve essere realmente reimportabile e contenere tutti i 100 puzzle.");

    var package = Path.Combine(tempRoot, "word-search-100.diez");
    await ProjectFileStore.SaveAsync(package, project);
    var reloaded = await ProjectFileStore.LoadAsync(package);
    var afterRoundTrip = DiezWordSearchBookGuard.Analyze(reloaded);
    Require(afterRoundTrip.Passed && afterRoundTrip.PresentPuzzles == 100 && afterRoundTrip.TotalWords == 500,
        "Il controllo globale su 100 puzzle deve sopravvivere al round-trip del pacchetto .diez.");
    Require(DiezWordSearchFinalizationBridge.Readiness(reloaded).Ready,
        "La readiness finale deve sopravvivere al round-trip del progetto.");

    // Quantity is part of the whole-book guard too: 99/100 is not a complete book.
    var removed = WordSearchWorkspaceService.GetRecords(reloaded).Single(record => record.Id == "PUZ-100");
    WordSearchWorkspaceService.DeleteRecord(reloaded, removed.ContentId);
    var incomplete = DiezWordSearchBookGuard.Analyze(reloaded);
    Require(!incomplete.Passed && !incomplete.PuzzleCountMatches && incomplete.PresentPuzzles == 99,
        "Un libro configurato per 100 puzzle non deve risultare completo con soli 99.");
    Require(!DiezWordSearchFinalizationBridge.Readiness(reloaded).Ready,
        "Il finalizzatore deve bloccare anche il libro 99/100, pur se tutte le parole rimaste sono uniche.");

    Console.WriteLine("WORD SEARCH BOOK PIANIST PASS: 100 puzzles shared one global duplicate domain; stale replacement stayed safe; final handoff was blocked on duplicates/incomplete quantity and reimported all 100 when ready.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
