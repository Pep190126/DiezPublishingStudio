using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class WordSearchFullDatabaseExportService
{
    public static string SuggestedName(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "word-search").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return (string.IsNullOrWhiteSpace(safe) ? "word-search" : safe) + "-database-completo.xlsx";
    }

    public static async Task<AiProductionActionResult> ExportAsync(PreviewProject project, string path)
    {
        var puzzleRecords = WordSearchWorkspaceService.GetRecords(project);
        var lexicon = WordSearchLexiconService.GetEntries(project);
        if (puzzleRecords.Count == 0 && lexicon.Count == 0)
            return new(false, "Non ci sono ancora dati Word Search da salvare.");

        var fullPath = EnsureExtension(path, ".xlsx");
        var sheets = new List<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)>
        {
            ("PAROLE", LexiconRows(lexicon)),
            ("DATABASE", PuzzleDatabaseRows(project, puzzleRecords)),
            ("INFO", InfoRows(project, lexicon.Count, puzzleRecords.Count))
        };
        await WriteWorkbookAsync(fullPath, sheets);
        return new(true,
            $"Database completo salvato: {Path.GetFileName(fullPath)} · {lexicon.Count} parole disponibili · {puzzleRecords.Count} puzzle. Può essere reimportato in Diez.");
    }

    private static IReadOnlyList<IReadOnlyList<string>> LexiconRows(IReadOnlyList<WordSearchLexiconEntry> entries)
    {
        var canonicalNormalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "word", "words", "parola", "term", "termine", "keyword",
            "category", "categoria", "subcategory", "sottocategoria", "subcat",
            "series", "serie", "collection", "collezione",
            "decade", "decennio", "year", "years", "anno", "anni",
            "relevance", "relevancescore", "rilevanza", "nostalgia", "nostalgiascore", "score",
            "kdpsafe", "safe", "sicuro", "origin", "origine"
        };
        var extraHeaders = entries
            .SelectMany(e => e.Fields?.Keys ?? Enumerable.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h) && !canonicalNormalized.Contains(NormalizeHeader(h)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var headers = new List<string>
        {
            "ID", "WORD", "CATEGORY", "SUBCATEGORY", "SERIES", "DECADE", "YEAR", "RELEVANCE", "KDPSAFE", "ORIGIN"
        };
        headers.AddRange(extraHeaders);
        var rows = new List<IReadOnlyList<string>> { headers };
        foreach (var entry in entries)
        {
            var row = new List<string>
            {
                entry.Id,
                entry.Word,
                entry.Category,
                entry.Subcategory,
                entry.Series,
                entry.Decade,
                entry.Year,
                entry.Relevance?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                entry.KdpSafe.HasValue ? (entry.KdpSafe.Value ? "YES" : "NO") : string.Empty,
                entry.Origin
            };
            foreach (var header in extraHeaders)
                row.Add(entry.Fields is not null && entry.Fields.TryGetValue(header, out var value) ? value : string.Empty);
            rows.Add(row);
        }
        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> PuzzleDatabaseRows(PreviewProject project, IReadOnlyList<WordSearchRecord> records)
    {
        var rows = new List<IReadOnlyList<string>>();
        var header = new List<string> { "Campo" };
        header.AddRange(Enumerable.Range(1, records.Count).Select(i => $"Puzzle {i}"));
        rows.Add(header);
        if (records.Count == 0) return rows;

        var expected = records.ToDictionary(r => r.ContentId, r => WordSearchDatabaseService.ExpectedWordCount(project, r));
        var maxWords = Math.Max(1, expected.Values.DefaultIfEmpty(20).Max());
        AddRow(rows, "Ordine", records.Select(r => r.Order.ToString(CultureInfo.InvariantCulture)));
        AddRow(rows, "ID", records.Select(r => r.Id));
        AddRow(rows, "Titolo", records.Select(r => r.Title));
        AddRow(rows, "Tema", records.Select(r => r.Theme));
        AddRow(rows, "Numero parole previste", records.Select(r => expected[r.ContentId].ToString(CultureInfo.InvariantCulture)));
        for (var i = 0; i < maxWords; i++)
            AddRow(rows, $"Parola {i + 1:D2}", records.Select(r => i < r.Words.Count && i < expected[r.ContentId] ? r.Words[i] : string.Empty));
        AddRow(rows, "Numero parole presenti", records.Select(r => r.Words.Count.ToString(CultureInfo.InvariantCulture)));
        AddRow(rows, "Stato", records.Select(r => r.Status));
        AddRow(rows, "Origine", records.Select(r => r.Origin));
        AddRow(rows, "Note", records.Select(r => r.Notes));
        AddRow(rows, "Aggiornato", records.Select(r => r.UpdatedAtLocal));
        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> InfoRows(PreviewProject project, int words, int puzzles) =>
        new List<IReadOnlyList<string>>
        {
            new[] { "Informazione", "Valore" },
            new[] { "Tipo di archivio", "Word Search" },
            new[] { "Scopo", "Database completo di lavoro, leggibile e reimportabile in Diez" },
            new[] { "Titolo", project.EditionMetadata?.Title ?? project.Name },
            new[] { "Parole disponibili", words.ToString(CultureInfo.InvariantCulture) },
            new[] { "Puzzle presenti", puzzles.ToString(CultureInfo.InvariantCulture) },
            new[] { "Foglio PAROLE", "Database disponibile proveniente da intake, AI e correzioni" },
            new[] { "Foglio DATABASE", "Ogni colonna è un puzzle: Puzzle 1, Puzzle 2, ... Puzzle N" },
            new[] { "Regola", "I riferimenti tecnici di Diez non sono necessari per leggere o modificare questo file" }
        };

    private static void AddRow(List<IReadOnlyList<string>> rows, string field, IEnumerable<string> values)
    {
        var row = new List<string> { field };
        row.AddRange(values);
        rows.Add(row);
    }

    private static async Task WriteWorkbookAsync(string path, IReadOnlyList<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)> sheets)
    {
        EnsureDirectory(path);
        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Count));
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook(sheets.Select(s => s.Name).ToList()));
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
            await WriteEntry(archive, "xl/styles.xml", Styles());
            for (var i = 0; i < sheets.Count; i++)
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
                row.Add(new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    r == 0 ? new XAttribute("s", "1") : null,
                    new XElement(x + "is", new XElement(x + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? string.Empty))));
            }
            data.Add(row);
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "worksheet",
                new XElement(x + "sheetViews",
                    new XElement(x + "sheetView",
                        new XAttribute("workbookViewId", "0"),
                        new XAttribute("showGridLines", "1"),
                        new XElement(x + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                data)));
    }

    private static string ContentTypes(int count)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        for (var i = 1; i <= count; i++)
            root.Add(new XElement(x + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string RootRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
    }

    private static string Workbook(IReadOnlyList<string> names)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets = new XElement(x + "sheets");
        for (var i = 0; i < names.Count; i++)
            sheets.Add(new XElement(x + "sheet", new XAttribute("name", names[i]), new XAttribute("sheetId", i + 1), new XAttribute(r + "id", $"rId{i + 1}")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "workbook", new XAttribute(XNamespace.Xmlns + "r", r), sheets)));
    }

    private static string WorkbookRels(int count)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(x + "Relationships");
        for (var i = 1; i <= count; i++)
            root.Add(new XElement(x + "Relationship", new XAttribute("Id", $"rId{i}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        root.Add(new XElement(x + "Relationship", new XAttribute("Id", $"rId{count + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
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

    private static string NormalizeHeader(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in (value ?? string.Empty).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        return builder.ToString();
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
