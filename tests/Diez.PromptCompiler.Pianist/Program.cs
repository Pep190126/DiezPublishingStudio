using DiezPublishingStudio;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var project = ProjectFileStore.Create("Prompt Compiler pianist");
BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);

var coloring = BookTypePromptProfileService.LoadColoring(project);
coloring.SubjectDescription = "animali della giungla";
coloring.EnvironmentDescription = "giungla";
coloring.Style = "Kawaii";
coloring.BoldEasy = true; // deliberately conflicting with thin line weight below
coloring.LineWeight = "Sottile — Fine";
coloring.TargetAudience = "Bambini 6–9 anni";
coloring.Difficulty = "Facile";
coloring.Complexity = "Media";
coloring.ElementDensity = "Bassa";
coloring.Background = "Contestuale leggero";
BookTypePromptProfileService.SaveColoring(project, coloring);
ColoringBoldEasyPolicyStore.Save(project, true, coloring.LineWeight);
ColoringCozyPolicyStore.Save(project, true);

var hard = ColoringIndependentHardProfileService.Resolve(project);
Require(hard.Style == "Kawaii", "Selected visual style must remain the single HARD style.");
Require(!hard.BoldEasy, "Thin/Fine line weight must force Bold & Easy HARD OFF even after frantic conflicting input.");
Require(hard.Cozy, "Cozy must remain an independent HARD dimension.");
Require(hard.LineWeight.Contains("Sottile", StringComparison.OrdinalIgnoreCase), "Thin line-weight selection must remain authoritative.");

var subjects = MultiSubjectProfileService.Load(project);
subjects.Enabled = true;
MultiSubjectProfileService.SetCount(subjects, 2);
var cast = MultiSubjectProfileService.ActiveSubjects(subjects).ToList();
cast[0].Name = "Scimmia";
cast[0].Description = "piccola scimmia sorridente con coda lunga";
cast[1].Name = "Tucano";
cast[1].Description = "tucano con grande becco";
MultiSubjectProfileService.Save(project, subjects);

StructuredSceneEnvironmentStore.Save(project, "giungla generica con vegetazione fitta");
var scenes = StructuredSceneProfileService.Load(project);
scenes.Enabled = true;
StructuredSceneProfileService.SetCount(scenes, 1);
var scene = StructuredSceneProfileService.ActiveScenes(scenes).Single();
scene.Name = "Cascata";
scene.Description = "la scimmia su una liana vicino a una cascata";
StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, cast[0].SubjectId, true);
StructuredSceneProfileService.SetSubjectParticipation(scenes, scene.SceneId, cast[1].SubjectId, true);
StructuredSceneProfileService.Save(project, scenes);

var request = new PromptEngineeringRequest
{
    BookType = BookTypeProfileService.ColoringBook,
    SeriesCount = 3,
    Subject = "3 immagini separate di animali della giungla",
    Environment = "giungla generica con vegetazione fitta",
    Audience = coloring.TargetAudience,
    Difficulty = coloring.Difficulty,
    LineWeight = coloring.LineWeight,
    Complexity = coloring.Complexity,
    Density = coloring.ElementDensity,
    Background = coloring.Background,
    Style = coloring.Style,
    NoTextInsideImage = true
};

var workDirection = VisualPromptIntentSynthesizer.BuildWorkUnitDirection(
    project,
    request,
    "one monkey",
    scene,
    StructuredSceneProfileService.Participants(project, scene));
Require(workDirection.StartsWith("ART DIRECTION — SYNTHESIZED:", StringComparison.Ordinal),
    "Prompt Compiler 3.6 must synthesize an explicit provider-facing art direction.");
Require(workDirection.Contains("monkey on a vine near a waterfall", StringComparison.OrdinalIgnoreCase),
    "Scene-local intent must appear in the synthesized direction.");
Require(workDirection.Contains("generic jungle", StringComparison.OrdinalIgnoreCase),
    "Generic environment may remain as supporting context.");
Require(workDirection.IndexOf("monkey on a vine", StringComparison.OrdinalIgnoreCase) <
        workDirection.IndexOf("generic jungle", StringComparison.OrdinalIgnoreCase),
    "Scene-local environment/action must precede generic environment context.");
Require(workDirection.Contains("current scene action determine the local staging", StringComparison.OrdinalIgnoreCase),
    "The synthesized direction must explicitly preserve scene-local precedence.");
