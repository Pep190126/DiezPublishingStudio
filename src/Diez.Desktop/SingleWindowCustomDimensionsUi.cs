using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// Gives custom physical dimensions and custom pixel resolution their own explicit
/// numeric controls. The legacy TextBoxes remain the model bridge for the existing
/// ImageSpecs logic, but are hidden inside their old compact rows to avoid duplicate
/// or clipped inputs.
/// </summary>
internal static class SingleWindowCustomDimensionsUi
{
    private const string MarkerName = "DiezExplicitCustomDimensions";
    private static readonly HashSet<MainWindow> Attached = [];

    public static void Attach(MainWindow window)
    {
        if (!Attached.Add(window)) return;
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = PageHost(host);
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                Dispatcher.UIThread.Post(() => EnsureCurrentPage(window), DispatcherPriority.Background);
        };
        window.Closed += (_, _) => Attached.Remove(window);
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        object host;
        try { host = SingleWindowEntryPointUi.GetHost(window); }
        catch { return; }
        if (PageHost(host).Content is not Control page) return;
        if (Descendants(page).Any(c => string.Equals(c.Name, MarkerName, StringComparison.Ordinal))) return;

        var specs = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            string.Equals(p.Name, "DiezImageSpecsPanel", StringComparison.Ordinal));
        if (specs is null) return;

        var preset = Find<ComboBox>(specs, "ImageSpecPreset");
        var width = Find<TextBox>(specs, "ImageSpecWidth");
        var height = Find<TextBox>(specs, "ImageSpecHeight");
        var unit = Find<ComboBox>(specs, "ImageSpecUnit");
        var resolution = Find<ComboBox>(specs, "ImageSpecResolutionClass");
        var pixelWidth = Find<TextBox>(specs, "ImageSpecPixelWidth");
        var pixelHeight = Find<TextBox>(specs, "ImageSpecPixelHeight");
        var dpi = Find<TextBox>(specs, "ImageSpecDpi");
        if (preset is null || width is null || height is null || unit is null || resolution is null ||
            pixelWidth is null || pixelHeight is null || dpi is null) return;

        var oldPhysicalRow = FindHorizontalRow(specs, width, height);
        if (oldPhysicalRow is not null) oldPhysicalRow.IsVisible = false;
        var oldPixelRow = FindHorizontalRow(specs, pixelWidth, pixelHeight);
        if (oldPixelRow is not null) oldPixelRow.IsVisible = false;

        var customWidth = Number("CustomImageSpecWidth", Parse(width.Text, 8.5m), 0.01m, 10000m, 0.1m, "0.##", 150);
        var customHeight = Number("CustomImageSpecHeight", Parse(height.Text, 11m), 0.01m, 10000m, 0.1m, "0.##", 150);
        var customUnit = new ComboBox
        {
            Name = "CustomImageSpecUnit",
            ItemsSource = new[] { "in", "mm" },
            SelectedItem = unit.SelectedItem?.ToString() ?? "in",
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var physicalSummary = new TextBlock
        {
            Name = "CustomImageSpecPhysicalSummary",
            TextWrapping = TextWrapping.Wrap
        };
        var physicalInputs = new StackPanel
        {
            Name = "CustomImageSpecPhysicalInputs",
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Dimensioni personalizzate — inserisci valori numerici", FontSize = 15 },
                Labeled("Larghezza", customWidth),
                Labeled("Altezza", customHeight),
                Labeled("Unità", customUnit)
            }
        };
        var physicalPanel = Card("Dimensioni pagina", physicalSummary, physicalInputs);
        physicalPanel.Name = MarkerName;

        var customPixelWidth = Number("CustomImageSpecPixelWidth", Parse(pixelWidth.Text, 2550m), 1m, 100000m, 1m, "0", 170);
        var customPixelHeight = Number("CustomImageSpecPixelHeight", Parse(pixelHeight.Text, 3300m), 1m, 100000m, 1m, "0", 170);
        var customDpi = Number("CustomImageSpecDpi", Parse(dpi.Text, 300m), 36m, 2400m, 1m, "0", 150);
        var pixelSummary = new TextBlock
        {
            Name = "CustomImageSpecPixelSummary",
            TextWrapping = TextWrapping.Wrap
        };
        var pixelInputs = new StackPanel
        {
            Name = "CustomImageSpecPixelInputs",
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Risoluzione personalizzata — inserisci i pixel", FontSize = 15 },
                Labeled("Larghezza px", customPixelWidth),
                Labeled("Altezza px", customPixelHeight)
            }
        };
        var resolutionPanel = Card("Risoluzione e DPI", pixelSummary,
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    pixelInputs,
                    Labeled("DPI destinazione stampa", customDpi)
                }
            });
        resolutionPanel.Name = "DiezExplicitCustomResolution";

        var presetIndex = DirectChildIndexContaining(specs, preset);
        specs.Children.Insert(Math.Max(0, presetIndex + 1), physicalPanel);
        var resolutionIndex = DirectChildIndexContaining(specs, resolution);
        specs.Children.Insert(Math.Max(0, resolutionIndex + 1), resolutionPanel);

        var syncing = false;

        void PullFromLegacy()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                customWidth.Value = Parse(width.Text, customWidth.Value ?? 8.5m);
                customHeight.Value = Parse(height.Text, customHeight.Value ?? 11m);
                customUnit.SelectedItem = unit.SelectedItem?.ToString() ?? "in";
                customPixelWidth.Value = Parse(pixelWidth.Text, customPixelWidth.Value ?? 2550m);
                customPixelHeight.Value = Parse(pixelHeight.Text, customPixelHeight.Value ?? 3300m);
                customDpi.Value = Parse(dpi.Text, customDpi.Value ?? 300m);

                var isCustomPhysical = string.Equals(preset.SelectedItem?.ToString(), "Personalizzato", StringComparison.OrdinalIgnoreCase);
                var resolutionLabel = resolution.SelectedItem?.ToString() ?? string.Empty;
                var isCustomPixels = resolutionLabel.StartsWith("Personalizzata", StringComparison.OrdinalIgnoreCase);
                physicalInputs.IsVisible = isCustomPhysical;
                pixelInputs.IsVisible = isCustomPixels;
                physicalSummary.Text = isCustomPhysical
                    ? "Formato Personalizzato: specifica larghezza, altezza e unità qui sotto."
                    : $"Preset: {preset.SelectedItem} · {width.Text} × {height.Text} {unit.SelectedItem}.";
                pixelSummary.Text = isCustomPixels
                    ? "Classe Personalizzata: specifica larghezza e altezza in pixel qui sotto."
                    : $"Risoluzione effettiva: {pixelWidth.Text} × {pixelHeight.Text} px · {dpi.Text} DPI.";
            }
            finally { syncing = false; }
        }

        void PushPhysical()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                width.Text = Format(customWidth.Value, width.Text);
                height.Text = Format(customHeight.Value, height.Text);
                unit.SelectedItem = customUnit.SelectedItem?.ToString() ?? "in";
            }
            finally { syncing = false; }
            PullFromLegacy();
        }

        void PushPixels()
        {
            if (syncing) return;
            syncing = true;
            try
            {
                pixelWidth.Text = Format(customPixelWidth.Value, pixelWidth.Text, 0);
                pixelHeight.Text = Format(customPixelHeight.Value, pixelHeight.Text, 0);
            }
            finally { syncing = false; }
            PullFromLegacy();
        }

        void PushDpi()
        {
            if (syncing) return;
            syncing = true;
            try { dpi.Text = Format(customDpi.Value, dpi.Text, 0); }
            finally { syncing = false; }
            PullFromLegacy();
        }

        customWidth.ValueChanged += (_, _) => PushPhysical();
        customHeight.ValueChanged += (_, _) => PushPhysical();
        customUnit.SelectionChanged += (_, _) => PushPhysical();
        customPixelWidth.ValueChanged += (_, _) => PushPixels();
        customPixelHeight.ValueChanged += (_, _) => PushPixels();
        customDpi.ValueChanged += (_, _) => PushDpi();

        width.TextChanged += (_, _) => PullFromLegacy();
        height.TextChanged += (_, _) => PullFromLegacy();
        unit.SelectionChanged += (_, _) => PullFromLegacy();
        pixelWidth.TextChanged += (_, _) => PullFromLegacy();
        pixelHeight.TextChanged += (_, _) => PullFromLegacy();
        dpi.TextChanged += (_, _) => PullFromLegacy();
        preset.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(PullFromLegacy, DispatcherPriority.Background);
        resolution.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(PullFromLegacy, DispatcherPriority.Background);

        PullFromLegacy();
        SingleWindowVisibleInputsUi.Apply(window);
    }

    private static Border Card(string title, params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17 });
        foreach (var child in children) stack.Children.Add(child);
        return new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            Child = stack
        };
    }

    private static NumericUpDown Number(string name, decimal value, decimal min, decimal max, decimal step, string format, double width) => new()
    {
        Name = name,
        Value = value,
        Minimum = min,
        Maximum = max,
        Increment = step,
        FormatString = format,
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left,
        MinHeight = 38
    };

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control }
    };

    private static decimal Parse(string? text, decimal fallback)
    {
        var value = (text ?? string.Empty).Trim().Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static string Format(decimal? value, string? fallback, int decimals = 2)
    {
        if (!value.HasValue) return fallback ?? string.Empty;
        return decimals == 0
            ? decimal.Round(value.Value, 0).ToString("0", CultureInfo.InvariantCulture)
            : value.Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static T? Find<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

    private static StackPanel? FindHorizontalRow(Control root, params Control[] targets) =>
        Descendants(root).OfType<StackPanel>()
            .Where(p => p.Orientation == Orientation.Horizontal)
            .FirstOrDefault(p => targets.All(t => Descendants(p).Contains(t)));

    private static int DirectChildIndexContaining(StackPanel parent, Control target)
    {
        for (var i = 0; i < parent.Children.Count; i++)
            if (Descendants(parent.Children[i]).Contains(target)) return i;
        return parent.Children.Count - 1;
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
