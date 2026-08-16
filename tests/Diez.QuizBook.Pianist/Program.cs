using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static BookTypeAiOptionDefinition Option(PreviewProject project, string key) =>
    BookTypeAiOptionsCoreService.Definitions(project).Single(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-quiz-book-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Quiz pianist");
    BookTypeProfileService.Set(project, BookTypeProfileService.Quiz);
    BookTypeAiOptionsCoreService.Set(project, Option(project, "QuestionCount"), "75");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "AnswersPerQuestion"), "4");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "Difficulty"), "Mista");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "NoDuplicates"), "true");
    BookTypeAiOptionsCoreService.Set(project, Option(project, "IncludeExplanations"), "true");

    for (var index = 1; index <= 75; index++)
    {
        var record = QuizWorkspaceService.AddNew(project);
        record.Question = $"Qual è la risposta univoca alla domanda numero {index:D3}?";
        record.Answers = [$"Risposta A {index:D3}", $"Risposta B {index:D3}", $"Risposta C {index:D3}", $"Risposta D {index:D3}"];
        record.CorrectAnswerIndex = index % 4;
        record.Category = index % 2 == 0 ? "Categoria pari" : "Categoria dispari";
        record.Difficulty = index % 3 == 0 ? "Difficile" : "Media";
        record.Explanation = $"Spiegazione verificabile della domanda {index:D3}.";
        record.Status = QuizQuestionRecord.StatusApproved;
        QuizWorkspaceService.Save(project, record);
    }

    var json = JsonSerializer.Serialize(project);
    var ready = DiezQuizFrontendBridge.Read(json);
    Require(ready.Ready && ready.ExpectedQuestions == 75 && ready.PresentQuestions == 75 && ready.ApprovedQuestions == 75,
        "75 domande valide, uniche e approvate devono rendere READY il Quiz configurato per 75.");
    Require(ready.Questions.Select(question => question.QuestionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 75,
        "Gli ID domanda devono essere unici e stabili.");

    var q1 = ready.Questions.Single(question => question.QuestionId == "Q-001");
    var raw = JsonNode.Parse(json)!.AsObject();
    raw["FutureRoot"] = new JsonObject { ["Marker"] = "preserve" };
    var q1Node = raw["ContentNodes"]!.AsArray().OfType<JsonObject>()
        .Single(node => node["ContentId"]?.GetValue<string>() == q1.ContentId.ToString());
    q1Node["FutureQuizField"] = "preserve-question-extension";
    var extendedJson = raw.ToJsonString();

    var update = DiezQuizFrontendBridge.SaveQuestion(
        extendedJson,
        q1.ContentId,
        q1.QuestionId,
        q1.Question + " aggiornamento",
        q1.Answers,
        q1.CorrectAnswer,
        q1.Category,
        q1.Difficulty,
        q1.Explanation,
        q1.Status,
        "Aggiornata dal pianist");
    Require(update.SelectedId == q1.ContentId && update.State.PresentQuestions == 75,
        "Aggiornare Q-001 non deve cambiare ContentId né duplicare la domanda.");
    var afterUpdateRaw = JsonNode.Parse(update.ProjectJson)!.AsObject();
    Require(afterUpdateRaw["FutureRoot"]?["Marker"]?.GetValue<string>() == "preserve",
        "Il bridge Quiz deve preservare estensioni future alla root.");
    Require(afterUpdateRaw["ContentNodes"]!.AsArray().OfType<JsonObject>()
            .Single(node => node["ContentId"]?.GetValue<string>() == q1.ContentId.ToString())["FutureQuizField"]?.GetValue<string>() == "preserve-question-extension",
        "Aggiornare una domanda non deve cancellare campi futuri del ContentNode.");

    // Whole-book duplicate: make Q-075 equivalent to Q-001 after normalization.
    var state = update.State;
    var q75 = state.Questions.Single(question => question.QuestionId == "Q-075");
    var q1Updated = state.Questions.Single(question => question.QuestionId == "Q-001");
    var duplicate = DiezQuizFrontendBridge.SaveQuestion(
        update.ProjectJson,
        q75.ContentId,
        q75.QuestionId,
        "   " + q1Updated.Question.ToLowerInvariant() + "   ",
        q75.Answers,
        q75.CorrectAnswer,
        q75.Category,
        q75.Difficulty,
        q75.Explanation,
        q75.Status,
        q75.Notes);
    Require(!duplicate.State.Ready && duplicate.State.DuplicateQuestions == 1,
        "Una domanda duplicata tra Q-001 e Q-075 deve bloccare l'intero libro Quiz.");
    var blocked = Path.Combine(tempRoot, "blocked.csv");
    var blockedExport = await DiezQuizFrontendBridge.ExportFinalCsvAsync(duplicate.ProjectJson, blocked);
    Require(!blockedExport.Exported && !File.Exists(blocked),
        "Il CSV finale non deve essere creato mentre esiste un duplicato globale.");

    // Structural invalidity: wrong answer count, missing explanation and invalid correct answer all block.
    var structurallyInvalid = DiezQuizFrontendBridge.SaveQuestion(
        update.ProjectJson,
        q75.ContentId,
        q75.QuestionId,
        q75.Question,
        new[] { "Solo A", "Solo B" },
        9,
        q75.Category,
        q75.Difficulty,
        "",
        QuizQuestionRecord.StatusApproved,
        q75.Notes);
    Require(!structurallyInvalid.State.Ready && structurallyInvalid.State.InvalidQuestions == 1,
        "Numero risposte errato, risposta corretta invalida e spiegazione mancante devono bloccare il Quiz.");

    // Restore Q-075, then final export must contain all 75 questions.
    var restored = DiezQuizFrontendBridge.SaveQuestion(
        structurallyInvalid.ProjectJson,
        q75.ContentId,
        q75.QuestionId,
        q75.Question,
        q75.Answers,
        q75.CorrectAnswer,
        q75.Category,
        q75.Difficulty,
        q75.Explanation,
        q75.Status,
        q75.Notes);
    Require(restored.State.Ready, "Ripristinata Q-075, il Quiz deve tornare READY.");

    var finalPath = Path.Combine(tempRoot, "quiz-final.csv");
    var finalExport = await DiezQuizFrontendBridge.ExportFinalCsvAsync(restored.ProjectJson, finalPath);
    Require(finalExport.Exported && File.Exists(finalPath) && new FileInfo(finalPath).Length > 0,
        "Il Quiz completo deve esportare un CSV finale reale.");
    var lines = await File.ReadAllLinesAsync(finalPath);
    Require(lines.Length == 76, "Il CSV finale deve avere una riga header + 75 domande.");
    Require(lines.Count(line => line.Contains("Q-001", StringComparison.Ordinal)) == 1 &&
            lines.Count(line => line.Contains("Q-075", StringComparison.Ordinal)) == 1,
        "Il CSV finale deve contenere una sola volta gli ID estremi Q-001 e Q-075.");

    // Quantity is exact: 74/75 must revoke readiness.
    var removedState = DiezQuizFrontendBridge.DeleteQuestion(restored.ProjectJson, q75.ContentId);
    Require(!removedState.State.Ready && removedState.State.PresentQuestions == 74 && removedState.State.ExpectedQuestions == 75,
        "Un Quiz configurato per 75 domande non deve risultare pronto con 74.");

    Console.WriteLine("QUIZ BOOK PIANIST PASS: 75-question quantity, stable IDs, whole-book duplicate detection, answer/explanation validation, migration-safe updates and final CSV handoff survived stress.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
