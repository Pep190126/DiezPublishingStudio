using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezQuizQuestionDto(
    Guid ContentId,
    string QuestionId,
    int Order,
    string Question,
    IReadOnlyList<string> Answers,
    int CorrectAnswer,
    string Category,
    string Difficulty,
    string Explanation,
    string Status,
    string Notes,
    IReadOnlyList<string> Issues);

public sealed record DiezQuizState(
    int ExpectedQuestions,
    int PresentQuestions,
    int ApprovedQuestions,
    int DuplicateQuestions,
    int InvalidQuestions,
    bool Ready,
    IReadOnlyList<DiezQuizQuestionDto> Questions,
    IReadOnlyList<string> Messages);

public sealed record DiezQuizMutation(
    string ProjectJson,
    string Status,
    string Message,
    Guid? SelectedId,
    DiezQuizState State);

public sealed record DiezQuizExportResult(bool Exported, string Message, string? OutputPath);

/// <summary>
/// Canonical Quiz boundary: stable question IDs, structured answers/correct answer,
/// whole-book duplicate detection and final CSV handoff gated by configured quantity and approvals.
/// </summary>
public static class DiezQuizFrontendBridge
{
    public static DiezQuizState Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return State(project);
    }

    public static DiezQuizMutation SaveQuestion(
        string projectJson,
        Guid? contentId,
        string? questionId,
        string? question,
        IEnumerable<string>? answers,
        int correctAnswer,
        string? category,
        string? difficulty,
        string? explanation,
        string? status,
        string? notes)
    {
        var (root, project) = Parse(projectJson);
        EnsureQuiz(project);
        var existing = contentId is Guid id && id != Guid.Empty
            ? QuizWorkspaceService.GetRecords(project).FirstOrDefault(record => record.ContentId == id)
            : null;
        var record = existing ?? new QuizQuestionRecord { ContentId = Guid.NewGuid() };
        record.Id = questionId ?? record.Id;
        record.Question = question ?? string.Empty;
        record.Answers = (answers ?? []).SelectMany(SplitAnswer).Where(value => value.Length > 0).ToList();
        record.CorrectAnswerIndex = correctAnswer - 1;
        record.Category = category ?? string.Empty;
        record.Difficulty = difficulty ?? string.Empty;
        record.Explanation = explanation ?? string.Empty;
        record.Status = status ?? QuizQuestionRecord.StatusToReview;
        record.Notes = notes ?? string.Empty;
        QuizWorkspaceService.Save(project, record);
        MergeProject(root, project);
        return new DiezQuizMutation(Write(root), "SAVED", $"{record.Id} salvata nel Quiz canonico.", record.ContentId, State(project));
    }

    public static DiezQuizMutation DeleteQuestion(string projectJson, Guid contentId)
    {
        var (root, project) = Parse(projectJson);
        var exists = QuizWorkspaceService.GetRecords(project).Any(record => record.ContentId == contentId);
        if (!exists) return new DiezQuizMutation(Write(root), "NOT_FOUND", "Domanda Quiz non trovata.", null, State(project));
        QuizWorkspaceService.Delete(project, contentId);
        MergeProject(root, project);
        return new DiezQuizMutation(Write(root), "DELETED", "Domanda rimossa dal Quiz canonico.", null, State(project));
    }

    public static string BuildCsv(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        return BuildCsv(project);
    }

    public static async Task<DiezQuizExportResult> ExportFinalCsvAsync(string projectJson, string outputPath)
    {
        var (_, project) = Parse(projectJson);
        var state = State(project);
        if (!state.Ready)
            return new DiezQuizExportResult(false, "Export finale Quiz bloccato: " + string.Join(" ", state.Messages.Take(3)), null);
        if (string.IsNullOrWhiteSpace(outputPath))
            return new DiezQuizExportResult(false, "Percorso di esportazione non valido.", null);
        var fullPath = EnsureExtension(Path.GetFullPath(outputPath), ".csv");
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, BuildCsv(project), new UTF8Encoding(true));
        return new DiezQuizExportResult(true, $"Quiz finale esportato: {Path.GetFileName(fullPath)} · {state.PresentQuestions} domande.", fullPath);
    }

    private static DiezQuizState State(PreviewProject project)
    {
        var records = QuizWorkspaceService.GetRecords(project);
        var expected = QuizWorkspaceService.ExpectedQuestionCount(project);
        var duplicates = QuizWorkspaceService.DuplicateQuestions(project);
        var dto = records.Select(record =>
        {
            var issues = QuizWorkspaceService.Analyze(project, record);
            return new DiezQuizQuestionDto(
                record.ContentId,
                record.Id,
                record.Order,
                record.Question,
                record.Answers.ToList(),
                record.CorrectAnswerIndex + 1,
                record.Category,
                record.Difficulty,
                record.Explanation,
                record.Status,
                record.Notes,
                issues.Messages.ToList());
        }).ToList();
        var invalid = records.Count(record => QuizWorkspaceService.Analyze(project, record).HasProblems);
        var approved = records.Count(record => string.Equals(record.Status, QuizQuestionRecord.StatusApproved, StringComparison.OrdinalIgnoreCase));
        var messages = new List<string>();
        if (records.Count != expected) messages.Add($"Domande: {records.Count}/{expected}.");
        if (invalid > 0) messages.Add($"{invalid} domande hanno struttura incompleta o incoerente.");
        if (duplicates.Count > 0) messages.Add($"{duplicates.Count} domande duplicate nell'intero libro.");
        if (approved != records.Count) messages.Add($"Domande approvate: {approved}/{records.Count}.");
        if (messages.Count == 0) messages.Add($"Quiz completo: {records.Count}/{expected} domande, tutte valide, uniche e approvate.");
        var ready = records.Count == expected && invalid == 0 && duplicates.Count == 0 && records.Count > 0 && approved == records.Count;
        return new DiezQuizState(expected, records.Count, approved, duplicates.Count, invalid, ready, dto, messages);
    }

    private static string BuildCsv(PreviewProject project)
    {
        var records = QuizWorkspaceService.GetRecords(project);
        var maxAnswers = Math.Max(1, records.Select(record => record.Answers.Count).DefaultIfEmpty(0).Max());
        var builder = new StringBuilder();
        var headers = new List<string> { "ID", "ORDINE", "DOMANDA", "CATEGORIA", "DIFFICOLTA" };
        headers.AddRange(Enumerable.Range(1, maxAnswers).Select(index => $"RISPOSTA {index}"));
        headers.AddRange(["RISPOSTA CORRETTA", "SPIEGAZIONE", "STATO", "NOTE"]);
        AppendCsv(builder, headers);
        foreach (var record in records.OrderBy(record => record.Order).ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
        {
            var row = new List<string>
            {
                record.Id,
                record.Order.ToString(),
                record.Question,
                record.Category,
                record.Difficulty
            };
            row.AddRange(Enumerable.Range(0, maxAnswers).Select(index => index < record.Answers.Count ? record.Answers[index] : string.Empty));
            row.Add(record.CorrectAnswerIndex >= 0 && record.CorrectAnswerIndex < record.Answers.Count ? record.Answers[record.CorrectAnswerIndex] : string.Empty);
            row.Add(record.Explanation);
            row.Add(record.Status);
            row.Add(record.Notes);
            AppendCsv(builder, row);
        }
        return builder.ToString();
    }

    private static IEnumerable<string> SplitAnswer(string? answer) =>
        (answer ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void EnsureQuiz(PreviewProject project)
    {
        var current = BookTypeProfileService.Get(project);
        if (!string.Equals(current, BookTypeProfileService.Quiz, StringComparison.OrdinalIgnoreCase))
            BookTypeProfileService.Set(project, BookTypeProfileService.Quiz);
    }

    private static (JsonObject Root, PreviewProject Project) Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeProject(JsonObject root, PreviewProject project)
    {
        MergeArray(root, "ContentNodes", project.ContentNodes, "ContentId");
        MergeArray(root, "Entities", project.Entities, "EntityId");
    }

    private static void MergeArray<T>(JsonObject root, string property, IEnumerable<T> typedItems, string idProperty)
    {
        var raw = root[property] as JsonArray ?? new JsonArray();
        root[property] = raw;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in typedItems)
        {
            if (JsonSerializer.SerializeToNode(item, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed[idProperty]);
            if (id.Length == 0) continue;
            ids.Add(id);
            var existing = raw.OfType<JsonObject>().FirstOrDefault(obj => string.Equals(Scalar(obj[idProperty]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) raw.Add(typed);
            else foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
        for (var index = raw.Count - 1; index >= 0; index--)
        {
            if (raw[index] is not JsonObject obj) continue;
            var id = Scalar(obj[idProperty]);
            if (id.Length > 0 && !ids.Contains(id)) raw.RemoveAt(index);
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static void AppendCsv(StringBuilder builder, IEnumerable<string> values) =>
        builder.AppendLine(string.Join(';', values.Select(value => '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"')));

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
