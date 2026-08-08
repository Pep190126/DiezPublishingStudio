using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class ProductionPackageService
{
    public static string SuggestedFileName(PreviewProject project)
    {
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 1 : parsed;
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        return $"{SanitizeFileName(title)}-production-{sequence:D3}.zip";
    }

    public static async Task<ProductionPackageResult> ExportAsync(PreviewProject project, string projectPath, string outputPath)
    {
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new ProductionPackageResult(false, "Production Package bloccato: il preflight non è READY.", null, 0);

        var candidate = PublicationCandidateService.GetLatest(project);
        var freeze = EditionFreezeService.GetLatestFreeze(project);
        if (candidate is null || freeze is null || !PublicationCandidateService.IsLatestCandidateCurrent(project))
            return new ProductionPackageResult(false, "Production Package bloccato: crea un Publication Candidate corrente.", null, 0);

        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
            return new ProductionPackageResult(false, "Production Package bloccato: salva prima il progetto come pacchetto .diez.", null, 0);
        if (string.IsNullOrWhiteSpace(outputPath))
            return new ProductionPackageResult(false, "Percorso Production Package non valido.", null, 0);

        var fullPath = EnsureExtension(outputPath, ".zip");
        EnsureDirectory(fullPath);
        var packageTemp = fullPath + ".tmp";
        if (File.Exists(packageTemp)) File.Delete(packageTemp);

        var workRoot = Path.Combine(Path.GetTempPath(), "DiezProductionPackage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var baseName = BaseName(project);
            var docxPath = Path.Combine(workRoot, baseName + ".docx");
            var csvPath = Path.Combine(workRoot, baseName + "-master.csv");
            var xlsxPath = Path.Combine(workRoot, baseName + "-master.xlsx");

            var docx = await DocxExportService.ExportAsync(project, projectPath, docxPath);
            if (!docx.Exported || string.IsNullOrWhiteSpace(docx.OutputPath))
                return new ProductionPackageResult(false, docx.Message, null, 0);

            var csv = await HandoffExportService.ExportMasterCsvAsync(project, csvPath);
            if (!csv.Exported || string.IsNullOrWhiteSpace(csv.OutputPath))
                return new ProductionPackageResult(false, csv.Message, null, 0);

            var xlsx = await HandoffExportService.ExportMasterXlsxAsync(project, xlsxPath);
            if (!xlsx.Exported || string.IsNullOrWhiteSpace(xlsx.OutputPath))
                return new ProductionPackageResult(false, xlsx.Message, null, 0);

            var metadataText = BuildMetadataJson(project, candidate, freeze);
            var illustrationPlanText = BuildIllustrationPlanCsv(project);
            var handoffNotesText = BuildHandoffNotes(project, candidate, freeze);
            var payloadEntries = new List<ProductionManifestEntry>();

            await using (var stream = new FileStream(packageTemp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await AddFileEntryAsync(archive, "manuscript/" + Path.GetFileName(docx.OutputPath), docx.OutputPath, "Editable illustrated manuscript", payloadEntries);
                await AddFileEntryAsync(archive, "data/" + Path.GetFileName(csv.OutputPath), csv.OutputPath, "Editable structured Master CSV", payloadEntries);
                await AddFileEntryAsync(archive, "data/" + Path.GetFileName(xlsx.OutputPath), xlsx.OutputPath, "Editable structured Master XLSX", payloadEntries);

                var images = project.Materials.Where(IllustrationPlanService.IsImage).ToList();
                for (var i = 0; i < images.Count; i++)
                {
                    var image = images[i];
                    var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, image);
                    if (bytes is null || bytes.Length == 0)
                        throw new InvalidDataException($"Originale immagine non disponibile nel .diez: {image.FileName}");

                    var entryPath = $"assets/images/{i + 1:D3}-{SanitizeFileName(image.FileName)}";
                    await AddBytesEntryAsync(archive, entryPath, bytes, "Original image asset", payloadEntries);
                }

                await AddTextEntryAsync(archive, "handoff/edition-metadata.json", metadataText, "Edition metadata", payloadEntries);
                await AddTextEntryAsync(archive, "handoff/illustration-plan.csv", illustrationPlanText, "Editable illustration placement plan", payloadEntries, emitBom: true);
                await AddTextEntryAsync(archive, "handoff/README-HANDOFF.txt", handoffNotesText, "Production handoff notes", payloadEntries);

                var manifest = new ProductionManifest(
                    1,
                    project.ProjectId,
                    project.Name,
                    candidate.CandidateId,
                    CandidateSequence(candidate),
                    freeze.CandidateId,
                    freeze.ProposedValue ?? string.Empty,
                    candidate.CreatedAtLocal ?? string.Empty,
                    DateTimeOffset.UtcNow.ToString("O"),
                    payloadEntries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList());
                await WriteTextEntryRawAsync(archive, "handoff/manifest.json", JsonSerializer.Serialize(manifest, JsonOptions()));
            }

            File.Move(packageTemp, fullPath, overwrite: true);
            return new ProductionPackageResult(
                true,
                $"Production Package esportato: {Path.GetFileName(fullPath)} · {payloadEntries.Count} file di handoff",
                fullPath,
                payloadEntries.Count);
        }
        catch
        {
            if (File.Exists(packageTemp)) File.Delete(packageTemp);
            throw;
        }
        finally
        {
            try { Directory.Delete(workRoot, recursive: true); } catch { }
        }
    }

    private static string BuildMetadataJson(PreviewProject project, RevisionCandidate candidate, RevisionCandidate freeze)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var document = new ProductionMetadata(
            metadata.Title ?? string.Empty,
            metadata.Subtitle ?? string.Empty,
            metadata.Creator ?? string.Empty,
            metadata.Language ?? string.Empty,
            metadata.Publisher ?? string.Empty,
            metadata.Isbn ?? string.Empty,
            metadata.Description ?? string.Empty,
            project.ProjectId,
            candidate.CandidateId,
            CandidateSequence(candidate),
            freeze.CandidateId,
            freeze.ProposedValue ?? string.Empty);
        return JsonSerializer.Serialize(document, JsonOptions());
    }

    private static string BuildIllustrationPlanCsv(PreviewProject project)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ordine;Immagine;Capitolo/Sezione;Posizione;Larghezza%;Didascalia");
        foreach (var placement in project.IllustrationPlacements.OrderBy(p => p.Ordinal).ThenBy(p => p.PlacementId))
        {
            var image = project.Materials.FirstOrDefault(m => m.MaterialId == placement.MaterialId);
            var content = project.ContentNodes.FirstOrDefault(n => n.ContentId == placement.ContentId);
            AppendCsvRow(builder,
                placement.Ordinal.ToString(),
                image?.FileName ?? string.Empty,
                content?.Title ?? string.Empty,
                IllustrationPlanService.PositionLabel(placement.Position ?? string.Empty),
                placement.WidthPercent.ToString(),
                placement.Caption ?? string.Empty);
        }
        return builder.ToString();
    }

    private static string BuildHandoffNotes(PreviewProject project, RevisionCandidate candidate, RevisionCandidate freeze)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var images = project.Materials.Count(IllustrationPlanService.IsImage);
        var builder = new StringBuilder();
        builder.AppendLine("DIEZ PUBLISHING STUDIO - PRODUCTION HANDOFF");
        builder.AppendLine();
        builder.AppendLine($"Titolo: {metadata.Title}");
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) builder.AppendLine($"Sottotitolo: {metadata.Subtitle}");
        if (!string.IsNullOrWhiteSpace(metadata.Creator)) builder.AppendLine($"Autore/creatore: {metadata.Creator}");
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) builder.AppendLine($"Editore: {metadata.Publisher}");
        if (!string.IsNullOrWhiteSpace(metadata.Isbn)) builder.AppendLine($"ISBN: {metadata.Isbn}");
        builder.AppendLine($"Lingua: {metadata.Language}");
        builder.AppendLine($"Publication Candidate: #{CandidateSequence(candidate)} ({candidate.CandidateId:N})");
        builder.AppendLine($"Edition Freeze: #{freeze.ProposedValue} ({freeze.CandidateId:N})");
        builder.AppendLine();
        builder.AppendLine("CONTENUTO DEL PACCHETTO");
        builder.AppendLine("- manuscript/: DOCX editoriale completamente modificabile; se presente un Piano illustrazioni, le immagini sono incorporate nelle posizioni concordate.");
        builder.AppendLine("- data/: copie strutturate del Master in CSV e XLSX, entrambe modificabili.");
        builder.AppendLine($"- assets/images/: {images} immagini originali estratte dal .diez senza resize, ricompressione, modifica DPI o upscale.");
        builder.AppendLine("- handoff/illustration-plan.csv: piano modificabile delle collocazioni, utile come riferimento per l'impaginatore.");
        builder.AppendLine("- handoff/edition-metadata.json: metadati dell'edizione.");
        builder.AppendLine("- handoff/manifest.json: inventario tecnico con SHA-256 dei file consegnati.");
        builder.AppendLine();
        builder.AppendLine("NOTA DI PRODUZIONE");
        builder.AppendLine("Questo pacchetto è un handoff editabile, non un layout finale. Diez non impone tipografia, font, gabbia, margini o resa definitiva: tali decisioni restano a Word, Publisher o all'impaginatore esterno.");
        builder.AppendLine("Le immagini in assets/images sono gli originali di riferimento anche quando una copia è incorporata nel DOCX.");
        return builder.ToString().TrimEnd();
    }

    private static async Task AddFileEntryAsync(ZipArchive archive, string entryPath, string sourcePath, string role, List<ProductionManifestEntry> manifest)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        await using (var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var target = entry.Open())
            await source.CopyToAsync(target);

        var info = new FileInfo(sourcePath);
        manifest.Add(new ProductionManifestEntry(entryPath, info.Length, await HashFileAsync(sourcePath), role));
    }

    private static async Task AddBytesEntryAsync(ZipArchive archive, string entryPath, byte[] bytes, string role, List<ProductionManifestEntry> manifest)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await target.WriteAsync(bytes);
        manifest.Add(new ProductionManifestEntry(entryPath, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), role));
    }

    private static async Task AddTextEntryAsync(ZipArchive archive, string entryPath, string text, string role, List<ProductionManifestEntry> manifest, bool emitBom = false)
    {
        var encoding = new UTF8Encoding(emitBom);
        var bytes = encoding.GetBytes(text ?? string.Empty);
        await AddBytesEntryAsync(archive, entryPath, bytes, role, manifest);
    }

    private static async Task WriteTextEntryRawAsync(ZipArchive archive, string entryPath, string text)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var writer = new StreamWriter(target, new UTF8Encoding(false));
        await writer.WriteAsync(text ?? string.Empty);
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(';');
            var value = values[i] ?? string.Empty;
            builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        builder.AppendLine();
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    private static int CandidateSequence(RevisionCandidate candidate) =>
        int.TryParse(candidate.ProposedValue, out var value) ? value : 0;

    private static string BaseName(PreviewProject project)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? project.Name : metadata.Title;
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null ? 1 : CandidateSequence(candidate);
        return $"{SanitizeFileName(title)}-publication-{sequence:D3}";
    }

    private static string EnsureExtension(string path, string extension)
    {
        var full = Path.GetFullPath(path);
        return string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase) ? full : full + extension;
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Diez-Edition" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        name = name.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(name) ? "Diez-Edition" : name;
    }

    private sealed record ProductionMetadata(
        string Title,
        string Subtitle,
        string Creator,
        string Language,
        string Publisher,
        string Isbn,
        string Description,
        Guid ProjectId,
        Guid PublicationCandidateId,
        int PublicationCandidateSequence,
        Guid EditionFreezeId,
        string EditionFreezeSequence);

    private sealed record ProductionManifest(
        int FormatVersion,
        Guid ProjectId,
        string ProjectName,
        Guid PublicationCandidateId,
        int PublicationCandidateSequence,
        Guid EditionFreezeId,
        string EditionFreezeSequence,
        string PublicationCandidateCreatedAtLocal,
        string PackageCreatedAtUtc,
        IReadOnlyList<ProductionManifestEntry> Entries);

    private sealed record ProductionManifestEntry(string Path, long SizeBytes, string Sha256, string Role);
}

internal readonly record struct ProductionPackageResult(bool Exported, string Message, string? OutputPath, int ItemCount);
