using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class ProductionPackageSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezProductionPackage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "manoscritto.txt");
            await File.WriteAllTextAsync(sourcePath,
                "Capitolo 1\nMilo osserva il Faro.\n\nCapitolo 2\nMilo torna a casa.", Encoding.UTF8);

            var imagePath = Path.Combine(root, "faro.png");
            var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZST8AAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(imagePath, imageBytes);

            var textMaterial = await MaterialImporter.ImportAsync(sourcePath);
            textMaterial.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var imageMaterial = await MaterialImporter.ImportAsync(imagePath);

            var project = ProjectFileStore.Create("Il Faro di Milo");
            project.Materials.Add(textMaterial);
            project.Materials.Add(imageMaterial);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(textMaterial));
            var metadata = EditionMetadataService.Update(project,
                "Il Faro di Milo", "Libro illustrato", "Ada Autrice", "it", "Diez", "9780306406157", "Production Package self-test");
            Require(metadata.Changed, "Metadati non applicati.");

            var firstChapter = project.ContentNodes.First(n => EditableMasterService.CanEdit(project, n));
            var placement = IllustrationPlanService.Upsert(
                project, null, imageMaterial.MaterialId, firstChapter.ContentId,
                IllustrationPlanService.AfterHeading, 75, "Il Faro al tramonto");
            Require(placement.Changed && placement.Placement is not null, "Piano illustrazioni non creato.");

            var projectPath = Path.Combine(root, "production.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);

            var blocked = await ProductionPackageService.ExportAsync(project, projectPath, Path.Combine(root, "blocked.zip"));
            Require(!blocked.Exported, "Il Production Package non deve uscire senza Publication Candidate.");

            Require(EditionFreezeService.CreateFreeze(project).Freeze is not null, "Edition Freeze non creato.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Preflight non READY.");
            Require(PublicationCandidateService.Create(project).Candidate is not null, "Publication Candidate non creato.");

            var packagePath = Path.Combine(root, "production-package.zip");
            var result = await ProductionPackageService.ExportAsync(project, projectPath, packagePath);
            Require(result.Exported && File.Exists(packagePath), "Production Package non esportato.");
            await VerifyPackageAsync(packagePath, imageBytes);

            var existing = project.IllustrationPlacements.Single();
            var changed = IllustrationPlanService.Upsert(
                project, existing.PlacementId, existing.MaterialId, existing.ContentId,
                IllustrationPlanService.AfterContent, 50, "Il Faro al tramonto - nuova posizione");
            Require(changed.Changed, "Modifica del Piano illustrazioni non applicata.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate deve diventare stale dopo modifica del Piano illustrazioni.");

            var stale = await ProductionPackageService.ExportAsync(project, projectPath, Path.Combine(root, "stale.zip"));
            Require(!stale.Exported, "Il Production Package non deve uscire da un Publication Candidate stale.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task VerifyPackageAsync(string packagePath, byte[] expectedImage)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Require(names.Any(n => n.StartsWith("manuscript/", StringComparison.Ordinal) && n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)), "DOCX manuscript mancante.");
        Require(names.Any(n => n.StartsWith("data/", StringComparison.Ordinal) && n.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)), "CSV Master mancante.");
        Require(names.Any(n => n.StartsWith("data/", StringComparison.Ordinal) && n.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)), "XLSX Master mancante.");
        Require(names.Any(n => n.StartsWith("assets/images/", StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)), "Originale immagine mancante.");
        Require(names.Contains("handoff/edition-metadata.json", StringComparer.Ordinal), "Metadati handoff mancanti.");
        Require(names.Contains("handoff/illustration-plan.csv", StringComparer.Ordinal), "Piano illustrazioni handoff mancante.");
        Require(names.Contains("handoff/README-HANDOFF.txt", StringComparer.Ordinal), "README handoff mancante.");
        Require(names.Contains("handoff/manifest.json", StringComparer.Ordinal), "Manifest handoff mancante.");
        Require(!names.Any(n => n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)), "Il Production Package non deve contenere PDF o EPUB.");

        var imageEntry = archive.Entries.Single(e => e.FullName.StartsWith("assets/images/", StringComparison.Ordinal));
        var imageBytes = await ReadBytesAsync(imageEntry);
        Require(imageBytes.SequenceEqual(expectedImage), "L'originale immagine nel package non coincide byte-per-byte.");

        var plan = await ReadTextAsync(archive.GetEntry("handoff/illustration-plan.csv")!);
        Require(plan.Contains("Il Faro al tramonto", StringComparison.Ordinal), "Didascalia mancante nel piano illustrazioni.");
        Require(plan.Contains("Dopo il titolo", StringComparison.Ordinal), "Posizione leggibile mancante nel piano illustrazioni.");
        Require(plan.Contains("75", StringComparison.Ordinal), "Larghezza immagine mancante nel piano illustrazioni.");

        var readme = await ReadTextAsync(archive.GetEntry("handoff/README-HANDOFF.txt")!);
        Require(readme.Contains("handoff editabile", StringComparison.OrdinalIgnoreCase), "Il README non chiarisce la natura editabile del package.");
        Require(readme.Contains("Publisher", StringComparison.OrdinalIgnoreCase), "Il README non cita Publisher come destinazione possibile.");

        var manifestText = await ReadTextAsync(archive.GetEntry("handoff/manifest.json")!);
        using var manifest = JsonDocument.Parse(manifestText);
        Require(manifest.RootElement.GetProperty("FormatVersion").GetInt32() == 1, "Versione manifest inattesa.");
        var manifestEntries = manifest.RootElement.GetProperty("Entries").EnumerateArray().ToList();
        Require(manifestEntries.Count >= 7, "Il manifest non inventaria tutti i payload principali.");
        var imageManifest = manifestEntries.Single(e => e.GetProperty("Path").GetString() == imageEntry.FullName);
        Require(imageManifest.GetProperty("SizeBytes").GetInt64() == expectedImage.LongLength, "Dimensione immagine errata nel manifest.");
        Require(!string.IsNullOrWhiteSpace(imageManifest.GetProperty("Sha256").GetString()), "SHA-256 immagine mancante nel manifest.");

        var docxEntry = archive.Entries.Single(e => e.FullName.StartsWith("manuscript/", StringComparison.Ordinal));
        var docxBytes = await ReadBytesAsync(docxEntry);
        using var docxStream = new MemoryStream(docxBytes);
        using var docx = new ZipArchive(docxStream, ZipArchiveMode.Read);
        var embedded = docx.Entries.SingleOrDefault(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        Require(embedded is not null, "Il DOCX del Production Package non contiene l'immagine prevista.");
        Require((await ReadBytesAsync(embedded!)).SequenceEqual(expectedImage), "Il media incorporato nel DOCX non coincide con l'originale.");
    }

    private static async Task<byte[]> ReadBytesAsync(ZipArchiveEntry entry)
    {
        await using var input = entry.Open();
        await using var memory = new MemoryStream();
        await input.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task<string> ReadTextAsync(ZipArchiveEntry entry)
    {
        await using var input = entry.Open();
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PRODUCTION PACKAGE SELF-TEST: " + message);
    }
}
