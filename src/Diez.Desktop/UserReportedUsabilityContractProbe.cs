using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DiezPublishingStudio;

/// <summary>
/// Regression contract for the exact usability issues reported from the installed Windows app:
/// Home material visibility, editable/left-aligned initial book title, physical Home-project navigation,
/// real imported-image preview on Coloring 1/4 and an actually scrollable long Quantity page.
/// </summary>
internal static class UserReportedUsabilityContractProbe
{
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static async Task RunAsync(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = Field<ContentControl>(host, "_pageHost")
            ?? throw new InvalidOperationException("Usability contract: pageHost assente.");
        var previewHost = Field<ContentControl>(host, "_previewHost")
            ?? throw new InvalidOperationException("Usability contract: previewHost assente.");
        var materialsList = Field<ListBox>(window, "_materialsList")
            ?? throw new InvalidOperationException("Usability contract: box Materiali Home assente.");
        var status = Field<TextBlock>(window, "_status")
            ?? throw new InvalidOperationException("Usability contract: status Home assente.");

        var tempProject = Path.Combine(Path.GetTempPath(), "diez-user-usability-" + Guid.NewGuid().ToString("N") + ".diez");
        var tempImage = Path.Combine(Path.GetTempPath(), "diez-user-preview-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            await File.WriteAllBytesAsync(tempImage, Convert.FromBase64String(TinyPngBase64));

            var project = ProjectFileStore.Create("Progetto Giungla");
            BookTypeProfileService.Set(project, BookTypeProfileService.ColoringBook);
            project.EditionMetadata.Title = string.Empty;
            var material = await MaterialImporter.ImportAsync(tempImage);
            project.Materials.Add(material);
            await ProjectFileStore.SaveAsync(tempProject, project);
            SetSession(window, project, tempProject);

            StableWorkflowRootUi.ActivateHome(window);
            materialsList.ItemsSource = null;
            materialsList.SelectedIndex = -1;
            status.Text = "Importati 1 materiali · contract";
            await WaitAsync(window, "home-material-refresh");

            if (materialsList.ItemsSource is not IEnumerable materialItems ||
                !materialItems.Cast<object>().Any(item => (item?.ToString() ?? string.Empty).Contains(material.FileName, StringComparison.Ordinal)))
                throw new InvalidOperationException("Il materiale importato non compare nel box Materiali della Home.");
            if (materialsList.SelectedIndex != 0)
                throw new InvalidOperationException("Il materiale appena importato non viene selezionato nel box Materiali Home.");
            RequireBounds(materialsList, 120, 40, "box Materiali Home");

            var entry = FindHomeEntry(window);
            RequireBounds(entry, 100, 24, "Percorso libro Home");
            await RequirePhysicalHitAsync(window, entry, "Percorso libro Home");
            await WaitAsync(window, "open-book-flow");

            if (!StableWorkflowRootUi.IsWorkflowActive(window))
                throw new InvalidOperationException("Percorso libro non attiva il Workflow stabile.");
            var typePage = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Tipo libro assente nel contract usability.");
            var title = Require<TextBox>(typePage, "DiezBookTitle");
            var titleField = Require<StackPanel>(typePage, "DiezBookTitleField");
            if (!string.Equals(title.Text, project.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Il Titolo del libro non parte dal nome del progetto.");
            if (title.IsReadOnly || !title.IsEnabled || !title.IsHitTestVisible || !title.Focusable)
                throw new InvalidOperationException("Il Titolo del libro iniziale non resta editabile.");
            if (title.TextAlignment != TextAlignment.Left ||
                title.HorizontalAlignment != HorizontalAlignment.Left ||
                titleField.HorizontalAlignment != HorizontalAlignment.Left)
                throw new InvalidOperationException("Il Titolo del libro non è allineato a sinistra con la label.");
            RequireBounds(title, 180, 26, "Titolo del libro");
            await RequirePhysicalHitAsync(window, title, "Titolo del libro");

            var originalTitle = title.Text ?? string.Empty;
            if (!title.Focus() || !title.IsFocused)
                throw new InvalidOperationException("Il Titolo del libro non accetta il focus reale.");
            title.CaretIndex = originalTitle.Length;
            title.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Source = title,
                Text = " X"
            });
            await WaitAsync(window, "book-title-text-input", 80);
            if (!string.Equals(title.Text, originalTitle + " X", StringComparison.Ordinal))
                throw new InvalidOperationException("Il Titolo del libro riceve focus ma non accetta input testuale routed.");

            var homeProject = Descendants(StableWorkflowRootUi.WorkflowRoot(window) ?? throw new InvalidOperationException("Workflow root assente."))
                .OfType<Button>()
                .FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Home progetto", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pulsante Home progetto assente.");
            homeProject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "home-project-click");

