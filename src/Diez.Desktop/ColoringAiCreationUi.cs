using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Dedicated guided creation surface for Coloring Book.
/// A coloring project creates images only: the user specifies the exact image count,
/// writes positive/negative instructions, may edit the generated prompt, and then
/// creates the corresponding stable image series in Diez.
/// </summary>
internal static class ColoringAiCreationUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.OfType<SimpleAiCreationWindow>().ToList())
            {
                if (Attached.Contains(window)) continue;
                var projectType = GetPrivateString(window, "_projectType");
                if (!string.Equals(projectType, "Coloring book", StringComparison.OrdinalIgnoreCase)) continue;
                Attached.Add(window);
                ReplaceContent(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void ReplaceContent(SimpleAiCreationWindow window)
    {
        var project = GetPrivate<PreviewProject>(window, "_project");
        var projectPath = GetPrivateString(window, "_projectPath");
        if (project is null || string.IsNullOrWhiteSpace(projectPath)) return;

        window.Title = "Coloring Book — crea immagini con AI";
        window.Width = 940;
        window.Height = 820;
        window.MinWidth = 800;
        window.MinHeight = 680;

        var count = Editor("1", 46);
        count.AcceptsReturn = false;
        count.Width = 120;
        count.HorizontalAlignment = HorizontalAlignment.Left;

        var mustDo = Editor(string.Empty, 110);
        mustDo.Watermark = "Es. Crea pagine da colorare con linee nere pulite, soggetti diversi, sfondo bianco e nessun testo.";

        var mustNotDo = Editor(string.Empty, 95);
        mustNotDo.Watermark = "Es. Non usare ombreggiature, grigi, testo, cornici o dettagli troppo fitti.";

        var prompt = Editor(string.Empty, 190);
        prompt.Watermark = "Diez prepara qui una bozza; puoi modificarla liberamente prima di copiarla o creare la serie.";

        var consistent = new CheckBox
        {
            Content = "Consistent — mantieni coerenti le immagini della raccolta",
            IsChecked = !string.IsNullOrWhiteSpace(ImageCollectionWorkspaceService.GetConsistencyRules(project))
        };
        var consistentRules = Editor(ImageCollectionWorkspaceService.GetConsistencyRules(project), 75);
        consistentRules.Watermark = "Es. stesso personaggio, stesso stile e tratto; soggetti/ambientazioni possono variare.";

        var status = new TextBlock
        {
            Text = "Indica il numero esatto di immagini. Tutti e tre i box di testo sono modificabili, copiabili e supportano Ctrl+Z.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var prepare = Button("Prepara prompt", 160);
        var copy = Button("Copia prompt", 145);
        var create = Button("Crea serie in Diez", 170);

        void PreparePrompt()
        {
            if (!TryCount(count.Text, out var imageCount, out var error))
            {
                status.Text = error;
                return;
            }
            prompt.Text = BuildMasterPrompt(project, imageCount, mustDo.Text, mustNotDo.Text, consistent.IsChecked == true, consistentRules.Text);
            status.Text = $"Prompt preparato per {imageCount} immagini. Puoi modificarlo liberamente prima di copiarlo o creare la serie.";
        }

        prepare.Click += (_, _) => PreparePrompt();
        copy.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(prompt.Text)) PreparePrompt();
            if (string.IsNullOrWhiteSpace(prompt.Text)) return;
            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                status.Text = "Non riesco ad accedere agli appunti di Windows.";
                return;
            }
            await clipboard.SetTextAsync(prompt.Text);
            status.Text = "Prompt copiato. Le modifiche manuali del box sono incluse.";
        };

        create.Click += async (_, _) =>
        {
            if (!TryCount(count.Text, out var imageCount, out var error))
            {
                status.Text = error;
                return;
            }
            if (string.IsNullOrWhiteSpace(mustDo.Text))
            {
                status.Text = "Scrivi prima nel box DEVE FARE cosa devono rappresentare e come devono essere le immagini.";
                mustDo.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt.Text)) PreparePrompt();
            if (string.IsNullOrWhiteSpace(prompt.Text)) return;

            var request = HumanAiPromptService.Write(mustDo.Text, mustNotDo.Text);
            var created = AiImageBatchService.CreateImageSeries(project, imageCount, request, "Pagina").ToList();
            for (var i = 0; i < created.Count; i++)
            {
                var item = created[i];
                item.Prompt = new StringBuilder()
                    .AppendLine(prompt.Text!.Trim())
                    .AppendLine()
                    .AppendLine($"ELEMENTO DIEZ: {item.Code}")
                    .AppendLine($"Questa è l'immagine {i + 1} di {created.Count}.")
                    .AppendLine("Genera un risultato distinto dagli altri senza cambiare le regole comuni.")
                    .ToString().Trim();
            }

            ImageCollectionWorkspaceService.SetConsistencyRules(project,
                consistent.IsChecked == true ? (consistentRules.Text ?? string.Empty).Trim() : string.Empty);
            var state = AiExchangeStateStore.Load(project);
            AiExchangeStateStore.EnsureVisualConsistencyContext(project, state,
                consistent.IsChecked == true, consistentRules.Text);
            AiExchangeStateStore.Save(project, state);
            await ProjectFileStore.SaveAsync(projectPath, project);
            status.Text = imageCount == 1
                ? "Creata 1 immagine in Diez. Ora puoi aprire Prompt Pack AI per esportarla."
                : $"Create {imageCount} immagini con ID stabili. Ora apri Prompt Pack AI: Diez le includerà come una sola serie logica.";
        };

        consistent.IsCheckedChanged += (_, _) => consistentRules.IsEnabled = consistent.IsChecked == true;
        consistentRules.IsEnabled = consistent.IsChecked == true;

        window.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new StackPanel
                {
                    Spacing = 9,
                    Children =
                    {
                        new TextBlock { Text = "Coloring Book: prepara le immagini", FontSize = 24 },
                        new TextBlock
                        {
                            Text = "Per un Coloring Book Diez crea immagini, non testo o tabelle. Specifica quante immagini vuoi ottenere.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        Label("Quante immagini vuoi creare? (numero preciso)"),
                        count,
                        Label("DEVE FARE"),
                        mustDo,
                        Label("NON DEVE FARE"),
                        mustNotDo,
                        consistent,
                        consistentRules,
                        Label("PROMPT — bozza modificabile"),
                        prompt,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { prepare, copy, create }
                        },
                        status
                    }
                }
            }
        };
    }

    internal static bool TryCount(string? text, out int count, out string error)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out count) || count < 1 || count > 500)
        {
            error = "Inserisci il numero preciso di immagini, da 1 a 500.";
            count = 0;
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static string BuildMasterPrompt(PreviewProject project, int count, string? mustDo, string? mustNotDo, bool consistent, string? rules)
    {
        var sb = new StringBuilder();
        var common = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(common))
        {
            sb.AppendLine("REGOLE COMUNI DEL PROGETTO:");
            sb.AppendLine(common);
            sb.AppendLine();
        }
        sb.AppendLine($"Crea {count} {(count == 1 ? "immagine" : "immagini")} per un Coloring Book.");
        sb.AppendLine();
        sb.AppendLine("DEVE FARE:");
        sb.AppendLine((mustDo ?? string.Empty).Trim());
        sb.AppendLine();
        sb.AppendLine("NON DEVE FARE:");
        sb.AppendLine((mustNotDo ?? string.Empty).Trim());
        if (consistent)
        {
            sb.AppendLine();
            sb.AppendLine("CONSISTENT:");
            sb.AppendLine(string.IsNullOrWhiteSpace(rules)
                ? "Mantieni coerenti personaggi, stile e tratto fra tutte le immagini, salvo variazioni richieste esplicitamente."
                : rules!.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("Ogni immagine deve essere distinta, rispettare le stesse regole comuni e non contenere numeri, ID o nomi file dentro l'immagine.");
        return sb.ToString().Trim();
    }

    private static TextBox Editor(string text, double height) => new()
    {
        Text = text,
        AcceptsReturn = true,
        Height = height,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        IsReadOnly = false,
        IsEnabled = true,
        IsHitTestVisible = true,
        Focusable = true,
        IsUndoEnabled = true
    };

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 16 };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static T? GetPrivate<T>(object instance, string fieldName) where T : class =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;

    private static string GetPrivateString(object instance, string fieldName) =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as string ?? string.Empty;
}
