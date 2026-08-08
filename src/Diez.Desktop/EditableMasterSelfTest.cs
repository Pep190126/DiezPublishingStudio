using System.Text;

namespace DiezPublishingStudio;

internal static class EditableMasterSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezEditableMaster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "master-source.txt");
            const string originalText = "Capitolo 1\nMilo ha gli occhi blu e guarda il mare.\n\nCapitolo 2\nMilo ha gli occhi verdi e torna al Faro.";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.UTF8);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Editable Master Test");
            project.Materials.Add(material);
            var nodes = ContentStructureAnalyzer.Analyze(material);
            project.ContentNodes.AddRange(nodes);

            var chapter2 = project.ContentNodes.First(n => n.Kind == "Chapter" && n.Title.Contains("2", StringComparison.OrdinalIgnoreCase));
            var originalChapterBody = chapter2.Body;
            Require(EditableMasterService.CanEdit(project, chapter2), "Il capitolo non è modificabile nel Master.");

            project.RevisionCandidates.Add(new RevisionCandidate
            {
                CandidateId = Guid.NewGuid(),
                IssueId = Guid.NewGuid(),
                IssueSignature = "stale-test",
                ContentId = chapter2.ContentId,
                Key = "eye_color",
                OriginalBody = chapter2.Body,
                ProposedBody = chapter2.Body.Replace("verdi", "blu", StringComparison.OrdinalIgnoreCase),
                Status = "Approved",
                CreatedAtLocal = DateTimeOffset.Now.ToString("O")
            });

            var editedText = chapter2.Body.Replace("verdi", "grigi", StringComparison.OrdinalIgnoreCase);
            var edit = EditableMasterService.ApplyManualEdit(project, chapter2.ContentId, editedText, "Correzione manuale test");
            Require(edit.Changed, "La modifica manuale non è stata applicata.");
            Require(chapter2.Body.Contains("grigi", StringComparison.OrdinalIgnoreCase), "Il Master non contiene la modifica manuale.");
            Require(material.ExtractedText.Contains("verdi", StringComparison.OrdinalIgnoreCase), "Lo snapshot estratto originale è stato alterato.");
            Require((await File.ReadAllTextAsync(sourcePath)).Contains("verdi", StringComparison.OrdinalIgnoreCase), "Il file sorgente è stato alterato.");
            Require(project.RevisionCandidates.Any(c => c.ContentId == chapter2.ContentId && c.Key == "manual_edit" && c.Status == "Applied"),
                "La revisione manuale non è stata registrata.");
            Require(project.RevisionCandidates.Any(c => c.IssueSignature == "stale-test" && c.Status == "Rejected"),
                "Una proposta precedente non è stata invalidata dopo la modifica manuale.");

            var projectPath = Path.Combine(root, "editable-master.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            var loaded = await ProjectFileStore.LoadAsync(projectPath);
            var loadedChapter = loaded.ContentNodes.Single(n => n.ContentId == chapter2.ContentId);
            Require(loadedChapter.Body.Contains("grigi", StringComparison.OrdinalIgnoreCase), "La modifica del Master non è sopravvissuta al round-trip.");
            Require(EditableMasterService.ManualRevisionCount(loaded, chapter2.ContentId) == 1, "La cronologia manuale non è sopravvissuta al round-trip.");

            var restore = EditableMasterService.RestoreImportedSnapshot(loaded, chapter2.ContentId);
            Require(restore.Changed, "Il ripristino dallo snapshot importato non è stato applicato.");
            Require(loadedChapter.Body == originalChapterBody, "Il ripristino non ha ricostruito il testo importato originale.");
            Require(EditableMasterService.ManualRevisionCount(loaded, chapter2.ContentId) == 2,
                "Il ripristino non è stato registrato come nuova revisione del Master.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("EDITABLE-MASTER SELF-TEST: " + message);
    }
}
