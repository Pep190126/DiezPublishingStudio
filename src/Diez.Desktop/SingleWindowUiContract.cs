using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class SingleWindowEntryPointUi
{
    private const string Marker = "SW-FLOW-4";

    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not Grid desktop) return;
        var header = desktop.Children.OfType<Grid>().FirstOrDefault(c => Grid.GetRow(c) == 0);
        if (header is null) return;

        InstallUndoRedo(window);

        if (header.Children.OfType<StackPanel>().Any(p => p.Children.OfType<Button>()
            .Any(b => (b.Content?.ToString() ?? string.Empty).Contains(Marker, StringComparison.Ordinal)))) return;

        header.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto");
        var open = new Button
        {
            Content = $"Percorso libro · {Marker}",
            Width = 220,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        open.Click += (_, _) => OpenCurrentBook(window);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Flusso guidato a finestra unica",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                },
                open
            }
        };
        Grid.SetRow(bar, 3);
        header.Children.Add(bar);
    }

    private static void InstallUndoRedo(MainWindow window)
    {
        window.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            var editor = window.FocusManager?.GetFocusedElement() as TextBox;
            if (editor is null || editor.IsReadOnly || !editor.IsEnabled || !editor.IsUndoEnabled) return;

            if (e.Key == Key.Z)
            {
                if (editor.CanUndo) editor.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                if (editor.CanRedo) editor.Redo();
                e.Handled = true;
            }
        };
    }

    internal static object GetHost(MainWindow window)
    {
        var field = typeof(SingleWindowOverlayFlowUi).GetField("Hosts", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Host single-window non trovato.");
        if (field.GetValue(null) is not IDictionary hosts || hosts[window] is not object host)
            throw new InvalidOperationException("Il percorso single-window non è montato nel MainWindow.");
        return host;
    }

    internal static void OpenCurrentBook(MainWindow window)
    {
        var host = GetHost(window);
        Invoke(host, "OpenCurrentBook");
        ReplaceOldMarker(window);
    }

    internal static object? Invoke(object host, string method, params object?[]? args)
    {
        var parameterCount = args?.Length ?? 0;
        var target = host.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == parameterCount)
            ?? throw new MissingMethodException(host.GetType().Name, method);
        return target.Invoke(host, args);
    }

    private static void ReplaceOldMarker(MainWindow window)
    {
        foreach (var text in Descendants(window).OfType<TextBlock>())
        {
            if ((text.Text ?? string.Empty).Contains("Diez single-window · SW-FLOW-2", StringComparison.Ordinal))
                text.Text = $"Diez single-window · {Marker}";
            else if ((text.Text ?? string.Empty).Contains("Diez single-window · SW-FLOW-3", StringComparison.Ordinal))
                text.Text = $"Diez single-window · {Marker}";
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);
        var seen = new HashSet<Control>();
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

internal static class SingleWindowUiContractProbe
{
    public static async Task RunAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-ui-flow-contract-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Coloring UI Contract");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            await ProjectFileStore.SaveAsync(temp, project);

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
            typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, temp);

            var entry = Descendants(window).OfType<Button>().FirstOrDefault(b =>
                (b.Content?.ToString() ?? string.Empty).Contains("Percorso libro · SW-FLOW-4", StringComparison.Ordinal));
            if (entry is null || !entry.IsVisible || !entry.IsEnabled)
                throw new InvalidOperationException("Il comando Percorso libro SW-FLOW-4 non è visibile e attivo nel MainWindow reale.");

            var host = SingleWindowEntryPointUi.GetHost(window);
            SingleWindowEntryPointUi.Invoke(host, "OpenCurrentBook");
            var pageHost = GetPageHost(host);
            AssertQuantityPage(pageHost.Content as Control);

            SingleWindowEntryPointUi.Invoke(host, "OpenPrompt", 12);
            AssertPromptPage(pageHost.Content as Control);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static ContentControl GetPageHost(object host) =>
        host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
        ?? throw new InvalidOperationException("PageHost single-window non disponibile.");

    private static void AssertQuantityPage(Control? page)
    {
        if (page is null) throw new InvalidOperationException("La pagina Quantità non è stata mostrata.");
        var controls = Descendants(page).ToList();
        if (!controls.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            throw new InvalidOperationException("Manca il campo logico 'Quante immagini vuoi creare?'.");
        var count = controls.OfType<TextBox>().FirstOrDefault(t => !t.AcceptsReturn && t.IsEnabled && !t.IsReadOnly);
        if (count is null) throw new InvalidOperationException("Il campo numero immagini non è un TextBox editabile.");
        if (controls.OfType<RadioButton>().Any())
            throw new InvalidOperationException("La pagina Coloring contiene ancora radio button legacy.");
    }

    private static void AssertPromptPage(Control? page)
    {
        if (page is null) throw new InvalidOperationException("La pagina Istruzioni non è stata mostrata.");
        var controls = Descendants(page).ToList();
        var labels = controls.OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        foreach (var expected in new[] { "DEVE FARE", "NON DEVE FARE", "PROMPT — modificabile" })
            if (!labels.Any(t => string.Equals(t, expected, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Manca il box '{expected}'.");

        var editors = controls.OfType<TextBox>().Where(t => t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) throw new InvalidOperationException("I tre box non sono tutti editabili.");
        if (editors.Take(3).Any(t => !t.IsUndoEnabled))
            throw new InvalidOperationException("Undo/redo non è attivo su tutti i tre box.");
        if (typeof(TextBox).GetMethod(nameof(TextBox.Undo), Type.EmptyTypes) is null ||
            typeof(TextBox).GetMethod(nameof(TextBox.Redo), Type.EmptyTypes) is null)
            throw new InvalidOperationException("Il controllo editor non espone Undo/Redo.");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        stack.Push(root);
        var seen = new HashSet<Control>();
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