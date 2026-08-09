using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class WordSearchExportService
{
    public static string SuggestedDatabaseName(PreviewProject project) => SafeBase(project) + "-database-word-search.xlsx";
    public static string SuggestedFlatXlsxName(PreviewProject project) => SafeBase(project) + "-tabella-word-search.xlsx";
    public static string SuggestedFlatCsvName(PreviewProject project) => SafeBase(project) + "-tabella-word-search.csv";

    public static async Task<AiProductionActionResult> ExportDatabaseAsync(PreviewProject project, string path)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da salvare nel database.");
        var fullPath = EnsureExtension(path, ".xlsx");
        await WriteWorkbookAsync(fullPath,
            ("DATABASE", DatabaseRows(records)),
            ("INFO", InfoRows(project, records.Count)));
        return new(true, $"Database Word Search salvato: {Path.GetFileName(fullPath)} · {records.Count} puzzle in colonne. Può essere reimportato in Diez.");
    }

    public static async Task<AiProductionActionResult> ExportFlatXlsxAsync(PreviewProject project, string path)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");
        var fullPath = EnsureExtension(path, ".xlsx");
        await WriteWorkbookAsync(fullPath, ("PUZZLE", FlatRows(records)));
        return new(true, $"Tabella XLSX esportata: {Path.GetFileName(fullPath)} · un puzzle per riga e parole in colonne.");
    }

    public static async Task<AiProductionActionResult> ExportFlatCsvAsync(PreviewProject project, string path)
    {
        var records = WordSearchWorkspaceService.GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");
        var fullPath = EnsureExtension(path, ".csv");
        var rows = FlatRows(records);
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
        EnsureDirectory(fullPath);
        await File.WriteAllTextAsync(fullPath, builder.ToString(), new UTF8Encoding(true));
        return new(true, $"Tabella CSV esportata: {Path.GetFileName(fullPath)} · un puzzle per riga e parole in colonne.");
    }

    private static IReadOnlyList<IReadOnlyList<string>> DatabaseRows(IReadOnlyList<WordSearchRecord> records)
    {
        var maxWords = Math.Max(1, records.Max(r => r.Words.Count));
        var rows = new List<IReadOnlyList<string>>();

        var header = new List<string> { "Campo" };
        header.AddRange(Enumerable.Range(1, records.Count).Select(i => $"Puzzle {i}"));
        rows.Add(header);

        AddDatabaseRow(rows, "Ordine", records.Select(r => r.Order.ToString(CultureInfo.InvariantCulture)));
        AddDatabaseRow(rows, "ID", records.Select(r => r.Id));
        AddDatabaseRow(rows, "Titolo", records.Select(r => r.Title));
        AddDatabaseRow(rows, "Tema", records.Select(r => r.Theme));
        for (var wordIndex = 0; wordIndex < maxWords; wordIndex++)
            AddDatabaseRow(rows, $"Parola {wordIndex + 1:D2}", records.Select(r => wordIndex < r.Words.Count ? r.Words[wordIndex] : string.Empty));
        AddDatabaseRow(rows, "Numero parole", records.Select(r => r.Words.Count.ToString(CultureInfo.InvariantCulture)));
        AddDatabaseRow(rows, "Stato", records.Select(r => r.Status));
        AddDatabaseRow(rows, "Origine", records.Select(r => r.Origin));
        AddDatabaseRow(rows, "Note", records.Select(r => r.Notes));
        AddDatabaseRow(rows, "Aggiornato", records.Select(r => r.UpdatedAtLocal));
        return rows;
    }

    private static void AddDatabaseRow(List<IReadOnlyList<string>> rows, string field, IEnumerable<string> values)
    {
        var row = new List<string> { field };
        row.AddRange(values);
        rows.Add(row);
    }

    private static IReadOnlyList<IReadOnlyList<string>> FlatRows(IReadOnlyList<WordSearchRecord> records)
    {
        var maxWords = Math.Max(1, records.Max(r => r.Words.Count));
        var headers = new List<string> { "Ordine", "ID", "Titolo", "Tema" };
        headers.AddRange(Enumerable.Range(1, maxWords).Select(i => $"Parola {i:D2}"));
        headers.AddRange(["Stato", "Origine", "Note"]);
        var rows = new List<IReadOnlyList<string>> { headers };
        foreach (var record in records)
        {
            var row = new List<string>
            {
                record.Order.ToString(CultureInfo.InvariantCulture), record.Id, record.Title, record.Theme
            };
            for (var i = 0; i < maxWords; i++) row.Add(i < record.Words.Count ? record.Words[i] : string.Empty);
            row.AddRange([record.Status, record.Origin, record.Notes]);
            rows.Add(row);
        }
        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> InfoRows(PreviewProject project, int count) =>
        new List<IReadOnlyList<string>>
        {
            new[] { "Informazione", "Valore" },
            new[] { "Tipo di archivio", "Word Search" },
            new[] { "Scopo", "Database completo di lavoro, leggibile e reimportabile in Diez" },
            new[] { "Titolo", project.EditionMetadata?.Title ?? project.Name },
            new[] { "Puzzle presenti", count.ToString(CultureInfo.InvariantCulture) },
            new[] { "Struttura", "Ogni colonna è un puzzle: Puzzle 1, Puzzle 2, ... Puzzle N" },
            new[] { "Regola ID", "PUZ-### identifica stabilmente il puzzle anche dopo correzioni o reimportazioni" }
        };

    private static async Task WriteWorkbookAsync(string path, params (string Name, IReadOnlyList<IReadOnlyList<string>> Rows)[] sheets)
    {
        EnsureDirectory(path);
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Length));
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook(sheets.Select(s => s.Name).ToList()));
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Length));
            await WriteEntry(archive, "xl/styles.xml", Styles());
            for (var i = 0; i < sheets.Length; i++)
                await WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i].Rows));
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
                var text = rows[r][c] ?? string.Empty;
                row.Add(new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    r == 0 ? new XAttribute("s", "1") : null,
                    new XElement(x + "is", new XElement(x + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text))));
            }
            data.Add(row);
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "worksheet",
                new XElement(x + "sheetViews", new XElement(x + "sheetView", new XAttribute("workbookViewId", "0"),
                    new XElement(x + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                data)));
    }

    private static string ContentTypes(int sheetCount)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        for (var i = 1; i <= sheetCount; i++)
            root.Add(new XElement(x + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string RootRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
    }

    private static string Workbook(IReadOnlyList<string> sheetNames)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets = new XElement(x + "sheets");
        for (var i = 0; i < sheetNames.Count; i++)
            sheets.Add(new XElement(x + "sheet", new XAttribute("name", sheetNames[i]), new XAttribute("sheetId", i + 1), new XAttribute(r + "id", $"rId{i + 1}")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "workbook", new XAttribute(XNamespace.Xmlns + "r", r), sheets)));
    }

    private static string WorkbookRels(int sheetCount)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(x + "Relationships");
        for (var i = 1; i <= sheetCount; i++)
            root.Add(new XElement(x + "Relationship", new XAttribute("Id", $"rId{i}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        root.Add(new XElement(x + "Relationship", new XAttribute("Id", $"rId{sheetCount + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string Styles()
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "styleSheet",
            new XElement(x + "fonts", new XAttribute("count", "2"),
                new XElement(x + "font", new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos"))),
                new XElement(x + "font", new XElement(x + "b"), new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos")))),
            new XElement(x + "fills", new XAttribute("count", "2"), new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "none"))), new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "gray125")))),
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