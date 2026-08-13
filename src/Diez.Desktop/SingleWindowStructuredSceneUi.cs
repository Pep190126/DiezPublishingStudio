using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Structured scenes reuse the native Environment editor instead of adding a second description box.
/// Users switch between a generic environment and per-scene definition. Scene/subject relationships remain
/// identity-based through stable SceneId + SubjectId values; names are display-only and may change safely.
/// </summary>
internal static class SingleWindowStructuredSceneUi
{
    private const string ModePanelName = "DiezEnvironmentSceneModePanel";
    private const string SceneToolbarName = "DiezStructuredSceneToolbar";
    private const string MembershipPanelName = "DiezSubjectSceneMembership";
    private const string GenericMode = "Ambientazione generica";
    private const string SceneMode = "Definisci scene";

    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];
    private static readonly HashSet<Control> Guards = [];

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
                Dispatcher.UIThread.Post(() => SafeRefresh(window), DispatcherPriority.Loaded);
            };
        }
        window.Closed += (_, _) => Attached.Remove(window);
        SafeRefresh(window);
    }

    public static void Refresh(MainWindow window) => SafeRefresh(window);

    private static void SafeRefresh(MainWindow window)
    {
        try
        {
            if (!TrySession(window, out var project, out var path)) return;
            var host = SingleWindowEntryPointUi.GetHost(window);
            var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
            if (pageHost?.Content is not Control page) return;

            var quantityRoot = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == "DiezNativeV11QuantityPage");
            if (quantityRoot is not null) EnsureEnvironmentSceneSwitch(project, path, page, quantityRoot);
            EnsureConsistentMembership(project, path, page, window);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.Error("structured-scene-ui-refresh", ex);
        }
    }

    private static void EnsureEnvironmentSceneSwitch(PreviewProject project, string path, Control page, StackPanel root)
    {
        var environment = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "VisualEnvironmentInstructions");
        if (environment is null) return;
        var environmentContainer = DirectChildContaining(root, environment) as StackPanel;
        if (environmentContainer is null) return;

        var panel = Descendants(environmentContainer).OfType<StackPanel>().FirstOrDefault(x => x.Name == ModePanelName);
        if (panel is null)
        {
            panel = BuildModePanel(project, path, environment, environmentContainer);
            environmentContainer.Children.Insert(0, panel);
        }

        RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
    }

    private static StackPanel BuildModePanel(PreviewProject project, string path, TextBox environment, StackPanel environmentContainer)
    {
        var mode = new ComboBox
        {
            Name = "EnvironmentSceneMode",
            ItemsSource = new[] { GenericMode, SceneMode },
            Width = 230,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var selector = new ComboBox
        {
            Name = "StructuredSceneSelector",
            Width = 235,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var name = new TextBox
        {
            Name = "StructuredSceneName",
            Width = 220,
            MinHeight = 34,
            Watermark = "Nome scena",
            IsUndoEnabled = true
        };
        var add = new Button
        {
            Name = "StructuredSceneAdd",
            Content = "+ Nuova scena",
            Width = 125,
            MinHeight = 34
        };
        var archive = new Button
        {
            Name = "StructuredSceneArchive",
            Content = "Archivia scena",
            Width = 125,
            MinHeight = 34
        };
        var status = new TextBlock
        {
            Name = "StructuredSceneStatus",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var toolbar = new StackPanel
        {
            Name = SceneToolbarName,
            Spacing = 5,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { selector, name, add, archive }
                },
                status
            }
        };
        var panel = new StackPanel
        {
            Name = ModePanelName,
            Spacing = 5,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Ambientazione", FontSize = 15, VerticalAlignment = VerticalAlignment.Center },
                        mode
                    }
                },
                toolbar
            }
        };

        void PersistScenes(StructuredSceneProfile scenes, string reason)
        {
            StructuredSceneProfileService.Save(project, scenes);
            _ = SafeProjectAutosave.SaveAsync(path, project, reason);
        }

        mode.SelectionChanged += (_, _) => Guarded(panel, () =>
        {
            var wantsScenes = string.Equals(mode.SelectedItem?.ToString(), SceneMode, StringComparison.Ordinal);
            var scenes = StructuredSceneProfileService.Load(project);
            if (wantsScenes == scenes.Enabled) return;

            if (wantsScenes)
            {
                SaveGenericEnvironment(project, environment.Text);
                scenes.Enabled = true;
                if (StructuredSceneProfileService.ActiveScenes(scenes).Count == 0)
                    StructuredSceneProfileService.Add(scenes);
                PersistScenes(scenes, "structured-scene-mode-on");
            }
            else
            {
                var current = StructuredSceneProfileService.ActiveScene(scenes);
                if (current is not null) current.Description = environment.Text ?? string.Empty;
                scenes.Enabled = false;
                PersistScenes(scenes, "structured-scene-mode-off");
            }
            RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
        });

        selector.SelectionChanged += (_, _) => Guarded(panel, () =>
        {
            if (selector.SelectedItem is not SceneChoice choice) return;
            var scenes = StructuredSceneProfileService.Load(project);
            if (!scenes.Enabled) return;
            if (StructuredSceneProfileService.ActiveScenes(scenes).All(x => !string.Equals(x.SceneId, choice.Id, StringComparison.OrdinalIgnoreCase))) return;
            scenes.ActiveSceneId = choice.Id;
            PersistScenes(scenes, "structured-scene-select");
            RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
        });

        name.LostFocus += (_, _) => Guarded(panel, () =>
        {
            var scenes = StructuredSceneProfileService.Load(project);
            var current = StructuredSceneProfileService.ActiveScene(scenes);
            if (!scenes.Enabled || current is null) return;
            if (!StructuredSceneProfileService.TryRename(scenes, current, name.Text, out var error))
            {
                status.Text = error;
                name.Text = current.Name;
                return;
            }
            PersistScenes(scenes, "structured-scene-rename");
            RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
        });

        add.Click += (_, _) => Guarded(panel, () =>
        {
            var scenes = StructuredSceneProfileService.Load(project);
            if (!scenes.Enabled) return;
            var current = StructuredSceneProfileService.ActiveScene(scenes);
            if (current is not null) current.Description = environment.Text ?? string.Empty;
            StructuredSceneProfileService.Add(scenes);
            PersistScenes(scenes, "structured-scene-add");
            RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
            environment.Focus();
        });

        archive.Click += (_, _) => Guarded(panel, () =>
        {
            var scenes = StructuredSceneProfileService.Load(project);
            if (!scenes.Enabled || StructuredSceneProfileService.ActiveScenes(scenes).Count <= 1) return;
            var current = StructuredSceneProfileService.ActiveScene(scenes);
            if (current is null) return;
            current.Description = environment.Text ?? string.Empty;
            StructuredSceneProfileService.RemoveFromActiveScenes(scenes, current.SceneId);
            PersistScenes(scenes, "structured-scene-archive");
            RefreshEnvironmentSceneSwitch(project, environment, environmentContainer, panel);
        });

        environment.TextChanged += (_, _) => Guarded(panel, () =>
        {
            var scenes = StructuredSceneProfileService.Load(project);
            if (!scenes.Enabled)
            {
                SaveGenericEnvironment(project, environment.Text);
                _ = SafeProjectAutosave.SaveAsync(path, project, "generic-environment");
                return;
            }
            var current = StructuredSceneProfileService.ActiveScene(scenes);
            if (current is null) return;
            current.Description = environment.Text ?? string.Empty;
            PersistScenes(scenes, "structured-scene-description");
        });

        return panel;
    }

    private static void RefreshEnvironmentSceneSwitch(
        PreviewProject project,
        TextBox environment,
        StackPanel environmentContainer,
        StackPanel panel)
    {
        Guards.Add(panel);
        try
        {
            var scenes = StructuredSceneProfileService.Load(project);
            var mode = Descendants(panel).OfType<ComboBox>().First(x => x.Name == "EnvironmentSceneMode");
            var selector = Descendants(panel).OfType<ComboBox>().First(x => x.Name == "StructuredSceneSelector");
            var name = Descendants(panel).OfType<TextBox>().First(x => x.Name == "StructuredSceneName");
            var add = Descendants(panel).OfType<Button>().First(x => x.Name == "StructuredSceneAdd");
            var archive = Descendants(panel).OfType<Button>().First(x => x.Name == "StructuredSceneArchive");
            var toolbar = Descendants(panel).OfType<StackPanel>().First(x => x.Name == SceneToolbarName);
            var status = Descendants(panel).OfType<TextBlock>().First(x => x.Name == "StructuredSceneStatus");
            var label = environmentContainer.Children.OfType<TextBlock>().FirstOrDefault();

            mode.SelectedItem = scenes.Enabled ? SceneMode : GenericMode;
            toolbar.IsVisible = scenes.Enabled;

            if (!scenes.Enabled)
            {
                if (!string.Equals(environment.Text, LoadGenericEnvironment(project), StringComparison.Ordinal))
                    environment.Text = LoadGenericEnvironment(project);
                environment.Watermark = "Descrivi l'ambientazione generale della serie. Puoi indicare variazioni locali per singola immagine.";
                if (label is not null) label.Text = "Ambientazione / scenario generale";
                return;
            }

            var active = StructuredSceneProfileService.ActiveScenes(scenes);
            var choices = active.Select(x => new SceneChoice(x.SceneId, x.Number, x.Name)).ToArray();
            selector.ItemsSource = choices;
            var current = StructuredSceneProfileService.ActiveScene(scenes);
            selector.SelectedItem = current is null
                ? null
                : choices.FirstOrDefault(x => string.Equals(x.Id, current.SceneId, StringComparison.OrdinalIgnoreCase));
            name.Text = current?.Name ?? string.Empty;
            if (!string.Equals(environment.Text, current?.Description ?? string.Empty, StringComparison.Ordinal))
                environment.Text = current?.Description ?? string.Empty;
            environment.Watermark = "Descrivi cosa accade in questa scena: azione, luogo, relazioni, oggetti ed elementi specifici. Poi premi + Nuova scena.";
            if (label is not null)
                label.Text = current is null ? "Descrizione scena" : $"Descrizione — Scena {current.Number}: {current.Name}";
            archive.IsEnabled = active.Count > 1;

            IReadOnlyList<MultiSubjectDefinition> participants = current is null
                ? Array.Empty<MultiSubjectDefinition>()
                : StructuredSceneProfileService.Participants(project, current);
            status.Text = current is null
                ? "Crea una scena per iniziare."
                : $"Scene attive: {active.Count} · Partecipanti Consistent: {(participants.Count == 0 ? "nessuno ancora" : string.Join(", ", participants.Select(x => x.Name)))}.";
            add.IsEnabled = active.Count < StructuredSceneProfileService.MaxScenes;
        }
        finally { Guards.Remove(panel); }
    }

    private static void EnsureConsistentMembership(PreviewProject project, string path, Control page, MainWindow window)
    {
        var multi = MultiSubjectProfileService.Load(project);
        var scenes = StructuredSceneProfileService.Load(project);
        var body = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == "ConsistencySubjectBody");
        if (body is not null)
        {
            var old = Descendants(body).OfType<ComboBox>().FirstOrDefault(x => x.Name == "SubjectConsistencyLevel_co_scene");
            var row = old is null ? null : DirectChildContaining(body, old);
            if (row is not null) row.IsVisible = !scenes.Enabled;
        }

        var existing = body is null ? null : Descendants(body).OfType<StackPanel>().FirstOrDefault(x => x.Name == MembershipPanelName);
        if (body is null || !multi.Enabled || !scenes.Enabled)
        {
            if (existing is not null) existing.IsVisible = false;
            return;
        }

        var current = MultiSubjectProfileService.ActiveSubject(multi);
        if (current is null) return;
        if (existing is not null) body.Children.Remove(existing);

        var membership = new StackPanel { Name = MembershipPanelName, Spacing = 5 };
        membership.Children.Add(new TextBlock
        {
            Text = $"A quali scene partecipa {current.Name}?",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });
        membership.Children.Add(new TextBlock
        {
            Text = "Seleziona tutte le scene in cui il personaggio deve comparire. I collegamenti usano identità interne stabili: rinominare scena o personaggio non li rompe.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        var list = new ListBox
        {
            Name = "SubjectSceneListBox",
            MinHeight = 84,
            MaxHeight = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var rows = new List<Control>();
        foreach (var scene in StructuredSceneProfileService.ActiveScenes(scenes))
        {
            var check = new CheckBox
            {
                Name = "SubjectScene_" + scene.SceneId.Replace("-", string.Empty),
                Content = $"Scena {scene.Number} — {scene.Name}",
                IsChecked = scene.ParticipantSubjectIds.Any(x => string.Equals(x, current.SubjectId, StringComparison.OrdinalIgnoreCase)),
                Margin = new Avalonia.Thickness(5, 3)
            };
            var sceneId = scene.SceneId;
            var subjectId = current.SubjectId;
            check.IsCheckedChanged += (_, _) =>
            {
                try
                {
                    var live = StructuredSceneProfileService.Load(project);
                    StructuredSceneProfileService.SetSubjectParticipation(live, sceneId, subjectId, check.IsChecked == true);
                    StructuredSceneProfileService.Save(project, live);
                    _ = SafeProjectAutosave.SaveAsync(path, project, "subject-scene-membership");
                }
                catch (Exception ex)
                {
                    CrashDiagnostics.Error("subject-scene-membership", ex);
                }
            };
            rows.Add(check);
        }
        list.ItemsSource = rows;
        membership.Children.Add(list);
        body.Children.Add(membership);

        var subjectSelector = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ConsistencySubjectSelector");
        if (subjectSelector is not null && Wired.Add(subjectSelector))
            subjectSelector.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => SafeRefresh(window), DispatcherPriority.Background);
    }

    private static string LoadGenericEnvironment(PreviewProject project)
    {
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
            return BookTypePromptProfileService.LoadColoring(project).EnvironmentDescription ?? string.Empty;
        return ImageCollectionPromptProfileService.Load(project).EnvironmentDescription ?? string.Empty;
    }

    private static void SaveGenericEnvironment(PreviewProject project, string? value)
    {
        var text = value ?? string.Empty;
        if (string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
        {
            var profile = BookTypePromptProfileService.LoadColoring(project);
            profile.EnvironmentDescription = text;
            BookTypePromptProfileService.SaveColoring(project, profile);
            return;
        }
        var imageProfile = ImageCollectionPromptProfileService.Load(project);
        imageProfile.EnvironmentDescription = text;
        ImageCollectionPromptProfileService.Save(project, imageProfile);
    }

    private static void Guarded(Control guard, Action action)
    {
        if (Guards.Contains(guard)) return;
        try { action(); }
        catch (Exception ex) { CrashDiagnostics.Error("structured-scene-ui-event", ex); }
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static Control? DirectChildContaining(StackPanel root, Control descendant)
    {
        foreach (var child in root.Children.OfType<Control>())
            if (ReferenceEquals(child, descendant) || Descendants(child).Any(x => ReferenceEquals(x, descendant))) return child;
        return null;
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

    private sealed record SceneChoice(string Id, int Number, string Name)
    {
        public override string ToString() => $"{Number} — {Name}";
    }
}
