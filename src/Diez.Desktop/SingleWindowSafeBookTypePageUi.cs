using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Native safe book-type page. It replaces the original page before the user can interact
/// with the old async handler. Save and navigation are split across dispatcher turns.
/// </summary>
internal static class SingleWindowSafeBookTypePageUi
{
    private const string PageMarker = "DiezSafeBookTypePage";
    private static readonly HashSet<MainWindow> Attached = [];
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                Dispatcher.UIThread.Post(() => ReplaceOriginalIfNeeded(window), DispatcherPriority.Loaded);
        };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => ReplaceOriginalIfNeeded(window), DispatcherPriority.Loaded);
        window.Closed += (_, _) => Attached.Remove(window);
        ReplaceOriginalIfNeeded(window);
    }

    internal static void ReplaceOriginalIfNeeded(MainWindow window)
    {
        if (!TrySession(window, out _, out _)) return;
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        if (PageHost(host).Content is not Control page) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, PageMarker, StringComparison.Ordinal))) return;
        if (!Descendants(page).OfType<TextBlock>().Any(t =>
                (t.Text ?? string.Empty).Contains("Quale libro stai preparando?", StringComparison.Ordinal))) return;
        Show(window);
    }

    public static void Show(MainWindow window)
    {
        if (!TrySession(window, out var project, out _)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var choices = new[]
        {
            BookTypeProfileService.ColoringBook,
            BookTypeProfileService.ImageCollection,
            BookTypeProfileService.IllustratedBook,
            BookTypeProfileService.EssayManual,
            BookTypeProfileService.WordSearch,
            BookTypeProfileService.Crossword,
            BookTypeProfileService.Quiz,
            BookTypeProfileService.Novel,
            BookTypeProfileService.DataCollection,
            BookTypeProfileService.Other
        };

        var combo = new ComboBox
        {
            Name = "DiezSafeBookTypeCombo",
            ItemsSource = choices,
            Width = 350,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var current = BookTypeProfileService.Get(project);
        combo.SelectedItem = choices.FirstOrDefault(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase))
                             ?? BookTypeProfileService.ColoringBook;

        var apply = new Button
        {
            Name = "DiezSafeBookTypeApplyV2",
            Content = "Usa questo Tipo libro",
            Width = 190,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        apply.Click += async (_, _) => await ApplyAsync(window, host, combo, apply);

        var root = new StackPanel
        {
            Name = PageMarker,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Quale libro stai preparando?", FontSize = 24 },
                new TextBlock
                {
                    Text = "Scegli il Tipo libro. Diez salva prima la scelta e solo dopo apre la schermata successiva.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                combo,
                apply
            }
        };

        SingleWindowEntryPointUi.Invoke(host, "Push", "Tipo libro", root,
            new Border
            {
                Padding = new Avalonia.Thickness(18),
                Child = new TextBlock
                {
                    Text = "Dopo la scelta, il riquadro anteprima e i controlli si adattano al Tipo libro.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            },
            "Seleziona il Tipo libro e conferma.");
        CrashDiagnostics.Navigation("book-type-safe-page-visible");
    }

    internal static async Task RunContractAsync(MainWindow window)
    {
        var temp = Path.Combine(Path.GetTempPath(), "diez-safe-book-type-" + Guid.NewGuid().ToString("N") + ".diez");
        try
        {
            var project = ProjectFileStore.Create("Safe Book Type Contract");
            await ProjectFileStore.SaveAsync(temp, project);
            SetSession(window, project, temp);
            Show(window);

            var host = SingleWindowEntryPointUi.GetHost(window);
            var page = PageHost(host).Content as Control ?? throw new InvalidOperationException("Pagina Tipo libro sicura assente.");
            if (!Descendants(page).Any(c => string.Equals(c.Name, PageMarker, StringComparison.Ordinal)))
                throw new InvalidOperationException("Marker pagina Tipo libro sicura assente.");
            var combo = Descendants(page).OfType<ComboBox>().First(c => c.Name == "DiezSafeBookTypeCombo");
            var button = Descendants(page).OfType<Button>().First(b => b.Name == "DiezSafeBookTypeApplyV2");
            combo.SelectedItem = BookTypeProfileService.ColoringBook;
            if (!await ApplyAsync(window, host, combo, button))
                throw new InvalidOperationException("Applicazione Tipo libro sicura fallita.");

            var reloaded = await ProjectFileStore.LoadAsync(temp);
            if (!string.Equals(BookTypeProfileService.Get(reloaded), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Tipo libro non persistito.");

            var next = PageHost(host).Content as Control;
            if (next is null || !Descendants(next).OfType<TextBlock>().Any(t =>
                    (t.Text ?? string.Empty).Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
                throw new InvalidOperationException("Navigazione alla quantità non completata.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task<bool> ApplyAsync(MainWindow window, object host, ComboBox combo, Button button)
    {
        if (!await Gate.WaitAsync(0)) return false;
        button.IsEnabled = false;
        try
        {
            if (!TrySession(window, out var project, out var path))
                throw new InvalidOperationException("Sessione progetto non disponibile.");

            var chosen = combo.SelectedItem?.ToString() ?? BookTypeProfileService.Other;
            CrashDiagnostics.Navigation("book-type-before-set", chosen);
            BookTypeProfileService.Set(project, chosen);
            CrashDiagnostics.Navigation("book-type-before-save", path);
            await ProjectFileStore.SaveAsync(path, project);
            CrashDiagnostics.Navigation("book-type-after-save", BookTypeProfileService.Get(project));

            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    CrashDiagnostics.Navigation("book-type-before-navigation", BookTypeProfileService.Get(project));
                    ClearHistory(host);
                    if (BookTypeProfileService.IsImageCollection(project))
                        SingleWindowEntryPointUi.Invoke(host, "OpenQuantity");
                    else
                        SingleWindowEntryPointUi.Invoke(host, "OpenCurrentBook");
                    CrashDiagnostics.Navigation("book-type-after-navigation", BookTypeProfileService.Get(project));
                    navigation.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    CrashDiagnostics.Error("book-type-navigation", ex);
                    Report(window, host, "Errore apertura schermata successiva. Diez resta aperto: " + ex.GetBaseException().Message);
                    navigation.TrySetResult(false);
                }
            }, DispatcherPriority.Loaded);

            var ok = await navigation.Task;
            if (ok) Report(window, host, $"Tipo libro salvato: {BookTypeProfileService.Get(project)}.");
            return ok;
        }
        catch (Exception ex)
        {
            CrashDiagnostics.Error("book-type-save", ex);
            Report(window, host, "Errore durante la scelta del Tipo libro. Diez resta aperto: " + ex.GetBaseException().Message);
            return false;
        }
        finally
        {
            button.IsEnabled = true;
            Gate.Release();
        }
    }

    private static void ClearHistory(object host)
    {
        if (host.GetType().GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) is IList list)
            list.Clear();
    }

    private static void Report(MainWindow window, object host, string text)
    {
        if (host.GetType().GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) is TextBlock hostStatus)
            hostStatus.Text = text;
        if (typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) is TextBlock mainStatus)
            mainStatus.Text = text;
    }

    private static bool TrySession(MainWindow window, out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
    }

    private static ContentControl PageHost(object host) =>
        host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl
        ?? throw new InvalidOperationException("PageHost single-window non disponibile.");

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
