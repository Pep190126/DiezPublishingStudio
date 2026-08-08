using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class PackageSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezPublishingStudio-SelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await VerifyEmbeddedMaterialAndStructureRoundTripAsync(root);
            await VerifyLegacyProjectMigrationAsync(root);
            await VerifyDocxIntakeAndStructureAsync(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static async Task VerifyEmbeddedMaterialAndStructureRoundTripAsync(string root)
    {
        var sourcePath = Path.Combine(root, "manoscritto.txt");
        const string sourceContent = "Capitolo 1\nMilo entra nel faro.\nLa luce si accende.\n\nCapitolo 2\nLa tempesta arriva.";
        await File.WriteAllTextAsync(sourcePath, sourceContent, Encoding.UTF8);

        var material = await MaterialImporter.ImportAsync(sourcePath);
        material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
        var project = ProjectFileStore.Create("Self Test");
        project.Materials.Add(material);
        project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));

        Require(project.ContentNodes.Any(n => n.Kind == "Chapter" && n.Title.StartsWith("Capitolo 1", StringComparison.Ordinal)),
            "Il primo capitolo non è stato riconosciuto.");
        Require(project.ContentNodes.Count(n => n.Kind == "Chapter") == 2,
            "Il numero di capitoli riconosciuti non è corretto.");

        var projectPath = Path.Combine(root, "roundtrip.diez");
        await ProjectFileStore.SaveAsync(projectPath, project);

        Require(ProjectFileStore.IsPackageFile(projectPath), "Il .diez salvato non è un pacchetto ZIP.");
        var loaded = await ProjectFileStore.LoadAsync(projectPath);
        Require(loaded.SchemaVersion == 4, "Schema .diez inatteso.");
        Require(loaded.Materials.Count == 1, "Il materiale non è sopravvissuto al round-trip.");
        Require(loaded.ContentNodes.Count == project.ContentNodes.Count, "La struttura editoriale non è sopravvissuta al round-trip.");
        Require(loaded.Materials[0].IsEmbedded, "Il materiale non risulta incorporato.");

        var embedded = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, loaded.Materials[0]);
        Require(embedded is not null && Encoding.UTF8.GetString(embedded).Contains("Milo entra nel faro", StringComparison.Ordinal),
            "Il contenuto incorporato non corrisponde alla sorgente.");

        File.Delete(sourcePath);
        await ProjectFileStore.SaveAsync(projectPath, loaded);
        var reloaded = await ProjectFileStore.LoadAsync(projectPath);
        var embeddedAfterSourceRemoval = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, reloaded.Materials[0]);
        Require(embeddedAfterSourceRemoval is not null && Encoding.UTF8.GetString(embeddedAfterSourceRemoval).Contains("Milo entra nel faro", StringComparison.Ordinal),
            "Lo snapshot è stato perso dopo la rimozione della sorgente.");
        Require(reloaded.ContentNodes.Any(n => n.Kind == "Chapter"), "La struttura è stata persa dopo un secondo salvataggio.");
    }

    private static async Task VerifyLegacyProjectMigrationAsync(string root)
    {
        var legacyPath = Path.Combine(root, "legacy-preview-01.diez");
        var legacy = new PreviewProject
        {
            Format = "diez-project-preview",
            SchemaVersion = 2,
            Name = "Legacy Preview",
            ProjectId = Guid.NewGuid(),
            SavedAtLocal = DateTimeOffset.Now.ToString("G")
        };

        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(legacy));
        Require(!ProjectFileStore.IsPackageFile(legacyPath), "Il fixture legacy non dovrebbe essere ZIP.");
        var loadedLegacy = await ProjectFileStore.LoadAsync(legacyPath);
        Require(loadedLegacy.Name == "Legacy Preview", "Il progetto 0.1 non è stato letto correttamente.");
        await ProjectFileStore.SaveAsync(legacyPath, loadedLegacy);
        Require(ProjectFileStore.IsPackageFile(legacyPath), "Il progetto legacy non è stato migrato.");
        Require((await ProjectFileStore.LoadAsync(legacyPath)).SchemaVersion == 4, "Il progetto legacy non è arrivato allo schema 4.");
    }

    private static async Task VerifyDocxIntakeAndStructureAsync(string root)
    {
        var docxPath = Path.Combine(root, "manoscritto.docx");
        await using (var file = File.Create(docxPath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            var documentEntry = archive.CreateEntry("word/document.xml");
            await using var stream = documentEntry.Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Capitolo Milo</w:t></w:r></w:p><w:p><w:r><w:t>Il faro nella tempesta.</w:t></w:r></w:p></w:body></w:document>");
        }

        var material = await MaterialImporter.ImportAsync(docxPath);
        material.ExtractedText = await EditorialTextExtractor.ExtractAsync(docxPath);
        var nodes = ContentStructureAnalyzer.Analyze(material);
        Require(material.Kind == "DOCX", "Il DOCX non è stato classificato correttamente.");
        Require(material.ExtractedText.Contains("Capitolo Milo", StringComparison.Ordinal), "Il testo DOCX non è stato estratto.");
        Require(nodes.Any(n => n.Kind == "Chapter" && n.Title == "Capitolo Milo"), "Il capitolo DOCX non è diventato un nodo editoriale.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SELF-TEST: " + message);
    }
}
