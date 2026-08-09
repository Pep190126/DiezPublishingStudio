using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class FinalizedOutputRecipes
{
    public const string EditableDocx = "editable-docx";
    public const string MasterCsv = "master-csv";
    public const string MasterXlsx = "master-xlsx";
    public const string WordSearchDatabaseXlsx = "wordsearch-database-xlsx";
    public const string WordSearchColumnsXlsx = "wordsearch-columns-xlsx";
    public const string WordSearchColumnsCsv = "wordsearch-columns-csv";
}

internal sealed class FinalizedBookRecord
{
    public Guid FinalizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PublicationCandidateId { get; set; }
    public int PublicationCandidateSequence { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BookType { get; set; } = string.Empty;
    public string FinalizedAtLocal { get; set; } = string.Empty;
    public string SnapshotFileName { get; set; } = "snapshot.diez";
    public List<FinalizedOutputRecord> Outputs { get; set; } = [];
}

internal sealed class FinalizedOutputRecord
{
    public Guid OutputId { get; set; }
    public string Recipe { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ArchivedFileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string CreatedAtLocal { get; set; } = string.Empty;
    public string GoogleUrl { get; set; } = string.Empty;
    public string LastGoogleAttemptAtLocal { get; set; } = string.Empty;
}

internal readonly record struct FinalizedLibraryActionResult(bool Success, string Message, string? OutputPath = null);

internal static class FinalizedLibraryService
{
    private const string ManifestName = "finalized.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string RootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiezPublishingStudio",
        "FinalizedLibrary");

