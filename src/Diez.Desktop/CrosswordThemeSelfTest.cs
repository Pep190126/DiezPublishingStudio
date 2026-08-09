namespace DiezPublishingStudio;

internal static class CrosswordThemeSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezCrosswordTheme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = ProjectFileStore.Create("Cruciverba tematico");
            BookTypeProfileService.Set(project, BookTypeProfileService.Crossword);
            CrosswordService.SetSetting(project, "ThemeMode", "Tematico");
            CrosswordService.SetSetting(project, "Theme", "Astronomia");

            var mercurio = CrosswordService.EnsureWord(project, "MERCURIO");
            var luna = CrosswordService.EnsureWord(project, "LUNA");
            var stella = CrosswordService.EnsureWord(project, "STELLA");
            var yard = CrosswordService.EnsureWord(project, "YARD");

            CrosswordThemeService.SetRole(project, mercurio.EntityId, CrosswordThemeService.Required);
            CrosswordThemeService.SetRole(project, luna.EntityId, CrosswordThemeService.Required);
            CrosswordThemeService.SetRole(project, stella.EntityId, CrosswordThemeService.Preferred);
            CrosswordThemeService.SetRole(project, yard.EntityId, CrosswordThemeService.Fallback);

            Require(CrosswordThemeService.ByRole(project, CrosswordThemeService.Required).Count == 2,
                "Le parole obbligatorie non vengono conservate correttamente.");
            Require(CrosswordThemeService.ByRole(project, CrosswordThemeService.Preferred).Single().Name == "STELLA",
                "La parola preferita non è riconosciuta.");
            Require(CrosswordThemeService.GetRole(project, yard.EntityId) == CrosswordThemeService.Fallback,
                "La parola di soccorso non è riconosciuta.");
            Require(CrosswordThemeService.GetRole(project, CrosswordService.EnsureWord(project, "SOLE").EntityId) == CrosswordThemeService.Normal,
                "Una parola senza ruolo esplicito deve restare Normale.");

            var path = Path.Combine(root, "tema.diez");
            await ProjectFileStore.SaveAsync(path, project);
            var loaded = await ProjectFileStore.LoadAsync(path);

            Require(CrosswordService.GetSetting(loaded, "Theme") == "Astronomia", "Il tema non è sopravvissuto al .diez.");
            var loadedMercury = CrosswordService.FindWord(loaded, "MERCURIO") ?? throw new InvalidOperationException("MERCURIO mancante dopo il reload.");
            var loadedYard = CrosswordService.FindWord(loaded, "YARD") ?? throw new InvalidOperationException("YARD mancante dopo il reload.");
            Require(CrosswordThemeService.GetRole(loaded, loadedMercury.EntityId) == CrosswordThemeService.Required,
                "Il ruolo Obbligatoria non è sopravvissuto al .diez.");
            Require(CrosswordThemeService.GetRole(loaded, loadedYard.EntityId) == CrosswordThemeService.Fallback,
                "Il ruolo Soccorso non è sopravvissuto al .diez.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("CROSSWORD THEME SELF-TEST: " + message);
    }
}
