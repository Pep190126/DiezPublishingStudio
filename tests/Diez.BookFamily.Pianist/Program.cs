using System.Text.Json.Nodes;
using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string NewProject(string type)
{
    var root = new JsonObject
    {
        ["Format"] = "diez-project-package",
        ["SchemaVersion"] = 10,
        ["Name"] = "Book family pianist",
        ["ProjectId"] = Guid.NewGuid().ToString(),
        ["SavedAtLocal"] = "",
        ["EditionMetadata"] = new JsonObject { ["Title"] = "Book family pianist", ["Language"] = "it" },
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
                ["Name"] = type,
                ["IsCandidate"] = false,
                ["Notes"] = "",
                ["FutureBookTypeField"] = "preserve-me"
            }
        },
        ["Relations"] = new JsonArray(),
        ["BibleEntries"] = new JsonArray(),
        ["ConsistencyFacts"] = new JsonArray(),
        ["ConsistencyIssues"] = new JsonArray(),
        ["ConsistencyResolutions"] = new JsonArray(),
        ["RevisionCandidates"] = new JsonArray(),
        ["UnoUiState"] = new JsonObject
        {
            [$"BookOptions.{type}.Notes"] = "LEGACY NOTES — DO NOT OVERWRITE",
            [$"BookOptions.{type}.LegacyOnly"] = "LEGACY VALUE"
        },
        ["FutureRoot"] = new JsonObject { ["Marker"] = "must-survive" }
    };
    return root.ToJsonString();
}

foreach (var type in new[] { BookTypeCatalog.Quiz, BookTypeCatalog.DataCollection, BookTypeCatalog.Other })
{
    var json = NewProject(type);
    var initial = DiezBookFamilyFrontendBridge.Read(json, type);
    Require(initial.BookType == type, $"{type}: il bridge deve riconoscere il tipo libro canonico.");
    Require(initial.LegacyNotesDraft == "LEGACY NOTES — DO NOT OVERWRITE",
        $"{type}: le note Uno legacy devono restare recuperabili ma non autorevoli.");
    Require(initial.Options.Count > 0, $"{type}: deve esporre le opzioni definite dal Core.");

    var values = initial.Options.ToDictionary(option => option.Key, option => (string?)option.Value, StringComparer.OrdinalIgnoreCase);
    if (type == BookTypeCatalog.Quiz)
    {
        values["QuestionCount"] = "75";
        values["AnswersPerQuestion"] = "4";
        values["Difficulty"] = "Difficile";
        values["NoDuplicates"] = "true";
        values["IncludeExplanations"] = "true";
    }
    else if (type == BookTypeCatalog.DataCollection)
    {
        values["ItemCount"] = "250";
        values["Deduplicate"] = "true";
        values["NormalizeFormats"] = "true";
        values["TrackProvenance"] = "true";
    }
    else
    {
        values["PrimaryOutput"] = "Data";
        values["StructureHint"] = "Record strutturati con fonti e note";
    }

    var mutation = DiezBookFamilyFrontendBridge.Save(json, type, values, "NOTE CANONICHE " + type);
    Require(mutation.Status == "SAVED", $"{type}: le opzioni devono essere salvate nel Core.");
    var reread = DiezBookFamilyFrontendBridge.Read(mutation.ProjectJson, type);
    Require(reread.Notes == "NOTE CANONICHE " + type,
        $"{type}: le note canoniche devono essere lette dall'entità Core, non da UnoUiState.");
    Require(reread.LegacyNotesDraft == "LEGACY NOTES — DO NOT OVERWRITE",
        $"{type}: il salvataggio canonico non deve sovrascrivere la vecchia bozza Uno.");

    foreach (var pair in values)
    {
        var option = reread.Options.FirstOrDefault(candidate => string.Equals(candidate.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
        if (option is null) continue;
        Require(string.Equals(option.Value, pair.Value, StringComparison.Ordinal),
            $"{type}: l'opzione {pair.Key} deve fare round-trip nel Core.");
    }

    var raw = JsonNode.Parse(mutation.ProjectJson)!.AsObject();
    Require(raw["FutureRoot"]?["Marker"]?.GetValue<string>() == "must-survive",
        $"{type}: un campo futuro alla root deve sopravvivere.");
    Require(raw["UnoUiState"]?[$"BookOptions.{type}.Notes"]?.GetValue<string>() == "LEGACY NOTES — DO NOT OVERWRITE",
        $"{type}: il bridge non deve scrivere nel vecchio UnoUiState.");
    Require(raw["Entities"]!.AsArray().OfType<JsonObject>()
        .Any(entity => entity["Kind"]?.GetValue<string>() == "DiezBookType" && entity["FutureBookTypeField"]?.GetValue<string>() == "preserve-me"),
        $"{type}: il merge non deve cancellare estensioni future dell'entità Tipo libro.");
    Require(raw["Entities"]!.AsArray().OfType<JsonObject>()
        .Any(entity => entity["Kind"]?.GetValue<string>() == "DiezBookFamilyNotes" && entity["Name"]?.GetValue<string>() == type),
        $"{type}: le note devono avere una rappresentazione Core canonica.");
}

Console.WriteLine("BOOK FAMILY PIANIST PASS: Quiz, Catalog/Data and Other options/notes use canonical Core state, preserve unknown JSON, and leave legacy UnoUiState untouched.");
