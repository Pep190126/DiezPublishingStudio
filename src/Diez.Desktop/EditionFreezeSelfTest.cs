using System.Text;

namespace DiezPublishingStudio;

internal static class EditionFreezeSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezEditionFreeze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "freeze-source.txt");
            const string originalText = "Capitolo 1\nMilo guarda il mare.\n\nCapitolo 2\nMilo torna al Faro.";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.UTF8);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Edition Freeze Test");
            project.Materials.Add(material);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));

            var projectPath = Path.Combine(root, "edition-freeze.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);
            Require(project.Materials.Count == 1 && project.Materials[0].IsEmbedded,
                "Il materiale sorgente deve essere incorporato prima del preflight.");

            var first = EditionFreezeService.CreateFreeze(project);
            Require(first.Freeze is not null, "Il primo Edition Freeze non è stato creato.");
            Require(EditionFreezeService.FreezeCount(project) == 1, "Il conteggio dei freeze non è corretto.");
            Require(EditionFreezeService.IsLatestFreezeCurrent(project), "Il freeze appena creato dovrebbe essere corrente.");
            var firstSnapshot = first.Freeze!.ProposedBody;

            var ready = EditionFreezeService.RunPreflight(project);
            Require(ready.Ready, "Il preflight iniziale dovrebbe essere READY.");
            Require(ready.Checks.All(c => c.Severity != "Error" || c.Passed), "Un controllo bloccante è fallito nel preflight READY.");

            var chapter2 = project.ContentNodes.First(n => n.Kind == "Chapter" && n.Title.Contains("2", StringComparison.OrdinalIgnoreCase));
            var edit = EditableMasterService.ApplyManualEdit(project, chapter2.ContentId,
                chapter2.Body.Replace("Faro", "Porto", StringComparison.OrdinalIgnoreCase),
                "Modifica dopo freeze");
            Require(edit.Changed, "La modifica del Master dopo il freeze non è stata applicata.");
            Require(!EditionFreezeService.IsLatestFreezeCurrent(project), "Il freeze dovrebbe risultare non corrente dopo una modifica del Master.");

            var blocked = EditionFreezeService.RunPreflight(project);
            Require(!blocked.Ready, "Il preflight dovrebbe bloccarsi quando il Master cambia dopo il freeze.");
            Require(blocked.Checks.Any(c => c.Code == "FREEZE_CURRENT" && !c.Passed),
                "Il preflight non segnala che l'Edition Freeze è superato.");
            Require(first.Freeze.ProposedBody == firstSnapshot,
                "Lo snapshot del primo Edition Freeze è stato alterato dalla modifica del Master.");

            var second = EditionFreezeService.CreateFreeze(project, "Secondo freeze dopo modifica manuale");
            Require(second.Freeze is not null, "Il secondo Edition Freeze non è stato creato.");
            Require(EditionFreezeService.FreezeCount(project) == 2, "Il secondo freeze non è stato aggiunto alla cronologia.");
            Require(EditionFreezeService.IsLatestFreezeCurrent(project), "Il secondo freeze dovrebbe coincidere con il Master corrente.");
            Require(second.Freeze!.ProposedBody.Contains("Porto", StringComparison.Ordinal),
                "Il secondo freeze non contiene la versione aggiornata del Master.");
            Require(first.Freeze.ProposedBody.Contains("Faro", StringComparison.Ordinal),
                "Il primo freeze non conserva la versione precedente del Master.");

            var readyAgain = EditionFreezeService.RunPreflight(project);
            Require(readyAgain.Ready, "Il preflight dovrebbe tornare READY dopo un nuovo freeze corrente.");

            await ProjectFileStore.SaveAsync(projectPath, project);
            var loaded = await ProjectFileStore.LoadAsync(projectPath);
            Require(EditionFreezeService.FreezeCount(loaded) == 2,
                "La cronologia Edition Freeze non è sopravvissuta al round-trip .diez.");
            Require(EditionFreezeService.IsLatestFreezeCurrent(loaded),
                "L'ultimo Edition Freeze non è corrente dopo il round-trip .diez.");
            Require(EditionFreezeService.RunPreflight(loaded).Ready,
                "Il preflight non resta READY dopo salvataggio e riapertura.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("EDITION-FREEZE SELF-TEST: " + message);
    }
}
