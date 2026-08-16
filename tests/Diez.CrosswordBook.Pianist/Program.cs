using System.Text.Json;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-crossword-book-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Cruciverba finale");
    BookTypeProfileService.Set(project, BookTypeProfileService.Crossword);
    CrosswordService.SetSetting(project, "PrimaryLanguage", "Italiano");
    CrosswordService.SetSetting(project, "Theme", "Napoli, mare e musica");

    var words = new[] { "NAPOLI", "MARE", "PIANOFORTE", "LUNA", "VESUVIO" };
    foreach (var word in words)
    {
        var entity = CrosswordService.EnsureWord(project, word, "Pianista finale");
        CrosswordService.SetDefinitionCell(project, entity.EntityId, 1, $"Definizione approvabile di {word}");
    }

    var incomplete = DiezCrosswordFinalizationBridge.Readiness(project);
    Require(!incomplete.Ready && incomplete.MissingApprovals == words.Length,
        "Il finalizzatore deve bloccare un vocabolario con definizioni non ancora approvate.");
    var blockedPath = Path.Combine(tempRoot, "blocked.txt");
    var blocked = await DiezCrosswordFinalizationBridge.ExportFinalQxwAsync(JsonSerializer.Serialize(project), blockedPath);
    Require(!blocked.Exported && !File.Exists(blockedPath),
        "L'handoff finale non deve creare file prima dell'approvazione delle definizioni.");

    foreach (var row in CrosswordService.DefinitionRows(project))
    {
        var entity = CrosswordService.FindWord(project, row.Word)!;
        CrosswordService.SetApproved(project, entity.EntityId, row.Definition1);
    }

    var ready = DiezCrosswordFinalizationBridge.Readiness(project);
    Require(ready.Ready && ready.WordCount == words.Length && ready.ApprovedWords == words.Length,
        "Ogni parola con almeno una definizione e una scelta approvata deve rendere pronto il cruciverba.");

    // Approval must point to one of the candidate definitions, not to arbitrary text.
    var napoli = CrosswordService.FindWord(project, "NAPOLI")!;
    CrosswordService.SetApproved(project, napoli.EntityId, "Testo arbitrario non candidato");
    var badApproval = DiezCrosswordFinalizationBridge.Readiness(project);
    Require(!badApproval.Ready && badApproval.MissingApprovals == 1,
        "Un testo arbitrario non deve essere accettato come definizione approvata finale.");
    var napoliRow = CrosswordService.DefinitionRows(project).Single(row => row.Word == "NAPOLI");
    CrosswordService.SetApproved(project, napoli.EntityId, napoliRow.Definition1);
    Require(DiezCrosswordFinalizationBridge.Readiness(project).Ready,
        "Ripristinata una definizione candidata valida, il gate deve recuperare.");

    var qxwPath = Path.Combine(tempRoot, "crossword-final.txt");
    var export = await DiezCrosswordFinalizationBridge.ExportFinalQxwAsync(JsonSerializer.Serialize(project), qxwPath);
    Require(export.Exported && File.Exists(qxwPath), "Il Qxw finale deve essere esportato quando il gate è READY.");
    var lines = await File.ReadAllLinesAsync(qxwPath);
    Require(lines.Length == words.Length && lines.SequenceEqual(lines.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
        "Il Qxw finale deve contenere tutte e sole le parole canoniche in ordine deterministico.");
    Require(lines.Distinct(StringComparer.OrdinalIgnoreCase).Count() == lines.Length,
        "Il Qxw finale non deve contenere duplicati.");

    var clues = DiezCrosswordFinalizationBridge.BuildApprovedCluesTsv(JsonSerializer.Serialize(project));
    Require(words.All(word => clues.Contains(word, StringComparison.Ordinal)),
        "Il TSV delle definizioni approvate deve contenere ogni parola finale.");

    var package = Path.Combine(tempRoot, "crossword-final.diez");
    await ProjectFileStore.SaveAsync(package, project);
    var reloaded = await ProjectFileStore.LoadAsync(package);
    Require(DiezCrosswordFinalizationBridge.Readiness(reloaded).Ready,
        "La readiness Cruciverba deve sopravvivere al round-trip .diez.");

    // Removing all definitions from one word must immediately revoke final readiness.
    var luna = CrosswordService.FindWord(reloaded, "LUNA")!;
    for (var i = 1; i <= 4; i++) CrosswordService.SetDefinitionCell(reloaded, luna.EntityId, i, "");
    var revoked = DiezCrosswordFinalizationBridge.Readiness(reloaded);
    Require(!revoked.Ready && revoked.MissingDefinitions == 1,
        "Rimuovere tutte le definizioni da una parola deve revocare immediatamente la readiness finale.");

    Console.WriteLine("CROSSWORD BOOK PIANIST PASS: final Qxw stayed blocked until every canonical word had a valid approved clue; deterministic handoff and readiness survived package round-trip.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
