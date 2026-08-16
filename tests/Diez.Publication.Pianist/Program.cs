using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-publication-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var sourcePath = Path.Combine(tempRoot, "source.txt");
    await File.WriteAllTextAsync(sourcePath, "Contenuto editoriale di prova per la pubblicazione Diez.");
    var imagePath = Path.Combine(tempRoot, "visual.png");
    await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z3L8AAAAASUVORK5CYII="));

    foreach (var bookType in BookTypeProfileService.All)
    {
        var safeType = string.Concat(bookType.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
        var project = ProjectFileStore.Create("Publication " + bookType);
        BookTypeProfileService.Set(project, bookType);

        var metadata = EditionMetadataService.Update(
            project,
            title: "Edizione " + bookType,
            subtitle: "Pianist",
            creator: "Diez",
            language: "it",
            publisher: "Diez Publishing Studio",
            isbn: string.Empty,
            description: "Verifica pubblicazione cross-family.");
        Require(metadata.Changed, $"Edition metadata must be editable for {bookType}.");

        var material = await MaterialImporter.ImportAsync(sourcePath);
        material.ExtractedText = await EditorialTextExtractor.ExtractAsync(sourcePath);
        project.Materials.Add(material);
        var chapter = new ContentNode
        {
            MaterialId = material.MaterialId,
            Kind = "Chapter",
            Title = "Capitolo / contenuto principale",
            Body = "Contenuto pubblicabile per " + bookType + ".",
            Ordinal = 1
        };
        project.ContentNodes.Add(chapter);

        if (BookTypeCatalog.IsVisual(bookType))
        {
            var image = await MaterialImporter.ImportAsync(imagePath);
            project.Materials.Add(image);
            VisualBookPlanService.Save(project, 1, consistent: false);
            project.AiProductionJobs.Add(new AiProductionJob
            {
                JobId = Guid.NewGuid(),
                Code = "IMG-001",
                OutputType = AiProductionService.TypeImage,
                Title = "Immagine finale fixture",
                Status = AiProductionService.StatusApplied,
                ResultMaterialId = image.MaterialId,
                TargetContentId = string.Equals(bookType, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase)
                    ? chapter.ContentId
                    : null,
                CreatedAtLocal = DateTimeOffset.Now.ToString("O"),
                UpdatedAtLocal = DateTimeOffset.Now.ToString("O")
            });
            if (string.Equals(bookType, BookTypeProfileService.IllustratedBook, StringComparison.OrdinalIgnoreCase))
            {
                var placement = IllustrationPlanService.Upsert(
                    project,
                    null,
                    image.MaterialId,
                    chapter.ContentId,
                    IllustrationPlanService.AfterContent,
                    80,
                    "Immagine fixture");
                Require(placement.Placement is not null, "Illustrated Book fixture must have a valid illustration placement.");
            }
        }

        var projectPath = Path.Combine(tempRoot, safeType + ".diez");
        await ProjectFileStore.SaveAsync(projectPath, project);
        Require(material.IsEmbedded, $"Source material must be embedded before publication for {bookType}.");
        Require(project.Materials.All(m => m.IsEmbedded), $"All fixture materials must be embedded before publication for {bookType}.");

        var freeze = EditionFreezeService.CreateFreeze(project, "Pianist freeze " + bookType);
        Require(freeze.Freeze is not null, $"Edition Freeze must be creatable for {bookType}.");
        Require(EditionFreezeService.IsLatestFreezeCurrent(project), $"New Edition Freeze must be current for {bookType}.");

        var preflight = EditionFreezeService.RunPreflight(project);
        Require(preflight.Ready, $"Minimal valid project must pass publication preflight for {bookType}: {preflight.Summary}");

        var candidate = PublicationCandidateService.Create(project);
        Require(candidate.Candidate is not null, $"Publication Candidate must be creatable for {bookType}.");
        Require(PublicationCandidateService.IsLatestCandidateCurrent(project),
            $"Publication Candidate must match the current freeze for {bookType}.");

        var packagePath = Path.Combine(tempRoot, safeType + "-publication.zip");
        var exported = await PublicationCandidateService.ExportPackageAsync(project, packagePath);
        Require(exported.Exported && File.Exists(packagePath) && new FileInfo(packagePath).Length > 0,
            $"Publication ZIP must export for {bookType}.");

        var masterCsv = Path.Combine(tempRoot, safeType + "-master.csv");
        var csvHandoff = await HandoffExportService.ExportMasterCsvAsync(project, masterCsv);
        Require(csvHandoff.Exported && File.Exists(masterCsv) && new FileInfo(masterCsv).Length > 0,
            $"Master CSV handoff must export for {bookType}.");
        var masterXlsx = Path.Combine(tempRoot, safeType + "-master.xlsx");
        var xlsxHandoff = await HandoffExportService.ExportMasterXlsxAsync(project, masterXlsx);
        Require(xlsxHandoff.Exported && File.Exists(masterXlsx) && new FileInfo(masterXlsx).Length > 0,
            $"Master XLSX handoff must export for {bookType}.");

        var edit = EditableMasterService.ApplyManualEdit(
            project,
            chapter.ContentId,
            chapter.Body + " Modifica dopo freeze.",
            "pianist post-freeze edit");
        Require(edit.Changed, $"Post-freeze edit should be applied for {bookType}.");
        Require(!EditionFreezeService.IsLatestFreezeCurrent(project),
            $"A post-freeze Master edit must invalidate the freeze for {bookType}.");
        Require(!PublicationCandidateService.IsLatestCandidateCurrent(project),
            $"A stale freeze must make the publication candidate non-current for {bookType}.");
        var blockedExport = await PublicationCandidateService.ExportPackageAsync(
            project,
            Path.Combine(tempRoot, safeType + "-stale.zip"));
        Require(!blockedExport.Exported,
            $"Publication export must be blocked after a post-freeze edit for {bookType}.");
        var blockedHandoff = await HandoffExportService.ExportMasterCsvAsync(
            project,
            Path.Combine(tempRoot, safeType + "-stale-master.csv"));
        Require(!blockedHandoff.Exported,
            $"Master handoff must also be blocked while the edition freeze is stale for {bookType}.");

        var newFreeze = EditionFreezeService.CreateFreeze(project, "Pianist freeze after edit");
        Require(newFreeze.Freeze is not null && EditionFreezeService.FreezeCount(project) == 2,
            $"A fresh freeze must be creatable after edits for {bookType}.");
        Require(EditionFreezeService.RunPreflight(project).Ready,
            $"Preflight must recover after creating a fresh freeze for {bookType}.");
        var newCandidate = PublicationCandidateService.Create(project);
        Require(newCandidate.Candidate is not null && PublicationCandidateService.Count(project) == 2,
            $"A new publication candidate must be created for the new freeze for {bookType}.");

        await ProjectFileStore.SaveAsync(projectPath, project);
        var reloaded = await ProjectFileStore.LoadAsync(projectPath);
        Require(BookTypeProfileService.Get(reloaded) == bookType,
            $"Book type must survive publication save/reload for {bookType}.");
        Require(EditionFreezeService.FreezeCount(reloaded) == 2 && PublicationCandidateService.Count(reloaded) == 2,
            $"Freeze/candidate history must survive save/reload for {bookType}.");
        Require(PublicationCandidateService.IsLatestCandidateCurrent(reloaded),
            $"Latest publication candidate must remain current after save/reload for {bookType}.");
    }

    Require(EditionMetadataService.IsValidIsbn("978-0-306-40615-7"), "Valid ISBN-13 must be accepted.");
    Require(!EditionMetadataService.IsValidIsbn("978-0-306-40615-8"), "Invalid ISBN-13 must be rejected.");

    Console.WriteLine("PUBLICATION PIANIST PASS: all ten book types survived type-valid fixtures, metadata, freeze, preflight, candidate, handoff, stale-edit blocking and regeneration.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
