using System.IO.Compression;
using System.Text;

namespace DiezPublishingStudio;

internal static class PublicationCandidateSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezPublicationCandidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "publication-source.txt");
            const string originalText = "Capitolo 1\nMilo guarda il mare.\n\nCapitolo 2\nMilo torna al Faro.";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.UTF8);

            var material = await MaterialImporter.ImportAsync(sourcePath);
            material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
            var project = ProjectFileStore.Create("Publication Candidate Test");
            project.Materials.Add(material);
            project.ContentNodes.AddRange(ContentStructureAnalyzer.Analyze(material));
            EditionMetadataService.Update(project, "Publication Candidate Test", "", "Autore Test", "it", "Diez", "9780306406157", "Test pacchetto editoriale");

            var projectPath = Path.Combine(root, "publication-candidate.diez");
            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);

            var blockedWithoutFreeze = PublicationCandidateService.Create(project);
            Require(blockedWithoutFreeze.Candidate is null,
                "Un Publication Candidate non deve nascere senza Edition Freeze e preflight READY.");

            var freeze = EditionFreezeService.CreateFreeze(project);
            Require(freeze.Freeze is not null, "Edition Freeze non creato nel test Publication Candidate.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe essere READY prima del Publication Candidate.");

            var created = PublicationCandidateService.Create(project);
            Require(created.Candidate is not null, "Publication Candidate non creato dopo preflight READY.");
            Require(PublicationCandidateService.Count(project) == 1, "Conteggio Publication Candidate errato.");
            Require(PublicationCandidateService.IsLatestCandidateCurrent(project), "Il Publication Candidate dovrebbe essere corrente.");
            Require(created.Candidate!.ProposedBody.Contains("Milo guarda il mare", StringComparison.Ordinal),
                "Il Publication Candidate non contiene il primo capitolo.");
            Require(created.Candidate.ProposedBody.Contains("Milo torna al Faro", StringComparison.Ordinal),
                "Il Publication Candidate non contiene il secondo capitolo.");

            var duplicate = PublicationCandidateService.Create(project);
            Require(duplicate.Candidate?.CandidateId == created.Candidate.CandidateId,
                "Lo stesso Edition Freeze non deve generare Publication Candidate duplicati.");

            await ProjectFileStore.SaveAsync(projectPath, project);
            project = await ProjectFileStore.LoadAsync(projectPath);
            Require(PublicationCandidateService.Count(project) == 1,
                "Publication Candidate non persistito nel round-trip .diez.");
            Require(PublicationCandidateService.IsLatestCandidateCurrent(project),
                "Publication Candidate non corrente dopo riapertura del .diez.");

            var packagePath = Path.Combine(root, "publication-package.zip");
            var exported = await PublicationCandidateService.ExportPackageAsync(project, packagePath);
            Require(exported.Exported && File.Exists(packagePath), "Pacchetto ZIP editoriale non esportato.");

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                Require(archive.GetEntry("master.txt") is not null, "Nel pacchetto manca master.txt.");
                Require(archive.GetEntry("metadata.json") is not null, "Nel pacchetto manca metadata.json.");
                Require(archive.GetEntry("edition-manifest.json") is not null, "Nel pacchetto manca edition-manifest.json.");
                Require(archive.GetEntry("preflight.txt") is not null, "Nel pacchetto manca preflight.txt.");
            }

            var persistedCandidate = PublicationCandidateService.GetLatest(project)!;
            var immutableBody = persistedCandidate.ProposedBody;
            var chapter2 = project.ContentNodes.First(n => n.Kind == "Chapter" && n.Title.Contains("2", StringComparison.OrdinalIgnoreCase));
            var edit = EditableMasterService.ApplyManualEdit(project, chapter2.ContentId,
                chapter2.Body.Replace("Faro", "Porto", StringComparison.OrdinalIgnoreCase),
                "Modifica dopo Publication Candidate");
            Require(edit.Changed, "La modifica del Master dopo Publication Candidate non è stata applicata.");
            Require(!PublicationCandidateService.IsLatestCandidateCurrent(project),
                "Il Publication Candidate dovrebbe risultare superato dopo una modifica del Master.");
            Require(persistedCandidate.ProposedBody == immutableBody,
                "Il Publication Candidate è stato alterato da una modifica successiva del Master.");

            var blockedAfterEdit = PublicationCandidateService.Create(project);
            Require(blockedAfterEdit.Candidate is null,
                "Non deve essere possibile creare un nuovo Publication Candidate con preflight bloccato.");

            var secondFreeze = EditionFreezeService.CreateFreeze(project, "Freeze dopo modifica del Master");
            Require(secondFreeze.Freeze is not null, "Secondo Edition Freeze non creato.");
            Require(EditionFreezeService.RunPreflight(project).Ready, "Il preflight dovrebbe tornare READY dopo il secondo freeze.");
            var secondCandidate = PublicationCandidateService.Create(project);
            Require(secondCandidate.Candidate is not null && PublicationCandidateService.Count(project) == 2,
                "Secondo Publication Candidate non creato.");
            Require(secondCandidate.Candidate!.ProposedBody.Contains("Porto", StringComparison.Ordinal),
                "Il secondo Publication Candidate non riflette il Master aggiornato.");
            Require(persistedCandidate.ProposedBody.Contains("Faro", StringComparison.Ordinal),
                "Il primo Publication Candidate non ha conservato la versione precedente.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PUBLICATION-CANDIDATE SELF-TEST: " + message);
    }
}