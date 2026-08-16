using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-longform-book-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    foreach (var bookType in new[] { BookTypeProfileService.Novel, BookTypeProfileService.EssayManual })
    {
        var project = ProjectFileStore.Create("Long-form " + bookType);
        BookTypeProfileService.Set(project, bookType);
        project.EditionMetadata.Title = "Edizione " + bookType;
        project.EditionMetadata.Language = "it";

        var emptyState = DiezLongFormFinalizationBridge.Readiness(project);
        Require(!emptyState.EditoriallyReady && emptyState.EditableContentCount == 0,
            $"{bookType}: un progetto senza Master non deve essere pronto.");

        var source = Path.Combine(tempRoot, (bookType == BookTypeProfileService.Novel ? "romanzo" : "manuale") + ".txt");
        await File.WriteAllTextAsync(source, "Testo sorgente incorporato e immutabile.");
        var material = await MaterialImporter.ImportAsync(source);
        material.ExtractedText = await File.ReadAllTextAsync(source);
        project.Materials.Add(material);

        var chapter1 = new ContentNode
        {
            MaterialId = material.MaterialId,
            Kind = "Chapter",
            Title = "Capitolo 1",
            Body = "Contenuto editoriale completo del primo capitolo.",
            Ordinal = 1
        };
        var chapter2 = new ContentNode
        {
            MaterialId = material.MaterialId,
            Kind = "Chapter",
            Title = "Capitolo 2",
            Body = "Contenuto editoriale completo del secondo capitolo.",
            Ordinal = 2
        };
        project.ContentNodes.Add(chapter1);
        project.ContentNodes.Add(chapter2);

        var ready = DiezLongFormFinalizationBridge.Readiness(project);
        Require(ready.EditoriallyReady && ready.ChapterCount == 2 && ready.EmptyContentCount == 0,
            $"{bookType}: due capitoli completi devono superare il gate editoriale familiare.");
        Require(ready.Checks.Any(check => check.Code == "CHAPTER_TARGET" && check.Severity == "Info"),
            $"{bookType}: il numero capitoli impostato deve restare indicativo e non HARD.");

        chapter2.Body = string.Empty;
        var emptyChapter = DiezLongFormFinalizationBridge.Readiness(project);
        Require(!emptyChapter.EditoriallyReady && emptyChapter.EmptyContentCount == 1,
            $"{bookType}: un capitolo vuoto deve bloccare la readiness.");
        chapter2.Body = "Contenuto ripristinato.";

        var issue = new ConsistencyIssue
        {
            IssueId = Guid.NewGuid(),
            Signature = "PIANIST-BLOCKING",
            Severity = "Error",
            Code = "CONTRADICTION",
            Message = "Contraddizione artificiale del pianista.",
            Status = "Open"
        };
        project.ConsistencyIssues.Add(issue);
        Require(!DiezLongFormFinalizationBridge.Readiness(project).EditoriallyReady,
            $"{bookType}: una issue Error aperta deve bloccare il gate long-form.");
        issue.Status = "Resolved";
        Require(DiezLongFormFinalizationBridge.Readiness(project).EditoriallyReady,
            $"{bookType}: risolta la contraddizione, il gate deve recuperare.");

        var path = Path.Combine(tempRoot, (bookType == BookTypeProfileService.Novel ? "romanzo" : "manuale") + ".diez");
        await ProjectFileStore.SaveAsync(path, project);
        Require(project.Materials.All(m => m.IsEmbedded), $"{bookType}: il materiale sorgente deve essere incorporato prima del freeze.");

        var freeze = EditionFreezeService.CreateFreeze(project, "Pianista long-form");
        Require(freeze.Freeze is not null, $"{bookType}: deve essere possibile creare Edition Freeze.");
        Require(EditionFreezeService.RunPreflight(project).Ready,
            $"{bookType}: il preflight generico deve essere READY dopo il gate familiare.");
        var candidate = PublicationCandidateService.Create(project);
        Require(candidate.Candidate is not null && PublicationCandidateService.IsLatestCandidateCurrent(project),
            $"{bookType}: deve essere creato un Publication Candidate corrente.");

        var publication = Path.Combine(tempRoot, (bookType == BookTypeProfileService.Novel ? "romanzo" : "manuale") + "-publication.zip");
        var exported = await PublicationCandidateService.ExportPackageAsync(project, publication);
        Require(exported.Exported && File.Exists(publication) && new FileInfo(publication).Length > 0,
            $"{bookType}: il pacchetto pubblicazione finale deve essere esportabile.");

        var edit = EditableMasterService.ApplyManualEdit(project, chapter1.ContentId,
            chapter1.Body + " Modifica dopo la pubblicazione.", "Pianista stale freeze");
        Require(edit.Changed, $"{bookType}: la modifica manuale del Master deve essere applicata.");
        Require(!EditionFreezeService.IsLatestFreezeCurrent(project) && !PublicationCandidateService.IsLatestCandidateCurrent(project),
            $"{bookType}: la modifica post-freeze deve rendere obsoleti freeze e candidate.");
        Require(!(await PublicationCandidateService.ExportPackageAsync(project,
                Path.Combine(tempRoot, "stale-" + Guid.NewGuid().ToString("N") + ".zip"))).Exported,
            $"{bookType}: l'export deve restare bloccato finché non si crea un nuovo freeze/candidate.");

        await ProjectFileStore.SaveAsync(path, project);
        var reloaded = await ProjectFileStore.LoadAsync(path);
        Require(DiezLongFormFinalizationBridge.Readiness(reloaded).EditoriallyReady,
            $"{bookType}: la readiness editoriale deve sopravvivere al round-trip .diez.");
    }

    Console.WriteLine("LONG-FORM BOOK PIANIST PASS: Novel and Essay/Manual blocked empty content and open contradictions, published through Freeze/Candidate, invalidated stale editions after edits, and survived package round-trip.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
