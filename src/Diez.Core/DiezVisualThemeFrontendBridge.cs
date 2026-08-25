using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiezPublishingStudio;

public sealed record DiezVisualThemeOptionDto(
    string ThemeId,
    string DisplayName,
    string Origin,
    bool IsCustom);

public sealed record DiezVisualThemeSubjectDto(
    string ItemId,
    string DisplayName,
    string Description,
    string CanonicalConcept,
    string CanonicalDescription,
    string Origin);

public sealed record DiezVisualThemeProposalEditDto(
    string DisplayName,
    string Description);

public sealed record DiezVisualThemeStateDto(
    string SelectedThemeId,
    string SelectedThemeName,
    string ThemeOrigin,
    bool IsCustom,
    string CustomThemeName,
    bool ArchiveCustomTheme,
    int RequestedCount,
    int RegenerationIndex,
    IReadOnlyList<DiezVisualThemeOptionDto> Themes,
    IReadOnlyList<DiezVisualThemeSubjectDto> Pool,
    IReadOnlyList<DiezVisualThemeSubjectDto> Proposal,
    bool ProposalReady,
    bool Resolved,
    bool ApiExpansionAvailable,
    string Message);

public sealed record DiezVisualThemeMutation(
    string ProjectJson,
    string Status,
    string Message,
    DiezVisualThemeStateDto State);

/// <summary>
/// Shared semantic policy for whether a persisted theme proposal is ready for explicit user acceptance.
/// The Uno UI must derive button state from this policy plus actual unsaved editor differences, never from
/// TextChanged events alone.
/// </summary>
public static class DiezVisualThemeAcceptancePolicy
{
    public static bool IsAcceptableProposal(
        int requestedCount,
        IReadOnlyList<DiezVisualThemeSubjectDto>? proposal)
    {
        var count = Math.Clamp(requestedCount, 1, MultiSubjectProfileService.MaxSubjects);
        if (proposal is null || proposal.Count != count) return false;
        if (proposal.Any(x => string.IsNullOrWhiteSpace(x.DisplayName))) return false;
        if (proposal.Select(x => x.DisplayName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != count) return false;
        return proposal.All(x =>
            VisualSemanticResolutionGuard.IsConcrete(x.DisplayName) ||
            VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept));
    }
}

/// <summary>
/// User-facing visual theme library. Built-in themes and their subject pools are resolved locally,
/// before any image prompt is compiled. JSON, planner prompts and transport details stay internal.
/// Custom-theme AI expansion is intentionally only a capability hook until a real direct API executor exists.
/// </summary>
public static class DiezVisualThemeFrontendBridge
{
    private const string StateEntityKind = "DiezVisualThemeState";
    private const string BuiltinOrigin = "BUILTIN";
    private const string UserAuthoredOrigin = "USER_AUTHORED";
    private const string UserLibraryOrigin = "USER_LIBRARY";
    private const string CustomSentinelId = "custom";
    private const int MaxPoolSubjects = 96;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private sealed class ThemeProjectState
    {
        public int SchemaVersion { get; set; } = 1;
        public string SelectedThemeId { get; set; } = string.Empty;
        public string SelectedThemeName { get; set; } = string.Empty;
        public string ThemeOrigin { get; set; } = string.Empty;
        public string CustomThemeName { get; set; } = string.Empty;
        public bool ArchiveCustomTheme { get; set; }
        public int RegenerationIndex { get; set; }
        public int ProposalVersion { get; set; }
        public bool Accepted { get; set; }
        public List<ThemeSubjectSnapshot> Proposal { get; set; } = [];
        public List<ThemeSubjectSnapshot> ProjectAdditions { get; set; } = [];
    }

    private sealed class ThemeSubjectSnapshot
    {
        public string ThemeId { get; set; } = string.Empty;
        public string ItemId { get; set; } = Guid.NewGuid().ToString("D");
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CanonicalConcept { get; set; } = string.Empty;
        public string CanonicalDescription { get; set; } = string.Empty;
        public string Origin { get; set; } = UserAuthoredOrigin;
    }

    private sealed class ThemeDefinition
    {
        public string ThemeId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string CanonicalTheme { get; init; } = string.Empty;
        public string Origin { get; init; } = BuiltinOrigin;
        public bool IsCustom { get; init; }
        public List<ThemeSeedSubject> Subjects { get; init; } = [];
    }

    private sealed record ThemeSeedSubject(string DisplayName, string CanonicalConcept);

    private sealed class UserThemeLibraryFile
    {
        public int SchemaVersion { get; set; } = 1;
        public List<UserThemeDefinition> Themes { get; set; } = [];
        public List<UserThemeOverlay> Overlays { get; set; } = [];
    }

