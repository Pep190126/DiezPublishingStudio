using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

namespace DiezPublishingStudio.UnoSpike;

internal sealed class DiezProjectDocument
{
    private const string ManifestEntryName = "project.json";
    private readonly JsonObject _root;
    private readonly Dictionary<string, string> _stagedEmbeddedFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _sourcePath;
    private bool _sourceWasPackage;

    private DiezProjectDocument(JsonObject root)
    {
        _root = root;
        Normalize();
    }

    public string? SourcePath => _sourcePath;

    public string Name
    {
        get => GetString(_root, "Name", "Nuovo progetto");
        set => _root["Name"] = value;
    }

    public string EditionTitle
    {
        get => GetString(EnsureObject(_root, "EditionMetadata"), "Title", Name);
        set => EnsureObject(_root, "EditionMetadata")["Title"] = value;
    }

    public string BookType
    {
        get
        {
            foreach (var node in EnsureArray(_root, "Entities").OfType<JsonObject>())
            {
                if (string.Equals(GetString(node, "Kind"), "DiezBookType", StringComparison.OrdinalIgnoreCase))
                    return GetString(node, "Name");
            }

            return string.Empty;
        }
        set
        {
            var entities = EnsureArray(_root, "Entities");
            JsonObject? first = null;
            var duplicates = new List<JsonNode>();
            foreach (var node in entities.OfType<JsonObject>())
            {
                if (!string.Equals(GetString(node, "Kind"), "DiezBookType", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (first is null) first = node;
                else duplicates.Add(node);
            }

            if (first is null)
            {
                first = new JsonObject
                {
                    ["EntityId"] = Guid.NewGuid().ToString(),
                    ["Kind"] = "DiezBookType",
                    ["Name"] = value,
                    ["IsCandidate"] = false,
                    ["Notes"] = "Tipo di libro scelto dall'utente."
                };
                entities.Add(first);
            }
            else
            {
                first["Name"] = value;
                first["IsCandidate"] = false;
            }

            foreach (var duplicate in duplicates)
                entities.Remove(duplicate);
        }
    }

    public static DiezProjectDocument Create(string name)
    {
        var now = DateTimeOffset.Now.ToString("G");
        var root = new JsonObject
        {
            ["Format"] = "diez-project-package",
            ["SchemaVersion"] = 10,
            ["Name"] = string.IsNullOrWhiteSpace(name) ? "Nuovo progetto" : name.Trim(),
            ["SavedAtLocal"] = now,
            ["ProjectId"] = Guid.NewGuid().ToString(),
            ["EditionMetadata"] = new JsonObject
            {
                ["Title"] = string.IsNullOrWhiteSpace(name) ? "Nuovo progetto" : name.Trim(),
                ["Subtitle"] = "",
                ["Creator"] = "",
                ["Language"] = "it",
                ["Publisher"] = "",
                ["Isbn"] = "",
                ["Description"] = ""
            },
            ["AiProduction"] = new JsonObject
            {
                ["SchemaVersion"] = 1,
                ["ProjectBrief"] = ""
            },
            ["AiProductionJobs"] = new JsonArray(),
            ["Materials"] = new JsonArray(),
            ["ContentNodes"] = new JsonArray(),
            ["IllustrationPlacements"] = new JsonArray(),
            ["Entities"] = new JsonArray(),
            ["Relations"] = new JsonArray(),
            ["BibleEntries"] = new JsonArray(),
            ["ConsistencyFacts"] = new JsonArray(),
            ["ConsistencyIssues"] = new JsonArray(),
            ["ConsistencyResolutions"] = new JsonArray(),
            ["RevisionCandidates"] = new JsonArray()
        };

        return new DiezProjectDocument(root);
    }

    public static async Task<DiezProjectDocument> LoadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Il progetto selezionato non esiste.", path);

        JsonObject? root = null;
        var wasPackage = false;

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var manifest = archive.GetEntry(ManifestEntryName);
            if (manifest is not null)
            {
                using var reader = new StreamReader(manifest.Open(), Encoding.UTF8, true);
                root = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject;
                wasPackage = root is not null;
            }
        }
        catch (InvalidDataException)
        {
            // Legacy JSON .diez files are still supported.
        }

        if (root is null)
        {
            var json = await File.ReadAllTextAsync(path);
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("Il file non contiene un progetto Diez valido.");
        }

        var doc = new DiezProjectDocument(root)
        {
            _sourcePath = Path.GetFullPath(path),
            _sourceWasPackage = wasPackage
        };
        return doc;
    }

