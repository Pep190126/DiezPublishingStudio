using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal readonly record struct ImageLayoutExportResult(bool Success, string Message, string? OutputPath);

internal static class ImageCollectionLayoutExportService
{
    public const string External = "Impaginazione esterna";
    public const string Internal = "Impaginazione interna";
    public const string Both = "Entrambi";

    private static readonly Regex CodeRegex = new(@"IMG-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> DocxImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp"
    };

    public static string SuggestedName(PreviewProject project, string mode)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "raccolta-immagini").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "raccolta-immagini";
        return string.Equals(mode, Internal, StringComparison.Ordinal)
            ? safe + "-impaginazione-interna.docx"
            : string.Equals(mode, Both, StringComparison.Ordinal)
                ? safe + "-impaginazione-entrambi.zip"
                : safe + "-impaginazione-esterna.zip";
    }

    public static async Task<ImageLayoutExportResult> ExportAsync(
        PreviewProject project,
        string projectPath,
        string outputPath,
        string mode,
        bool includeDescriptions)
    {
        if (string.Equals(mode, External, StringComparison.Ordinal))
        {
            var result = await ImageCollectionDescriptionService.ExportApprovedCollectionAsync(
                project, projectPath, outputPath, includeDescriptions);
            var path = EnsureExtension(outputPath, ".zip");
            return new(result.Success, result.Message, result.Success ? path : null);
        }

        if (string.Equals(mode, Internal, StringComparison.Ordinal))
            return await ExportInternalDocxAsync(project, projectPath, outputPath);

        if (!string.Equals(mode, Both, StringComparison.Ordinal))
            return new(false, "Scelta di impaginazione non riconosciuta.", null);

        var finalPath = EnsureExtension(outputPath, ".zip");
        EnsureDirectory(finalPath);
        var tempRoot = Path.Combine(Path.GetTempPath(), "DiezImageLayout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var externalZip = Path.Combine(tempRoot, "esterna.zip");
            var internalDocx = Path.Combine(tempRoot, "impaginazione-interna.docx");
            var externalResult = await ImageCollectionDescriptionService.ExportApprovedCollectionAsync(
                project, projectPath, externalZip, includeDescriptions);
            if (!externalResult.Success)
                return new(false, externalResult.Message, null);

            var internalResult = await ExportInternalDocxAsync(project, projectPath, internalDocx);
            if (!internalResult.Success)
                return internalResult;

            var tempFinal = finalPath + ".tmp";
            if (File.Exists(tempFinal)) File.Delete(tempFinal);
            await using (var output = new FileStream(tempFinal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var docxEntry = archive.CreateEntry("impaginazione-interna.docx", CompressionLevel.Optimal);
                await using (var target = docxEntry.Open())
                await using (var source = File.OpenRead(internalDocx))
                    await source.CopyToAsync(target);

                using var sourceZip = ZipFile.OpenRead(externalZip);
                foreach (var entry in sourceZip.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                    var targetEntry = archive.CreateEntry(entry.Name, CompressionLevel.Optimal);
                    await using var source = entry.Open();
                    await using var target = targetEntry.Open();
                    await source.CopyToAsync(target);
                }
            }
            File.Move(tempFinal, finalPath, true);
            return new(true,
                $"Creati entrambi: DOCX per impaginazione interna + {externalResult.Images} immagini originali" +
                (includeDescriptions ? $" + {externalResult.Descriptions} descrizioni abbinate" : string.Empty) + ".",
                finalPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static async Task<ImageLayoutExportResult> ExportInternalDocxAsync(
        PreviewProject project,
        string projectPath,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
            return new(false, "Salva prima il progetto .diez per creare l'impaginazione interna.", null);

        var approved = project.AiProductionJobs
            .Where(j => string.Equals(j.OutputType, AiProductionService.TypeImage, StringComparison.OrdinalIgnoreCase))
            .Where(j => string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal))
            .Where(j => j.ResultMaterialId.HasValue)
            .OrderBy(j => CodeNumber(j.Code))
            .ThenBy(j => j.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (approved.Count == 0)
            return new(false, "Non ci sono immagini approvate da inserire nel DOCX.", null);

        var images = new List<DocxImage>();
        var skipped = 0;
        foreach (var job in approved)
        {
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == job.ResultMaterialId!.Value);
            if (material is null) continue;
            var extension = Path.GetExtension(material.FileName).ToLowerInvariant();
            if (!DocxImageExtensions.Contains(extension))
            {
                skipped++;
                continue;
            }
            var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
            if (bytes is null || bytes.Length == 0) continue;
            var (width, height) = ReadDimensions(bytes, extension);
            images.Add(new DocxImage(StableBaseName(job.Code), extension, bytes, width, height));
        }
        if (images.Count == 0)
            return new(false, "Le immagini approvate non sono in un formato inseribile nel DOCX. Gli originali restano comunque esportabili con Impaginazione esterna.", null);

        var fullPath = EnsureExtension(outputPath, ".docx");
        EnsureDirectory(fullPath);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteTextAsync(archive, "[Content_Types].xml", ContentTypes(images));
            await WriteTextAsync(archive, "_rels/.rels", RootRelationships());
            await WriteTextAsync(archive, "word/document.xml", Document(images));
            await WriteTextAsync(archive, "word/_rels/document.xml.rels", DocumentRelationships(images));
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                var entry = archive.CreateEntry($"word/media/{image.BaseName}{image.Extension}", CompressionLevel.Optimal);
                await using var target = entry.Open();
                await target.WriteAsync(image.Bytes);
            }
        }
        File.Move(temp, fullPath, true);
        var message = $"DOCX per impaginazione interna creato con {images.Count} immagini, una per pagina e proporzioni conservate.";
        if (skipped > 0) message += $" {skipped} immagini non compatibili con Word non sono state inserite; restano disponibili come originali.";
        return new(true, message, fullPath);
    }

    private static string Document(IReadOnlyList<DocxImage> images)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        var body = new XElement(w + "body");

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var (cx, cy) = Fit(image.Width, image.Height);

            var picture = new XElement(pic + "pic",
                new XElement(pic + "nvPicPr",
                    new XElement(pic + "cNvPr",
                        new XAttribute("id", "0"),
                        new XAttribute("name", image.BaseName + image.Extension)),
                    new XElement(pic + "cNvPicPr")),
                new XElement(pic + "blipFill",
                    new XElement(a + "blip", new XAttribute(r + "embed", $"rId{i + 1}")),
                    new XElement(a + "stretch", new XElement(a + "fillRect"))),
                new XElement(pic + "spPr",
                    new XElement(a + "xfrm",
                        new XElement(a + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(a + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                    new XElement(a + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(a + "avLst"))));

            var graphic = new XElement(a + "graphic",
                new XElement(a + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                    picture));

            var inline = new XElement(wp + "inline",
                new XAttribute("distT", "0"),
                new XAttribute("distB", "0"),
                new XAttribute("distL", "0"),
                new XAttribute("distR", "0"),
                new XElement(wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(wp + "docPr", new XAttribute("id", i + 1), new XAttribute("name", image.BaseName)),
                graphic);

            body.Add(new XElement(w + "p",
                new XElement(w + "pPr", new XElement(w + "jc", new XAttribute(w + "val", "center"))),
                new XElement(w + "r", new XElement(w + "drawing", inline))));

            if (i < images.Count - 1)
                body.Add(new XElement(w + "p",
                    new XElement(w + "r",
                        new XElement(w + "br", new XAttribute(w + "type", "page")))));
        }

        body.Add(new XElement(w + "sectPr",
            new XElement(w + "pgSz", new XAttribute(w + "w", "12240"), new XAttribute(w + "h", "15840")),
            new XElement(w + "pgMar",
                new XAttribute(w + "top", "360"),
                new XAttribute(w + "right", "360"),
                new XAttribute(w + "bottom", "360"),
                new XAttribute(w + "left", "360"),
                new XAttribute(w + "header", "0"),
                new XAttribute(w + "footer", "0"),
                new XAttribute(w + "gutter", "0"))));

        var root = new XElement(w + "document",
            new XAttribute(XNamespace.Xmlns + "w", w),
            new XAttribute(XNamespace.Xmlns + "r", r),
            new XAttribute(XNamespace.Xmlns + "wp", wp),
            new XAttribute(XNamespace.Xmlns + "a", a),
            new XAttribute(XNamespace.Xmlns + "pic", pic),
            body);
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string ContentTypes(IReadOnlyList<DocxImage> images)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
        foreach (var ext in images.Select(i => i.Extension.TrimStart('.').ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase))
            root.Add(new XElement(x + "Default", new XAttribute("Extension", ext), new XAttribute("ContentType", Mime(ext))));
        root.Add(new XElement(x + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string RootRelationships()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "Relationships",
                new XElement(x + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml")))));
    }

    private static string DocumentRelationships(IReadOnlyList<DocxImage> images)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(x + "Relationships");
        for (var i = 0; i < images.Count; i++)
            root.Add(new XElement(x + "Relationship",
                new XAttribute("Id", $"rId{i + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", $"media/{images[i].BaseName}{images[i].Extension}")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static (long Cx, long Cy) Fit(int width, int height)
    {
        const long maxCx = 7315200;
        const long maxCy = 9601200;
        if (width <= 0 || height <= 0) return (maxCx, maxCy);
        var scale = Math.Min((double)maxCx / width, (double)maxCy / height);
        return ((long)Math.Round(width * scale), (long)Math.Round(height * scale));
    }

    private static (int Width, int Height) ReadDimensions(byte[] bytes, string extension)
    {
        try
        {
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 24)
                return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 10)
                return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
            if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 26)
                return (Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4))), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4))));
            if (extension is ".jpg" or ".jpeg")
                return ReadJpegDimensions(bytes);
        }
        catch { }
        return (0, 0);
    }

    private static (int Width, int Height) ReadJpegDimensions(byte[] bytes)
    {
        var offset = 2;
        while (offset + 8 < bytes.Length)
        {
            if (bytes[offset] != 0xFF) { offset++; continue; }
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9) continue;
            if (offset + 2 > bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) break;
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (offset + 7 <= bytes.Length)
                {
                    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2));
                    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                    return (width, height);
                }
            }
            offset += length;
        }
        return (0, 0);
    }

    private static string StableBaseName(string? code)
    {
        var match = CodeRegex.Match(code ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? $"IMG-{number:D3}"
            : string.IsNullOrWhiteSpace(code) ? "IMG" : code.Trim().ToUpperInvariant();
    }

    private static int CodeNumber(string? code)
    {
        var match = CodeRegex.Match(code ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : int.MaxValue;
    }

    private static string Mime(string extension) => extension.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static async Task WriteTextAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);

    private sealed record DocxImage(string BaseName, string Extension, byte[] Bytes, int Width, int Height);
}
