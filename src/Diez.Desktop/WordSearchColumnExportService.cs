using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class WordSearchColumnExportService
{
    public static string SuggestedXlsxName(PreviewProject project) => SafeBase(project) + "-puzzle.xlsx";
    public static string SuggestedCsvName(PreviewProject project) => SafeBase(project) + "-puzzle.csv";

    public static async Task<AiProductionActionResult> ExportXlsxAsync(PreviewProject project, string path)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");
        var fullPath = EnsureExtension(path, ".xlsx");
        var rows = BuildRows(project, records);
        await WriteWorkbookAsync(fullPath, rows);
        return new(true, $"XLSX esportato: {Path.GetFileName(fullPath)} · {records.Count} puzzle in colonne.");
    }

    public static async Task<AiProductionActionResult> ExportCsvAsync(PreviewProject project, string path)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");
        var fullPath = EnsureExtension(path, ".csv");
        EnsureDirectory(fullPath);
        var rows = BuildRows(project, records);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append('"').Append((row[i] ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            }
            builder.AppendLine();
        }
        await File.WriteAllTextAsync(fullPath, builder.ToString(), new UTF8Encoding(true));
        return new(true, $"CSV esportato: {Path.GetFileName(fullPath)} · {records.Count} puzzle in colonne.");
    }

    internal static IReadOnlyList<IReadOnlyList<string>> BuildRows(PreviewProject project, IReadOnlyList<WordSearchRecord> records)
    {
        var ordered = records.OrderBy(r => r.Order).ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var expected = ordered.ToDictionary(r => r.ContentId, r => WordSearchDatabaseService.ExpectedWordCount(project, r));
        var maxWords = Math.Max(1, expected.Values.DefaultIfEmpty(20).Max());
        var rows = new List<IReadOnlyList<string>>
        {
            Enumerable.Range(1, ordered.Count).Select(i => $"Puzzle {i}").ToList()
        };

        for (var wordIndex = 0; wordIndex < maxWords; wordIndex++)
        {
            var row = new List<string>(ordered.Count);
            foreach (var record in ordered)
            {
                var count = expected[record.ContentId];
                row.Add(wordIndex < count && wordIndex < record.Words.Count ? record.Words[wordIndex] : string.Empty);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static async Task WriteWorkbookAsync(string path, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        EnsureDirectory(path);
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes());
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook());
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            await WriteEntry(archive, "xl/styles.xml", Styles());
            await WriteEntry(archive, "xl/worksheets/sheet1.xml", Worksheet(rows));
        }
        File.Move(temp, path, true);
    }

    private static string Worksheet(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(x + "sheetData");
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(x + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Count; c++)
            {
                row.Add(new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    r == 0 ? new XAttribute("s", "1") : null,
                    new XElement(x + "is", new XElement(x + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? string.Empty))));
            }
            data.Add(row);
        }

        // Non disattiviamo le gridline: Excel mostra i normali bordi/griglia del foglio.
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "worksheet",
                new XElement(x + "sheetViews",
                    new XElement(x + "sheetView", new XAttribute("workbookViewId", "0"),
                        new XElement(x + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                data)));
    }

    private static string ContentTypes()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
    }

    private static string RootRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
    }

    private static string Workbook()
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "workbook", new XAttribute(XNamespace.Xmlns + "r", r),
            new XElement(x + "sheets", new XElement(x + "sheet", new XAttribute("name", "PUZZLE"), new XAttribute("sheetId", "1"), new XAttribute(r + "id", "rId1"))))));
    }

    private static string WorkbookRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(x + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")))));
    }

    private static string Styles()
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "styleSheet",
            new XElement(x + "fonts", new XAttribute("count", "2"),
                new XElement(x + "font", new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos"))),
                new XElement(x + "font", new XElement(x + "b"), new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos")))),
            new XElement(x + "fills", new XAttribute("count", "2"),
                new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(x + "borders", new XAttribute("count", "1"), new XElement(x + "border")),
            new XElement(x + "cellStyleXfs", new XAttribute("count", "1"), new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
            new XElement(x + "cellXfs", new XAttribute("count", "2"),
                new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1"))),
            new XElement(x + "cellStyles", new XAttribute("count", "1"), new XElement(x + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))))));
    }

    private static async Task WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static string SafeBase(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "word-search").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "word-search" : safe;
    }

    private static string CellRef(int column, int row)
    {
        var n = column + 1;
        var s = string.Empty;
        while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; }
        return s + row;
    }

    private static string EnsureExtension(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
    private static void EnsureDirectory(string path) { var dir = Path.GetDirectoryName(Path.GetFullPath(path)); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); }
    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);
}
