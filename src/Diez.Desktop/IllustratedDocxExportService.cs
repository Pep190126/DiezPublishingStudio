using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class IllustratedDocxExportService
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

    public static async Task<DocxExportResult> ExportAsync(PreviewProject project, string projectPath, string outputPath)
    {
        var baseResult = await DocxExportService.ExportAsync(project, outputPath);
        if (!baseResult.Exported || string.IsNullOrWhiteSpace(baseResult.OutputPath)) return baseResult;
        if (project.IllustrationPlacements.Count == 0) return baseResult;

        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
        {
            TryDelete(baseResult.OutputPath);
            return new DocxExportResult(false, "Esportazione DOCX illustrato bloccata: salva prima il progetto .diez per rendere disponibili gli originali incorporati.", null);
        }

        var errors = IllustrationPlanService.Validate(project);
        if (errors.Count > 0)
        {
            TryDelete(baseResult.OutputPath);
            return new DocxExportResult(false, $"Esportazione DOCX bloccata dal piano illustrazioni: {string.Join("; ", errors.Take(3))}", null);
        }

        var prepared = await PrepareImagesAsync(project, projectPath);
        if (!prepared.Success)
        {
            TryDelete(baseResult.OutputPath);
            return new DocxExportResult(false, prepared.Message, null);
        }

        try
        {
            using var archive = ZipFile.Open(baseResult.OutputPath, ZipArchiveMode.Update);
            UpdateContentTypes(archive, prepared.Images);
            UpdateDocumentRelationships(archive, prepared.Images);
            UpdateStyles(archive);
            await ReplaceTextEntryAsync(archive, "word/document.xml", BuildDocument(project, prepared.Images));

            foreach (var image in prepared.Images)
            {
                var existing = archive.GetEntry(image.EntryPath);
                existing?.Delete();
                var entry = archive.CreateEntry(image.EntryPath, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(image.Bytes);
            }
        }
        catch
        {
            TryDelete(baseResult.OutputPath);
            throw;
        }

        return new DocxExportResult(
            true,
            $"DOCX illustrato esportato: {Path.GetFileName(baseResult.OutputPath)} · {project.IllustrationPlacements.Count} collocazioni / {prepared.Images.Count} originali incorporati",
            baseResult.OutputPath);
    }

    private static async Task<ImagePreparation> PrepareImagesAsync(PreviewProject project, string projectPath)
    {
        var ids = project.IllustrationPlacements
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.PlacementId)
            .Select(p => p.MaterialId)
            .Distinct()
            .ToList();
        var images = new List<DocxImagePart>();

        for (var i = 0; i < ids.Count; i++)
        {
            var material = project.Materials.First(m => m.MaterialId == ids[i]);
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0)
                return new ImagePreparation(false, $"Impossibile leggere l'originale incorporato: {material.FileName}.", []);

            var extension = Path.GetExtension(material.FileName).ToLowerInvariant();
            if (!TryReadImageDimensions(bytes, extension, out var width, out var height))
                return new ImagePreparation(false, $"Impossibile determinare le dimensioni di {material.FileName}; usa un PNG/JPEG/GIF/BMP valido.", []);

            images.Add(new DocxImagePart(
                material.MaterialId,
                material.FileName,
                $"rId{i + 2}",
                $"word/media/image-{i + 1:D3}{extension}",
                extension,
                ContentType(extension),
                bytes,
                width,
                height));
        }

        return new ImagePreparation(true, string.Empty, images);
    }

    private static void UpdateContentTypes(ZipArchive archive, IReadOnlyList<DocxImagePart> images)
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        var document = ReadXml(archive, "[Content_Types].xml");
        var root = document.Root ?? throw new InvalidDataException("DOCX: [Content_Types].xml non valido.");

        foreach (var image in images.GroupBy(i => i.Extension, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var extension = image.Extension.TrimStart('.');
            var exists = root.Elements(ct + "Default").Any(e =>
                string.Equals((string?)e.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                root.Add(new XElement(ct + "Default",
                    new XAttribute("Extension", extension),
                    new XAttribute("ContentType", image.ContentType)));
        }

        ReplaceXmlEntry(archive, "[Content_Types].xml", document);
    }

    private static void UpdateDocumentRelationships(ZipArchive archive, IReadOnlyList<DocxImagePart> images)
    {
        XNamespace rel = PackageRelNs;
        var path = "word/_rels/document.xml.rels";
        var document = ReadXml(archive, path);
        var root = document.Root ?? throw new InvalidDataException("DOCX: relazioni documento non valide.");

        foreach (var oldImage in root.Elements(rel + "Relationship")
                     .Where(e => ((string?)e.Attribute("Type"))?.EndsWith("/image", StringComparison.Ordinal) == true)
                     .ToList())
            oldImage.Remove();

        foreach (var image in images)
            root.Add(new XElement(rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", OfficeRelNs + "/image"),
                new XAttribute("Target", image.EntryPath["word/".Length..])));

        ReplaceXmlEntry(archive, path, document);
    }

    private static void UpdateStyles(ZipArchive archive)
    {
        XNamespace w = WordMainNs;
        var path = "word/styles.xml";
        var document = ReadXml(archive, path);
        var root = document.Root ?? throw new InvalidDataException("DOCX: styles.xml non valido.");
        var exists = root.Elements(w + "style").Any(e => (string?)e.Attribute(w + "styleId") == "Caption");
        if (!exists)
        {
            var style = new XElement(w + "style",
                new XAttribute(w + "type", "paragraph"),
                new XAttribute(w + "styleId", "Caption"));
            style.Add(new XElement(w + "name", new XAttribute(w + "val", "Didascalia")));
            style.Add(new XElement(w + "basedOn", new XAttribute(w + "val", "Normal")));
            var rPr = new XElement(w + "rPr");
            rPr.Add(new XElement(w + "i"));
            rPr.Add(new XElement(w + "sz", new XAttribute(w + "val", "18")));
            rPr.Add(new XElement(w + "szCs", new XAttribute(w + "val", "18")));
            style.Add(rPr);
            root.Add(style);
        }
        ReplaceXmlEntry(archive, path, document);
    }

    private static string BuildDocument(PreviewProject project, IReadOnlyList<DocxImagePart> images)
    {
        XNamespace w = WordMainNs;
        XNamespace wp = WordDrawingNs;
        XNamespace a = DrawingNs;
        XNamespace pic = PictureNs;
        XNamespace r = OfficeRelNs;
        var metadata = project.EditionMetadata ?? new EditionMetadata();
        var imageMap = images.ToDictionary(i => i.MaterialId);
        var body = new XElement(w + "body");
        var drawingId = 1;

        body.Add(TextParagraph(metadata.Title, "Title", true));
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) body.Add(TextParagraph(metadata.Subtitle, "Subtitle", true));
        if (!string.IsNullOrWhiteSpace(metadata.Creator)) body.Add(TextParagraph(metadata.Creator, "Author", false));
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) body.Add(TextParagraph(metadata.Publisher, "Metadata", false));
        if (!string.IsNullOrWhiteSpace(metadata.Isbn)) body.Add(TextParagraph("ISBN " + metadata.Isbn, "Metadata", false));
        body.Add(PageBreak());

        var nodes = project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .ToList();

        foreach (var node in nodes)
        {
            var placements = IllustrationPlanService.OrderedForContent(project, node.ContentId);
            AddPlacements(body, placements, IllustrationPlanService.BeforeHeading, imageMap, ref drawingId);

            body.Add(TextParagraph(string.IsNullOrWhiteSpace(node.Title) ? "Sezione" : node.Title.Trim(), "Heading1", true));
            AddPlacements(body, placements, IllustrationPlanService.AfterHeading, imageMap, ref drawingId);

            foreach (var paragraph in SplitParagraphs(node.Body ?? string.Empty))
                body.Add(TextParagraph(paragraph, "Normal", false));

            AddPlacements(body, placements, IllustrationPlanService.AfterContent, imageMap, ref drawingId);
            foreach (var placement in placements.Where(p => p.Position == IllustrationPlanService.FullPageAfter))
            {
                body.Add(PageBreak());
                AddPlacement(body, placement, imageMap, ref drawingId);
                body.Add(PageBreak());
            }
        }

        var section = new XElement(w + "sectPr");
        section.Add(new XElement(w + "pgSz", new XAttribute(w + "w", "11906"), new XAttribute(w + "h", "16838")));
        section.Add(new XElement(w + "pgMar",
            new XAttribute(w + "top", "1440"),
            new XAttribute(w + "right", "1440"),
            new XAttribute(w + "bottom", "1440"),
            new XAttribute(w + "left", "1440"),
            new XAttribute(w + "header", "720"),
            new XAttribute(w + "footer", "720"),
            new XAttribute(w + "gutter", "0")));
        body.Add(section);

        var root = new XElement(w + "document");
        root.Add(new XAttribute(XNamespace.Xmlns + "wp", wp));
        root.Add(new XAttribute(XNamespace.Xmlns + "a", a));
        root.Add(new XAttribute(XNamespace.Xmlns + "pic", pic));
        root.Add(new XAttribute(XNamespace.Xmlns + "r", r));
        root.Add(body);
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private static void AddPlacements(
        XElement body,
        IEnumerable<IllustrationPlacement> placements,
        string position,
        IReadOnlyDictionary<Guid, DocxImagePart> images,
        ref int drawingId)
    {
        foreach (var placement in placements.Where(p => p.Position == position))
            AddPlacement(body, placement, images, ref drawingId);
    }

    private static void AddPlacement(
        XElement body,
        IllustrationPlacement placement,
        IReadOnlyDictionary<Guid, DocxImagePart> images,
        ref int drawingId)
    {
        if (!images.TryGetValue(placement.MaterialId, out var image)) return;
        body.Add(ImageParagraph(placement, image, drawingId++));
        if (!string.IsNullOrWhiteSpace(placement.Caption)) body.Add(CaptionParagraph(placement.Caption));
    }

    private static XElement ImageParagraph(IllustrationPlacement placement, DocxImagePart image, int drawingId)
    {
        XNamespace w = WordMainNs;
        XNamespace wp = WordDrawingNs;
        XNamespace a = DrawingNs;
        XNamespace pic = PictureNs;
        XNamespace r = OfficeRelNs;
        var size = DrawingSize(image.WidthPx, image.HeightPx, placement.WidthPercent);

        var nonVisualPicture = new XElement(pic + "nvPicPr");
        nonVisualPicture.Add(new XElement(pic + "cNvPr",
            new XAttribute("id", "0"),
            new XAttribute("name", image.FileName)));
        nonVisualPicture.Add(new XElement(pic + "cNvPicPr",
            new XElement(a + "picLocks", new XAttribute("noChangeAspect", "1"))));

        var blipFill = new XElement(pic + "blipFill");
        blipFill.Add(new XElement(a + "blip", new XAttribute(r + "embed", image.RelationshipId)));
        blipFill.Add(new XElement(a + "stretch", new XElement(a + "fillRect")));

        var transform = new XElement(a + "xfrm");
        transform.Add(new XElement(a + "off", new XAttribute("x", "0"), new XAttribute("y", "0")));
        transform.Add(new XElement(a + "ext", new XAttribute("cx", size.Cx), new XAttribute("cy", size.Cy)));
        var shapeProperties = new XElement(pic + "spPr");
        shapeProperties.Add(transform);
        shapeProperties.Add(new XElement(a + "prstGeom",
            new XAttribute("prst", "rect"),
            new XElement(a + "avLst")));

        var picture = new XElement(pic + "pic");
        picture.Add(nonVisualPicture);
        picture.Add(blipFill);
        picture.Add(shapeProperties);

        var graphicData = new XElement(a + "graphicData", new XAttribute("uri", PictureNs));
        graphicData.Add(picture);
        var graphic = new XElement(a + "graphic");
        graphic.Add(graphicData);

        var inline = new XElement(wp + "inline",
            new XAttribute("distT", "0"),
            new XAttribute("distB", "0"),
            new XAttribute("distL", "0"),
            new XAttribute("distR", "0"));
        inline.Add(new XElement(wp + "extent", new XAttribute("cx", size.Cx), new XAttribute("cy", size.Cy)));
        inline.Add(new XElement(wp + "docPr",
            new XAttribute("id", drawingId),
            new XAttribute("name", $"Illustrazione {drawingId}"),
            new XAttribute("descr", image.FileName)));
        inline.Add(new XElement(wp + "cNvGraphicFramePr",
            new XElement(a + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))));
        inline.Add(graphic);

        var drawing = new XElement(w + "drawing");
        drawing.Add(inline);
        var run = new XElement(w + "r");
        run.Add(drawing);
        var paragraph = new XElement(w + "p");
        paragraph.Add(new XElement(w + "pPr", new XElement(w + "jc", new XAttribute(w + "val", "center"))));
        paragraph.Add(run);
        return paragraph;
    }

    private static XElement CaptionParagraph(string caption)
    {
        XNamespace w = WordMainNs;
        var properties = new XElement(w + "pPr");
        properties.Add(new XElement(w + "pStyle", new XAttribute(w + "val", "Caption")));
        properties.Add(new XElement(w + "jc", new XAttribute(w + "val", "center")));
        var text = new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), caption.Trim());
        return new XElement(w + "p", properties, new XElement(w + "r", text));
    }

    private static XElement TextParagraph(string text, string style, bool keepNext)
    {
        XNamespace w = WordMainNs;
        var pPr = new XElement(w + "pPr", new XElement(w + "pStyle", new XAttribute(w + "val", style)));
        if (keepNext) pPr.Add(new XElement(w + "keepNext"));
        var paragraph = new XElement(w + "p", pPr);
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Add(new XElement(w + "r", new XElement(w + "br")));
            paragraph.Add(new XElement(w + "r",
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i])));
        }
        return paragraph;
    }

    private static XElement PageBreak()
    {
        XNamespace w = WordMainNs;
        return new XElement(w + "p", new XElement(w + "r",
            new XElement(w + "br", new XAttribute(w + "type", "page"))));
    }

    private static (long Cx, long Cy) DrawingSize(int widthPx, int heightPx, int widthPercent)
    {
        var width = TextWidthEmu * Math.Clamp(widthPercent, 25, 100) / 100L;
        var height = Math.Max(1L, width * heightPx / Math.Max(1, widthPx));
        if (height <= TextHeightEmu) return (width, height);
        return (Math.Max(1L, width * TextHeightEmu / height), TextHeightEmu);
    }

    private static IEnumerable<string> SplitParagraphs(string body)
    {
        var normalized = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length == 0) yield break;
        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!string.IsNullOrWhiteSpace(paragraph)) yield return paragraph;
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"DOCX: parte mancante: {path}.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXmlEntry(ZipArchive archive, string path, XDocument document)
    {
        var old = archive.GetEntry(path);
        old?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(document.ToString(SaveOptions.DisableFormatting));
    }

    private static async Task ReplaceTextEntryAsync(ZipArchive archive, string path, string content)
    {
        var old = archive.GetEntry(path);
        old?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static int MaterialOrder(PreviewProject project, Guid materialId)
    {
        var index = project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string ContentType(string extension) => extension switch
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
        if (extension == ".png" && bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
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
        if (extension is ".jpg" or ".jpeg") return TryReadJpegDimensions(bytes, out width, out height);
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
            if (IsStartOfFrame(marker) && length >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 5, 2));
                return width > 0 && height > 0;
            }
            index += length;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record DocxImagePart(
        Guid MaterialId,
        string FileName,
        string RelationshipId,
        string EntryPath,
        string Extension,
        string ContentType,
        byte[] Bytes,
        int WidthPx,
        int HeightPx);

    private sealed record ImagePreparation(bool Success, string Message, IReadOnlyList<DocxImagePart> Images);
}
