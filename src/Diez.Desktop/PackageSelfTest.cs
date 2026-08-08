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
            await VerifyEmbeddedMaterialStructureGraphAndBibleRoundTripAsync(root);
            await VerifyLegacyProjectMigrationAsync(root);
            await VerifyDocxIntakeAndStructureAsync(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static async Task VerifyEmbeddedMaterialStructureGraphAndBibleRoundTripAsync(string root)
    {
        var sourcePath = Path.Combine(root, "manoscritto.txt");
        const string sourceContent =
            "Capitolo 1\nMilo entra nel Faro. Milo osserva il Faro. Milo cerca una chiave.\n\n" +
            "Capitolo 2\nMilo torna al Faro durante la tempesta.";
        await File.WriteAllTextAsync(sourcePath, sourceContent, Encoding.UTF8);

        var material = await MaterialImporter.ImportAsync(sourcePath);
        material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
        var project = ProjectFileStore.Create("Self Test");
        project.Materials.Add(material);
        var nodes = ContentStructureAnalyzer.Analyze(material);
        project.ContentNodes.AddRange(nodes);
        var graph = ContentGraphEngine.Analyze(project, material, nodes);

        Require(project.ContentNodes.Count(n => n.Kind == "Chapter") == 2,
            "Il numero di capitoli riconosciuti non è corretto.");
        Require(graph.EntitiesCreated >= 2, "Il Content Graph non ha creato le entità attese.");

        var milo = project.Entities.FirstOrDefault(e => e.Name == "Milo" && e.Kind == "Character");
        var faro = project.Entities.FirstOrDefault(e => e.Name == "Faro" && e.Kind == "Location");
        Require(milo is not null, "Milo non è stato rilevato come personaggio candidato.");
        Require(faro is not null, "Faro non è stato rilevato come luogo candidato.");
        Require(project.Relations.Any(r => r.FromId == milo!.EntityId && r.Type == "LocatedIn" && r.ToId == faro!.EntityId),
            "La relazione Milo LocatedIn Faro non è stata creata.");

        Require(ContentGraphEngine.ConfirmEntity(project, milo!.EntityId), "La conferma di Milo è fallita.");
        Require(!milo.IsCandidate, "Milo è ancora candidato dopo la conferma.");
        Require(project.BibleEntries.Any(b => b.SubjectEntityId == milo.EntityId && b.Key == "canonical_name" && b.Value == "Milo" && b.Authority == "Binding"),
            "Il nome canonico di Milo non è entrato nella Bible.");

        var projectPath = Path.Combine(root, "roundtrip.diez");
        await ProjectFileStore.SaveAsync(projectPath, project);
        Require(ProjectFileStore.IsPackageFile(projectPath), "Il .diez salvato non è un pacchetto ZIP.");

        var loaded = await ProjectFileStore.LoadAsync(projectPath);
        Require(loaded.SchemaVersion == 5, "Schema .diez inatteso.");
        Require(loaded.Materials.Count == 1, "Il materiale non è sopravvissuto al round-trip.");
        Require(loaded.ContentNodes.Count == project.ContentNodes.Count, "La struttura editoriale non è sopravvissuta al round-trip.");
        Require(loaded.Entities.Any(e => e.Name == "Milo" && !e.IsCandidate), "Milo confermato non è sopravvissuto al round-trip.");
        Require(loaded.Entities.Any(e => e.Name == "Faro" && e.Kind == "Location"), "Faro non è sopravvissuto al round-trip.");
        Require(loaded.Relations.Any(r => r.Type == "LocatedIn"), "Le relazioni del Content Graph non sono sopravvissute.");
        Require(loaded.BibleEntries.Any(b => b.Key == "canonical_name" && b.Value == "Milo" && b.Authority == "Binding"),
            "La Bible non è sopravvissuta al round-trip.");
        Require(loaded.Materials[0].IsEmbedded, "Il materiale non risulta incorporato.");

        var embedded = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, loaded.Materials[0]);
        Require(embedded is not null && Encoding.UTF8.GetString(embedded).Contains("Milo entra nel Faro", StringComparison.Ordinal),
            "Il contenuto incorporato non corrisponde alla sorgente.");

        File.Delete(sourcePath);
        await ProjectFileStore.SaveAsync(projectPath, loaded);
        var reloaded = await ProjectFileStore.LoadAsync(projectPath);
        var embeddedAfterSourceRemoval = await ProjectFileStore.ReadEmbeddedMaterialAsync(projectPath, reloaded.Materials[0]);
        Require(embeddedAfterSourceRemoval is not null && Encoding.UTF8.GetString(embeddedAfterSourceRemoval).Contains("Milo entra nel Faro", StringComparison.Ordinal),
            "Lo snapshot è stato perso dopo la rimozione della sorgente.");
        Require(reloaded.BibleEntries.Any(b => b.Value == "Milo"), "La Bible è stata persa dopo un secondo salvataggio.");
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
        Require((await ProjectFileStore.LoadAsync(legacyPath)).SchemaVersion == 5, "Il progetto legacy non è arrivato allo schema 5.");
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
            await writer.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Capitolo Milo</w:t></w:r></w:p><w:p><w:r><w:t>Milo visita il Faro. Milo ritorna al Faro.</w:t></w:r></w:p></w:body></w:document>");
        }

        var material = await MaterialImporter.ImportAsync(docxPath);
        material.ExtractedText = await EditorialTextExtractor.ExtractAsync(docxPath);
        var nodes = ContentStructureAnalyzer.Analyze(material);
        Require(material.Kind == "DOCX", "Il DOCX non è stato classificato correttamente.");
        Require(material.ExtractedText.Contains("Capitolo Milo", StringComparison.Ordinal), "Il testo DOCX non è stato estratto.");
        Require(nodes.Any(n => n.Kind == "Chapter" && n.Title == "Capitolo Milo"), "Il capitolo DOCX non è diventato un nodo editoriale.");

        var project = ProjectFileStore.Create("DOCX Graph");
        project.Materials.Add(material);
        project.ContentNodes.AddRange(nodes);
        ContentGraphEngine.Analyze(project, material, nodes);
        Require(project.Entities.Any(e => e.Name == "Milo"), "Il nome Milo nel DOCX non è entrato nel Content Graph.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SELF-TEST: " + message);
    }
}
