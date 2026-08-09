namespace DiezPublishingStudio;

internal static class AiExchangeApiSelfTest
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZK1sAAAAASUVORK5CYII=");

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiezAiApiSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = ProjectFileStore.Create("API Test");
            BookTypeProfileService.Set(project, BookTypeProfileService.ImageCollection);
            for (var i = 1; i <= 4; i++)
                AiProductionService.CreateJob(project, AiProductionService.TypeImage, $"Immagine {i}", $"Soggetto {i}");
            var state = AiExchangeStateStore.Load(project);
            var units = state.WorkUnits.OrderBy(w => w.Position).ToList();
            Require(units.Count == 4, "Work Unit API non create.");

            var snapshot = new AiExchangeRequestSnapshot
            {
                JobId = units[0].JobId,
                PromptPackId = Guid.NewGuid(),
                Transport = "API",
                CreatedAtLocal = DateTimeOffset.Now.ToString("O"),
                Items = units.Select(u => new AiExchangeSnapshotItem
                {
                    WorkUnitId = u.WorkUnitId,
                    TargetCandidateVersion = AiExchangeStateStore.NextVersionNumber(state, u.WorkUnitId)
                }).ToList()
            };
            state.RequestSnapshots.Add(snapshot);

            var image1 = Path.Combine(root, "one.png");
            var image2 = Path.Combine(root, "two.png");
            var image3 = Path.Combine(root, "three.png");
            await File.WriteAllBytesAsync(image1, Png);
            await File.WriteAllBytesAsync(image2, Png);
            await File.WriteAllBytesAsync(image3, Png);

            IAiExchangeApiAdapter adapter = new AiExchangeMockApiAdapter();
            Require(adapter.Capabilities.ImageGeneration && adapter.Capabilities.ImageEdit && adapter.Capabilities.MultiImageReference,
                "Le capability visuali del provider non sono dichiarate.");

            var timeout = await adapter.RunAttemptAsync(project, state, snapshot, units[0].WorkUnitId, "TIMEOUT");
            var rateLimit = await adapter.RunAttemptAsync(project, state, snapshot, units[0].WorkUnitId, "RATE_LIMIT");
            Require(timeout is null && rateLimit is null, "Timeout/rate limit non restano tentativi senza versione editoriale.");
            Require(!state.Versions.Any(v => v.WorkUnitId == units[0].WorkUnitId),
                "Un errore di trasporto ha creato una versione.");

            var invalid = await adapter.RunAttemptAsync(project, state, snapshot, units[0].WorkUnitId, "INVALID_RESPONSE");
            Require(invalid?.Status == "INVALID", "Risposta provider invalida non isolata.");
            Require(!state.Versions.Any(v => v.WorkUnitId == units[0].WorkUnitId),
                "Risposta invalida ha creato una versione.");

            var missingDescription = await adapter.RunAttemptAsync(project, state, snapshot, units[0].WorkUnitId,
                "MISSING_DESCRIPTION", image1, description: "non usata");
            Require(missingDescription?.Status == "INCOMPLETE", "Immagine senza descrizione non è incompleta.");

            // Arrivo fuori ordine: la terza Work Unit può arrivare prima della seconda senza cambiare posizione/identità.
            var outOfOrder3 = await adapter.RunAttemptAsync(project, state, snapshot, units[2].WorkUnitId,
                "SUCCESS", image3, description: "Descrizione tre");
            var outOfOrder2 = await adapter.RunAttemptAsync(project, state, snapshot, units[1].WorkUnitId,
                "SUCCESS", image2, description: "Descrizione due");
            Require(outOfOrder3?.Status == "IMPORTED" && outOfOrder2?.Status == "IMPORTED",
                "Risultati API fuori ordine non importati.");
            Require(state.Versions.Any(v => v.WorkUnitId == units[1].WorkUnitId) &&
                    state.Versions.Any(v => v.WorkUnitId == units[2].WorkUnitId),
                "I risultati fuori ordine non conservano la propria Work Unit.");

            var duplicate = await adapter.RunAttemptAsync(project, state, snapshot, units[1].WorkUnitId,
                "SUCCESS", image2, description: "Descrizione due");
            Require(duplicate?.Status == "DUPLICATE", "Risultato API identico non riconosciuto come duplicato.");

            var missingAsset = await adapter.RunAttemptAsync(project, state, snapshot, units[3].WorkUnitId,
                "MISSING_ASSET", image3, description: "Descrizione quattro");
            Require(missingAsset?.Status == "INCOMPLETE", "Risposta senza asset non è incompleta.");

            var mock = (AiExchangeMockApiAdapter)adapter;
            Require(mock.Attempts == 9, "Numero tentativi API inatteso: retry e versioni non sono separati correttamente.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AI API SELF-TEST: " + message);
    }
}
