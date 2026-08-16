using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility facade for the legacy Avalonia frontend. All editorial values and
/// per-book storage rules live in Diez.Core; only control construction remains here.
/// </summary>
internal static class BookTypeAiOptionsService
{
    public static IReadOnlyList<BookTypeAiOptionDefinition> Definitions(PreviewProject project) =>
        BookTypeAiOptionsCoreService.Definitions(project);

    public static string Get(PreviewProject project, BookTypeAiOptionDefinition definition) =>
        BookTypeAiOptionsCoreService.Get(project, definition);

    public static void Set(PreviewProject project, BookTypeAiOptionDefinition definition, string? value) =>
        BookTypeAiOptionsCoreService.Set(project, definition, value);

    public static IReadOnlyList<string> PromptLines(PreviewProject project) =>
        BookTypeAiOptionsCoreService.PromptLines(project);

    public static Control BuildEditor(PreviewProject project, Action? changed = null)
    {
        var outer = new StackPanel { Spacing = 8 };
        var type = BookTypeProfileService.Get(project);

        if (BookTypeAiOptionsCoreService.UsesStructureQuestion(type))
        {
            outer.Children.Add(new TextBlock
            {
                Text = "Conosci già la struttura e il numero di pagine?",
                FontSize = 17,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            var yes = new RadioButton
            {
                Content = "Sì",
                GroupName = "diez-structure-choice",
                IsChecked = BookTypeAiOptionsCoreService.StructureIsKnown(project)
            };
            var no = new RadioButton
            {
                Content = "No, definiscili in base al progetto",
                GroupName = "diez-structure-choice",
                IsChecked = !BookTypeAiOptionsCoreService.StructureIsKnown(project)
            };
            var choices = new StackPanel { Spacing = 6 };

            void RefreshChoice()
            {
                choices.IsVisible = BookTypeAiOptionsCoreService.StructureIsKnown(project);
                changed?.Invoke();
            }

            yes.IsCheckedChanged += (_, _) =>
            {
                if (yes.IsChecked != true) return;
                BookTypeAiOptionsCoreService.SetStructureDecision(project, true);
                RefreshChoice();
            };
            no.IsCheckedChanged += (_, _) =>
            {
                if (no.IsChecked != true) return;
                BookTypeAiOptionsCoreService.SetStructureDecision(project, false);
                RefreshChoice();
            };

            outer.Children.Add(yes);
            outer.Children.Add(no);
            outer.Children.Add(new TextBlock
            {
                Text = "Se scegli No, Diez parte dai materiali del progetto, propone la struttura e ti mostra i numeri risultanti prima che tu li approvi.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12
            });

            BuildOptionsPanel(project, choices, type, changed);
            choices.IsVisible = BookTypeAiOptionsCoreService.StructureIsKnown(project);
            outer.Children.Add(choices);
        }
        else
        {
            BuildOptionsPanel(project, outer, type, changed);
        }

        return new Border
        {
            Padding = new Thickness(10),
            Child = outer
        };
    }

    private static void BuildOptionsPanel(PreviewProject project, StackPanel panel, string type, Action? changed)
    {
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(type) ? "Scelte del contenuto" : $"Scelte per {type}",
            FontSize = 17
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Usa questi controlli per le cose ripetitive. I due box servono solo per le indicazioni che non entrano bene qui.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });

        foreach (var definition in Definitions(project))
        {
            Control input;
            switch (definition.Kind)
            {
                case BookTypeAiOptionKind.Toggle:
                {
                    var check = new CheckBox
                    {
                        Content = definition.Label,
                        IsChecked = string.Equals(Get(project, definition), "true", StringComparison.OrdinalIgnoreCase)
                    };
                    check.IsCheckedChanged += (_, _) =>
                    {
                        Set(project, definition, check.IsChecked == true ? "true" : "false");
                        changed?.Invoke();
                    };
                    input = check;
                    break;
                }
                case BookTypeAiOptionKind.Choice:
                {
                    var combo = new ComboBox
                    {
                        ItemsSource = definition.Choices,
                        SelectedItem = Get(project, definition),
                        MinWidth = 190
                    };
                    if (combo.SelectedIndex < 0 && definition.Choices is { Count: > 0 }) combo.SelectedIndex = 0;
                    combo.SelectionChanged += (_, _) =>
                    {
                        Set(project, definition, combo.SelectedItem?.ToString());
                        changed?.Invoke();
                    };
                    input = Field(definition.Label, combo);
                    break;
                }
                default:
                {
                    var text = new TextBox
                    {
                        Text = Get(project, definition),
                        MinWidth = 190,
                        Watermark = definition.Kind == BookTypeAiOptionKind.Number ? "Numero" : "Facoltativo"
                    };
                    text.TextChanged += (_, _) =>
                    {
                        Set(project, definition, text.Text);
                        changed?.Invoke();
                    };
                    input = Field(definition.Label, text);
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(definition.Help)) ToolTip.SetTip(input, definition.Help);
            panel.Children.Add(input);
        }
    }

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label }, control }
    };
}

internal static class BookTypeAiOptionsUi
{
    private static readonly HashSet<Window> Attached = [];

    public static void Attach(MainWindow mainWindow)
    {
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) return;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            foreach (var window in desktop.Windows.ToList())
            {
                if (window is not (AiJobEditorWindow or SimpleAiCreationWindow)) continue;
                if (Attached.Contains(window)) continue;
                if (!TryAttach(window)) continue;
                Attached.Add(window);
                window.Closed += (_, _) => Attached.Remove(window);
            }
        };
        mainWindow.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static bool TryAttach(Window window)
    {
        var project = window.GetType().GetField("_project", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) as PreviewProject;
        if (project is null) return false;

        var mustNotDoLabel = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => string.Equals(t.Text, "NON DEVE FARE", StringComparison.Ordinal));
        if (mustNotDoLabel is null) return false;

        var root = FindMainStack(window);
        if (root is null) return false;
        var labelIndex = root.Children.IndexOf(mustNotDoLabel);
        if (labelIndex >= 0)
        {
            var insertAt = Math.Min(root.Children.Count, labelIndex + 2);
            root.Children.Insert(insertAt, BookTypeAiOptionsService.BuildEditor(project));
            return true;
        }

        var request = window.GetType().GetField("_request", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) as TextBox;
        if (request is null) return false;
        var requestField = Descendants(window).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(request));
        if (requestField is null) return false;
        var parent = Descendants(window).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(requestField));
        if (parent is null) return false;
        var index = parent.Children.IndexOf(requestField);
        parent.Children.Insert(index + 1, BookTypeAiOptionsService.BuildEditor(project));
        return true;
    }

    private static StackPanel? FindMainStack(Window window)
    {
        if (window.Content is Border border)
        {
            if (border.Child is StackPanel stack) return stack;
            if (border.Child is ScrollViewer scroll && scroll.Content is StackPanel scrollStack) return scrollStack;
        }
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
