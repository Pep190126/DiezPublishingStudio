using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

internal static class ImageCollectionLayoutSelfTest
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2ZQAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezImageCollectionLayoutSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "images.diez");
            var project = ProjectFileStore.Create("Coloring Test");
            AiImageBatchService.CreateImageSeries(project, 2, "Line art coerente", "Tavola");
            await ProjectFileStore.SaveAsync(projectPath, project);

            var received = Path.Combine(root, "received.zip");
            using (var archive = ZipFile.Open(received, ZipArchiveMode.Create))
            {
                WriteImage(archive, "IMG-001.png");
                WriteImage(archive, "IMG-002.png");
            }
            var imported = await AiImageBatchService.ImportResultZipAsync(project, projectPath, received);
            if (!imported.Success || imported.Linked != 2)
                throw new InvalidOperationException("Il test non riesce a preparare le immagini della raccolta.");

            foreach (var job in project.AiProductionJobs.Where(j => j.Code.StartsWith("IMG-", StringComparison.Ordinal)))
            {
                if (!AiProductionService.Approve(project, job).Success)
                    throw new InvalidOperationException($"{job.Code} non è approvabile nel test raccolta immagini.");
            }
            var first = project.AiProductionJobs.Single(j => j.Code == "IMG-001");
            var second = project.AiProductionJobs.Single(j => j.Code == "IMG-002");
            ImageCollectionDescriptionService.SetDescription(first, "Descrizione lunga IMG-001\nSeconda riga.\nTerza riga.");
            ImageCollectionDescriptionService.SetDescription(second, "Descrizione IMG-002");
            await ProjectFileStore.SaveAsync(projectPath, project);

            var externalNoDescriptions = Path.Combine(root, "external-no-text.zip");
            var noText = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                externalNoDescriptions,
                ImageCollectionLayoutExportService.External,
                includeDescriptions: false,
                ImageCollectionDescriptionService.DescriptionDocx);
            if (!noText.Success) throw new InvalidOperationException("Impaginazione esterna senza descrizioni fallita.");
            using (var archive = ZipFile.OpenRead(externalNoDescriptions))
            {
                var names = archive.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (!names.SequenceEqual(new[] { "IMG-001.png", "IMG-002.png" }))
                    throw new InvalidOperationException("Impaginazione esterna senza descrizioni deve contenere soltanto gli originali.");
                await AssertImageBytesAsync(archive, "IMG-001.png");
                await AssertImageBytesAsync(archive, "IMG-002.png");
            }

            var externalDocx = Path.Combine(root, "external-docx.zip");
            var withDocx = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                externalDocx,
                ImageCollectionLayoutExportService.External,
                includeDescriptions: true,
                ImageCollectionDescriptionService.DescriptionDocx);
            if (!withDocx.Success) throw new InvalidOperationException("Impaginazione esterna con descrizioni DOCX fallita.");
            using (var archive = ZipFile.OpenRead(externalDocx))
            {
                var names = archive.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var expected = new[] { "IMG-001.docx", "IMG-001.png", "IMG-002.docx", "IMG-002.png" };
                if (!names.SequenceEqual(expected))
                    throw new InvalidOperationException("Le descrizioni DOCX non mantengono lo stesso nome base delle immagini.");
                await AssertImageBytesAsync(archive, "IMG-001.png");
                await AssertDescriptionDocxAsync(archive, "IMG-001.docx", "Descrizione lunga IMG-001", "Terza riga.");
            }

            var externalTxt = Path.Combine(root, "external-txt.zip");
            var withTxt = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                externalTxt,
                ImageCollectionLayoutExportService.External,
                includeDescriptions: true,
                ImageCollectionDescriptionService.DescriptionTxt);
            if (!withTxt.Success) throw new InvalidOperationException("Impaginazione esterna con descrizioni TXT fallita.");
            using (var archive = ZipFile.OpenRead(externalTxt))
            {
                if (archive.GetEntry("IMG-001.txt") is null || archive.GetEntry("IMG-001.docx") is not null)
                    throw new InvalidOperationException("La scelta TXT non viene rispettata.");
            }

            var internalDocx = Path.Combine(root, "internal.docx");
            var internalResult = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                internalDocx,
                ImageCollectionLayoutExportService.Internal,
                includeDescriptions: true,
                ImageCollectionDescriptionService.DescriptionDocx);
            if (!internalResult.Success || !File.Exists(internalDocx))
                throw new InvalidOperationException("Impaginazione interna DOCX fallita.");
            using (var archive = ZipFile.OpenRead(internalDocx))
            {
                var media = archive.GetEntry("word/media/IMG-001.png")
                    ?? throw new InvalidOperationException("Il DOCX interno non contiene IMG-001.");
                await using var stream = media.Open();
                await using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                if (!memory.ToArray().SequenceEqual(PngBytes))
                    throw new InvalidOperationException("Il DOCX interno non conserva i byte originali dell'immagine incorporata.");
            }

            var both = Path.Combine(root, "both.zip");
            var bothResult = await ImageCollectionLayoutChoiceService.ExportAsync(
                project,
                projectPath,
                both,
                ImageCollectionLayoutExportService.Both,
                includeDescriptions: true,
                ImageCollectionDescriptionService.DescriptionDocx);
            if (!bothResult.Success) throw new InvalidOperationException("Export Entrambi fallito.");
            using (var archive = ZipFile.OpenRead(both))
            {
                if (archive.GetEntry("impaginazione-interna.docx") is null ||
                    archive.GetEntry("IMG-001.png") is null ||
                    archive.GetEntry("IMG-001.docx") is null ||
                    archive.GetEntry("IMG-002.png") is null ||
                    archive.GetEntry("IMG-002.docx") is null)
                    throw new InvalidOperationException("Entrambi non contiene DOCX interno, originali e descrizioni gemelle.");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void WriteImage(ZipArchive archive, string name)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(PngBytes);
    }

    private static async Task AssertImageBytesAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Immagine mancante: {name}");
        await using var stream = entry.Open();
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        if (!memory.ToArray().SequenceEqual(PngBytes))
            throw new InvalidOperationException($"L'originale {name} è stato modificato durante l'export.");
    }

    private static async Task AssertDescriptionDocxAsync(ZipArchive outer, string name, params string[] expectedTexts)
    {
        var entry = outer.GetEntry(name) ?? throw new InvalidOperationException($"Descrizione DOCX mancante: {name}");
        await using var source = entry.Open();
        await using var memory = new MemoryStream();
        await source.CopyToAsync(memory);
        memory.Position = 0;
        using var docx = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: true);
        if (docx.GetEntry("[Content_Types].xml") is null || docx.GetEntry("_rels/.rels") is null)
            throw new InvalidOperationException($"{name} non è un pacchetto DOCX completo.");
        var document = docx.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException($"{name} non contiene word/document.xml.");
        await using var documentStream = document.Open();
        using var reader = new StreamReader(documentStream, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();
        foreach (var text in expectedTexts)
            if (!xml.Contains(text, StringComparison.Ordinal))
                throw new InvalidOperationException($"{name} ha perso parte della descrizione: {text}");
    }
}
