using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

internal static class VisualSceneFrontendContract
{
    [ModuleInitializer]
    internal static void Run()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Visual scene frontend contract: " + message);
        }

        var root = new JsonObject
        {
            ["Format"] = "diez-project-package",
            ["SchemaVersion"] = 10,
            ["Name"] = "Scene frontend",
            ["ProjectId"] = Guid.NewGuid().ToString(),
            ["EditionMetadata"] = new JsonObject { ["Title"] = "Scene frontend", ["Language"] = "it" },
            ["AiProduction"] = new JsonObject { ["SchemaVersion"] = 1, ["ProjectBrief"] = "" },
            ["AiProductionJobs"] = new JsonArray(),
            ["Materials"] = new JsonArray(),
            ["ContentNodes"] = new JsonArray(),
            ["IllustrationPlacements"] = new JsonArray(),
            ["Entities"] = new JsonArray
            {
                new JsonObject
                {
                    ["EntityId"] = Guid.NewGuid().ToString(),
                    ["Kind"] = "DiezBookType",
                    ["Name"] = BookTypeCatalog.ColoringBook,
                    ["IsCandidate"] = false,
                    ["Notes"] = "",
                    ["FutureBookTypeField"] = "keep-book-extension"
                }
            },
            ["Relations"] = new JsonArray(),
            ["BibleEntries"] = new JsonArray(),
            ["ConsistencyFacts"] = new JsonArray(),
            ["ConsistencyIssues"] = new JsonArray(),
            ["ConsistencyResolutions"] = new JsonArray(),
            ["RevisionCandidates"] = new JsonArray(),
            ["FutureRoot"] = new JsonObject { ["Marker"] = "keep-scene-extension" }
        };

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var saved = DiezVisualBookFrontendBridge.SaveColoring(
            json,
            1,
            "un giovane esploratore",
            "ambiente generico",
            true,
            "Mantieni identità e tratto coerenti.",
            new DiezColoringProfileDto(
                "Clean Line Art", false, false, "Bambini 6–9 anni", "Facile", "Spesso — Bold",
                "Bassa", "Bassa", "Semplice / minimo", "Ampio",
                true, true, true, true, true, ""));

        var subjectsMutation = DiezVisualSceneFrontendBridge.ConfigureSubjects(saved.ProjectJson, true, 2);
        var subjects = subjectsMutation.State.Subjects;
        Require(subjects.Count == 2, "la UI deve poter creare due soggetti canonici.");

        var firstSubjectId = subjects[0].SubjectId;
        var secondSubjectId = subjects[1].SubjectId;
        var namedFirst = DiezVisualSceneFrontendBridge.SaveSubject(
            subjectsMutation.ProjectJson, firstSubjectId, "Milo", "giovane esploratore con cappello rotondo");
        var namedSecond = DiezVisualSceneFrontendBridge.SaveSubject(
            namedFirst.ProjectJson, secondSubjectId, "Luna", "gatta compagna con collare semplice");
        var coScene = DiezVisualSceneFrontendBridge.SaveConsistencyRule(
            namedSecond.ProjectJson, firstSubjectId, "co_scene", "LOCKED", "USER", "Luna compare solo nelle scene selezionate.");

        var scenesMutation = DiezVisualSceneFrontendBridge.ConfigureScenes(coScene.ProjectJson, true, 2);
        Require(scenesMutation.State.Scenes.Count == 2, "la UI deve poter creare Scene canoniche.");
        var firstSceneId = scenesMutation.State.Scenes[0].SceneId;
        var retiredSecondSceneId = scenesMutation.State.Scenes[1].SceneId;

        var sceneSaved = DiezVisualSceneFrontendBridge.SaveScene(
            scenesMutation.ProjectJson, firstSceneId, "Cucina", "Milo prepara una torta sul tavolo della cucina");
        var attachMilo = DiezVisualSceneFrontendBridge.SetSceneParticipation(
            sceneSaved.ProjectJson, firstSceneId, firstSubjectId, true);
        var attachLuna = DiezVisualSceneFrontendBridge.SetSceneParticipation(
            attachMilo.ProjectJson, firstSceneId, secondSubjectId, true);

        var state = DiezVisualSceneFrontendBridge.Read(attachLuna.ProjectJson);
        var scene = state.Scenes.Single(x => x.SceneId == firstSceneId);
        Require(scene.ParticipantSubjectIds.Contains(firstSubjectId, StringComparer.OrdinalIgnoreCase),
            "Milo deve essere legato alla Scena tramite SubjectId.");
        Require(scene.ParticipantSubjectIds.Contains(secondSubjectId, StringComparer.OrdinalIgnoreCase),
            "Luna deve essere legata alla Scena tramite SubjectId.");

        var renamed = DiezVisualSceneFrontendBridge.SaveScene(
            attachLuna.ProjectJson, firstSceneId, "Cucina di casa", "Milo e Luna preparano una torta insieme");
        Require(DiezVisualSceneFrontendBridge.Read(renamed.ProjectJson).Scenes.Any(x => x.SceneId == firstSceneId && x.Name == "Cucina di casa"),
            "rinominare una Scena non deve cambiare SceneId.");

        var reduced = DiezVisualSceneFrontendBridge.ConfigureScenes(renamed.ProjectJson, true, 1);
        var expanded = DiezVisualSceneFrontendBridge.ConfigureScenes(reduced.ProjectJson, true, 2);
        var newSecondSceneId = expanded.State.Scenes.OrderBy(x => x.Number).Last().SceneId;
        Require(!string.Equals(newSecondSceneId, retiredSecondSceneId, StringComparison.OrdinalIgnoreCase),
            "un SceneId archiviato non deve essere riciclato quando il conteggio cresce di nuovo.");

        var packed = DiezVisualBookFrontendBridge.BuildPromptPack(expanded.ProjectJson);
        var prompt = packed.Items.Single().Prompt;
        Require(prompt.Contains("Milo", StringComparison.OrdinalIgnoreCase) && prompt.Contains("Luna", StringComparison.OrdinalIgnoreCase),
            "il Prompt atomico deve includere i partecipanti della Scena selezionata.");
        Require(prompt.Contains("torta", StringComparison.OrdinalIgnoreCase),
            "l'ambientazione/azione locale della Scena deve entrare nel Prompt atomico.");
        Require(!prompt.Contains(firstSceneId, StringComparison.OrdinalIgnoreCase) &&
                !prompt.Contains(firstSubjectId, StringComparison.OrdinalIgnoreCase),
            "SceneId e SubjectId devono restare metadati interni e non contaminare il Prompt provider-facing.");

        var job = DiezAiExchangeBridge.CreateReadyJob(packed.ProjectJson, packed.Items[0].Title, "Image", packed.Items[0].Prompt);
        Require(job.Job.WorkUnitId.HasValue, "il job visuale deve avere una Work Unit.");
        var requirements = DiezVisionFrontendBridge.Requirements(job.ProjectJson, job.Job.WorkUnitId!.Value);
        Require(requirements.Any(x => string.Equals(x.Key, "scene_participants_match", StringComparison.OrdinalIgnoreCase) && x.Required),
            "con partecipanti di Scena, Vision deve esporre scene_participants_match come gate richiesto.");

        var finalRoot = JsonNode.Parse(job.ProjectJson)!.AsObject();
        Require(finalRoot["FutureRoot"]?["Marker"]?.GetValue<string>() == "keep-scene-extension",
            "il bridge Scene non deve eliminare estensioni future alla root.");
        Require(finalRoot["Entities"]!.AsArray().OfType<JsonObject>().Any(x =>
                x["Kind"]?.GetValue<string>() == "DiezBookType" &&
                x["FutureBookTypeField"]?.GetValue<string>() == "keep-book-extension"),
            "il bridge Scene non deve eliminare campi futuri dalle entità esistenti.");
    }
}
