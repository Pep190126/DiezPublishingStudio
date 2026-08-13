using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DiezPublishingStudio;

internal static class StructuredSceneProfileSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Structured Scene Contract");
        BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
        var coloring = BookTypePromptProfileService.LoadColoring(project);
        coloring.SubjectDescription = "friendly woodland characters";
        coloring.EnvironmentDescription = "garden and outdoor spaces";
        coloring.Style = "Kawaii";
        coloring.LineWeight = "Medio";
        BookTypePromptProfileService.SaveColoring(project, coloring);
        ImageCollectionWorkspaceService.SetConsistencyRules(project, "Consistent enabled");

        var multi = MultiSubjectProfileService.Load(project);
        multi.Enabled = true;
        multi.GroupDescription = "friendly woodland characters";
        MultiSubjectProfileService.SetCount(multi, 3);
        var subjects = MultiSubjectProfileService.ActiveSubjects(multi).ToList();
        Rename(multi, subjects[0], "Milo");
        Rename(multi, subjects[1], "Luna");
        Rename(multi, subjects[2], "Toby");
        subjects[0].Description = "small cat with a heart-shaped patch above the left eye";
        subjects[1].Description = "friendly dog with long floppy ears";
        subjects[2].Description = "young rabbit with one ear slightly bent forward";
        MultiSubjectProfileService.Save(project, multi);

        var scenes = StructuredSceneProfileService.Load(project);
        scenes.Enabled = true;
        StructuredSceneProfileService.SetCount(scenes, 2);
        var activeScenes = StructuredSceneProfileService.ActiveScenes(scenes).ToList();
        RenameScene(scenes, activeScenes[0], "Butterfly game");
        RenameScene(scenes, activeScenes[1], "Quiet picnic");
        activeScenes[0].Description = "Milo chases a butterfly while Luna watches nearby.";
        activeScenes[1].Description = "Toby sits beside a small picnic basket under a tree.";
        StructuredSceneProfileService.SetSubjectParticipation(scenes, activeScenes[0].SceneId, subjects[0].SubjectId, true);
        StructuredSceneProfileService.SetSubjectParticipation(scenes, activeScenes[0].SceneId, subjects[1].SubjectId, true);
        StructuredSceneProfileService.SetSubjectParticipation(scenes, activeScenes[1].SceneId, subjects[2].SubjectId, true);
        scenes.ActiveSceneId = activeScenes[0].SceneId;
        StructuredSceneProfileService.Save(project, scenes);

        var reloaded = StructuredSceneProfileService.Load(project);
        Require(StructuredSceneProfileService.ActiveScenes(reloaded).Count == 2, "non persistono due scene attive.");
        Require(StructuredSceneProfileService.ActiveScenes(reloaded)[0].SceneId == activeScenes[0].SceneId, "SceneId 1 non stabile.");
        Require(StructuredSceneProfileService.ActiveScenes(reloaded)[1].SceneId == activeScenes[1].SceneId, "SceneId 2 non stabile.");
        Require(StructuredSceneProfileService.Participants(project, StructuredSceneProfileService.ActiveScenes(reloaded)[0]).Count == 2,
            "membership scena 1 non conserva Milo+Luna.");

        var units = Enumerable.Range(1, 3).Select(i => new AiExchangeWorkUnit
        {
            WorkUnitId = Guid.NewGuid(), Code = $"IMG-{i:000}", ContentType = AiExchangeContentTypes.Image,
            Mode = AiExchangeModes.AiOnly, Position = i
        }).ToList();
        var settings = new PromptPreparationSettings { ProviderId = PromptEngineeringProviderIds.OpenAi, PreferAdvancedModel = true };
        var prompts = units.Select((unit, i) => PromptPackProviderFacingService.BuildImageGenerationPrompt(project, unit, 3, i + 1, settings)).ToList();

        Require(prompts[0].Contains("PRIMARY SUBJECT — HARD LOCK: Milo", StringComparison.Ordinal),
            "WU1 non mantiene Milo come focal subject della scena 1.");
        Require(prompts[0].Contains("SCENE PARTICIPANTS — HARD LOCK: Milo, Luna", StringComparison.Ordinal),
            "WU1 non porta Milo+Luna come partecipanti HARD.");
        Require(prompts[0].Contains("SCENE INTENT — HARD LOCK: Butterfly game", StringComparison.Ordinal),
            "WU1 non porta l'intento della scena.");
        Require(prompts[0].Contains("PARTICIPANT IDENTITY — HARD LOCK [Luna]", StringComparison.Ordinal),
            "WU1 non porta l'identità del partecipante non focale.");
        Require(prompts[1].Contains("PRIMARY SUBJECT — HARD LOCK: Toby", StringComparison.Ordinal),
            "WU2 deve usare Toby perché è l'unico partecipante strutturato della scena 2.");
        Require(prompts[1].Contains("SCENE PARTICIPANTS — HARD LOCK: Toby", StringComparison.Ordinal),
            "WU2 non porta Toby come partecipante HARD.");
        Require(prompts[2].Contains("SCENE PARTICIPANTS — HARD LOCK: Milo, Luna", StringComparison.Ordinal),
            "WU3 non riusa deterministicamente la scena 1 in base alla Position stabile.");
        Require(prompts.All(p => activeScenes.All(s => !p.Contains(s.SceneId, StringComparison.OrdinalIgnoreCase))),
            "SceneId interno è arrivato nel renderer prompt.");
        Require(prompts.All(p => subjects.All(s => !p.Contains(s.SubjectId, StringComparison.OrdinalIgnoreCase))),
            "SubjectId interno è arrivato nel renderer prompt di scena.");

        var partialWu2 = PromptPackProviderFacingService.BuildImageGenerationPrompt(project, units[1], 1, 1, settings);
        Require(partialWu2.Contains("Quiet picnic", StringComparison.Ordinal) &&
                partialWu2.Contains("PRIMARY SUBJECT — HARD LOCK: Toby", StringComparison.Ordinal),
            "export parziale WU2 perde la scena stabile numero 2.");

        var vision = new VisionValidationRequest { Expected = new VisionExpectedSpecification { ItemSubject = "legacy" } };
        VisionStructuredSubjectService.Apply(project, units[0], vision);
        Require(string.Equals(vision.Expected.ItemSubject, "Milo, Luna", StringComparison.Ordinal),
            "Vision diretta non verifica i partecipanti strutturati della scena 1.");
        Require(vision.Expected.ConsistencyRules.Contains("SCENE PARTICIPANTS — HARD: Milo, Luna", StringComparison.Ordinal),
            "Vision diretta non riceve il gate HARD dei partecipanti scena.");

        var state = new AiExchangeState { WorkUnits = units };
        var tempZip = Path.Combine(Path.GetTempPath(), "diez-scene-id-selftest-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                WriteWorkUnits(archive, "prompt-manifest.json", units);
                WriteWorkUnits(archive, "request-context.json", units);
            }
            PromptPackSceneIdentityService.Apply(tempZip, project, state, units.Select(x => x.WorkUnitId));
            using var read = ZipFile.OpenRead(tempZip);
            foreach (var file in new[] { "prompt-manifest.json", "request-context.json" })
            {
                using var reader = new StreamReader(read.GetEntry(file)!.Open(), Encoding.UTF8, true);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                var nodes = doc.RootElement.GetProperty("work_units").EnumerateArray().ToList();
                Require(nodes[0].GetProperty("scene_id").GetString() == activeScenes[0].SceneId, file + ": SceneId WU1 errato.");
                Require(nodes[1].GetProperty("scene_id").GetString() == activeScenes[1].SceneId, file + ": SceneId WU2 errato.");
                Require(nodes[2].GetProperty("scene_id").GetString() == activeScenes[0].SceneId, file + ": SceneId WU3 errato.");
                Require(nodes[0].GetProperty("scene_participant_subject_ids").GetArrayLength() == 2,
                    file + ": participant SubjectIds scena 1 mancanti.");
                Require(nodes[1].GetProperty("scene_participant_subject_names")[0].GetString() == "Toby",
                    file + ": participant name scena 2 errato.");
            }
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }

        var allIds = reloaded.Scenes.Select(x => x.SceneId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        StructuredSceneProfileService.SetCount(reloaded, 1);
        Require(StructuredSceneProfileService.ActiveScenes(reloaded).Count == 1, "riduzione scene non porta a una attiva.");
        Require(reloaded.Scenes.All(x => allIds.Contains(x.SceneId)), "riduzione scene cancella SceneId storico.");
    }

    private static void WriteWorkUnits(ZipArchive archive, string path, IReadOnlyList<AiExchangeWorkUnit> units)
    {
        var payload = JsonSerializer.Serialize(new
        {
            work_units = units.Select(x => new { id = x.WorkUnitId.ToString("D"), code = x.Code }).ToArray()
        });
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(payload);
    }

    private static void Rename(MultiSubjectProfile model, MultiSubjectDefinition subject, string name)
    {
        if (!MultiSubjectProfileService.TryRename(model, subject, name, out var error))
            throw new InvalidOperationException("STRUCTURED SCENE SELF-TEST: " + error);
    }

    private static void RenameScene(StructuredSceneProfile model, StructuredSceneDefinition scene, string name)
    {
        if (!StructuredSceneProfileService.TryRename(model, scene, name, out var error))
            throw new InvalidOperationException("STRUCTURED SCENE SELF-TEST: " + error);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("STRUCTURED SCENE SELF-TEST: " + message);
    }
}
