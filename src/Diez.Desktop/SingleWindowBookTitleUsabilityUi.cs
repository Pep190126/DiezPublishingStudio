using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

/// <summary>
/// Keeps the visible book title field human-friendly without changing ProjectId semantics.
/// When no edition title exists yet, it starts from the project name and remains fully editable.
/// </summary>
internal static class SingleWindowBookTitleUsabilityUi
{
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly HashSet<TextBox> WiredTitles = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;

        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("PageHost non disponibile per il titolo libro.");

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => Apply(window, pageHost), DispatcherPriority.Loaded);
        };

        Apply(window, pageHost);
        window.Closed += (_, _) => Attached.Remove(window);
    }

    private static void Apply(MainWindow window, ContentControl pageHost)
    {
        SingleWindowVisualBookIdentityUi.Apply(window);
        StablePageObserverLifecycleUi.Refresh(window, pageHost, "stable-page-change");

        var project = Field<PreviewProject>(window, "_project");
        if (project is null || pageHost.Content is not Control page) return;

        var title = Descendants(page).OfType<TextBox>().FirstOrDefault(c => c.Name == "DiezBookTitle");
        var field = Descendants(page).OfType<StackPanel>().FirstOrDefault(c => c.Name == "DiezBookTitleField");
        if (title is null || field is null) return;

        if (string.IsNullOrWhiteSpace(project.EditionMetadata.Title))
        {
            project.EditionMetadata.Title = project.Name;
            title.Text = project.Name;
        }
        else if (string.IsNullOrWhiteSpace(title.Text))
        {
            title.Text = project.EditionMetadata.Title;
        }

        const double preferredWidth = 620;
        const double horizontalSafety = 16;
        var mountedWidth = pageHost.Bounds.Width;
        var width = mountedWidth > horizontalSafety
            ? Math.Min(preferredWidth, mountedWidth - horizontalSafety)
            : Math.Min(preferredWidth, 520);

        field.Width = width;
        field.MaxWidth = preferredWidth;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.IsEnabled = true;
        field.IsHitTestVisible = true;
        title.Width = width;
        title.MaxWidth = preferredWidth;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextAlignment = TextAlignment.Left;
        title.Background = Brushes.White;
        title.Foreground = Brushes.Black;
        title.BorderBrush = Brushes.Gray;
        title.BorderThickness = new Avalonia.Thickness(2);

        title.IsReadOnly = false;
        title.IsEnabled = true;
        title.IsHitTestVisible = true;
        title.Focusable = true;
        title.IsUndoEnabled = true;

        if (WiredTitles.Add(title))
        {
            title.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                SafeStartupTrace.Write(
                    "book-title-input | event=pointer-pressed" +
                    " | focused=" + title.IsFocused +
                    " | enabled=" + title.IsEnabled +
                    " | hitTest=" + title.IsHitTestVisible +
                    " | readOnly=" + title.IsReadOnly);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            title.GotFocus += (_, _) => SafeStartupTrace.Write(
                "book-title-input | event=got-focus | focused=" + title.IsFocused);
            title.TextChanged += (_, _) => SafeStartupTrace.Write(
                "book-title-input | event=text-changed | length=" + (title.Text?.Length ?? 0));
        }

        foreach (var label in field.Children.OfType<TextBlock>())
            label.HorizontalAlignment = HorizontalAlignment.Left;

        SingleWindowQuantityUsabilityUi.ForceWin32Frame(window, "book-title-input-ready");
        SafeStartupTrace.Write(
            "book-title-usability | title=" + (title.Text ?? string.Empty) +
            " | source=" + (string.Equals(title.Text, project.Name, StringComparison.Ordinal) ? "project-name" : "edition-title") +
            " | alignment=left" +
            " | mountedWidth=" + mountedWidth.ToString("0.##") +
            " | editorWidth=" + width.ToString("0.##") +
            " | withinMountedPage=" + (mountedWidth <= 0 || width <= mountedWidth) +
            " | editable=" + (!title.IsReadOnly && title.IsEnabled && title.IsHitTestVisible && title.Focusable));

        Dispatcher.UIThread.Post(() => TraceCompositionChain(title), DispatcherPriority.Render);
        Dispatcher.UIThread.Post(() => TraceCompositionChain(title), DispatcherPriority.Background);
    }

    private static void TraceCompositionChain(Control target)
    {
        try
        {
            var visualType = typeof(Avalonia.Visual);
            var compositionProperty = visualType.GetProperty(
                "CompositionVisual",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (compositionProperty is null)
            {
                SafeStartupTrace.Write("book-title-compositor-chain | compositionProperty=<missing>");
                return;
            }

            var visuals = new[] { (Avalonia.Visual)target }
                .Concat(target.GetVisualAncestors())
                .Take(16)
                .ToList();
            var parts = new List<string>();

            for (var i = 0; i < visuals.Count; i++)
            {
                var visual = visuals[i];
                var composition = compositionProperty.GetValue(visual);
                bool? linkedToParent = null;
                string parentChildrenState = "n/a";

                if (i + 1 < visuals.Count)
                {
                    var parentComposition = compositionProperty.GetValue(visuals[i + 1]);
                    if (composition is null || parentComposition is null)
                    {
                        linkedToParent = false;
                        parentChildrenState = parentComposition is null ? "parent-comp-null" : "child-comp-null";
                    }
                    else
                    {
                        var childrenProperty = FindProperty(parentComposition.GetType(), "Children");
                        if (childrenProperty?.GetValue(parentComposition) is IEnumerable children)
                        {
                            var childObjects = children.Cast<object>().ToList();
                            linkedToParent = childObjects.Any(child => ReferenceEquals(child, composition));
                            parentChildrenState = "count=" + childObjects.Count;
                        }
                        else
                        {
                            parentChildrenState = "children-unavailable";
                        }
                    }
                }

                parts.Add(
                    Describe(visual) +
                    ":comp=" + (composition is null ? "null" : composition.GetType().Name) +
                    ":linked=" + (linkedToParent.HasValue ? linkedToParent.Value.ToString() : "root") +
                    ":parentChildren=" + parentChildrenState +
                    ":serverTransform=" + ReadServerTransform(composition) +
                    ":compRoot=" + ReadCompositionProperty(composition, "Root") +
                    ":compDrawList=" + ReadCompositionProperty(composition, "DrawList") +
                    ":compOffset=" + ReadCompositionProperty(composition, "Offset") +
                    ":compSize=" + ReadCompositionProperty(composition, "Size") +
                    ":compVisible=" + ReadCompositionProperty(composition, "Visible") +
                    ":compOpacity=" + ReadCompositionProperty(composition, "Opacity") +
                    ":compClipToBounds=" + ReadCompositionProperty(composition, "ClipToBounds") +
                    ":compClip=" + ReadCompositionProperty(composition, "Clip") +
                    ":compTransform=" + ReadCompositionProperty(composition, "TransformMatrix"));
            }

            SafeStartupTrace.Write("book-title-compositor-chain | " + string.Join(" > ", parts));
        }
        catch (Exception ex)
        {
            SafeStartupTrace.Write("book-title-compositor-chain | error=" + ex.GetBaseException().Message);
        }
    }

    private static string ReadServerTransform(object? composition)
    {
        if (composition is null) return "<comp-null>";
        try
        {
            var method = FindMethod(composition.GetType(), "TryGetServerGlobalTransform");
            if (method is null) return "<missing>";
            var value = method.Invoke(composition, null);
            return value is null ? "<null>" : value.ToString() ?? "<null-string>";
        }
        catch (Exception ex)
        {
            return "<error:" + ex.GetBaseException().Message.Replace('|', '/') + ">";
        }
    }

    private static string ReadCompositionProperty(object? composition, string name)
    {
        if (composition is null) return "<comp-null>";
        try
        {
            var property = FindProperty(composition.GetType(), name);
            if (property is null) return "<missing>";
            var value = property.GetValue(composition);
            return value is null ? "<null>" : value.ToString() ?? "<null-string>";
        }
        catch (Exception ex)
        {
            return "<error:" + ex.GetBaseException().Message.Replace('|', '/') + ">";
        }
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null) return property;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method is not null) return method;
        }
        return null;
    }

    private static string Describe(Avalonia.Visual visual)
    {
        if (visual is not Control control) return visual.GetType().Name;
        return control.GetType().Name +
               "[name=" + (control.Name ?? "-") +
               ",bounds=" + control.Bounds +
               ",visible=" + control.IsVisible +
               ",attached=" + control.IsAttachedToVisualTree() +
               ",hit=" + control.IsHitTestVisible +
               ",enabled=" + control.IsEnabled +
               ",effectiveEnabled=" + control.IsEffectivelyEnabled +
               ",z=" + control.ZIndex +
               ",opacity=" + control.Opacity.ToString("0.##") + "]";
    }

    private static T? Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as T;

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
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer scroll when scroll.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }
}
