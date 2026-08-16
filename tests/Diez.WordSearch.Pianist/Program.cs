using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-wordsearch-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Word Search pianist");
    BookTypeProfileService.Set(project, BookTypeProfileService.WordSearch);

    var first = WordSearchWorkspaceService.AddNew(project);
    var second = WordSearchWorkspaceService.AddNew(project);
    Require(first.Id == "PUZ-001" && second.Id == "PUZ-002", "Puzzle IDs must be allocated deterministically.");
    var firstContentId = first.ContentId;

    first.Title = "Città italiane";
    first.Theme = "Città";
    first.Words = [" roma ", "mare", "MARE", "sole", "luna"];
    WordSearchWorkspaceService.SaveRecord(project, first);
    second.Title = "Secondo puzzle";
    second.Theme = "Città";
    second.Words = ["MILANO", "TORINO", "GENOVA", "BARI", "LECCE"];
    WordSearchWorkspaceService.SaveRecord(project, second);

    var issues = WordSearchWorkspaceService.Analyze(project, first);
    Require(issues.DuplicateWordsInside == 1, "Duplicate words inside a puzzle must be detected.");
    WordSearchWorkspaceService.NormalizeSelectedWords(project, first, removeDuplicates: true);
    first = WordSearchWorkspaceService.GetRecords(project).Single(r => r.ContentId == firstContentId);
    Require(first.Words.SequenceEqual(new[] { "ROMA", "MARE", "SOLE", "LUNA" }),
        "Normalization must uppercase, trim and deduplicate without changing puzzle identity.");
    Require(first.Id == "PUZ-001" && first.ContentId == firstContentId,
        "Editing words must preserve puzzle ID and ContentId.");

    WordSearchDatabaseService.SetExpectedWordCount(project, first.Id, 5);
    var validation = WordSearchValidationService.Analyze(project, first);
    Require(validation.HasProblems && validation.ExpectedWords == 5 && validation.PresentWords == 4,
        "Expected-count validation must report a missing word.");

    var lexiconText = string.Join('\n',
        "WORD;CATEGORY;SUBCATEGORY;DECADE;RELEVANCE;KDPSAFE",
        "ROMA;Città;Italia;1990;0.80;YES",
        "NAPOLI;Città;Italia;1990;0.95;YES",
        "MILANO;Città;Italia;1990;0.99;YES",
        "FIRENZE;Città;Italia;1980;0.90;YES",
        "RISCHIO;Città;Italia;1990;1.00;NO");
    var lexiconImport = WordSearchLexiconService.ImportDelimitedText(project, lexiconText, "Pianist");
    Require(lexiconImport.Recognized && lexiconImport.Added == 5, "Classified lexicon import must be recognized.");
    var stableLexiconIds = WordSearchLexiconService.GetEntries(project).ToDictionary(e => e.Word, e => e.Id, StringComparer.OrdinalIgnoreCase);
    var repeatedLexiconImport = WordSearchLexiconService.ImportDelimitedText(project, lexiconText, "Pianist");
    Require(repeatedLexiconImport.Recognized && repeatedLexiconImport.Added == 0,
        "Repeated lexicon import must be idempotent rather than duplicating words.");
    Require(WordSearchLexiconService.GetEntries(project).All(e => stableLexiconIds.TryGetValue(e.Word, out var id) && id == e.Id),
        "Repeated lexicon imports must preserve stable lexicon IDs.");

    var suggestions = WordSearchReplacementService.Suggest(project, first, 1, maxLength: 12, maxResults: 20);
    Require(suggestions.Any(c => c.Word.Equals("NAPOLI", StringComparison.OrdinalIgnoreCase)),
        "Contextual replacement must suggest an unused compatible lexicon word.");
    Require(suggestions.All(c => !c.Word.Equals("MILANO", StringComparison.OrdinalIgnoreCase)),
        "Replacement must not suggest a word already used by another puzzle.");
    Require(suggestions.All(c => !c.Word.Equals("RISCHIO", StringComparison.OrdinalIgnoreCase)),
        "Replacement must exclude lexicon entries explicitly marked unsafe.");

    var napoli = suggestions.First(c => c.Word.Equals("NAPOLI", StringComparison.OrdinalIgnoreCase));
    var replacement = WordSearchReplacementService.Replace(project, first, 1, napoli);
    Require(replacement.Success, "Contextual replacement must succeed for a still-valid word position.");
    first = WordSearchWorkspaceService.GetRecords(project).Single(r => r.ContentId == firstContentId);
    Require(first.Words[0] == "NAPOLI" && first.Id == "PUZ-001",
        "Replacement must change only the requested word and preserve puzzle identity.");

    // Stale interaction after the user changes/removes state must be harmless.
    var stale = WordSearchReplacementService.Replace(project, first, 999, napoli);
    Require(!stale.Success, "A stale word position must fail safely instead of corrupting the puzzle.");

    var columnXlsx = Path.Combine(tempRoot, "columns.xlsx");
    var columnCsv = Path.Combine(tempRoot, "columns.csv");
    Require((await WordSearchColumnExportService.ExportXlsxAsync(project, columnXlsx)).Success,
        "Column XLSX export must succeed.");
    Require((await WordSearchColumnExportService.ExportCsvAsync(project, columnCsv)).Success,
        "Column CSV export must succeed.");
    Require(File.Exists(columnXlsx) && new FileInfo(columnXlsx).Length > 0, "Column XLSX must contain data.");
    Require(File.Exists(columnCsv) && new FileInfo(columnCsv).Length > 0, "Column CSV must contain data.");

    var databaseXlsx = Path.Combine(tempRoot, "word-search-db.xlsx");
    Require((await WordSearchExportService.ExportDatabaseAsync(project, databaseXlsx)).Success,
        "Reimportable puzzle database export must succeed.");
    var freshDatabaseProject = ProjectFileStore.Create("Word Search reimport");
    BookTypeProfileService.Set(freshDatabaseProject, BookTypeProfileService.WordSearch);
    var dbImport = await WordSearchDatabaseService.ImportDatabaseAsync(
        freshDatabaseProject, databaseXlsx, Guid.Empty, replaceExisting: false);
    Require(dbImport.Recognized && dbImport.Added == 2,
        "Exported puzzle database must reimport both puzzles into a fresh project.");
    var importedFirst = WordSearchWorkspaceService.GetRecords(freshDatabaseProject).Single(r => r.Id == "PUZ-001");
    Require(importedFirst.Words.SequenceEqual(first.Words),
        "Puzzle words must round-trip through the reimportable database.");
    Require(WordSearchDatabaseService.ExpectedWordCount(freshDatabaseProject, importedFirst) == 5,
        "Expected word count must round-trip through the database export.");

    var fullDatabase = Path.Combine(tempRoot, "word-search-full.xlsx");
    Require((await WordSearchFullDatabaseExportService.ExportAsync(project, fullDatabase)).Success,
        "Full Word Search database export must succeed.");
    var freshFullProject = ProjectFileStore.Create("Word Search full reimport");
    BookTypeProfileService.Set(freshFullProject, BookTypeProfileService.WordSearch);
    var fullPuzzleImport = await WordSearchDatabaseService.ImportDatabaseAsync(
        freshFullProject, fullDatabase, Guid.Empty, replaceExisting: false);
    Require(fullPuzzleImport.Recognized && fullPuzzleImport.Added == 2,
        "Full database must expose a reimportable DATABASE sheet.");
    var fullLexiconImport = await WordSearchLexiconService.ImportXlsxAsync(freshFullProject, fullDatabase, "Reimportato");
    Require(fullLexiconImport.Recognized && WordSearchLexiconService.GetEntries(freshFullProject).Count == 5,
        "Full database must expose a reimportable classified lexicon sheet.");

    var packagePath = Path.Combine(tempRoot, "word-search-pianist.diez");
    for (var round = 0; round < 5; round++)
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ProjectFileStore.SaveAsync(packagePath, project)));
    var reloaded = await ProjectFileStore.LoadAsync(packagePath);
    Require(BookTypeProfileService.Get(reloaded) == BookTypeProfileService.WordSearch,
        "Word Search book identity must survive repeated concurrent saves.");
    var persisted = WordSearchWorkspaceService.GetRecords(reloaded);
    Require(persisted.Count == 2 && persisted.Single(r => r.Id == "PUZ-001").ContentId == firstContentId,
        "Puzzle count, stable puzzle ID and ContentId must survive package stress saves.");
    Require(WordSearchLexiconService.GetEntries(reloaded).Count == 5,
        "Lexicon must survive package stress saves without duplication.");
    Require(WordSearchDatabaseService.ExpectedWordCount(reloaded, persisted.Single(r => r.Id == "PUZ-001")) == 5,
        "Expected-count settings must survive package stress saves.");

    Console.WriteLine("WORD SEARCH PIANIST PASS: stable puzzles, lexicon, contextual replacement, validation and reimportable exports survived noisy use.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
