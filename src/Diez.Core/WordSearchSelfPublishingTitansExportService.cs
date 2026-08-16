using System.IO.Compression;
using System.Security;
using System.Text;

namespace DiezPublishingStudio;

/// <summary>
/// Final Word Search handoff profile compatible with the column-oriented
/// Self Publishing Titans sample supplied for Diez.
///
/// Contract:
/// - one puzzle per column;
/// - headers are "puzzle 1", "puzzle 2", ... in lower case;
/// - one word position per row;
/// - no Diez metadata rows in the final handoff;
/// - CSV uses UTF-8 BOM + comma delimiter + LF line endings;
/// - plain cells remain unquoted, while RFC-compatible quoting is used only
///   when a value actually contains comma, quote or a line break.
///
/// Empty trailing columns/rows present in a particular spreadsheet sample are
/// treated as padding, not book data, so Diez does not invent extra puzzles.
/// </summary>
internal static class WordSearchSelfPublishingTitansExportService
{
    public static string SuggestedCsvName(PreviewProject project) => SafeBase(project) + "-self-publishing-titans.csv";
    public static string SuggestedXlsxName(PreviewProject project) => SafeBase(project) + "-self-publishing-titans.xlsx";

    public static async Task<AiProductionActionResult> ExportCsvAsync(PreviewProject project, string path)
    {
        var records = OrderedRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");

        var fullPath = EnsureExtension(path, ".csv");
        EnsureDirectory(fullPath);
        var rows = BuildRows(project, records);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(EscapeCsv(row[i]));
            }
            builder.Append('\n');
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new(true,
            $"CSV finale Self Publishing Titans esportato: {Path.GetFileName(fullPath)} · {records.Count} puzzle in colonne.");
    }

    public static async Task<AiProductionActionResult> ExportXlsxAsync(PreviewProject project, string path)
    {
        var records = OrderedRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");

        var fullPath = EnsureExtension(path, ".xlsx");
        EnsureDirectory(fullPath);
        var rows = BuildRows(project, records);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes());
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook());
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            await WriteEntry(archive, "xl/worksheets/sheet1.xml", Worksheet(rows));
        }
        File.Move(temp, fullPath, true);

        return new(true,
            $"XLSX finale Self Publishing Titans esportato: {Path.GetFileName(fullPath)} · {records.Count} puzzle in colonne.");
    }

    internal static IReadOnlyList<IReadOnlyList<string>> BuildRows(PreviewProject project) =>
        BuildRows(project, OrderedRecords(project));

    private static IReadOnlyList<IReadOnlyList<string>> BuildRows(
        PreviewProject project,
        IReadOnlyList<WordSearchRecord> records)
    {
        if (records.Count == 0) return [];
        var expected = records.ToDictionary(r => r.ContentId, r => WordSearchDatabaseService.ExpectedWordCount(project, r));
        var maxWords = Math.Max(1, expected.Values.DefaultIfEmpty(20).Max());
        var rows = new List<IReadOnlyList<string>>
        {
            Enumerable.Range(1, records.Count).Select(i => $"puzzle {i}").ToList()
        };

        for (var wordIndex = 0; wordIndex < maxWords; wordIndex++)
        {
            rows.Add(records.Select(record =>
                wordIndex < expected[record.ContentId] && wordIndex < record.Words.Count
                    ? record.Words[wordIndex]
                    : string.Empty).ToList());
        }
        return rows;
    }

    private static IReadOnlyList<WordSearchRecord> OrderedRecords(PreviewProject project) =>
        WordSearchWorkspaceService.GetRecords(project)
            .OrderBy(record => record.Order)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n')) return text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string Worksheet(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            builder.Append("<row r=\"").Append(r + 1).Append("\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var reference = ColumnName(c + 1) + (r + 1);
                var escaped = SecurityElement.Escape(rows[r][c] ?? string.Empty) ?? string.Empty;
                builder.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(escaped).Append("</t></is></c>");
            }
            builder.Append("</row>");
        }
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string ContentTypes() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""";

    private static string RootRels() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";

    private static string Workbook() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets><sheet name="PUZZLE" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""";

    private static string WorkbookRels() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""";

    private static async Task WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.TrimStart());
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static string SafeBase(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "word-search").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "word-search" : safe;
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }
}
