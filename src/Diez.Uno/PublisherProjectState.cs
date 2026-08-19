using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio.UnoSpike;

internal sealed record PublisherMaterialIntentDto(
    string IntentCode,
    string IntentLabel,
    string Instruction,
    string AiUsePolicy,
    string Fidelity,
    string Scope);

internal sealed record PublisherProjectHistoryItem(
    Guid HistoryId,
    DateTimeOffset CreatedAt,
    string ActionCode,
    string Label,
    string Note,
    Guid? ParentHistoryId,
    Guid BranchId,
    bool IsCurrent)
{
    public string Display => $"{(IsCurrent ? "●" : "○")} {CreatedAt:dd/MM/yyyy HH:mm:ss} · {Label}" +
                             (string.IsNullOrWhiteSpace(Note) ? string.Empty : $" · {Note}");
}

internal sealed record PublisherAiExchangeNames(
    string DateToken,
    int Version,
    string PromptPackFileName,
    string ResponseFileName,
    string PreviousExpectedResponseFileName);

/// <summary>
/// Publisher-facing canonical extensions that can evolve independently from the migration shell.
/// Project history, material intent and AI handoff naming are project data; selected tabs and pane
/// geometry are deliberately excluded because they are transient workspace state.
/// </summary>
internal static class PublisherProjectState
{
    private const string HistoryArrayName = "ProjectHistory";
    private const string HistoryStateName = "ProjectHistoryState";
    private const string NamingStateName = "AiExchangeNaming";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ApplyCoreJson")]
    private static extern void ApplyCoreJson(DiezProjectDocument document, string json);

    public static string ProjectId(DiezProjectDocument document)
    {
        var root = Parse(document);
        return ReadString(root, "ProjectId", "session-" + RuntimeHelpers.GetHashCode(document));
    }

    public static bool RemoveUiKey(DiezProjectDocument document, string key)
    {
        var root = Parse(document);
        if (root["UnoUiState"] is not JsonObject ui || !ui.Remove(key)) return false;
        Apply(document, root);
        return true;
    }

    public static int ReadUiInt(DiezProjectDocument document, string key, int fallback = 0)
    {
        var root = Parse(document);
        if (root["UnoUiState"] is not JsonObject ui || ui[key] is not JsonValue value) return fallback;
        return value.TryGetValue<int>(out var result) ? result : fallback;
    }

    public static PublisherMaterialIntentDto ReadMaterialIntent(DiezProjectDocument document, Guid materialId)
    {
        var material = FindMaterial(Parse(document), materialId);
        if (material?["PublisherIntent"] is not JsonObject intent)
            return new("UNASSIGNED", "Da decidere", string.Empty, "NEVER_SEND", "NOT_APPLICABLE", "PROJECT");

        return new PublisherMaterialIntentDto(
            ReadString(intent, "IntentCode", "UNASSIGNED"),
            ReadString(intent, "IntentLabel", "Da decidere"),
            ReadString(intent, "Instruction"),
            ReadString(intent, "AiUsePolicy", "NEVER_SEND"),
            ReadString(intent, "Fidelity", "NOT_APPLICABLE"),
            ReadString(intent, "Scope", "PROJECT"));
    }

    public static void SaveMaterialIntent(
        DiezProjectDocument document,
        Guid materialId,
        string intentCode,
        string intentLabel,
        string instruction,
        string aiUsePolicy,
        string fidelity,
        string scope = "PROJECT")
    {
        var root = Parse(document);
        var material = FindMaterial(root, materialId)
            ?? throw new InvalidOperationException("Il materiale selezionato non esiste più nel progetto.");
        material["PublisherIntent"] = new JsonObject
        {
            ["IntentCode"] = intentCode,
            ["IntentLabel"] = intentLabel,
            ["Instruction"] = instruction ?? string.Empty,
            ["AiUsePolicy"] = aiUsePolicy,
            ["Fidelity"] = fidelity,
            ["Scope"] = scope,
            ["UpdatedAtLocal"] = DateTimeOffset.Now.ToString("O")
        };
        Apply(document, root);
    }

    public static bool HasHistory(DiezProjectDocument document) =>
        Parse(document)[HistoryArrayName] is JsonArray history && history.Count > 0;