    public async Task SaveAsync(string? path = null)
    {
        var destination = string.IsNullOrWhiteSpace(path) ? _sourcePath : Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("Nessun percorso di salvataggio disponibile.");

        _root["Format"] = "diez-project-package";
        _root["SchemaVersion"] = Math.Max(10, GetInt(_root, "SchemaVersion", 10));
        _root["SavedAtLocal"] = DateTimeOffset.Now.ToString("G");

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temp = destination + ".uno.tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                if (_sourceWasPackage && !string.IsNullOrWhiteSpace(_sourcePath) && File.Exists(_sourcePath))
                {
                    using var input = ZipFile.OpenRead(_sourcePath);
                    foreach (var entry in input.Entries)
                    {
                        if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (_stagedEmbeddedFiles.ContainsKey(entry.FullName))
                            continue;

                        var copy = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        using var sourceStream = entry.Open();
                        using var targetStream = copy.Open();
                        await sourceStream.CopyToAsync(targetStream);
                    }
                }

                foreach (var staged in _stagedEmbeddedFiles)
                {
                    if (!File.Exists(staged.Value)) continue;
                    var entry = output.CreateEntry(staged.Key, CompressionLevel.Optimal);
                    await using var source = File.OpenRead(staged.Value);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target);
                }

                var manifest = output.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifest.Open();
                await using var writer = new StreamWriter(manifestStream, new UTF8Encoding(false));
                await writer.WriteAsync(_root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            File.Move(temp, destination, true);
            _sourcePath = destination;
            _sourceWasPackage = true;
            _stagedEmbeddedFiles.Clear();
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    public IReadOnlyList<string> MaterialDisplayItems()
    {
        var result = new List<string>();
        foreach (var material in EnsureArray(_root, "Materials").OfType<JsonObject>())
        {
            var name = GetString(material, "FileName", "(senza nome)");
            var kind = GetString(material, "Kind", "Materiale");
            var size = GetLong(material, "SizeBytes", 0);
            result.Add($"{name} · {kind} · {FormatSize(size)}");
        }
        return result;
    }

    public IReadOnlyList<string> ContentDisplayItems() =>
        EnsureArray(_root, "ContentNodes").OfType<JsonObject>()
            .OrderBy(x => GetInt(x, "Ordinal", 0))
            .Select(x => $"{GetString(x, "Kind", "Section")} · {GetString(x, "Title", "(senza titolo)")}")
            .ToList();

    public IReadOnlyList<string> EntityDisplayItems() =>
        EnsureArray(_root, "Entities").OfType<JsonObject>()
            .Where(x => !GetString(x, "Kind").StartsWith("Diez", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{GetString(x, "Kind", "Concept")} · {GetString(x, "Name", "(senza nome)")}")
            .ToList();

    public IReadOnlyList<string> IssueDisplayItems() =>
        EnsureArray(_root, "ConsistencyIssues").OfType<JsonObject>()
            .Select(x => $"[{GetString(x, "Status", "Open")}] {GetString(x, "Severity", "Warning")} · {GetString(x, "Message", GetString(x, "Code", "Problema"))}")
            .ToList();

    public IReadOnlyList<DiezAiFrontendJob> AiJobs()
    {
        try { return DiezAiExchangeBridge.ReadJobs(ExportProjectJson()); }
        catch { return []; }
    }

    public IReadOnlyList<string> AiJobDisplayItems()
    {
        var coreJobs = AiJobs();
        if (coreJobs.Count > 0)
            return coreJobs.Select(x => $"{x.Code} · {x.DisplayType} · {x.DisplayStatus} · {x.Title}").ToList();

        return EnsureArray(_root, "AiProductionJobs").OfType<JsonObject>()
            .OrderBy(x => GetString(x, "Code"))
            .Select(x => $"{GetString(x, "Code", "AI")} · {GetString(x, "OutputType", "Image")} · {GetString(x, "Status", "Ready")} · {GetString(x, "Title")}")
            .ToList();
    }

    public IReadOnlyList<DiezAiFrontendVersion> AiVersions(Guid workUnitId)
    {
        try { return DiezAiExchangeBridge.ReadVersions(ExportProjectJson(), workUnitId); }
        catch { return []; }
    }

    public async Task<DiezAiFrontendResultMutation> IngestAiTextResultAsync(
        Guid workUnitId,
        string? textContent,
        int? candidateVersion = null,
        string resultStatus = "COMPLETE")
    {
        var mutation = await DiezAiExchangeBridge.IngestTextResultAsync(
            ExportProjectJson(),
            workUnitId,
            textContent,
            candidateVersion,
            resultStatus);
        ApplyCoreJson(mutation.ProjectJson);
        return mutation;
    }

    public async Task<DiezAiFrontendImageMutation> IngestAiImageResultAsync(
        Guid workUnitId,
        string imagePath,
        string? description,
        int? candidateVersion = null,
        string resultStatus = "COMPLETE")
    {
        var mutation = await DiezAiExchangeBridge.IngestImageResultAsync(
            ExportProjectJson(),
            workUnitId,
            imagePath,
            description,
            candidateVersion,
            resultStatus);
        ApplyCoreJson(mutation.ProjectJson);

        var material = mutation.Material;
        if (material is { NeedsPackageStaging: true } &&
            !string.IsNullOrWhiteSpace(material.EmbeddedPath) &&
            !string.IsNullOrWhiteSpace(material.SourcePath) &&
            File.Exists(material.SourcePath))
        {
            _stagedEmbeddedFiles[material.EmbeddedPath] = material.SourcePath;
            MarkMaterialEmbedded(material.MaterialId, material.EmbeddedPath);
        }

        return mutation;
    }

    public IReadOnlyList<DiezVisionRequirement> VisionRequirements(Guid workUnitId)
    {
        try { return DiezVisionFrontendBridge.Requirements(ExportProjectJson(), workUnitId); }
        catch { return []; }
    }

    public DiezVisionApprovalResult ApproveAiImageVersionWithVision(
        Guid versionId,
        IEnumerable<DiezVisionCheckInput> checks,
        string? summary = null,
        double confidence = 1.0)
    {
        var result = DiezVisionFrontendBridge.ApproveImageVersion(
            ExportProjectJson(),
            versionId,
            checks,
            summary,
            confidence);
        ApplyCoreJson(result.ProjectJson);
        return result;
    }

    public DiezAiFrontendResultMutation ApproveAiVersion(Guid versionId)
    {
        var mutation = DiezAiExchangeBridge.ApproveVersion(ExportProjectJson(), versionId);
        ApplyCoreJson(mutation.ProjectJson);
        return mutation;
    }

    public int MaterialCount => EnsureArray(_root, "Materials").Count;
    public int ContentCount => EnsureArray(_root, "ContentNodes").Count;
    public int EntityCount => EnsureArray(_root, "Entities").OfType<JsonObject>()
        .Count(x => !GetString(x, "Kind").StartsWith("Diez", StringComparison.OrdinalIgnoreCase));
    public int OpenIssueCount => EnsureArray(_root, "ConsistencyIssues").OfType<JsonObject>()
        .Count(x => string.Equals(GetString(x, "Status", "Open"), "Open", StringComparison.OrdinalIgnoreCase));

    public async Task<string> ImportMaterialAsync(string path)
    {
        if (!File.Exists(path)) return "File non trovato.";
        var bytes = await File.ReadAllBytesAsync(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var materials = EnsureArray(_root, "Materials");

        if (materials.OfType<JsonObject>().Any(x =>
                string.Equals(GetString(x, "Sha256"), hash, StringComparison.OrdinalIgnoreCase)))
            return "Duplicato ignorato.";

        var id = Guid.NewGuid();
        var ext = Path.GetExtension(path);
        var embeddedPath = $"materials/{id:N}{ext.ToLowerInvariant()}";
        var extracted = string.Empty;
        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                extracted = await File.ReadAllTextAsync(path);
                if (extracted.Length > 200_000) extracted = extracted[..200_000];
            }
            catch { }
        }

        materials.Add(new JsonObject
        {
            ["MaterialId"] = id.ToString(),
            ["FileName"] = Path.GetFileName(path),
            ["SourcePath"] = path,
            ["Kind"] = MaterialKind(ext),
            ["ImportedAtLocal"] = DateTimeOffset.Now.ToString("G"),
            ["SizeBytes"] = bytes.LongLength,
            ["Sha256"] = hash,
            ["Summary"] = extracted.Length == 0 ? "Materiale incorporato nel progetto." : FirstLine(extracted),
            ["Preview"] = extracted.Length == 0 ? "" : extracted[..Math.Min(extracted.Length, 1200)],
            ["ExtractedText"] = extracted,
            ["Columns"] = new JsonArray(),
            ["EmbeddedPath"] = embeddedPath,
            ["IsEmbedded"] = true
        });

        _stagedEmbeddedFiles[embeddedPath] = path;
        return "Importato.";
    }

    public bool RemoveMaterialAt(int index)
    {
        var materials = EnsureArray(_root, "Materials");
        if (index < 0 || index >= materials.Count) return false;
        materials.RemoveAt(index);
        return true;
    }

    public string GetUiString(string key, string fallback = "") =>
        GetString(EnsureObject(_root, "UnoUiState"), key, fallback);

    public void SetUiString(string key, string? value) =>
        EnsureObject(_root, "UnoUiState")[key] = value ?? string.Empty;

    public bool GetUiBool(string key, bool fallback = false)
    {
        var node = EnsureObject(_root, "UnoUiState")[key];
        return node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;
    }

    public void SetUiBool(string key, bool value) =>
        EnsureObject(_root, "UnoUiState")[key] = value;

    public int GetUiInt(string key, int fallback = 0)
    {
        var node = EnsureObject(_root, "UnoUiState")[key];
        return node is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
    }

    public void SetUiInt(string key, int value) =>
        EnsureObject(_root, "UnoUiState")[key] = value;

    public IReadOnlyList<SceneItem> Scenes()
    {
        var scenes = EnsureArray(EnsureObject(_root, "UnoUiState"), "StructuredScenes");
        return scenes.OfType<JsonObject>()
            .Select(x => new SceneItem(
                GetString(x, "SceneId", Guid.NewGuid().ToString()),
                GetString(x, "Name", "Scena"),
                GetString(x, "Description"),
                GetBool(x, "IsActive", true)))
            .ToList();
    }

    public SceneItem AddScene()
    {
        var scene = new SceneItem(Guid.NewGuid().ToString(), $"Scena {Scenes().Count + 1}", "", true);
        EnsureArray(EnsureObject(_root, "UnoUiState"), "StructuredScenes").Add(new JsonObject
        {
            ["SceneId"] = scene.SceneId,
            ["Name"] = scene.Name,
            ["Description"] = scene.Description,
            ["IsActive"] = true
        });
        return scene;
    }

    public void UpdateScene(SceneItem scene)
    {
        foreach (var node in EnsureArray(EnsureObject(_root, "UnoUiState"), "StructuredScenes").OfType<JsonObject>())
        {
            if (!string.Equals(GetString(node, "SceneId"), scene.SceneId, StringComparison.OrdinalIgnoreCase))
                continue;
            node["Name"] = scene.Name;
            node["Description"] = scene.Description;
            node["IsActive"] = scene.IsActive;
            return;
        }
    }

    public void AddAiJob(string title, string outputType, string prompt)
    {
        var brief = GetUiString("AI.ProjectBrief");
        var mutation = DiezAiExchangeBridge.CreateReadyJob(
            ExportProjectJson(),
            title,
            outputType,
            prompt,
            string.IsNullOrWhiteSpace(brief) ? null : brief);
        ApplyCoreJson(mutation.ProjectJson);
    }

    public string ExportProjectJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private void ApplyCoreJson(string json)
    {
        var updated = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Il Core ha restituito un progetto Diez non valido.");
        _root.Clear();
        foreach (var pair in updated)
            _root[pair.Key] = pair.Value?.DeepClone();
        Normalize();
    }

    private void MarkMaterialEmbedded(Guid materialId, string embeddedPath)
    {
        foreach (var material in EnsureArray(_root, "Materials").OfType<JsonObject>())
        {
            if (!Guid.TryParse(GetString(material, "MaterialId"), out var id) || id != materialId)
                continue;
            material["EmbeddedPath"] = embeddedPath;
            material["IsEmbedded"] = true;
            return;
        }
    }

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(GetString(_root, "Format")))
            _root["Format"] = "diez-project-package";
        if (_root["SchemaVersion"] is null) _root["SchemaVersion"] = 10;
        if (_root["ProjectId"] is null) _root["ProjectId"] = Guid.NewGuid().ToString();

        foreach (var name in new[]
        {
            "AiProductionJobs", "Materials", "ContentNodes", "IllustrationPlacements", "Entities",
            "Relations", "BibleEntries", "ConsistencyFacts", "ConsistencyIssues",
            "ConsistencyResolutions", "RevisionCandidates"
        })
            EnsureArray(_root, name);

        EnsureObject(_root, "EditionMetadata");
        EnsureObject(_root, "AiProduction");
        EnsureObject(_root, "UnoUiState");
    }

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

    private static string GetString(JsonObject obj, string name, string fallback = "")
    {
        var node = obj[name];
        if (node is JsonValue value && value.TryGetValue<string>(out var result))
            return result ?? fallback;
        return fallback;
    }

    private static int GetInt(JsonObject obj, string name, int fallback)
    {
        var node = obj[name];
        if (node is JsonValue value && value.TryGetValue<int>(out var result))
            return result;
        return fallback;
    }

    private static long GetLong(JsonObject obj, string name, long fallback)
    {
        var node = obj[name];
        if (node is JsonValue value && value.TryGetValue<long>(out var result))
            return result;
        return fallback;
    }

    private static bool GetBool(JsonObject obj, string name, bool fallback)
    {
        var node = obj[name];
        return node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;
    }

    private static string FirstLine(string text)
    {
        var line = text.Replace("\r\n", "\n").Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        return line.Length > 180 ? line[..180] + "…" : line;
    }

    private static string MaterialKind(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "Image",
        ".csv" or ".xlsx" => "Table",
        ".pdf" => "PDF",
        ".docx" or ".odt" or ".rtf" or ".txt" or ".md" => "Document",
        _ => "Material"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}

internal sealed record SceneItem(string SceneId, string Name, string Description, bool IsActive);