    private sealed class UserThemeDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string DisplayName { get; set; } = string.Empty;
        public string CanonicalTheme { get; set; } = string.Empty;
        public List<ThemeSubjectSnapshot> Subjects { get; set; } = [];
    }

    private sealed class UserThemeOverlay
    {
        public string ThemeId { get; set; } = string.Empty;
        public List<ThemeSubjectSnapshot> Subjects { get; set; } = [];
    }

    private static readonly IReadOnlyList<ThemeDefinition> Builtins =
    [
        Builtin("jungle", "Animali della giungla", "jungle animals",
            S("Leone", "lion"), S("Tigre", "tiger"), S("Elefante", "elephant"), S("Giraffa", "giraffe"),
            S("Scimmia", "monkey"), S("Tucano", "toucan"), S("Pappagallo", "parrot"), S("Coccodrillo", "crocodile"),
            S("Ippopotamo", "hippopotamus"), S("Giaguaro", "jaguar"), S("Bradipo", "sloth"), S("Serpente", "snake")),

        Builtin("halloween", "Halloween", "Halloween",
            S("Zucca jack-o'-lantern", "jack-o'-lantern pumpkin"), S("Fantasma amichevole", "friendly ghost"),
            S("Gatto con cappello da strega", "cat wearing a witch hat"), S("Pipistrello", "bat"),
            S("Gufo notturno", "night owl"), S("Streghetta amichevole", "friendly young witch"),
            S("Scheletro simpatico", "friendly skeleton"), S("Ragno", "spider"),
            S("Mostriciattolo buffo", "cute little monster"), S("Mummia simpatica", "friendly mummy"),
            S("Calderone magico", "magic cauldron"), S("Casa stregata", "haunted house")),

        Builtin("christmas", "Natale / Christmas", "Christmas",
            S("Babbo Natale", "Santa Claus"), S("Renna", "reindeer"), S("Pupazzo di neve", "snowman"),
            S("Elfo di Natale", "Christmas elf"), S("Albero di Natale", "Christmas tree"),
            S("Orso polare con sciarpa", "polar bear wearing a scarf"), S("Pinguino natalizio", "Christmas penguin"),
            S("Schiaccianoci", "nutcracker"), S("Omino di pan di zenzero", "gingerbread man"),
            S("Slitta", "Christmas sleigh"), S("Regalo con fiocco", "wrapped present with bow"), S("Campana natalizia", "Christmas bell")),

        Builtin("dinosaurs", "Dinosauri", "dinosaurs",
            S("Tyrannosaurus rex", "Tyrannosaurus rex"), S("Triceratopo", "Triceratops"), S("Stegosauro", "Stegosaurus"),
            S("Brachiosauro", "Brachiosaurus"), S("Velociraptor", "Velociraptor"), S("Anchilosauro", "Ankylosaurus"),
            S("Parasaurolofo", "Parasaurolophus"), S("Pteranodonte", "Pteranodon"), S("Spinosauro", "Spinosaurus"),
            S("Diplodoco", "Diplodocus")),

        Builtin("space", "Spazio", "outer space",
            S("Astronauta", "astronaut"), S("Razzo", "space rocket"), S("Pianeta con anelli", "ringed planet"),
            S("Rover lunare", "lunar rover"), S("Satellite", "satellite"), S("Alieno amichevole", "friendly alien"),
            S("Robot spaziale", "space robot"), S("Luna", "Moon"), S("Cometa", "comet"), S("Telescopio", "telescope"),
            S("Navicella spaziale", "spaceship"), S("Stazione spaziale", "space station")),

        Builtin("farm", "Fattoria", "farm",
            S("Mucca", "cow"), S("Maialino", "piglet"), S("Gallina", "hen"), S("Gallo", "rooster"),
            S("Pecora", "sheep"), S("Capra", "goat"), S("Cavallo", "horse"), S("Anatra", "duck"),
            S("Coniglio", "rabbit"), S("Trattore", "farm tractor"), S("Spaventapasseri", "scarecrow"), S("Fienile", "barn")),

        Builtin("ocean", "Oceano / mare", "ocean",
            S("Delfino", "dolphin"), S("Balena", "whale"), S("Tartaruga marina", "sea turtle"), S("Polpo", "octopus"),
            S("Cavalluccio marino", "seahorse"), S("Pesce pagliaccio", "clownfish"), S("Squalo", "shark"),
            S("Stella marina", "starfish"), S("Granchio", "crab"), S("Medusa", "jellyfish"), S("Foca", "seal"), S("Manta", "manta ray")),

        Builtin("pets", "Animali domestici", "pets",
            S("Gatto", "cat"), S("Cane", "dog"), S("Coniglio domestico", "pet rabbit"), S("Criceto", "hamster"),
            S("Porcellino d'India", "guinea pig"), S("Pappagallino", "budgerigar"), S("Pesce rosso", "goldfish"),
            S("Tartaruga domestica", "pet turtle"), S("Furetto", "ferret"), S("Cagnolino", "puppy"), S("Gattino", "kitten")),

        Builtin("vehicles", "Veicoli", "vehicles",
            S("Automobile", "car"), S("Camion", "truck"), S("Autobus", "bus"), S("Treno", "train"),
            S("Aereo", "airplane"), S("Elicottero", "helicopter"), S("Motocicletta", "motorcycle"), S("Bicicletta", "bicycle"),
            S("Escavatore", "excavator"), S("Trattore", "tractor"), S("Barca a vela", "sailboat"), S("Camion dei pompieri", "fire truck")),

        Builtin("fairy", "Fiabe / fantasy", "fairy tales and fantasy",
            S("Drago amichevole", "friendly dragon"), S("Unicorno", "unicorn"), S("Fata", "fairy"), S("Mago", "wizard"),
            S("Principessa", "princess"), S("Cavaliere", "knight"), S("Gnomo", "gnome"), S("Sirena", "mermaid"),
            S("Fenice", "phoenix"), S("Castello fiabesco", "fairy-tale castle"), S("Pegaso", "Pegasus"), S("Folletto", "pixie")),

        Builtin("botanical", "Fiori / botanica", "flowers and botany",
            S("Rosa", "rose flower"), S("Girasole", "sunflower"), S("Tulipano", "tulip"), S("Margherita", "daisy"),
            S("Lavanda", "lavender"), S("Orchidea", "orchid"), S("Peonia", "peony"), S("Fiore di loto", "lotus flower"),
            S("Cactus fiorito", "flowering cactus"), S("Felce", "fern"), S("Monstera", "Monstera plant"), S("Bouquet di fiori", "flower bouquet")),

        Builtin("winter", "Inverno", "winter",
            S("Pupazzo di neve", "snowman"), S("Volpe artica", "arctic fox"), S("Orso polare", "polar bear"),
            S("Pinguino", "penguin"), S("Gufo delle nevi", "snowy owl"), S("Slitta", "sled"),
            S("Pattini da ghiaccio", "ice skates"), S("Casetta innevata", "snow-covered cottage"),
            S("Fiocco di neve", "snowflake"), S("Abete innevato", "snow-covered fir tree")),

        Builtin("spring", "Primavera", "spring",
            S("Farfalla", "butterfly"), S("Ape", "bee"), S("Coccinella", "ladybug"), S("Coniglietto", "bunny"),
            S("Pulcino", "chick"), S("Tulipano", "tulip"), S("Narciso", "daffodil"), S("Uccellino", "small bird"),
            S("Annaffiatoio", "watering can"), S("Cestino di fiori", "basket of flowers"), S("Ranocchio", "frog")),

        Builtin("easter", "Pasqua", "Easter",
            S("Coniglietto pasquale", "Easter bunny"), S("Uovo decorato", "decorated Easter egg"), S("Pulcino", "chick"),
            S("Cestino pasquale", "Easter basket"), S("Agnellino", "lamb"), S("Fiore primaverile", "spring flower"),
            S("Carota", "carrot"), S("Coniglietto con uovo", "bunny holding an Easter egg"), S("Campanella", "small bell")),

        Builtin("valentine", "San Valentino", "Valentine's Day",
            S("Cuore sorridente", "smiling heart"), S("Orsetto con cuore", "teddy bear holding a heart"),
            S("Coppia di uccellini", "pair of lovebirds"), S("Cupido", "Cupid"), S("Rosa", "rose flower"),
            S("Busta con cuore", "envelope with a heart"), S("Scatola di cioccolatini", "box of chocolates"),
            S("Palloncino a cuore", "heart-shaped balloon"), S("Lucchetto a cuore", "heart-shaped padlock"))
    ];

    public static DiezVisualThemeStateDto Read(string projectJson)
    {
        var (_, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        EnsureDefaultSelection(project, state);
        return BuildState(project, state, "Scegli un tema, controlla il pool e proponi i soggetti localmente con Diez.");
    }

    public static DiezVisualThemeMutation SelectTheme(
        string projectJson,
        string? themeId,
        string? customThemeName = null,
        bool archiveCustomTheme = false)
    {
        var (root, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        EnsureDefaultSelection(project, state);
        var cleanId = (themeId ?? string.Empty).Trim();
        var option = Options(state).FirstOrDefault(x => string.Equals(x.ThemeId, cleanId, StringComparison.OrdinalIgnoreCase));
        if (option is null)
            return Mutation(projectJson, "INVALID_THEME", "Tema non riconosciuto.");

        var changed = !string.Equals(state.SelectedThemeId, cleanId, StringComparison.OrdinalIgnoreCase);
        state.SelectedThemeId = cleanId;
        state.SelectedThemeName = option.DisplayName;
        state.ThemeOrigin = option.Origin;
        state.ArchiveCustomTheme = archiveCustomTheme;
        if (string.Equals(cleanId, CustomSentinelId, StringComparison.OrdinalIgnoreCase))
            state.CustomThemeName = (customThemeName ?? state.CustomThemeName).Trim();
        else if (!cleanId.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
            state.CustomThemeName = string.Empty;

        if (changed)
        {
            state.Proposal = [];
            state.RegenerationIndex = 0;
            state.Accepted = false;
            InvalidateResolvedPlan(project);
        }
        SaveProjectState(project, state);
        MergeEntities(root, project);
        return new DiezVisualThemeMutation(
            Write(root), "SELECTED", $"Tema selezionato: {state.SelectedThemeName}.", BuildState(project, state, string.Empty));
    }

    public static DiezVisualThemeMutation Propose(
        string projectJson,
        string? themeId,
        string? customThemeName,
        bool archiveCustomTheme,
        bool regenerate,
        string? mustDo = null,
        string? mustNotDo = null)
    {
        var (root, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        EnsureDefaultSelection(project, state);
        var selected = ResolveSelectedTheme(project, state, themeId, customThemeName, archiveCustomTheme, persistCustomLibrary: true, out var selectionMessage);
        if (selected is null)
            return Mutation(projectJson, "INVALID_THEME", selectionMessage);

        var count = Math.Clamp(VisualBookPlanService.Load(project).ImageCount, 1, MultiSubjectProfileService.MaxSubjects);
        var pool = Pool(project, state, selected);
        var exclusions = (mustNotDo ?? string.Empty).Trim();
        var requirements = (mustDo ?? string.Empty).Trim();
        pool = pool.Where(x => !Mentions(exclusions, x)).ToList();
        if (pool.Count < count)
        {
            SetAggregateSubject(project, count, selected.DisplayName);
            SaveProjectState(project, state);
            MergeEntities(root, project);
            return new DiezVisualThemeMutation(
                Write(root),
                "POOL_TOO_SMALL",
                $"Il tema “{selected.DisplayName}” contiene {pool.Count} soggetti disponibili, ma il progetto ne richiede {count}. Aggiungi altri soggetti personalizzati al tema oppure, in futuro, usa l'espansione API.",
                BuildState(project, state, string.Empty));
        }

        if (regenerate) state.RegenerationIndex++;
        var style = StyleContext(project);
        var audience = AudienceContext(project);
        var required = pool.Where(x => Mentions(requirements, x)).ToList();
        var remaining = pool.Where(x => required.All(r => !SameSubject(r, x)))
            .OrderBy(x => StableRank($"{selected.ThemeId}|{style}|{audience}|{count}|{state.RegenerationIndex}|{x.ItemId}"), StringComparer.Ordinal)
            .ToList();
        var proposal = required.Concat(remaining)
            .GroupBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(count)
            .Select(Clone)
            .ToList();

        if (proposal.Count != count)
            return Mutation(projectJson, "PROPOSAL_INVALID", "Diez non riesce a costruire una proposta distinta con i vincoli correnti.");

        state.Proposal = proposal;
        state.ProposalVersion++;
        state.Accepted = false;
        InvalidateResolvedPlan(project);
        SetAggregateSubject(project, count, selected.DisplayName);
        SaveProjectState(project, state);
        MergeEntities(root, project);
        var action = regenerate ? "rigenerata" : "preparata";
        return new DiezVisualThemeMutation(
            Write(root),
            "PROPOSED",
            $"Proposta {action}: {count} soggetti distinti dal tema “{selected.DisplayName}”. Controllali, modificali se serve e poi accettali.",
            BuildState(project, state, string.Empty));
    }

    public static DiezVisualThemeMutation SaveProposalEdits(
        string projectJson,
        IEnumerable<DiezVisualThemeProposalEditDto>? edits)
    {
        var (root, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        var list = (edits ?? []).ToList();
        if (state.Proposal.Count == 0 || list.Count != state.Proposal.Count)
            return Mutation(projectJson, "INVALID_EDIT", "La proposta a video non corrisponde più allo stato del progetto.");

        var cleaned = list.Select(x => new DiezVisualThemeProposalEditDto(
            (x.DisplayName ?? string.Empty).Trim(),
            (x.Description ?? string.Empty).Trim())).ToList();
        if (cleaned.Any(x => !VisualSemanticResolutionGuard.IsConcrete(x.DisplayName)))
            return Mutation(projectJson, "INVALID_EDIT", "Ogni soggetto deve avere un nome concreto e riconoscibile.");
        if (cleaned.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cleaned.Count)
            return Mutation(projectJson, "INVALID_EDIT", "I soggetti della proposta devono restare distinti.");

        for (var i = 0; i < state.Proposal.Count; i++)
        {
            var original = state.Proposal[i];
            var nameChanged = !string.Equals(original.DisplayName.Trim(), cleaned[i].DisplayName, StringComparison.OrdinalIgnoreCase);
            original.DisplayName = cleaned[i].DisplayName;
            original.Description = cleaned[i].Description;
            if (nameChanged)
            {
                // The new wording is a direct publisher-authored canonical fact. Without a configured semantic API
                // Diez must not pretend to have generated a technical-English concept it did not actually derive.
                original.CanonicalConcept = string.Empty;
                original.CanonicalDescription = string.Empty;
                original.Origin = UserAuthoredOrigin;
            }
        }

        state.ProposalVersion++;
        state.Accepted = false;
        InvalidateResolvedPlan(project);
        SaveProjectState(project, state);
        MergeEntities(root, project);
        return new DiezVisualThemeMutation(
            Write(root), "EDITED", "Modifiche alla proposta salvate. Controlla l'elenco e accetta quando è corretto.", BuildState(project, state, string.Empty));
    }

    public static DiezVisualThemeMutation AddCustomSubject(
        string projectJson,
        string? themeId,
        string? customThemeName,
        bool archiveCustomTheme,
        string? displayName,
        string? description,
        bool archiveInThemeLibrary)
    {
        var (root, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        EnsureDefaultSelection(project, state);
        var selected = ResolveSelectedTheme(project, state, themeId, customThemeName, archiveCustomTheme, persistCustomLibrary: archiveCustomTheme, out var selectionMessage);
        if (selected is null)
            return Mutation(projectJson, "INVALID_THEME", selectionMessage);

        var cleanName = (displayName ?? string.Empty).Trim();
        var cleanDescription = (description ?? string.Empty).Trim();
        if (!VisualSemanticResolutionGuard.IsConcrete(cleanName))
            return Mutation(projectJson, "INVALID_SUBJECT", "Scrivi un soggetto/parola concreta prima di aggiungerla al tema.");

        var currentPool = Pool(project, state, selected);
        if (currentPool.Any(x => string.Equals(x.DisplayName, cleanName, StringComparison.OrdinalIgnoreCase)))
            return Mutation(projectJson, "DUPLICATE", $"“{cleanName}” è già presente nel tema “{selected.DisplayName}”.");

        var item = new ThemeSubjectSnapshot
        {
            ThemeId = selected.ThemeId,
            ItemId = Guid.NewGuid().ToString("D"),
            DisplayName = cleanName,
            Description = cleanDescription.Length > 0 ? cleanDescription : $"{cleanName} come singolo soggetto principale riconoscibile.",
            CanonicalConcept = string.Empty,
            CanonicalDescription = string.Empty,
            Origin = UserAuthoredOrigin
        };
        state.ProjectAdditions.Add(Clone(item));
        if (archiveInThemeLibrary)
            AddLibrarySubject(selected, item);
        SaveProjectState(project, state);
        MergeEntities(root, project);
        return new DiezVisualThemeMutation(
            Write(root),
            "SUBJECT_ADDED",
            archiveInThemeLibrary
                ? $"“{cleanName}” aggiunto al progetto e alla libreria del tema “{selected.DisplayName}”."
                : $"“{cleanName}” aggiunto solo a questo progetto per il tema “{selected.DisplayName}”.",
            BuildState(project, state, string.Empty));
    }

    public static DiezVisualThemeMutation AcceptProposal(string projectJson)
    {
        var (root, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        var count = Math.Clamp(VisualBookPlanService.Load(project).ImageCount, 1, MultiSubjectProfileService.MaxSubjects);
        if (state.Proposal.Count != count)
            return Mutation(projectJson, "NO_PROPOSAL", $"Prima prepara una proposta di esattamente {count} soggetti.");
        if (state.Proposal.Any(x => !VisualSemanticResolutionGuard.IsConcrete(x.DisplayName) &&
                                    !VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept)))
            return Mutation(projectJson, "INVALID_PROPOSAL", "La proposta contiene ancora soggetti non concreti.");
        if (state.Proposal.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != count)
            return Mutation(projectJson, "INVALID_PROPOSAL", "La proposta contiene soggetti duplicati.");

        var multi = new MultiSubjectProfile
        {
            SchemaVersion = 2,
            Enabled = true,
            RequestedCount = count,
            GroupDescription = state.SelectedThemeName,
            Subjects = state.Proposal.Select(x =>
            {
                var subject = new MultiSubjectDefinition
                {
                    SubjectId = Guid.NewGuid().ToString("D"),
                    Name = x.DisplayName,
                    CanonicalConcept = x.CanonicalConcept,
                    Description = x.Description,
                    CanonicalDescription = x.CanonicalDescription,
                    Included = true,
                    Archived = false
                };
                MultiSubjectProfileService.EnsureConsistencyDefaults(subject);
                return subject;
            }).ToList()
        };
        multi.ActiveSubjectId = multi.Subjects[0].SubjectId;
        MultiSubjectProfileService.Save(project, multi);
        state.Accepted = true;
        SetAggregateSubject(project, count, state.SelectedThemeName);
        SaveProjectState(project, state);
        MergeEntities(root, project);
        return new DiezVisualThemeMutation(
            Write(root),
            "ACCEPTED",
            $"Piano soggetti congelato: {count} soggetti del tema “{state.SelectedThemeName}” sono ora pronti per Prompt e Prompt Pack.",
            BuildState(project, state, string.Empty));
    }

    private static DiezVisualThemeMutation Mutation(string projectJson, string status, string message)
    {
        var (_, project) = Parse(projectJson);
        var state = LoadProjectState(project);
        EnsureDefaultSelection(project, state);
        return new DiezVisualThemeMutation(projectJson, status, message, BuildState(project, state, message));
    }

    private static DiezVisualThemeStateDto BuildState(PreviewProject project, ThemeProjectState state, string message)
    {
        EnsureDefaultSelection(project, state);
        var options = Options(state);
        var selected = ResolveDefinition(state.SelectedThemeId, state.SelectedThemeName, state.ThemeOrigin, state.CustomThemeName);
        var pool = selected is null ? [] : Pool(project, state, selected);
        var count = Math.Clamp(VisualBookPlanService.Load(project).ImageCount, 1, MultiSubjectProfileService.MaxSubjects);
        var multi = MultiSubjectProfileService.Load(project);
        var active = MultiSubjectProfileService.ActiveSubjects(multi);
        var legacySubject = SubjectDescription(project);
        var legacyConcrete = VisualSemanticResolutionGuard.IsConcrete(legacySubject) &&
                             !VisualSemanticResolutionGuard.LooksAggregateSubject(legacySubject);
        var resolved = (multi.Enabled &&
                        active.Count == count &&
                        active.All(x => VisualSemanticResolutionGuard.IsConcrete(x.CanonicalConcept) || VisualSemanticResolutionGuard.IsConcrete(x.Name))) ||
                       legacyConcrete;
        var proposalDto = state.Proposal.Select(ToDto).ToList();
        var proposalReady = DiezVisualThemeAcceptancePolicy.IsAcceptableProposal(count, proposalDto);
        var isCustom = state.SelectedThemeId == CustomSentinelId ||
                       state.SelectedThemeId.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ||
                       state.SelectedThemeId.StartsWith("user:", StringComparison.OrdinalIgnoreCase);
        var api = AiProviderCatalog.All.Any(x => x.SupportsDirectApi && x.SupportsText);
        return new DiezVisualThemeStateDto(
            state.SelectedThemeId,
            state.SelectedThemeName,
            state.ThemeOrigin,
            isCustom,
            state.CustomThemeName,
            state.ArchiveCustomTheme,
            count,
            state.RegenerationIndex,
            options,
            pool.Select(ToDto).ToList(),
            proposalDto,
            proposalReady,
            resolved,
            api,
            message);
    }

    private static ThemeDefinition? ResolveSelectedTheme(
        PreviewProject project,
        ThemeProjectState state,
        string? requestedThemeId,
        string? customThemeName,
        bool archiveCustomTheme,
        bool persistCustomLibrary,
        out string message)
    {
        var requested = (requestedThemeId ?? state.SelectedThemeId).Trim();
        if (requested.Length == 0) requested = "jungle";
        var customName = (customThemeName ?? state.CustomThemeName).Trim();

        if (string.Equals(requested, CustomSentinelId, StringComparison.OrdinalIgnoreCase))
        {
            if (customName.Length == 0)
            {
                message = "Scrivi il nome del tema Custom prima di proporre o aggiungere soggetti.";
                return null;
            }
            if (archiveCustomTheme && persistCustomLibrary)
            {
                var saved = UpsertCustomTheme(customName);
                requested = "user:" + saved.Id;
                state.SelectedThemeId = requested;
                state.SelectedThemeName = saved.DisplayName;
                state.ThemeOrigin = UserLibraryOrigin;
            }
            else
            {
                requested = "project:" + Slug(customName);
                state.SelectedThemeId = requested;
                state.SelectedThemeName = customName;
                state.ThemeOrigin = UserAuthoredOrigin;
            }
            state.CustomThemeName = customName;
            state.ArchiveCustomTheme = archiveCustomTheme;
            message = string.Empty;
            return ResolveDefinition(state.SelectedThemeId, state.SelectedThemeName, state.ThemeOrigin, state.CustomThemeName);
        }

        var option = Options(state).FirstOrDefault(x => string.Equals(x.ThemeId, requested, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            message = "Tema non riconosciuto.";
            return null;
        }
        var changed = !string.Equals(state.SelectedThemeId, option.ThemeId, StringComparison.OrdinalIgnoreCase);
        state.SelectedThemeId = option.ThemeId;
        state.SelectedThemeName = option.DisplayName;
        state.ThemeOrigin = option.Origin;
        state.ArchiveCustomTheme = archiveCustomTheme;
        if (changed)
        {
            state.Proposal = [];
            state.RegenerationIndex = 0;
            state.Accepted = false;
            InvalidateResolvedPlan(project);
        }
        message = string.Empty;
        return ResolveDefinition(state.SelectedThemeId, state.SelectedThemeName, state.ThemeOrigin, state.CustomThemeName);
    }

    private static ThemeDefinition? ResolveDefinition(string themeId, string displayName, string origin, string customName)
    {
        var builtin = Builtins.FirstOrDefault(x => string.Equals(x.ThemeId, themeId, StringComparison.OrdinalIgnoreCase));
        if (builtin is not null) return builtin;
        if (themeId.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
        {
            var id = themeId[5..];
            var user = LoadLibrary().Themes.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (user is null) return null;
            return new ThemeDefinition
            {
                ThemeId = "user:" + user.Id,
                DisplayName = user.DisplayName,
                CanonicalTheme = user.CanonicalTheme,
                Origin = UserLibraryOrigin,
                IsCustom = true,
                Subjects = []
            };
        }
        if (themeId.StartsWith("project:", StringComparison.OrdinalIgnoreCase) || themeId == CustomSentinelId)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? customName : displayName;
            return new ThemeDefinition
            {
                ThemeId = themeId,
                DisplayName = name,
                CanonicalTheme = string.Empty,
                Origin = UserAuthoredOrigin,
                IsCustom = true,
                Subjects = []
            };
        }
        return null;
    }

    private static List<ThemeSubjectSnapshot> Pool(PreviewProject project, ThemeProjectState state, ThemeDefinition selected)
    {
        var items = new List<ThemeSubjectSnapshot>();
        foreach (var subject in selected.Subjects)
        {
            items.Add(new ThemeSubjectSnapshot
            {
                ThemeId = selected.ThemeId,
                ItemId = $"{selected.ThemeId}:{Slug(subject.CanonicalConcept)}",
                DisplayName = subject.DisplayName,
                Description = $"{subject.DisplayName} come singolo soggetto principale, chiaro e riconoscibile nel tema {selected.DisplayName}.",
                CanonicalConcept = subject.CanonicalConcept,
                CanonicalDescription = $"One clear, recognizable {subject.CanonicalConcept} as the primary focal subject for the {selected.CanonicalTheme} theme.",
                Origin = selected.Origin
            });
        }

        var library = LoadLibrary();
        if (selected.ThemeId.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
        {
            var id = selected.ThemeId[5..];
            var userTheme = library.Themes.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (userTheme is not null) items.AddRange(userTheme.Subjects.Select(Clone));
        }
        var overlay = library.Overlays.FirstOrDefault(x => string.Equals(x.ThemeId, selected.ThemeId, StringComparison.OrdinalIgnoreCase));
        if (overlay is not null) items.AddRange(overlay.Subjects.Select(Clone));
        items.AddRange(state.ProjectAdditions.Where(x => string.Equals(x.ThemeId, selected.ThemeId, StringComparison.OrdinalIgnoreCase)).Select(Clone));

        return items
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => x.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(MaxPoolSubjects)
            .ToList();
    }

    private static IReadOnlyList<DiezVisualThemeOptionDto> Options(ThemeProjectState state)
    {
        var list = Builtins.Select(x => new DiezVisualThemeOptionDto(x.ThemeId, x.DisplayName, BuiltinOrigin, false)).ToList();
        foreach (var theme in LoadLibrary().Themes.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            list.Add(new DiezVisualThemeOptionDto("user:" + theme.Id, theme.DisplayName, UserLibraryOrigin, true));
        if (state.SelectedThemeId.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.SelectedThemeName))
            list.Add(new DiezVisualThemeOptionDto(state.SelectedThemeId, state.SelectedThemeName, UserAuthoredOrigin, true));
        list.Add(new DiezVisualThemeOptionDto(CustomSentinelId, "Custom", UserAuthoredOrigin, true));
        return list.GroupBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
    }

    private static void EnsureDefaultSelection(PreviewProject project, ThemeProjectState state)
    {
        if (!string.IsNullOrWhiteSpace(state.SelectedThemeId) && ResolveDefinition(state.SelectedThemeId, state.SelectedThemeName, state.ThemeOrigin, state.CustomThemeName) is not null)
            return;
        var subject = SubjectDescription(project);
        var inferred = Builtins.FirstOrDefault(x =>
            subject.Contains(x.DisplayName, StringComparison.OrdinalIgnoreCase) ||
            subject.Contains(x.CanonicalTheme, StringComparison.OrdinalIgnoreCase));
        inferred ??= Builtins[0];
        state.SelectedThemeId = inferred.ThemeId;
        state.SelectedThemeName = inferred.DisplayName;
        state.ThemeOrigin = inferred.Origin;
    }

    private static string SubjectDescription(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return BookTypePromptProfileService.LoadColoring(project).SubjectDescription ?? string.Empty;
        return ImageCollectionPromptProfileService.Load(project).SubjectDescription ?? string.Empty;
    }

    private static void SetAggregateSubject(PreviewProject project, int count, string themeName)
    {
        var aggregate = $"{count} soggetti di {themeName}";
        var type = BookTypeProfileService.Get(project);
        if (string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            p.SubjectDescription = aggregate;
            BookTypePromptProfileService.SaveColoring(project, p);
        }
        else
        {
            var p = ImageCollectionPromptProfileService.Load(project);
            p.SubjectDescription = aggregate;
            ImageCollectionPromptProfileService.Save(project, p);
        }
    }

    private static void InvalidateResolvedPlan(PreviewProject project)
    {
        var multi = MultiSubjectProfileService.Load(project);
        if (!multi.Enabled) return;
        multi.Enabled = false;
        MultiSubjectProfileService.Save(project, multi);
    }

    private static string StyleContext(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        return string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? BookTypePromptProfileService.LoadColoring(project).Style
            : ImageCollectionPromptProfileService.Load(project).RenderingStyle;
    }

    private static string AudienceContext(PreviewProject project)
    {
        var type = BookTypeProfileService.Get(project);
        return string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)
            ? BookTypePromptProfileService.LoadColoring(project).TargetAudience
            : "general editorial audience";
    }

    private static bool Mentions(string text, ThemeSubjectSnapshot subject)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains(subject.DisplayName, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(subject.CanonicalConcept) && text.Contains(subject.CanonicalConcept, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameSubject(ThemeSubjectSnapshot left, ThemeSubjectSnapshot right) =>
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(left.CanonicalConcept) &&
         string.Equals(left.CanonicalConcept, right.CanonicalConcept, StringComparison.OrdinalIgnoreCase));

    private static string StableRank(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static ThemeDefinition Builtin(string id, string display, string canonical, params ThemeSeedSubject[] subjects) => new()
    {
        ThemeId = id,
        DisplayName = display,
        CanonicalTheme = canonical,
        Origin = BuiltinOrigin,
        Subjects = subjects.ToList()
    };

    private static ThemeSeedSubject S(string display, string canonical) => new(display, canonical);

    private static DiezVisualThemeSubjectDto ToDto(ThemeSubjectSnapshot x) => new(
        x.ItemId, x.DisplayName, x.Description, x.CanonicalConcept, x.CanonicalDescription, x.Origin);

    private static ThemeSubjectSnapshot Clone(ThemeSubjectSnapshot x) => new()
    {
        ThemeId = x.ThemeId,
        ItemId = x.ItemId,
        DisplayName = x.DisplayName,
        Description = x.Description,
        CanonicalConcept = x.CanonicalConcept,
        CanonicalDescription = x.CanonicalDescription,
        Origin = x.Origin
    };

    private static ThemeProjectState LoadProjectState(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, StateEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null || string.IsNullOrWhiteSpace(entity.Notes)) return new ThemeProjectState();
        try
        {
            var state = JsonSerializer.Deserialize<ThemeProjectState>(entity.Notes, JsonOptions) ?? new ThemeProjectState();
            state.Proposal ??= [];
            state.ProjectAdditions ??= [];
            return state;
        }
        catch { return new ThemeProjectState(); }
    }

    private static void SaveProjectState(PreviewProject project, ThemeProjectState state)
    {
        var entity = project.Entities.FirstOrDefault(x => string.Equals(x.Kind, StateEntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = StateEntityKind, Name = "Libreria temi e proposta soggetti", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Name = "Libreria temi e proposta soggetti";
        entity.Notes = JsonSerializer.Serialize(state, JsonOptions);
    }

    private static UserThemeLibraryFile LoadLibrary()
    {
        try
        {
            var path = LibraryPath();
            if (!File.Exists(path)) return new UserThemeLibraryFile();
            var model = JsonSerializer.Deserialize<UserThemeLibraryFile>(File.ReadAllText(path), JsonOptions) ?? new UserThemeLibraryFile();
            model.Themes ??= [];
            model.Overlays ??= [];
            foreach (var theme in model.Themes) theme.Subjects ??= [];
            foreach (var overlay in model.Overlays) overlay.Subjects ??= [];
            return model;
        }
        catch { return new UserThemeLibraryFile(); }
    }

    private static void SaveLibrary(UserThemeLibraryFile model)
    {
        var path = LibraryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static UserThemeDefinition UpsertCustomTheme(string name)
    {
        var clean = name.Trim();
        var library = LoadLibrary();
        var existing = library.Themes.FirstOrDefault(x => string.Equals(x.DisplayName, clean, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var created = new UserThemeDefinition
        {
            DisplayName = clean,
            CanonicalTheme = string.Empty,
            Subjects = []
        };
        library.Themes.Add(created);
        SaveLibrary(library);
        return created;
    }

    private static void AddLibrarySubject(ThemeDefinition theme, ThemeSubjectSnapshot item)
    {
        var library = LoadLibrary();
        if (theme.ThemeId.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
        {
            var id = theme.ThemeId[5..];
            var user = library.Themes.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (user is null) return;
            if (!user.Subjects.Any(x => string.Equals(x.DisplayName, item.DisplayName, StringComparison.OrdinalIgnoreCase)))
                user.Subjects.Add(Clone(item));
        }
        else
        {
            var overlay = library.Overlays.FirstOrDefault(x => string.Equals(x.ThemeId, theme.ThemeId, StringComparison.OrdinalIgnoreCase));
            if (overlay is null)
            {
                overlay = new UserThemeOverlay { ThemeId = theme.ThemeId };
                library.Overlays.Add(overlay);
            }
            if (!overlay.Subjects.Any(x => string.Equals(x.DisplayName, item.DisplayName, StringComparison.OrdinalIgnoreCase)))
                overlay.Subjects.Add(Clone(item));
        }
        SaveLibrary(library);
    }

    private static string LibraryPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductInfo.ProductName,
        "config",
        "visual-themes.json");

    private static string Slug(string value)
    {
        var chars = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    private static (JsonObject Root, PreviewProject Project) Parse(string projectJson)
    {
        var root = JsonNode.Parse(projectJson) as JsonObject
            ?? throw new InvalidDataException("Il JSON del progetto Diez non è valido.");
        var project = JsonSerializer.Deserialize<PreviewProject>(projectJson, JsonOptions)
            ?? throw new InvalidDataException("Il progetto Diez non può essere letto dal Core.");
        project.EditionMetadata ??= new EditionMetadata();
        project.AiProduction ??= new AiProductionSettings();
        project.AiProductionJobs ??= [];
        project.Materials ??= [];
        project.ContentNodes ??= [];
        project.IllustrationPlacements ??= [];
        project.Entities ??= [];
        project.Relations ??= [];
        project.BibleEntries ??= [];
        project.ConsistencyFacts ??= [];
        project.ConsistencyIssues ??= [];
        project.ConsistencyResolutions ??= [];
        project.RevisionCandidates ??= [];
        return (root, project);
    }

    private static void MergeEntities(JsonObject root, PreviewProject project)
    {
        var raw = root["Entities"] as JsonArray ?? new JsonArray();
        root["Entities"] = raw;
        foreach (var entity in project.Entities)
        {
            if (JsonSerializer.SerializeToNode(entity, JsonOptions) is not JsonObject typed) continue;
            var id = Scalar(typed["EntityId"]);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var existing = raw.OfType<JsonObject>().FirstOrDefault(x =>
                string.Equals(Scalar(x["EntityId"]), id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) raw.Add(typed);
            else foreach (var pair in typed) existing[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        return node?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static string Write(JsonObject root) => root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
