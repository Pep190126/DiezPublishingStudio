using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class HandoffExportService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    private const int SpreadsheetCellChunk = 30000;

    public static string SuggestedCsvFileName(PreviewProject project) =>
        $"{BaseName(project)}-master.csv";

    public static string SuggestedXlsxFileName(PreviewProject project) =>
        $"{BaseName(project)}-master.xlsx";

    public static string SuggestedImageZipFileName(PreviewProject project) =>
        $"{BaseName(project)}-immagini-originali.zip";

    public static async Task<HandoffExportResult> ExportMasterCsvAsync(PreviewProject project, string outputPath)
    {
        var validation = ValidateEditorialHandoff(project);
        if (!validation.Ready)
            return new HandoffExportResult(false, validation.Message, null, 0);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new HandoffExportResult(false, "Percorso CSV non valido.", null, 0);

        var nodes = OrderedEditableNodes(project);
        var fullPath = EnsureExtension(outputPath, ".csv");
        EnsureDirectory(fullPath);
        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            var text = BuildCsv(project, nodes);
            await File.WriteAllTextAsync(tempPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(tempPath, fullPath, overwrite: true);
            return new HandoffExportResult(true, $"CSV esportato: {Path.GetFileName(fullPath)}", fullPath, nodes.Count);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public static async Task<HandoffExportResult> ExportMasterXlsxAsync(PreviewProject project, string outputPath)
    {
        var validation = ValidateEditorialHandoff(project);
        if (!validation.Ready)
            return new HandoffExportResult(false, validation.Message, null, 0);

        if (string.IsNullOrWhiteSpace(outputPath))
            return new HandoffExportResult(false, "Percorso XLSX non valido.", null, 0);

        var nodes = OrderedEditableNodes(project);
        var rows = BuildSpreadsheetRows(project, nodes);
        var fullPath = EnsureExtension(outputPath, ".xlsx");
        EnsureDirectory(fullPath);
        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteTextEntryAsync(archive, "[Content_Types].xml", BuildSpreadsheetContentTypes());
                await WriteTextEntryAsync(archive, "_rels/.rels", BuildSpreadsheetRootRelationships());
                await WriteTextEntryAsync(archive, "xl/workbook.xml", BuildWorkbook());
                await WriteTextEntryAsync(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships());
                await WriteTextEntryAsync(archive, "xl/styles.xml", BuildSpreadsheetStyles());
                await WriteTextEntryAsync(archive, "xl/worksheets/sheet1.xml", BuildWorksheet(rows));
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return new HandoffExportResult(true, $"XLSX esportato: {Path.GetFileName(fullPath)}", fullPath, rows.Count);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public static async Task<HandoffExportResult> ExportOriginalImagesZipAsync(PreviewProject project, string projectPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !ProjectFileStore.IsPackageFile(projectPath))
            return new HandoffExportResult(false, "Salva prima il progetto come pacchetto .diez per esportare gli originali.", null, 0);
        if (string.IsNullOrWhiteSpace(outputPath))
            return new HandoffExportResult(false, "Percorso ZIP non valido.", null, 0);

        var images = project.Materials.Where(IsImageMaterial).ToList();
        if (images.Count == 0)
            return new HandoffExportResult(false, "Nel progetto non ci sono immagini da esportare.", null, 0);

        var fullPath = EnsureExtension(outputPath, ".zip");
        EnsureDirectory(fullPath);
        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                for (var i = 0; i < images.Count; i++)
                {
                    var material = images[i];
                    var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                    if (bytes is null)
                        throw new InvalidDataException($"Originale immagine non disponibile nel .diez: {material.FileName}");

                    var entryName = $"{i + 1:D3}-{SanitizeFileName(material.FileName)}";
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await target.WriteAsync(bytes);
                }
            }

            File.Move(tempPath, fullPath, overwrite: true);
            return new HandoffExportResult(true, $"ZIP immagini esportato: {Path.GetFileName(fullPath)} · {images.Count} originali", fullPath, images.Count);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static HandoffValidation ValidateEditorialHandoff(PreviewProject project)
    {
        var preflight = EditionFreezeService.RunPreflight(project);
        if (!preflight.Ready)
            return new HandoffValidation(false, "Export editoriale bloccato: il preflight non è READY.");
        if (!PublicationCandidateService.IsLatestCandidateCurrent(project))
            return new HandoffValidation(false, "Export editoriale bloccato: crea un Publication Candidate corrente.");
        if (OrderedEditableNodes(project).Count == 0)
            return new HandoffValidation(false, "Export editoriale bloccato: nessun contenuto Master disponibile.");
        return new HandoffValidation(true, string.Empty);
    }

    private static List<ContentNode> OrderedEditableNodes(PreviewProject project) =>
        project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(project, n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .ThenBy(n => n.ContentId)
            .ToList();

    private static string BuildCsv(PreviewProject project, IReadOnlyList<ContentNode> nodes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ordine;Materiale;Tipo;Titolo;Testo;Origine");
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == node.MaterialId);
            AppendCsvRow(builder,
                (i + 1).ToString(),
                material?.FileName ?? string.Empty,
                node.Kind ?? string.Empty,
                node.Title ?? string.Empty,
                node.Body ?? string.Empty,
                node.SourceLocator ?? string.Empty);
        }
        return builder.ToString();
    }

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(';');
            var value = values[i] ?? string.Empty;
            builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }
        builder.AppendLine();
    }

    private static List<SpreadsheetRow> BuildSpreadsheetRows(PreviewProject project, IReadOnlyList<ContentNode> nodes)
    {
        var rows = new List<SpreadsheetRow>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var material = project.Materials.FirstOrDefault(m => m.MaterialId == node.MaterialId);
            var chunks = Chunk(node.Body ?? string.Empty, SpreadsheetCellChunk).ToList();
            if (chunks.Count == 0) chunks.Add(string.Empty);
            for (var part = 0; part < chunks.Count; part++)
            {
                rows.Add(new SpreadsheetRow(
                    i + 1,
                    part + 1,
                    material?.FileName ?? string.Empty,
                    node.Kind ?? string.Empty,
                    node.Title ?? string.Empty,
                    chunks[part],
                    node.SourceLocator ?? string.Empty));
            }
        }
        return rows;
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        if (string.IsNullOrEmpty(value)) yield break;
        for (var offset = 0; offset < value.Length; offset += size)
            yield return value.Substring(offset, Math.Min(size, value.Length - offset));
    }

    private static string BuildSpreadsheetContentTypes()
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")))));
    }

    private static string BuildSpreadsheetRootRelationships()
    {
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rel + "Relationships",
                new XElement(rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")))));
    }

    private static string BuildWorkbook()
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(main + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", rel),
                new XElement(main + "sheets",
                    new XElement(main + "sheet", new XAttribute("name", "Master"), new XAttribute("sheetId", "1"), new XAttribute(rel + "id", "rId1"))))));
    }

    private static string BuildWorkbookRelationships()
    {
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rel + "Relationships",
                new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(rel + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")))));
    }

    private static string BuildSpreadsheetStyles()
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(main + "styleSheet",
                new XElement(main + "fonts", new XAttribute("count", "2"),
                    new XElement(main + "font", new XElement(main + "sz", new XAttribute("val", "11")), new XElement(main + "name", new XAttribute("val", "Aptos"))),
                    new XElement(main + "font", new XElement(main + "b"), new XElement(main + "sz", new XAttribute("val", "11")), new XElement(main + "name", new XAttribute("val", "Aptos")))),
                new XElement(main + "fills", new XAttribute("count", "2"),
                    new XElement(main + "fill", new XElement(main + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(main + "fill", new XElement(main + "patternFill", new XAttribute("patternType", "gray125")))),
                new XElement(main + "borders", new XAttribute("count", "1"), new XElement(main + "border")),
                new XElement(main + "cellStyleXfs", new XAttribute("count", "1"), new XElement(main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                new XElement(main + "cellXfs", new XAttribute("count", "2"),
                    new XElement(main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                    new XElement(main + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1"))),
                new XElement(main + "cellStyles", new XAttribute("count", "1"), new XElement(main + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))))));
    }

    private static string BuildWorksheet(IReadOnlyList<SpreadsheetRow> rows)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetData = new XElement(main + "sheetData");
        var headers = new[] { "Ordine", "Parte", "Materiale", "Tipo", "Titolo", "Testo", "Origine" };
        sheetData.Add(BuildSheetRow(main, 1, headers, header: true));

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            sheetData.Add(BuildSheetRow(main, i + 2,
            [
                row.Order.ToString(), row.Part.ToString(), row.Material, row.Kind, row.Title, row.Body, row.SourceLocator
            ], header: false));
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(main + "worksheet",
                new XElement(main + "cols",
                    Column(main, 1, 10), Column(main, 2, 8), Column(main, 3, 28), Column(main, 4, 16), Column(main, 5, 34), Column(main, 6, 80), Column(main, 7, 28)),
                sheetData));
        return Xml(document);
    }

    private static XElement Column(XNamespace main, int index, double width) =>
        new(main + "col", new XAttribute("min", index), new XAttribute("max", index), new XAttribute("width", width), new XAttribute("customWidth", "1"));

    private static XElement BuildSheetRow(XNamespace main, int rowNumber, IReadOnlyList<string> values, bool header)
    {
        var row = new XElement(main + "row", new XAttribute("r", rowNumber));
        for (var i = 0; i < values.Count; i++)
        {
            var cell = new XElement(main + "c",
                new XAttribute("r", CellReference(i, rowNumber)),
                new XAttribute("t", "inlineStr"),
                header ? new XAttribute("s", "1") : null,
                new XElement(main + "is", new XElement(main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), values[i] ?? string.Empty)));
            row.Add(cell);
        }
        return row;
    }

    private static string CellReference(int zeroBasedColumn, int row)
    {
        var n = zeroBasedColumn + 1;
        var letters = string.Empty;
        while (n > 0)
        {
            n--;
            letters = (char)('A' + n % 26) + letters;
            n /= 26;
        }
        return letters + row;
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static bool IsImageMaterial(MaterialEntry material) =>
        material.Kind.StartsWith("Immagine", StringComparison.OrdinalIgnoreCase) || ImageExtensions.Contains(Path.GetExtension(material.FileName));

    private static int MaterialOrder(PreviewProject project, Guid materialId)
    {
        var index = project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string BaseName(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var candidate = PublicationCandidateService.GetLatest(project);
        var sequence = candidate is null || !int.TryParse(candidate.ProposedValue, out var parsed) ? 1 : parsed;
        return $"{SanitizeFileName(title)}-handoff-{sequence:D3}";
    }

    private static string EnsureExtension(string path, string extension)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
            fullPath += extension;
        return fullPath;
    }

    private static void EnsureDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Diez-Project" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        return name.Replace(' ', '-');
    }

    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);

    private readonly record struct HandoffValidation(bool Ready, string Message);
    private sealed record SpreadsheetRow(int Order, int Part, string Material, string Kind, string Title, string Body, string SourceLocator);
}

internal readonly record struct HandoffExportResult(bool Exported, string Message, string? OutputPath, int ItemCount);
