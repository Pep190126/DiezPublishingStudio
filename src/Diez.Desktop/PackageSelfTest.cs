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
            await VerifyConsistencyReviewLifecycleAsync(root);
            await VerifyRevisionCandidateLifecycleAsync(root);
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
            "Capitolo 1\nMilo entra nel Faro. Milo osserva il Faro. Milo cerca una chiave. Milo ha gli occhi blu.\n\n" +
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
        Require(project.ConsistencyFacts.Any(f => f.SubjectEntityId == milo.EntityId && f.Key == "eye_color" && f.Value.Equals("blu", StringComparison.OrdinalIgnoreCase)),
            "Il Consistency Engine non ha estratto il colore degli occhi.");

        var projectPath = Path.Combine(root, "roundtrip.diez");
        await ProjectFileStore.SaveAsync(projectPath, project);
        Require(ProjectFileStore.IsPackageFile(projectPath), "Il .diez salvato non è un pacchetto ZIP.");

        var loaded = await ProjectFileStore.LoadAsync(projectPath);
        Require(loaded.SchemaVersion == 10, "Schema .diez inatteso.");
        Require(loaded.Materials.Count == 1, "Il materiale non è sopravvissuto al round-trip.");
        Require(loaded.ContentNodes.Count == project.ContentNodes.Count, "La struttura editoriale non è sopravvissuta al round-trip.");
        Require(loaded.Entities.Any(e => e.Name == "Milo" && !e.IsCandidate), "Milo confermato non è sopravvissuto al round-trip.");
        Require(loaded.Entities.Any(e => e.Name == "Faro" && e.Kind == "Location"), "Faro non è sopravvissuto al round-trip.");
        Require(loaded.Relations.Any(r => r.Type == "LocatedIn"), "Le relazioni del Content Graph non sono sopravvissute.");
        Require(loaded.BibleEntries.Any(b => b.Key == "canonical_name" && b.Value == "Milo" && b.Authority == "Binding"),
            "La Bible non è sopravvissuta al round-trip.");
        Require(loaded.ConsistencyFacts.Any(f => f.Key == "eye_color" && f.Value.Equals("blu", StringComparison.OrdinalIgnoreCase)),
            "I fatti di coerenza non sono sopravvissuti al round-trip.");
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

    private static async Task VerifyConsistencyReviewLifecycleAsync(string root)
    {
        var project = CreateMiloContradictionProject();
        var milo = project.Entities.Single(e => e.Name == "Milo");
        var chapter1 = project.ContentNodes.Single(n => n.Ordinal == 1);
        var chapter2 = project.ContentNodes.Single(n => n.Ordinal == 2);
        var originalChapter1 = chapter1.Body;
        var originalChapter2 = chapter2.Body;

        var analysis = ConsistencyEngine.Rebuild(project);
        Require(analysis.FactsDetected == 2, "Il Consistency Engine non ha rilevato entrambi i fatti.");
        var issue = project.ConsistencyIssues.SingleOrDefault(i => i.Code == "FACT_CONTRADICTION" && i.SubjectEntityId == milo.EntityId);
        Require(issue is not null, "La contraddizione blu/verdi non è stata rilevata.");
        Require(!string.IsNullOrWhiteSpace(issue!.Signature), "Il problema non ha una firma stabile.");
        var issueId = issue.IssueId;
        var signature = issue.Signature;

        Require(ConsistencyReviewService.AcceptException(project, issueId), "La decisione di eccezione non è stata registrata.");
        Require(issue.Status == "AcceptedException", "Lo stato AcceptedException non è stato applicato.");
        Require(project.ConsistencyResolutions.Count == 1, "La cronologia di revisione non contiene la decisione.");
        Require(chapter1.Body == originalChapter1 && chapter2.Body == originalChapter2,
            "La revisione ha modificato il manoscritto senza approvazione editoriale.");

        ConsistencyEngine.Rebuild(project);
        var rebuiltIssue = project.ConsistencyIssues.Single(i => i.Signature == signature);
        Require(rebuiltIssue.IssueId == issueId, "L'identità del problema non è stabile dopo il rebuild.");
        Require(rebuiltIssue.Status == "AcceptedException", "Lo stato umano è stato perso dopo il rebuild.");

        Require(ConsistencyReviewService.Reopen(project, rebuiltIssue.IssueId), "La riapertura del problema è fallita.");
        Require(ConsistencyReviewService.MarkReviewed(project, rebuiltIssue.IssueId), "Lo stato Reviewed non è stato applicato.");
        Require(ConsistencyReviewService.MarkResolved(project, rebuiltIssue.IssueId), "Lo stato Resolved non è stato applicato.");
        Require(project.ConsistencyResolutions.Count == 4, "La cronologia non registra tutte le transizioni umane.");
        Require(chapter1.Body == originalChapter1 && chapter2.Body == originalChapter2,
            "Le transizioni di stato hanno modificato il contenuto editoriale.");

        ConsistencyEngine.Rebuild(project);
        Require(project.ConsistencyIssues.Single(i => i.Signature == signature).Status == "Resolved",
            "Lo stato Resolved non è sopravvissuto al rebuild.");

        var path = Path.Combine(root, "consistency-review.diez");
        await ProjectFileStore.SaveAsync(path, project);
        var loaded = await ProjectFileStore.LoadAsync(path);
        Require(loaded.SchemaVersion == 10, "Il progetto di revisione non usa schema 10.");
        ConsistencyEngine.Rebuild(loaded);
        var loadedIssue = loaded.ConsistencyIssues.Single(i => i.Signature == signature);
        Require(loadedIssue.Status == "Resolved", "Lo stato di revisione non è sopravvissuto al round-trip.");
        Require(loaded.ConsistencyResolutions.Count == 4, "La cronologia di revisione non è sopravvissuta al round-trip.");
        Require(loaded.ContentNodes.Single(n => n.ContentId == chapter1.ContentId).Body == originalChapter1 &&
                loaded.ContentNodes.Single(n => n.ContentId == chapter2.ContentId).Body == originalChapter2,
            "Il round-trip di revisione ha alterato il manoscritto.");

        loaded.ContentNodes.Single(n => n.ContentId == chapter2.ContentId).Body = "Milo ha gli occhi blu mentre entra nella stanza.";
        ConsistencyEngine.Rebuild(loaded);
        Require(!loaded.ConsistencyIssues.Any(i => i.Signature == signature),
            "Il problema rimane attivo dopo la correzione effettiva del contenuto.");
        Require(loaded.ConsistencyResolutions.Count == 4,
            "La cronologia umana è stata cancellata quando il problema è scomparso.");
    }

    private static async Task VerifyRevisionCandidateLifecycleAsync(string root)
    {
        var project = CreateMiloContradictionProject();
        ConsistencyEngine.Rebuild(project);
        var issue = project.ConsistencyIssues.Single(i => i.Code == "FACT_CONTRADICTION");
        var chapter2 = project.ContentNodes.Single(n => n.Ordinal == 2);
        var originalBody = chapter2.Body;

        var creation = RevisionCandidateService.CreateForIssue(project, issue.IssueId);
        Require(creation.Candidate is not null, "La proposta di revisione non è stata creata.");
        var candidate = creation.Candidate!;
        Require(candidate.Status == "Proposed", "La nuova proposta non è nello stato Proposed.");
        Require(chapter2.Body == originalBody, "La creazione della proposta ha modificato il contenuto.");
        Require(candidate.ProposedBody.Contains("occhi blu", StringComparison.OrdinalIgnoreCase),
            "La proposta non contiene la correzione attesa verso il primo fatto coerente.");
        Require(candidate.ProposedBody != candidate.OriginalBody, "La proposta non differisce dal contenuto originale.");

        Require(RevisionCandidateService.Approve(project, candidate.CandidateId), "L'approvazione della proposta è fallita.");
        Require(candidate.Status == "Approved", "La proposta non è stata marcata Approved.");
        Require(chapter2.Body == originalBody, "L'approvazione ha modificato il contenuto prima dell'applicazione esplicita.");

        var path = Path.Combine(root, "revision-candidate.diez");
        await ProjectFileStore.SaveAsync(path, project);
        var loaded = await ProjectFileStore.LoadAsync(path);
        Require(loaded.SchemaVersion == 10, "Il progetto con Revision Candidate non usa schema 10.");
        var loadedCandidate = loaded.RevisionCandidates.Single(c => c.CandidateId == candidate.CandidateId);
        Require(loadedCandidate.Status == "Approved", "Lo stato Approved della proposta non è sopravvissuto al round-trip.");
        Require(loaded.ContentNodes.Single(n => n.ContentId == chapter2.ContentId).Body == originalBody,
            "Il salvataggio di una proposta approvata ha alterato il contenuto.");

        var apply = RevisionCandidateService.ApplyApproved(loaded, loadedCandidate.CandidateId);
        Require(apply.Applied, "L'applicazione esplicita della proposta approvata è fallita.");
        var corrected = loaded.ContentNodes.Single(n => n.ContentId == chapter2.ContentId);
        Require(corrected.Body.Contains("occhi blu", StringComparison.OrdinalIgnoreCase) &&
                !corrected.Body.Contains("occhi verdi", StringComparison.OrdinalIgnoreCase),
            "La proposta approvata non ha modificato il contenuto come previsto.");
        Require(loadedCandidate.Status == "Applied", "La proposta applicata non è nello stato Applied.");
        Require(!loaded.ConsistencyIssues.Any(i => i.Code == "FACT_CONTRADICTION"),
            "La contraddizione rimane dopo l'applicazione della correzione.");

        await ProjectFileStore.SaveAsync(path, loaded);
        var appliedRoundTrip = await ProjectFileStore.LoadAsync(path);
        Require(appliedRoundTrip.RevisionCandidates.Single(c => c.CandidateId == candidate.CandidateId).Status == "Applied",
            "Lo stato Applied non è sopravvissuto al round-trip.");
        Require(appliedRoundTrip.ContentNodes.Single(n => n.ContentId == chapter2.ContentId).Body.Contains("occhi blu", StringComparison.OrdinalIgnoreCase),
            "La revisione applicata non è sopravvissuta al round-trip.");

        var staleProject = CreateMiloContradictionProject();
        ConsistencyEngine.Rebuild(staleProject);
        var staleIssue = staleProject.ConsistencyIssues.Single(i => i.Code == "FACT_CONTRADICTION");
        var staleCreation = RevisionCandidateService.CreateForIssue(staleProject, staleIssue.IssueId);
        Require(staleCreation.Candidate is not null, "La proposta per il test anti-sovrascrittura non è stata creata.");
        var staleCandidate = staleCreation.Candidate!;
        Require(RevisionCandidateService.Approve(staleProject, staleCandidate.CandidateId), "L'approvazione della proposta stale è fallita.");
        var staleNode = staleProject.ContentNodes.Single(n => n.ContentId == staleCandidate.ContentId);
        staleNode.Body += " Modifica umana successiva.";
        var humanEditedBody = staleNode.Body;
        var staleApply = RevisionCandidateService.ApplyApproved(staleProject, staleCandidate.CandidateId);
        Require(!staleApply.Applied, "Diez ha sovrascritto un contenuto cambiato dopo la creazione della proposta.");
        Require(staleNode.Body == humanEditedBody, "Il controllo stale ha alterato la modifica umana successiva.");
    }

    private static PreviewProject CreateMiloContradictionProject()
    {
        var project = ProjectFileStore.Create("Milo Contradiction");
        var materialId = Guid.NewGuid();
        var milo = new GraphEntity
        {
            EntityId = Guid.NewGuid(),
            Kind = "Character",
            Name = "Milo",
            IsCandidate = false,
            SourceMaterialId = materialId,
            Notes = "Personaggio confermato"
        };
        project.Entities.Add(milo);
        project.BibleEntries.Add(new BibleEntry
        {
            SubjectEntityId = milo.EntityId,
            Key = "canonical_name",
            Value = "Milo",
            Authority = "Binding",
            IsActive = true
        });
        project.ContentNodes.Add(new ContentNode
        {
            MaterialId = materialId,
            Kind = "Chapter",
            Title = "Capitolo 1",
            Body = "Milo ha gli occhi blu e guarda il mare.",
            Ordinal = 1,
            SourceLocator = "Capitolo 1"
        });
        project.ContentNodes.Add(new ContentNode
        {
            MaterialId = materialId,
            Kind = "Chapter",
            Title = "Capitolo 2",
            Body = "Milo ha gli occhi verdi mentre entra nella stanza.",
            Ordinal = 2,
            SourceLocator = "Capitolo 2"
        });
        return project;
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
        Require((await ProjectFileStore.LoadAsync(legacyPath)).SchemaVersion == 10, "Il progetto legacy non è arrivato allo schema 10.");
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