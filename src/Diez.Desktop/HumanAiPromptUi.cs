using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal readonly record struct HumanAiPromptIntent(string MustDo, string MustNotDo);

internal static class HumanAiPromptService
{
    private const string MustDoHeading = "DEVE FARE:";
    private const string MustNotDoHeading = "NON DEVE FARE:";

    public static HumanAiPromptIntent Read(string? request)
    {
        var text = (request ?? string.Empty).Trim();
        if (text.Length == 0) return new(string.Empty, string.Empty);

        var doIndex = text.IndexOf(MustDoHeading, StringComparison.OrdinalIgnoreCase);
        var dontIndex = text.IndexOf(MustNotDoHeading, StringComparison.OrdinalIgnoreCase);
        if (doIndex < 0 || dontIndex < 0 || dontIndex <= doIndex)
            return new(text, string.Empty);

        var mustDo = text[(doIndex + MustDoHeading.Length)..dontIndex].Trim();
        var mustNotDo = text[(dontIndex + MustNotDoHeading.Length)..].Trim();
        return new(mustDo, mustNotDo);
    }

    public static string Write(string? mustDo, string? mustNotDo)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(MustDoHeading);
        builder.AppendLine((mustDo ?? string.Empty).Trim());
        builder.AppendLine();
        builder.AppendLine(MustNotDoHeading);
        builder.Append((mustNotDo ?? string.Empty).Trim());
        return builder.ToString().TrimEnd();
    }

    public static string BuildPrompt(PreviewProject project, AiProductionJob job)
    {
        var intent = Read(job.Request);
        var commonRules = (project.AiProduction?.ProjectBrief ?? string.Empty).Trim();
        var title = (job.Title ?? string.Empty).Trim();
        var outputInstruction = job.OutputType switch
        {
            AiProductionService.TypeImage => "Restituisci una singola immagine pronta da controllare. Non inserire testo, cornici o elementi estranei salvo che siano richiesti esplicitamente sopra.",
            AiProductionService.TypeData => "Restituisci dati strutturati e regolari, facili da usare in CSV/XLSX, senza commenti estranei ai dati.",
            _ => "Restituisci soltanto il testo proposto, pronto per essere controllato e modificato; non considerarlo già approvato o applicato al libro."
        };

        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(commonRules))
        {
            builder.AppendLine("REGOLE COMUNI DEL PROGETTO:");
            builder.AppendLine(commonRules);
            builder.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine("CONTENUTO:");
            builder.AppendLine(title);
            builder.AppendLine();
        }
        builder.AppendLine(MustDoHeading);
        builder.AppendLine(string.IsNullOrWhiteSpace(intent.MustDo)
            ? "Segui le regole comuni del progetto per creare questo contenuto."
            : intent.MustDo);
        builder.AppendLine();
        builder.AppendLine(MustNotDoHeading);
        builder.AppendLine(intent.MustNotDo);
        builder.AppendLine();
        builder.AppendLine("RISULTATO DA RESTITUIRE:");
        builder.Append(outputInstruction);
        return builder.ToString().Trim();
    }

    public static bool NormalizeProject(PreviewProject project)
    {
        var changed = false;
        foreach (var job in project.AiProductionJobs)
        {
            var humanPrompt = BuildPrompt(project, job);
            if (string.Equals(job.Prompt, humanPrompt, StringComparison.Ordinal)) continue;
            job.Prompt = humanPrompt;
            changed = true;
        }
        return changed;
    }
}

