using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class FriendlyLayoutUi
{
    private const string DefaultHelp = "Suggerimento: seleziona o porta il focus su un comando per sapere cosa fa e quando usarlo.";

    public static void Attach(MainWindow window)
    {
        window.Width = 1400;
        window.Height = 790;
        window.MinWidth = 1000;
        window.MinHeight = 650;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (window.Content is not Border border || border.Child is not StackPanel root || root.Children.Count < 19)
            return;

        var logo = root.Children[0];
        var title = root.Children[1];
        var subtitle = root.Children[2];
        var projectButtons = root.Children[3] as StackPanel;
        var appStatus = root.Children[4] as TextBlock;

        var materialsLabel = root.Children[5] as TextBlock;
        var materialsList = root.Children[6] as ListBox;
        var masterLabel = root.Children[7] as TextBlock;
        var structureList = root.Children[8] as ListBox;
        var masterButtons = root.Children[9] as StackPanel;
        var entitiesLabel = root.Children[10] as TextBlock;
        var entitiesList = root.Children[11] as ListBox;
        var graphButtons = root.Children[12] as StackPanel;
        var issuesLabel = root.Children[13] as TextBlock;
        var issuesList = root.Children[14] as ListBox;
        var reviewButtons = root.Children[15] as StackPanel;
        var revisionButtons = root.Children[16] as StackPanel;
        var detailLabel = root.Children[17] as TextBlock;
        var preview = root.Children[18] as TextBox;

        if (projectButtons is null || appStatus is null ||
            materialsLabel is null || materialsList is null || masterLabel is null || structureList is null || masterButtons is null ||
            entitiesLabel is null || entitiesList is null || graphButtons is null || issuesLabel is null || issuesList is null ||
            reviewButtons is null || revisionButtons is null || detailLabel is null || preview is null)
            return;

        var buttonPanels = new[] { projectButtons, masterButtons, graphButtons, reviewButtons, revisionButtons };
        var allButtons = buttonPanels.SelectMany(panel => panel.Children.OfType<Button>()).ToList();

        RenameSections(materialsLabel, masterLabel, entitiesLabel, issuesLabel, detailLabel);
        RenameButtons(allButtons);

        var helpText = new TextBlock
        {
            Text = DefaultHelp,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        AttachHelp(helpText, allButtons, materialsList, structureList, entitiesList, issuesList, preview);

        // Reparent only after every original child has been captured and help handlers attached.
        root.Children.Clear();

        PrepareList(materialsList);
        PrepareList(structureList);
        PrepareList(entitiesList);
        PrepareList(issuesList);
        preview.Width = double.NaN;
        preview.Height = double.NaN;
        preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        preview.VerticalAlignment = VerticalAlignment.Stretch;

        if (subtitle is TextBlock subtitleText)
        {
            subtitleText.FontSize = 13;
            subtitleText.Text = "Il tuo progetto editoriale, dalla sorgente alla consegna modificabile";
        }
        if (logo is TextBlock logoText) logoText.FontSize = 28;
        if (title is TextBlock titleText) titleText.FontSize = 24;

        foreach (var button in projectButtons.Children.OfType<Button>())
            button.Width = 150;

        CompactButtonRow(masterButtons);
        CompactButtonRow(graphButtons);
        CompactButtonRow(reviewButtons);
        CompactButtonRow(revisionButtons);

        var helpBar = new Border
        {
            Padding = new Thickness(10, 6),
            Child = helpText
        };

        var header = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 5,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { logo, title, subtitle }
                },
                projectButtons.WithGridRow(1),
                appStatus.WithGridRow(2)
            }
        };

        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,82,Auto,116,Auto"),
            RowSpacing = 5,
            Margin = new Thickness(0, 0, 7, 0),
            Children =
            {
                materialsLabel,
                materialsList.WithGridRow(1),
                masterLabel.WithGridRow(2),
                structureList.WithGridRow(3),
                masterButtons.WithGridRow(4)
            }
        };

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,82,Auto,108,Auto,Auto"),
            RowSpacing = 5,
            Margin = new Thickness(7, 0, 0, 0),
            Children =
            {
                entitiesLabel,
                entitiesList.WithGridRow(1),
                issuesLabel.WithGridRow(2),
                issuesList.WithGridRow(3),
                reviewButtons.WithGridRow(4),
                revisionButtons.WithGridRow(5)
            }
        };

        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children =
            {
                left,
                right.WithGridColumn(1)
            }
        };

        var detail = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 4,
            Children =
            {
                detailLabel,
                preview.WithGridRow(1)
            }
        };

        var desktop = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,178,Auto"),
            RowSpacing = 8,
            Children =
            {
                header,
                workspace.WithGridRow(1),
                detail.WithGridRow(2),
                helpBar.WithGridRow(3)
            }
        };

        border.Padding = new Thickness(14, 10);
        border.Child = desktop;
    }

    private static void RenameSections(TextBlock materials, TextBlock master, TextBlock entities, TextBlock issues, TextBlock detail)
    {
        materials.Text = "Materiali del progetto";
        master.Text = "Testo di lavoro";
        entities.Text = "Riferimenti da tenere coerenti";
        issues.Text = "Controlli e possibili problemi";
        detail.Text = "Cosa c'è qui / Dettagli";

        foreach (var label in new[] { materials, master, entities, issues, detail })
        {
            label.Width = double.NaN;
            label.FontSize = 15;
            label.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static void RenameButtons(IEnumerable<Button> buttons)
    {
        var translations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Apri .diez"] = "Apri progetto",
            ["Importa materiali"] = "Aggiungi materiali",
            ["Rimuovi materiale"] = "Rimuovi",
            ["Modifica Master"] = "Modifica testo",
            ["Ripristina importato"] = "Torna all'originale",
            ["Conferma entità"] = "Tieni sotto controllo",
            ["Ignora entità"] = "Ignora",
            ["Segna rivisto"] = "Ho controllato",
            ["Accetta eccezione"] = "Va bene così",
            ["Riapri"] = "Riapri problema",
            ["Crea proposta"] = "Proponi correzione",
            ["Approva proposta"] = "Approva",
            ["Scarta proposta"] = "Scarta",
            ["Applica approvata"] = "Applica al testo",
            ["Edizione / Preflight"] = "Prepara consegna",
            ["Export / Handoff"] = "Esporta / Consegna"
        };

        foreach (var button in buttons)
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (translations.TryGetValue(text, out var friendly)) button.Content = friendly;
        }
    }

    private static void AttachHelp(TextBlock help, IEnumerable<Button> buttons, params Control[] contextControls)
    {
        var helpByButton = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nuovo progetto"] = "Crea un nuovo file .diez. Usalo quando inizi un nuovo libro, coloring o altro progetto editoriale.",
            ["Apri progetto"] = "Riapre un progetto .diez già esistente e recupera tutto il lavoro salvato.",
            ["Aggiungi materiali"] = "Aggiunge al progetto testi, documenti, tabelle o immagini e ne conserva una copia dentro il .diez.",
            ["Rimuovi"] = "Rimuove dal progetto il materiale selezionato e ripulisce i riferimenti che dipendono solo da quello.",
            ["Salva"] = "Salva nel .diez lo stato corrente del progetto. Usalo dopo modifiche importanti e prima di chiudere.",
            ["Prepara consegna"] = "Quando il lavoro è maturo, qui controlli i dati del libro, salvi una versione da verificare, esegui i controlli finali e la approvi per l'esportazione.",
            ["Esporta / Consegna"] = "Crea i file modificabili da portare in Word, Publisher, Excel, Canva o da dare a un impaginatore.",
            ["Modifica testo"] = "Modifica il capitolo o la sezione selezionata nel testo di lavoro, senza toccare l'originale importato.",
            ["Torna all'originale"] = "Riporta la sezione selezionata al testo che era stato importato all'inizio, conservando comunque la storia delle revisioni.",
            ["Tieni sotto controllo"] = "Conferma che il riferimento selezionato è importante e deve essere seguito per evitare incoerenze nel progetto.",
            ["Ignora"] = "Dice a Diez che il riferimento selezionato non è utile da controllare.",
            ["Ho controllato"] = "Segna che hai esaminato il possibile problema, senza dire ancora che è risolto.",
            ["Va bene così"] = "Accetta consapevolmente una differenza: non è un errore, è voluta o non richiede correzione.",
            ["Segna risolto"] = "Usalo quando il problema è stato realmente corretto o verificato come non più presente.",
            ["Riapri problema"] = "Riapre un problema già deciso se vuoi riesaminarlo.",
            ["Proponi correzione"] = "Prepara una possibile modifica separata dal testo. Non cambia ancora il libro.",
            ["Approva"] = "Approva la proposta che hai controllato. Il testo non cambia ancora finché non premi Applica al testo.",
            ["Scarta"] = "Rifiuta la proposta di correzione senza modificare il testo.",
            ["Applica al testo"] = "Applica davvero al testo di lavoro una proposta che hai già approvato."
        };

        foreach (var button in buttons)
        {
            var key = button.Content?.ToString() ?? string.Empty;
            if (!helpByButton.TryGetValue(key, out var message)) continue;
            ToolTip.SetTip(button, message);
            button.GotFocus += (_, _) => help.Text = message;
            button.PointerEntered += (_, _) => help.Text = message;
        }

        var context = new Dictionary<Control, string>
        {
            [contextControls[0]] = "Materiali del progetto: qui vedi gli originali incorporati. Selezionane uno per controllare provenienza, tipo e anteprima nei Dettagli.",
            [contextControls[1]] = "Testo di lavoro: capitoli e sezioni su cui puoi lavorare senza sovrascrivere gli originali importati.",
            [contextControls[2]] = "Riferimenti da tenere coerenti: persone, luoghi o altri elementi che Diez ha individuato e che puoi decidere di seguire o ignorare.",
            [contextControls[3]] = "Controlli e possibili problemi: qui compaiono differenze o incoerenze da esaminare. Una segnalazione non modifica mai da sola il testo.",
            [contextControls[4]] = "Dettagli: spiega l'elemento che hai selezionato e, quando c'è una proposta, mostra cosa cambierebbe prima di applicarla."
        };

        foreach (var pair in context)
        {
            ToolTip.SetTip(pair.Key, pair.Value);
            pair.Key.GotFocus += (_, _) => help.Text = pair.Value;
            pair.Key.PointerEntered += (_, _) => help.Text = pair.Value;
        }
    }

    private static void PrepareList(ListBox list)
    {
        list.Width = double.NaN;
        list.Height = double.NaN;
        list.HorizontalAlignment = HorizontalAlignment.Stretch;
        list.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private static void CompactButtonRow(StackPanel panel)
    {
        panel.Spacing = 6;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        foreach (var button in panel.Children.OfType<Button>())
            button.Width = 145;
    }
}
