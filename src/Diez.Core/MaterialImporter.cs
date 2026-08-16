using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class MaterialImporter
{
    private const int MaxPdfScanBytes = 16 * 1024 * 1024;

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
            ".docx" => ImportDocx(path),
            ".odt" => ImportOdt(path),
            ".rtf" => await ImportRtfAsync(path),
            ".pdf" => await ImportPdfAsync(path),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => ImportImage(path),
            _ => throw new NotSupportedException("Formato non ancora supportato in questa build.")
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
            if (lineCount <= 40)
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

    private static MaterialEntry ImportDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX non valido: word/document.xml mancante.");

        XDocument document;
        using (var stream = documentEntry.Open()) document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = document.Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
            .Where(text => text.Length > 0)
            .ToList();

        var preview = string.Join(Environment.NewLine, paragraphs.Take(40));
        var characters = paragraphs.Sum(p => p.Length);
        return new MaterialEntry
        {
            Kind = "DOCX",
            Summary = $"{paragraphs.Count:N0} paragrafi · {characters:N0} caratteri",
            Preview = preview
        };
    }

    private static MaterialEntry ImportOdt(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var contentEntry = archive.GetEntry("content.xml")
            ?? throw new InvalidDataException("ODT non valido: content.xml mancante.");

        XDocument document;
        using (var stream = contentEntry.Open()) document = XDocument.Load(stream);
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        var paragraphs = document.Descendants()
            .Where(e => e.Name == text + "p" || e.Name == text + "h")
            .Select(e => string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim())
            .Where(value => value.Length > 0)
            .ToList();

        return new MaterialEntry
        {
            Kind = "ODT",
            Summary = $"{paragraphs.Count:N0} paragrafi",
            Preview = string.Join(Environment.NewLine, paragraphs.Take(40))
        };
    }

    private static async Task<MaterialEntry> ImportRtfAsync(string path)
    {
        var rtf = await File.ReadAllTextAsync(path);
        var plain = RtfToPlainText(rtf);
        var lines = plain.Replace("\r\n", "\n").Split('\n');
        var nonEmptyLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));
        return new MaterialEntry
        {
            Kind = "RTF",
            Summary = $"{nonEmptyLines:N0} righe di testo · {plain.Length:N0} caratteri",
            Preview = string.Join(Environment.NewLine, lines.Take(40)).Trim()
        };
    }

    private static async Task<MaterialEntry> ImportPdfAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var count = (int)Math.Min(stream.Length, MaxPdfScanBytes);
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (chunk == 0) break;
            read += chunk;
        }

        var ascii = Encoding.Latin1.GetString(buffer, 0, read);
        var pageCount = Regex.Matches(ascii, @"/Type\s*/Page\b", RegexOptions.CultureInvariant).Count;
        var titleMatch = Regex.Match(ascii, @"/Title\s*\((?<title>(?:\\.|[^)])*)\)", RegexOptions.CultureInvariant);
        var title = titleMatch.Success ? UnescapePdfLiteral(titleMatch.Groups["title"].Value) : string.Empty;

        var pageText = pageCount > 0 ? $"{pageCount:N0} pagine rilevate" : "numero pagine non determinato";
        var preview = string.IsNullOrWhiteSpace(title)
            ? "PDF incorporato nel progetto. In questa fase Diez ne registra impronta, dimensione e struttura di base; l'estrazione editoriale completa del testo verrà raffinata nei prossimi passaggi."
            : $"Titolo PDF: {title}\n\nPDF incorporato nel progetto. In questa fase Diez ne registra impronta, dimensione e struttura di base.";

        return new MaterialEntry
        {
            Kind = "PDF",
            Summary = pageText,
            Preview = preview
        };
    }

    private static MaterialEntry ImportImage(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var (width, height) = TryReadImageDimensions(path);
        var dimensions = width.HasValue && height.HasValue
            ? $"{width.Value:N0} × {height.Value:N0} px"
            : "dimensioni non rilevate";

        return new MaterialEntry
        {
            Kind = "Immagine " + extension,
            Summary = dimensions,
            Preview = $"Risorsa immagine {extension}\n{dimensions}\n\nL'originale viene incorporato nel file .diez, così il progetto non dipende dal percorso sorgente sul PC."
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

    private static string RtfToPlainText(string rtf)
    {
        var output = new StringBuilder();
        for (var i = 0; i < rtf.Length; i++)
        {
            var ch = rtf[i];
            if (ch is '{' or '}') continue;
            if (ch != '\\')
            {
                if (ch != '\r' && ch != '\n') output.Append(ch);
                continue;
            }

            if (i + 1 >= rtf.Length) break;
            var next = rtf[++i];
            if (next is '\\' or '{' or '}')
            {
                output.Append(next);
                continue;
            }

            if (next == '\'' && i + 2 < rtf.Length)
            {
                var hex = rtf.Substring(i + 1, 2);
                if (byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                    output.Append(Encoding.GetEncoding(1252).GetString([value]));
                i += 2;
                continue;
            }

            if (!char.IsLetter(next))
            {
                if (next == '~') output.Append(' ');
                continue;
            }

            var word = new StringBuilder().Append(next);
            while (i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
                word.Append(rtf[++i]);

            var sign = 1;
            if (i + 1 < rtf.Length && rtf[i + 1] == '-')
            {
                sign = -1;
                i++;
            }

            var number = 0;
            var hasNumber = false;
            while (i + 1 < rtf.Length && char.IsDigit(rtf[i + 1]))
            {
                hasNumber = true;
                number = number * 10 + (rtf[++i] - '0');
            }
            number *= sign;

            if (i + 1 < rtf.Length && rtf[i + 1] == ' ') i++;

            switch (word.ToString())
            {
                case "par":
                case "line":
                    output.AppendLine();
                    break;
                case "tab":
                    output.Append('\t');
                    break;
                case "u" when hasNumber:
                    output.Append((char)(number < 0 ? number + 65536 : number));
                    if (i + 1 < rtf.Length && rtf[i + 1] != '\\' && rtf[i + 1] != '{' && rtf[i + 1] != '}')
                        i++;
                    break;
            }
        }

        return Regex.Replace(output.ToString(), @"[ \t]+", " ").Trim();
    }

    private static (int? Width, int? Height) TryReadImageDimensions(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".png" => ReadPngDimensions(path),
                ".gif" => ReadGifDimensions(path),
                ".bmp" => ReadBmpDimensions(path),
                ".jpg" or ".jpeg" => ReadJpegDimensions(path),
                _ => (null, null)
            };
        }
        catch
        {
            return (null, null);
        }
    }

    private static (int?, int?) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < header.Length ||
            header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
            return (null, null);
        return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static (int?, int?) ReadGifDimensions(string path)
    {
        Span<byte> header = stackalloc byte[10];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < header.Length) return (null, null);
        return (BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]));
    }

    private static (int?, int?) ReadBmpDimensions(string path)
    {
        Span<byte> header = stackalloc byte[26];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < header.Length || header[0] != (byte)'B' || header[1] != (byte)'M')
            return (null, null);
        return (Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header[18..22])), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header[22..26])));
    }

    private static (int?, int?) ReadJpegDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (ReadUInt16BigEndian(reader) != 0xFFD8) return (null, null);

        while (stream.Position + 4 < stream.Length)
        {
            byte prefix;
            do prefix = reader.ReadByte(); while (prefix != 0xFF && stream.Position < stream.Length);
            byte marker;
            do marker = reader.ReadByte(); while (marker == 0xFF && stream.Position < stream.Length);

            if (marker is 0xD8 or 0xD9) continue;
            var segmentLength = ReadUInt16BigEndian(reader);
            if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length) break;

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                _ = reader.ReadByte();
                var height = ReadUInt16BigEndian(reader);
                var width = ReadUInt16BigEndian(reader);
                return (width, height);
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }
        return (null, null);
    }

    private static int ReadUInt16BigEndian(BinaryReader reader) => (reader.ReadByte() << 8) | reader.ReadByte();

    private static string UnescapePdfLiteral(string value) => value
        .Replace("\\(", "(")
        .Replace("\\)", ")")
        .Replace("\\n", " ")
        .Replace("\\r", " ")
        .Replace("\\\\", "\\")
        .Trim();

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
