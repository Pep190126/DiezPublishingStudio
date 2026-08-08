using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class DocxExportService
{
    private const string WordMainNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static string SuggestedFileName(PreviewProject project)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? project.Name : metadata.Title;
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 1 : parsed;
        return $"{SanitizeFileName(title)}-publication-{sequence:D3}.docx";
    }

    public static async Task<DocxExportResult> ExportAsync(PreviewProject project, string outputPath)
    {
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new DocxExportResult(false, "Esportazione DOCX bloccata: il preflight non è READY.", null);

        var candidate = PublicationCandidateService.GetLatest(project);
        if (candidate is null || !PublicationCandidateService.IsLatestCandidateCurrent(project))
            return new DocxExportResult(false, "Esportazione DOCX bloccata: crea un Publication Candidate corrente.", null);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new DocxExportResult(false, "Percorso DOCX non valido.", null);

        var metadata = project.EditionMetadata ?? new EditionMetadata();
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Language))
            return new DocxExportResult(false, "Esportazione DOCX bloccata: titolo e lingua sono obbligatori.", null);

        var nodes = project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .ToList();
        if (nodes.Count == 0)
            return new DocxExportResult(false, "Esportazione DOCX bloccata: nessun contenuto editoriale disponibile.", null);

        var fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".docx", StringComparison.OrdinalIgnoreCase))
            fullPath += ".docx";
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypes());
                await WriteEntryAsync(archive, "_rels/.rels", BuildRootRelationships());
                await WriteEntryAsync(archive, "docProps/core.xml", BuildCoreProperties(project, candidate, metadata));
                await WriteEntryAsync(archive, "docProps/app.xml", BuildAppProperties());
                await WriteEntryAsync(archive, "word/document.xml", BuildDocument(metadata, nodes));
                await WriteEntryAsync(archive, "word/styles.xml", BuildStyles(metadata.Language));
                await WriteEntryAsync(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships());
            }

            if (File.Exists(fullPath)) File.Delete(fullPath);
            File.Move(tempPath, fullPath);
            return new DocxExportResult(true, $"DOCX esportato: {Path.GetFileName(fullPath)}", fullPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static string BuildContentTypes()
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/docProps/app.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml"))));
        return Xml(document);
    }

    private static string BuildRootRelationships()
    {
        XNamespace rel = PackageRelNs;
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rel + "Relationships",
                new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", OfficeRelNs + "/officeDocument"), new XAttribute("Target", "word/document.xml")),
                new XElement(rel + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"), new XAttribute("Target", "docProps/core.xml")),
                new XElement(rel + "Relationship", new XAttribute("Id", "rId3"), new XAttribute("Type", OfficeRelNs + "/extended-properties"), new XAttribute("Target", "docProps/app.xml"))));
        return Xml(document);
    }

    private static string BuildDocumentRelationships()
    {
        XNamespace rel = PackageRelNs;
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rel + "Relationships",
                new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", OfficeRelNs + "/styles"), new XAttribute("Target", "styles.xml"))));
        return Xml(document);
    }

    private static string BuildCoreProperties(PreviewProject project, RevisionCandidate candidate, EditionMetadata metadata)
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace dcterms = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var modified = CandidateModifiedUtc(candidate);

        var properties = new XElement(cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "dcterms", dcterms),
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XElement(dc + "title", metadata.Title),
            Optional(dc + "subject", metadata.Subtitle),
            Optional(dc + "creator", metadata.Creator),
            Optional(dc + "description", metadata.Description),
            Optional(dc + "language", metadata.Language),
            new XElement(dc + "identifier", string.IsNullOrWhiteSpace(metadata.Isbn) ? $"urn:uuid:{project.ProjectId:D}" : $"urn:isbn:{metadata.Isbn}"),
            new XElement(cp + "lastModifiedBy", "Diez Publishing Studio"),
            new XElement(dcterms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), modified),
            new XElement(dcterms + "modified", new XAttribute(xsi + "type", "dcterms:W3CDTF"), modified));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), properties));
    }

    private static string BuildAppProperties()
    {
        XNamespace ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ep + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", vt),
                new XElement(ep + "Application", "Diez Publishing Studio"),
                new XElement(ep + "AppVersion", "0.14")));
        return Xml(document);
    }

    private static string BuildDocument(EditionMetadata metadata, IReadOnlyList<ContentNode> nodes)
    {
        XNamespace w = WordMainNs;
        var body = new XElement(w + "body");

        body.Add(Paragraph(metadata.Title, "Title", keepNext: true));
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) body.Add(Paragraph(metadata.Subtitle, "Subtitle", keepNext: true));
        if (!string.IsNullOrWhiteSpace(metadata.Creator)) body.Add(Paragraph(metadata.Creator, "Author", keepNext: false));
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) body.Add(Paragraph(metadata.Publisher, "Metadata", keepNext: false));
        if (!string.IsNullOrWhiteSpace(metadata.Isbn)) body.Add(Paragraph("ISBN " + metadata.Isbn, "Metadata", keepNext: false));
        body.Add(PageBreakParagraph());

        foreach (var node in nodes)
        {
            var title = string.IsNullOrWhiteSpace(node.Title) ? "Sezione" : node.Title.Trim();
            body.Add(Paragraph(title, "Heading1", keepNext: true));
            foreach (var paragraph in SplitParagraphs(node.Body ?? string.Empty))
                body.Add(Paragraph(paragraph, "Normal", keepNext: false));
        }

        body.Add(new XElement(w + "sectPr",
            new XElement(w + "pgSz", new XAttribute(w + "w", "11906"), new XAttribute(w + "h", "16838")),
            new XElement(w + "pgMar", new XAttribute(w + "top", "1440"), new XAttribute(w + "right", "1440"), new XAttribute(w + "bottom", "1440"), new XAttribute(w + "left", "1440"), new XAttribute(w + "header", "720"), new XAttribute(w + "footer", "720"), new XAttribute(w + "gutter", "0"))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(w + "document", body));
        return Xml(document);
    }

    private static XElement Paragraph(string text, string style, bool keepNext)
    {
        XNamespace w = WordMainNs;
        var pPr = new XElement(w + "pPr", new XElement(w + "pStyle", new XAttribute(w + "val", style)));
        if (keepNext) pPr.Add(new XElement(w + "keepNext"));
        var paragraph = new XElement(w + "p", pPr);

        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Add(new XElement(w + "r", new XElement(w + "br")));
            paragraph.Add(new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i])));
        }
        return paragraph;
    }

    private static XElement PageBreakParagraph()
    {
        XNamespace w = WordMainNs;
        return new XElement(w + "p", new XElement(w + "r", new XElement(w + "br", new XAttribute(w + "type", "page"))));
    }

    private static string BuildStyles(string language)
    {
        XNamespace w = WordMainNs;
        var lang = string.IsNullOrWhiteSpace(language) ? "it-IT" : language.Trim();
        var styles = new XElement(w + "styles",
            new XElement(w + "docDefaults",
                new XElement(w + "rPrDefault", new XElement(w + "rPr",
                    new XElement(w + "rFonts", new XAttribute(w + "ascii", "Aptos"), new XAttribute(w + "hAnsi", "Aptos")),
                    new XElement(w + "lang", new XAttribute(w + "val", lang)))),
                new XElement(w + "pPrDefault", new XElement(w + "pPr", new XElement(w + "spacing", new XAttribute(w + "after", "160"), new XAttribute(w + "line", "276"), new XAttribute(w + "lineRule", "auto"))))),
            Style(w, "Normal", "Normale", "22", false),
            Style(w, "Title", "Titolo", "36", true),
            Style(w, "Subtitle", "Sottotitolo", "26", false),
            Style(w, "Author", "Autore", "24", false),
            Style(w, "Metadata", "Metadati", "20", false),
            HeadingStyle(w));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), styles));
    }

    private static XElement Style(XNamespace w, string id, string name, string size, bool bold)
    {
        var rPr = new XElement(w + "rPr", new XElement(w + "sz", new XAttribute(w + "val", size)), new XElement(w + "szCs", new XAttribute(w + "val", size)));
        if (bold) rPr.Add(new XElement(w + "b"));
        return new XElement(w + "style", new XAttribute(w + "type", "paragraph"), new XAttribute(w + "styleId", id),
            new XElement(w + "name", new XAttribute(w + "val", name)), rPr);
    }

    private static XElement HeadingStyle(XNamespace w) =>
        new(w + "style", new XAttribute(w + "type", "paragraph"), new XAttribute(w + "styleId", "Heading1"),
            new XElement(w + "name", new XAttribute(w + "val", "Titolo 1")),
            new XElement(w + "basedOn", new XAttribute(w + "val", "Normal")),
            new XElement(w + "next", new XAttribute(w + "val", "Normal")),
            new XElement(w + "qFormat"),
            new XElement(w + "pPr", new XElement(w + "keepNext"), new XElement(w + "keepLines"), new XElement(w + "spacing", new XAttribute(w + "before", "360"), new XAttribute(w + "after", "160"))),
            new XElement(w + "rPr", new XElement(w + "b"), new XElement(w + "sz", new XAttribute(w + "val", "30")), new XElement(w + "szCs", new XAttribute(w + "val", "30"))));

    private static IEnumerable<string> SplitParagraphs(string body)
    {
        var normalized = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0) yield break;
        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!string.IsNullOrWhiteSpace(paragraph)) yield return paragraph;
    }

    private static XElement? Optional(XName name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value.Trim());

    private static string CandidateModifiedUtc(RevisionCandidate candidate)
    {
        if (DateTimeOffset.TryParse(candidate.CreatedAtLocal, out var parsed))
            return parsed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content ?? string.Empty);
    }

    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);

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
}

internal readonly record struct DocxExportResult(bool Exported, string Message, string? OutputPath);