            if (StableWorkflowRootUi.IsWorkflowActive(window))
                throw new InvalidOperationException("Home progetto non restituisce ownership alla Home nello stesso click.");
            var homeRoot = StableWorkflowRootUi.HomeRoot(window)
                ?? throw new InvalidOperationException("Home root assente dopo Home progetto.");
            if (!homeRoot.IsEnabled || !homeRoot.IsHitTestVisible || homeRoot.Opacity < 0.9)
                throw new InvalidOperationException("Home progetto torna a una Home non interattiva/visibile.");
            RequireBounds(materialsList, 120, 40, "box Materiali dopo Home progetto");

            entry = FindHomeEntry(window);
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(window, "reopen-book-flow");
            SingleWindowNativeV11Ui.ShowQuantity(window, host);
            await WaitAsync(window, "quantity-page", 420);

            var quantity = pageHost.Content as Control
                ?? throw new InvalidOperationException("Pagina Coloring 1/4 assente.");
            var scroll = quantity as ScrollViewer ?? Descendants(quantity).OfType<ScrollViewer>().FirstOrDefault()
                ?? throw new InvalidOperationException("ScrollViewer Coloring 1/4 assente.");
            RequireBounds(scroll, 180, 100, "ScrollViewer Coloring 1/4");
            if (scroll.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Visible)
                throw new InvalidOperationException("La scrollbar verticale Coloring 1/4 non è resa esplicitamente visibile.");
            if (!scroll.IsEnabled || !scroll.IsHitTestVisible)
                throw new InvalidOperationException("Coloring 1/4 è visibile ma lo ScrollViewer non possiede input.");
            if (scroll.Extent.Height <= scroll.Viewport.Height + 1)
                throw new InvalidOperationException($"Coloring 1/4 non risulta scrollabile: extent={scroll.Extent}, viewport={scroll.Viewport}.");