    public static void EnsureHistoryBaseline(DiezProjectDocument document, string label = "Stato iniziale")
    {
        if (HasHistory(document)) return;
        CreateCheckpoint(document, "BASELINE", label, "Punto di partenza della cronologia progetto.");
    }

    public static PublisherProjectHistoryItem CreateCheckpoint(
        DiezProjectDocument document,
        string actionCode,
        string label,
        string? note = null)
    {
        var root = Parse(document);
        var history = EnsureArray(root, HistoryArrayName);
        var state = EnsureObject(root, HistoryStateName);
        var currentId = ReadGuid(state, "CurrentHistoryId");
        var current = currentId.HasValue ? FindHistory(history, currentId.Value) : null;
        var currentBranch = current is null ? Guid.Empty : ReadGuid(current, "BranchId") ?? Guid.Empty;
        var hasForwardChildren = currentId.HasValue && history.OfType<JsonObject>()
            .Any(x => ReadGuid(x, "ParentHistoryId") == currentId);
        var branchId = currentBranch == Guid.Empty || hasForwardChildren ? Guid.NewGuid() : currentBranch;
        var historyId = Guid.NewGuid();
        var now = DateTimeOffset.Now;

        var snapshot = root.DeepClone() as JsonObject ?? new JsonObject();
        snapshot.Remove(HistoryArrayName);
        snapshot.Remove(HistoryStateName);
        snapshot.Remove("SavedAtLocal");
        if (snapshot["UnoUiState"] is JsonObject ui) ui.Remove("Visual.ActivePhase");
        var snapshotJson = snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var snapshotBytes = Encoding.UTF8.GetBytes(snapshotJson);

        history.Add(new JsonObject
        {
            ["HistoryId"] = historyId.ToString(),
            ["CreatedAt"] = now.ToString("O"),
            ["ActionCode"] = string.IsNullOrWhiteSpace(actionCode) ? "CHECKPOINT" : actionCode.Trim(),
            ["Label"] = string.IsNullOrWhiteSpace(label) ? "Checkpoint" : label.Trim(),
            ["Note"] = note ?? string.Empty,
            ["SnapshotHash"] = Convert.ToHexString(SHA256.HashData(snapshotBytes)).ToLowerInvariant(),
            ["SnapshotGzipBase64"] = Compress(snapshotBytes),
            ["ParentHistoryId"] = currentId?.ToString() ?? string.Empty,
            ["BranchId"] = branchId.ToString()
        });
        state["CurrentHistoryId"] = historyId.ToString();
        state["CurrentBranchId"] = branchId.ToString();
        Apply(document, root);

        return new PublisherProjectHistoryItem(
            historyId, now, actionCode, label, note ?? string.Empty, currentId, branchId, true);
    }

