using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace DiezPublishingStudio;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupProjectPath = desktop.Args?
                .FirstOrDefault(a => a.EndsWith(".diez", StringComparison.OrdinalIgnoreCase));
            var mainWindow = new MainWindow(startupProjectPath);
            desktop.MainWindow = mainWindow;

            var failures = new List<string>();
            if (!StartupDiagnostics.TryAttach("Prepara consegna", () => EditionWorkflowUi.Attach(mainWindow), out var editionError) && editionError is not null)
                failures.Add(editionError);
            if (!StartupDiagnostics.TryAttach("Esporta / Consegna", () => HandoffWorkflowUi.Attach(mainWindow), out var handoffError) && handoffError is not null)
                failures.Add(handoffError);
            if (!StartupDiagnostics.TryAttach("Contenuti con AI", () => AiProductionUi.Attach(mainWindow), out var aiError) && aiError is not null)
                failures.Add(aiError);
            if (!StartupDiagnostics.TryAttach("Prompt Pack AI universale", () => AiExchangeUi.Attach(mainWindow), out var exchangeError) && exchangeError is not null)
                failures.Add(exchangeError);
            if (!StartupDiagnostics.TryAttach("Correzione AI mirata", () => AiExchangeCorrectionUi.Attach(mainWindow), out var correctionError) && correctionError is not null)
                failures.Add(correctionError);
            if (!StartupDiagnostics.TryAttach("Istruzioni AI: deve fare / non deve fare", () => HumanAiPromptUi.Attach(mainWindow), out var humanPromptError) && humanPromptError is not null)
                failures.Add(humanPromptError);
            if (!StartupDiagnostics.TryAttach("Box AI editabili", () => HumanAiPromptInputGuard.Attach(mainWindow), out var humanPromptInputError) && humanPromptInputError is not null)
                failures.Add(humanPromptInputError);
            if (!StartupDiagnostics.TryAttach("Scelte AI per Tipo libro", () => BookTypeAiOptionsUi.Attach(mainWindow), out var bookAiOptionsError) && bookAiOptionsError is not null)
                failures.Add(bookAiOptionsError);
            if (!StartupDiagnostics.TryAttach("Serie immagini AI", () => AiImageBatchUi.Attach(mainWindow), out var batchError) && batchError is not null)
                failures.Add(batchError);
            if (!StartupDiagnostics.TryAttach("Descrizioni e impaginazione immagini", () => ImageCollectionDescriptionUi.Attach(mainWindow), out var imageDescriptionError) && imageDescriptionError is not null)
                failures.Add(imageDescriptionError);
            if (!StartupDiagnostics.TryAttach("Layout e aiuto contestuale", () => FriendlyLayoutUi.Attach(mainWindow), out var layoutError) && layoutError is not null)
                failures.Add(layoutError);
            if (!StartupDiagnostics.TryAttach("Libreria libri finalizzati", () => FinalizedLibraryUi.Attach(mainWindow), out var finalizedLibraryError) && finalizedLibraryError is not null)
                failures.Add(finalizedLibraryError);
            if (!StartupDiagnostics.TryAttach("Ambiente del libro", () => BookWorkspaceTabsUi.Attach(mainWindow), out var workspaceError) && workspaceError is not null)
                failures.Add(workspaceError);
            if (!StartupDiagnostics.TryAttach("Host unico dei tab del libro", () => UnifiedBookWorkspaceUi.Attach(mainWindow), out var unifiedWorkspaceError) && unifiedWorkspaceError is not null)
                failures.Add(unifiedWorkspaceError);
            if (!StartupDiagnostics.TryAttach("Database e sostituzioni Word Search", () => WordSearchDatabaseToolsUi.Attach(mainWindow), out var wordSearchToolsError) && wordSearchToolsError is not null)
                failures.Add(wordSearchToolsError);
            if (!StartupDiagnostics.TryAttach("Word Search su Fogli Google", () => WordSearchGoogleExportUi.Attach(mainWindow), out var wordSearchGoogleError) && wordSearchGoogleError is not null)
                failures.Add(wordSearchGoogleError);
            if (!StartupDiagnostics.TryAttach("Guida passo passo", () => GuidedModeUi.Attach(mainWindow), out var guideError) && guideError is not null)
                failures.Add(guideError);
            if (!StartupDiagnostics.TryAttach("Tipo libro persistente", () => BookTypeProfileUi.Attach(mainWindow), out var bookTypeError) && bookTypeError is not null)
                failures.Add(bookTypeError);
            if (!StartupDiagnostics.TryAttach("Linguaggio semplice", () => PlainLanguageUi.Attach(mainWindow), out var languageError) && languageError is not null)
                failures.Add(languageError);

            StartupDiagnostics.ShowWarning(mainWindow, failures);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
