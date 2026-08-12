using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class SingleWindowSubjectStyleUi
{
    private const string SubjectBarName = "DiezMultiSubjectBar";
    private const string ConsistencyScopeName = "DiezSubjectConsistencyScope";
    private const string CustomSaveName = "ColoringSaveCustomStyle";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];

    private static readonly (string Key, string Label)[] SubjectCriteria =
    [
        ("outfit", "Outfit / accessori"),
        ("expression", "Espressione"),
        ("action", "Posa / azione"),
        ("framing", "Inquadratura / punto di vista"),
        ("co_scene", "Scene con altri soggetti/personaggi")
    ];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is not null)
        {
            pageHost.PropertyChanged += (_, e) =>
            {
                if (e.Property != ContentControl.ContentProperty) return;
                Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Loaded);
                Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Background);
            };
        }
        window.Closed += (_, _) => Attached.Remove(window);
        Refresh(window);
    }

    public static void Refresh(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == "DiezNativeV11QuantityPage");
        if (root is null) return;

        var subject = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "VisualSubjectInstructions");
        if (subject is not null) EnsureSubjectUi(project, path, root, page, subject);
        EnsureSubjectConsistencyUi(project, path, page);
        EnsureCustomStyleUi(project, path, page);
    }

    private static void EnsureSubjectUi(PreviewProject project, string path, StackPanel root, Control page, TextBox subject)
    {
        var model = MultiSubjectProfileService.Load(project);
        var existing = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == SubjectBarName);
        if (existing is not null)
        {
            RefreshSubjectState(project, subject, existing, model, false);
            return;
        }

        var enabled = new CheckBox { Name = "MultiSubjectEnabled", Content = "Multi-soggetto/personaggio", IsChecked = model.Enabled };
        var count = new NumericUpDown
        {
            Name = "MultiSubjectCount",
            Value = model.RequestedCount,
            Minimum = 1,
            Maximum = MultiSubjectProfileService.MaxSubjects,
            Increment = 1,
            FormatString = "0",
            Width = 82,
            MinHeight = 34
        };
        var selector = new ComboBox { Name = "MultiSubjectSelector", Width = 190, MinHeight = 34 };
        var name = new TextBox
        {
            Name = "MultiSubjectName",
            Width = 180,
            MinHeight = 34,
            Watermark = "Nome soggetto/personaggio",
            IsUndoEnabled = true
        };
        var add = new Button { Name = "MultiSubjectAdd", Content = "+", Width = 38, MinHeight = 34 };
        var remove = new Button { Name = "MultiSubjectRemove", Content = "−", Width = 38, MinHeight = 34 };
        var status = new TextBlock { Name = "MultiSubjectStatus", TextWrapping = TextWrapping.Wrap, FontSize = 12 };

        var bar = new StackPanel
        {
            Name = SubjectBarName,
            Spacing = 4,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        enabled,
                        new TextBlock { Text = "N°", VerticalAlignment = VerticalAlignment.Center }, count,
                        selector, name, add, remove
                    }
                },
                status
            }
        };

        var subjectContainer = DirectChildContaining(root, subject);
        var insert = subjectContainer is null ? 2 : root.Children.IndexOf(subjectContainer);
        root.Children.Insert(Math.Clamp(insert, 0, root.Children.Count), bar);

        var changing = false;
        async Task PersistAsync()
        {
            MultiSubjectProfileService.Save(project, model);
            await SafeProjectAutosave.SaveAsync(path, project, "multi-subject-profile");
        }

        void RefreshLocal(bool setText = true)
        {
            changing = true;
            try
            {
                RefreshSubjectState(project, subject, bar, model, setText);
            }
            finally { changing = false; }
        }

        enabled.IsCheckedChanged += async (_, _) =>
        {
            if (changing) return;
            if (enabled.IsChecked == true)
            {
                model.GroupDescription = subject.Text ?? model.GroupDescription;
                model.Enabled = true;
                MultiSubjectProfileService.SetCount(model, (int)(count.Value ?? 1));
            }
            else
            {
                var active = MultiSubjectProfileService.ActiveSubject(model);
                if (active is not null) active.Description = subject.Text ?? active.Description;
                model.Enabled = false;
                WriteGroupDescription(project, model.GroupDescription);
            }
            await PersistAsync();
            RefreshLocal();
        };

        count.ValueChanged += async (_, _) =>
        {
            if (changing || enabled.IsChecked != true) return;
            MultiSubjectProfileService.SetCount(model, (int)(count.Value ?? 1));
            await PersistAsync();
            RefreshLocal();
        };

        selector.SelectionChanged += async (_, _) =>
        {
            if (changing || selector.SelectedItem is not SubjectChoice choice) return;
            var current = MultiSubjectProfileService.ActiveSubject(model);
            if (current is not null) current.Description = subject.Text ?? current.Description;
            model.ActiveSubjectId = choice.Id;
            await PersistAsync();
            RefreshLocal();
        };

        name.LostFocus += async (_, _) =>
        {
            if (changing || !model.Enabled) return;
            var current = MultiSubjectProfileService.ActiveSubject(model);
            if (current is null) return;
            if (!MultiSubjectProfileService.TryRename(model, current, name.Text, out var error))
            {
                status.Text = error;
                changing = true;
                name.Text = current.Name;
                changing = false;
                return;
            }
            status.Text = string.Empty;
            await PersistAsync();
            RefreshLocal(false);
        };

        add.Click += async (_, _) =>
        {
            if (!model.Enabled) return;
            MultiSubjectProfileService.Add(model);
            await PersistAsync();
            RefreshLocal();
        };
        remove.Click += async (_, _) =>
        {
            if (!model.Enabled) return;
            MultiSubjectProfileService.RemoveFromActiveCast(model, model.ActiveSubjectId);
            await PersistAsync();
            RefreshLocal();
        };

        subject.TextChanged += async (_, _) =>
        {
            if (changing) return;
            if (model.Enabled)
            {
                var current = MultiSubjectProfileService.ActiveSubject(model);
                if (current is not null) current.Description = subject.Text ?? string.Empty;
            }
            else
            {
                model.GroupDescription = subject.Text ?? string.Empty;
                WriteGroupDescription(project, model.GroupDescription);
            }
            await PersistAsync();
        };

        RefreshLocal();
    }

    private static void RefreshSubjectState(PreviewProject project, TextBox subject, StackPanel bar, MultiSubjectProfile model, bool setText)
    {
        var enabled = Descendants(bar).OfType<CheckBox>().First(x => x.Name == "MultiSubjectEnabled");
        var count = Descendants(bar).OfType<NumericUpDown>().First(x => x.Name == "MultiSubjectCount");
        var selector = Descendants(bar).OfType<ComboBox>().First(x => x.Name == "MultiSubjectSelector");
        var name = Descendants(bar).OfType<TextBox>().First(x => x.Name == "MultiSubjectName");
        var add = Descendants(bar).OfType<Button>().First(x => x.Name == "MultiSubjectAdd");
        var remove = Descendants(bar).OfType<Button>().First(x => x.Name == "MultiSubjectRemove");
        var status = Descendants(bar).OfType<TextBlock>().First(x => x.Name == "MultiSubjectStatus");

        enabled.IsChecked = model.Enabled;
        count.Value = model.RequestedCount;
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        var choices = active.Select(x => new SubjectChoice(x.SubjectId, x.Name, !string.IsNullOrWhiteSpace(x.Description))).ToArray();
        selector.ItemsSource = choices;
        var current = MultiSubjectProfileService.ActiveSubject(model);
        selector.SelectedItem = current is null ? null : choices.FirstOrDefault(x => string.Equals(x.Id, current.SubjectId, StringComparison.OrdinalIgnoreCase));
        name.Text = current?.Name ?? string.Empty;

        count.IsVisible = selector.IsVisible = name.IsVisible = add.IsVisible = remove.IsVisible = model.Enabled;
        count.IsEnabled = selector.IsEnabled = name.IsEnabled = add.IsEnabled = model.Enabled;
        remove.IsEnabled = model.Enabled && active.Count > 1;
        status.Text = model.Enabled
            ? $"{active.Count}/{MultiSubjectProfileService.MaxSubjects} soggetti attivi. Nome e descrizione restano legati a un SubjectId stabile."
            : "Facoltativo. Se resta OFF, usa il campo sotto solo per temi/gruppi (es. animali, fiori, piante, veicoli).";

        var label = FindLabelFor(subject);
        if (model.Enabled && current is not null)
        {
            if (label is not null) label.Text = "Descrizione — " + current.Name;
            subject.Watermark = "Descrivi solo questo soggetto/personaggio: aspetto, segni distintivi, età/proporzioni, caratteristiche da mantenere. Facoltativo.";
            if (setText && !string.Equals(subject.Text, current.Description, StringComparison.Ordinal)) subject.Text = current.Description;
        }
        else
        {
            if (label is not null) label.Text = "Tema / gruppo di soggetti";
            subject.Watermark = "Es. animali della giungla, fiori tropicali, piante grasse, dinosauri, veicoli. Usa gruppi/temi, non una lista di personaggi singoli.";
            var group = string.IsNullOrWhiteSpace(model.GroupDescription) ? ReadGroupDescription(project) : model.GroupDescription;
            if (setText && !string.Equals(subject.Text, group, StringComparison.Ordinal)) subject.Text = group;
        }
    }

    private static void EnsureSubjectConsistencyUi(PreviewProject project, string path, Control page)
    {
        var panel = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == "DiezConsistencyCriteriaPanel");
        if (panel is null) return;
        var model = MultiSubjectProfileService.Load(project);
        var existing = Descendants(panel).OfType<StackPanel>().FirstOrDefault(x => x.Name == ConsistencyScopeName);
        if (existing is null)
        {
            existing = BuildSubjectConsistencyScope(project, path, model);
            panel.Children.Insert(Math.Min(2, panel.Children.Count), existing);
        }
        existing.IsVisible = model.Enabled;

        var characterLevel = Descendants(panel).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ConsistencyLevel_character");
        if (characterLevel is not null)
        {
            var row = ParentStack(characterLevel, panel);
            if (row is not null) row.IsVisible = !model.Enabled;
        }

        if (model.Enabled) RefreshSubjectConsistencyScope(project, path, existing, model);
    }

    private static StackPanel BuildSubjectConsistencyScope(PreviewProject project, string path, MultiSubjectProfile model)
    {
        var selector = new ComboBox { Name = "ConsistencySubjectSelector", Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var body = new StackPanel { Name = "ConsistencySubjectBody", Spacing = 7 };
        var scope = new StackPanel
        {
            Name = ConsistencyScopeName,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Consistent del soggetto/personaggio", FontSize = 15 },
                new TextBlock { Text = "L'identità del soggetto è stabile. Qui decidi cosa può restare coerente o variare nelle sue diverse apparizioni.", TextWrapping = TextWrapping.Wrap },
                selector,
                body,
                new Separator()
            }
        };
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem is not SubjectChoice choice) return;
            var current = MultiSubjectProfileService.Load(project);
            current.ActiveSubjectId = choice.Id;
            MultiSubjectProfileService.Save(project, current);
            await SafeProjectAutosave.SaveAsync(path, project, "subject-consistency-select");
            RefreshSubjectConsistencyScope(project, path, scope, current);
        };
        return scope;
    }

    private static void RefreshSubjectConsistencyScope(PreviewProject project, string path, StackPanel scope, MultiSubjectProfile model)
    {
        var selector = Descendants(scope).OfType<ComboBox>().First(x => x.Name == "ConsistencySubjectSelector");
        var body = Descendants(scope).OfType<StackPanel>().First(x => x.Name == "ConsistencySubjectBody");
        var active = MultiSubjectProfileService.ActiveSubjects(model);
        var current = MultiSubjectProfileService.ActiveSubject(model);
        var choices = active.Select(x => new SubjectChoice(x.SubjectId, x.Name, !string.IsNullOrWhiteSpace(x.Description))).ToArray();
        selector.ItemsSource = choices;
        selector.SelectedItem = current is null ? null : choices.FirstOrDefault(x => string.Equals(x.Id, current.SubjectId, StringComparison.OrdinalIgnoreCase));
        body.Children.Clear();
        if (current is null) return;
        MultiSubjectProfileService.EnsureConsistencyDefaults(current);

        body.Children.Add(new TextBlock
        {
            Text = $"Identità / aspetto fisico — Da mantenere (HARD) · {current.Name}",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var (key, label) in SubjectCriteria)
            body.Children.Add(BuildConsistencyRuleEditor(project, path, model, current, key, label));
    }

    private static Control BuildConsistencyRuleEditor(
        PreviewProject project, string path, MultiSubjectProfile model, MultiSubjectDefinition subject, string key, string label)
    {
        var rule = subject.Consistency[key];
        var levels = new[] { new Choice("LOCKED", "Da mantenere"), new Choice("PREFERRED", "Preferibilmente coerente"), new Choice("FREE", "Può variare") };
        var strategies = new[] { new Choice("USER", "La definisco io"), new Choice("AI", "La decide l’AI"), new Choice("MIXED", "Mista: do indicazioni e l’AI completa") };
        var level = new ComboBox { Name = "SubjectConsistencyLevel_" + key, ItemsSource = levels, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var strategy = new ComboBox { Name = "SubjectConsistencyStrategy_" + key, ItemsSource = strategies, Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        var variation = new TextBox
        {
            Name = "SubjectConsistencyVariation_" + key,
            Text = rule.Variation,
            MinHeight = 62,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Indica cosa può cambiare e gli eventuali limiti.",
            IsUndoEnabled = true
        };
        level.SelectedItem = levels.First(x => x.Value == rule.Level);
        strategy.SelectedItem = strategies.First(x => x.Value == rule.Strategy);

        void Visibility()
        {
            var free = rule.Level == "FREE";
            strategy.IsVisible = free;
            variation.IsVisible = free;
        }
        async Task SaveAsync()
        {
            MultiSubjectProfileService.Save(project, model);
            await SafeProjectAutosave.SaveAsync(path, project, "subject-consistency");
        }
        level.SelectionChanged += async (_, _) =>
        {
            if (level.SelectedItem is Choice selected) rule.Level = selected.Value;
            Visibility();
            await SaveAsync();
        };
        strategy.SelectionChanged += async (_, _) =>
        {
            if (strategy.SelectedItem is Choice selected) rule.Strategy = selected.Value;
            await SaveAsync();
        };
        variation.TextChanged += async (_, _) =>
        {
            rule.Variation = variation.Text ?? string.Empty;
            await SaveAsync();
        };
        Visibility();
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontSize = 13 },
                level,
                strategy,
                variation
            }
        };
    }

    private static void EnsureCustomStyleUi(PreviewProject project, string path, Control page)
    {
        if (!string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase)) return;
        var style = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ColoringStyle");
        var custom = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "ColoringCustomStyleNotes");
        if (style is null || custom is null) return;

        var values = ColoringIndependentHardProfileService.SelectableStyles.ToArray();
        style.ItemsSource = values;
        var profile = BookTypePromptProfileService.LoadColoring(project);
        if (style.SelectedItem is null || !values.Contains(style.SelectedItem.ToString(), StringComparer.OrdinalIgnoreCase))
            style.SelectedItem = values.FirstOrDefault(x => string.Equals(x, profile.Style, StringComparison.OrdinalIgnoreCase)) ?? "Clean Line Art";

        var container = DirectParentStack(custom);
        if (container is null) return;
        var label = container.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is not null) label.Text = "Stile Custom — descrizione HARD";
        custom.Watermark = "Descrivi lo stile visivo esatto da rispettare. Questo testo diventa STYLE — HARD LOCK.";

        var save = Descendants(container).OfType<CheckBox>().FirstOrDefault(x => x.Name == CustomSaveName);
        if (save is null)
        {
            save = new CheckBox { Name = CustomSaveName, Content = "Salva questo stile tra i miei stili personalizzati" };
            container.Children.Add(save);
        }

        void RefreshVisibility()
        {
            var selected = style.SelectedItem?.ToString() ?? string.Empty;
            var library = CustomStyleLibraryService.TryResolve(selected, out var definition);
            var isCustom = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase) || library;
            container.IsVisible = isCustom;
            save!.IsVisible = string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase);
            if (library && !string.Equals(custom.Text, definition, StringComparison.Ordinal)) custom.Text = definition;
        }

        if (Wired.Add(style))
            style.SelectionChanged += async (_, _) =>
            {
                var p = BookTypePromptProfileService.LoadColoring(project);
                var selected = style.SelectedItem?.ToString() ?? "Clean Line Art";
                if (CustomStyleLibraryService.TryResolve(selected, out var definition))
                {
                    p.Style = "Custom";
                    p.CustomStyleNotes = definition;
                    custom.Text = definition;
                }
                else p.Style = selected;
                BookTypePromptProfileService.SaveColoring(project, p);
                await SafeProjectAutosave.SaveAsync(path, project, "coloring-style-authority");
                RefreshVisibility();
            };

        if (Wired.Add(custom))
            custom.TextChanged += async (_, _) =>
            {
                var selected = style.SelectedItem?.ToString() ?? string.Empty;
                if (!string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase) &&
                    !CustomStyleLibraryService.TryResolve(selected, out _)) return;
                var p = BookTypePromptProfileService.LoadColoring(project);
                p.Style = "Custom";
                p.CustomStyleNotes = custom.Text ?? string.Empty;
                BookTypePromptProfileService.SaveColoring(project, p);
                if (save?.IsChecked == true && !string.IsNullOrWhiteSpace(custom.Text))
                    CustomStyleLibraryService.Add(custom.Text);
                await SafeProjectAutosave.SaveAsync(path, project, "coloring-custom-style");
            };

        if (Wired.Add(save))
            save.IsCheckedChanged += (_, _) =>
            {
                if (save.IsChecked == true && !string.IsNullOrWhiteSpace(custom.Text))
                    CustomStyleLibraryService.Add(custom.Text);
            };

        RefreshVisibility();
    }

    private static string ReadGroupDescription(PreviewProject project)
    {
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return BookTypePromptProfileService.LoadColoring(project).SubjectDescription;
        return ImageCollectionPromptProfileService.Load(project).SubjectDescription;
    }

    private static void WriteGroupDescription(PreviewProject project, string text)
    {
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var p = BookTypePromptProfileService.LoadColoring(project);
            p.SubjectDescription = text ?? string.Empty;
            BookTypePromptProfileService.SaveColoring(project, p);
        }
        else
        {
            var p = ImageCollectionPromptProfileService.Load(project);
            p.SubjectDescription = text ?? string.Empty;
            ImageCollectionPromptProfileService.Save(project, p);
        }
    }

    private static TextBlock? FindLabelFor(Control control)
    {
        var parent = DirectParentStack(control);
        return parent?.Children.OfType<TextBlock>().FirstOrDefault();
    }

    private static StackPanel? DirectParentStack(Control control) => control.Parent as StackPanel;

    private static StackPanel? ParentStack(Control control, StackPanel boundary)
    {
        Control? current = control;
        while (current?.Parent is Control parent && !ReferenceEquals(parent, boundary))
        {
            if (parent is StackPanel stack && ReferenceEquals(stack.Parent, boundary)) return stack;
            current = parent;
        }
        return null;
    }

    private static Control? DirectChildContaining(StackPanel root, Control descendant)
    {
        foreach (var child in root.Children.OfType<Control>())
            if (ReferenceEquals(child, descendant) || Descendants(child).Any(x => ReferenceEquals(x, descendant))) return child;
        return null;
    }

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

    private sealed record SubjectChoice(string Id, string Name, bool HasDescription)
    {
        public override string ToString() => HasDescription ? Name + " ✓" : Name;
    }

    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
