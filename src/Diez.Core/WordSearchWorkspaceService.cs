using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal sealed class WordSearchRecord
{
    public Guid ContentId { get; set; }
    public Guid SourceMaterialId { get; set; }
    public int Order { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public List<string> Words { get; set; } = [];
    public string Status { get; set; } = WordSearchWorkspaceService.StatusToReview;
    public string Origin { get; set; } = "Importato";
    public string Notes { get; set; } = string.Empty;
    public string UpdatedAtLocal { get; set; } = string.Empty;
}

internal readonly record struct WordSearchMergeResult(
    bool Recognized,
    int Added,
    int Updated,
    int Unchanged,
    int Conflicts,
    string Message);

internal sealed record WordSearchIssueSummary(
    int DuplicateWordsInside,
    int WordsUsedElsewhere,
    bool MissingTitle,
    bool MissingTheme,
    bool TooFewWords,
    IReadOnlyList<string> Messages)
{
    public bool HasProblems => DuplicateWordsInside > 0 || MissingTitle || MissingTheme || TooFewWords;
}

internal static class WordSearchWorkspaceService
{
    public const string NodeKind = "WordSearchPuzzle";
    public const string StatusToReview = "Da controllare";
    public const string StatusApproved = "Approvato";
    public const string StatusNeedsRevision = "Da rifare";

    private const int FormatVersion = 1;
    private static readonly Regex PuzzleIdRegex = new(@"(?:PUZ[-_ ]*)?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StoredPuzzle
    {
        public int SchemaVersion { get; set; } = FormatVersion;
        public string Theme { get; set; } = string.Empty;
        public List<string> Words { get; set; } = [];
        public string Status { get; set; } = StatusToReview;
        public string Origin { get; set; } = "Importato";
        public string Notes { get; set; } = string.Empty;
        public string UpdatedAtLocal { get; set; } = string.Empty;
    }

    private sealed record ParsedTable(string SheetName, List<string> Headers, List<List<string>> Rows);

    public static bool HasWordSearchDatabase(PreviewProject project) =>
        project.ContentNodes.Any(n => string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));

    public static List<WordSearchRecord> GetRecords(PreviewProject project) =>
        project.ContentNodes
            .Where(n => string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase))
            .Select(ToRecord)
            .OrderBy(r => r.Order <= 0 ? int.MaxValue : r.Order)
            .ThenBy(r => PuzzleNumber(r.Id))
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static WordSearchRecord AddNew(PreviewProject project)
    {
        var next = Math.Max(1, GetRecords(project).Select(r => PuzzleNumber(r.Id)).DefaultIfEmpty(0).Max() + 1);
        var record = new WordSearchRecord
        {
            ContentId = Guid.NewGuid(),
            Order = GetRecords(project).Select(r => r.Order).DefaultIfEmpty(0).Max() + 1,
            Id = $"PUZ-{next:D3}",
            Title = $"Puzzle {next:D3}",
            Status = StatusToReview,
            Origin = "Creato in Diez",
            UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
        };
        SaveRecord(project, record);
        return record;
    }

    public static void SaveRecord(PreviewProject project, WordSearchRecord record)
    {
        record.Id = EnsureId(project, record.Id, record.ContentId);
        record.Order = record.Order <= 0 ? NextOrder(project, record.ContentId) : record.Order;
        record.Title = (record.Title ?? string.Empty).Trim();
        record.Theme = (record.Theme ?? string.Empty).Trim();
        record.Words = NormalizeWords(record.Words, removeDuplicates: false);
        record.Status = NormalizeStatus(record.Status);
        record.Origin = string.IsNullOrWhiteSpace(record.Origin) ? "Modificato in Diez" : record.Origin.Trim();
        record.Notes = (record.Notes ?? string.Empty).Trim();
        record.UpdatedAtLocal = DateTimeOffset.Now.ToString("O");

        var node = record.ContentId == Guid.Empty
            ? null
            : project.ContentNodes.FirstOrDefault(n => n.ContentId == record.ContentId && string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new ContentNode { ContentId = record.ContentId == Guid.Empty ? Guid.NewGuid() : record.ContentId };
            project.ContentNodes.Add(node);
            record.ContentId = node.ContentId;
        }

        node.MaterialId = record.SourceMaterialId;
        node.Kind = NodeKind;
        node.Title = record.Title;
        node.Ordinal = record.Order;
        node.SourceLocator = record.Id;
        node.Body = JsonSerializer.Serialize(new StoredPuzzle
        {
            SchemaVersion = FormatVersion,
            Theme = record.Theme,
            Words = record.Words,
            Status = record.Status,
            Origin = record.Origin,
            Notes = record.Notes,
            UpdatedAtLocal = record.UpdatedAtLocal
        }, JsonOptions);
    }

    public static void DeleteRecord(PreviewProject project, Guid contentId) =>
        project.ContentNodes.RemoveAll(n => n.ContentId == contentId && string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));

    public static void NormalizeSelectedWords(PreviewProject project, WordSearchRecord record, bool removeDuplicates)
    {
        record.Words = NormalizeWords(record.Words.Select(w => w.ToUpperInvariant()), removeDuplicates);
        record.Origin = AppendModified(record.Origin);
        SaveRecord(project, record);
    }

    public static WordSearchIssueSummary Analyze(PreviewProject project, WordSearchRecord record)
    {
        var normalized = record.Words.Select(NormalizeWordKey).Where(w => w.Length > 0).ToList();
        var duplicateInside = normalized.GroupBy(w => w, StringComparer.OrdinalIgnoreCase).Sum(g => Math.Max(0, g.Count() - 1));
        var otherWords = GetRecords(project)
            .Where(r => r.ContentId != record.ContentId)
            .SelectMany(r => r.Words)
            .Select(NormalizeWordKey)
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedElsewhere = normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count(otherWords.Contains);
        var messages = new List<string>();
        var missingTitle = string.IsNullOrWhiteSpace(record.Title);
        var missingTheme = string.IsNullOrWhiteSpace(record.Theme);
        var tooFew = record.Words.Count < 5;
        if (missingTitle) messages.Add("Manca il titolo.");
        if (missingTheme) messages.Add("Manca il tema.");
        if (tooFew) messages.Add($"Ci sono solo {record.Words.Count} parole.");
        if (duplicateInside > 0) messages.Add($"{duplicateInside} parole duplicate dentro questo puzzle.");
        if (usedElsewhere > 0) messages.Add($"{usedElsewhere} parole compaiono anche in altri puzzle.");
        if (messages.Count == 0) messages.Add("Nessun problema evidente nei dati del puzzle.");
        return new WordSearchIssueSummary(duplicateInside, usedElsewhere, missingTitle, missingTheme, tooFew, messages);
    }

    public static async Task<WordSearchMergeResult> ImportXlsxFileAsync(
        PreviewProject project,
        string path,
        Guid sourceMaterialId,
        bool replaceExisting)
    {
        if (!File.Exists(path)) return new(false, 0, 0, 0, 0, "Il file XLSX non esiste.");
        var bytes = await File.ReadAllBytesAsync(path);
        return ImportXlsxBytes(project, bytes, sourceMaterialId, replaceExisting);
    }

    public static WordSearchMergeResult ImportXlsxBytes(
        PreviewProject project,
        byte[] bytes,
        Guid sourceMaterialId,
        bool replaceExisting)
    {
        if (!TryReadBestWordSearchTable(bytes, out var table))
            return new(false, 0, 0, 0, 0, "Il file non sembra un database Word Search: non trovo una tabella con puzzle e parole.");
        var incoming = ParseRecords(table!, sourceMaterialId, "Importato");
        if (incoming.Count == 0)
            return new(false, 0, 0, 0, 0, "La struttura sembra Word Search, ma non contiene puzzle utilizzabili.");
        return Merge(project, incoming, replaceExisting, $"Riconosciuto database Word Search nel foglio '{table!.SheetName}'.");
    }

    public static async Task<WordSearchMergeResult> CollectFromProjectAsync(PreviewProject project, string projectPath)
    {
        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var conflicts = 0;
        var recognized = false;

        foreach (var material in project.Materials.Where(m => m.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            byte[]? bytes = null;
            try
            {
                bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null && File.Exists(material.SourcePath)) bytes = await File.ReadAllBytesAsync(material.SourcePath);
            }
            catch { }
            if (bytes is null) continue;
            var result = ImportXlsxBytes(project, bytes, material.MaterialId, replaceExisting: false);
            if (!result.Recognized) continue;
            recognized = true;
            added += result.Added;
            updated += result.Updated;
            unchanged += result.Unchanged;
            conflicts += result.Conflicts;
        }

        foreach (var job in project.AiProductionJobs.Where(j =>
                     string.Equals(j.OutputType, AiProductionService.TypeData, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal) &&
                     !string.IsNullOrWhiteSpace(j.ResultText)))
        {
            if (!TryParseDelimitedTable(job.ResultText, out var table)) continue;
            var incoming = ParseRecords(table!, Guid.Empty, "Creato con AI");
            if (incoming.Count == 0) continue;
            recognized = true;
            var result = Merge(project, incoming, replaceExisting: false, $"Dati AI {job.Code}");
            added += result.Added;
            updated += result.Updated;
            unchanged += result.Unchanged;
            conflicts += result.Conflicts;
        }

        var message = recognized
            ? $"Database raccolto: {added} nuovi puzzle · {unchanged} già presenti" + (conflicts > 0 ? $" · {conflicts} ID con dati diversi lasciati invariati" : string.Empty) + "."
            : "Non ho trovato ancora un database Word Search riconoscibile nei materiali XLSX o nei dati AI approvati.";
        return new(recognized, added, updated, unchanged, conflicts, message);
    }

    public static async Task<AiProductionActionResult> ExportXlsxAsync(PreviewProject project, string path)
    {
        var records = GetRecords(project);
        if (records.Count == 0) return new(false, "Non ci sono puzzle da esportare.");
        var fullPath = EnsureExtension(path, ".xlsx");
        EnsureDirectory(fullPath);
        var temp = fullPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        var maxWords = Math.Max(1, records.Max(r => r.Words.Count));

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteEntry(archive, "[Content_Types].xml", ContentTypes());
            await WriteEntry(archive, "_rels/.rels", RootRels());
            await WriteEntry(archive, "xl/workbook.xml", Workbook());
            await WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            await WriteEntry(archive, "xl/styles.xml", Styles());
            await WriteEntry(archive, "xl/worksheets/sheet1.xml", PuzzleSheet(records, maxWords));
            await WriteEntry(archive, "xl/worksheets/sheet2.xml", InfoSheet(project, records.Count));
        }
        File.Move(temp, fullPath, true);
        return new(true, $"Database Word Search esportato: {Path.GetFileName(fullPath)} · {records.Count} puzzle.");
    }

    public static string SuggestedFileName(PreviewProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.EditionMetadata?.Title) ? project.Name : project.EditionMetadata.Title;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat((title ?? "word-search").Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return (string.IsNullOrWhiteSpace(safe) ? "word-search" : safe) + "-database-completo.xlsx";
    }

    private static WordSearchRecord ToRecord(ContentNode node)
    {
        StoredPuzzle payload;
        try { payload = JsonSerializer.Deserialize<StoredPuzzle>(node.Body ?? string.Empty, JsonOptions) ?? new StoredPuzzle(); }
        catch { payload = new StoredPuzzle { Notes = node.Body ?? string.Empty }; }
        return new WordSearchRecord
        {
            ContentId = node.ContentId,
            SourceMaterialId = node.MaterialId,
            Order = node.Ordinal,
            Id = NormalizeId(node.SourceLocator),
            Title = node.Title ?? string.Empty,
            Theme = payload.Theme ?? string.Empty,
            Words = payload.Words ?? [],
            Status = NormalizeStatus(payload.Status),
            Origin = payload.Origin ?? string.Empty,
            Notes = payload.Notes ?? string.Empty,
            UpdatedAtLocal = payload.UpdatedAtLocal ?? string.Empty
        };
    }

    private static WordSearchMergeResult Merge(PreviewProject project, IEnumerable<WordSearchRecord> records, bool replaceExisting, string prefix)
    {
        var existing = GetRecords(project).ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var conflicts = 0;
        var seenIncoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextNumber = Math.Max(1, existing.Keys.Select(PuzzleNumber).DefaultIfEmpty(0).Max() + 1);
        var nextOrder = Math.Max(1, existing.Values.Select(r => r.Order).DefaultIfEmpty(0).Max() + 1);

        foreach (var raw in records)
        {
            var record = raw;
            if (string.IsNullOrWhiteSpace(record.Id)) record.Id = $"PUZ-{nextNumber++:D3}";
            else record.Id = NormalizeId(record.Id);
            if (string.IsNullOrWhiteSpace(record.Id)) record.Id = $"PUZ-{nextNumber++:D3}";
            if (!seenIncoming.Add(record.Id)) { conflicts++; continue; }
            if (record.Order <= 0) record.Order = nextOrder++;

            if (!existing.TryGetValue(record.Id, out var current))
            {
                SaveRecord(project, record);
                existing[record.Id] = record;
                added++;
                continue;
            }

            if (Equivalent(current, record)) { unchanged++; continue; }
            if (!replaceExisting) { conflicts++; continue; }
            record.ContentId = current.ContentId;
            if (record.Order <= 0) record.Order = current.Order;
            record.Origin = AppendModified(record.Origin);
            SaveRecord(project, record);
            existing[record.Id] = record;
            updated++;
        }

        return new(true, added, updated, unchanged, conflicts,
            $"{prefix} {added} aggiunti · {updated} sostituiti per ID · {unchanged} già uguali" + (conflicts > 0 ? $" · {conflicts} conflitti non applicati" : string.Empty) + ".");
    }

    private static bool Equivalent(WordSearchRecord a, WordSearchRecord b) =>
        string.Equals(a.Title?.Trim(), b.Title?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Theme?.Trim(), b.Theme?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        a.Words.Select(NormalizeWordKey).SequenceEqual(b.Words.Select(NormalizeWordKey), StringComparer.OrdinalIgnoreCase);

    private static List<WordSearchRecord> ParseRecords(ParsedTable table, Guid sourceMaterialId, string defaultOrigin)
    {
        var map = table.Headers.Select((h, i) => (Header: NormalizeHeader(h), Index: i)).ToList();
        int Find(params string[] names) => map.FirstOrDefault(x => names.Contains(x.Header, StringComparer.OrdinalIgnoreCase)).Index;
        int FindOptional(params string[] names)
        {
            var found = map.FirstOrDefault(x => names.Contains(x.Header, StringComparer.OrdinalIgnoreCase));
            return found.Header is null ? -1 : found.Index;
        }

        var idIndex = FindOptional("id", "puzzleid", "codice", "codicepuzzle");
        var orderIndex = FindOptional("ordine", "order", "numero", "n");
        var titleIndex = FindOptional("titolo", "title", "puzzletitle", "nomepuzzle", "nome");
        var themeIndex = FindOptional("tema", "theme", "categoria", "category", "argomento");
        var statusIndex = FindOptional("stato", "status");
        var originIndex = FindOptional("origine", "origin", "source");
        var notesIndex = FindOptional("note", "notes", "annotazioni");
        var updatedIndex = FindOptional("aggiornato", "updated", "modificato");
        var compactWordsIndex = FindOptional("parole", "words", "wordlist", "listaparole");
        var wordIndexes = map.Where(x => x.Header.StartsWith("parola", StringComparison.OrdinalIgnoreCase) || x.Header.StartsWith("word", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Index != compactWordsIndex)
            .Select(x => x.Index).Distinct().Order().ToList();

        string Cell(List<string> row, int index) => index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
        var list = new List<WordSearchRecord>();
        var fallbackOrder = 1;
        foreach (var row in table.Rows)
        {
            var title = Cell(row, titleIndex);
            var theme = Cell(row, themeIndex);
            var id = Cell(row, idIndex);
            var words = new List<string>();
            if (compactWordsIndex >= 0) words.AddRange(SplitWords(Cell(row, compactWordsIndex)));
            foreach (var index in wordIndexes)
            {
                var value = Cell(row, index);
                if (!string.IsNullOrWhiteSpace(value)) words.Add(value);
            }
            words = NormalizeWords(words, removeDuplicates: false);
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(theme) && words.Count == 0) continue;
            _ = int.TryParse(Cell(row, orderIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order);
            list.Add(new WordSearchRecord
            {
                SourceMaterialId = sourceMaterialId,
                Order = order > 0 ? order : fallbackOrder,
                Id = NormalizeId(id),
                Title = title,
                Theme = theme,
                Words = words,
                Status = NormalizeStatus(Cell(row, statusIndex)),
                Origin = string.IsNullOrWhiteSpace(Cell(row, originIndex)) ? defaultOrigin : Cell(row, originIndex),
                Notes = Cell(row, notesIndex),
                UpdatedAtLocal = Cell(row, updatedIndex)
            });
            fallbackOrder++;
        }
        return list;
    }

    private static bool TryReadBestWordSearchTable(byte[] bytes, out ParsedTable? best)
    {
        best = null;
        try
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null) return false;
            XDocument workbook;
            XDocument rels;
            using (var s = workbookEntry.Open()) workbook = XDocument.Load(s);
            using (var s = relsEntry.Open()) rels = XDocument.Load(s);
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
            var shared = ReadSharedStrings(archive, main);
            var tables = new List<ParsedTable>();
            foreach (var sheet in workbook.Descendants(main + "sheet"))
            {
                var name = (string?)sheet.Attribute("name") ?? "Foglio";
                var rid = (string?)sheet.Attribute(officeRel + "id");
                if (string.IsNullOrWhiteSpace(rid)) continue;
                var target = rels.Descendants(packageRel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == rid)?.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target)) continue;
                var path = target.Replace('\\', '/').TrimStart('/');
                if (!path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) path = "xl/" + path;
                var entry = archive.GetEntry(path);
                if (entry is null) continue;
                XDocument doc;
                using (var s = entry.Open()) doc = XDocument.Load(s);
                var matrix = ReadSheetRows(doc, main, shared);
                var first = matrix.FindIndex(r => r.Any(v => !string.IsNullOrWhiteSpace(v)));
                if (first < 0) continue;
                var headers = matrix[first];
                var rows = matrix.Skip(first + 1).ToList();
                tables.Add(new ParsedTable(name, headers, rows));
            }
            best = tables.OrderByDescending(ScoreTable).FirstOrDefault();
            return best is not null && ScoreTable(best) >= 4;
        }
        catch { return false; }
    }

    private static int ScoreTable(ParsedTable table)
    {
        var headers = table.Headers.Select(NormalizeHeader).ToList();
        var score = 0;
        if (headers.Any(h => h is "id" or "puzzleid" or "codice" or "codicepuzzle")) score += 2;
        if (headers.Any(h => h is "titolo" or "title" or "puzzletitle" or "nomepuzzle")) score++;
        if (headers.Any(h => h is "tema" or "theme" or "categoria" or "category" or "argomento")) score++;
        if (headers.Any(h => h is "parole" or "words" or "wordlist" or "listaparole" || h.StartsWith("parola") || h.StartsWith("word"))) score += 3;
        if (table.SheetName.Contains("puzzle", StringComparison.OrdinalIgnoreCase) || table.SheetName.Contains("word", StringComparison.OrdinalIgnoreCase)) score++;
        return score;
    }

    private static bool TryParseDelimitedTable(string text, out ParsedTable? table)
    {
        table = null;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count < 2) return false;
        var delimiter = new[] { ';', '\t', ',' }.OrderByDescending(c => CountDelimiter(lines[0], c)).First();
        var rows = lines.Select(l => ParseDelimitedLine(l, delimiter)).ToList();
        table = new ParsedTable("Dati AI", rows[0], rows.Skip(1).ToList());
        return ScoreTable(table) >= 4;
    }

    private static List<List<string>> ReadSheetRows(XDocument document, XNamespace main, IReadOnlyList<string> shared)
    {
        var matrix = new List<List<string>>();
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
            var list = new List<string>(max);
            for (var i = 0; i < max; i++) list.Add(values.TryGetValue(i, out var value) ? value : string.Empty);
            matrix.Add(list);
        }
        return matrix;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))).ToList();
    }

    private static string ReadCell(XElement cell, XNamespace main, IReadOnlyList<string> shared)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr") return string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
        var raw = cell.Element(main + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < shared.Count) return shared[index];
        if (type == "b") return raw == "1" ? "TRUE" : "FALSE";
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

    private static List<string> SplitWords(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split(new[] { '|', ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static List<string> NormalizeWords(IEnumerable<string>? words, bool removeDuplicates)
    {
        var result = (words ?? []).Select(w => (w ?? string.Empty).Trim()).Where(w => w.Length > 0).ToList();
        return removeDuplicates ? result.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : result;
    }

    private static string NormalizeWordKey(string value) => Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ").ToUpperInvariant();

    private static string NormalizeStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Contains("approv", StringComparison.OrdinalIgnoreCase) || value.Equals("ok", StringComparison.OrdinalIgnoreCase)) return StatusApproved;
        if (value.Contains("rif", StringComparison.OrdinalIgnoreCase) || value.Contains("revision", StringComparison.OrdinalIgnoreCase)) return StatusNeedsRevision;
        return StatusToReview;
    }

    private static string NormalizeId(string? id)
    {
        var value = (id ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        var match = PuzzleIdRegex.Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? $"PUZ-{number:D3}" : value.ToUpperInvariant();
    }

    private static int PuzzleNumber(string? id)
    {
        var match = PuzzleIdRegex.Match(id ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : int.MaxValue;
    }

    private static string EnsureId(PreviewProject project, string id, Guid currentContentId)
    {
        var normalized = NormalizeId(id);
        if (normalized.Length > 0 && !project.ContentNodes.Any(n => n.ContentId != currentContentId && string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase) && string.Equals(NormalizeId(n.SourceLocator), normalized, StringComparison.OrdinalIgnoreCase))) return normalized;
        var next = Math.Max(1, GetRecords(project).Where(r => r.ContentId != currentContentId).Select(r => PuzzleNumber(r.Id)).Where(n => n != int.MaxValue).DefaultIfEmpty(0).Max() + 1);
        return $"PUZ-{next:D3}";
    }

    private static int NextOrder(PreviewProject project, Guid currentContentId) =>
        Math.Max(1, GetRecords(project).Where(r => r.ContentId != currentContentId).Select(r => r.Order).DefaultIfEmpty(0).Max() + 1);

    private static string AppendModified(string? origin)
    {
        var value = string.IsNullOrWhiteSpace(origin) ? "Modificato in Diez" : origin.Trim();
        return value.Contains("modificat", StringComparison.OrdinalIgnoreCase) ? value : value + " · modificato";
    }

    private static string NormalizeHeader(string? value)
    {
        var formD = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) b.Append(char.ToLowerInvariant(ch));
        }
        return b.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int CountDelimiter(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == delimiter && !quoted) count++;
        }
        return count;
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var b = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { b.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == delimiter && !quoted) { values.Add(b.ToString()); b.Clear(); }
            else b.Append(ch);
        }
        values.Add(b.ToString());
        return values;
    }

    private static string PuzzleSheet(IReadOnlyList<WordSearchRecord> records, int maxWords)
    {
        var headers = new List<string> { "Ordine", "ID", "Titolo", "Tema", "Numero parole" };
        headers.AddRange(Enumerable.Range(1, maxWords).Select(i => $"Parola {i:D2}"));
        headers.AddRange(["Stato", "Origine", "Note", "Aggiornato"]);
        var rows = new List<IReadOnlyList<string>> { headers };
        foreach (var record in records)
        {
            var row = new List<string>
            {
                record.Order.ToString(CultureInfo.InvariantCulture), record.Id, record.Title, record.Theme,
                record.Words.Count.ToString(CultureInfo.InvariantCulture)
            };
            for (var i = 0; i < maxWords; i++) row.Add(i < record.Words.Count ? record.Words[i] : string.Empty);
            row.AddRange([record.Status, record.Origin, record.Notes, record.UpdatedAtLocal]);
            rows.Add(row);
        }
        return Worksheet(rows);
    }

    private static string InfoSheet(PreviewProject project, int count) => Worksheet(new List<IReadOnlyList<string>>
    {
        new[] { "Informazione", "Valore" },
        new[] { "Tipo di archivio", "Word Search" },
        new[] { "Versione formato", FormatVersion.ToString(CultureInfo.InvariantCulture) },
        new[] { "Titolo progetto", project.EditionMetadata?.Title ?? project.Name },
        new[] { "Puzzle presenti", count.ToString(CultureInfo.InvariantCulture) },
        new[] { "Nota", "Il foglio PUZZLE è il database completo e può essere reimportato in Diez." }
    });

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
                    new XElement(x + "is", new XElement(x + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? string.Empty))));
            }
            data.Add(row);
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "worksheet",
            new XElement(x + "sheetViews", new XElement(x + "sheetView", new XAttribute("workbookViewId", "0"), new XElement(x + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
            data)));
    }

    private static string ContentTypes()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/content-types";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Types",
            new XElement(x + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(x + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/worksheets/sheet2.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(x + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")))));
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
            new XElement(x + "sheets",
                new XElement(x + "sheet", new XAttribute("name", "PUZZLE"), new XAttribute("sheetId", "1"), new XAttribute(r + "id", "rId1")),
                new XElement(x + "sheet", new XAttribute("name", "INFO"), new XAttribute("sheetId", "2"), new XAttribute(r + "id", "rId2"))))));
    }

    private static string WorkbookRels()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(x + "Relationships",
            new XElement(x + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(x + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet2.xml")),
            new XElement(x + "Relationship", new XAttribute("Id", "rId3"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")))));
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
            new XElement(x + "cellXfs", new XAttribute("count", "2"), new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")), new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1"))),
            new XElement(x + "cellStyles", new XAttribute("count", "1"), new XElement(x + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))))));
    }

    private static async Task WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
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