            var verticalBar = scroll.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && bar.Bounds.Width > 0 && bar.Bounds.Height > 20)
                ?? throw new InvalidOperationException("Coloring 1/4 dichiara scrollbar verticale visibile ma non espone una ScrollBar verticale fisicamente misurata.");
            await RequirePhysicalHitAsync(window, verticalBar, "ScrollBar verticale Coloring 1/4");

            scroll.Offset = new Vector(scroll.Offset.X, 0);
            using (var pointer = new Avalonia.Input.Pointer(0xD1E2, PointerType.Mouse, true))
            {
                var wheel = new PointerWheelEventArgs(
                    scroll,
                    pointer,
                    window,
                    new Point(20, 20),
                    (ulong)Environment.TickCount64,
                    new PointerPointProperties(),
                    KeyModifiers.None,
                    new Vector(0, -1));
                scroll.RaiseEvent(wheel);
            }
            await WaitAsync(window, "quantity-wheel-input", 80);
            if (scroll.Offset.Y < 1)
                throw new InvalidOperationException("Coloring 1/4 espone contenuto oltre il viewport ma una vera rotella routed non sposta lo scroll.");

            await WaitAsync(window, "quantity-image-preview", 500);
            var preview = previewHost.Content as Control
                ?? throw new InvalidOperationException("Preview Coloring 1/4 assente.");
            var image = Descendants(preview).OfType<Image>().FirstOrDefault(i => i.Source is not null)
                ?? throw new InvalidOperationException("L'immagine importata non compare più nell'anteprima Coloring 1/4.");
            RequireBounds(previewHost, 120, 100, "preview host Coloring 1/4");
            if (image.Source is null)
                throw new InvalidOperationException("Anteprima Coloring 1/4 senza sorgente immagine.");

            SafeStartupTrace.Write(
                "user-usability-contract | OK" +
                " | homeMaterials=" + project.Materials.Count +
                " | homeEntryPhysicalHit=true" +
                " | titlePhysicalHit=true" +
                " | titleTextInput=true"+
                " | homeProject=true" +
                " | scrollBarPhysicalHit=true" +
                " | wheelInput=true" +
                " | scrollExtent=" + scroll.Extent +
                " | scrollViewport=" + scroll.Viewport +
                " | previewImage=true");
        }
        finally
        {
            try { SingleWindowEntryPointUi.Invoke(host, "ShowHome"); } catch { }
            try { StableWorkflowRootUi.ActivateHome(window); } catch { }
            try { if (File.Exists(tempProject)) File.Delete(tempProject); } catch { }
            try { if (File.Exists(tempImage)) File.Delete(tempImage); } catch { }
        }
    }

    private static Button FindHomeEntry(MainWindow window)
    {
        var home = StableWorkflowRootUi.HomeRoot(window)
            ?? throw new InvalidOperationException("Home root non disponibile.");
        return Descendants(home).OfType<Button>().FirstOrDefault(b =>
                   string.Equals(b.Name, SingleWindowNativeEntryBridgeUi.NativeEntryName, StringComparison.Ordinal))
               ?? throw new InvalidOperationException("Pulsante Percorso libro stabile non disponibile.");
    }

    private static async Task WaitAsync(MainWindow window, string reason, int delayMs = 180)
    {
        await Task.Delay(delayMs);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        AvaloniaLayoutPumpUi.Execute(window, "contract-" + reason);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void RequireBounds(Control control, double minWidth, double minHeight, string label)
    {
        if (control.Bounds.Width < minWidth || control.Bounds.Height < minHeight)
            throw new InvalidOperationException(
                $"Il controllo '{label}' non partecipa al layout fisico: {control.Bounds.Width:0.##} × {control.Bounds.Height:0.##}.");
    }

    private static async Task RequirePhysicalHitAsync(MainWindow window, Control target, string label)
    {
        var center = new Point(Math.Max(1, target.Bounds.Width / 2), Math.Max(1, target.Bounds.Height / 2));
        var windowPoint = target.TranslatePoint(center, window)
            ?? throw new InvalidOperationException($"Il controllo '{label}' non può tradurre le proprie coordinate verso MainWindow.");
        var hit = window.InputHitTest(windowPoint);
        var hitVisual = hit as Visual;
        var reachesTarget = HitReachesTarget(hitVisual, target);

        SafeStartupTrace.Write(
            "physical-input-hit | target=" + label +
            " | point=" + windowPoint +
            " | hitType=" + (hit?.GetType().FullName ?? "<null>") +
            " | hitName=" + ((hit as Control)?.Name ?? "<unnamed>") +
            " | reachesTarget=" + reachesTarget +
            " | targetBounds=" + target.Bounds);
        SafeStartupTrace.Write(
            "physical-input-hit-path | target=" + label +
            " | hitPath=" + VisualPath(hitVisual) +
            " | targetPath=" + VisualPath(target));

        if (!reachesTarget)
            TracePhysicalHitScan(window, target, label, windowPoint);

        if (!OperatingSystem.IsWindows())
        {
            if (!reachesTarget)
                throw new InvalidOperationException(
                    $"Il punto fisico di '{label}' viene intercettato da '{hit?.GetType().FullName ?? "<null>"}' invece del controllo atteso.");
            return;
        }

        var routedPressed = false;
        target.AddHandler(InputElement.PointerPressedEvent, OnPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        GetCursorPos(out var previousCursor);
        try
        {
            var screenPoint = window.PointToScreen(windowPoint);
            var handle = window.TryGetPlatformHandle();
            if (handle is not null && string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
                SetForegroundWindow(handle.Handle);
            if (!SetCursorPos(screenPoint.X, screenPoint.Y))
                throw new InvalidOperationException($"Non riesco a posizionare il puntatore Win32 su '{label}'.");

            var inputs = new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
            };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
                throw new InvalidOperationException($"SendInput ha inviato {sent}/{inputs.Length} eventi per '{label}' (Win32={Marshal.GetLastWin32Error()}).");

            await WaitAsync(window, "native-pointer-" + label.Replace(' ', '-'), 100);
            SafeStartupTrace.Write(
                "physical-native-pointer | target=" + label +
                " | screenPoint=" + screenPoint +
                " | routedPressed=" + routedPressed +
                " | programmaticHit=" + reachesTarget);

            if (!routedPressed)
                throw new InvalidOperationException(
                    $"Il click Win32 reale sul centro di '{label}' non attraversa il controllo target.");
        }
        finally
        {
            target.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            SetCursorPos(previousCursor.X, previousCursor.Y);
        }

        void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            routedPressed = true;
            SafeStartupTrace.Write(
                "physical-native-pointer | target=" + label +
                " | event=pointer-pressed" +
                " | source=" + (e.Source?.GetType().FullName ?? "<null>") +
                " | targetFocused=" + target.IsFocused);
        }
    }

    private static void TracePhysicalHitScan(MainWindow window, Control target, string label, Point expectedPoint)
    {
        const double radiusX = 320;
        const double radiusY = 220;
        const double step = 10;
        var minX = Math.Max(0, expectedPoint.X - radiusX);
        var maxX = Math.Min(window.ClientSize.Width, expectedPoint.X + radiusX);
        var minY = Math.Max(0, expectedPoint.Y - radiusY);
        var maxY = Math.Min(window.ClientSize.Height, expectedPoint.Y + radiusY);
        var probes = 0;

        for (var y = minY; y <= maxY; y += step)
        {
            for (var x = minX; x <= maxX; x += step)
            {
                probes++;
                var probePoint = new Point(x, y);
                var probeHit = window.InputHitTest(probePoint);
                var probeVisual = probeHit as Visual;
                if (!HitReachesTarget(probeVisual, target)) continue;

                SafeStartupTrace.Write(
                    "physical-input-hit-scan | target=" + label +
                    " | found=true" +
                    " | expectedPoint=" + expectedPoint +
                    " | foundPoint=" + probePoint +
                    " | delta=" + new Vector(probePoint.X - expectedPoint.X, probePoint.Y - expectedPoint.Y) +
                    " | probes=" + probes +
                    " | hitType=" + (probeHit?.GetType().FullName ?? "<null>") +
                    " | hitName=" + ((probeHit as Control)?.Name ?? "<unnamed>"));
                return;
            }
        }

        SafeStartupTrace.Write(
            "physical-input-hit-scan | target=" + label +
            " | found=false" +
            " | expectedPoint=" + expectedPoint +
            " | area=" + new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)) +
            " | step=" + step +
            " | probes=" + probes);
    }

    private static bool HitReachesTarget(Visual? hitVisual, Control target) =>
        hitVisual is not null &&
        (ReferenceEquals(hitVisual, target) || hitVisual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target)));

    private static string VisualPath(Visual? visual)
    {
        if (visual is null) return "<null>";
        return string.Join(" > ", new[] { visual }.Concat(visual.GetVisualAncestors()).Take(18).Select(DescribeVisual));
    }

    private static string DescribeVisual(Visual visual)
    {
        if (visual is not Control control) return visual.GetType().Name;
        return control.GetType().Name +
               "[name=" + (control.Name ?? "-") +
               ",bounds=" + control.Bounds +
               ",hit=" + control.IsHitTestVisible +
               ",enabled=" + control.IsEnabled +
               ",visible=" + control.IsVisible +
               ",z=" + control.ZIndex +
               ",opacity=" + control.Opacity.ToString("0.##") + "]";
    }

    private static T Require<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Controllo {name} mancante.");

    private static void SetSession(MainWindow window, PreviewProject project, string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_project", flags)?.SetValue(window, project);
        typeof(MainWindow).GetField("_currentProjectPath", flags)?.SetValue(window, path);
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
                case ScrollViewer viewer when viewer.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl content when content.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
