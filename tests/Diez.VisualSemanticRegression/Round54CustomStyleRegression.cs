using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

internal static class Round54CustomStyleRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new JsonObject
        {
            ["Format"] = "diez-project-package",
            ["SchemaVersion"] = 10,
            ["Name"] = "Round54 Custom regression",
            ["SavedAtLocal"] = "",
            ["ProjectId"] = Guid.NewGuid().ToString(),
            ["EditionMetadata"] = new JsonObject { ["Title"] = "Round54 Custom regression", ["Language"] = "it" },
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
                    ["Notes"] = ""
                }
            },
            ["Relations"] = new JsonArray(),
            ["BibleEntries"] = new JsonArray(),
            ["ConsistencyFacts"] = new JsonArray(),
            ["ConsistencyIssues"] = new JsonArray(),
            ["ConsistencyResolutions"] = new JsonArray(),
            ["RevisionCandidates"] = new JsonArray()
        };

        var json = DiezVisualBookFrontendBridge.SaveColoring(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            1,
            "gatto sorridente",
            "giardino",
            false,
            string.Empty,
            new DiezColoringProfileDto(
                "Cute & Playful", true, false, "Bambini 6–9 anni", "Facile", "Medio",
                "Bassa", "Bassa", "Semplice / minimo", "Ampio",
                true, true, true, true, true, "")).ProjectJson;

        const string definition = "Organic hand-drawn storybook line art with rounded expressive shapes and playful uneven contour rhythm";
        var saved = DiezColoringCustomStyleFrontendBridge.Save(json, true, definition, false);
        Require(saved.Status == "SAVED", "Round54 Custom: il salvataggio project-only deve riuscire.");
        Require(saved.State.IsActive, "Round54 Custom: lo stile deve risultare attivo.");
        Require(saved.State.Definition == definition, "Round54 Custom: la definizione utente deve restare integra nel progetto.");
        Require(!saved.State.ArchiveInLibrary, "Round54 Custom: project-only non deve diventare implicitamente archivio globale.");

        var pack = DiezVisualBookFrontendBridge.BuildPromptPack(saved.ProjectJson);
        Require(pack.Items.Count == 1, "Round54 Custom: il progetto atomico deve compilare una Work Unit.");
        Require(pack.Items[0].Prompt.Contains(definition, StringComparison.OrdinalIgnoreCase),
            "Round54 Custom: il renderer deve ricevere la definizione Custom, non una label vuota.");
        Require(!pack.Items[0].Prompt.Contains("STYLE — HARD LOCK: Custom.", StringComparison.OrdinalIgnoreCase),
            "Round54 Custom: la parola UI `Custom` non deve sostituire la definizione effettiva nel renderer.");

        var disabled = DiezColoringCustomStyleFrontendBridge.Save(saved.ProjectJson, false, string.Empty, false);
        Require(disabled.Status == "SAVED" && !disabled.State.IsActive,
            "Round54 Custom: selezionare uno stile non-Custom deve togliere l'autorità Custom.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
