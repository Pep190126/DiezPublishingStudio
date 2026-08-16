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

    var embedded = await ProjectFileStore.ReadEmbeddedMaterialAsync(packagePath, reloaded.Materials[0]);
    Require(embedded is { Length: > 0 }, "Embedded source must survive repeated package rewrites.");

    Console.WriteLine("PIANIST CORE PASS: repeated, stale and concurrent framework actions preserved project integrity.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
