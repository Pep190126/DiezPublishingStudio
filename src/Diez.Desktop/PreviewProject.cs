using System.Text.Json;

namespace DiezPublishingStudio;

internal sealed class PreviewProject
{
    public string Format { get; set; } = "diez-project-preview";
    public int SchemaVersion { get; set; } = 2;
    public string Name { get; set; } = "Nuovo progetto";
    public string SavedAtLocal { get; set; } = string.Empty;
    public Guid ProjectId { get; set; } = Guid.NewGuid();
    public List<MaterialEntry> Materials { get; set; } = [];
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
    public List<string> Columns { get; set; } = [];
}

internal static class ProjectFileStore
{
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
        var json = await File.ReadAllTextAsync(path);
        var project = JsonSerializer.Deserialize<PreviewProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Il file non contiene un progetto Diez valido.");

        if (!project.Format.StartsWith("diez-project", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Formato progetto non riconosciuto.");

        project.Materials ??= [];
        return project;
    }

    public static async Task SaveAsync(string path, PreviewProject project)
    {
        project.Format = "diez-project-preview";
        project.SchemaVersion = 2;
        project.SavedAtLocal = DateTimeOffset.Now.ToString("G");

        var json = JsonSerializer.Serialize(project, JsonOptions);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, path, true);
    }
}
