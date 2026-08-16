using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-core-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var sourcePath = Path.Combine(tempRoot, "stress.md");
    await File.WriteAllTextAsync(sourcePath,
        "# Capitolo 1\nAnna ha 30 anni. Anna vive a Napoli.\n\n# Capitolo 2\nAnna ha 31 anni. Anna vive a Napoli.\n");

    var project = ProjectFileStore.Create("Pianist stress");
    var firstImport = await MaterialImporter.ImportAsync(sourcePath);
    var secondImport = await MaterialImporter.ImportAsync(sourcePath);
    Require(firstImport.Sha256 == secondImport.Sha256, "Repeated import must produce a stable fingerprint.");

    firstImport.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
    project.Materials.Add(firstImport);
    var nodes = ContentStructureAnalyzer.Analyze(firstImport);
    Require(nodes.Count >= 3, "Expected document and chapter structure.");
    project.ContentNodes.AddRange(nodes);

    var graph = ContentGraphEngine.Analyze(project, firstImport, nodes);
    Require(graph.EntitiesCreated > 0, "Expected at least one graph entity.");
    var anna = project.Entities.FirstOrDefault(e => string.Equals(e.Name, "Anna", StringComparison.OrdinalIgnoreCase));
    Require(anna is not null, "Expected Anna entity candidate.");
    Require(ContentGraphEngine.ConfirmEntity(project, anna!.EntityId), "Entity confirmation should succeed.");
    Require(project.ConsistencyIssues.Any(i => i.Code == "FACT_CONTRADICTION"), "Contradictory ages should be detected.");

    var issue = project.ConsistencyIssues.First(i => i.Code == "FACT_CONTRADICTION");
    Require(ConsistencyReviewService.MarkReviewed(project, issue.IssueId, "pianist review"), "Issue review should succeed.");
    ConsistencyEngine.Rebuild(project);
    Require(project.ConsistencyIssues.First(i => i.Signature == issue.Signature).Status == "Reviewed",
        "Issue status must survive a rebuild.");

    var secondChapter = project.ContentNodes.First(n => n.Body.Contains("31 anni", StringComparison.Ordinal));
    var edit = EditableMasterService.ApplyManualEdit(project, secondChapter.ContentId,
        secondChapter.Body.Replace("31 anni", "30 anni", StringComparison.Ordinal), "stress correction");
    Require(edit.Changed, "Manual edit should be applied.");
    Require(EditableMasterService.ManualRevisionCount(project, secondChapter.ContentId) == 1,
        "Manual edit history must be retained.");
    Require(!project.ConsistencyIssues.Any(i => i.Code == "FACT_CONTRADICTION" && i.Status == "Open"),
        "Corrected contradiction must not remain open.");

    // Pianist across the whole framework: every current book family must be a first-class,
    // stable routing identity rather than an incidental UI string.
    Require(BookTypeProfileService.All.Length == 10, "The canonical framework must expose all ten book types.");
    Require(BookTypeProfileService.All.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10,
        "Canonical book types must be unique.");

    var identityBeforeBookTypeHammer = project.ProjectId;
    var materialCountBeforeBookTypeHammer = project.Materials.Count;
    var contentIdsBeforeBookTypeHammer = project.ContentNodes.Select(n => n.ContentId).Order().ToArray();
    var revisionCountBeforeBookTypeHammer = EditableMasterService.ManualRevisionCount(project, secondChapter.ContentId);

    for (var round = 0; round < 4; round++)
    {
        foreach (var bookType in BookTypeProfileService.All)
        {
            BookTypeProfileService.Set(project, bookType);
            Require(string.Equals(BookTypeProfileService.Get(project), bookType, StringComparison.Ordinal),
                $"Book type must round-trip exactly: {bookType}.");
        }
    }

    Require(project.ProjectId == identityBeforeBookTypeHammer, "Book-type hammering must not change project identity.");
    Require(project.Materials.Count == materialCountBeforeBookTypeHammer, "Book-type hammering must not duplicate/remove materials.");
    Require(project.ContentNodes.Select(n => n.ContentId).Order().SequenceEqual(contentIdsBeforeBookTypeHammer),
        "Book-type hammering must not change shared content identity.");
    Require(EditableMasterService.ManualRevisionCount(project, secondChapter.ContentId) == revisionCountBeforeBookTypeHammer,
        "Book-type hammering must not lose revision history.");

    // Stable subject identity under stressful editing: names are mutable presentation,
    // SubjectId is the identity used by downstream consistency/scene participation.
    BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
    var cast = MultiSubjectProfileService.Load(project);
    cast.Enabled = true;
    MultiSubjectProfileService.SetCount(cast, 3);
    var initialSubjects = MultiSubjectProfileService.ActiveSubjects(cast).ToList();
    Require(initialSubjects.Count == 3, "Pianist cast should expose three active subjects.");
    var firstSubjectId = initialSubjects[0].SubjectId;
    var removedSubjectId = initialSubjects[2].SubjectId;

    Require(MultiSubjectProfileService.TryRename(cast, initialSubjects[0], "Anna protagonista", out _),
        "Subject rename should succeed.");
    Require(initialSubjects[0].SubjectId == firstSubjectId, "Renaming a subject must never change SubjectId.");
    Require(!MultiSubjectProfileService.TryRename(cast, initialSubjects[1], "Anna protagonista", out _),
        "Duplicate active subject names must be rejected safely.");

    MultiSubjectProfileService.RemoveFromActiveCast(cast, removedSubjectId);
    Require(MultiSubjectProfileService.ActiveSubjects(cast).All(s => s.SubjectId != removedSubjectId),
        "Removed subject must leave the active cast.");
    var reactivated = MultiSubjectProfileService.Add(cast);
    Require(reactivated.SubjectId == removedSubjectId,
        "Reactivating a non-archived cast member must preserve its stable SubjectId/history.");
    MultiSubjectProfileService.Save(project, cast);

    var castReloadedFromProject = MultiSubjectProfileService.Load(project);
    var renamedReloaded = castReloadedFromProject.Subjects.FirstOrDefault(s => s.SubjectId == firstSubjectId);
    Require(renamedReloaded is not null && renamedReloaded.Name == "Anna protagonista",
        "SubjectId and rename must round-trip through project persistence.");
    Require(castReloadedFromProject.Subjects.Select(s => s.SubjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == castReloadedFromProject.Subjects.Count,
        "Subject identities must remain unique after frantic cast edits.");

    StructuredSceneEnvironmentStore.Save(project, "Napoli, luce mediterranea; ambiente generico della serie.");
    Require(StructuredSceneEnvironmentStore.Load(project, string.Empty).Contains("Napoli", StringComparison.Ordinal),
        "Generic scene environment must survive independent from scene-local editing.");

    // A visual job belongs to its active visual session. Frantic switching to another
    // family must archive it instead of leaking it into the next book-type workflow.
    BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
    var visualJob = AiProductionService.CreateJob(
        project,
        AiProductionService.TypeImage,
        "Pianist visual job",
        "Generate a stress-test illustration.");
    Require(project.AiProductionJobs.Any(j => j.JobId == visualJob.JobId), "Visual job should start in the active session.");

    BookTypeProfileService.Set(project, BookTypeProfileService.Novel);
    Require(!project.AiProductionJobs.Any(j => j.JobId == visualJob.JobId),
        "Visual job from Coloring must not leak into Novel after a book-type switch.");
    Require(VisualPromptSessionService.ArchivedJobCount(project) >= 1,
        "Visual job must be retained in archived session history rather than discarded.");

    // Hammer the type switch again after archival. The archived job must never reappear
    // in the operational list even if the user bounces back and forth rapidly.
    for (var i = 0; i < 20; i++)
    {
        BookTypeProfileService.Set(project, i % 2 == 0 ? BookTypeProfileService.ColoringBook : BookTypeProfileService.Novel);
        Require(!project.AiProductionJobs.Any(j => j.JobId == visualJob.JobId),
            "Archived visual job must not resurrect during frantic routing.");
    }

    // Stale/invalid selections are expected during frantic interaction and must be harmless.
    for (var i = 0; i < 100; i++)
    {
        Require(!ConsistencyReviewService.MarkResolved(project, Guid.NewGuid()), "Unknown issue id must be harmless.");
        Require(!RevisionCandidateService.Approve(project, Guid.NewGuid()), "Unknown candidate id must be harmless.");
        Require(!RevisionCandidateService.Reject(project, Guid.NewGuid()), "Unknown candidate id must be harmless.");
        Require(!IllustrationPlanService.Remove(project, Guid.NewGuid()), "Unknown placement id must be harmless.");
        Require(!EditableMasterService.ApplyManualEdit(project, Guid.NewGuid(), "noise").Changed,
            "Unknown content id must be harmless.");
    }

    var packagePath = Path.Combine(tempRoot, "pianist.diez");
    await ProjectFileStore.SaveAsync(packagePath, project);
    Require(ProjectFileStore.IsPackageFile(packagePath), "Saved project must be a .diez package.");
    Require(firstImport.IsEmbedded, "Imported original must be embedded after save.");

    // Hammer Save repeatedly/concurrently: ProjectFileStore must serialize access safely.
    for (var round = 0; round < 5; round++)
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ProjectFileStore.SaveAsync(packagePath, project)));

    var reloaded = await ProjectFileStore.LoadAsync(packagePath);
    Require(reloaded.ProjectId == project.ProjectId, "Project identity must survive stress saves.");
    Require(reloaded.Materials.Count == 1, "Stress saves must not duplicate materials.");
    Require(reloaded.ContentNodes.Count == project.ContentNodes.Count, "Content structure must survive stress saves.");
    Require(EditableMasterService.ManualRevisionCount(reloaded, secondChapter.ContentId) == 1,
        "Manual revision history must survive save/reload.");
    Require(string.Equals(BookTypeProfileService.Get(reloaded), BookTypeProfileService.Get(project), StringComparison.Ordinal),
        "Active book type must survive stress save/reload.");
    Require(VisualPromptSessionService.ArchivedJobCount(reloaded) >= 1,
        "Archived visual session history must survive stress save/reload.");
    var persistedCast = MultiSubjectProfileService.Load(reloaded);
    Require(persistedCast.Subjects.Any(s => s.SubjectId == firstSubjectId && s.Name == "Anna protagonista"),
        "Stable SubjectId and user rename must survive package stress saves.");
    Require(StructuredSceneEnvironmentStore.Load(reloaded, string.Empty).Contains("Napoli", StringComparison.Ordinal),
        "Generic scene environment must survive package stress saves.");

    var embedded = await ProjectFileStore.ReadEmbeddedMaterialAsync(packagePath, reloaded.Materials[0]);
    Require(embedded is { Length: > 0 }, "Embedded source must survive repeated package rewrites.");

    Console.WriteLine("PIANIST CORE PASS: all book families, stable subjects, repeated, stale and concurrent actions preserved project integrity.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
