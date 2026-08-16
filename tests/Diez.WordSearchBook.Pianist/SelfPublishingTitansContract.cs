using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DiezPublishingStudio;

internal static class SelfPublishingTitansContract
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "diez-spt-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var project = ProjectFileStore.Create("Self Publishing Titans contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.WordSearch);
            SetOption(project, "PuzzleCount", "2");
            SetOption(project, "WordsPerPuzzle", "3");
            SetOption(project, "NoDuplicates", "true");

            var first = WordSearchWorkspaceService.AddNew(project);
            first.Title = "Puzzle 1";
            first.Theme = "Tema A";
            first.Words = ["alpha", "beta", "gamma"];
            first.Status = WordSearchWorkspaceService.StatusApproved;
            WordSearchWorkspaceService.SaveRecord(project, first);

            var second = WordSearchWorkspaceService.AddNew(project);
            second.Title = "Puzzle 2";
            second.Theme = "Tema B";
            second.Words = ["delta", "epsilon", "zeta"];
            second.Status = WordSearchWorkspaceService.StatusApproved;
            WordSearchWorkspaceService.SaveRecord(project, second);

            var json = JsonSerializer.Serialize(project);
            var ready = DiezWordSearchFinalizationBridge.Readiness(json);
            Require(ready.Ready, "The fixture must be finalizable before testing Self Publishing Titans handoff.");

            var csvPath = Path.Combine(tempRoot, "titans.csv");
            var csvResult = DiezWordSearchFinalizationBridge.ExportFinalCsvAsync(json, csvPath).GetAwaiter().GetResult();
            Require(csvResult.Exported && File.Exists(csvPath), "Final CSV must export after the Word Search final gate passes.");
            var bytes = File.ReadAllBytes(csvPath);
            Require(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Self Publishing Titans CSV must carry an UTF-8 BOM like the supplied sample.");
            var csv = Encoding.UTF8.GetString(bytes[3..]);
            Require(csv == "puzzle 1,puzzle 2\nalpha,delta\nbeta,epsilon\ngamma,zeta\n",
                "Final CSV must be a comma-delimited puzzle-column matrix with lower-case puzzle headers and LF endings.");
            Require(!csv.Contains(';') && !csv.Contains('"'),
                "Simple Self Publishing Titans cells must not use semicolons or unnecessary quotes.");

            var xlsxPath = Path.Combine(tempRoot, "titans.xlsx");
            var xlsxResult = DiezWordSearchFinalizationBridge.ExportFinalXlsxAsync(json, xlsxPath).GetAwaiter().GetResult();
            Require(xlsxResult.Exported && File.Exists(xlsxPath) && new FileInfo(xlsxPath).Length > 0,
                "Final XLSX must be available alongside CSV after the same gate.");
            using var archive = ZipFile.OpenRead(xlsxPath);
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidOperationException("Final XLSX must contain its puzzle sheet.");
            using var reader = new StreamReader(sheet.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            Require(xml.Contains("puzzle 1", StringComparison.Ordinal) && xml.Contains("puzzle 2", StringComparison.Ordinal),
                "Final XLSX must expose the same lower-case puzzle-column headers as the CSV profile.");
            Require(!xml.Contains("Tema A", StringComparison.Ordinal) && !xml.Contains("Status", StringComparison.OrdinalIgnoreCase),
                "Final handoff must not leak Diez metadata rows into the Self Publishing Titans matrix.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void SetOption(PreviewProject project, string key, string value)
    {
        var definition = BookTypeAiOptionsCoreService.Definitions(project)
            .Single(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));
        BookTypeAiOptionsCoreService.Set(project, definition, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
