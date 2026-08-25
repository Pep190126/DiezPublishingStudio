using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiezPublishingStudio;

internal static class Round561PromptGateRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        var aggregate = SaveColoring(NewProject(), 3, "3 soggetti di Halloween");
        var initial = DiezVisualThemeFrontendBridge.Read(aggregate);
        Require(!initial.Resolved, "Round561: un aggregato non congelato non deve risultare pronto per il Prompt.");

        var proposed = DiezVisualThemeFrontendBridge.Propose(aggregate, "halloween", null, false, regenerate: false);
        Require(proposed.Status == "PROPOSED" && proposed.State.ProposalReady && !proposed.State.Resolved,
            "Round561: la proposta Tema deve restare bloccata fino all'accettazione.");

        var accepted = DiezVisualThemeFrontendBridge.AcceptProposal(proposed.ProjectJson);
        Require(accepted.Status == "ACCEPTED" && accepted.State.Resolved,
            "Round561: dopo Accetta e congela il piano deve risultare risolto.");

        // Simulate the real Uno refresh + SaveSetupAsync that happens before entering Phase 2.
        var setup = DiezVisualBookFrontendBridge.Read(accepted.ProjectJson);
        Require(setup.Coloring is not null, "Round561: il profilo Coloring deve sopravvivere al refresh.");
        var roundTrip = DiezVisualBookFrontendBridge.SaveColoring(
            accepted.ProjectJson,
            setup.ImageCount,
            setup.Subject,
            setup.Environment,
            setup.Consistent,
            setup.ConsistencyRules,
            setup.Coloring!).ProjectJson;

        var afterRoundTrip = DiezVisualThemeFrontendBridge.Read(roundTrip);
        Require(afterRoundTrip.Resolved,
            "Round561: il salvataggio della Definizione dopo il freeze non deve perdere i SubjectId canonici.");
        var pack = DiezVisualBookFrontendBridge.BuildPromptPack(roundTrip);
        Require(pack.Items.Count == 3,
            "Round561: Accept → refresh → SaveSetup → Prompt deve produrre tre prompt atomici.");
        Require(pack.Items.All(x => !x.Prompt.Contains("3 soggetti di Halloween", StringComparison.OrdinalIgnoreCase)),
            "Round561: l'aggregato Tema non deve diventare PRIMARY SUBJECT del renderer dopo il freeze.");

        var legacyConcrete = SaveColoring(NewProject(), 3, "gatto sorridente");
        Require(DiezVisualThemeFrontendBridge.Read(legacyConcrete).Resolved,
            "Round561: un progetto legacy con soggetto concreto deve restare compatibile con il gate UI.");
        Require(DiezVisualBookFrontendBridge.BuildPromptPack(legacyConcrete).Items.Count == 3,
            "Round561: il nuovo gate non deve rompere la compilazione legacy concreta.");
    }

    private static string SaveColoring(string json, int count, string subject)
    {
        return DiezVisualBookFrontendBridge.SaveColoring(
            json,
            count,
            subject,
            string.Empty,
            consistent: false,
            string.Empty,
            new DiezColoringProfileDto(
                "Cute & Playful", true, false, "Bambini 6–9 anni", "Facile", "Medio",
                "Bassa", "Bassa", "Semplice / minimo", "Ampio",
                true, true, true, true, true, "")).ProjectJson;
    }

    private static string NewProject()
    {
        var root = new JsonObject
        {
            ["Format"] = "diez-project-package",
            ["SchemaVersion"] = 10,
            ["Name"] = "Round561 Prompt gate regression",
            ["SavedAtLocal"] = "",
            ["ProjectId"] = Guid.NewGuid().ToString(),
            ["EditionMetadata"] = new JsonObject { ["Title"] = "Round561", ["Language"] = "it" },
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
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
