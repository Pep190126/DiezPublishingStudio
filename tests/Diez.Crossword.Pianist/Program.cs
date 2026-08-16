using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-crossword-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Crossword pianist");
    BookTypeProfileService.Set(project, BookTypeProfileService.Crossword);

    var anna = CrosswordService.EnsureWord(project, " Anna ");
    var sameAnna = CrosswordService.EnsureWord(project, "AN-NA");
    Require(anna.EntityId == sameAnna.EntityId, "Equivalent grid spellings must not create duplicate crossword words.");
    Require(anna.Name == "ANNA", "Crossword grid words must be normalized deterministically.");

    CrosswordService.SetDefinitionCell(project, anna.EntityId, 1, "Nome proprio femminile");
    CrosswordService.SetDefinitionCell(project, anna.EntityId, 2, "La protagonista del test");
    CrosswordService.SetNotes(project, anna.EntityId, "Verificare il contesto");
    CrosswordService.SetApproved(project, anna.EntityId, "Nome proprio femminile");
    CrosswordThemeService.SetRole(project, anna.EntityId, CrosswordThemeService.Required);

    var roleHammer = new[]
    {
        CrosswordThemeService.Required,
        CrosswordThemeService.Preferred,
        CrosswordThemeService.Normal,
        CrosswordThemeService.Fallback
    };
    for (var i = 0; i < 80; i++)
        CrosswordThemeService.SetRole(project, anna.EntityId, roleHammer[i % roleHammer.Length]);
    CrosswordThemeService.SetRole(project, anna.EntityId, CrosswordThemeService.Required);
    Require(CrosswordThemeService.GetRole(project, anna.EntityId) == CrosswordThemeService.Required,
        "Rapid role changes must leave exactly the last requested role.");
    Require(CrosswordThemeService.ByRole(project, CrosswordThemeService.Required).Count(w => w.EntityId == anna.EntityId) == 1,
        "Theme role indexing must not duplicate a word.");

    var dicPath = Path.Combine(tempRoot, "stress.dic");
    await File.WriteAllTextAsync(dicPath, "6\n# commento\nNAPOLI\nnapoli/AB\nPIANISTA\nPiano-Forte\nA\n");
    var import = await CrosswordService.ImportWordListAsync(project, dicPath);
    Require(import.Added == 3, "Dictionary import should add NAPOLI, PIANISTA and PIANOFORTE exactly once.");
    Require(import.Existing == 1, "Repeated NAPOLI in dictionary must be counted as existing.");
    Require(CrosswordService.Words(project).Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == CrosswordService.Words(project).Count,
        "Crossword vocabulary must remain unique after noisy import.");

    CrosswordService.SetSetting(project, "PrimaryLanguage", "Italiano");
    CrosswordService.SetSetting(project, "Theme", "Napoli e musica");
    var prompt = CrosswordService.BuildDefinitionPrompt(project);
    Require(prompt.Contains("Napoli e musica", StringComparison.Ordinal), "Definition prompt must carry the crossword theme.");
    Require(prompt.Contains(CrosswordService.Words(project).Count.ToString(), StringComparison.Ordinal),
        "Definition prompt must carry the actual vocabulary count.");

    var xlsxPath = Path.Combine(tempRoot, "definitions.xlsx");
    await CrosswordService.WriteDefinitionTemplateXlsxAsync(project, xlsxPath);
    Require(File.Exists(xlsxPath) && new FileInfo(xlsxPath).Length > 0, "Definition XLSX template must be produced.");

    // Import the workbook we just generated. Empty definition cells must not erase the definitions already held in the project.
    var before = CrosswordService.DefinitionRows(project).Single(r => r.Word == "ANNA");
    var xlsxImport = await CrosswordService.ImportDefinitionsXlsxAsync(project, xlsxPath);
    var after = CrosswordService.DefinitionRows(project).Single(r => r.Word == "ANNA");
    Require(xlsxImport.WordsCreated == 0, "Round-trip template import must not duplicate existing crossword words.");
    Require(after.Definition1 == before.Definition1 && after.Definition2 == before.Definition2,
        "Empty XLSX definition cells must not destroy existing definitions.");

    var qxwPath = Path.Combine(tempRoot, "qxw.txt");
    await CrosswordService.ExportQxwTextAsync(project, qxwPath);
    var qxwLines = await File.ReadAllLinesAsync(qxwPath);
    Require(qxwLines.SequenceEqual(qxwLines.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
        "Qxw handoff must be deterministically sorted.");
    Require(qxwLines.Distinct(StringComparer.OrdinalIgnoreCase).Count() == qxwLines.Length,
        "Qxw handoff must not contain duplicate words.");

    var packagePath = Path.Combine(tempRoot, "crossword-pianist.diez");
    for (var round = 0; round < 4; round++)
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ProjectFileStore.SaveAsync(packagePath, project)));
    var reloaded = await ProjectFileStore.LoadAsync(packagePath);
    Require(BookTypeProfileService.Get(reloaded) == BookTypeProfileService.Crossword,
        "Crossword identity must survive repeated package saves.");
    Require(CrosswordService.Words(reloaded).Count == CrosswordService.Words(project).Count,
        "Crossword vocabulary must survive repeated package saves without duplication.");
    var persistedAnna = CrosswordService.FindWord(reloaded, "anna");
    Require(persistedAnna is not null && persistedAnna.EntityId == anna.EntityId,
        "Crossword word identity must survive package save/reload.");
    Require(CrosswordThemeService.GetRole(reloaded, anna.EntityId) == CrosswordThemeService.Required,
        "Crossword theme role must survive package save/reload.");
    var persistedRow = CrosswordService.DefinitionRows(reloaded).Single(r => r.Word == "ANNA");
    Require(persistedRow.Definition1 == "Nome proprio femminile" && persistedRow.Approved == "Nome proprio femminile",
        "Definitions and approved clue must survive package save/reload.");

    Console.WriteLine("CROSSWORD PIANIST PASS: vocabulary, themed roles, definitions, XLSX and Qxw handoff survived noisy repeated use.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
