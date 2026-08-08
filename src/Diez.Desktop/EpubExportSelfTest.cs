using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

internal static class EpubExportSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezEpubExport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "epub-source.txt");
            const string originalText = "Capitolo 1\nMilo guarda il mare.\n\nCapitolo 2\nMilo torna al Faro.";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.UTF8);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Il viaggio di Milo");
            project.Materials.Add(material);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));
            var metadataResult = EditionMetadataService.Update(project,
                "Il viaggio di Milo", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Edizione di prova EPUB");
            Require(metadataResult.Changed, "I metadati EPUB di prova non sono stati applicati.");

            var projectPath = Path.Combine(root, "epub.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);

            var blockedWithoutCandidate = await EpubExportService.ExportAsync(project, Path.Combine(root, "blocked.epub"));
            Require(!blockedWithoutCandidate.Exported, "L'EPUB non deve essere esportato senza Publication Candidate.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Edition Freeze non creato nel test EPUB.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY nel test EPUB.");
            var publication = PublicationCandidateService.Create(project);
            Require(publication.Candidate is not null, "Publication Candidate non creato nel test EPUB.");

            var epubPath = Path.Combine(root, "milo.epub");
            var exported = await EpubExportService.ExportAsync(project, epubPath);
            Require(exported.Exported && File.Exists(epubPath), "EPUB non esportato.");

            await VerifyContainerAsync(epubPath, project);

            var metadataChange = EditionMetadataService.Update(project,
                "Il viaggio di Milo - seconda edizione", "Una storia del Faro", "Ada Autrice", "it", "Diez", "9780306406157", "Edizione di prova EPUB");
            Require(metadataChange.Changed, "La modifica metadati dopo EPUB non è stata applicata.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate deve risultare superato dopo modifica metadati.");
            var blockedAfterMetadataEdit = await EpubExportService.ExportAsync(project, Path.Combine(root, "stale.epub"));
            Require(!blockedAfterMetadataEdit.Exported, "L'EPUB non deve essere esportato da un Publication Candidate superato.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task VerifyContainerAsync(string epubPath, PreviewProject project)
    {
        var bytes = await File.ReadAllBytesAsync(epubPath);
        Require(bytes.Length > 30, "EPUB troppo piccolo per essere un container valido.");
        Require(bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 3 && bytes[3] == 4,
            "L'EPUB non inizia con un local ZIP header.");
        var compressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        Require(compressionMethod == 0, "La prima entry EPUB deve essere il mimetype non compresso.");

        using var archive = ZipFile.OpenRead(epubPath);
        Require(archive.Entries.FirstOrDefault()?.FullName == "mimetype", "mimetype non è la prima entry EPUB.");
        Require(await ReadEntryAsync(archive, "mimetype") == "application/epub+zip", "mimetype EPUB errato.");
        Require(archive.GetEntry("META-INF/container.xml") is not null, "container.xml mancante.");
        Require(archive.GetEntry("EPUB/package.opf") is not null, "package.opf mancante.");
        Require(archive.GetEntry("EPUB/nav.xhtml") is not null, "nav.xhtml mancante.");
        Require(archive.GetEntry("EPUB/styles.css") is not null, "styles.css mancante.");
        Require(archive.GetEntry("EPUB/text/chapter-001.xhtml") is not null, "Primo capitolo XHTML mancante.");
        Require(archive.GetEntry("EPUB/text/chapter-002.xhtml") is not null, "Secondo capitolo XHTML mancante.");

        var package = await ReadEntryAsync(archive, "EPUB/package.opf");
        Require(package.Contains("version=\"3.0\"", StringComparison.Ordinal), "Versione package EPUB errata.");
        Require(package.Contains("Il viaggio di Milo", StringComparison.Ordinal), "Titolo non presente nel package EPUB.");
        Require(package.Contains("Ada Autrice", StringComparison.Ordinal), "Autore non presente nel package EPUB.");
        Require(package.Contains("9780306406157", StringComparison.Ordinal), "ISBN non presente nel package EPUB.");
        Require(package.Contains($"urn:uuid:{project.ProjectId:D}", StringComparison.Ordinal), "Identificatore progetto non presente nel package EPUB.");
        Require(package.Contains("property=\"nav\"", StringComparison.Ordinal), "Navigation document non dichiarato nel manifest EPUB.");
        Require(package.Contains("dcterms:modified", StringComparison.Ordinal), "Data dcterms:modified mancante nel package EPUB.");

        var nav = await ReadEntryAsync(archive, "EPUB/nav.xhtml");
        Require(nav.Contains("epub:type=\"toc\"", StringComparison.Ordinal), "TOC EPUB non dichiarata nel navigation document.");
        Require(nav.Contains("chapter-001.xhtml", StringComparison.Ordinal) && nav.Contains("chapter-002.xhtml", StringComparison.Ordinal),
            "La TOC EPUB non contiene entrambi i capitoli.");

        var chapter1 = await ReadEntryAsync(archive, "EPUB/text/chapter-001.xhtml");
        var chapter2 = await ReadEntryAsync(archive, "EPUB/text/chapter-002.xhtml");
        Require(chapter1.Contains("Milo guarda il mare", StringComparison.Ordinal), "Testo del primo capitolo mancante nell'EPUB.");
        Require(chapter2.Contains("Milo torna al Faro", StringComparison.Ordinal), "Testo del secondo capitolo mancante nell'EPUB.");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("EPUB SELF-TEST: entry mancante: " + path);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("EPUB SELF-TEST: " + message);
    }
}
