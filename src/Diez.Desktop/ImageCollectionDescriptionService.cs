using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DiezPublishingStudio;

internal readonly record struct ImageCollectionExportResult(
    bool Success,
    int Images,
    int Descriptions,
    int MissingDescriptions,
    string Message);

internal static class ImageCollectionDescriptionService
{
    private static readonly Regex CodeRegex = new(@"IMG-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    public static string GetDescription(AiProductionJob job) =>
        string.Equals(job.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase)
            ? job.ResultText ?? string.Empty
            : string.Empty;

    public static void SetDescription(AiProductionJob job, string? description)
    {
        if (!string.Equals(job.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La descrizione immagine può essere salvata soltanto su un elemento immagine.");

        job.ResultText = description ?? string.Empty;
        job.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");
    }

    public static async Task<ImageCollectionExportResult> ExportApprovedCollectionAsync(
        PreviewProject project,
        string projectPath,
        string path,
        bool includeDescriptions)
    {
        var jobs = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .Where(j => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal))
            .Where(j => j.ResultMaterialId.HasValue)
            .OrderBy(j => CodeNumber(j.Code))
            .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jobs.Count == 0)
            return new(false, 0, 0, 0, "Non ci sono immagini approvate da esportare.");

        var fullPath = EnsureExtension(path, ".zip");
        var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        var images = 0;
        var descriptions = 0;
        var missingDescriptions = 0;

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var job in jobs)
            {
                var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId!.Value);
                if (material is null) continue;

                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null || bytes.Length == 0) continue;

                var extension = Path.GetExtension(material.FileName).ToLowerInvariant();
                if (!ImageExtensions.Contains(extension)) extension = ".png";
                var baseName = StableBaseName(job.Code);

                var imageEntry = archive.CreateEntry(baseName + extension, CompressionLevel.Optimal);
                await using (var target = imageEntry.Open())
                    await target.WriteAsync(bytes);
                images++;

                if (!includeDescriptions) continue;

                var description = GetDescription(job);
                if (string.IsNullOrWhiteSpace(description)) missingDescriptions++;
                var descriptionEntry = archive.CreateEntry(baseName + ".txt", CompressionLevel.Optimal);
                await using var descriptionStream = descriptionEntry.Open();
                await using var writer = new StreamWriter(descriptionStream, new UTF8Encoding(false));
                await writer.WriteAsync(description);
                descriptions++;
            }
        }

        if (images == 0)
        {
            File.Delete(temp);
            return new(false, 0, 0, 0, "Non sono riuscito a leggere le immagini approvate dal progetto.");
        }

        File.Move(temp, fullPath, true);
        var message = includeDescriptions
            ? $"Raccolta esportata: {images} immagini + {descriptions} descrizioni con lo stesso nome base"
            : $"Raccolta esportata: {images} immagini, senza descrizioni";
        if (includeDescriptions && missingDescriptions > 0)
            message += $" · {missingDescriptions} descrizioni sono ancora vuote";

        return new(true, images, descriptions, missingDescriptions, message + ".");
    }

    public static string SuggestedCollectionZipName(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "raccolta-immagini").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return (string.IsNullOrWhiteSpace(safe) ? "raccolta-immagini" : safe) + "-immagini-approvate.zip";
    }

    private static string StableBaseName(string? code)
    {
        var match = CodeRegex.Match(code ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? $"IMG-{number:D3}"
            : string.IsNullOrWhiteSpace(code) ? "IMG" : code.Trim().ToUpperInvariant();
    }

    private static int CodeNumber(string? code)
    {
        var match = CodeRegex.Match(code ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : int.MaxValue;
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
}
