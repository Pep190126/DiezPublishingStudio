using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace DiezPublishingStudio.UnoSpike;

internal static class UnoConsolidationExportService
{
    public static async Task<string> ExportMaterialsZipAsync(DiezProjectDocument document, string path)
    {
        var items = ProjectMaterialPreviewPanel.ReadItems(document);
        if (items.Count == 0) return "Non ci sono materiali da esportare.";

        var allAiMaterials = new HashSet<Guid>();
        var approvedAiMaterials = new HashSet<Guid>();
        foreach (var job in document.AiJobs())
        {
            if (!job.WorkUnitId.HasValue) continue;
            foreach (var version in document.AiVersions(job.WorkUnitId.Value))
            {
                if (!version.MaterialId.HasValue) continue;
                allAiMaterials.Add(version.MaterialId.Value);
                if (string.Equals(version.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                    approvedAiMaterials.Add(version.MaterialId.Value);
            }
        }

        var selected = items
            .Where(item =>
                !allAiMaterials.Contains(item.MaterialId) ||
                approvedAiMaterials.Contains(item.MaterialId))
            .Where(item =>
                approvedAiMaterials.Contains(item.MaterialId) ||
                !LooksLikeUnapprovedLegacyAi(item))
            .ToList();

        if (selected.Count == 0)
            return "Non ci sono materiali utente o asset AI approvati da includere nel pacchetto.";

        var fullPath = EnsureExtension(path, ".zip");
        EnsureDirectory(fullPath);
        var temp = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        var exported = 0;
        var missing = 0;
        try
        {
            await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var manifest = new List<string>
            {
                "FILE\tORIGINE\tSHA256\tDIMENSIONE_BYTE"
            };
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in selected)
            {
                var localPath = await ProjectMaterialPreviewPanel.ResolveMaterialPathAsync(document, item);
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    missing++;
                    continue;
                }

                var origin = approvedAiMaterials.Contains(item.MaterialId) ? "AI_APPROVATA" : "UTENTE";
                var folder = origin == "AI_APPROVATA" ? "ai-approvate" : "materiali-utente";
                var fileName = UniqueFileName(SafeEntryFileName(item.FileName), item.MaterialId, usedNames, folder);
                var entryName = folder + "/" + fileName;
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using (var input = File.OpenRead(localPath))
                await using (var target = entry.Open())
                    await input.CopyToAsync(target);

                manifest.Add(string.Join('\t',
                    entryName,
                    origin,
                    item.Sha256,
                    item.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                exported++;
            }

            var manifestEntry = archive.CreateEntry("MANIFEST-MATERIALI.tsv", CompressionLevel.Optimal);
            await using (var target = manifestEntry.Open())
            await using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
                await writer.WriteAsync(string.Join(Environment.NewLine, manifest));

            archive.Dispose();
            await output.FlushAsync();
            File.Move(temp, fullPath, true);
            return $"Materiali esportati: {exported} file in {Path.GetFileName(fullPath)}" +
                   (missing > 0 ? $" · {missing} record senza byte disponibili" : string.Empty) + ".";
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    public static async Task<string> ExportWordSearchFullDatabaseAsync(DiezProjectDocument document, string path)
    {
        var snapshot = document.WordSearchWorkspace();
        if (snapshot.Lexicon.Count == 0 && snapshot.Puzzles.Count == 0)
            return "Il progetto non contiene ancora dati Word Search da esportare.";

        var lexiconRows = new List<IReadOnlyList<string>>
        {
            new[] { "WORD", "CATEGORY", "SUBCATEGORY", "YEAR" }
        };
        foreach (var entry in snapshot.Lexicon)
            lexiconRows.Add(new[] { entry.Word, entry.Category, entry.Subcategory, entry.Year });

        var maxWords = snapshot.Puzzles.Select(p => p.Words.Count).DefaultIfEmpty(0).Max();
        var puzzleHeader = new List<string> { "PUZZLE_ID", "TITLE", "THEME", "STATUS" };
        puzzleHeader.AddRange(Enumerable.Range(1, maxWords).Select(i => $"WORD_{i:D2}"));
        puzzleHeader.Add("NOTES");
        var puzzleRows = new List<IReadOnlyList<string>> { puzzleHeader };
        foreach (var puzzle in snapshot.Puzzles)
        {
            var row = new List<string> { puzzle.PuzzleId, puzzle.Title, puzzle.Theme, puzzle.Status };
            for (var i = 0; i < maxWords; i++) row.Add(i < puzzle.Words.Count ? puzzle.Words[i] : string.Empty);
            row.Add(puzzle.Notes);
            puzzleRows.Add(row);
        }

        var infoRows = new List<IReadOnlyList<string>>
        {
            new[] { "INFORMAZIONE", "VALORE" },
            new[] { "Tipo", "Word Search · database completo" },
            new[] { "Titolo", document.EditionTitle },
            new[] { "Parole nel lessico", snapshot.Lexicon.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "Puzzle nel libro", snapshot.Puzzles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "Scopo", "Archivio completo del lessico canonico disponibile più fotografia dei puzzle correnti" }
        };

        var fullPath = EnsureExtension(path, ".xlsx");
        await WriteWorkbookAsync(fullPath,
            ("PAROLE", lexiconRows),
            ("PUZZLE", puzzleRows),
            ("INFO", infoRows));
        return $"Database completo Word Search esportato: {Path.GetFileName(fullPath)} · {snapshot.Lexicon.Count} parole · {snapshot.Puzzles.Count} puzzle.";
    }

    public static async Task<string> ExportWordSearchBookDatabaseAsync(DiezProjectDocument document, string path)
    {
        var snapshot = document.WordSearchWorkspace();
        if (snapshot.Puzzles.Count == 0) return "Non ci sono puzzle Word Search nel libro corrente.";

        var lexicon = snapshot.Lexicon
            .GroupBy(x => x.Word ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var used = new Dictionary<string, (string Display, SortedSet<string> PuzzleIds)>(StringComparer.OrdinalIgnoreCase);
        foreach (var puzzle in snapshot.Puzzles)
        {
            foreach (var raw in puzzle.Words)
            {
                var word = (raw ?? string.Empty).Trim();
                if (word.Length == 0) continue;
                if (!used.TryGetValue(word, out var current))
                    current = (word, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                current.PuzzleIds.Add(puzzle.PuzzleId);
                used[word] = current;
            }
        }

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "WORD", "CATEGORY", "SUBCATEGORY", "YEAR", "PUZZLE_IDS" }
        };
        foreach (var pair in used.OrderBy(x => x.Value.Display, StringComparer.OrdinalIgnoreCase))
        {
            lexicon.TryGetValue(pair.Key, out var meta);
            rows.Add(new[]
            {
                pair.Value.Display,
                meta?.Category ?? string.Empty,
                meta?.Subcategory ?? string.Empty,
                meta?.Year ?? string.Empty,
                string.Join(" | ", pair.Value.PuzzleIds)
            });
        }

        var puzzleRows = new List<IReadOnlyList<string>>
        {
            new[] { "PUZZLE_ID", "TITLE", "THEME", "STATUS", "WORD_COUNT" }
        };
        foreach (var puzzle in snapshot.Puzzles)
            puzzleRows.Add(new[]
            {
                puzzle.PuzzleId,
                puzzle.Title,
                puzzle.Theme,
                puzzle.Status,
                puzzle.Words.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        var infoRows = new List<IReadOnlyList<string>>
        {
            new[] { "INFORMAZIONE", "VALORE" },
            new[] { "Tipo", "Word Search · database del libro" },
            new[] { "Titolo", document.EditionTitle },
            new[] { "Parole uniche usate", used.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "Puzzle", snapshot.Puzzles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "Regola", "Contiene soltanto parole effettivamente presenti nei puzzle correnti" }
        };

        var fullPath = EnsureExtension(path, ".xlsx");
        await WriteWorkbookAsync(fullPath,
            ("PAROLE_LIBRO", rows),
            ("PUZZLE", puzzleRows),
            ("INFO", infoRows));
        return $"Database del libro Word Search esportato: {Path.GetFileName(fullPath)} · {used.Count} parole uniche usate.";
    }

    private static bool LooksLikeUnapprovedLegacyAi(ProjectMaterialPreviewItem item) =>
        item.Summary.Contains("Risultato AI", StringComparison.OrdinalIgnoreCase) ||
        item.Preview.Contains("Risultato importato automaticamente", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteWorkbookAsync(string path, params (string Name, IReadOnlyList<IReadOnlyList<string>> Rows)[] sheets)
    {
        EnsureDirectory(path);
        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            await WriteEntryAsync(archive, "[Content_Types].xml", ContentTypes(sheets.Length));
            await WriteEntryAsync(archive, "_rels/.rels", RootRelationships());
            await WriteEntryAsync(archive, "xl/workbook.xml", Workbook(sheets.Select(x => x.Name).ToList()));
            await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Length));
            await WriteEntryAsync(archive, "xl/styles.xml", Styles());
            for (var i = 0; i < sheets.Length; i++)
                await WriteEntryAsync(archive, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i].Rows));
            archive.Dispose();
            await stream.FlushAsync();
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static string Worksheet(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetData = new XElement(x + "sheetData");
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(x + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Count; c++)
            {
                row.Add(new XElement(x + "c",
                    new XAttribute("r", CellRef(c, r + 1)),
                    new XAttribute("t", "inlineStr"),
                    r == 0 ? new XAttribute("s", "1") : null,
                    new XElement(x + "is",
                        new XElement(x + "t",
                            new XAttribute(XNamespace.Xml + "space", "preserve"),
                            rows[r][c] ?? string.Empty))));
            }
            sheetData.Add(row);
        }

        var worksheet = new XElement(x + "worksheet",
            new XElement(x + "sheetViews",
                new XElement(x + "sheetView",
                    new XAttribute("workbookViewId", "0"),
                    new XElement(x + "pane",
                        new XAttribute("ySplit", "1"),
                        new XAttribute("topLeftCell", "A2"),
                        new XAttribute("state", "frozen")))),
            sheetData);
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet));
    }

    private static string Workbook(IReadOnlyList<string> names)
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets = new XElement(x + "sheets");
        for (var i = 0; i < names.Count; i++)
        {
            sheets.Add(new XElement(x + "sheet",
                new XAttribute("name", SafeSheetName(names[i])),
                new XAttribute("sheetId", i + 1),
                new XAttribute(r + "id", $"rId{i + 1}")));
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "workbook", new XAttribute(XNamespace.Xmlns + "r", r), sheets)));
    }

    private static string WorkbookRelationships(int count)
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(x + "Relationships");
        for (var i = 1; i <= count; i++)
        {
            root.Add(new XElement(x + "Relationship",
                new XAttribute("Id", $"rId{i}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        }
        root.Add(new XElement(x + "Relationship",
            new XAttribute("Id", $"rId{count + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string RootRelationships()
    {
        XNamespace x = "http://schemas.openxmlformats.org/package/2006/relationships";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "Relationships",
                new XElement(x + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")))));
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
        {
            root.Add(new XElement(x + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
    }

    private static string Styles()
    {
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return Xml(new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(x + "styleSheet",
                new XElement(x + "fonts", new XAttribute("count", "2"),
                    new XElement(x + "font", new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos"))),
                    new XElement(x + "font", new XElement(x + "b"), new XElement(x + "sz", new XAttribute("val", "11")), new XElement(x + "name", new XAttribute("val", "Aptos")))),
                new XElement(x + "fills", new XAttribute("count", "2"),
                    new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(x + "fill", new XElement(x + "patternFill", new XAttribute("patternType", "gray125")))),
                new XElement(x + "borders", new XAttribute("count", "1"), new XElement(x + "border")),
                new XElement(x + "cellStyleXfs", new XAttribute("count", "1"),
                    new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                new XElement(x + "cellXfs", new XAttribute("count", "2"),
                    new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                    new XElement(x + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1"))),
                new XElement(x + "cellStyles", new XAttribute("count", "1"),
                    new XElement(x + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))))));
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var writer = new StreamWriter(target, new UTF8Encoding(false));
        await writer.WriteAsync(content);
    }

    private static string CellRef(int column, int row)
    {
        var n = column + 1;
        var value = string.Empty;
        while (n > 0)
        {
            n--;
            value = (char)('A' + n % 26) + value;
            n /= 26;
        }
        return value + row;
    }

    private static string SafeSheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var result = new string((value ?? "Foglio").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return result.Length > 31 ? result[..31] : result;
    }

    private static string UniqueFileName(string fileName, Guid id, HashSet<string> used, string folder)
    {
        var key = folder + "/" + fileName;
        if (used.Add(key)) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = $"{stem}-{id:N}"[..Math.Min(stem.Length + 1 + 8, stem.Length + 33)] + ext;
        key = folder + "/" + candidate;
        used.Add(key);
        return candidate;
    }

    private static string SafeEntryFileName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "materiale" : Path.GetFileName(value);
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }

    private static string EnsureExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);
}
