using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal sealed class WordSearchLexiconEntry
{
    public string Id { get; set; } = string.Empty;
    public string Word { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Subcategory { get; set; } = string.Empty;
    public string Series { get; set; } = string.Empty;
    public string Decade { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public double? Relevance { get; set; }
    public bool? KdpSafe { get; set; }
    public string Origin { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal readonly record struct WordSearchLexiconMergeResult(
    bool Recognized,
    int Added,
    int Updated,
    int Unchanged,
    string Message);

internal static class WordSearchLexiconService
{
    private const string NodeKind = "WordSearchSourceDatabase";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class StoredLexicon
    {
        public int SchemaVersion { get; set; } = WordSearchLexiconService.SchemaVersion;
        public List<WordSearchLexiconEntry> Entries { get; set; } = [];
    }

    public static List<WordSearchLexiconEntry> GetEntries(PreviewProject project)
    {
        var node = project.ContentNodes.FirstOrDefault(n => string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));
        if (node is null || string.IsNullOrWhiteSpace(node.Body)) return [];
        try
        {
            var stored = JsonSerializer.Deserialize<StoredLexicon>(node.Body, JsonOptions);
            return stored?.Entries ?? [];
        }
        catch { return []; }
    }

    public static void SetEntries(PreviewProject project, IEnumerable<WordSearchLexiconEntry> entries)
    {
        var normalized = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Word))
            .Select(NormalizeEntry)
            .OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Subcategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Decade, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Year, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var node = project.ContentNodes.FirstOrDefault(n => string.Equals(n.Kind, NodeKind, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new ContentNode
            {
                Kind = NodeKind,
                Title = "Database parole Word Search",
                SourceLocator = "word-search-database",
                Ordinal = 0
            };
            project.ContentNodes.Add(node);
        }
        node.Body = JsonSerializer.Serialize(new StoredLexicon { Entries = normalized }, JsonOptions);
    }

    public static async Task<WordSearchLexiconMergeResult> ImportXlsxAsync(PreviewProject project, string path, string origin)
    {
        if (!File.Exists(path)) return new(false, 0, 0, 0, "Il file del database non esiste.");
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (!TryParseWorkbook(bytes, origin, out var incoming))
                return new(false, 0, 0, 0, "Il file non sembra un database di parole: non trovo una colonna Parola/Word con righe classificabili.");
            return Merge(project, incoming, origin);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, "Non riesco a leggere il database di parole: " + ex.Message);
        }
    }

    public static WordSearchLexiconMergeResult ImportDelimitedText(PreviewProject project, string text, string origin)
    {
        if (!TryParseDelimited(text, origin, out var incoming))
            return new(false, 0, 0, 0, "I dati non sembrano un database di parole classificato.");
        return Merge(project, incoming, origin);
    }

    public static async Task<WordSearchLexiconMergeResult> CollectFromProjectAsync(PreviewProject project, string projectPath)
    {
        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var recognized = false;

        foreach (var material in project.Materials.Where(m =>
                     m.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                     m.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                     m.FileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var bytes = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, material);
                if (bytes is null && File.Exists(material.SourcePath)) bytes = await File.ReadAllBytesAsync(material.SourcePath);
                if (bytes is null) continue;

                WordSearchLexiconMergeResult result;
                if (material.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseWorkbook(bytes, "Importato", out var entries)) continue;
                    result = Merge(project, entries, "Importato");
                }
                else
                {
                    var text = DecodeText(bytes);
                    if (!TryParseDelimited(text, "Importato", out var entries)) continue;
                    result = Merge(project, entries, "Importato");
                }
                recognized = true;
                added += result.Added;
                updated += result.Updated;
                unchanged += result.Unchanged;
            }
            catch { }
        }

        foreach (var job in project.AiProductionJobs.Where(j =>
                     string.Equals(j.OutputType, AiProductionService.TypeData, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(j.Status, AiProductionService.StatusApproved, StringComparison.Ordinal) &&
                     !string.IsNullOrWhiteSpace(j.ResultText)))
        {
            if (!TryParseDelimited(job.ResultText, "Creato con AI", out var entries)) continue;
            var result = Merge(project, entries, "Creato con AI");
            recognized = true;
            added += result.Added;
            updated += result.Updated;
            unchanged += result.Unchanged;
        }

        return recognized
            ? new(true, added, updated, unchanged,
                $"Database parole raccolto: {GetEntries(project).Count} voci totali · {added} aggiunte · {updated} aggiornate · {unchanged} già presenti.")
            : new(false, 0, 0, 0, "Non ho trovato ancora un database di parole classificato nei materiali o nei dati AI approvati.");
    }

    public static IReadOnlyList<string> Categories(PreviewProject project) => GetEntries(project)
        .Select(e => e.Category).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> Subcategories(PreviewProject project, string? category = null) => GetEntries(project)
        .Where(e => string.IsNullOrWhiteSpace(category) || string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
        .Select(e => e.Subcategory).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    private static WordSearchLexiconMergeResult Merge(PreviewProject project, IEnumerable<WordSearchLexiconEntry> incoming, string origin)
    {
        var existing = GetEntries(project);
        var byId = existing.Where(e => !string.IsNullOrWhiteSpace(e.Id)).ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var bySignature = existing.ToDictionary(Signature, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var raw in incoming)
        {
            var entry = NormalizeEntry(raw);
            if (string.IsNullOrWhiteSpace(entry.Word)) continue;
            if (string.IsNullOrWhiteSpace(entry.Origin)) entry.Origin = origin;

            WordSearchLexiconEntry? current = null;
            if (!string.IsNullOrWhiteSpace(entry.Id)) byId.TryGetValue(entry.Id, out current);
            if (current is null) bySignature.TryGetValue(Signature(entry), out current);

            if (current is null)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = StableId(entry);
                existing.Add(entry);
                byId[entry.Id] = entry;
                bySignature[Signature(entry)] = entry;
                added++;
                continue;
            }

            if (Equivalent(current, entry))
            {
                unchanged++;
                continue;
            }

            var index = existing.IndexOf(current);
            entry.Id = string.IsNullOrWhiteSpace(current.Id) ? StableId(entry) : current.Id;
            entry.Origin = string.IsNullOrWhiteSpace(entry.Origin) ? current.Origin : entry.Origin;
            existing[index] = entry;
            byId[entry.Id] = entry;
            bySignature[Signature(entry)] = entry;
            updated++;
        }

        SetEntries(project, existing);
        return new(true, added, updated, unchanged,
            $"Database parole riconosciuto: {existing.Count} voci totali · {added} aggiunte · {updated} aggiornate · {unchanged} già uguali.");
    }

    private static bool TryParseWorkbook(byte[] bytes, string origin, out List<WordSearchLexiconEntry> entries)
    {
        entries = [];
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

            foreach (var sheet in workbook.Descendants(main + "sheet"))
            {
                var rid = (string?)sheet.Attribute(officeRel + "id");
                if (string.IsNullOrWhiteSpace(rid)) continue;
                var target = rels.Descendants(packageRel + "Relationship")
                    .FirstOrDefault(r => string.Equals((string?)r.Attribute("Id"), rid, StringComparison.Ordinal))?
                    .Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target)) continue;
                var path = target.Replace('\\', '/').TrimStart('/');
                if (!path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) path = "xl/" + path;
                var entry = archive.GetEntry(path);
                if (entry is null) continue;
                XDocument doc;
                using (var s = entry.Open()) doc = XDocument.Load(s);
                var rows = ReadRows(doc, main, shared);
                var parsed = ParseRows(rows, origin);
                if (parsed.Count > entries.Count) entries = parsed;
            }
            return entries.Count > 0;
        }
        catch
        {
            entries = [];
            return false;
        }
    }

    private static bool TryParseDelimited(string text, string origin, out List<WordSearchLexiconEntry> entries)
    {
        entries = [];
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Where(_ => true).ToString();
        var rawLines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (rawLines.Count < 2) return false;
        var delimiter = new[] { ';', '\t', ',' }.OrderByDescending(c => CountDelimiter(rawLines[0], c)).First();
        var rows = rawLines.Select(l => ParseDelimitedLine(l, delimiter)).ToList();
        entries = ParseRows(rows, origin);
        return entries.Count > 0;
    }

    private static List<WordSearchLexiconEntry> ParseRows(List<List<string>> rows, string origin)
    {
        if (rows.Count < 2) return [];
        var headerRow = rows.FindIndex(r => r.Any(v => IsWordHeader(NormalizeHeader(v))));
        if (headerRow < 0) return [];
        var headers = rows[headerRow];
        var normalizedHeaders = headers.Select(NormalizeHeader).ToList();
        var wordIndex = normalizedHeaders.FindIndex(IsWordHeader);
        if (wordIndex < 0) return [];

        int Find(params string[] aliases) => normalizedHeaders.FindIndex(h => aliases.Contains(h, StringComparer.OrdinalIgnoreCase));
        var idIndex = Find("id", "wordid", "recordid", "codice");
        var categoryIndex = Find("category", "categoria");
        var subcategoryIndex = Find("subcategory", "sottocategoria", "subcat");
        var seriesIndex = Find("series", "serie", "collection", "collezione");
        var decadeIndex = Find("decade", "decadeperiod", "decennio");
        var yearIndex = Find("year", "years", "anno", "anni");
        var relevanceIndex = Find("relevance", "relevance score", "relevance_score", "rilevanza", "nostalgia", "nostalgiascore", "score");
        var safeIndex = Find("kdpsafe", "kdp safe", "safe", "sicuro");

        string Cell(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? (row[index] ?? string.Empty).Trim() : string.Empty;
        var result = new List<WordSearchLexiconEntry>();
        foreach (var row in rows.Skip(headerRow + 1))
        {
            var word = Cell(row, wordIndex);
            if (string.IsNullOrWhiteSpace(word)) continue;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                var name = (headers[i] ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                fields[name] = Cell(row, i);
            }

            var relevanceText = Cell(row, relevanceIndex);
            double? relevance = double.TryParse(relevanceText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRel)
                ? parsedRel : null;
            var safeText = Cell(row, safeIndex);
            bool? safe = string.IsNullOrWhiteSpace(safeText) ? null : ParseBool(safeText);

            var entry = new WordSearchLexiconEntry
            {
                Id = Cell(row, idIndex),
                Word = word,
                Category = Cell(row, categoryIndex),
                Subcategory = Cell(row, subcategoryIndex),
                Series = Cell(row, seriesIndex),
                Decade = Cell(row, decadeIndex),
                Year = Cell(row, yearIndex),
                Relevance = relevance,
                KdpSafe = safe,
                Origin = origin,
                Fields = fields
            };
            if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = StableId(entry);
            result.Add(entry);
        }
        return result;
    }

    private static WordSearchLexiconEntry NormalizeEntry(WordSearchLexiconEntry entry)
    {
        entry.Word = Clean(entry.Word);
        entry.Category = Clean(entry.Category);
        entry.Subcategory = Clean(entry.Subcategory);
        entry.Series = Clean(entry.Series);
        entry.Decade = Clean(entry.Decade);
        entry.Year = Clean(entry.Year);
        entry.Id = Clean(entry.Id);
        entry.Origin = Clean(entry.Origin);
        entry.Fields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = StableId(entry);
        return entry;
    }

    private static bool Equivalent(WordSearchLexiconEntry a, WordSearchLexiconEntry b) =>
        string.Equals(NormalizeWord(a.Word), NormalizeWord(b.Word), StringComparison.Ordinal) &&
        Same(a.Category, b.Category) && Same(a.Subcategory, b.Subcategory) && Same(a.Series, b.Series) &&
        Same(a.Decade, b.Decade) && Same(a.Year, b.Year) && a.Relevance == b.Relevance && a.KdpSafe == b.KdpSafe;

    private static string Signature(WordSearchLexiconEntry e) => string.Join("|",
        NormalizeWord(e.Word), NormalizeKey(e.Category), NormalizeKey(e.Subcategory), NormalizeKey(e.Series), NormalizeKey(e.Decade), NormalizeKey(e.Year));

    private static string StableId(WordSearchLexiconEntry e)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Signature(e)));
        return "WORD-" + Convert.ToHexString(hash)[..12];
    }

    private static bool IsWordHeader(string header) => header is "word" or "words" or "parola" or "term" or "termine" or "keyword";
    private static string Clean(string? value) => string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string NormalizeWord(string? value) => Clean(value).ToUpperInvariant();
    private static string NormalizeKey(string? value) => Clean(value).ToUpperInvariant();
    private static bool Same(string? a, string? b) => string.Equals(NormalizeKey(a), NormalizeKey(b), StringComparison.Ordinal);

    private static bool ParseBool(string value)
    {
        var normalized = NormalizeKey(value);
        return normalized is "YES" or "Y" or "TRUE" or "1" or "SI" or "SÌ" or "OK" or "SAFE";
    }

    private static string NormalizeHeader(string? value)
    {
        var formD = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))).ToList();
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
            var list = new List<string>(max);
            for (var i = 0; i < max; i++) list.Add(values.TryGetValue(i, out var value) ? value : string.Empty);
            result.Add(list);
        }
        return result;
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
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == delimiter && !quoted) { values.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(ch);
        }
        values.Add(builder.ToString());
        return values;
    }

    private static string DecodeText(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch { return Encoding.GetEncoding(1252).GetString(bytes); }
    }
}
