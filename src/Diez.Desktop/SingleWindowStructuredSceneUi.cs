using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Compact structured-scene editor plus per-subject scene membership in Consistent.
/// UI events update the in-memory graph synchronously; project I/O is queued through SafeProjectAutosave,
/// so no async-void continuation can resume against a page that navigation has already replaced.
/// </summary>
internal static class SingleWindowStructuredSceneUi
{
    private const string ScenePanelName = "DiezStructuredScenePanel";
    private const string MembershipPanelName = "DiezSubjectSceneMembership";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<Control> Wired = [];
    private static readonly HashSet<StackPanel> Guards = [];

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
            if (quantityRoot is not null) EnsureSceneEditor(project, path, page, quantityRoot);
            EnsureConsistentMembership(project, path, page, window);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.Error("structured-scene-ui-refresh", ex);
        }
    }

    private static void EnsureSceneEditor(PreviewProject project, string path, Control page, StackPanel root)
    {
        var panel = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == ScenePanelName);
        if (panel is null)
        {
            panel = BuildScenePanel(project, path);
            var subject = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "VisualSubjectInstructions");
            var subjectContainer = subject is null ? null : DirectChildContaining(root, subject);
            var index = subjectContainer is null ? Math.Min(4, root.Children.Count) : root.Children.IndexOf(subjectContainer) + 1;
            root.Children.Insert(Math.Clamp(index, 0, root.Children.Count), panel);
        }
        RefreshScenePanel(project, panel);
    }

    private static StackPanel BuildScenePanel(PreviewProject project, string path)
    {
        var enabled = new CheckBox { Name = "StructuredSceneEnabled", Content = "Scene strutturate" };
        var count = new NumericUpDown
        {
            Name = "StructuredSceneCount", Minimum = 1, Maximum = StructuredSceneProfileService.MaxScenes,
            Increment = 1, FormatString = "0", Width = 82, MinHeight = 34
        };
        var selector = new ComboBox { Name = "StructuredSceneSelector", Width = 220, MinHeight = 34 };
        var name = new TextBox { Name = "StructuredSceneName", Width = 210, MinHeight = 34, Watermark = "Nome scena", IsUndoEnabled = true };
        var add = new Button { Name = "StructuredSceneAdd", Content = "+", Width = 38, MinHeight = 34 };
        var remove = new Button { Name = "StructuredSceneRemove", Content = "−", Width = 38, MinHeight = 34 };
        var description = new TextBox
        {
            Name = "StructuredSceneDescription", MinHeight = 72, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, IsUndoEnabled = true,
            Watermark = "Descrivi cosa succede nella scena, l'azione, le relazioni e gli elementi specifici della scena. Facoltativo."
        };
        var status = new TextBlock { Name = "StructuredSceneStatus", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel
        {
            Name = ScenePanelName, Spacing = 5,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        enabled, new TextBlock { Text = "N°", VerticalAlignment = VerticalAlignment.Center },
                        count, selector, name, add, remove
                    }
                },
                new TextBlock { Name = "StructuredSceneDescriptionLabel", Text = "Descrizione scena", FontSize = 13 },
                description, status
            }
        };

        void Commit(StructuredSceneProfile model, string reason)
        {
            StructuredSceneProfileService.Save(project, model);
            _ = SafeProjectAutosave.SaveAsync(path, project, reason);
        }

        enabled.IsCheckedChanged += (_, _) => Guarded(panel, () =>
        {
            var model = StructuredSceneProfileService.Load(project);
            model.Enabled = enabled.IsChecked == true;
            if (model.Enabled) StructuredSceneProfileService.SetCount(model, (int)(count.Value ?? 1));
            Commit(model, "structured-scene-toggle");
            RefreshScenePanel(project, panel);
        });
        count.ValueChanged += (_, _) => Guarded(panel, () =>
        {
            if (enabled.IsChecked != true) return;
            var model = StructuredSceneProfileService.Load(project);
            StructuredSceneProfileService.SetCount(model, (int)(count.Value ?? 1));
            Commit(model, "structured-scene-count");
            RefreshScenePanel(project, panel);
        });
        selector.SelectionChanged += (_, _) => Guarded(panel, () =>
        {
            if (selector.SelectedItem is not SceneChoice choice) return;
            var model = StructuredSceneProfileService.Load(project);
            if (!StructuredSceneProfileService.ActiveScenes(model).Any(x => string.Equals(x.SceneId, choice.Id, StringComparison.OrdinalIgnoreCase))) return;
            model.ActiveSceneId = choice.Id;
            Commit(model, "structured-scene-select");
            RefreshScenePanel(project, panel);
        });
        name.LostFocus += (_, _) => Guarded(panel, () =>
        {
            var model = StructuredSceneProfileService.Load(project);
            var scene = StructuredSceneProfileService.ActiveScene(model);
            if (scene is null) return;
            if (!StructuredSceneProfileService.TryRename(model, scene, name.Text, out var error))
            {
                status.Text = error;
                name.Text = scene.Name;
                return;
            }
            Commit(model, "structured-scene-rename");
            RefreshScenePanel(project, panel);
        });
        add.Click += (_, _) => Guarded(panel, () =>
        {
            var model = StructuredSceneProfileService.Load(project);
            if (!model.Enabled) return;
            StructuredSceneProfileService.Add(model);
            Commit(model, "structured-scene-add");
            RefreshScenePanel(project, panel);
        });
        remove.Click += (_, _) => Guarded(panel, () =>
        {
            var model = StructuredSceneProfileService.Load(project);
            if (!model.Enabled) return;
            StructuredSceneProfileService.RemoveFromActiveScenes(model, model.ActiveSceneId);
            Commit(model, "structured-scene-remove");
            RefreshScenePanel(project, panel);
        });
        description.TextChanged += (_, _) => Guarded(panel, () =>
        {
            var model = StructuredSceneProfileService.Load(project);
            var scene = StructuredSceneProfileService.ActiveScene(model);
            if (!model.Enabled || scene is null) return;
            scene.Description = description.Text ?? string.Empty;
            Commit(model, "structured-scene-description");
        });
        return panel;
    }

    private static void Guarded(StackPanel panel, Action action)
    {
        if (Guards.Contains(panel)) return;
        try { action(); }
        catch (Exception ex) { CrashDiagnostics.Error("structured-scene-ui-event", ex); }
    }

    private static void RefreshScenePanel(PreviewProject project, StackPanel panel)
    {
        Guards.Add(panel);
        try
        {
            var model = StructuredSceneProfileService.Load(project);
            var enabled = Descendants(panel).OfType<CheckBox>().First(x => x.Name == "StructuredSceneEnabled");
            var count = Descendants(panel).OfType<NumericUpDown>().First(x => x.Name == "StructuredSceneCount");
            var selector = Descendants(panel).OfType<ComboBox>().First(x => x.Name == "StructuredSceneSelector");
            var name = Descendants(panel).OfType<TextBox>().First(x => x.Name == "StructuredSceneName");
            var description = Descendants(panel).OfType<TextBox>().First(x => x.Name == "StructuredSceneDescription");
            var add = Descendants(panel).OfType<Button>().First(x => x.Name == "StructuredSceneAdd");
            var remove = Descendants(panel).OfType<Button>().First(x => x.Name == "StructuredSceneRemove");
            var status = Descendants(panel).OfType<TextBlock>().First(x => x.Name == "StructuredSceneStatus");
            var label = Descendants(panel).OfType<TextBlock>().First(x => x.Name == "StructuredSceneDescriptionLabel");

            enabled.IsChecked = model.Enabled;
            count.Value = model.RequestedCount;
            var active = StructuredSceneProfileService.ActiveScenes(model);
            var choices = active.Select(x => new SceneChoice(x.SceneId, x.Number, x.Name)).ToArray();
            selector.ItemsSource = choices;
            var current = StructuredSceneProfileService.ActiveScene(model);
            selector.SelectedItem = current is null ? null : choices.FirstOrDefault(x => string.Equals(x.Id, current.SceneId, StringComparison.OrdinalIgnoreCase));
            name.Text = current?.Name ?? string.Empty;
            description.Text = current?.Description ?? string.Empty;
            label.Text = current is null ? "Descrizione scena" : $"Descrizione — Scena {current.Number}: {current.Name}";
            count.IsVisible = selector.IsVisible = name.IsVisible = add.IsVisible = remove.IsVisible = description.IsVisible = label.IsVisible = model.Enabled;
            remove.IsEnabled = model.Enabled && active.Count > 1;
            IReadOnlyList<MultiSubjectDefinition> participants = current is null ? Array.Empty<MultiSubjectDefinition>() : StructuredSceneProfileService.Participants(project, current);
            status.Text = model.Enabled
                ? $"{active.Count} scene attive · SceneId stabile. Partecipanti: {(participants.Count == 0 ? "nessuno assegnato" : string.Join(", ", participants.Select(x => x.Name)))}."
                : "Facoltativo. Se OFF, il progetto continua a usare tema/soggetto senza SceneId strutturato.";
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
        if (body is null || !multi.Enabled || !scenes.Enabled)
        {
            var stale = Descendants(page).OfType<StackPanel>().FirstOrDefault(x => x.Name == MembershipPanelName);
            if (stale is not null) stale.IsVisible = false;
            return;
        }

        var current = MultiSubjectProfileService.ActiveSubject(multi);
        if (current is null) return;
        var existing = Descendants(body).OfType<StackPanel>().FirstOrDefault(x => x.Name == MembershipPanelName);
        if (existing is not null) body.Children.Remove(existing);

        var membership = new StackPanel { Name = MembershipPanelName, Spacing = 4 };
        membership.Children.Add(new TextBlock { Text = $"Partecipa alle scene — {current.Name}", FontSize = 13, TextWrapping = TextWrapping.Wrap });
        membership.Children.Add(new TextBlock { Text = "La relazione usa SceneId + SubjectId: rinominare scena o personaggio non rompe il collegamento.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        foreach (var scene in StructuredSceneProfileService.ActiveScenes(scenes))
        {
            var check = new CheckBox
            {
                Name = "SubjectScene_" + scene.SceneId.Replace("-", string.Empty),
                Content = $"Scena {scene.Number} — {scene.Name}",
                IsChecked = scene.ParticipantSubjectIds.Any(x => string.Equals(x, current.SubjectId, StringComparison.OrdinalIgnoreCase))
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
                    Dispatcher.UIThread.Post(() => SafeRefresh(window), DispatcherPriority.Background);
                }
                catch (Exception ex) { CrashDiagnostics.Error("subject-scene-membership", ex); }
            };
            membership.Children.Add(check);
        }
        body.Children.Add(membership);

        var subjectSelector = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ConsistencySubjectSelector");
        if (subjectSelector is not null && Wired.Add(subjectSelector))
            subjectSelector.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() => SafeRefresh(window), DispatcherPriority.Background);
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
