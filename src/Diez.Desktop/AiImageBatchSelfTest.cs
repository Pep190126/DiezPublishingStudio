using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

internal static class AiImageBatchSelfTest
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2ZQAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezAiImageBatchSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "batch.diez");
            var project = ProjectFileStore.Create("Batch Images Test");
            var jobs = AiImageBatchService.CreateImageSeries(project, 5, "Coloring nostalgico con soggetti diversi.", "Tavola");
            if (jobs.Select(j => j.Code).SequenceEqual(new[] { "IMG-001", "IMG-002", "IMG-003", "IMG-004", "IMG-005" }) is false)
                throw new InvalidOperationException("La serie immagini non mantiene una sequenza IMG-### stabile.");
            await ProjectFileStore.SaveAsync(projectPath, project);

            var pack = Path.Combine(root, "pack.xlsx");
            var packResult = await AiImageBatchService.ExportPackXlsxAsync(
                project, pack, AiImageBatchService.ProviderOpenAi, preferMostAdvancedModel: true, onlyMissingOrToRedo: false);
            if (!packResult.Success || !File.Exists(pack))
                throw new InvalidOperationException("Il pacchetto XLSX immagini non è stato creato.");
            using (var archive = ZipFile.OpenRead(pack))
            {
                if (archive.GetEntry("xl/worksheets/sheet1.xml") is null || archive.GetEntry("xl/worksheets/sheet2.xml") is null)
                    throw new InvalidOperationException("Il pacchetto immagini non contiene i fogli ISTRUZIONI e IMMAGINI.");
                var instructions = await ReadEntryAsync(archive, "xl/worksheets/sheet1.xml");
                var images = await ReadEntryAsync(archive, "xl/worksheets/sheet2.xml");
                if (!instructions.Contains("GPT Image 2", StringComparison.Ordinal) ||
                    !instructions.Contains("Non rinumerare", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Le istruzioni del pacchetto non contengono modello/prevenzione rinumerazione.");
                foreach (var code in jobs.Select(j => j.Code))
                    if (!images.Contains(code, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Il pacchetto non contiene {code}.");
            }

            var firstZip = Path.Combine(root, "first.zip");
            CreateZip(firstZip, "IMG-002.png", "IMG-003.png", "IMG-005.png");
            var firstImport = await AiImageBatchService.ImportResultZipAsync(project, projectPath, firstZip);
            if (!firstImport.Success || firstImport.Linked != 3 || firstImport.Missing != 2)
                throw new InvalidOperationException("Il primo ZIP parziale non è stato ricomposto correttamente.");
            AssertHasResult(project, "IMG-002");
            AssertHasResult(project, "IMG-003");
            AssertHasResult(project, "IMG-005");
            AssertMissing(project, "IMG-001");
            AssertMissing(project, "IMG-004");

            foreach (var code in new[] { "IMG-002", "IMG-003", "IMG-005" })
            {
                var job = project.AiProductionJobs.Single(j => j.Code == code);
                if (!AiProductionService.Approve(project, job).Success)
                    throw new InvalidOperationException($"{code} non è approvabile dopo l'import ZIP.");
            }
            await ProjectFileStore.SaveAsync(projectPath, project);

            var correctionPack = Path.Combine(root, "correction.xlsx");
            var correctionResult = await AiImageBatchService.ExportPackXlsxAsync(
                project, correctionPack, AiImageBatchService.ProviderGemini, preferMostAdvancedModel: true, onlyMissingOrToRedo: true);
            if (!correctionResult.Success)
                throw new InvalidOperationException("Il pacchetto di rettifica non è stato creato.");
            using (var archive = ZipFile.OpenRead(correctionPack))
            {
                var images = await ReadEntryAsync(archive, "xl/worksheets/sheet2.xml");
                var instructions = await ReadEntryAsync(archive, "xl/worksheets/sheet1.xml");
                if (!images.Contains("IMG-001", StringComparison.Ordinal) || !images.Contains("IMG-004", StringComparison.Ordinal) ||
                    images.Contains("IMG-002", StringComparison.Ordinal))
                    throw new InvalidOperationException("Il pacchetto di rettifica non contiene soltanto i buchi della serie.");
                if (!instructions.Contains("Nano Banana Pro", StringComparison.Ordinal))
                    throw new InvalidOperationException("La preferenza Gemini avanzata non è stata scritta nel pacchetto.");
            }

            var secondZip = Path.Combine(root, "second.zip");
            CreateZip(secondZip, "IMG-004.png", "IMG-001.png");
            var secondImport = await AiImageBatchService.ImportResultZipAsync(project, projectPath, secondZip);
            if (!secondImport.Success || secondImport.Linked != 2 || secondImport.Missing != 0)
                throw new InvalidOperationException("Il secondo ZIP non ha riempito automaticamente i buchi per ID.");

            foreach (var job in project.AiProductionJobs.Where(j => j.Code.StartsWith("IMG-", StringComparison.Ordinal)))
            {
                if (!job.ResultMaterialId.HasValue)
                    throw new InvalidOperationException($"{job.Code} è rimasto senza risultato dopo la rettifica.");
                if (job.Status != AiProductionService.StatusApproved && !AiProductionService.Approve(project, job).Success)
                    throw new InvalidOperationException($"{job.Code} non è approvabile.");
            }
            await ProjectFileStore.SaveAsync(projectPath, project);

            var finalZip = Path.Combine(root, "approved.zip");
            var finalResult = await AiImageBatchService.ExportApprovedImagesZipAsync(project, projectPath, finalZip);
            if (!finalResult.Success || !File.Exists(finalZip))
                throw new InvalidOperationException("Lo ZIP finale delle immagini approvate non è stato creato.");
            using (var archive = ZipFile.OpenRead(finalZip))
            {
                var names = archive.Entries.Select(e => e.FullName).ToList();
                var expected = new[] { "IMG-001.png", "IMG-002.png", "IMG-003.png", "IMG-004.png", "IMG-005.png" };
                if (!names.SequenceEqual(expected))
                    throw new InvalidOperationException("Lo ZIP finale non rispetta la sequenza stabile IMG-###.");
                if (names.Any(n => n.Contains("manifest", StringComparison.OrdinalIgnoreCase) || !n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Lo ZIP finale deve contenere soltanto immagini, senza manifest.");
            }

            var reloaded = await ProjectFileStore.LoadAsync(projectPath);
            var ordered = reloaded.AiProductionJobs
                .Where(j => j.Code.StartsWith("IMG-", StringComparison.Ordinal))
                .OrderBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
                .Select(j => j.Code)
                .ToList();
            if (!ordered.SequenceEqual(new[] { "IMG-001", "IMG-002", "IMG-003", "IMG-004", "IMG-005" }))
                throw new InvalidOperationException("La sequenza logica delle immagini non persiste nel .diez.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void CreateZip(string path, params string[] names)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in names)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(PngBytes);
        }
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Voce XLSX mancante: {name}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void AssertHasResult(PreviewProject project, string code)
    {
        if (!project.AiProductionJobs.Single(j => j.Code == code).ResultMaterialId.HasValue)
            throw new InvalidOperationException($"{code} dovrebbe avere un risultato.");
    }

    private static void AssertMissing(PreviewProject project, string code)
    {
        if (project.AiProductionJobs.Single(j => j.Code == code).ResultMaterialId.HasValue)
            throw new InvalidOperationException($"{code} dovrebbe essere ancora mancante.");
    }
}