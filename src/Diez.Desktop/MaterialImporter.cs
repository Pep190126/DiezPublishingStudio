using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class MaterialImporter
{
    public static async Task<MaterialEntry> ImportAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Il file selezionato non esiste più.", path);

        var info = new FileInfo(path);
        await using var hashStream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var entry = extension switch
        {
            ".txt" or ".md" => await ImportTextAsync(path),
            ".csv" => await ImportCsvAsync(path),
            ".xlsx" => ImportXlsx(path),
            _ => throw new NotSupportedException("In questa build puoi importare TXT, Markdown, CSV e XLSX.")
        };

        entry.FileName = info.Name;
        entry.SourcePath = info.FullName;
        entry.SizeBytes = info.Length;
        entry.Sha256 = hash;
        entry.ImportedAtLocal = DateTimeOffset.Now.ToString("G");
        return entry;
    }

    private static async Task<MaterialEntry> ImportTextAsync(string path)
    {
        var preview = new StringBuilder();
        var lineCount = 0;

        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync() is { } line)
        {
            lineCount++;
            if (lineCount <= 30)
                preview.AppendLine(line);
        }

        return new MaterialEntry
        {
            Kind = Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ? "Markdown" : "Testo",
            Summary = $"{lineCount:N0} righe",
            Preview = preview.ToString().TrimEnd()
        };
    }

    private static async Task<MaterialEntry> ImportCsvAsync(string path)
    {
        var rows = new List<List<string>>();
        var totalRows = 0;
        char delimiter = ',';
        var delimiterChosen = false;

        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!delimiterChosen && !string.IsNullOrWhiteSpace(line))
            {
                delimiter = DetectDelimiter(line);
                delimiterChosen = true;
            }

            totalRows++;
            if (rows.Count < 30)
                rows.Add(ParseCsvLine(line, delimiter));
        }

        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var columns = columnCount == 0
            ? []
            : Enumerable.Range(0, columnCount)
                .Select(i => rows.Count > 0 && i < rows[0].Count && !string.IsNullOrWhiteSpace(rows[0][i])
                    ? rows[0][i].Trim()
                    : $"Colonna {i + 1}")
                .ToList();

        var preview = string.Join(Environment.NewLine,
            rows.Select(r => string.Join(" | ", r.Select(CleanPreviewValue))));

        return new MaterialEntry
        {
            Kind = "CSV",
            Summary = $"{totalRows:N0} righe · {columnCount:N0} colonne · separatore {DescribeDelimiter(delimiter)}",
            Preview = preview,
            Columns = columns
        };
    }

    private static MaterialEntry ImportXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("XLSX non valido: workbook.xml mancante.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("XLSX non valido: relazioni workbook mancanti.");

        XDocument workbook;
        XDocument rels;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        using (var stream = relsEntry.Open()) rels = XDocument.Load(stream);

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("L'XLSX non contiene fogli.");
        var sheetName = (string?)firstSheet.Attribute("name") ?? "Foglio 1";
        var relationshipId = (string?)firstSheet.Attribute(officeRel + "id")
            ?? throw new InvalidDataException("Relazione del primo foglio mancante.");
        var target = rels.Descendants(packageRel + "Relationship")
            .FirstOrDefault(r => string.Equals((string?)r.Attribute("Id"), relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("File XML del primo foglio non trovato.");

        var normalizedTarget = target.Replace('\\', '/').TrimStart('/');
        var sheetPath = normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalizedTarget
            : "xl/" + normalizedTarget;
        var sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidDataException($"Foglio XLSX mancante: {sheetPath}.");

        var sharedStrings = ReadSharedStrings(archive, main);
        XDocument sheetDocument;
        using (var stream = sheetEntry.Open()) sheetDocument = XDocument.Load(stream);

        var allRows = sheetDocument.Descendants(main + "row").ToList();
        var previewRows = new List<List<string>>();
        var maxColumns = 0;

        foreach (var row in allRows.Take(30))
        {
            var valuesByColumn = new Dictionary<int, string>();
            var fallbackColumn = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                var columnIndex = string.IsNullOrWhiteSpace(reference)
                    ? fallbackColumn
                    : GetColumnIndex(reference);
                fallbackColumn = columnIndex + 1;
                valuesByColumn[columnIndex] = ReadCellValue(cell, main, sharedStrings);
                maxColumns = Math.Max(maxColumns, columnIndex + 1);
            }

            var rowValues = new List<string>();
            for (var i = 0; i < maxColumns; i++)
                rowValues.Add(valuesByColumn.TryGetValue(i, out var value) ? value : string.Empty);
            previewRows.Add(rowValues);
        }

        var columns = maxColumns == 0
            ? []
            : Enumerable.Range(0, maxColumns)
                .Select(i => previewRows.Count > 0 && i < previewRows[0].Count && !string.IsNullOrWhiteSpace(previewRows[0][i])
                    ? previewRows[0][i].Trim()
                    : $"Colonna {i + 1}")
                .ToList();

        var preview = string.Join(Environment.NewLine,
            previewRows.Select(r => string.Join(" | ", r.Select(CleanPreviewValue))));

        return new MaterialEntry
        {
            Kind = "XLSX",
            Summary = $"{sheetName} · {allRows.Count:N0} righe · {maxColumns:N0} colonne",
            Preview = preview,
            Columns = columns
        };
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(main + "si")
            .Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, XNamespace main, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            return string.Concat(cell.Descendants(main + "t").Select(t => t.Value));

        var raw = cell.Element(main + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.Ordinal) && int.TryParse(raw, out var index) && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];

        if (string.Equals(type, "b", StringComparison.Ordinal))
            return raw == "1" ? "TRUE" : "FALSE";

        return raw;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var value = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch)) break;
            value = value * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, value - 1);
    }

    private static char DetectDelimiter(string line)
    {
        var candidates = new[] { ';', ',', '\t' };
        return candidates
            .Select(c => (Delimiter: c, Count: CountDelimiterOutsideQuotes(line, c)))
            .OrderByDescending(x => x.Count)
            .First().Delimiter;
    }

    private static int CountDelimiterOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && line[i] == delimiter)
            {
                count++;
            }
        }
        return count;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string DescribeDelimiter(char delimiter) => delimiter switch
    {
        ';' => ";",
        '\t' => "TAB",
        _ => ","
    };

    private static string CleanPreviewValue(string value)
    {
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= 120 ? cleaned : cleaned[..117] + "...";
    }
}