Require(workDirection.Contains("Monkey", StringComparison.OrdinalIgnoreCase) && workDirection.Contains("Toucan", StringComparison.OrdinalIgnoreCase),
    "All required structured-scene participants must remain visible in one scene.");

var contaminated = string.Join(Environment.NewLine,
    "FRESH GENERATION",
    "DIEZ RENDER REQUEST ID: req-123-session-456-retry-9",
    workDirection,
    "Create ONE finished, publication-quality coloring-book illustration.",
    "PRIMARY SUBJECT — HARD LOCK: one monkey. The subject must be dominant.",
    "COMPOSITION — HARD LOCK: exactly ONE unified composition; not a collage or triptych.",
    "STYLE — HARD LOCK: Kawaii. Reject documentary realism.",
    ColoringIndependentHardProfileService.BoldEasyDirective(hard.BoldEasy),
    ColoringIndependentHardProfileService.CozyDirective(hard.Cozy),
    "LINE WEIGHT — HARD: the selected line weight is authoritative.",
    "DRAWING CRAFT: smooth intentional organic contours.",
    "COLOR OUTPUT — HARD: pure black #000000 and white #FFFFFF only.",
    "USER REQUIREMENT — HARD: 3 images, one per animal.",
    "Source-image policy: internal execution metadata only.",
    "SERIES ROLE: this is item 1 of 3.",
    "If the renderer cannot comply, retry with session id abc.",
    "FINAL CHECK — HARD: retry failed items.");

var renderer = PromptPackRendererVisualBriefService.Build(contaminated);
Require(renderer.Contains("ART DIRECTION — SYNTHESIZED:", StringComparison.Ordinal),
    "Renderer brief must retain synthesized art direction.");
Require(renderer.Contains("STYLE — HARD LOCK: Kawaii", StringComparison.Ordinal),
    "Renderer brief must retain the selected HARD style.");
Require(renderer.Contains("BOLD & EASY — HARD: OFF", StringComparison.Ordinal),
    "Renderer brief must retain resolved Bold & Easy OFF.");
Require(renderer.Contains("COZY — HARD: ON", StringComparison.Ordinal),
    "Renderer brief must retain Cozy ON independently.");
Require(renderer.Contains("LINE WEIGHT — HARD:", StringComparison.Ordinal),
    "Renderer brief must retain line-weight HARD lock.");
Require(renderer.Contains("COMPOSITION — HARD LOCK: one continuous unified primary scene", StringComparison.Ordinal),
    "Renderer brief must collapse composition instructions to one continuous scene.");
Require(!renderer.Contains("req-123", StringComparison.OrdinalIgnoreCase) &&
        !renderer.Contains("session", StringComparison.OrdinalIgnoreCase) &&
        !renderer.Contains("retry", StringComparison.OrdinalIgnoreCase),
    "Renderer brief must contain no request/session/retry metadata.");
Require(!renderer.Contains("SERIES ROLE", StringComparison.OrdinalIgnoreCase) &&
        !renderer.Contains("3 images", StringComparison.OrdinalIgnoreCase) &&
        !renderer.Contains("one per animal", StringComparison.OrdinalIgnoreCase),
    "Renderer brief must contain no batch orchestration or series-layout directive.");
Require(!renderer.Contains("triptych", StringComparison.OrdinalIgnoreCase) &&
        !renderer.Contains("collage", StringComparison.OrdinalIgnoreCase),
    "Renderer brief must not carry multi-panel/layout soup from the source prompt.");
Require(!PromptEnglishNormalizer.ContainsKnownItalianVisualVocabulary(renderer),
    "Provider-facing renderer brief must not retain known Italian visual vocabulary.");
PromptPackRendererVisualBriefService.EnsureVisualOnly(renderer);

var rejected = false;
try
{
    PromptPackRendererVisualBriefService.EnsureVisualOnly(
        "Create a collage/contact sheet with 3 images and retry failed panels using session 9.");
}
catch (InvalidOperationException)
{
    rejected = true;
}
Require(rejected, "Visual boundary must hard-reject orchestration/multi-panel contamination that survives sanitization.");

Console.WriteLine("PROMPT COMPILER 3.6 PIANIST PASS: synthesized art direction and HARD locks survived while routing/session/retry/layout soup was excluded.");
