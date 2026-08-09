using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

internal static class WordSearchWorkspaceSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezWordSearchSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = ProjectFileStore.Create("Nostalgic Word Search Test");
            var first = Make(project, 1, "Cucina", 20);
            var second = Make(project, 2, "Giochi", 18);
            var third = Make(project, 3, "Vacanze", 20);
            foreach (var record in new[] { first, second, third })
                WordSearchDatabaseService.SetExpectedWordCount(project, record.Id, 20);

            if (WordSearchDatabaseService.ExpectedWordCount(project, second) != 20)
                throw new InvalidOperationException("Il numero di parole scelto non rimane stabile.");
            var issue = WordSearchWorkspaceChecks.Analyze(project, second);
            if (!issue.TooFewWords || !issue.Messages.Any(m => m.Contains("18/20", StringComparison.Ordinal)))
                throw new InvalidOperationException("Un puzzle incompleto non viene mostrato come 18/20.");

            var xlsx = Path.Combine(root, "titans.xlsx");
            var xlsxResult = await WordSearchColumnExportService.ExportXlsxAsync(project, xlsx);
            if (!xlsxResult.Success || !File.Exists(xlsx)) throw new InvalidOperationException("Export XLSX a colonne fallito.");
            using (var archive = ZipFile.OpenRead(xlsx))
            {
                var xml = await ReadEntryAsync(archive, "xl/worksheets/sheet1.xml");
                if (!xml.Contains("Puzzle 1", StringComparison.Ordinal) || !xml.Contains("Puzzle 2", StringComparison.Ordinal) || !xml.Contains("Puzzle 3", StringComparison.Ordinal))
                    throw new InvalidOperationException("L'XLSX non usa le intestazioni Puzzle 1...N.");
                if (xml.Contains("showGridLines=\"0\"", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("L'XLSX non deve nascondere la normale griglia di Excel.");
                if (Count(xml, "<row") != 21)
                    throw new InvalidOperationException("Con 20 parole previste l'XLSX deve avere intestazione + 20 righe di parole.");
            }

            var csv = Path.Combine(root, "titans.csv");
            var csvResult = await WordSearchColumnExportService.ExportCsvAsync(project, csv);
            if (!csvResult.Success) throw new InvalidOperationException("Export CSV a colonne fallito.");
            var csvLines = await File.ReadAllLinesAsync(csv);
            if (csvLines.Length != 21 || !csvLines[0].Contains("Puzzle 1", StringComparison.Ordinal) || !csvLines[0].Contains("Puzzle 3", StringComparison.Ordinal))
                throw new InvalidOperationException("Il CSV non rispetta Puzzle 1...N con 20 righe di parole.");

            var database = Path.Combine(root, "database.xlsx");
            var dbResult = await WordSearchExportService.ExportDatabaseAsync(project, database);
            if (!dbResult.Success) throw new InvalidOperationException("Export database completo fallito.");

            var imported = ProjectFileStore.Create("Reimport");
            var merge = await WordSearchDatabaseService.ImportDatabaseAsync(imported, database, Guid.Empty, replaceExisting: true);
            if (!merge.Recognized || WordSearchWorkspaceService.GetRecords(imported).Count != 3)
                throw new InvalidOperationException("Il database esportato non è reimportabile.");
            var importedSecond = WordSearchWorkspaceService.GetRecords(imported).Single(r => r.Id == "PUZ-002");
            if (WordSearchDatabaseService.ExpectedWordCount(imported, importedSecond) != 20 || importedSecond.Words.Count != 18)
                throw new InvalidOperationException("Il reimport perde il requisito 18/20.");

            var beforeFirst = string.Join("|", WordSearchWorkspaceService.GetRecords(imported).Single(r => r.Id == "PUZ-001").Words);
            importedSecond.Words[0] = "CORRETTA";
            WordSearchWorkspaceService.SaveRecord(imported, importedSecond);
            var afterFirst = string.Join("|", WordSearchWorkspaceService.GetRecords(imported).Single(r => r.Id == "PUZ-001").Words);
            if (!string.Equals(beforeFirst, afterFirst, StringComparison.Ordinal))
                throw new InvalidOperationException("La correzione chirurgica di PUZ-002 ha modificato PUZ-001.");
            if (WordSearchWorkspaceService.GetRecords(imported).Single(r => r.Id == "PUZ-002").Words[0] != "CORRETTA")
                throw new InvalidOperationException("La correzione chirurgica non è rimasta sul puzzle selezionato.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static WordSearchRecord Make(PreviewProject project, int number, string theme, int words)
    {
        var record = new WordSearchRecord
        {
            Order = number,
            Id = $"PUZ-{number:D3}",
            Title = $"Puzzle {number}",
            Theme = theme,
            Words = Enumerable.Range(1, words).Select(i => $"PAROLA{number:D2}_{i:D2}").ToList(),
            Origin = "Test"
        };
        WordSearchWorkspaceService.SaveRecord(project, record);
        return record;
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Voce XLSX mancante: {name}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
