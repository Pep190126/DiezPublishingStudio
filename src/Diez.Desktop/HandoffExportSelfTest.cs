using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class HandoffExportSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezHandoffExport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var textPath = Path.Combine(root, "master-source.txt");
            const string originalText = "Capitolo 1\nMilo guarda il mare.\n\nCapitolo 2\nMilo torna al Faro.";
            await File.WriteAllTextAsync(textPath, originalText, Encoding.UTF8);

            var imagePath = Path.Combine(root, "tavola-01.png");
            var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZST8AAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(imagePath, imageBytes);

            var textMaterial = await MaterialImporter.ImportAsync(textPath);
            textMaterial.ExtractedText = await EditorialTextExtractor.ExtractAsync(textPath);
            var imageMaterial = await MaterialImporter.ImportAsync(imagePath);

            var project = ProjectFileStore.Create("Il viaggio di Milo");
            project.Materials.Add(textMaterial);
            project.Materials.Add(imageMaterial);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(textMaterial));
            var metadataResult = EditionMetadataService.Update(project,
                "Il viaggio di Milo", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Handoff editabile di prova");
            Require(metadataResult.Changed, "I metadati di prova non sono stati applicati.");

            var projectPath = Path.Combine(root, "handoff.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);

            var imageZipPath = Path.Combine(root, "immagini.zip");
            var imageExport = await HandoffExportService.ExportOriginalImagesZipAsync(project, projectPath, imageZipPath);
            Require(imageExport.Exported && imageExport.ItemCount == 1 && File.Exists(imageZipPath), "ZIP immagini non esportato prima del Publication Candidate.");
            await VerifyImageArchiveAsync(imageZipPath, imageBytes);

            var blockedCsv = await HandoffExportService.ExportMasterCsvAsync(project, Path.Combine(root, "blocked.csv"));
            var blockedXlsx = await HandoffExportService.ExportMasterXlsxAsync(project, Path.Combine(root, "blocked.xlsx"));
            Require(!blockedCsv.Exported && !blockedXlsx.Exported, "CSV/XLSX editoriali non devono essere esportati senza Publication Candidate.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Edition Freeze non creato nel test handoff.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY nel test handoff.");
            var publication = PublicationCandidateService.Create(project);
            Require(publication.Candidate is not null, "Publication Candidate non creato nel test handoff.");

            var csvPath = Path.Combine(root, "master.csv");
            var csvExport = await HandoffExportService.ExportMasterCsvAsync(project, csvPath);
            Require(csvExport.Exported && File.Exists(csvPath), "CSV Master non esportato.");
            var csv = await File.ReadAllTextAsync(csvPath, Encoding.UTF8);
            Require(csv.Contains("Ordine;Materiale;Tipo;Titolo;Testo;Origine", StringComparison.Ordinal), "Header CSV handoff mancante.");
            Require(csv.Contains("Capitolo 1", StringComparison.Ordinal) && csv.Contains("Milo guarda il mare", StringComparison.Ordinal), "Contenuto primo capitolo mancante nel CSV.");
            Require(csv.Contains("Capitolo 2", StringComparison.Ordinal) && csv.Contains("Milo torna al Faro", StringComparison.Ordinal), "Contenuto secondo capitolo mancante nel CSV.");

            var xlsxPath = Path.Combine(root, "master.xlsx");
            var xlsxExport = await HandoffExportService.ExportMasterXlsxAsync(project, xlsxPath);
            Require(xlsxExport.Exported && File.Exists(xlsxPath), "XLSX Master non esportato.");
            await VerifyXlsxAsync(xlsxPath);

            var metadataChange = EditionMetadataService.Update(project,
                "Il viaggio di Milo - seconda edizione", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Handoff editabile di prova");
            Require(metadataChange.Changed, "La modifica metadati dopo handoff non è stata applicata.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate deve risultare superato dopo modifica metadati.");
            Require(!(await HandoffExportService.ExportMasterCsvAsync(project, Path.Combine(root, "stale.csv"))).Exported, "CSV non deve essere esportato da candidate superato.");
            Require(!(await HandoffExportService.ExportMasterXlsxAsync(project, Path.Combine(root, "stale.xlsx"))).Exported, "XLSX non deve essere esportato da candidate superato.");

            var imageExportAfterMetadataChange = await HandoffExportService.ExportOriginalImagesZipAsync(project, projectPath, Path.Combine(root, "immagini-2.zip"));
            Require(imageExportAfterMetadataChange.Exported, "Lo ZIP degli originali non deve dipendere dal Publication Candidate.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task VerifyImageArchiveAsync(string zipPath, byte[] expectedBytes)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        Require(archive.Entries.Count == 1, "Lo ZIP coloring deve contenere solo le immagini, senza manifest o file accessori.");
        var entry = archive.Entries[0];
        Require(entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Entry ZIP immagini non riconosciuta come PNG.");
        await using var stream = entry.Open();
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Require(memory.ToArray().SequenceEqual(expectedBytes), "L'immagine esportata non coincide byte-per-byte con l'originale incorporato.");
    }

    private static async Task VerifyXlsxAsync(string xlsxPath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        Require(archive.GetEntry("[Content_Types].xml") is not null, "[Content_Types].xml XLSX mancante.");
        Require(archive.GetEntry("_rels/.rels") is not null, "Relazioni package XLSX mancanti.");
        Require(archive.GetEntry("xl/workbook.xml") is not null, "Workbook XLSX mancante.");
        Require(archive.GetEntry("xl/_rels/workbook.xml.rels") is not null, "Relazioni workbook XLSX mancanti.");
        Require(archive.GetEntry("xl/styles.xml") is not null, "Stili XLSX mancanti.");
        Require(archive.GetEntry("xl/worksheets/sheet1.xml") is not null, "Foglio Master XLSX mancante.");

        var workbook = await ReadEntryAsync(archive, "xl/workbook.xml");
        Require(workbook.Contains("name=\"Master\"", StringComparison.Ordinal), "Foglio Master non dichiarato nel workbook.");

        var sheetXml = await ReadEntryAsync(archive, "xl/worksheets/sheet1.xml");
        var sheet = XDocument.Parse(sheetXml);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var text = string.Join(" ", sheet.Descendants(main + "t").Select(t => t.Value));
        Require(text.Contains("Ordine", StringComparison.Ordinal) && text.Contains("Parte", StringComparison.Ordinal), "Colonne handoff XLSX mancanti.");
        Require(text.Contains("Capitolo 1", StringComparison.Ordinal) && text.Contains("Milo guarda il mare", StringComparison.Ordinal), "Primo capitolo mancante nell'XLSX.");
        Require(text.Contains("Capitolo 2", StringComparison.Ordinal) && text.Contains("Milo torna al Faro", StringComparison.Ordinal), "Secondo capitolo mancante nell'XLSX.");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("HANDOFF SELF-TEST: entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("HANDOFF SELF-TEST: " + message);
    }
}
