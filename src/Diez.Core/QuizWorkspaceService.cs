using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal sealed class QuizQuestionRecord
{
    public Guid ContentId { get; set; }
    public int Order { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> Answers { get; set; } = [];
    public int CorrectAnswerIndex { get; set; } = -1;
    public string Category { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Status { get; set; } = StatusToReview;
    public string Notes { get; set; } = string.Empty;
    public string UpdatedAtLocal { get; set; } = string.Empty;

    public const string StatusToReview = "Da controllare";
    public const string StatusApproved = "Approvato";
    public const string StatusNeedsRevision = "Da rifare";
}

internal sealed record QuizQuestionIssueSummary(
    bool MissingQuestion,
    bool WrongAnswerCount,
    bool InvalidCorrectAnswer,
    bool DuplicateAnswers,
    bool MissingExplanation,
    IReadOnlyList<string> Messages)
{
    public bool HasProblems => MissingQuestion || WrongAnswerCount || InvalidCorrectAnswer || DuplicateAnswers || MissingExplanation;
}

internal static class QuizWorkspaceService
{
    public const string NodeKind = "QuizQuestion";
    private static readonly Regex IdRegex = new(@"(?:Q|QUIZ)[-_ ]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StoredQuestion
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> Answers { get; set; } = [];
        public int CorrectAnswerIndex { get; set; } = -1;
        public string Category { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string Status { get; set; } = QuizQuestionRecord.StatusToReview;
        public string Notes { get; set; } = string.Empty;
        public string UpdatedAtLocal { get; set; } = string.Empty;
    }

    public static List<QuizQuestionRecord> GetRecords(PreviewProject project) => project.ContentNodes
        .Where(node => string.Equals(node.Kind, NodeKind, StringComparison.OrdinalIgnoreCase))
        .Select(ToRecord)
        .OrderBy(record => record.Order <= 0 ? int.MaxValue : record.Order)
        .ThenBy(record => Number(record.Id))
        .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static QuizQuestionRecord AddNew(PreviewProject project)
    {
        var records = GetRecords(project);
        var next = Math.Max(1, records.Select(record => Number(record.Id)).DefaultIfEmpty(0).Max() + 1);
        var record = new QuizQuestionRecord
        {
            ContentId = Guid.NewGuid(),
            Order = records.Select(record => record.Order).DefaultIfEmpty(0).Max() + 1,
            Id = $"Q-{next:D3}",
            Difficulty = ReadOption(project, "Difficulty", "Media"),
            Status = QuizQuestionRecord.StatusToReview,
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        Save(project, record);
        return record;
    }

    public static void Save(PreviewProject project, QuizQuestionRecord record)
    {
        record.ContentId = record.ContentId == Guid.Empty ? Guid.NewGuid() : record.ContentId;
        record.Id = EnsureId(project, record.Id, record.ContentId);
        record.Order = record.Order <= 0 ? GetRecords(project).Select(r => r.Order).DefaultIfEmpty(0).Max() + 1 : record.Order;
        record.Question = Clean(record.Question);
        record.Answers = (record.Answers ?? []).Select(Clean).Where(value => value.Length > 0).ToList();
        record.Category = Clean(record.Category);
        record.Difficulty = Clean(record.Difficulty);
        record.Explanation = Clean(record.Explanation);
        record.Notes = (record.Notes ?? string.Empty).Trim();
        record.Status = NormalizeStatus(record.Status);
        record.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");

        var node = project.ContentNodes.FirstOrDefault(existing =>
            existing.ContentId == record.ContentId && string.Equals(existing.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new ContentNode { ContentId = record.ContentId };
            project.ContentNodes.Add(node);
        }
        node.Kind = NodeKind;
        node.Title = record.Question;
        node.Ordinal = record.Order;
        node.SourceLocator = record.Id;
        node.Body = JsonSerializer.Serialize(new StoredQuestion
        {
            Answers = record.Answers,
            CorrectAnswerIndex = record.CorrectAnswerIndex,
            Category = record.Category,
            Difficulty = record.Difficulty,
            Explanation = record.Explanation,
            Status = record.Status,
            Notes = record.Notes,
            UpdatedAtLocal = record.UpdatedAtLocal
        }, JsonOptions);
    }

    public static void Delete(PreviewProject project, Guid contentId) =>
        project.ContentNodes.RemoveAll(node => node.ContentId == contentId && string.Equals(node.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));

    public static QuizQuestionIssueSummary Analyze(PreviewProject project, QuizQuestionRecord record)
    {
        var expectedAnswers = ReadPositiveIntOption(project, "AnswersPerQuestion", 4);
        var includeExplanation = ReadBoolOption(project, "IncludeExplanations", true);
        var missingQuestion = string.IsNullOrWhiteSpace(record.Question);
        var wrongAnswerCount = record.Answers.Count != expectedAnswers;
        var invalidCorrect = record.CorrectAnswerIndex < 0 || record.CorrectAnswerIndex >= record.Answers.Count;
        var duplicateAnswers = record.Answers.Select(Key).Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);
        var missingExplanation = includeExplanation && string.IsNullOrWhiteSpace(record.Explanation);
        var messages = new List<string>();
        if (missingQuestion) messages.Add("Manca la domanda.");
        if (wrongAnswerCount) messages.Add($"Risposte: {record.Answers.Count}/{expectedAnswers}.");
        if (invalidCorrect) messages.Add("La risposta corretta non è selezionata o non esiste.");
        if (duplicateAnswers) messages.Add("La stessa risposta compare più volte nella domanda.");
        if (missingExplanation) messages.Add("Manca la spiegazione richiesta dalle opzioni del libro.");
        if (messages.Count == 0) messages.Add("Domanda strutturalmente completa.");
        return new QuizQuestionIssueSummary(missingQuestion, wrongAnswerCount, invalidCorrect, duplicateAnswers, missingExplanation, messages);
    }

    public static int ExpectedQuestionCount(PreviewProject project) => ReadPositiveIntOption(project, "QuestionCount", 100);
    public static bool NoDuplicates(PreviewProject project) => ReadBoolOption(project, "NoDuplicates", true);

    public static IReadOnlyDictionary<string, List<QuizQuestionRecord>> DuplicateQuestions(PreviewProject project)
    {
        if (!NoDuplicates(project)) return new Dictionary<string, List<QuizQuestionRecord>>();
        return GetRecords(project)
            .Where(record => Key(record.Question).Length > 0)
            .GroupBy(record => Key(record.Question), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static QuizQuestionRecord ToRecord(ContentNode node)
    {
        StoredQuestion payload;
        try { payload = JsonSerializer.Deserialize<StoredQuestion>(node.Body ?? string.Empty, JsonOptions) ?? new StoredQuestion(); }
        catch { payload = new StoredQuestion { Notes = node.Body ?? string.Empty }; }
        return new QuizQuestionRecord
        {
            ContentId = node.ContentId,
            Order = node.Ordinal,
            Id = NormalizeId(node.SourceLocator),
            Question = node.Title ?? string.Empty,
            Answers = payload.Answers ?? [],
            CorrectAnswerIndex = payload.CorrectAnswerIndex,
            Category = payload.Category ?? string.Empty,
            Difficulty = payload.Difficulty ?? string.Empty,
            Explanation = payload.Explanation ?? string.Empty,
            Status = NormalizeStatus(payload.Status),
            Notes = payload.Notes ?? string.Empty,
            UpdatedAtLocal = payload.UpdatedAtLocal ?? string.Empty
        };
    }

    private static string EnsureId(PreviewProject project, string? id, Guid contentId)
    {
        var normalized = NormalizeId(id);
        var used = GetRecords(project).Where(record => record.ContentId != contentId)
            .Select(record => record.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Length > 0 && !used.Contains(normalized)) return normalized;
        var next = Math.Max(1, used.Select(Number).DefaultIfEmpty(0).Max() + 1);
        while (used.Contains($"Q-{next:D3}")) next++;
        return $"Q-{next:D3}";
    }

    private static string NormalizeId(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        var match = IdRegex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? $"Q-{number:D3}" : text.ToUpperInvariant();
    }

    private static int Number(string? id)
    {
        var match = IdRegex.Match(id ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : 0;
    }

    private static string NormalizeStatus(string? status) => status?.Trim() switch
    {
        QuizQuestionRecord.StatusApproved => QuizQuestionRecord.StatusApproved,
        QuizQuestionRecord.StatusNeedsRevision => QuizQuestionRecord.StatusNeedsRevision,
        _ => QuizQuestionRecord.StatusToReview
    };

    private static string ReadOption(PreviewProject project, string key, string fallback)
    {
        var definition = BookTypeAiOptionsCoreService.Definitions(project).FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        return definition is null ? fallback : BookTypeAiOptionsCoreService.Get(project, definition);
    }

    private static int ReadPositiveIntOption(PreviewProject project, string key, int fallback) =>
        int.TryParse(ReadOption(project, key, fallback.ToString()), out var value) && value > 0 ? value : fallback;

    private static bool ReadBoolOption(PreviewProject project, string key, bool fallback) =>
        bool.TryParse(ReadOption(project, key, fallback.ToString().ToLowerInvariant()), out var value) ? value : fallback;

    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Key(string? value) => Clean(value).ToUpperInvariant();
}
