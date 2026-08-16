using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var tempRoot = Path.Combine(Path.GetTempPath(), "diez-scene-pianist-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var project = ProjectFileStore.Create("Scene pianist");
    BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);

    var subjects = MultiSubjectProfileService.Load(project);
    subjects.Enabled = true;
    MultiSubjectProfileService.SetCount(subjects, 2);
    var activeSubjects = MultiSubjectProfileService.ActiveSubjects(subjects).ToList();
    activeSubjects[0].Name = "Anna";
    activeSubjects[1].Name = "Bruno";
    var annaId = activeSubjects[0].SubjectId;
    MultiSubjectProfileService.Save(project, subjects);

    var scenes = StructuredSceneProfileService.Load(project);
    scenes.Enabled = true;
    StructuredSceneProfileService.SetCount(scenes, 2);
    var initial = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
    Require(initial.Count == 2, "Two active scenes expected.");
    var first = initial[0];
    var second = initial[1];
    var firstId = first.SceneId;
    var archivedId = second.SceneId;

    Require(StructuredSceneProfileService.TryRename(scenes, first, "Concerto a Napoli", out _), "Scene rename should succeed.");
    first.Description = "Anna suona durante il concerto.";
    Require(first.SceneId == firstId, "Rename/edit must never change SceneId.");
    Require(!StructuredSceneProfileService.TryRename(scenes, second, "Concerto a Napoli", out _),
        "Duplicate active scene names must be rejected safely.");

    StructuredSceneProfileService.SetSubjectParticipation(scenes, firstId, annaId, true);
    StructuredSceneProfileService.Save(project, scenes);

    // Rename the subject after scene membership was recorded. Membership must resolve by SubjectId.
    subjects = MultiSubjectProfileService.Load(project);
    var anna = subjects.Subjects.First(s => s.SubjectId == annaId);
    Require(MultiSubjectProfileService.TryRename(subjects, anna, "Anna pianista", out _), "Subject rename should succeed.");
    MultiSubjectProfileService.Save(project, subjects);
    scenes = StructuredSceneProfileService.Load(project);
    first = scenes.Scenes.First(s => s.SceneId == firstId);
    var participants = StructuredSceneProfileService.Participants(project, first);
    Require(participants.Count == 1 && participants[0].SubjectId == annaId && participants[0].Name == "Anna pianista",
        "Scene membership must follow stable SubjectId through subject rename.");

    StructuredSceneProfileService.RemoveFromActiveScenes(scenes, archivedId);
    Require(scenes.Scenes.Single(s => s.SceneId == archivedId).Archived, "Removed scene must remain as archived history.");
    var replacement = StructuredSceneProfileService.Add(scenes);
    Require(replacement.SceneId != archivedId, "A newly created scene must never recycle an archived SceneId.");
    Require(scenes.Scenes.Select(s => s.SceneId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == scenes.Scenes.Count,
        "Every historical scene must retain a unique SceneId.");

    // Hammer archive/new repeatedly. Every user-visible new scene gets a fresh identity.
    var seenIds = scenes.Scenes.Select(s => s.SceneId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < 25; i++)
    {
        var active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        if (active.Count < 2) StructuredSceneProfileService.Add(scenes);
        active = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        var remove = active[^1];
        StructuredSceneProfileService.RemoveFromActiveScenes(scenes, remove.SceneId);
        var created = StructuredSceneProfileService.Add(scenes);
        Require(seenIds.Add(created.SceneId), "Pianist archive/add cycle must always allocate a fresh SceneId.");
    }

    StructuredSceneProfileService.Save(project, scenes);
    var activeFinal = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
    Require(activeFinal.Count >= 2, "Scene hammer must leave an operational scene set.");
    Require(StructuredSceneProfileService.SceneForPosition(project, 1)?.SceneId == activeFinal[0].SceneId,
        "Position 1 must map to the first active scene.");
    Require(StructuredSceneProfileService.SceneForPosition(project, activeFinal.Count + 1)?.SceneId == activeFinal[0].SceneId,
        "Scene assignment must cycle deterministically by stable work-unit position.");

    var packagePath = Path.Combine(tempRoot, "scene-pianist.diez");
    await ProjectFileStore.SaveAsync(packagePath, project);
    var reloaded = await ProjectFileStore.LoadAsync(packagePath);
    var persisted = StructuredSceneProfileService.Load(reloaded);
    Require(persisted.Scenes.Any(s => s.SceneId == firstId && s.Name == "Concerto a Napoli"),
        "SceneId and user-visible rename must survive package save/reload.");
    Require(persisted.Scenes.Any(s => s.SceneId == archivedId && s.Archived),
        "Archived SceneId must survive package save/reload as historical identity.");
    var persistedFirst = persisted.Scenes.First(s => s.SceneId == firstId);
    var persistedParticipants = StructuredSceneProfileService.Participants(reloaded, persistedFirst);
    Require(persistedParticipants.Count == 1 && persistedParticipants[0].SubjectId == annaId,
        "SubjectId + SceneId participation must survive package save/reload.");

    Console.WriteLine("SCENE PIANIST PASS: SceneId was never recycled and SubjectId+SceneId membership survived frantic edits.");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}
