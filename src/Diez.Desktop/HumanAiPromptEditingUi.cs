using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Human-facing editor for DEVE FARE / NON DEVE FARE / PROMPT.
/// The generated prompt is a starting point: the user may edit it directly.
/// Direct prompt edits are preserved until the user explicitly regenerates the prompt
/// from the two human instruction boxes.
/// </summary>
internal static class HumanAiPromptEditingUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.ToList())
            {
                if (window is not (AiProductionWindow or AiJobEditorWindow or SimpleAiCreationWindow)) continue;
                if (Attached.Add(window)) AttachWindow(window);
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
            case AiProductionWindow:
                AttachProductionWindow(window);
                break;
            case AiJobEditorWindow:
                AttachJobEditor(window);
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
        ConfigureEditor(request, "Descrivi in modo concreto ciò che vuoi ottenere.", 105);

        var mustNotDo = NewEditor("Scrivi cosa deve evitare. Puoi lasciare vuoto se non hai divieti particolari.", 105);
        var initial = HumanAiPromptService.Read(request.Text);
        var syncing = true;
        request.Text = initial.MustDo;
        request.Tag = initial.MustDo;
        mustNotDo.Text = initial.MustNotDo;
        syncing = false;

        field.Children.Add(new TextBlock { Text = "NON DEVE FARE", Margin = new Thickness(0, 5, 0, 0) });
        field.Children.Add(mustNotDo);
        field.Children.Add(new TextBlock
        {
            Text = "Entrambi i box supportano selezione, copia e Ctrl+Z. Diez li userà per preparare il prompt iniziale.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });

        request.TextChanged += (_, _) =>
        {
            if (!syncing) request.Tag = request.Text ?? string.Empty;
        };

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
            }, RoutingStrategies.Tunnel);
        }
    }

    private static void AttachProductionWindow(Window window)
    {
        if (GetPrivate<TextBox>(window, "_request") is not TextBox request ||
            GetPrivate<TextBox>(window, "_prompt") is not TextBox prompt) return;
        var parent = FindPanelContaining(window, request);
        if (parent is not Grid grid) return;

        var oldLabel = grid.Children.OfType<TextBlock>()
            .FirstOrDefault(t => Grid.GetRow(t) == 1 && (t.Text ?? string.Empty).Contains("Richiesta", StringComparison.OrdinalIgnoreCase));
        if (oldLabel is not null) oldLabel.IsVisible = false;

        grid.Children.Remove(request);
        ConfigureEditor(request, "Cosa deve ottenere l'AI.", 72);
        var mustNotDo = NewEditor("Cosa deve evitare. Facoltativo.", 72);
        ConfigureEditor(prompt, "Prompt pronto: puoi modificarlo liberamente prima di copiarlo.", 145);

        var editor = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = "DEVE FARE" }, request,
                new TextBlock { Text = "NON DEVE FARE" }, mustNotDo
            }
        };
        Grid.SetRow(editor, 2);
        grid.Children.Add(editor);
        if (grid.RowDefinitions.Count > 2) grid.RowDefinitions[2].Height = GridLength.Auto;

        var syncing = false;

        void LoadFromSelected()
        {
            var job = SelectedJob(window);
            if (job is null) return;
            var intent = HumanAiPromptService.Read(job.Request);
            syncing = true;
            request.Text = intent.MustDo;
            mustNotDo.Text = intent.MustNotDo;
            prompt.Text = job.Prompt;
            syncing = false;
        }

        void RegeneratePrompt()
        {
            if (syncing) return;
            var job = SelectedJob(window);
            var project = GetPrivate<PreviewProject>(window, "_project");
            if (job is null || project is null) return;
            job.Request = HumanAiPromptService.Write(request.Text, mustNotDo.Text);
            var generated = HumanAiPromptService.BuildPrompt(project, job);
            job.Prompt = generated;
            syncing = true;
            prompt.Text = generated;
            syncing = false;
        }

        request.TextChanged += (_, _) =>
        {
            if (!syncing) RegeneratePrompt();
        };
        mustNotDo.TextChanged += (_, _) =>
        {
            if (!syncing) RegeneratePrompt();
        };
        prompt.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var job = SelectedJob(window);
            if (job is not null) job.Prompt = prompt.Text ?? string.Empty;
        };

        request.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        mustNotDo.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        prompt.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        window.Closed += async (_, _) => await PersistWindowProjectAsync(window);

        if (GetPrivate<ListBox>(window, "_jobs") is ListBox jobs)
            jobs.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(LoadFromSelected);

        var rebuild = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Aggiorna istruzioni", StringComparison.OrdinalIgnoreCase) ||
                                 (b.Content?.ToString() ?? string.Empty).Contains("Ricrea prompt", StringComparison.OrdinalIgnoreCase));
        if (rebuild is not null)
        {
            rebuild.AddHandler(Button.ClickEvent, async (_, e) =>
            {
                e.Handled = true;
                RegeneratePrompt();
                await PersistWindowProjectAsync(window);
            }, RoutingStrategies.Tunnel);
        }

        Dispatcher.UIThread.Post(LoadFromSelected);
    }

    private static void AttachSimpleCreation(Window window)
    {
        if (GetPrivate<TextBox>(window, "_request") is not TextBox request ||
            GetPrivate<TextBox>(window, "_instructions") is not TextBox prompt) return;
        if (window.Content is not Border border || border.Child is not StackPanel stack) return;

        var index = stack.Children.IndexOf(request);
        if (index < 0) return;
        var previousLabel = index > 0 ? stack.Children[index - 1] as TextBlock : null;
        if (previousLabel is not null) previousLabel.Text = "DEVE FARE";

        ConfigureEditor(request, "Descrivi cosa vuoi che l'AI faccia.", 95);
        var mustNotDo = NewEditor("Descrivi cosa non deve fare. Puoi lasciare vuoto.", 95);
        ConfigureEditor(prompt, "Prompt pronto: puoi modificarlo, copiarlo e usare Ctrl+Z.", 180);

        stack.Children.Insert(index + 1, new TextBlock { Text = "NON DEVE FARE" });
        stack.Children.Insert(index + 2, mustNotDo);
        border.Child = null;
        border.Child = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        window.Height = Math.Max(window.Height, 760);

        var syncing = false;
        request.TextChanged += (_, _) =>
        {
            if (!syncing) request.Tag = request.Text ?? string.Empty;
        };
        prompt.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var job = GetPrivate<AiProductionJob>(window, "_job");
            if (job is not null) job.Prompt = prompt.Text ?? string.Empty;
        };
        prompt.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        request.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        mustNotDo.LostFocus += async (_, _) => await PersistWindowProjectAsync(window);
        window.Closed += async (_, _) => await PersistWindowProjectAsync(window);

        var prepare = stack.Children.OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Prepara le istruzioni", StringComparison.Ordinal));
        if (prepare is not null)
        {
            prepare.AddHandler(Button.ClickEvent, (_, _) =>
            {
                var plainMustDo = request.Tag?.ToString() ?? request.Text ?? string.Empty;
                syncing = true;
                request.Text = HumanAiPromptService.Write(plainMustDo, mustNotDo.Text);
                syncing = false;
            }, RoutingStrategies.Tunnel);

            prepare.Click += (_, _) => Dispatcher.UIThread.Post(async () =>
            {
                var project = GetPrivate<PreviewProject>(window, "_project");
                var job = GetPrivate<AiProductionJob>(window, "_job");
                if (project is null || job is null) return;
                var generated = HumanAiPromptService.BuildPrompt(project, job);
                job.Prompt = generated;
                var intent = HumanAiPromptService.Read(job.Request);
                syncing = true;
                prompt.Text = generated;
                request.Text = intent.MustDo;
                request.Tag = intent.MustDo;
                mustNotDo.Text = intent.MustNotDo;
                syncing = false;
                await PersistWindowProjectAsync(window);
            });
        }
    }

    private static void ConfigureEditor(TextBox box, string watermark, double height)
    {
        HumanAiPromptInputGuard.MakeEditable(box);
        box.IsUndoEnabled = true;
        box.AcceptsReturn = true;
        box.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        box.Height = height;
        box.Watermark = watermark;
    }

    private static TextBox NewEditor(string watermark, double height)
    {
        var box = new TextBox();
        ConfigureEditor(box, watermark, height);
        return box;
    }

    private static async Task PersistWindowProjectAsync(Window window)
    {
        var project = GetPrivate<PreviewProject>(window, "_project");
        var path = GetPrivateString(window, "_projectPath");
        if (project is null || string.IsNullOrWhiteSpace(path)) return;
        try { await ProjectFileStore.SaveAsync(path, project); } catch { }
    }

    private static AiProductionJob? SelectedJob(Window window)
    {
        var jobs = GetPrivate<ListBox>(window, "_jobs");
        var ordered = GetPrivate<List<AiProductionJob>>(window, "_orderedJobs");
        if (jobs is null || ordered is null || jobs.SelectedIndex < 0 || jobs.SelectedIndex >= ordered.Count) return null;
        return ordered[jobs.SelectedIndex];
    }

    private static Panel? FindPanelContaining(Control root, Control target)
    {
        foreach (var control in Descendants(root))
            if (control is Panel panel && panel.Children.Contains(target)) return panel;
        return null;
    }

    private static T? GetPrivate<T>(object instance, string fieldName) where T : class =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T;

    private static string GetPrivateString(object instance, string fieldName) =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as string ?? string.Empty;

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
