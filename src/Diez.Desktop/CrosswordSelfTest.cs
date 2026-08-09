using System.Text;

namespace DiezPublishingStudio;

internal static class CrosswordSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezCrossword-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = ProjectFileStore.Create("Cruciverba Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.Crossword);
            CrosswordService.SetSetting(project, "PrimaryLanguage", "Italiano");
            CrosswordService.SetSetting(project, "ThemeMode", "Tematico");
            CrosswordService.SetSetting(project, "Theme", "Astronomia");

            var italian = Path.Combine(root, "italiano.txt");
            await File.WriteAllTextAsync(italian,
                "casa\nCITTÀ\nsoftware\nweekend\nmercurio\nCASA\n", new UTF8Encoding(false));
            var foreign = Path.Combine(root, "estero.txt");
            await File.WriteAllTextAsync(foreign,
                "SUNSET\nyard\nSOFTWARE\n", new UTF8Encoding(false));
            var dic = Path.Combine(root, "parole.dic");
            await File.WriteAllTextAsync(dic,
                "4\ntelefono/AB\ngatto/X\nyard/Q\nmonna-lisa/Z\n", new UTF8Encoding(false));

            var a = await CrosswordService.ImportWordListAsync(project, italian);
            var b = await CrosswordService.ImportWordListAsync(project, foreign);
            var c = await CrosswordService.ImportWordListAsync(project, dic);

            Require(a.Added == 5 && a.Existing == 1, "Import TXT italiano: conteggi inattesi.");
            Require(b.Added == 2 && b.Existing == 1, "Import TXT estero: deduplica inattesa.");
            Require(c.Added == 3 && c.Existing == 1, "Import DIC: lemmi/flag non gestiti come previsto.");
            Require(CrosswordService.Words(project).Count == 10, "Il vocabolario unificato dovrebbe contenere 10 forme uniche.");
            Require(CrosswordService.FindWord(project, "città")?.Name == "CITTÀ", "Gli accenti devono restare nella forma di griglia quando sono lettere.");
            Require(CrosswordService.FindWord(project, "monna-lisa")?.Name == "MONNALISA", "La normalizzazione della forma di griglia non rimuove correttamente la punteggiatura.");
            Require(BookTypeProfileService.IsCrossword(project), "Il progetto non resta riconosciuto come Cruciverba.");

            var qxw = Path.Combine(root, "qxw.txt");
            await CrosswordService.ExportQxwTextAsync(project, qxw);
            var exported = await File.ReadAllLinesAsync(qxw);
            Require(exported.Length == 10, "Il TXT Qxw non contiene tutte e sole le forme uniche.");
            Require(exported.SequenceEqual(exported.OrderBy(w => w, StringComparer.OrdinalIgnoreCase)), "Il TXT Qxw non è ordinato.");
            Require(exported.Distinct(StringComparer.OrdinalIgnoreCase).Count() == exported.Length, "Il TXT Qxw contiene duplicati.");
            Require(exported.Contains("SUNSET") && exported.Contains("YARD"), "Le parole straniere di riserva sono state eliminate dal listone.");

            var template = Path.Combine(root, "definizioni-template.xlsx");
            await CrosswordService.WriteDefinitionTemplateXlsxAsync(project, template);
            Require(File.Exists(template) && new FileInfo(template).Length > 0, "Il template XLSX definizioni non è stato creato.");

            var answer = Path.Combine(root, "definizioni-ai.xlsx");
            await CrosswordService.WriteDefinitionWorkbookAsync(answer,
            [
                new CrosswordDefinitionRow("MERCURIO", "Il pianeta più vicino al Sole", "Un elemento chimico", "Il messaggero degli dei", "Un metallo liquido", "Tema: astronomia"),
                new CrosswordDefinitionRow("YARD", "Misura inglese di lunghezza", "Un cortile, in inglese", "Unità da tre piedi", "Termine anglosassone", "Parola straniera di riserva"),
                new CrosswordDefinitionRow("PESCA", "Frutto dalla buccia vellutata", "Attività con lenza e ami", "La raccolta del pescatore", "Può essere sportiva", "Parola aggiunta dall'AI")
            ]);

            var importedDefinitions = await CrosswordService.ImportDefinitionsXlsxAsync(project, answer);
            Require(importedDefinitions.Rows == 3, "Non sono state importate tutte le righe definizioni.");
            Require(importedDefinitions.WordsCreated == 1, "La parola PESCA non è stata aggiunta dal foglio AI.");
            Require(importedDefinitions.DefinitionsImported == 12, "Non sono state importate tutte le quattro definizioni per riga.");

            var mercury = CrosswordService.FindWord(project, "MERCURIO") ?? throw new InvalidOperationException("MERCURIO mancante.");
            var yard = CrosswordService.FindWord(project, "YARD") ?? throw new InvalidOperationException("YARD mancante.");
            CrosswordService.SetApproved(project, mercury.EntityId, "Il pianeta più vicino al Sole");
            CrosswordService.SetApproved(project, yard.EntityId, "Misura inglese di lunghezza");

            var rows = CrosswordService.DefinitionRows(project);
            var mercuryRow = rows.Single(r => r.Word == "MERCURIO");
            Require(mercuryRow.Definition1 == "Il pianeta più vicino al Sole", "La prima definizione di MERCURIO non è stata importata.");
            Require(mercuryRow.Definition2 == "Un elemento chimico", "Le alternative di MERCURIO non sono state conservate.");
            Require(mercuryRow.Approved == "Il pianeta più vicino al Sole", "La definizione approvata non è separata dalle alternative.");
            Require(CrosswordService.MissingDefinitions(project) == 8, "Il conteggio delle parole senza definizioni non è corretto.");

            var projectPath = Path.Combine(root, "crossword.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            var loaded = await ProjectFileStore.LoadAsync(projectPath);
            Require(BookTypeProfileService.IsCrossword(loaded), "Il Tipo libro Cruciverba non è sopravvissuto al round-trip .diez.");
            Require(CrosswordService.Words(loaded).Count == 11, "Il vocabolario cruciverba non è sopravvissuto al round-trip .diez.");
            Require(CrosswordService.GetSetting(loaded, "Theme") == "Astronomia", "Il tema non è sopravvissuto al round-trip .diez.");
            var loadedMercury = CrosswordService.DefinitionRows(loaded).Single(r => r.Word == "MERCURIO");
            Require(loadedMercury.Approved == "Il pianeta più vicino al Sole", "La definizione approvata non è sopravvissuta al round-trip .diez.");
            Require(loadedMercury.Definition2 == "Un elemento chimico", "Le definizioni alternative sono state perse dopo il round-trip .diez.");

            var reexport = Path.Combine(root, "qxw-after-roundtrip.txt");
            await CrosswordService.ExportQxwTextAsync(loaded, reexport);
            var after = await File.ReadAllLinesAsync(reexport);
            Require(after.Length == 11 && after.Contains("PESCA"), "Il TXT Qxw rigenerato dal .diez non corrisponde al vocabolario salvato.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("CROSSWORD SELF-TEST: " + message);
    }
}
