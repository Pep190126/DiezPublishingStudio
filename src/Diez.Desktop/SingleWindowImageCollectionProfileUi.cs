using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class SingleWindowImageCollectionProfileUi
{
    private const string PanelName = "DiezImageCollectionProfilePanel";

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page || !TrySession(window, out var project, out var path)) return;
        if (!string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ImageCollection, StringComparison.OrdinalIgnoreCase)) return;

        var texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        if (texts.Any(t => t.Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            EnsureQuantityProfile(page, project, path);
        else if (texts.Any(t => string.Equals(t, "PROMPT — modificabile", StringComparison.Ordinal)))
            EnsurePromptProfile(page, project);
    }

    private static void EnsureQuantityProfile(Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("quantità", StringComparison.OrdinalIgnoreCase)));
        if (root is null) return;

        var profile = ImageCollectionPromptProfileService.Load(project);
        var subject = Editor("ImageCollectionSubject", profile.SubjectDescription, 100,
            "Descrivi soggetto/i, azione, caratteristiche, elementi obbligatori e variazioni consentite nella serie.");
        var environment = Editor("ImageCollectionEnvironment", profile.EnvironmentDescription, 100,
            "Descrivi ambiente/scenario, sfondo, oggetti, contesto e relazione col soggetto.");
        var use = Combo("ImageCollectionEditorialUse", ImageCollectionPromptProfileService.EditorialUses, profile.EditorialUse, 320);
        var color = Combo("ImageCollectionColorMode", ImageCollectionPromptProfileService.ColorModes, profile.ColorMode, 320);
        var detail = Combo("ImageCollectionDetailLevel", ImageCollectionPromptProfileService.DetailLevels, profile.DetailLevel, 190);
        var line = Combo("ImageCollectionLineTreatment", ImageCollectionPromptProfileService.LineTreatments, profile.LineTreatment, 290);
        var style = Combo("ImageCollectionRenderingStyle", ImageCollectionPromptProfileService.RenderingStyles, profile.RenderingStyle, 260);
        var background = Combo("ImageCollectionBackground", ImageCollectionPromptProfileService.Backgrounds, profile.Background, 270);
        var viewpoint = Combo("ImageCollectionViewpoint", ImageCollectionPromptProfileService.Viewpoints, profile.Viewpoint, 300);
        var readable = Check("Soggetto sempre chiaramente leggibile", profile.KeepSubjectReadable);
        var noText = Check("Evita testo/etichette dentro l'immagine salvo richiesta", profile.AvoidTextInsideImage);
        var clarity = Check("Priorità alla chiarezza editoriale", profile.EditorialClarity);
        var sameScale = Check("Mantieni scala/inquadratura comparabili nelle serie", profile.SameScaleWhenSeries);
        var notes = Editor("ImageCollectionNotes", profile.Notes, 72, "Note aggiuntive sul tipo di illustrazione o sulla serie.");

        async Task SaveAsync()
        {
            profile.SubjectDescription = subject.Text ?? string.Empty;
            profile.EnvironmentDescription = environment.Text ?? string.Empty;
            profile.EditorialUse = use.SelectedItem?.ToString() ?? profile.EditorialUse;
            profile.ColorMode = color.SelectedItem?.ToString() ?? profile.ColorMode;
            profile.DetailLevel = detail.SelectedItem?.ToString() ?? profile.DetailLevel;
            profile.LineTreatment = line.SelectedItem?.ToString() ?? profile.LineTreatment;
            profile.RenderingStyle = style.SelectedItem?.ToString() ?? profile.RenderingStyle;
            profile.Background = background.SelectedItem?.ToString() ?? profile.Background;
            profile.Viewpoint = viewpoint.SelectedItem?.ToString() ?? profile.Viewpoint;
            profile.KeepSubjectReadable = readable.IsChecked == true;
            profile.AvoidTextInsideImage = noText.IsChecked == true;
            profile.EditorialClarity = clarity.IsChecked == true;
            profile.SameScaleWhenSeries = sameScale.IsChecked == true;
            profile.Notes = notes.Text ?? string.Empty;
            ImageCollectionPromptProfileService.Save(project, profile);
            await ProjectFileStore.SaveAsync(path, project);
        }

        foreach (var box in new[] { subject, environment, notes }) box.TextChanged += async (_, _) => await SaveAsync();
        foreach (var combo in new[] { use, color, detail, line, style, background, viewpoint }) combo.SelectionChanged += async (_, _) => await SaveAsync();
        foreach (var check in new[] { readable, noText, clarity, sameScale }) check.IsCheckedChanged += async (_, _) => await SaveAsync();

        use.SelectionChanged += (_, _) => ApplyUseDefaults(use.SelectedItem?.ToString() ?? profile.EditorialUse, color, detail, line, style, background, viewpoint, sameScale);

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Profilo della Raccolta immagini", FontSize = 19 },
                new TextBlock
                {
                    Text = "La Raccolta immagini può essere a colori, in scala di grigi o in bianco/nero puro e può servire anche per figure di saggi, manuali e sequenze didattiche.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new TextBlock { Text = "Soggetto/i — scelta e descrizione", FontSize = 15 }, subject,
                new TextBlock { Text = "Ambiente / scenario — descrizione", FontSize = 15 }, environment,
                Labeled("Uso editoriale", use),
                Labeled("Resa cromatica", color),
                new TextBlock
                {
                    Text = "Scala di grigi permette sfumature dal bianco al nero; Bianco e nero puro usa invece solo #000000 e #FFFFFF.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Dettaglio", detail), Labeled("Linee / contorno", line) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Stile resa", style), Labeled("Sfondo", background) } },
                Labeled("Punto di vista", viewpoint),
                new StackPanel { Spacing = 4, Children = { readable, noText, clarity, sameScale } },
                new TextBlock { Text = "Note (facoltative)", FontSize = 13 }, notes
            }
        };

        var actions = root.Children.OfType<StackPanel>().LastOrDefault(p => p.Orientation == Orientation.Horizontal && p.Children.OfType<Button>().Any());
        var index = actions is null ? root.Children.Count : root.Children.IndexOf(actions);
        root.Children.Insert(Math.Max(0, index), panel);
        if (actions is not null)
        {
            var next = actions.Children.OfType<Button>().FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
            if (next is not null) next.Click += async (_, _) => await SaveAsync();
        }
    }

    private static void EnsurePromptProfile(Control page, PreviewProject project)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, "DiezImageCollectionPromptProfileMarker", StringComparison.Ordinal))) return;
        var editors = Descendants(page).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) return;
        var prompt = editors[2];
        var changing = false;

        void EnsureBlock()
        {
            if (changing) return;
            var text = prompt.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || text.Contains("PROFILO EDITORIALE RACCOLTA IMMAGINI:", StringComparison.Ordinal)) return;
            changing = true;
            prompt.Text = text.TrimEnd() + Environment.NewLine + Environment.NewLine + ImageCollectionPromptProfileService.BuildPromptBlock(project);
            changing = false;
        }

        prompt.TextChanged += (_, _) => EnsureBlock();
        foreach (var button in Descendants(page).OfType<Button>().Where(b =>
                     (b.Content?.ToString() ?? string.Empty).StartsWith("Prepara prompt", StringComparison.OrdinalIgnoreCase)))
            button.Click += (_, _) => EnsureBlock();

        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(prompt));
        if (root is not null)
        {
            var idx = root.Children.IndexOf(prompt);
            root.Children.Insert(Math.Min(root.Children.Count, idx + 1), new TextBlock
            {
                Name = "DiezImageCollectionPromptProfileMarker",
                Text = "Diez aggiunge uso editoriale, resa cromatica, dettaglio, contorni, soggetto e ambiente della Raccolta immagini.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    private static void ApplyUseDefaults(string use, ComboBox color, ComboBox detail, ComboBox line, ComboBox style, ComboBox background, ComboBox viewpoint, CheckBox sameScale)
    {
        if (use.Contains("esercizi", StringComparison.OrdinalIgnoreCase) || use.Contains("movimenti", StringComparison.OrdinalIgnoreCase))
        {
            color.SelectedItem = "Scala di grigi — con sfumature";
            detail.SelectedItem = "Medio";
            line.SelectedItem = "Contorno medio";
            style.SelectedItem = "Infografico / didattico";
            background.SelectedItem = "Bianco pulito";
            viewpoint.SelectedItem = "Stesso punto di vista per tutta la serie";
            sameScale.IsChecked = true;
        }
        else if (use.Contains("tecnica", StringComparison.OrdinalIgnoreCase) || use.Contains("manuale", StringComparison.OrdinalIgnoreCase))
        {
            color.SelectedItem = "Scala di grigi — con sfumature";
            detail.SelectedItem = "Alto";
            line.SelectedItem = "Contorno sottile";
            style.SelectedItem = "Tecnico pulito";
            background.SelectedItem = "Bianco pulito";
            sameScale.IsChecked = true;
        }
        else if (use.Contains("saggio", StringComparison.OrdinalIgnoreCase) || use.Contains("editoriale", StringComparison.OrdinalIgnoreCase))
        {
            color.SelectedItem = "Scala di grigi — con sfumature";
            detail.SelectedItem = "Medio";
            line.SelectedItem = "Contorno sottile";
            style.SelectedItem = "Illustrativo chiaro";
            background.SelectedItem = "Semplice / funzionale";
        }
    }

    private static TextBox Editor(string name, string value, double height, string watermark) => new()
    {
        Name = name,
        Text = value,
        Height = height,
        AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Watermark = watermark,
        IsUndoEnabled = true
    };

    private static ComboBox Combo(string name, IEnumerable<string> values, string selected, double width) => new()
    {
        Name = name,
        ItemsSource = values.ToArray(),
        SelectedItem = selected,
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };
    private static StackPanel Labeled(string label, Control control) => new() { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 13 }, control } };

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        var seen = new HashSet<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case Panel p:
                    for (var i = p.Children.Count - 1; i >= 0; i--) stack.Push(p.Children[i]);
                    break;
                case Border b when b.Child is Control child: stack.Push(child); break;
                case ScrollViewer s when s.Content is Control child: stack.Push(child); break;
                case ContentControl c when c.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
