using System.IO.Compression;

namespace DiezPublishingStudio;

internal static class VisualHandoffExportService
{
    public static string SuggestedFileName(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var safe = Sanitize(title);
        return $"{safe}-immagini-finali.zip";
    }

    public static async Task<HandoffExportResult> ExportFinalImagesZipAsync(
        PreviewProject project,
        string projectPath,
        string outputPath)
    {
        if (!VisualBookPlanService.IsVisualFamily(project))
            return new HandoffExportResult(false, "Il progetto non è un libro con immagini.", null, 0);
        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
            return new HandoffExportResult(false, "Salva prima il progetto come pacchetto .diez.", null, 0);
        if (string.IsNullOrWhiteSpace(outputPath))
            return new HandoffExportResult(false, "Percorso ZIP non valido.", null, 0);

        var problems = VisualBookPlanService.ProductionProblems(project);
        if (problems.Count > 0)
            return new HandoffExportResult(false, "Export immagini bloccato: " + string.Join(" ", problems.Take(3)), null, 0);

        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new HandoffExportResult(false, "Export immagini bloccato: il preflight non è READY.", null, 0);
        if (!PublicationCandidateService.IsLatestCandidateCurrent(project))
            return new HandoffExportResult(false, "Export immagini bloccato: crea un Publication Candidate corrente.", null, 0);

        var plan = VisualBookPlanService.Load(project);
        var jobs = VisualBookPlanService.AppliedImageJobs(project);
        if (jobs.Count != plan.ImageCount)
            return new HandoffExportResult(false, "Export immagini bloccato: il numero di immagini finali non coincide con il piano.", null, 0);

        var final = new List<(AiProductionJob Job, MaterialEntry Material)>();
        var seenMaterials = new HashSet<Guid>();
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            if (!job.ResultMaterialId.HasValue)
                return new HandoffExportResult(false, $"{job.Code}: materiale finale mancante.", null, 0);
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId.Value);
            if (material is null || !IllustrationPlanService.IsImage(material))
                return new HandoffExportResult(false, $"{job.Code}: immagine finale non trovata.", null, 0);
            if (!material.IsEmbedded)
                return new HandoffExportResult(false, $"{job.Code}: l'immagine finale non è incorporata nel .diez.", null, 0);
            if (!seenMaterials.Add(material.MaterialId))
                return new HandoffExportResult(false, $"{job.Code}: lo stesso asset finale è usato da più immagini del libro.", null, 0);
            if (!string.IsNullOrWhiteSpace(material.Sha256) && !seenHashes.Add(material.Sha256))
                return new HandoffExportResult(false, $"{job.Code}: due immagini finali hanno contenuto identico.", null, 0);
            final.Add((job, material));
        }

        var fullPath = outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(outputPath) : Path.GetFullPath(outputPath + ".zip");
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                for (var i = 0; i < final.Count; i++)
                {
                    var (job, material) = final[i];
                    var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                    if (bytes is null)
                        throw new InvalidDataException($"Originale finale non disponibile nel .diez: {material.FileName}");
                    var extension = Path.GetExtension(material.FileName);
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
                    var baseName = Path.GetFileNameWithoutExtension(material.FileName);
                    var entryName = $"{i + 1:D3}-{Sanitize(job.Code)}-{Sanitize(baseName)}{extension.ToLowerInvariant()}";
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await target.WriteAsync(bytes);
                }
            }
            File.Move(temp, fullPath, overwrite: true);
            return new HandoffExportResult(true, $"ZIP immagini finali esportato: {Path.GetFileName(fullPath)} · {final.Count} asset", fullPath, final.Count);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    private static string Sanitize(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Diez" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '-');
        return text.Replace(' ', '-');
    }
}
