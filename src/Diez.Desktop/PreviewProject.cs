using System.IO.Compression;
using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class PreviewProject
{
    public string Format { get; set; } = "diez-project-package";
    public int SchemaVersion { get; set; } = 7;
    public string Name { get; set; } = "Nuovo progetto";
    public string SavedAtLocal { get; set; } = string.Empty;
    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public List<MaterialEntry> Materials { get; set; } = [];
    public List<ContentNode> ContentNodes { get; set; } = [];
    public List<GraphEntity> Entities { get; set; } = [];
    public List<ContentRelation> Relations { get; set; } = [];
    public List<BibleEntry> BibleEntries { get; set; } = [];
    public List<ConsistencyFact> ConsistencyFacts { get; set; } = [];
    public List<ConsistencyIssue> ConsistencyIssues { get; set; } = [];
    public List<ConsistencyResolution> ConsistencyResolutions { get; set; } = [];
}

internal sealed class MaterialEntry
{
    public Guid MaterialId { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ImportedAtLocal { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public string EmbeddedPath { get; set; } = string.Empty;
    public bool IsEmbedded { get; set; }
}

internal sealed class ContentNode
{
    public Guid ContentId { get; set; } = Guid.NewGuid();
    public Guid MaterialId { get; set; }
    public Guid? ParentId { get; set; }
    public string Kind { get; set; } = "Section";
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string SourceLocator { get; set; } = string.Empty;
}

internal sealed class GraphEntity
{
    public Guid EntityId { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "Concept";
    public string Name { get; set; } = string.Empty;
    public bool IsCandidate { get; set; } = true;
    public Guid? SourceMaterialId { get; set; }
    public Guid? FirstSourceContentId { get; set; }
    public string Notes { get; set; } = string.Empty;
}

internal sealed class ContentRelation
{
    public Guid RelationId { get; set; } = Guid.NewGuid();
    public string FromKind { get; set; } = "Entity";
    public Guid FromId { get; set; }
    public string Type { get; set; } = "References";
    public string ToKind { get; set; } = "Content";
    public Guid ToId { get; set; }
    public bool IsCandidate { get; set; } = true;
    public string Evidence { get; set; } = string.Empty;
}

internal sealed class BibleEntry
{
    public Guid BibleEntryId { get; set; } = Guid.NewGuid();
    public Guid SubjectEntityId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Authority { get; set; } = "Proposed";
    public bool IsActive { get; set; } = true;
    public Guid? SourceContentId { get; set; }
}

internal sealed class ConsistencyFact
{
    public Guid FactId { get; set; } = Guid.NewGuid();
    public Guid SubjectEntityId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public Guid ContentId { get; set; }
    public string SourceLocator { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}

internal sealed class ConsistencyIssue
{
    public Guid IssueId { get; set; } = Guid.NewGuid();
    public string Signature { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Code { get; set; } = string.Empty;
    public Guid? SubjectEntityId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<Guid> ContentIds { get; set; } = [];
    public string Status { get; set; } = "Open";
    public string DetectedAtLocal { get; set; } = string.Empty;
}

internal sealed class ConsistencyResolution
{
    public Guid ResolutionId { get; set; } = Guid.NewGuid();
    public Guid IssueId { get; set; }
    public string IssueSignature { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = "Open";
    public string NewStatus { get; set; } = "Reviewed";
    public string Action { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string CreatedAtLocal { get; set; } = string.Empty;
}

internal static class ProjectFileStore
{
    private const string ManifestEntryName = "project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static PreviewProject Create(string name) => new()
    {
        Name = name,
        SavedAtLocal = DateTimeOffset.Now.ToString("G")
    };

    public static async Task<PreviewProject> LoadAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Il progetto selezionato non esiste più.", path);

        PreviewProject project;
        if (IsPackageFile(path))
        {
            using var archive = ZipFile.OpenRead(path);
            var manifest = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException("Pacchetto .diez non valido: project.json mancante.");

            using var reader = new StreamReader(manifest.Open());
            var json = await reader.ReadToEndAsync();
            project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
                ?? throw new InvalidDataException("Il pacchetto non contiene un progetto Diez valido.");
        }
        else
        {
            var json = await File.ReadAllTextAsync(path);
            project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
                ?? throw new InvalidDataException("Il file non contiene un progetto Diez valido.");
        }

        if (string.IsNullOrWhiteSpace(project.Format) ||
            !project.Format.StartsWith("diez-project", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Formato progetto non riconosciuto.");

        Normalize(project);
        return project;
    }

    public static async Task SaveAsync(string path, PreviewProject project)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Percorso progetto non valido.", nameof(path));

        Normalize(project);
        project.Format = "diez-project-package";
        project.SchemaVersion = 7;
        project.SavedAtLocal = DateTimeOffset.Now.ToString("G");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        FileStream? oldStream = null;
        ZipArchive? oldArchive = null;

        try
        {
            if (File.Exists(path) && IsPackageFile(path))
            {
                oldStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                oldArchive = new ZipArchive(oldStream, ZipArchiveMode.Read, leaveOpen: false);
            }

            await using (var tempStream = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var newArchive = new ZipArchive(tempStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var material in project.Materials)
                {
                    material.EmbeddedPath = BuildEmbeddedPath(material);
                    material.IsEmbedded = await CopyMaterialIntoPackageAsync(oldArchive, newArchive, material);
                }

                var manifest = newArchive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifest.Open();
                await JsonSerializer.SerializeAsync(manifestStream, project, JsonOptions);
            }
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
        finally
        {
            oldArchive?.Dispose();
            oldStream?.Dispose();
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public static bool IsPackageFile(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < 4) return false;
        Span<byte> signature = stackalloc byte[4];
        _ = stream.Read(signature);
        return signature[0] == (byte)'P' && signature[1] == (byte)'K' &&
               signature[2] == 3 && signature[3] == 4;
    }

    public static async Task<byte[]?> ReadEmbeddedMaterialAsync(string projectPath, MaterialEntry material)
    {
        if (!IsPackageFile(projectPath) || string.IsNullOrWhiteSpace(material.EmbeddedPath)) return null;
        using var archive = ZipFile.OpenRead(projectPath);
        var entry = archive.GetEntry(material.EmbeddedPath);
        if (entry is null) return null;
        await using var source = entry.Open();
        await using var memory = new MemoryStream();
        await source.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task<bool> CopyMaterialIntoPackageAsync(ZipArchive? oldArchive, ZipArchive newArchive, MaterialEntry material)
    {
        var previous = oldArchive?.GetEntry(material.EmbeddedPath);
        if (previous is not null)
        {
            var destination = newArchive.CreateEntry(material.EmbeddedPath, CompressionLevel.Optimal);
            await using var source = previous.Open();
            await using var target = destination.Open();
            await source.CopyToAsync(target);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(material.SourcePath) && File.Exists(material.SourcePath))
        {
            var destination = newArchive.CreateEntry(material.EmbeddedPath, CompressionLevel.Optimal);
            await using var source = File.Open(material.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var target = destination.Open();
            await source.CopyToAsync(target);
            return true;
        }

        return false;
    }

    private static string BuildEmbeddedPath(MaterialEntry material)
    {
        var safeName = string.Concat(material.FileName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "materiale.bin";
        return $"materials/{material.MaterialId:N}/{safeName}";
    }

    private static void Normalize(PreviewProject project)
    {
        if (project.ProjectId == Guid.Empty) project.ProjectId = Guid.NewGuid();
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];

        foreach (var material in project.Materials)
        {
            if (material.MaterialId == Guid.Empty) material.MaterialId = Guid.NewGuid();
            material.Columns ??= [];
            material.FileName ??= string.Empty;
            material.SourcePath ??= string.Empty;
            material.Kind ??= string.Empty;
            material.ImportedAtLocal ??= string.Empty;
            material.Sha256 ??= string.Empty;
            material.Summary ??= string.Empty;
            material.Preview ??= string.Empty;
            material.ExtractedText ??= string.Empty;
            material.EmbeddedPath ??= string.Empty;
        }

        foreach (var node in project.ContentNodes)
        {
            if (node.ContentId == Guid.Empty) node.ContentId = Guid.NewGuid();
            node.Kind ??= "Section";
            node.Title ??= string.Empty;
            node.Body ??= string.Empty;
            node.SourceLocator ??= string.Empty;
        }

        foreach (var entity in project.Entities)
        {
            if (entity.EntityId == Guid.Empty) entity.EntityId = Guid.NewGuid();
            entity.Kind ??= "Concept";
            entity.Name ??= string.Empty;
            entity.Notes ??= string.Empty;
        }

        foreach (var relation in project.Relations)
        {
            if (relation.RelationId == Guid.Empty) relation.RelationId = Guid.NewGuid();
            relation.FromKind ??= "Entity";
            relation.ToKind ??= "Content";
            relation.Type ??= "References";
            relation.Evidence ??= string.Empty;
        }

        foreach (var entry in project.BibleEntries)
        {
            if (entry.BibleEntryId == Guid.Empty) entry.BibleEntryId = Guid.NewGuid();
            entry.Key ??= string.Empty;
            entry.Value ??= string.Empty;
            entry.Authority ??= "Proposed";
        }

        foreach (var fact in project.ConsistencyFacts)
        {
            if (fact.FactId == Guid.Empty) fact.FactId = Guid.NewGuid();
            fact.Key ??= string.Empty;
            fact.Value ??= string.Empty;
            fact.SourceLocator ??= string.Empty;
            fact.Evidence ??= string.Empty;
        }

        foreach (var issue in project.ConsistencyIssues)
        {
            if (issue.IssueId == Guid.Empty) issue.IssueId = Guid.NewGuid();
            issue.Signature ??= string.Empty;
            issue.Severity ??= "Warning";
            issue.Code ??= string.Empty;
            issue.Key ??= string.Empty;
            issue.Message ??= string.Empty;
            issue.ContentIds ??= [];
            issue.Status ??= "Open";
            issue.DetectedAtLocal ??= string.Empty;
        }

        foreach (var resolution in project.ConsistencyResolutions)
        {
            if (resolution.ResolutionId == Guid.Empty) resolution.ResolutionId = Guid.NewGuid();
            resolution.IssueSignature ??= string.Empty;
            resolution.PreviousStatus ??= "Open";
            resolution.NewStatus ??= "Reviewed";
            resolution.Action ??= string.Empty;
            resolution.Note ??= string.Empty;
            resolution.CreatedAtLocal ??= string.Empty;
        }
    }
}
