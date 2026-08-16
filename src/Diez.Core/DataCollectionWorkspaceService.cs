using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal sealed class DataCollectionRecord
{
    public Guid ContentId { get; set; }
    public int Order { get; set; }
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Provenance { get; set; } = string.Empty;
    public string Status { get; set; } = StatusToReview;
    public string Notes { get; set; } = string.Empty;
    public string UpdatedAtLocal { get; set; } = string.Empty;

    public const string StatusToReview = "Da controllare";
    public const string StatusApproved = "Approvato";
    public const string StatusNeedsRevision = "Da rifare";
}

internal static class DataCollectionWorkspaceService
{
    public const string NodeKind = "DataCollectionItem";
    private static readonly Regex IdRegex = new(@"(?:ITEM|REC|R)[-_ ]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StoredRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Provenance { get; set; } = string.Empty;
        public string Status { get; set; } = DataCollectionRecord.StatusToReview;
        public string Notes { get; set; } = string.Empty;
        public string UpdatedAtLocal { get; set; } = string.Empty;
    }

    public static List<DataCollectionRecord> GetRecords(PreviewProject project) => project.ContentNodes
        .Where(node => string.Equals(node.Kind, NodeKind, StringComparison.OrdinalIgnoreCase))
        .Select(ToRecord)
        .OrderBy(record => record.Order <= 0 ? int.MaxValue : record.Order)
        .ThenBy(record => Number(record.Id))
        .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static IReadOnlyList<string> Fields(PreviewProject project)
    {
        var raw = ReadOption(project, "Fields", "Nome\nCategoria\nDescrizione\nFonte");
        return raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Clean)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int ExpectedItemCount(PreviewProject project) => ReadPositiveIntOption(project, "ItemCount", 100);
    public static bool Deduplicate(PreviewProject project) => ReadBoolOption(project, "Deduplicate", true);
    public static bool TrackProvenance(PreviewProject project) => ReadBoolOption(project, "TrackProvenance", true);

    public static DataCollectionRecord AddNew(PreviewProject project)
    {
        var records = GetRecords(project);
        var next = Math.Max(1, records.Select(record => Number(record.Id)).DefaultIfEmpty(0).Max() + 1);
        var record = new DataCollectionRecord
        {
            ContentId = Guid.NewGuid(),
            Order = records.Select(record => record.Order).DefaultIfEmpty(0).Max() + 1,
            Id = $"ITEM-{next:D3}",
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        Save(project, record);
        return record;
    }

    public static void Save(PreviewProject project, DataCollectionRecord record)
    {
        record.ContentId = record.ContentId == Guid.Empty ? Guid.NewGuid() : record.ContentId;
        record.Id = EnsureId(project, record.Id, record.ContentId);
        record.Order = record.Order <= 0 ? GetRecords(project).Select(r => r.Order).DefaultIfEmpty(0).Max() + 1 : record.Order;
        var configuredFields = Fields(project);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in configuredFields)
            normalized[field] = record.Values.TryGetValue(field, out var value) ? Clean(value) : string.Empty;
        foreach (var pair in record.Values.Where(pair => !normalized.ContainsKey(pair.Key)))
            normalized[Clean(pair.Key)] = Clean(pair.Value);
        record.Values = normalized;
        record.Provenance = Clean(record.Provenance);
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
        node.Title = DisplayTitle(record, configuredFields);
        node.Ordinal = record.Order;
        node.SourceLocator = record.Id;
        node.Body = JsonSerializer.Serialize(new StoredRecord
        {
            Values = record.Values,
            Provenance = record.Provenance,
            Status = record.Status,
            Notes = record.Notes,
            UpdatedAtLocal = record.UpdatedAtLocal
        }, JsonOptions);
    }

    public static void Delete(PreviewProject project, Guid contentId) =>
        project.ContentNodes.RemoveAll(node => node.ContentId == contentId && string.Equals(node.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Issues(PreviewProject project, DataCollectionRecord record)
    {
        var fields = Fields(project);
        var messages = new List<string>();
        var missing = fields.Where(field => !record.Values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value)).ToList();
        if (missing.Count > 0) messages.Add("Campi mancanti: " + string.Join(", ", missing) + ".");
        if (TrackProvenance(project) && string.IsNullOrWhiteSpace(record.Provenance)) messages.Add("Manca la provenienza del record.");
        if (messages.Count == 0) messages.Add("Record completo secondo i campi configurati.");
        return messages;
    }

    public static IReadOnlyDictionary<string, List<DataCollectionRecord>> Duplicates(PreviewProject project)
    {
        if (!Deduplicate(project)) return new Dictionary<string, List<DataCollectionRecord>>();
        var fields = Fields(project);
        return GetRecords(project)
            .Select(record => (Record: record, Signature: Signature(record, fields)))
            .Where(item => item.Signature.Length > 0)
            .GroupBy(item => item.Signature, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Record).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static string Signature(DataCollectionRecord record, IReadOnlyList<string> fields)
    {
        var values = fields.Select(field => record.Values.TryGetValue(field, out var value) ? Key(value) : string.Empty).ToList();
        return values.All(string.IsNullOrWhiteSpace) ? string.Empty : string.Join('\u001F', values);
    }

    private static string DisplayTitle(DataCollectionRecord record, IReadOnlyList<string> fields)
    {
        foreach (var field in fields)
            if (record.Values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return record.Id;
    }

    private static DataCollectionRecord ToRecord(ContentNode node)
    {
        StoredRecord payload;
        try { payload = JsonSerializer.Deserialize<StoredRecord>(node.Body ?? string.Empty, JsonOptions) ?? new StoredRecord(); }
        catch { payload = new StoredRecord { Notes = node.Body ?? string.Empty }; }
        return new DataCollectionRecord
        {
            ContentId = node.ContentId,
            Order = node.Ordinal,
            Id = NormalizeId(node.SourceLocator),
            Values = payload.Values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Provenance = payload.Provenance ?? string.Empty,
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
        while (used.Contains($"ITEM-{next:D3}")) next++;
        return $"ITEM-{next:D3}";
    }

    private static string NormalizeId(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        var match = IdRegex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? $"ITEM-{number:D3}" : text.ToUpperInvariant();
    }

    private static int Number(string? id)
    {
        var match = IdRegex.Match(id ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : 0;
    }

    private static string NormalizeStatus(string? status) => status?.Trim() switch
    {
        DataCollectionRecord.StatusApproved => DataCollectionRecord.StatusApproved,
        DataCollectionRecord.StatusNeedsRevision => DataCollectionRecord.StatusNeedsRevision,
        _ => DataCollectionRecord.StatusToReview
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
