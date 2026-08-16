using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var project = ProjectFileStore.Create("Long-form pianist");
project.Materials.Add(new MaterialEntry
{
    FileName = "manoscritto.md",
    Kind = "Markdown",
    ExtractedText = "Capitolo di prova",
    ImportedAtLocal = DateTimeOffset.Now.ToString("O")
});
project.Materials.Add(new MaterialEntry
{
    FileName = "tavola-01.png",
    Kind = "Image",
    ImportedAtLocal = DateTimeOffset.Now.ToString("O")
});

var chapter = new ContentNode { Kind = "Chapter", Title = "Capitolo 1", Body = "Test", Ordinal = 1 };
var scene = new ContentNode { Kind = "Scene", Title = "Scena 1", Body = "Napoli", Ordinal = 2, ParentId = chapter.ContentId };
project.ContentNodes.Add(chapter);
project.ContentNodes.Add(scene);
project.Entities.Add(new GraphEntity { Kind = "Character", Name = "Anna", Notes = "Pianista", IsCandidate = false });
project.Entities.Add(new GraphEntity { Kind = "Place", Name = "Napoli", Notes = "Ambientazione", IsCandidate = false });
project.Entities.Add(new GraphEntity { Kind = "Event", Name = "Concerto", Notes = "Evento", IsCandidate = false });
project.Entities.Add(new GraphEntity { Kind = "PlotThread", Name = "Esibizione", Notes = "Filo narrativo", IsCandidate = false });
project.ConsistencyIssues.Add(new ConsistencyIssue { Code = "FACT_CONTRADICTION", Message = "Contraddizione di prova", Status = "Open" });
project.AiProductionJobs.Add(new AiProductionJob { Title = "Revisione capitolo", OutputType = "Text", Status = "Ready" });
project.IllustrationPlacements.Add(new IllustrationPlacement { MaterialId = project.Materials[1].MaterialId, ContentId = scene.ContentId, Ordinal = 1 });

foreach (var type in new[] { BookTypeProfileService.Novel, BookTypeProfileService.EssayManual, BookTypeProfileService.IllustratedBook })
{
    Require(LongFormWorkspaceService.Supports(type), $"Long-form contract must support {type}.");
    BookTypeProfileService.Set(project, type);
    LongFormWorkspaceService.SetStructureDecision(project, true);
    var snapshot = LongFormWorkspaceService.Build(project);

    Require(snapshot.BookType == type, $"Snapshot must retain the active book type: {type}.");
    Require(snapshot.StructureIsKnown, $"Structure decision must be visible for {type}.");
    Require(snapshot.TextMaterialCount == 1, $"Text material must be shared with {type}.");
    Require(snapshot.Characters.Count == 1 && snapshot.Characters[0].Name == "Anna", $"Characters must be visible for {type}.");
    Require(snapshot.Places.Count == 1 && snapshot.Places[0].Name == "Napoli", $"Places must be visible for {type}.");
    Require(snapshot.Events.Count == 1, $"Events must be visible for {type}.");
    Require(snapshot.Threads.Count == 1, $"Narrative threads must be visible for {type}.");
    Require(snapshot.ChapterCount == 1 && snapshot.SceneCount == 1, $"Structure must be shared with {type}.");
    Require(snapshot.OpenIssues.Count == 1 && snapshot.Contradictions.Count == 1, $"Consistency state must be visible for {type}.");
    Require(snapshot.AiJobs.Count == 1, $"AI production state must be visible for {type}.");
    Require(snapshot.IllustrationPlacements.Count == 1, $"Illustration plan must remain visible for {type}.");

    if (type == BookTypeProfileService.IllustratedBook)
    {
        Require(snapshot.IsIllustrated, "Illustrated-book snapshot must be explicitly illustrated.");
        Require(snapshot.ImageMaterialCount == 1, "Illustrated-book snapshot must expose image materials.");
    }
    else
    {
        Require(!snapshot.IsIllustrated, $"{type} must not be mislabeled as Illustrated Book.");
    }
}

// Press all the routing keys repeatedly: family identity can change, shared editorial
// state cannot disappear or multiply simply because the user changes their mind.
var originalEntityIds = project.Entities.Select(e => e.EntityId).Order().ToArray();
var originalContentIds = project.ContentNodes.Select(n => n.ContentId).Order().ToArray();
for (var i = 0; i < 60; i++)
{
    var type = (i % 3) switch
    {
        0 => BookTypeProfileService.Novel,
        1 => BookTypeProfileService.EssayManual,
        _ => BookTypeProfileService.IllustratedBook
    };
    BookTypeProfileService.Set(project, type);
    var snapshot = LongFormWorkspaceService.Build(project);
    Require(snapshot.BookType == type, "Rapid long-form routing must never return a stale family snapshot.");
}
Require(project.Entities.Select(e => e.EntityId).Order().SequenceEqual(originalEntityIds),
    "Rapid long-form routing must not mutate shared editorial entity identity.");
Require(project.ContentNodes.Select(n => n.ContentId).Order().SequenceEqual(originalContentIds),
    "Rapid long-form routing must not mutate shared content identity.");

BookTypeProfileService.Set(project, BookTypeProfileService.Quiz);
Require(!LongFormWorkspaceService.Supports(BookTypeProfileService.Get(project)), "Quiz must not be silently treated as long-form.");
var rejected = false;
try { _ = LongFormWorkspaceService.Build(project); }
catch (InvalidOperationException) { rejected = true; }
Require(rejected, "Unsupported book families must fail explicitly instead of receiving the wrong workspace contract.");

Console.WriteLine("LONG-FORM PIANIST PASS: Novel, Essay/Manual and Illustrated Book share editorial state without routing contamination.");
