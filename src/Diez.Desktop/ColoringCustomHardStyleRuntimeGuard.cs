using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Compatibility boundary for the native Coloring page while older decorators still observe the same style
/// controls. When the visible Custom editor changes, the exact text is re-asserted as the project-local HARD
/// authority at the end of the UI cycle. The guard is intentionally inactive for non-Custom selections, so an
/// explicit Kawaii/Cartoon/etc. selection still deactivates Custom through the normal style owner.
/// </summary>
internal static class ColoringCustomHardStyleRuntimeGuard
{
    private static readonly HashSet<MainWindow> AttachedWindows = [];
    private static readonly HashSet<TextBox> WiredEditors = [];

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        _ = AwaitApplicationAsync();
    }

    private static async Task AwaitApplicationAsync()
    {
        // Module initialization happens before Avalonia has necessarily created Application.Current.
        // Poll only during startup; once MainWindow is attached this task exits permanently.
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
            try
            {
                var attached = await Dispatcher.UIThread.InvokeAsync(TryAttachCurrentWindow);
                if (attached) return;
            }
            catch
            {
                // Startup ordering only. Normal startup diagnostics still report real UI failures elsewhere.
            }
        }
    }

    private static bool TryAttachCurrentWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not MainWindow window)
            return false;
        Attach(window);
        return true;
    }

    private static void Attach(MainWindow window)
    {
        if (!AttachedWindows.Add(window)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != ContentControl.ContentProperty) return;
            Dispatcher.UIThread.Post(() => WireCurrentPage(window), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(() => WireCurrentPage(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => AttachedWindows.Remove(window);
        WireCurrentPage(window);
    }

    private static void WireCurrentPage(MainWindow window)
    {
        if (!TrySession(window, out var project, out var path)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page) return;

        var custom = Descendants(page).OfType<TextBox>().FirstOrDefault(x => x.Name == "ColoringCustomStyleNotes");
        var style = Descendants(page).OfType<ComboBox>().FirstOrDefault(x => x.Name == "ColoringStyle");
        if (custom is null || style is null || !WiredEditors.Add(custom)) return;

        custom.TextChanged += (_, _) =>
        {
            if (!ShouldOwnCustom(project, custom, style)) return;
            var definition = custom.Text ?? string.Empty;
            AssertAuthority(project, path, definition);
            // Let all legacy TextChanged/SelectionChanged observers finish, then assert the same user value
            // once more. This is not a retry/render loop; it only reconciles in-memory UI state.
            Dispatcher.UIThread.Post(() =>
            {
                if (ShouldOwnCustom(project, custom, style))
                    AssertAuthority(project, path, custom.Text ?? string.Empty);
            }, DispatcherPriority.Background);
        };
    }

    private static bool ShouldOwnCustom(PreviewProject project, TextBox custom, ComboBox style)
    {
        if (!custom.IsVisible) return false;
        var selected = style.SelectedItem?.ToString() ?? string.Empty;
        if (string.Equals(selected, "Custom", StringComparison.OrdinalIgnoreCase)) return true;
        if (CustomStyleLibraryService.TryResolve(selected, out _)) return true;
        if (ColoringCustomHardStyleStore.LoadState(project).IsActive) return true;
        return string.Equals(BookTypePromptProfileService.LoadColoring(project).Style, "Custom", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAuthority(PreviewProject project, string path, string definition)
    {
        var clean = (definition ?? string.Empty).Trim();
        var profile = BookTypePromptProfileService.LoadColoring(project);
        profile.Style = "Custom";
        profile.CustomStyleNotes = clean;
        BookTypePromptProfileService.SaveColoring(project, profile);
        ColoringCustomHardStyleStore.Activate(project, clean);
        _ = SafeProjectAutosave.SaveAsync(path, project, "custom-style-hard-runtime-reconcile");
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
                case Panel panel:
                    for (var i = panel.Children.Count - 1; i >= 0; i--) stack.Push(panel.Children[i]);
                    break;
                case Border border when border.Child is Control child: stack.Push(child); break;
                case ScrollViewer scroll when scroll.Content is Control child: stack.Push(child); break;
                case ContentControl content when content.Content is Control child: stack.Push(child); break;
            }
        }
    }
}
