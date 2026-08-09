using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace DiezPublishingStudio;

internal sealed record CrosswordDefinitionRow(
    string Word,
    string Definition1,
    string Definition2,
    string Definition3,
    string Definition4,
    string Notes,
    string Approved = "");

internal readonly record struct CrosswordImportResult(int Added, int Existing, int Ignored);
internal readonly record struct CrosswordDefinitionImportResult(int Rows, int WordsCreated, int DefinitionsImported);

internal static class CrosswordService
{
    private const string WordKind = "CrosswordWord";
    private const string SettingKind = "CrosswordSetting";
    private const string D1 = "crossword_definition_1";
    private const string D2 = "crossword_definition_2";
    private const string D3 = "crossword_definition_3";
    private const string D4 = "crossword_definition_4";
    private const string Note = "crossword_note";
    private const string Approved = "crossword_approved";

    public static IReadOnlyList<GraphEntity> Words(PreviewProject project) => project.Entities
        .Where(e => string.Equals(e.Kind, WordKind, StringComparison.OrdinalIgnoreCase))
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static GraphEntity? FindWord(PreviewProject project, string? value)
    {
        var word = NormalizeGridWord(value);
        if (word.Length == 0) return null;
        return project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, WordKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, word, StringComparison.OrdinalIgnoreCase));
    }

    public static GraphEntity EnsureWord(PreviewProject project, string value, string? source = null)
    {
        var word = NormalizeGridWord(value);
        if (word.Length == 0) throw new ArgumentException("Parola non valida.", nameof(value));
        var entity = FindWord(project, word);
        if (entity is not null) return entity;
        entity = new GraphEntity
        {
            EntityId = Guid.NewGuid(),
            Kind = WordKind,
            Name = word,
            IsCandidate = false,
            Notes = string.IsNullOrWhiteSpace(source) ? "Vocabolario cruciverba" : $"Vocabolario cruciverba · fonte: {source}"
        };
        project.Entities.Add(entity);
        return entity;
    }

    public static async Task<CrosswordImportResult> ImportWordListAsync(PreviewProject project, string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        var isDic = string.Equals(Path.GetExtension(path), ".dic", StringComparison.OrdinalIgnoreCase);
        var added = 0;
        var existing = 0;
        var ignored = 0;
        var start = 0;
        if (isDic && lines.Length > 0 && int.TryParse(lines[0].Trim(), out _)) start = 1;

        for (var i = start; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#')) { ignored++; continue; }
            if (isDic)
            {
                var slash = raw.IndexOf('/');
                if (slash >= 0) raw = raw[..slash];
            }
            var word = NormalizeGridWord(raw);
            if (word.Length < 2) { ignored++; continue; }
            if (FindWord(project, word) is not null) { existing++; continue; }
            EnsureWord(project, word, Path.GetFileName(path));
            added++;
        }
        return new CrosswordImportResult(added, existing, ignored);
    }

    public static async Task ExportQxwTextAsync(PreviewProject project, string path)
    {
        var words = Words(project).Select(e => e.Name).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList();
        await File.WriteAllLinesAsync(path, words, new UTF8Encoding(false));
    }

    public static IReadOnlyList<CrosswordDefinitionRow> DefinitionRows(PreviewProject project) =>
        Words(project).Select(entity => new CrosswordDefinitionRow(
            entity.Name,
            GetValue(project, entity.EntityId, D1),
            GetValue(project, entity.EntityId, D2),
            GetValue(project, entity.EntityId, D3),
            GetValue(project, entity.EntityId, D4),
            GetValue(project, entity.EntityId, Note),
            GetValue(project, entity.EntityId, Approved))).ToList();

    public static void SetDefinitionCell(PreviewProject project, Guid wordId, int definitionIndex, string? value)
    {
        var key = definitionIndex switch { 1 => D1, 2 => D2, 3 => D3, 4 => D4, _ => throw new ArgumentOutOfRangeException(nameof(definitionIndex)) };
        SetValue(project, wordId, key, value, "Proposed");
    }

    public static void SetNotes(PreviewProject project, Guid wordId, string? value) => SetValue(project, wordId, Note, value, "Proposed");
    public static void SetApproved(PreviewProject project, Guid wordId, string? value) => SetValue(project, wordId, Approved, value, "Binding");

    public static int MissingDefinitions(PreviewProject project) => DefinitionRows(project).Count(r =>
        string.IsNullOrWhiteSpace(r.Definition1) && string.IsNullOrWhiteSpace(r.Definition2) &&
        string.IsNullOrWhiteSpace(r.Definition3) && string.IsNullOrWhiteSpace(r.Definition4));

    public static string GetSetting(PreviewProject project, string key, string defaultValue = "") =>
        project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, SettingKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase))?.Notes ?? defaultValue;

    public static void SetSetting(PreviewProject project, string key, string? value)
    {
        var entity = project.Entities.FirstOrDefault(e =>
            string.Equals(e.Kind, SettingKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            project.Entities.Add(new GraphEntity { Kind = SettingKind, Name = key, Notes = value?.Trim() ?? string.Empty, IsCandidate = false });
        }
        else entity.Notes = value?.Trim() ?? string.Empty;
    }

    public static string BuildDefinitionPrompt(PreviewProject project)
    {
        var language = GetSetting(project, "PrimaryLanguage", "Italiano");
        var theme = GetSetting(project, "Theme", "Generico");
        var wordCount = Words(project).Count;
        return $"""
Usa il file XLSX allegato come elenco vincolante di {wordCount} parole per un cruciverba.
Lingua principale del cruciverba: {language}.
Tema: {theme}.

Per ogni riga:
- NON modificare la colonna PAROLA;
- compila DEFINIZIONE 1, DEFINIZIONE 2, DEFINIZIONE 3 e DEFINIZIONE 4 con possibili definizioni da cruciverba;
- proponi significati diversi quando la stessa parola è ambigua;
- per nomi propri, sigle, termini tecnici o parole straniere usa definizioni comprensibili nella lingua principale;
- se il tema rende un significato più pertinente, privilegialo senza inventare fatti;
- non inserire la soluzione dentro la definizione e non renderla banalmente riconoscibile ripetendola;
- se non sei sicuro, scrivilo in NOTE invece di inventare;
- restituisci un file XLSX mantenendo esattamente le colonne PAROLA, DEFINIZIONE 1, DEFINIZIONE 2, DEFINIZIONE 3, DEFINIZIONE 4, NOTE.
""";
    }

    public static async Task WriteDefinitionTemplateXlsxAsync(PreviewProject project, string path) =>
        await WriteDefinitionWorkbookAsync(path, Words(project).Select(w => new CrosswordDefinitionRow(w.Name, "", "", "", "", "")).ToList());

    internal static async Task WriteDefinitionWorkbookAsync(string path, IReadOnlyList<CrosswordDefinitionRow> rows)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);
        await using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntry(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
        await WriteEntry(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        await WriteEntry(zip, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets><sheet name="DEFINIZIONI" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
        await WriteEntry(zip, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""");

        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        var headers = new[] { "PAROLA", "DEFINIZIONE 1", "DEFINIZIONE 2", "DEFINIZIONE 3", "DEFINIZIONE 4", "NOTE" };
        AppendRow(xml, 1, headers);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            AppendRow(xml, i + 2, [row.Word, row.Definition1, row.Definition2, row.Definition3, row.Definition4, row.Notes]);
        }
        xml.Append("</sheetData></worksheet>");
        await WriteEntry(zip, "xl/worksheets/sheet1.xml", xml.ToString());
    }

    public static async Task<CrosswordDefinitionImportResult> ImportDefinitionsXlsxAsync(PreviewProject project, string path)
    {
        var rows = ReadXlsx(path);
        var rowCount = 0;
        var created = 0;
        var definitions = 0;
        foreach (var cells in rows)
        {
            if (!cells.TryGetValue("PAROLA", out var sourceWord)) continue;
            var word = NormalizeGridWord(sourceWord);
            if (word.Length < 2) continue;
            var entity = FindWord(project, word);
            if (entity is null) { entity = EnsureWord(project, word, Path.GetFileName(path)); created++; }
            for (var i = 1; i <= 4; i++)
            {
                if (!cells.TryGetValue($"DEFINIZIONE {i}", out var definition) || string.IsNullOrWhiteSpace(definition)) continue;
                SetDefinitionCell(project, entity.EntityId, i, definition);
                definitions++;
            }
            if (cells.TryGetValue("NOTE", out var note) && !string.IsNullOrWhiteSpace(note)) SetNotes(project, entity.EntityId, note);
            if (cells.TryGetValue("APPROVATA", out var approved) && !string.IsNullOrWhiteSpace(approved)) SetApproved(project, entity.EntityId, approved);
            rowCount++;
        }
        await Task.CompletedTask;
        return new CrosswordDefinitionImportResult(rowCount, created, definitions);
    }

    public static string NormalizeGridWord(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToUpperInvariant())
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        return builder.ToString();
    }

    private static string GetValue(PreviewProject project, Guid wordId, string key) =>
        project.BibleEntries.FirstOrDefault(b => b.SubjectEntityId == wordId && b.IsActive &&
            string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    private static void SetValue(PreviewProject project, Guid wordId, string key, string? value, string authority)
    {
        var existing = project.BibleEntries.FirstOrDefault(b => b.SubjectEntityId == wordId && b.IsActive &&
            string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        var text = value?.Trim() ?? string.Empty;
        if (existing is null)
        {
            project.BibleEntries.Add(new BibleEntry
            {
                SubjectEntityId = wordId,
                Key = key,
                Value = text,
                Authority = authority,
                IsActive = true
            });
        }
        else
        {
            existing.Value = text;
            existing.Authority = authority;
        }
    }

    private static async Task WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.TrimStart());
    }

    private static void AppendRow(StringBuilder xml, int row, IReadOnlyList<string> values)
    {
        xml.Append($"<row r=\"{row}\">");
        for (var i = 0; i < values.Count; i++)
        {
            var reference = ColumnName(i + 1) + row;
            var escaped = SecurityElement.Escape(values[i] ?? string.Empty) ?? string.Empty;
            xml.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{escaped}</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static List<Dictionary<string, string>> ReadXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relDoc = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace relPkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbook = LoadXml(archive, "xl/workbook.xml") ?? throw new InvalidDataException("XLSX non valido: workbook mancante.");
        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault() ?? throw new InvalidDataException("XLSX senza fogli.");
        var relationId = (string?)firstSheet.Attribute(relDoc + "id") ?? "rId1";
        var rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var target = rels?.Descendants(relPkg + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == relationId)?.Attribute("Target")?.Value
                     ?? "worksheets/sheet1.xml";
        target = target.Replace('\\', '/').TrimStart('/');
        var sheetPath = target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
        var sheet = LoadXml(archive, sheetPath) ?? throw new InvalidDataException("XLSX non valido: foglio dati mancante.");

        var shared = new List<string>();
        var sharedDoc = LoadXml(archive, "xl/sharedStrings.xml");
        if (sharedDoc is not null)
            shared.AddRange(sharedDoc.Descendants(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))));

        var rawRows = new List<Dictionary<int, string>>();
        foreach (var row in sheet.Descendants(main + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(main + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? string.Empty;
                var column = ColumnIndex(reference);
                if (column <= 0) continue;
                var type = (string?)cell.Attribute("t") ?? string.Empty;
                string value;
                if (type == "inlineStr") value = string.Concat(cell.Descendants(main + "t").Select(t => t.Value));
                else
                {
                    value = cell.Element(main + "v")?.Value ?? string.Empty;
                    if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count) value = shared[index];
                }
                cells[column] = value.Trim();
            }
            if (cells.Count > 0) rawRows.Add(cells);
        }
        if (rawRows.Count == 0) return [];
        var headerRow = rawRows[0];
        var headers = headerRow.ToDictionary(k => k.Key, v => NormalizeHeader(v.Value));
        if (!headers.Values.Contains("PAROLA", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Nel file XLSX manca la colonna PAROLA.");
        var result = new List<Dictionary<string, string>>();
        foreach (var row in rawRows.Skip(1))
        {
            var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in row)
                if (headers.TryGetValue(pair.Key, out var header) && header.Length > 0) mapped[header] = pair.Value;
            if (mapped.Count > 0) result.Add(mapped);
        }
        return result;
    }

    private static XDocument? LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string NormalizeHeader(string value) => string.Join(' ', value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int ColumnIndex(string reference)
    {
        var value = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            value = checked(value * 26 + (char.ToUpperInvariant(ch) - 'A' + 1));
        }
        return value;
    }

    private static string ColumnName(int index)
    {
        var result = string.Empty;
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index /= 26;
        }
        return result;
    }
}
