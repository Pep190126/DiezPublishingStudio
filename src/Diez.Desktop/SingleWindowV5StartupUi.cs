using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// SW-FLOW-5 makes the logical book workflow the first visible Diez screen.
/// The legacy MainWindow remains alive as the physical window and project host,
/// but the user no longer needs to discover a hidden entry button.
/// </summary>
internal static class SingleWindowV5StartupUi
{
    public const string Marker = "SW-FLOW-5";

    public static void Attach(MainWindow window)
    {
        window.KeyDown += HandleEditorShortcuts;
        window.Closed += (_, _) => window.KeyDown -= HandleEditorShortcuts;
        window.Opened += async (_, _) =>
        {
            // MainWindow may already be opening a .diez supplied on the command line.
            // Give that handler a short chance to complete before selecting the first page.
            for (var i = 0; i < 8 && !TrySession(window, out _, out _); i++)
                await Task.Delay(40);

            ShowStart(window);
        };
    }

    internal static void ShowStart(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        ReplaceMarker(window);

        if (TrySession(window, out _, out _))
        {
            // Always show the explicit book-type page first. The user can confirm
            // or change it instead of relying on inference from an old project.
            SingleWindowEntryPointUi.Invoke(host, "OpenBookTypeChoice");
            ReplaceMarker(window);
            return;
        }

        ShowWelcome(window, host);
    }

    private static void ShowWelcome(MainWindow window, object host)
    {
        var create = Button("Nuovo progetto", 180);
        var open = Button("Apri progetto .diez", 190);

        create.Click += async (_, _) =>
        {
            await InvokeMainTaskAsync(window, "CreateProjectAsync");
            if (TrySession(window, out _, out _))
            {
                SingleWindowEntryPointUi.Invoke(host, "OpenBookTypeChoice");
                ReplaceMarker(window);
            }
        };

        open.Click += async (_, _) =>
        {
            await InvokeMainTaskAsync(window, "OpenProjectAsync");
            if (TrySession(window, out _, out _))
            {
                SingleWindowEntryPointUi.Invoke(host, "OpenBookTypeChoice");
                ReplaceMarker(window);
            }
        };

        var content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Diez Publishing Studio", FontSize = 28 },
                new TextBlock
                {
                    Text = "Inizia da un progetto. Tutte le schermate successive resteranno in questa stessa finestra.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { create, open }
                }
            }
        };

        var preview = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Anteprima", FontSize = 22 },
                    new TextBlock
                    {
                        Text = "Qui compariranno paradigmi, immagini generate, confronti e descrizioni durante il lavoro.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            }
        };

        SingleWindowEntryPointUi.Invoke(host, "Push", "Inizia · SW-FLOW-5", content, preview,
            "Crea o apri un progetto; il passo successivo sarà la scelta del Tipo libro.");
        ReplaceMarker(window);
    }

    private static void HandleEditorShortcuts(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if (e.Source is not TextBox editor || editor.IsReadOnly || !editor.IsEnabled || !editor.IsUndoEnabled) return;

        if (e.Key == Key.Z)
        {
            editor.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            editor.Redo();
            e.Handled = true;
        }
    }

    private static async Task InvokeMainTaskAsync(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).Name, methodName);
        var result = method.Invoke(window, null);
        if (result is Task task) await task;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static void ReplaceMarker(MainWindow window)
    {
        foreach (var text in Descendants(window).OfType<TextBlock>())
        {
            var value = text.Text ?? string.Empty;
            if (value.Contains("Diez single-window · SW-FLOW-", StringComparison.Ordinal))
                text.Text = $"Diez single-window · {Marker}";
        }
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
