using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class EpubExportService
{
    private const string EpubMimeType = "application/epub+zip";

    public static string SuggestedFileName(PreviewProject project)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? project.Name : metadata.Title;
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 1 : parsed;
        return $"{SanitizeFileName(title)}-publication-{sequence:D3}.epub";
    }

    public static async Task<EpubExportResult> ExportAsync(PreviewProject project, string outputPath)
    {
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new EpubExportResult(false, "Esportazione EPUB bloccata: il preflight non è READY.", null);

        var candidate = PublicationCandidateService.GetLatest(project);
        if (candidate is null || !PublicationCandidateService.IsLatestCandidateCurrent(project))
            return new EpubExportResult(false, "Esportazione EPUB bloccata: crea un Publication Candidate corrente.", null);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new EpubExportResult(false, "Percorso EPUB non valido.", null);

        var metadata = project.EditionMetadata ?? new EditionMetadata();
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Language))
            return new EpubExportResult(false, "Esportazione EPUB bloccata: titolo e lingua sono obbligatori.", null);

        var nodes = project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .ToList();
        if (nodes.Count == 0)
            return new EpubExportResult(false, "Esportazione EPUB bloccata: nessun contenuto editoriale disponibile.", null);

        var fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".epub", StringComparison.OrdinalIgnoreCase))
            fullPath += ".epub";
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteEntryAsync(archive, "mimetype", EpubMimeType, CompressionLevel.NoCompression);
                await WriteEntryAsync(archive, "META-INF/container.xml", BuildContainerXml(), CompressionLevel.Optimal);
                await WriteEntryAsync(archive, "EPUB/styles.css", BuildCss(), CompressionLevel.Optimal);

                var chapters = new List<EpubChapter>();
                for (var i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    var fileName = $"text/chapter-{i + 1:D3}.xhtml";
                    var title = string.IsNullOrWhiteSpace(node.Title) ? $"Sezione {i + 1}" : node.Title.Trim();
                    chapters.Add(new EpubChapter(i + 1, fileName, title));
                    await WriteEntryAsync(archive, "EPUB/" + fileName,
                        BuildChapterXhtml(metadata.Language, title, node.Body ?? string.Empty), CompressionLevel.Optimal);
                }

                await WriteEntryAsync(archive, "EPUB/nav.xhtml", BuildNavXhtml(metadata, chapters), CompressionLevel.Optimal);
                await WriteEntryAsync(archive, "EPUB/package.opf", BuildPackageOpf(project, candidate, metadata, chapters), CompressionLevel.Optimal);
            }

            if (File.Exists(fullPath)) File.Delete(fullPath);
            File.Move(tempPath, fullPath);
            return new EpubExportResult(true, $"EPUB esportato: {Path.GetFileName(fullPath)}", fullPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static string BuildContainerXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
        "  <rootfiles>\n" +
        "    <rootfile full-path=\"EPUB/package.opf\" media-type=\"application/oebps-package+xml\"/>\n" +
        "  </rootfiles>\n" +
        "</container>";

    private static string BuildPackageOpf(PreviewProject project, RevisionCandidate candidate, EditionMetadata metadata, IReadOnlyList<EpubChapter> chapters)
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xml = XNamespace.Xml;

        var modified = CandidateModifiedUtc(candidate);
        var package = new XElement(opf + "package",
            new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "pub-id"),
            new XAttribute(xml + "lang", metadata.Language),
            new XElement(opf + "metadata",
                new XAttribute(XNamespace.Xmlns + "dc", dc),
                new XElement(dc + "identifier", new XAttribute("id", "pub-id"), $"urn:uuid:{project.ProjectId:D}"),
                new XElement(dc + "title", metadata.Title),
                new XElement(dc + "language", metadata.Language),
                OptionalElement(dc + "creator", metadata.Creator),
                OptionalElement(dc + "publisher", metadata.Publisher),
                OptionalElement(dc + "description", BuildDescription(metadata)),
                OptionalElement(dc + "identifier", string.IsNullOrWhiteSpace(metadata.Isbn) ? string.Empty : $"urn:isbn:{metadata.Isbn}"),
                new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), modified)),
            new XElement(opf + "manifest",
                new XElement(opf + "item", new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"),
                    new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")),
                new XElement(opf + "item", new XAttribute("id", "css"), new XAttribute("href", "styles.css"),
                    new XAttribute("media-type", "text/css")),
                chapters.Select(chapter => new XElement(opf + "item",
                    new XAttribute("id", $"chapter-{chapter.Sequence:D3}"),
                    new XAttribute("href", chapter.FileName),
                    new XAttribute("media-type", "application/xhtml+xml")))),
            new XElement(opf + "spine",
                chapters.Select(chapter => new XElement(opf + "itemref",
                    new XAttribute("idref", $"chapter-{chapter.Sequence:D3}")))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), package);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildNavXhtml(EditionMetadata metadata, IReadOnlyList<EpubChapter> chapters)
    {
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        XNamespace epub = "http://www.idpf.org/2007/ops";
        XNamespace xml = XNamespace.Xml;

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(xhtml + "html",
                new XAttribute(XNamespace.Xmlns + "epub", epub),
                new XAttribute("lang", metadata.Language),
                new XAttribute(xml + "lang", metadata.Language),
                new XElement(xhtml + "head",
                    new XElement(xhtml + "title", metadata.Title),
                    new XElement(xhtml + "link", new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css"), new XAttribute("href", "styles.css"))),
                new XElement(xhtml + "body",
                    new XElement(xhtml + "nav",
                        new XAttribute(epub + "type", "toc"),
                        new XAttribute("id", "toc"),
                        new XElement(xhtml + "h1", "Indice"),
                        new XElement(xhtml + "ol",
                            chapters.Select(chapter => new XElement(xhtml + "li",
                                new XElement(xhtml + "a", new XAttribute("href", chapter.FileName), chapter.Title))))))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildChapterXhtml(string language, string title, string body)
    {
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        XNamespace xml = XNamespace.Xml;

        var article = new XElement(xhtml + "article", new XElement(xhtml + "h1", title));
        foreach (var paragraph in SplitParagraphs(body))
        {
            var p = new XElement(xhtml + "p");
            var lines = paragraph.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Add(new XElement(xhtml + "br"));
                p.Add(new XText(lines[i]));
            }
            article.Add(p);
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(xhtml + "html",
                new XAttribute("lang", language),
                new XAttribute(xml + "lang", language),
                new XElement(xhtml + "head",
                    new XElement(xhtml + "title", title),
                    new XElement(xhtml + "link", new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css"), new XAttribute("href", "../styles.css"))),
                new XElement(xhtml + "body", article)));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static IEnumerable<string> SplitParagraphs(string body)
    {
        var normalized = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0) yield break;

        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!string.IsNullOrWhiteSpace(paragraph)) yield return paragraph;
    }

    private static string BuildCss() =>
        "body { font-family: serif; line-height: 1.45; margin: 5%; }\n" +
        "article { max-width: 42em; margin: 0 auto; }\n" +
        "h1 { break-before: page; margin-bottom: 1.5em; }\n" +
        "p { margin: 0 0 0.9em 0; text-align: left; }\n" +
        "nav ol { padding-left: 1.5em; }";

    private static XElement? OptionalElement(XName name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value.Trim());

    private static string BuildDescription(EditionMetadata metadata)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) parts.Add(metadata.Subtitle.Trim());
        if (!string.IsNullOrWhiteSpace(metadata.Description)) parts.Add(metadata.Description.Trim());
        return string.Join(" — ", parts);
    }

    private static string CandidateModifiedUtc(RevisionCandidate candidate)
    {
        if (DateTimeOffset.TryParse(candidate.CreatedAtLocal, out var parsed))
            return parsed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content, CompressionLevel compression)
    {
        var entry = archive.CreateEntry(name, compression);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content ?? string.Empty);
    }

    private static int MaterialOrder(PreviewProject project, Guid materialId)
    {
        var index = project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Diez-Edition" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        return name.Replace(' ', '-');
    }

    private sealed record EpubChapter(int Sequence, string FileName, string Title);
}

internal readonly record struct EpubExportResult(bool Exported, string Message, string? OutputPath);
