using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

/// <summary>
/// Small migration-safe boundary used after package ingestion to retain the authoritative request
/// snapshot on a Candidate without asking the package frontend to edit AI Exchange JSON directly.
/// </summary>
public static class DiezAiSnapshotFrontendBridge
{
    private const string ExchangeEntityKind = "DiezAiExchangeState";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string AttachVersion(string projectJson, Guid versionId, Guid requestSnapshotId)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.Entities ??= [];
        var state = AiExchangeStateStore.Load(project);
        var version = state.Versions.FirstOrDefault(v => v.VersionId == versionId)
            ?? throw new InvalidOperationException("Versione AI non trovata.");
        version.SourceSnapshotId = requestSnapshotId;
        AiExchangeStateStore.Save(project, state);

        var typed = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, ExchangeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (typed is null) return projectJson;
        var entities = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = entities;
        var raw = entities.OfType<JsonObject>().FirstOrDefault(e =>
            string.Equals(Read(e, "Kind"), ExchangeEntityKind, StringComparison.OrdinalIgnoreCase));
        if (raw is null)
        {
            raw = new JsonObject();
            entities.Add(raw);
        }
        raw["EntityId"] = typed.EntityId.ToString();
        raw["Kind"] = typed.Kind;
        raw["Name"] = typed.Name;
        raw["IsCandidate"] = typed.IsCandidate;
        raw["Notes"] = typed.Notes;
        if (typed.SourceMaterialId.HasValue) raw["SourceMaterialId"] = typed.SourceMaterialId.Value.ToString();
        if (typed.FirstSourceContentId.HasValue) raw["FirstSourceContentId"] = typed.FirstSourceContentId.Value.ToString();
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Read(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var result) ? result ?? string.Empty : string.Empty;
}
