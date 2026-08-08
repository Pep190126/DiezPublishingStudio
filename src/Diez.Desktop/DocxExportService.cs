using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class DocxExportService
{
    private const string WordMainNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WordDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string PictureNs = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const long EmuPerTwip = 635L;
    private const long TextWidthEmu = 9026L * EmuPerTwip;
    private const long TextHeightEmu = 13958L * EmuPerTwip;

    public static string SuggestedFileName(PreviewProject project)
    {
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var title = string.IsNullOrWhiteSpace(metadata.Title) ? project.Name : metadata.Title;
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 1 : parsed;
        return $"{SanitizeFileName(title)}-publication-{sequence:D3}.docx";
    }

    public static Task<DocxExportResult> ExportAsync(PreviewProject project, string outputPath) =>
        ExportAsync(project, string.Empty, outputPath);

    public static async Task<DocxExportResult> ExportAsync(PreviewProject project, string projectPath, string outputPath)
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

        var illustrationErrors = IllustrationPlanService.Validate(project);
        if (illustrationErrors.Count > 0)
            return new DocxExportResult(false, $"Esportazione DOCX bloccata dal piano illustrazioni: {string.Join("; ", illustrationErrors.Take(3))}", null);

        var imagePreparation = await PrepareImagePartsAsync(project, projectPath);
        if (!imagePreparation.Success)
            return new DocxExportResult(false, imagePreparation.Message, null);
        var images = imagePreparation.Images;

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
                await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypes(images));
                await WriteEntryAsync(archive, "_rels/.rels", BuildRootRelationships());
                await WriteEntryAsync(archive, "docProps/core.xml", BuildCoreProperties(project, candidate, metadata));
                await WriteEntryAsync(archive, "docProps/app.xml", BuildAppProperties());
                await WriteEntryAsync(archive, "word/document.xml", BuildDocument(project, metadata, nodes, images));
                await WriteEntryAsync(archive, "word/styles.xml", BuildStyles(metadata.Language));
                await WriteEntryAsync(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships(images));
                foreach (var image in images)
                    await WriteBinaryEntryAsync(archive, image.EntryPath, image.Bytes);
            }

            if (File.Exists(fullPath)) File.Delete(fullPath);
            File.Move(tempPath, fullPath);
            var imageText = images.Count == 0 ? string.Empty : $" · {project.IllustrationPlacements.Count} collocazioni / {images.Count} originali immagine incorporati";
            return new DocxExportResult(true, $"DOCX esportato: {Path.GetFileName(fullPath)}{imageText}", fullPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static async Task<ImagePreparation> PrepareImagePartsAsync(PreviewProject project, string projectPath)
    {
        if (project.IllustrationPlacements.Count == 0)
            return new ImagePreparation(true, string.Empty, []);
        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
            return new ImagePreparation(false, "Esportazione DOCX illustrato bloccata: salva prima il progetto .diez per rendere disponibili gli originali incorporati.", []);

        var orderedMaterialIds = project.IllustrationPlacements
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.PlacementId)
            .Select(p => p.MaterialId)
            .Distinct()
            .ToList();
        var images = new List<DocxImagePart>();
        for (var i = 0; i < orderedMaterialIds.Count; i++)
        {
            var material = project.Materials.First(m => m.MaterialId == orderedMaterialIds[i]);
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0)
                return new ImagePreparation(false, $"Impossibile leggere l'originale incorporato: {material.FileName}.", []);
            if (!TryReadImageDimensions(bytes, Path.GetExtension(material.FileName), out var width, out var height))
                return new ImagePreparation(false, $"Impossibile determinare le dimensioni di {material.FileName}; sostituisci l'immagine con un PNG/JPEG/GIF/BMP valido.", []);

            var extension = Path.GetExtension(material.FileName).ToLowerInvariant();
            var relationshipId = $"rId{i + 2}";
            var entryPath = $"word/media/image-{i + 1:D3}{extension}";
            images.Add(new DocxImagePart(material.MaterialId, material.FileName, relationshipId, entryPath, extension, ImageContentType(extension), bytes, width, height));
        }
        return new ImagePreparation(true, string.Empty, images);
    }

    private static string BuildContentTypes(IReadOnlyList<DocxImagePart> images)
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        var types = new XElement(ct + "Types",
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));

        foreach (var image in images.GroupBy(i => i.Extension, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            types.Add(new XElement(ct + "Default", new XAttribute("Extension", image.Extension.TrimStart('.')), new XAttribute("ContentType", image.ContentType)));

        types.Add(
            new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/docProps/app.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml")));

        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), types));
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

    private static string BuildDocumentRelationships(IReadOnlyList<DocxImagePart> images)
    {
        XNamespace rel = PackageRelNs;
        var relationships = new XElement(rel + "Relationships",
            new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", OfficeRelNs + "/styles"), new XAttribute("Target", "styles.xml")));
        foreach (var image in images)
            relationships.Add(new XElement(rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", OfficeRelNs + "/image"),
                new XAttribute("Target", image.EntryPath["word/".Length..])));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), relationships));
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

    private static string BuildDocument(PreviewProject project, EditionMetadata metadata, IReadOnlyList<ContentNode> nodes, IReadOnlyList<DocxImagePart> images)
    {
        XNamespace w = WordMainNs;
        XNamespace wp = WordDrawingNs;
        XNamespace a = DrawingNs;
        XNamespace pic = PictureNs;
        XNamespace r = OfficeRelNs;
        var imageByMaterial = images.ToDictionary(i => i.MaterialId);
        var body = new XElement(w + "body");
        var drawingId = 1;

        body.Add(Paragraph(metadata.Title, "Title", keepNext: true));
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) body.Add(Paragraph(metadata.Subtitle, "Subtitle", keepNext: true));
        if (!string.IsNullOrWhiteSpace(metadata.Creator)) body.Add(Paragraph(metadata.Creator, "Author", keepNext: false));
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) body.Add(Paragraph(metadata.Publisher, "Metadata", keepNext: false));
        if (!string.IsNullOrWhiteSpace(metadata.Isbn)) body.Add(Paragraph("ISBN " + metadata.Isbn, "Metadata", keepNext: false));
        body.Add(PageBreakParagraph());

        foreach (var node in nodes)
        {
            var placements = IllustrationPlanService.OrderedForContent(project, node.ContentId);
            AddPlacements(body, placements.Where(p => p.Position == IllustrationPlanService.BeforeHeading), imageByMaterial, ref drawingId);

            var title = string.IsNullOrWhiteSpace(node.Title) ? "Sezione" : node.Title.Trim();
            body.Add(Paragraph(title, "Heading1", keepNext: true));

            AddPlacements(body, placements.Where(p => p.Position == IllustrationPlanService.AfterHeading), imageByMaterial, ref drawingId);

            foreach (var paragraph in SplitParagraphs(node.Body ?? string.Empty))
                body.Add(Paragraph(paragraph, "Normal", keepNext: false));

            AddPlacements(body, placements.Where(p => p.Position == IllustrationPlanService.AfterContent), imageByMaterial, ref drawingId);
            foreach (var placement in placements.Where(p => p.Position == IllustrationPlanService.FullPageAfter))
            {
                body.Add(PageBreakParagraph());
                AddPlacement(body, placement, imageByMaterial, ref drawingId);
                body.Add(PageBreakParagraph());
            }
        }

        body.Add(new XElement(w + "sectPr",
            new XElement(w + "pgSz", new XAttribute(w + "w", "11906"), new XAttribute(w + "h", "16838")),
            new XElement(w + "pgMar", new XAttribute(w + "top", "1440"), new XAttribute(w + "right", "1440"), new XAttribute(w + "bottom", "1440"), new XAttribute(w + "left", "1440"), new XAttribute(w + "header", "720"), new XAttribute(w + "footer", "720"), new XAttribute(w + "gutter", "0"))));

        var root = new XElement(w + "document",
            new XAttribute(XNamespace.Xmlns + "wp", wp),
            new XAttribute(XNamespace.Xmlns + "a", a),
            new XAttribute(XNamespace.Xmlns + "pic", pic),
            new XAttribute(XNamespace.Xmlns + "r", r),
            body);
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static void AddPlacements(XElement body, IEnumerable<IllustrationPlacement> placements, IReadOnlyDictionary<Guid, DocxImagePart> images, ref int drawingId)
    {
        foreach (var placement in placements)
            AddPlacement(body, placement, images, ref drawingId);
    }

    private static void AddPlacement(XElement body, IllustrationPlacement placement, IReadOnlyDictionary<Guid, DocxImagePart> images, ref int drawingId)
    {
        if (!images.TryGetValue(placement.MaterialId, out var image)) return;
        body.Add(ImageParagraph(placement, image, drawingId++));
        if (!string.IsNullOrWhiteSpace(placement.Caption))
            body.Add(CaptionParagraph(placement.Caption));
    }

    private static XElement ImageParagraph(IllustrationPlacement placement, DocxImagePart image, int drawingId)
    {
        XNamespace w = WordMainNs;
        XNamespace wp = WordDrawingNs;
        XNamespace a = DrawingNs;
        XNamespace pic = PictureNs;
        XNamespace r = OfficeRelNs;
        var (cx, cy) = DrawingSize(image.WidthPx, image.HeightPx, placement.WidthPercent);

        var drawing = new XElement(w + "drawing",
            new XElement(wp + "inline",
                new XAttribute("distT", "0"), new XAttribute("distB", "0"), new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                new XElement(wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(wp + "docPr", new XAttribute("id", drawingId), new XAttribute("name", $"Illustrazione {drawingId}"), new XAttribute("descr", image.FileName)),
                new XElement(wp + "cNvGraphicFramePr", new XElement(a + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                new XElement(a + "graphic",
                    new XElement(a + "graphicData", new XAttribute("uri", PictureNs),
                        new XElement(pic + "pic",
                            new XElement(pic + "nvPicPr",
                                new XElement(pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", image.FileName)),
                                new XElement(pic + "cNvPicPr", new XElement(a + "picLocks", new XAttribute("noChangeAspect", "1")))),
                            new XElement(pic + "blipFill",
                                new XElement(a + "blip", new XAttribute(r + "embed", image.RelationshipId)),
                                new XElement(a + "stretch", new XElement(a + "fillRect"))),
                            new XElement(pic + "spPr",
                                new XElement(a + "xfrm",
                                    new XElement(a + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                    new XElement(a + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                new XElement(a + "prstGeom", new XAttribute("prst", "rect"), new XElement(a + "avLst")))))))));

        return new XElement(w + "p",
            new XElement(w + "pPr", new XElement(w + "jc", new XAttribute(w + "val", "center"))),
            new XElement(w + "r", drawing));
    }

    private static XElement CaptionParagraph(string caption)
    {
        XNamespace w = WordMainNs;
        return new XElement(w + "p",
            new XElement(w + "pPr",
                new XElement(w + "pStyle", new XAttribute(w + "val", "Caption")),
                new XElement(w + "jc", new XAttribute(w + "val", "center"))),
            new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), caption.Trim())));
    }

    private static (long Cx, long Cy) DrawingSize(int widthPx, int heightPx, int widthPercent)
    {
        var requestedWidth = TextWidthEmu * Math.Clamp(widthPercent, 25, 100) / 100L;
        var requestedHeight = Math.Max(1L, requestedWidth * heightPx / Math.Max(1, widthPx));
        if (requestedHeight <= TextHeightEmu) return (requestedWidth, requestedHeight);
        var scaledWidth = Math.Max(1L, requestedWidth * TextHeightEmu / requestedHeight);
        return (scaledWidth, TextHeightEmu);
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
            CaptionStyle(w),
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

    private static XElement CaptionStyle(XNamespace w) =>
        new(w + "style", new XAttribute(w + "type", "paragraph"), new XAttribute(w + "styleId", "Caption"),
            new XElement(w + "name", new XAttribute(w + "val", "Didascalia")),
            new XElement(w + "basedOn", new XAttribute(w + "val", "Normal")),
            new XElement(w + "rPr", new XElement(w + "i"), new XElement(w + "sz", new XAttribute(w + "val", "18")), new XElement(w + "szCs", new XAttribute(w + "val", "18"))));

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

    private static async Task WriteBinaryEntryAsync(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
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

    private static string ImageContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static bool TryReadImageDimensions(byte[] bytes, string extension, out int width, out int height)
    {
        width = 0;
        height = 0;
        extension = extension.ToLowerInvariant();
        if (extension == ".png" && bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            return width > 0 && height > 0;
        }
        if (extension == ".gif" && bytes.Length >= 10)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
            return width > 0 && height > 0;
        }
        if (extension == ".bmp" && bytes.Length >= 26)
        {
            width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)));
            return width > 0 && height > 0;
        }
        if (extension is ".jpg" or ".jpeg")
            return TryReadJpegDimensions(bytes, out width, out height);
        return false;
    }

    private static bool TryReadJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return false;
        var index = 2;
        while (index + 8 < bytes.Length)
        {
            if (bytes[index] != 0xFF) { index++; continue; }
            while (index < bytes.Length && bytes[index] == 0xFF) index++;
            if (index >= bytes.Length) break;
            var marker = bytes[index++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7) continue;
            if (index + 1 >= bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index, 2));
            if (length < 2 || index + length > bytes.Length) break;
            if (IsJpegStartOfFrame(marker) && length >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 5, 2));
                return width > 0 && height > 0;
            }
            index += length;
        }
        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private sealed record DocxImagePart(Guid MaterialId, string FileName, string RelationshipId, string EntryPath, string Extension, string ContentType, byte[] Bytes, int WidthPx, int HeightPx);
    private sealed record ImagePreparation(bool Success, string Message, IReadOnlyList<DocxImagePart> Images);
}

internal readonly record struct DocxExportResult(bool Exported, string Message, string? OutputPath);