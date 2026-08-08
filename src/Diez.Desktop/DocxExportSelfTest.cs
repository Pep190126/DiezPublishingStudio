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

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Il viaggio di Milo");
            project.Materials.Add(material);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));
            var metadataResult = EditionMetadataService.Update(project,
                "Il viaggio di Milo", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Edizione di prova DOCX");
            Require(metadataResult.Changed, "I metadati DOCX di prova non sono stati applicati.");

            var projectPath = Path.Combine(root, "docx.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);

            var blockedWithoutCandidate = await DocxExportService.ExportAsync(project, Path.Combine(root, "blocked.docx"));
            Require(!blockedWithoutCandidate.Exported, "Il DOCX non deve essere esportato senza Publication Candidate.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Edition Freeze non creato nel test DOCX.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY nel test DOCX.");
            var publication = PublicationCandidateService.Create(project);
            Require(publication.Candidate is not null, "Publication Candidate non creato nel test DOCX.");

            var docxPath = Path.Combine(root, "milo.docx");
            var exported = await DocxExportService.ExportAsync(project, docxPath);
            Require(exported.Exported && File.Exists(docxPath), "DOCX non esportato.");
            await VerifyContainerAsync(docxPath, project);

            var metadataChange = EditionMetadataService.Update(project,
                "Il viaggio di Milo - seconda edizione", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Edizione di prova DOCX");
            Require(metadataChange.Changed, "La modifica metadati dopo DOCX non è stata applicata.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate deve risultare superato dopo modifica metadati.");
            var blockedAfterMetadataEdit = await DocxExportService.ExportAsync(project, Path.Combine(root, "stale.docx"));
            Require(!blockedAfterMetadataEdit.Exported, "Il DOCX non deve essere esportato da un Publication Candidate superato.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task VerifyContainerAsync(string docxPath, PreviewProject project)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        Require(archive.GetEntry("[Content_Types].xml") is not null, "[Content_Types].xml mancante.");
        Require(archive.GetEntry("_rels/.rels") is not null, "Relazioni package mancanti.");
        Require(archive.GetEntry("word/document.xml") is not null, "word/document.xml mancante.");
        Require(archive.GetEntry("word/styles.xml") is not null, "word/styles.xml mancante.");
        Require(archive.GetEntry("word/_rels/document.xml.rels") is not null, "Relazioni documento mancanti.");
        Require(archive.GetEntry("docProps/core.xml") is not null, "Proprietà core mancanti.");
        Require(archive.GetEntry("docProps/app.xml") is not null, "Proprietà app mancanti.");

        var contentTypes = await ReadEntryAsync(archive, "[Content_Types].xml");
        Require(contentTypes.Contains("wordprocessingml.document.main+xml", StringComparison.Ordinal), "Content type principale DOCX mancante.");

        var documentXml = await ReadEntryAsync(archive, "word/document.xml");
        var document = XDocument.Parse(documentXml);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = string.Join(" ", document.Descendants(w + "t").Select(t => t.Value));
        Require(text.Contains("Il viaggio di Milo", StringComparison.Ordinal), "Titolo edizione mancante nel DOCX.");
        Require(text.Contains("Ada Autrice", StringComparison.Ordinal), "Autore mancante nel DOCX.");
        Require(text.Contains("Capitolo 1", StringComparison.Ordinal) && text.Contains("Capitolo 2", StringComparison.Ordinal), "Titoli capitoli mancanti nel DOCX.");
        Require(text.Contains("Milo guarda il mare", StringComparison.Ordinal), "Testo primo capitolo mancante nel DOCX.");
        Require(text.Contains("Milo torna al Faro", StringComparison.Ordinal), "Testo secondo capitolo mancante nel DOCX.");
        Require(document.Descendants(w + "pStyle").Any(e => (string?)e.Attribute(w + "val") == "Heading1"), "Stile Heading1 non applicato ai capitoli.");
        Require(document.Descendants(w + "br").Any(e => (string?)e.Attribute(w + "type") == "page"), "Interruzione pagina dopo frontespizio mancante.");

        var styles = await ReadEntryAsync(archive, "word/styles.xml");
        Require(styles.Contains("styleId=\"Heading1\"", StringComparison.Ordinal), "Definizione Heading1 mancante.");
        Require(styles.Contains("styleId=\"Title\"", StringComparison.Ordinal), "Definizione Title mancante.");

        var core = await ReadEntryAsync(archive, "docProps/core.xml");
        Require(core.Contains("Il viaggio di Milo", StringComparison.Ordinal), "Titolo core properties mancante.");
        Require(core.Contains("Ada Autrice", StringComparison.Ordinal), "Autore core properties mancante.");
        Require(core.Contains("9780306406157", StringComparison.Ordinal), "ISBN core properties mancante.");
        Require(core.Contains("it", StringComparison.Ordinal), "Lingua core properties mancante.");

        var rels = await ReadEntryAsync(archive, "_rels/.rels");
        Require(rels.Contains("word/document.xml", StringComparison.Ordinal), "Relazione verso il documento Word mancante.");
        Require(rels.Contains("docProps/core.xml", StringComparison.Ordinal), "Relazione verso core properties mancante.");

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
