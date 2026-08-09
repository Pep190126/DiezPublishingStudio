namespace DiezPublishingStudio;

internal static class WordSearchReplacementSelfTest
{
    public static void Run()
    {
        var project = ProjectFileStore.Create("Replacement Test");
        var database = string.Join(Environment.NewLine,
            "ID;WORD;CATEGORY;SUBCATEGORY;SERIES;DECADE;YEAR;RELEVANCE;KDPSAFE",
            "W001;TELEFONO;Tecnologia;Comunicazione;Casa e città;1980S;1985;8;YES",
            "W002;CABINA;Tecnologia;Comunicazione;Casa e città;1980S;1985;9;YES",
            "W003;GETTONE;Tecnologia;Comunicazione;Casa e città;1980S;1984;9;YES",
            "W004;WALKMAN;Tecnologia;Audio;Elettronica;1980S;1985;10;YES",
            "W005;MARCHIO;Tecnologia;Comunicazione;Casa e città;1980S;1985;10;NO",
            "W006;SEGRETERIA;Tecnologia;Comunicazione;Casa e città;1980S;1985;7;YES",
            "W007;CABINA TELEFONICA;Tecnologia;Comunicazione;Casa e città;1980S;1985;10;YES");

        var import = WordSearchLexiconService.ImportDelimitedText(project, database, "Test");
        if (!import.Recognized || WordSearchLexiconService.GetEntries(project).Count != 7)
            throw new InvalidOperationException("Il database parole classificato non viene riconosciuto.");

        var first = new WordSearchRecord
        {
            Order = 1,
            Id = "PUZ-001",
            Title = "Tecnologia anni 80",
            Theme = "Tecnologia",
            Words = new List<string> { "TELEFONO", "RADIO", "TV", "MODEM", "COMPUTER" },
            Origin = "Test"
        };
        WordSearchWorkspaceService.SaveRecord(project, first);
        WordSearchDatabaseService.SetExpectedWordCount(project, first.Id, 5);

        var second = new WordSearchRecord
        {
            Order = 2,
            Id = "PUZ-002",
            Title = "Altro",
            Theme = "Tecnologia",
            Words = new List<string> { "CABINA", "VIDEOGAME", "CASSETTA", "JOYSTICK", "ARCADE" },
            Origin = "Test"
        };
        WordSearchWorkspaceService.SaveRecord(project, second);
        WordSearchDatabaseService.SetExpectedWordCount(project, second.Id, 5);

        var current = WordSearchWorkspaceService.GetRecords(project).Single(r => r.Id == "PUZ-001");
        var suggestions = WordSearchReplacementService.Suggest(project, current, 1, maxLength: 12, maxResults: 20);
        if (suggestions.Count == 0)
            throw new InvalidOperationException("Non vengono proposte sostituzioni contestuali.");
        if (suggestions.Any(s => string.Equals(s.Word, "CABINA", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Una parola già usata in un altro puzzle non deve essere suggerita.");
        if (suggestions.Any(s => string.Equals(s.Word, "MARCHIO", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Una parola KDPSAFE=NO non deve essere suggerita.");
        if (suggestions.Any(s => string.Equals(s.Word, "SEGRETERIA", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Una parola con rilevanza inferiore all'originale non deve essere suggerita.");
        if (suggestions.Any(s => string.Equals(s.Word, "CABINA TELEFONICA", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Una parola oltre la lunghezza massima non deve essere suggerita.");
        if (!string.Equals(suggestions[0].Word, "GETTONE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La sostituzione con stessa serie/sottocategoria/categoria/decade non è prioritaria.");

        var beforeSecond = string.Join("|", WordSearchWorkspaceService.GetRecords(project).Single(r => r.Id == "PUZ-002").Words);
        var replace = WordSearchReplacementService.Replace(project, current, 1, suggestions[0]);
        if (!replace.Success) throw new InvalidOperationException("La sostituzione chirurgica non riesce.");
        var afterFirst = WordSearchWorkspaceService.GetRecords(project).Single(r => r.Id == "PUZ-001");
        var afterSecond = string.Join("|", WordSearchWorkspaceService.GetRecords(project).Single(r => r.Id == "PUZ-002").Words);
        if (afterFirst.Words[0] != "GETTONE")
            throw new InvalidOperationException("La parola selezionata non è stata sostituita.");
        if (!string.Equals(beforeSecond, afterSecond, StringComparison.Ordinal))
            throw new InvalidOperationException("La sostituzione di PUZ-001 ha modificato un altro puzzle.");
        if (afterFirst.Status != WordSearchWorkspaceService.StatusToReview)
            throw new InvalidOperationException("Dopo una sostituzione il puzzle deve tornare da controllare.");
    }
}