    public static IReadOnlyList<FinalizedBookRecord> LoadAll()
    {
        if (!Directory.Exists(RootPath)) return [];
        var result = new List<FinalizedBookRecord>();
        foreach (var manifest in Directory.EnumerateFiles(RootPath, ManifestName, SearchOption.AllDirectories))
        {
            try
            {
                var item = JsonSerializer.Deserialize<FinalizedBookRecord>(File.ReadAllText(manifest), JsonOptions);
                if (item is not null && item.FinalizationId != Guid.Empty) result.Add(item);
            }
            catch { }
        }
        return result
            .OrderByDescending(r => ParseDate(r.FinalizedAtLocal))
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<FinalizedOutputRecord> RecordOutputAsync(
        PreviewProject project,
        string projectPath,
        string outputPath,
        string recipe,
        string label)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            throw new InvalidOperationException("Per archiviare la finalizzazione serve il progetto .diez salvato.");
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            throw new InvalidOperationException("L'output finalizzato da archiviare non esiste.");

        Directory.CreateDirectory(RootPath);
        var candidate = PublicationCandidateService.GetLatest(project);
        var candidateId = candidate?.CandidateId ?? Guid.Empty;
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 0 : parsed;

        var existing = candidateId == Guid.Empty
            ? null
            : LoadAll().FirstOrDefault(r => r.ProjectId == project.ProjectId && r.PublicationCandidateId == candidateId);
        var record = existing ?? new FinalizedBookRecord
        {
            FinalizationId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            PublicationCandidateId = candidateId,
            PublicationCandidateSequence = sequence,
            Title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title,
            BookType = BookTypeProfileService.Get(project),
            FinalizedAtLocal = DateTimeOffset.Now.ToString("O")
        };

        var folder = RecordFolder(record.FinalizationId);
        Directory.CreateDirectory(folder);
        var snapshotPath = Path.Combine(folder, record.SnapshotFileName);
        if (!File.Exists(snapshotPath))
        {
            await ProjectFileStore.SaveAsync(projectPath, project);
            await CopyFileAsync(projectPath, snapshotPath, overwrite: false);
        }

        var sourceHash = await Sha256Async(outputPath);
        var same = record.Outputs.FirstOrDefault(o =>
            string.Equals(o.Recipe, recipe, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(o.Sha256, sourceHash, StringComparison.OrdinalIgnoreCase));
        if (same is not null)
        {
            await SaveManifestAsync(record);
            return same;
        }

        var output = new FinalizedOutputRecord
        {
            OutputId = Guid.NewGuid(),
            Recipe = recipe,
            Label = string.IsNullOrWhiteSpace(label) ? Path.GetExtension(outputPath).TrimStart('.').ToUpperInvariant() : label.Trim(),
            FileName = Path.GetFileName(outputPath),
            Sha256 = sourceHash,
            SizeBytes = new FileInfo(outputPath).Length,
            CreatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        output.ArchivedFileName = output.OutputId.ToString("N") + "-" + SafeFileName(output.FileName);
        var archivedPath = Path.Combine(folder, output.ArchivedFileName);
        await CopyFileAsync(outputPath, archivedPath, overwrite: false);
        var archivedHash = await Sha256Async(archivedPath);
        if (!string.Equals(sourceHash, archivedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(archivedPath);
            throw new InvalidOperationException("La copia archiviata non coincide con l'output appena creato.");
        }

        record.Outputs.Add(output);
        await SaveManifestAsync(record);
        return output;
    }

    public static async Task<FinalizedLibraryActionResult> CopyIdenticalAsync(Guid finalizationId, Guid outputId, string destinationPath)
    {
        var found = Find(finalizationId, outputId);
        if (found.Record is null || found.Output is null)
            return new(false, "Non trovo più questa copia finalizzata.");
        var source = ArchivedOutputPath(found.Record, found.Output);
        if (!File.Exists(source)) return new(false, "La copia identica archiviata non è più disponibile.");
        if (!string.Equals(await Sha256Async(source), found.Output.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(false, "La copia archiviata non supera il controllo di integrità.");

        var target = EnsureExtension(destinationPath, Path.GetExtension(found.Output.FileName));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            await CopyFileAsync(source, target, overwrite: true);
        if (!string.Equals(await Sha256Async(target), found.Output.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(false, "La nuova copia non coincide byte per byte con quella finalizzata.");
        return new(true, $"Copia identica creata e verificata: {Path.GetFileName(target)}", target);
    }

    public static async Task<FinalizedLibraryActionResult> RegenerateAsync(Guid finalizationId, Guid outputId, string destinationPath)
    {
        var found = Find(finalizationId, outputId);
        if (found.Record is null || found.Output is null)
            return new(false, "Non trovo più questa finalizzazione.");
        var snapshotPath = SnapshotPath(found.Record);
        if (!File.Exists(snapshotPath)) return new(false, "Manca la versione .diez congelata necessaria per rigenerare.");

        var project = await ProjectFileStore.LoadAsync(snapshotPath);
        var target = EnsureExtension(destinationPath, Path.GetExtension(found.Output.FileName));
        switch (found.Output.Recipe)
        {
            case FinalizedOutputRecipes.EditableDocx:
            {
                var result = await DocxExportService.ExportAsync(project, snapshotPath, target);
                return new(result.Exported, result.Message, result.OutputPath);
            }
            case FinalizedOutputRecipes.MasterCsv:
            {
                var result = await HandoffExportService.ExportMasterCsvAsync(project, target);
                return new(result.Exported, result.Message, result.OutputPath);
            }
            case FinalizedOutputRecipes.MasterXlsx:
            {
                var result = await HandoffExportService.ExportMasterXlsxAsync(project, target);
                return new(result.Exported, result.Message, result.OutputPath);
            }
            case FinalizedOutputRecipes.WordSearchDatabaseXlsx:
            {
                var result = await WordSearchFullDatabaseExportService.ExportAsync(project, target);
                return new(result.Success, result.Message, result.OutputPath);
            }
            case FinalizedOutputRecipes.WordSearchColumnsXlsx:
            {
                var result = await WordSearchColumnExportService.ExportXlsxAsync(project, target);
                return new(result.Success, result.Message, result.OutputPath);
            }
            case FinalizedOutputRecipes.WordSearchColumnsCsv:
            {
                var result = await WordSearchColumnExportService.ExportCsvAsync(project, target);
                return new(result.Success, result.Message, result.OutputPath);
            }
            default:
                return new(false, "Questa vecchia uscita può essere copiata identica, ma non ha ancora una ricetta di rigenerazione supportata.");
        }
    }

    public static async Task<FinalizedLibraryActionResult> RetryGoogleAsync(Guid finalizationId, Guid outputId)
    {
        var found = Find(finalizationId, outputId);
        if (found.Record is null || found.Output is null)
            return new(false, "Non trovo più questa copia finalizzata.");
        var source = ArchivedOutputPath(found.Record, found.Output);
        if (!File.Exists(source)) return new(false, "La copia archiviata da inviare a Google non è più disponibile.");
        if (!string.Equals(await Sha256Async(source), found.Output.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(false, "La copia archiviata non supera il controllo di integrità.");

        GoogleDocsExportResult result;
        var extension = Path.GetExtension(found.Output.FileName).ToLowerInvariant();
        if (extension == ".docx")
            result = await GoogleDocsExportService.ExportDocxAsync(source, found.Output.FileName);
        else if (extension == ".xlsx")
            result = await GoogleDocsExportService.ExportXlsxAsync(source, found.Output.FileName);
        else if (extension == ".csv")
            result = await GoogleDocsExportService.ExportCsvAsSheetAsync(source, Path.GetFileNameWithoutExtension(found.Output.FileName));
        else
            return new(false, "Questo formato non può essere aperto direttamente con Google Documenti o Fogli Google.");

        found.Output.LastGoogleAttemptAtLocal = DateTimeOffset.Now.ToString("O");
        if (result.Success && !string.IsNullOrWhiteSpace(result.DocumentUrl)) found.Output.GoogleUrl = result.DocumentUrl;
        await SaveManifestAsync(found.Record);
        return new(result.Success, result.Message, result.DocumentUrl);
    }

    public static FinalizedLibraryActionResult OpenArchived(Guid finalizationId, Guid outputId)
    {
        var found = Find(finalizationId, outputId);
        if (found.Record is null || found.Output is null) return new(false, "Non trovo più questa copia finalizzata.");
        var source = ArchivedOutputPath(found.Record, found.Output);
        if (!File.Exists(source)) return new(false, "La copia archiviata non è più disponibile.");
        try
        {
            Process.Start(new ProcessStartInfo { FileName = source, UseShellExecute = true });
            return new(true, $"Aperta la copia archiviata: {found.Output.FileName}", source);
        }
        catch (Exception ex) { return new(false, "Non riesco ad aprire il file: " + ex.Message); }
    }

    public static string ArchivedOutputPath(FinalizedBookRecord record, FinalizedOutputRecord output) =>
        Path.Combine(RecordFolder(record.FinalizationId), output.ArchivedFileName);

    public static string SnapshotPath(FinalizedBookRecord record) =>
        Path.Combine(RecordFolder(record.FinalizationId), record.SnapshotFileName);

    private static (FinalizedBookRecord? Record, FinalizedOutputRecord? Output) Find(Guid finalizationId, Guid outputId)
    {
        var record = LoadAll().FirstOrDefault(r => r.FinalizationId == finalizationId);
        return (record, record?.Outputs.FirstOrDefault(o => o.OutputId == outputId));
    }

    private static async Task SaveManifestAsync(FinalizedBookRecord record)
    {
        var folder = RecordFolder(record.FinalizationId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ManifestName);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(record, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static string RecordFolder(Guid id) => Path.Combine(RootPath, id.ToString("N"));

    private static async Task CopyFileAsync(string source, string destination, bool overwrite)
    {
        if (!overwrite && File.Exists(destination)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output);
        await output.FlushAsync();
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static string EnsureExtension(string path, string extension)
    {
        var full = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(extension) && !string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase))
            full += extension;
        return full;
    }

    private static string SafeFileName(string value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "output" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return result;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
