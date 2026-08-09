using System.IO.Compression;

namespace DiezPublishingStudio;

internal static class ImageCollectionLayoutChoiceService
{
    public static async Task<ImageLayoutExportResult> ExportAsync(
        PreviewProject project,
        string projectPath,
        string outputPath,
        string mode,
        bool includeDescriptions,
        string descriptionFormat)
    {
        if (string.Equals(mode, ImageCollectionLayoutExportService.External, StringComparison.Ordinal))
        {
            var external = await ImageCollectionDescriptionService.ExportApprovedCollectionAsync(
                project, projectPath, outputPath, includeDescriptions, descriptionFormat);
            var path = EnsureExtension(outputPath, ".zip");
            return new(external.Success, external.Message, external.Success ? path : null);
        }

        if (string.Equals(mode, ImageCollectionLayoutExportService.Internal, StringComparison.Ordinal))
            return await ImageCollectionLayoutExportService.ExportAsync(
                project, projectPath, outputPath, mode, includeDescriptions: false);

        if (!string.Equals(mode, ImageCollectionLayoutExportService.Both, StringComparison.Ordinal))
            return new(false, "Scelta di impaginazione non riconosciuta.", null);

        var finalPath = EnsureExtension(outputPath, ".zip");
        var directory = Path.GetDirectoryName(Path.GetFullPath(finalPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezImageLayoutChoice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var externalZip = Path.Combine(tempRoot, "impaginazione-esterna.zip");
            var internalDocx = Path.Combine(tempRoot, "impaginazione-interna.docx");

            var external = await ImageCollectionDescriptionService.ExportApprovedCollectionAsync(
                project, projectPath, externalZip, includeDescriptions, descriptionFormat);
            if (!external.Success)
                return new(false, external.Message, null);

            var internalResult = await ImageCollectionLayoutExportService.ExportAsync(
                project, projectPath, internalDocx, ImageCollectionLayoutExportService.Internal, includeDescriptions: false);
            if (!internalResult.Success)
                return internalResult;

            var tempFinal = finalPath + ".tmp";
            if (File.Exists(tempFinal)) File.Delete(tempFinal);
            await using (var output = new FileStream(tempFinal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var docxEntry = archive.CreateEntry("impaginazione-interna.docx", CompressionLevel.Optimal);
                await using (var target = docxEntry.Open())
                await using (var source = File.OpenRead(internalDocx))
                    await source.CopyToAsync(target);

                using var sourceZip = ZipFile.OpenRead(externalZip);
                foreach (var entry in sourceZip.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                    var targetEntry = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    await using var source = entry.Open();
                    await using var target = targetEntry.Open();
                    await source.CopyToAsync(target);
                }
            }
            File.Move(tempFinal, finalPath, true);

            var descriptionPart = includeDescriptions
                ? $" + {external.Descriptions} descrizioni {NormalizeDescriptionFormat(descriptionFormat)} abbinate"
                : string.Empty;
            return new(true,
                $"Creati entrambi: DOCX per impaginazione interna + {external.Images} immagini originali{descriptionPart}.",
                finalPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string NormalizeDescriptionFormat(string format) =>
        string.Equals(format, ImageCollectionDescriptionService.DescriptionDocx, StringComparison.OrdinalIgnoreCase)
            ? ImageCollectionDescriptionService.DescriptionDocx
            : ImageCollectionDescriptionService.DescriptionTxt;

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
}