internal static class HumanAiPromptUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (TryGetMainProject(mainWindow, out var project)) HumanAiPromptService.NormalizeProject(project);

            foreach (var window in desktop.Windows.ToList())
            {
                if (window is not (AiProductionWindow or AiJobEditorWindow or SimpleAiCreationWindow)) continue;
                if (Attached.Add(window)) AttachWindow(window);
                RefreshVisiblePrompt(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void AttachWindow(Window window)
    {
        window.Closed += (_, _) => Attached.Remove(window);
        switch (window)
        {
            case AiJobEditorWindow:
                AttachJobEditor(window);
                break;
            case AiProductionWindow:
                AttachProductionWindow(window);
                break;
            case SimpleAiCreationWindow:
                AttachSimpleCreation(window);
                break;
        }
    }

    private static void AttachJobEditor(Window window)
    {
        window.Title = "Nuovo contenuto con AI";
        window.Height = Math.Max(window.Height, 610);
        window.MinHeight = Math.Max(window.MinHeight, 540);

        if (GetPrivate<TextBox>(window, "_request") is not TextBox request) return;
        var field = FindPanelContaining(window, request);
        if (field is null) return;

        var label = field.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is not null) label.Text = "DEVE FARE";
        request.Watermark = "Descrivi in modo concreto ciò che vuoi ottenere.";
        request.Height = 105;

        var mustNotDo = NewInstructionBox("Scrivi cosa deve evitare. Puoi lasciare vuoto se non hai divieti particolari.", 105);
        var initial = HumanAiPromptService.Read(request.Text);
        var syncing = true;
        request.Text = initial.MustDo;
        mustNotDo.Text = initial.MustNotDo;
        syncing = false;

        field.Children.Add(new TextBlock { Text = "NON DEVE FARE", Margin = new Thickness(0, 5, 0, 0) });
        field.Children.Add(mustNotDo);
        field.Children.Add(new TextBlock
        {
            Text = "Diez costruisce le istruzioni complete usando questi due campi e le regole comuni del progetto.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });

        void Sync()
        {
            if (syncing) return;
            syncing = true;
            var mustDo = request.Text ?? string.Empty;
            request.Text = HumanAiPromptService.Write(mustDo, mustNotDo.Text);
            syncing = false;
        }

        // Il TextBox originale resta quello letto dalla finestra al momento della creazione.
        // Mostriamo il testo umano nel controllo e codifichiamo la coppia solo immediatamente prima del click.
        request.TextChanged += (_, _) =>
        {
            if (syncing) return;
            request.Tag = request.Text ?? string.Empty;
        };
        mustNotDo.TextChanged += (_, _) => request.Tag = request.Tag ?? request.Text ?? string.Empty;

        var create = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Crea", StringComparison.OrdinalIgnoreCase));
        if (create is not null)
        {
            create.Content = "Crea istruzioni";
            create.AddHandler(Button.ClickEvent, (_, _) =>
            {
                var mustDo = request.Tag?.ToString() ?? request.Text ?? string.Empty;
                syncing = true;
                request.Text = HumanAiPromptService.Write(mustDo, mustNotDo.Text);
                syncing = false;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        var technical = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => (t.Text ?? string.Empty).Contains("prompt verrà costruito", StringComparison.OrdinalIgnoreCase));
        if (technical is not null)
            technical.Text = "Diez unirà queste indicazioni alle regole comuni del progetto e preparerà il testo da copiare nella tua AI.";
    }

    private static void AttachProductionWindow(Window window)
    {
        if (GetPrivate<TextBox>(window, "_request") is not TextBox request ||
            GetPrivate<TextBox>(window, "_prompt") is not TextBox prompt) return;
        var parent = FindPanelOrGridContaining(window, request);
        if (parent is not Grid grid) return;

        var oldLabel = grid.Children.OfType<TextBlock>()
            .FirstOrDefault(t => Grid.GetRow(t) == 1 && (t.Text ?? string.Empty).Contains("Richiesta", StringComparison.OrdinalIgnoreCase));
        if (oldLabel is not null) oldLabel.IsVisible = false;

        grid.Children.Remove(request);
        request.Height = 72;
        request.Watermark = "Cosa deve ottenere l'AI.";
        var mustNotDo = NewInstructionBox("Cosa deve evitare. Facoltativo.", 72);
        var doLabel = new TextBlock { Text = "DEVE FARE" };
        var dontLabel = new TextBlock { Text = "NON DEVE FARE" };
        var editor = new StackPanel
        {
            Spacing = 3,
            Children = { doLabel, request, dontLabel, mustNotDo }
        };
        Grid.SetRow(editor, 2);
        grid.Children.Add(editor);
        if (grid.RowDefinitions.Count > 2) grid.RowDefinitions[2].Height = GridLength.Auto;
        prompt.Height = 145;

        var syncing = false;
        void LoadFromSelected()
        {
            var job = SelectedJob(window);
            if (job is null) return;
            var intent = HumanAiPromptService.Read(job.Request);
            syncing = true;
            request.Text = intent.MustDo;
            mustNotDo.Text = intent.MustNotDo;
            HumanAiPromptService.NormalizeProject(GetPrivate<PreviewProject>(window, "_project") ?? new PreviewProject());
            prompt.Text = job.Prompt;
            syncing = false;
        }
        void StoreIntoHiddenModel()
        {
            if (syncing) return;
            var job = SelectedJob(window);
            if (job is null) return;
            job.Request = HumanAiPromptService.Write(request.Text, mustNotDo.Text);
            var project = GetPrivate<PreviewProject>(window, "_project");
            if (project is not null)
            {
                job.Prompt = HumanAiPromptService.BuildPrompt(project, job);
                prompt.Text = job.Prompt;
            }
        }

        request.TextChanged += (_, _) => StoreIntoHiddenModel();
        mustNotDo.TextChanged += (_, _) => StoreIntoHiddenModel();
        if (GetPrivate<ListBox>(window, "_jobs") is ListBox jobs)
            jobs.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(LoadFromSelected);

        var rebuild = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Aggiorna istruzioni", StringComparison.OrdinalIgnoreCase) ||
                                 (b.Content?.ToString() ?? string.Empty).Contains("Ricrea prompt", StringComparison.OrdinalIgnoreCase));
        if (rebuild is not null)
            rebuild.AddHandler(Button.ClickEvent, (_, _) => StoreIntoHiddenModel(), Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Dispatcher.UIThread.Post(LoadFromSelected);
    }

    private static void AttachSimpleCreation(Window window)
    {
        if (GetPrivate<TextBox>(window, "_request") is not TextBox request) return;
        if (window.Content is not Border border || border.Child is not StackPanel stack) return;

        var index = stack.Children.IndexOf(request);
        if (index < 0) return;
        var previousLabel = index > 0 ? stack.Children[index - 1] as TextBlock : null;
        if (previousLabel is not null) previousLabel.Text = "DEVE FARE";

        request.Height = 95;
        request.Watermark = "Descrivi cosa vuoi che l'AI faccia.";
        var mustNotDo = NewInstructionBox("Descrivi cosa non deve fare. Puoi lasciare vuoto.", 95);
        stack.Children.Insert(index + 1, new TextBlock { Text = "NON DEVE FARE" });
        stack.Children.Insert(index + 2, mustNotDo);

        // La finestra guidata può crescere: rendiamo il contenuto scorrevole invece di forzare un'altezza enorme.
        border.Child = null;
        border.Child = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        window.Height = Math.Max(window.Height, 740);

        var syncing = false;
        void EncodeForCreation()
        {
            if (syncing) return;
            syncing = true;
            var plainMustDo = request.Tag?.ToString() ?? request.Text ?? string.Empty;
            request.Text = HumanAiPromptService.Write(plainMustDo, mustNotDo.Text);
            syncing = false;
        }
        request.TextChanged += (_, _) =>
        {
            if (!syncing) request.Tag = request.Text ?? string.Empty;
        };

        var prepare = stack.Children.OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Prepara le istruzioni", StringComparison.Ordinal));
        if (prepare is not null)
        {
            prepare.AddHandler(Button.ClickEvent, (_, _) => EncodeForCreation(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            prepare.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                var project = GetPrivate<PreviewProject>(window, "_project");
                var job = GetPrivate<AiProductionJob>(window, "_job");
                var instructions = GetPrivate<TextBox>(window, "_instructions");
                if (project is null || job is null || instructions is null) return;
                job.Prompt = HumanAiPromptService.BuildPrompt(project, job);
                instructions.Text = job.Prompt;
                var intent = HumanAiPromptService.Read(job.Request);
                syncing = true;
                request.Text = intent.MustDo;
                request.Tag = intent.MustDo;
                mustNotDo.Text = intent.MustNotDo;
                syncing = false;
            });
        }
    }

    private static void RefreshVisiblePrompt(Window window)
    {
        var project = GetPrivate<PreviewProject>(window, "_project");
        if (project is null) return;
        HumanAiPromptService.NormalizeProject(project);

        if (window is AiProductionWindow && GetPrivate<TextBox>(window, "_prompt") is TextBox prompt)
        {
            var job = SelectedJob(window);
            if (job is not null && !string.Equals(prompt.Text, job.Prompt, StringComparison.Ordinal)) prompt.Text = job.Prompt;
        }
        else if (window is SimpleAiCreationWindow &&
                 GetPrivate<AiProductionJob>(window, "_job") is AiProductionJob job &&
                 GetPrivate<TextBox>(window, "_instructions") is TextBox instructions &&
                 !string.Equals(instructions.Text, job.Prompt, StringComparison.Ordinal))
        {
            instructions.Text = job.Prompt;
        }
    }

    private static AiProductionJob? SelectedJob(Window window)
    {
        var jobs = GetPrivate<ListBox>(window, "_jobs");
        var ordered = GetPrivate<List<AiProductionJob>>(window, "_orderedJobs");
        if (jobs is null || ordered is null || jobs.SelectedIndex < 0 || jobs.SelectedIndex >= ordered.Count) return null;
        return ordered[jobs.SelectedIndex];
    }

    private static TextBox NewInstructionBox(string watermark, double height) => new()
    {
        AcceptsReturn = true,
        Height = height,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Watermark = watermark
    };

    private static T? GetPrivate<T>(object instance, string fieldName) where T : class =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;

    private static bool TryGetMainProject(MainWindow window, out PreviewProject project)
    {
        project = typeof(MainWindow).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as PreviewProject ?? null!;
        return project is not null;
    }

    private static Panel? FindPanelContaining(Control root, Control target)
    {
        foreach (var control in Descendants(root))
            if (control is Panel panel && panel.Children.Contains(target)) return panel;
        return null;
    }

    private static Control? FindPanelOrGridContaining(Control root, Control target)
    {
        foreach (var control in Descendants(root))
            if (control is Panel panel && panel.Children.Contains(target)) return panel;
        return null;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        if (root is Border border && border.Child is Control borderChild)
            foreach (var child in Descendants(borderChild)) yield return child;
        if (root is ScrollViewer scroll && scroll.Content is Control scrollChild)
            foreach (var child in Descendants(scrollChild)) yield return child;
        if (root is ContentControl content && content.Content is Control contentChild)
            foreach (var child in Descendants(contentChild)) yield return child;
    }
}
