using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal static class WordSearchDatabaseService
{
    private const string SettingsKind = "WordSearchPuzzleSettings";

    public static int ExpectedWordCount(PreviewProject project, WordSearchRecord record)
    {
        var node = project.ContentNodes.FirstOrDefault(n =>
            string.Equals(n.Kind, SettingsKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.SourceLocator, record.Id, StringComparison.OrdinalIgnoreCase));
        if (node is not null && int.TryParse(node.Body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stored) && stored > 0)
            return Math.Max(stored, record.Words.Count);
        return Math.Max(20, record.Words.Count);
    }

    public static void SetExpectedWordCount(PreviewProject project, string puzzleId, int count)
    {
        if (count <= 0) return;
        var node = project.ContentNodes.FirstOrDefault(n =>
            string.Equals(n.Kind, SettingsKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.SourceLocator, puzzleId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new ContentNode
            {
                Kind = SettingsKind,
                SourceLocator = puzzleId,
                Title = "Numero parole previsto",
                Ordinal = 0
            };
            project.ContentNodes.Add(node);
        }
        node.Body = count.ToString(CultureInfo.InvariantCulture);
    }

    public static async Task<WordSearchMergeResult> ImportDatabaseAsync(
        PreviewProject project,
        string path,
        Guid sourceMaterialId,
        bool replaceExisting)
    {
        if (!File.Exists(path)) return new(false, 0, 0, 0, 0, "Il file selezionato non esiste.");
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (!TryReadDatabase(bytes, out var incoming, out var expected))
                return new(false, 0, 0, 0, 0, "Non è il formato 'Esporta database' di Word Search.");

            var existing = WordSearchWorkspaceService.GetRecords(project)
                .ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var updated = 0;
            var unchanged = 0;
            var conflicts = 0;

            foreach (var record in incoming)
            {
                record.SourceMaterialId = sourceMaterialId;
                if (!existing.TryGetValue(record.Id, out var current))
                {
                    WordSearchWorkspaceService.SaveRecord(project, record);
                    SetExpectedWordCount(project, record.Id, expected.TryGetValue(record.Id, out var e) ? e : Math.Max(20, record.Words.Count));
                    existing[record.Id] = record;
                    added++;
                    continue;
                }

                var same = string.Equals(current.Title?.Trim(), record.Title?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(current.Theme?.Trim(), record.Theme?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           current.Words.Select(NormalizeWord).SequenceEqual(record.Words.Select(NormalizeWord), StringComparer.OrdinalIgnoreCase);
                if (same)
                {
                    SetExpectedWordCount(project, current.Id, expected.TryGetValue(record.Id, out var e) ? e : ExpectedWordCount(project, current));
                    unchanged++;
                    continue;
                }

                if (!replaceExisting)
                {
                    conflicts++;
                    continue;
                }

                record.ContentId = current.ContentId;
                if (record.Order <= 0) record.Order = current.Order;
                record.Origin = string.IsNullOrWhiteSpace(record.Origin) ? "Reimportato" : record.Origin + " · reimportato";
                WordSearchWorkspaceService.SaveRecord(project, record);
                SetExpectedWordCount(project, record.Id, expected.TryGetValue(record.Id, out var replacementExpected) ? replacementExpected : Math.Max(20, record.Words.Count));
                updated++;
            }

            return new(true, added, updated, unchanged, conflicts,
                $"Riconosciuto database Word Search: {added} aggiunti · {updated} sostituiti per ID · {unchanged} già uguali" +
                (conflicts > 0 ? $" · {conflicts} conflitti lasciati invariati" : string.Empty) + ".");
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, 0, "Non riesco a leggere il database: " + ex.Message);
        }
    }

    public static bool TryReadDatabase(byte[] bytes, out List<WordSearchRecord> records, out Dictionary<string, int> expectedById)
    {
        records = [];
        expectedById = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null) return false;

            XDocument workbook;
            XDocument rels;
            using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
            using (var stream = relsEntry.Open()) rels = XDocument.Load(stream);
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

            var databaseSheet = workbook.Descendants(main + "sheet")
                .FirstOrDefault(s => string.Equals((string?)s.Attribute("name"), "DATABASE", StringComparison.OrdinalIgnoreCase));
            if (databaseSheet is null) return false;
            var rid = (string?)databaseSheet.Attribute(officeRel + "id");
            var target = rels.Descendants(packageRel + "Relationship")
                .FirstOrDefault(r => string.Equals((string?)r.Attribute("Id"), rid, StringComparison.Ordinal))?
                .Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target)) return false;
            var entryPath = target.Replace('\\', '/').TrimStart('/');
            if (!entryPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) entryPath = "xl/" + entryPath;
            var sheetEntry = archive.GetEntry(entryPath);
            if (sheetEntry is null) return false;
            XDocument sheet;
            using (var stream = sheetEntry.Open()) sheet = XDocument.Load(stream);
            var shared = ReadSharedStrings(archive, main);
            var matrix = ReadRows(sheet, main, shared);
            if (matrix.Count < 3 || matrix[0].Count < 2) return false;
            if (!string.Equals(Cell(matrix[0], 0).Trim(), "Campo", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Cell(matrix[0], 1).StartsWith("Puzzle ", StringComparison.OrdinalIgnoreCase)) return false;

            var fields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in matrix.Skip(1))
            {
                var name = Cell(row, 0).Trim();
                if (name.Length == 0) continue;
                fields[name] = row.Skip(1).ToList();
            }
            if (!fields.TryGetValue("ID", out var ids)) return false;
            var count = Math.Min(ids.Count, matrix[0].Count - 1);
            var wordRows = fields.Keys
                .Where(k => k.StartsWith("Parola ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => WordRowNumber(k))
                .ToList();

            for (var i = 0; i < count; i++)
            {
                var id = NormalizePuzzleId(Value(fields, "ID", i));
                if (string.IsNullOrWhiteSpace(id)) id = $"PUZ-{i + 1:D3}";
                var words = new List<string>();
                foreach (var wordRow in wordRows)
                {
                    var word = Value(fields, wordRow, i).Trim();
                    if (word.Length > 0) words.Add(word);
                }
                var expected = wordRows.Count;
                if (fields.TryGetValue("Numero parole previste", out _ ) &&
                    int.TryParse(Value(fields, "Numero parole previste", i), out var explicitExpected) && explicitExpected > 0)
                    expected = explicitExpected;
                if (expected <= 0) expected = Math.Max(20, words.Count);

                _ = int.TryParse(Value(fields, "Ordine", i), out var order);
                records.Add(new WordSearchRecord
                {
                    Order = order > 0 ? order : i + 1,
                    Id = id,
                    Title = Value(fields, "Titolo", i),
                    Theme = Value(fields, "Tema", i),
                    Words = words,
                    Status = string.IsNullOrWhiteSpace(Value(fields, "Stato", i)) ? WordSearchWorkspaceService.StatusToReview : Value(fields, "Stato", i),
                    Origin = string.IsNullOrWhiteSpace(Value(fields, "Origine", i)) ? "Reimportato" : Value(fields, "Origine", i),
                    Notes = Value(fields, "Note", i),
                    UpdatedAtLocal = Value(fields, "Aggiornato", i)
                });
                expectedById[id] = Math.Max(expected, words.Count);
            }
            return records.Count > 0;
        }
        catch
        {
            records = [];
            expectedById = new(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    private static string Value(Dictionary<string, List<string>> fields, string name, int index) =>
        fields.TryGetValue(name, out var values) && index >= 0 && index < values.Count ? values[index] : string.Empty;

    private static string NormalizePuzzleId(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? $"PUZ-{number:D3}" : trimmed.ToUpperInvariant();
    }

    private static int WordRowNumber(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    private static string NormalizeWord(string value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

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

    private static List<List<string>> ReadRows(XDocument document, XNamespace main, IReadOnlyList<string> shared)
    {
        var result = new List<List<string>>();
        foreach (var row in document.Descendants(main + "row"))
        {
            var values = new Dictionary<int, string>();
            var fallback = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                var index = string.IsNullOrWhiteSpace(reference) ? fallback : ColumnIndex(reference);
                fallback = index + 1;
                values[index] = ReadCell(cell, main, shared);
            }
            var max = values.Count == 0 ? 0 : values.Keys.Max() + 1;
            var valuesList = new List<string>(max);
            for (var i = 0; i < max; i++) valuesList.Add(values.TryGetValue(i, out var value) ? value : string.Empty);
            result.Add(valuesList);
        }
        return result;
    }

    private static string ReadCell(XElement cell, XNamespace main, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
        var raw = cell.Element(main + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < shared.Count) return shared[index];
        return raw;
    }

    private static int ColumnIndex(string reference)
    {
        var value = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            value = value * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, value - 1);
    }

    private static string Cell(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;
}