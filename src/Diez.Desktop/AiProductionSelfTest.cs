using System.IO.Compression;

namespace DiezPublishingStudio;

internal static class AiProductionSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezAiProductionSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "ai-production.diez");
            var textSource = Path.Combine(root, "source.txt");
            await File.WriteAllTextAsync(textSource, "Testo originale del capitolo.");
            var sourceMaterial = await MaterialImporter.ImportAsync(textSource);
            sourceMaterial.ExtractedText = "Testo originale del capitolo.";

            var project = ProjectFileStore.Create("AI Production Test");
            project.Materials.Add(sourceMaterial);
            var node = new ContentNode
            {
                MaterialId = sourceMaterial.MaterialId,
                Kind = "Section",
                Title = "Capitolo test",
                Body = "Testo originale del capitolo.",
                Ordinal = 1,
                SourceLocator = "test:1"
            };
            project.ContentNodes.Add(node);
            await ProjectFileStore.SaveAsync(projectPath, project);

            AiProductionService.SetProjectBrief(project,
                "Libro coerente, linguaggio semplice. Per le immagini usa uno stile line art pulito e senza testo.");

            var imageJob = AiProductionService.CreateJob(project, AiProductionService.TypeImage,
                "Jukebox anni '50", "Disegna un jukebox vintage isolato, adatto a un coloring book.");
            if (imageJob.Code != "IMG-001" || !imageJob.Prompt.Contains("BRIEF GENERALE", StringComparison.Ordinal))
                throw new InvalidOperationException("Il primo job immagine non ha codice/prompt attesi.");

            var pngPath = Path.Combine(root, "IMG-001.png");
            var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2ZQAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(pngPath, pngBytes);
            var attach = await AiProductionService.AttachResultFileAsync(project, projectPath, imageJob, pngPath);
            if (!attach.Success || imageJob.Status != AiProductionService.StatusToReview || !imageJob.ResultMaterialId.HasValue)
                throw new InvalidOperationException("Il risultato immagine non è stato collegato correttamente al job.");
            if (!AiProductionService.Approve(project, imageJob).Success || imageJob.Status != AiProductionService.StatusApproved)
                throw new InvalidOperationException("Il risultato immagine non è approvabile.");

            var textJob = AiProductionService.CreateJob(project, AiProductionService.TypeText,
                "Riscrittura capitolo", "Rendi il testo più scorrevole mantenendo il significato.", node.ContentId);
            AiProductionService.SetTextResult(textJob, "Testo AI approvato ma non ancora applicato.");
            if (!AiProductionService.Approve(project, textJob).Success)
                throw new InvalidOperationException("Il job testuale non è approvabile.");
            if (node.Body != "Testo originale del capitolo.")
                throw new InvalidOperationException("L'approvazione AI ha modificato il Master prima dell'applicazione esplicita.");
            var apply = AiProductionService.ApplyApprovedText(project, textJob);
            if (!apply.Success || node.Body != "Testo AI approvato ma non ancora applicato." || textJob.Status != AiProductionService.StatusApplied)
                throw new InvalidOperationException("Il testo AI approvato non è stato applicato correttamente al Master.");

            var dataJob = AiProductionService.CreateJob(project, AiProductionService.TypeData,
                "Parole nostalgiche", "Genera una tabella di termini nostalgici con categoria e nota.");
            AiProductionService.SetTextResult(dataJob, "Termine;Categoria;Nota\nBIGLIE;Giochi;Anni 70");
            if (dataJob.Code != "DAT-001") throw new InvalidOperationException("Codice job dati inatteso.");

            var csvPath = Path.Combine(root, "prompt-pack.csv");
            var xlsxPath = Path.Combine(root, "prompt-pack.xlsx");
            if (!(await AiPromptPackExportService.ExportCsvAsync(project, csvPath)).Success || !File.Exists(csvPath))
                throw new InvalidOperationException("Prompt pack CSV non esportato.");
            if (!(await AiPromptPackExportService.ExportXlsxAsync(project, xlsxPath)).Success || !File.Exists(xlsxPath))
                throw new InvalidOperationException("Prompt pack XLSX non esportato.");
            using (var archive = ZipFile.OpenRead(xlsxPath))
            {
                if (archive.GetEntry("xl/worksheets/sheet1.xml") is null || archive.GetEntry("xl/workbook.xml") is null)
                    throw new InvalidOperationException("Prompt pack XLSX non contiene le parti OOXML richieste.");
            }

            await ProjectFileStore.SaveAsync(projectPath, project);
            var reloaded = await ProjectFileStore.LoadAsync(projectPath);
            if (reloaded.SchemaVersion != 11 || reloaded.AiProductionJobs.Count != 3)
                throw new InvalidOperationException("La coda AI non persiste nello schema 11.");
            var imageReloaded = reloaded.AiProductionJobs.Single(j => j.Code == "IMG-001");
            if (imageReloaded.Status != AiProductionService.StatusApproved || !imageReloaded.ResultMaterialId.HasValue)
                throw new InvalidOperationException("Stato/collegamento del job immagine non persistono.");
            var resultMaterial = reloaded.Materials.Single(m => m.MaterialId == imageReloaded.ResultMaterialId.Value);
            var embedded = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, resultMaterial);
            if (embedded is null || !embedded.SequenceEqual(pngBytes))
                throw new InvalidOperationException("Il risultato immagine AI non è incorporato byte-per-byte nel .diez.");
            if (reloaded.ContentNodes.Single(n => n.ContentId == node.ContentId).Body != "Testo AI approvato ma non ancora applicato.")
                throw new InvalidOperationException("Il testo AI applicato non persiste nel Master.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