    public static IReadOnlyList<PublisherProjectHistoryItem> History(DiezProjectDocument document)
    {
        var root = Parse(document);
        if (root[HistoryArrayName] is not JsonArray history) return [];
        var current = root[HistoryStateName] is JsonObject state ? ReadGuid(state, "CurrentHistoryId") : null;
        return history.OfType<JsonObject>()
            .Select(x =>
            {
                var id = ReadGuid(x, "HistoryId") ?? Guid.Empty;
                var created = DateTimeOffset.TryParse(ReadString(x, "CreatedAt"), out var parsed)
                    ? parsed : DateTimeOffset.MinValue;
                return new PublisherProjectHistoryItem(
                    id,
                    created,
                    ReadString(x, "ActionCode", "CHECKPOINT"),
                    ReadString(x, "Label", "Checkpoint"),
                    ReadString(x, "Note"),
                    ReadGuid(x, "ParentHistoryId"),
                    ReadGuid(x, "BranchId") ?? Guid.Empty,
                    current == id);
            })
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public static bool RestoreHistory(DiezProjectDocument document, Guid historyId, out string message)
    {
        var root = Parse(document);
        if (root[HistoryArrayName] is not JsonArray history)
        {
            message = "La cronologia progetto è vuota.";
            return false;
        }
        var entry = FindHistory(history, historyId);
        if (entry is null)
        {
            message = "Checkpoint non trovato.";
            return false;
        }
        var encoded = ReadString(entry, "SnapshotGzipBase64");
        if (string.IsNullOrWhiteSpace(encoded))
        {
            message = "Il checkpoint non contiene uno snapshot ripristinabile.";
            return false;
        }

        try
        {
            var restored = JsonNode.Parse(Encoding.UTF8.GetString(Decompress(encoded))) as JsonObject
                ?? throw new InvalidDataException("Snapshot progetto non valido.");
            restored[HistoryArrayName] = history.DeepClone();
            var state = root[HistoryStateName]?.DeepClone() as JsonObject ?? new JsonObject();
            state["CurrentHistoryId"] = historyId.ToString();
            state["CurrentBranchId"] = ReadString(entry, "BranchId");
            restored[HistoryStateName] = state;
            if (restored["UnoUiState"] is JsonObject ui) ui.Remove("Visual.ActivePhase");
            Apply(document, restored);
            message = $"Ripristinato: {ReadString(entry, "Label", "Checkpoint")}. Puoi ancora tornare avanti dalla cronologia.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Ripristino non riuscito: " + ex.GetBaseException().Message;
            return false;
        }
    }

    public static bool MoveBack(DiezProjectDocument document, out string message)
    {
        var root = Parse(document);
        var history = root[HistoryArrayName] as JsonArray;
        var state = root[HistoryStateName] as JsonObject;
        var currentId = state is null ? null : ReadGuid(state, "CurrentHistoryId");
        var current = history is null || !currentId.HasValue ? null : FindHistory(history, currentId.Value);
        var parent = current is null ? null : ReadGuid(current, "ParentHistoryId");
        if (!parent.HasValue)
        {
            message = "Sei già al primo stato disponibile.";
            return false;
        }
        return RestoreHistory(document, parent.Value, out message);
    }

    public static bool MoveForward(DiezProjectDocument document, out string message)
    {
        var root = Parse(document);
        var history = root[HistoryArrayName] as JsonArray;
        var state = root[HistoryStateName] as JsonObject;
        var currentId = state is null ? null : ReadGuid(state, "CurrentHistoryId");
        if (history is null || !currentId.HasValue)
        {
            message = "Non c'è uno stato successivo disponibile.";
            return false;
        }
        var branchId = ReadGuid(state!, "CurrentBranchId");
        var children = history.OfType<JsonObject>()
            .Where(x => ReadGuid(x, "ParentHistoryId") == currentId)
            .OrderBy(x => ReadString(x, "CreatedAt"), StringComparer.Ordinal)
            .ToList();
        var target = branchId.HasValue
            ? children.FirstOrDefault(x => ReadGuid(x, "BranchId") == branchId) ?? children.LastOrDefault()
            : children.LastOrDefault();
        var targetId = target is null ? null : ReadGuid(target, "HistoryId");
        if (!targetId.HasValue)
        {
            message = "Non c'è uno stato successivo disponibile. Se esiste un ramo alternativo, selezionalo dalla cronologia.";
            return false;
        }
        return RestoreHistory(document, targetId.Value, out message);
    }

    public static PublisherAiExchangeNames PreviewNextAiExchange(DiezProjectDocument document, DateTimeOffset? now = null)
    {
        var root = Parse(document);
        var naming = EnsureObject(root, NamingStateName);
        var date = (now ?? DateTimeOffset.Now).ToString("yyyyMMdd");
        var lastDate = ReadString(naming, "LastIssuedDateToken");
        var lastVersion = ReadInt(naming, "LastIssuedVersion", 0);
        var nextVersion = string.Equals(lastDate, date, StringComparison.Ordinal) ? lastVersion + 1 : 1;
        var baseName = $"{SafeFileName(document.Name)}_{date}_v{nextVersion:D3}";
        return new PublisherAiExchangeNames(
            date,
            nextVersion,
            baseName + "_prompt-pack.zip",
            baseName + "_response.zip",
            ReadString(naming, "ExpectedResponseFileName"));
    }

    public static void StageAiExchange(DiezProjectDocument document, PublisherAiExchangeNames names)
    {
        var root = Parse(document);
        var naming = EnsureObject(root, NamingStateName);
        naming["PendingDateToken"] = names.DateToken;
        naming["PendingVersion"] = names.Version;
        naming["PendingPromptPackFileName"] = names.PromptPackFileName;
        naming["ExpectedResponseFileName"] = names.ResponseFileName;
        Apply(document, root);
    }

    public static void CommitAiExchange(DiezProjectDocument document, PublisherAiExchangeNames names)
    {
        var root = Parse(document);
        var naming = EnsureObject(root, NamingStateName);
        naming["LastIssuedDateToken"] = names.DateToken;
        naming["LastIssuedVersion"] = names.Version;
        naming["LastPromptPackFileName"] = names.PromptPackFileName;
        naming["ExpectedResponseFileName"] = names.ResponseFileName;
        naming["LastExpectedResponseFileName"] = names.ResponseFileName;
        naming["LastIssuedAtLocal"] = DateTimeOffset.Now.ToString("O");
        naming.Remove("PendingDateToken");
        naming.Remove("PendingVersion");
        naming.Remove("PendingPromptPackFileName");
        Apply(document, root);
    }

    public static void CancelStagedAiExchange(DiezProjectDocument document, PublisherAiExchangeNames names)
    {
        var root = Parse(document);
        var naming = EnsureObject(root, NamingStateName);
        naming["ExpectedResponseFileName"] = names.PreviousExpectedResponseFileName;
        naming.Remove("PendingDateToken");
        naming.Remove("PendingVersion");
        naming.Remove("PendingPromptPackFileName");
        Apply(document, root);
    }

    public static string ExpectedResponseFileName(DiezProjectDocument document)
    {
        var root = Parse(document);
        return root[NamingStateName] is JsonObject naming
            ? ReadString(naming, "ExpectedResponseFileName")
            : string.Empty;
    }

    public static void RecordProviderResponseFileName(DiezProjectDocument document, string? providerFileName)
    {
        var root = Parse(document);
        var naming = EnsureObject(root, NamingStateName);
        naming["LastProviderResponseFileName"] = providerFileName ?? string.Empty;
        naming["LastResponseImportedAtLocal"] = DateTimeOffset.Now.ToString("O");
        Apply(document, root);
    }

    public static string CanonicalPromptPackPath(string selectedPath, PublisherAiExchangeNames names)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(selectedPath));
        return Path.Combine(directory ?? Environment.CurrentDirectory, names.PromptPackFileName);
    }

    private static JsonObject Parse(DiezProjectDocument document) =>
        JsonNode.Parse(document.ExportProjectJson()) as JsonObject
        ?? throw new InvalidDataException("Il progetto Diez non contiene JSON valido.");

    private static void Apply(DiezProjectDocument document, JsonObject root) =>
        ApplyCoreJson(document, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    private static JsonObject? FindMaterial(JsonObject root, Guid materialId) =>
        (root["Materials"] as JsonArray)?.OfType<JsonObject>()
        .FirstOrDefault(x => ReadGuid(x, "MaterialId") == materialId);

    private static JsonObject? FindHistory(JsonArray history, Guid historyId) =>
        history.OfType<JsonObject>().FirstOrDefault(x => ReadGuid(x, "HistoryId") == historyId);

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing) return existing;
        var value = new JsonObject();
        parent[name] = value;
        return value;
    }

    private static JsonArray EnsureArray(JsonObject parent, string name)
    {
        if (parent[name] is JsonArray existing) return existing;
        var value = new JsonArray();
        parent[name] = value;
        return value;
    }

    private static string ReadString(JsonObject obj, string name, string fallback = "")
    {
        if (obj[name] is JsonValue value && value.TryGetValue<string>(out var result)) return result ?? fallback;
        return fallback;
    }

    private static int ReadInt(JsonObject obj, string name, int fallback = 0)
    {
        if (obj[name] is JsonValue value && value.TryGetValue<int>(out var result)) return result;
        return fallback;
    }

    private static Guid? ReadGuid(JsonObject obj, string name)
    {
        var raw = ReadString(obj, name);
        return Guid.TryParse(raw, out var result) && result != Guid.Empty ? result : null;
    }

    private static string Compress(byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(bytes, 0, bytes.Length);
        return Convert.ToBase64String(memory.ToArray());
    }

    private static byte[] Decompress(string base64)
    {
        var compressed = Convert.FromBase64String(base64);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string SafeFileName(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "DiezProject" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch)) builder.Append('_');
            else builder.Append(invalid.Contains(ch) ? '-' : ch);
        }
        var safe = builder.ToString().Trim('_', '-', '.');
        if (safe.Length > 70) safe = safe[..70];
        return string.IsNullOrWhiteSpace(safe) ? "DiezProject" : safe;
    }
}
