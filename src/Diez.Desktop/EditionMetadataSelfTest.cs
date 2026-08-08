using System.Text;

namespace DiezPublishingStudio;

internal static class EditionMetadataSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezEditionMetadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "metadata-source.txt");
            await File.WriteAllTextAsync(sourcePath, "Capitolo 1\nMilo guarda il mare.", Encoding.UTF8);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Libro di prova");
            project.Materials.Add(material);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));

            Require(project.EditionMetadata.Title == "Libro di prova", "Il titolo iniziale deve derivare dal nome progetto.");
            Require(project.EditionMetadata.Language == "it", "La lingua iniziale della UI italiana deve essere it.");

            var invalid = EditionMetadataService.Update(project, "Libro di prova", "", "Ada Autrice", "it", "Diez", "123", "Test");
            Require(!invalid.Changed, "Un ISBN non valido non deve essere salvato.");

            var updated = EditionMetadataService.Update(project, "Libro di prova", "Sottotitolo", "Ada Autrice", "it", "Diez", "9780306406157", "Descrizione editoriale");
            Require(updated.Changed, "I metadati validi non sono stati aggiornati.");
            Require(project.EditionMetadata.Isbn == "9780306406157", "ISBN-13 valido non salvato.");

            var projectPath = Path.Combine(root, "metadata.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);
            Require(project.Materials.Count == 1 && project.Materials[0].IsEmbedded,
                "Il materiale deve essere incorporato prima del preflight metadata.");
            Require(project.SchemaVersion == 9, "Il progetto deve essere salvato con schema 9.");
            Require(project.EditionMetadata.Creator == "Ada Autrice", "Autore non persistito nel .diez.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Freeze non creato dopo i metadati.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY con titolo e lingua validi.");

            var before = freeze.Freeze!.ProposedBody;
            var changed = EditionMetadataService.Update(project, "Libro di prova - seconda edizione", "Sottotitolo", "Ada Autrice", "it", "Diez", "9780306406157", "Descrizione editoriale");
            Require(changed.Changed, "La modifica del titolo non è stata applicata.");
            Require(!EditionFreezeService.IsLatestFreezeCurrent(project), "Una modifica ai metadati deve rendere superato il freeze.");
            Require(freeze.Freeze.ProposedBody == before, "Lo snapshot del freeze non deve cambiare dopo la modifica dei metadati.");

            await ProjectFileStore.SaveAsync(projectPath, project);
            var loaded = await ProjectFileStore.LoadAsync(projectPath);
            Require(loaded.EditionMetadata.Title.Contains("seconda edizione", StringComparison.Ordinal), "Titolo aggiornato non persistito.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("EDITION-METADATA SELF-TEST: " + message);
    }
}