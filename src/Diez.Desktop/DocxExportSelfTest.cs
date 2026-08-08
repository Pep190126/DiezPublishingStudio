using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class DocxExportSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezDocxExport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "docx-source.txt");
            const string originalText = "Capitolo 1\nMilo guarda il mare.\n\nCapitolo 2\nMilo torna al Faro.";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.UTF8);

            var imagePath = Path.Combine(root, "faro.png");
            var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZST8AAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(imagePath, imageBytes);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var imageMaterial = await MaterialImporter.ImportAsync(imagePath);
            var project = ProjectFileStore.Create("Il viaggio di Milo");
            project.Materials.Add(material);
            project.Materials.Add(imageMaterial);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));
            var firstContent = project.ContentNodes.First(n => EditableMasterService.CanEdit(project, n));
            var placementResult = IllustrationPlanService.Upsert(
                project, null, imageMaterial.MaterialId, firstContent.ContentId,
                IllustrationPlanService.AfterHeading, 75, "Il Faro sul mare");
            Require(placementResult.Changed && placementResult.Placement is not null, "La collocazione immagine di prova non è stata creata.");

            var metadataResult = EditionMetadataService.Update(project,
                "Il viaggio di Milo", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Edizione di prova DOCX");
            Require(metadataResult.Changed, "I metadati DOCX di prova non sono stati applicati.");

            var projectPath = Path.Combine(root, "docx.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);
            Require(project.SchemaVersion == 10, "Il piano illustrazioni non è stato salvato nello schema 10.");
            Require(project.IllustrationPlacements.Count == 1 && project.IllustrationPlacements[0].Caption == "Il Faro sul mare", "Il piano illustrazioni non è sopravvissuto al round-trip.");

            var blockedWithoutCandidate = await DocxExportService.ExportAsync(project, projectPath, Path.Combine(root, "blocked.docx"));
            Require(!blockedWithoutCandidate.Exported, "Il DOCX non deve essere esportato senza Publication Candidate.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Edition Freeze non creato nel test DOCX.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY nel test DOCX illustrato.");
            var publication = PublicationCandidateService.Create(project);
            Require(publication.Candidate is not null, "Publication Candidate non creato nel test DOCX.");

            var docxPath = Path.Combine(root, "milo.docx");
            var exported = await DocxExportService.ExportAsync(project, projectPath, docxPath);
            Require(exported.Exported && File.Exists(docxPath), "DOCX illustrato non esportato.");
            await VerifyContainerAsync(docxPath, project, imageBytes);

            var placement = project.IllustrationPlacements.Single();
            var placementChange = IllustrationPlanService.Upsert(
                project, placement.PlacementId, placement.MaterialId, placement.ContentId,
                IllustrationPlanService.AfterContent, 50, "Il Faro dopo il testo");
            Require(placementChange.Changed, "La modifica del piano illustrazioni non è stata applicata.");
            Require(!EditionFreezeService.IsLatestFreezeCurrent(project), "L'Edition Freeze deve risultare superato dopo modifica del piano illustrazioni.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate deve risultare superato dopo modifica del piano illustrazioni.");
            var blockedAfterPlanEdit = await DocxExportService.ExportAsync(project, projectPath, Path.Combine(root, "stale.docx"));
            Require(!blockedAfterPlanEdit.Exported, "Il DOCX non deve essere esportato da un Publication Candidate superato dopo modifica del piano illustrazioni.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task VerifyContainerAsync(string docxPath, PreviewProject project, byte[] expectedImageBytes)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        Require(archive.GetEntry("[Content_Types].xml") is not null, "[Content_Types].xml mancante.");
        Require(archive.GetEntry("_rels/.rels") is not null, "Relazioni package mancanti.");
        Require(archive.GetEntry("word/document.xml") is not null, "word/document.xml mancante.");
        Require(archive.GetEntry("word/styles.xml") is not null, "word/styles.xml mancante.");
        Require(archive.GetEntry("word/_rels/document.xml.rels") is not null, "Relazioni documento mancanti.");
        Require(archive.GetEntry("docProps/core.xml") is not null, "Proprietà core mancanti.");
        Require(archive.GetEntry("docProps/app.xml") is not null, "Proprietà app mancanti.");
        var media = archive.GetEntry("word/media/image-001.png");
        Require(media is not null, "Originale immagine non incorporato nel DOCX.");
        await using (var stream = media!.Open())
        await using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory);
            Require(memory.ToArray().SequenceEqual(expectedImageBytes), "Il media incorporato nel DOCX non coincide byte-per-byte con l'originale.");
        }

        var contentTypes = await ReadEntryAsync(archive, "[Content_Types].xml");
        Require(contentTypes.Contains("wordprocessingml.document.main+xml", StringComparison.Ordinal), "Content type principale DOCX mancante.");
        Require(contentTypes.Contains("image/png", StringComparison.Ordinal), "Content type PNG mancante nel DOCX illustrato.");

        var documentXml = await ReadEntryAsync(archive, "word/document.xml");
        var document = XDocument.Parse(documentXml);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var text = string.Join(" ", document.Descendants(w + "t").Select(t => t.Value));
        Require(text.Contains("Il viaggio di Milo", StringComparison.Ordinal), "Titolo edizione mancante nel DOCX.");
        Require(text.Contains("Ada Autrice", StringComparison.Ordinal), "Autore mancante nel DOCX.");
        Require(text.Contains("Capitolo 1", StringComparison.Ordinal) && text.Contains("Capitolo 2", StringComparison.Ordinal), "Titoli capitoli mancanti nel DOCX.");
        Require(text.Contains("Milo guarda il mare", StringComparison.Ordinal), "Testo primo capitolo mancante nel DOCX.");
        Require(text.Contains("Milo torna al Faro", StringComparison.Ordinal), "Testo secondo capitolo mancante nel DOCX.");
        Require(text.Contains("Il Faro sul mare", StringComparison.Ordinal), "Didascalia illustrazione mancante nel DOCX.");
        Require(document.Descendants(w + "pStyle").Any(e => (string?)e.Attribute(w + "val") == "Heading1"), "Stile Heading1 non applicato ai capitoli.");
        Require(document.Descendants(w + "br").Any(e => (string?)e.Attribute(w + "type") == "page"), "Interruzione pagina dopo frontespizio mancante.");
        Require(document.Descendants(a + "blip").Any(e => !string.IsNullOrWhiteSpace((string?)e.Attribute(r + "embed"))), "DrawingML non collega l'immagine incorporata.");

        var styles = await ReadEntryAsync(archive, "word/styles.xml");
        Require(styles.Contains("styleId=\"Heading1\"", StringComparison.Ordinal), "Definizione Heading1 mancante.");
        Require(styles.Contains("styleId=\"Title\"", StringComparison.Ordinal), "Definizione Title mancante.");
        Require(styles.Contains("styleId=\"Caption\"", StringComparison.Ordinal), "Stile Caption mancante per le didascalie.");

        var core = await ReadEntryAsync(archive, "docProps/core.xml");
        Require(core.Contains("Il viaggio di Milo", StringComparison.Ordinal), "Titolo core properties mancante.");
        Require(core.Contains("Ada Autrice", StringComparison.Ordinal), "Autore core properties mancante.");
        Require(core.Contains("9780306406157", StringComparison.Ordinal), "ISBN core properties mancante.");
        Require(core.Contains("it", StringComparison.Ordinal), "Lingua core properties mancante.");

        var rels = await ReadEntryAsync(archive, "word/_rels/document.xml.rels");
        Require(rels.Contains("relationships/image", StringComparison.Ordinal), "Relazione immagine DOCX mancante.");
        Require(rels.Contains("media/image-001.png", StringComparison.Ordinal), "Target media DOCX mancante.");

        Require(project.ProjectId != Guid.Empty, "ProjectId non valido nel test DOCX.");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("DOCX SELF-TEST: entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("DOCX SELF-TEST: " + message);
    }
}