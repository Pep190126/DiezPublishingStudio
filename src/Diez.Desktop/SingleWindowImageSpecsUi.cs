using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Production controls for image-based books. The UI is added to the logical
/// Quantity page and the resulting block is injected into every generated prompt.
/// </summary>
internal static class SingleWindowImageSpecsUi
{
    private const string PanelName = "DiezImageSpecsPanel";
    private const string EntityKind = "DiezImageGenerationSpecs";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly PagePreset[] Presets =
    [
        new("letter", "US Letter — 8.5 × 11 in", "8.5", "11", "in", "17:22", "2550", "3300"),
        new("a4", "A4 — 210 × 297 mm", "210", "297", "mm", "210:297", "2480", "3508"),
        new("a5", "A5 — 148 × 210 mm", "148", "210", "mm", "148:210", "1748", "2480"),
        new("square", "Quadrato — 8.5 × 8.5 in", "8.5", "8.5", "in", "1:1", "2550", "2550"),
        new("custom", "Personalizzato", "8.5", "11", "in", "17:22", "2550", "3300")
    ];

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;

        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
                EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page || !TrySession(window, out var project, out var path)) return;

        var texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        if (texts.Any(t => t.Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            EnsureQuantitySpecs(page, project, path);
        else if (texts.Any(t => string.Equals(t, "PROMPT — modificabile", StringComparison.Ordinal)))
            EnsurePromptInjection(page, project);
    }

    internal static string BuildPromptBlock(PreviewProject project)
    {
        var s = Load(project);
        var sb = new StringBuilder();
        sb.AppendLine("SPECIFICHE TECNICHE:");
        sb.AppendLine($"- Formato pagina: {PresetLabel(s.PresetId)}.");
        sb.AppendLine($"- Dimensioni finali: {s.Width} × {s.Height} {s.Unit}.");
        sb.AppendLine($"- Orientamento: {s.Orientation}.");
        sb.AppendLine($"- Aspect ratio: {s.AspectRatio}.");
        sb.AppendLine($"- Risoluzione target: {s.PixelWidth} × {s.PixelHeight} px.");
        sb.AppendLine($"- DPI di destinazione per stampa: {s.Dpi} DPI.");
        sb.AppendLine($"- Qualità: {s.Quality}.");
        sb.AppendLine($"- Tratto / dettaglio: {s.LineDetail}.");
        sb.AppendLine($"- Margine di sicurezza: {s.SafeMargin} {s.Unit}.");
        if (s.Bleed)
            sb.AppendLine($"- Bleed / abbondanza: {s.BleedAmount} {s.Unit} per lato.");
        else
            sb.AppendLine("- Bleed / abbondanza: nessuno.");
        sb.AppendLine("- Output Coloring Book: line art nero pulito su fondo bianco, senza testo, watermark, numeri, cornici tecniche o nomi file nell'immagine.");
        sb.AppendLine("- Evita grigi, ombreggiature e riempimenti pieni salvo richiesta esplicita nel prompt.");
        sb.AppendLine("- Mantieni gli elementi importanti entro il margine di sicurezza; il DPI è un requisito di output/stampa, non testo da disegnare nell'immagine.");
        return sb.ToString().Trim();
    }

    private static void EnsureQuantitySpecs(Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("Coloring Book — quantità", StringComparison.Ordinal)));
        if (root is null) return;

        var s = Load(project);
        var preset = new ComboBox { Name = "ImageSpecPreset", ItemsSource = Presets, Width = 310, HorizontalAlignment = HorizontalAlignment.Left };
        preset.SelectedItem = Presets.FirstOrDefault(p => p.Id == s.PresetId) ?? Presets[0];
        var width = SmallEditor(s.Width, 90);
        var height = SmallEditor(s.Height, 90);
        var unit = new ComboBox { ItemsSource = new[] { "in", "mm" }, SelectedItem = s.Unit, Width = 80 };
        var orientation = new ComboBox { Name = "ImageSpecOrientation", ItemsSource = new[] { "Verticale", "Orizzontale", "Quadrata" }, SelectedItem = s.Orientation, Width = 160 };
        var ratio = SmallEditor(s.AspectRatio, 110); ratio.Name = "ImageSpecAspectRatio";
        var pxW = SmallEditor(s.PixelWidth, 105); pxW.Name = "ImageSpecPixelWidth";
        var pxH = SmallEditor(s.PixelHeight, 105); pxH.Name = "ImageSpecPixelHeight";
        var dpi = SmallEditor(s.Dpi, 90); dpi.Name = "ImageSpecDpi";
        var quality = new ComboBox { Name = "ImageSpecQuality", ItemsSource = new[] { "Standard", "Alta", "Massima / stampa" }, SelectedItem = s.Quality, Width = 190 };
        var line = new ComboBox { Name = "ImageSpecLineDetail", ItemsSource = new[] { "Linee semplici e pulite", "Dettaglio medio", "Dettaglio alto ma colorabile" }, SelectedItem = s.LineDetail, Width = 260 };
        var safe = SmallEditor(s.SafeMargin, 90); safe.Name = "ImageSpecSafeMargin";
        var bleed = new CheckBox { Name = "ImageSpecBleed", Content = "Bleed / abbondanza", IsChecked = s.Bleed };
        var bleedAmount = SmallEditor(s.BleedAmount, 90); bleedAmount.Name = "ImageSpecBleedAmount"; bleedAmount.IsVisible = s.Bleed;

        void FromControls()
        {
            s.PresetId = (preset.SelectedItem as PagePreset)?.Id ?? "custom";
            s.Width = width.Text?.Trim() ?? s.Width;
            s.Height = height.Text?.Trim() ?? s.Height;
            s.Unit = unit.SelectedItem?.ToString() ?? s.Unit;
            s.Orientation = orientation.SelectedItem?.ToString() ?? s.Orientation;
            s.AspectRatio = ratio.Text?.Trim() ?? s.AspectRatio;
            s.PixelWidth = pxW.Text?.Trim() ?? s.PixelWidth;
            s.PixelHeight = pxH.Text?.Trim() ?? s.PixelHeight;
            s.Dpi = dpi.Text?.Trim() ?? s.Dpi;
            s.Quality = quality.SelectedItem?.ToString() ?? s.Quality;
            s.LineDetail = line.SelectedItem?.ToString() ?? s.LineDetail;
            s.SafeMargin = safe.Text?.Trim() ?? s.SafeMargin;
            s.Bleed = bleed.IsChecked == true;
            s.BleedAmount = bleedAmount.Text?.Trim() ?? s.BleedAmount;
            Save(project, s);
        }

        void ApplyPreset(PagePreset p)
        {
            if (p.Id == "custom") return;
            width.Text = p.Width; height.Text = p.Height; unit.SelectedItem = p.Unit;
            ratio.Text = p.Ratio; pxW.Text = p.PixelWidth; pxH.Text = p.PixelHeight;
            orientation.SelectedItem = p.Width == p.Height ? "Quadrata" : "Verticale";
            FromControls();
        }

        preset.SelectionChanged += (_, _) => { if (preset.SelectedItem is PagePreset p) ApplyPreset(p); };
        width.TextChanged += (_, _) => FromControls();
        height.TextChanged += (_, _) => FromControls();
        unit.SelectionChanged += (_, _) => FromControls();
        orientation.SelectionChanged += (_, _) => FromControls();
        ratio.TextChanged += (_, _) => FromControls();
        pxW.TextChanged += (_, _) => FromControls();
        pxH.TextChanged += (_, _) => FromControls();
        dpi.TextChanged += (_, _) => FromControls();
        quality.SelectionChanged += (_, _) => FromControls();
        line.SelectionChanged += (_, _) => FromControls();
        safe.TextChanged += (_, _) => FromControls();
        bleed.IsCheckedChanged += (_, _) => { bleedAmount.IsVisible = bleed.IsChecked == true; FromControls(); };
        bleedAmount.TextChanged += (_, _) => FromControls();

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Specifiche immagine / stampa", FontSize = 19 },
                new TextBlock { Text = "Questi valori entrano automaticamente nel prompt e nel Prompt Pack.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                Labeled("Formato pagina", preset),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Larghezza", width), Labeled("Altezza", height), Labeled("Unità", unit) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Orientamento", orientation), Labeled("Aspect ratio", ratio) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Larghezza px", pxW), Labeled("Altezza px", pxH), Labeled("DPI", dpi) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Qualità", quality), Labeled("Tratto / dettaglio", line) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Margine sicurezza", safe), bleed, bleedAmount } }
            }
        };

        var actions = root.Children.OfType<StackPanel>().LastOrDefault(p => p.Orientation == Orientation.Horizontal && p.Children.OfType<Button>().Any());
        var index = actions is null ? root.Children.Count : root.Children.IndexOf(actions);
        root.Children.Insert(Math.Max(0, index), panel);

        if (actions is not null)
        {
            var next = actions.Children.OfType<Button>().FirstOrDefault(b => (b.Content?.ToString() ?? string.Empty).Contains("Avanti", StringComparison.OrdinalIgnoreCase));
            if (next is not null)
                next.Click += async (_, _) => { FromControls(); await ProjectFileStore.SaveAsync(path, project); };
        }
    }

    private static void EnsurePromptInjection(Control page, PreviewProject project)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, "DiezImageSpecsPromptMarker", StringComparison.Ordinal))) return;
        var editors = Descendants(page).OfType<TextBox>().Where(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly).ToList();
        if (editors.Count < 3) return;
        var prompt = editors[2];
        var internalChange = false;

        void EnsureBlock()
        {
            if (internalChange) return;
            var text = prompt.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || text.Contains("SPECIFICHE TECNICHE:", StringComparison.Ordinal)) return;
            if (!text.Contains("Coloring Book", StringComparison.OrdinalIgnoreCase)) return;
            internalChange = true;
            prompt.Text = text.TrimEnd() + Environment.NewLine + Environment.NewLine + BuildPromptBlock(project);
            internalChange = false;
        }

        prompt.TextChanged += (_, _) => EnsureBlock();
        var buttons = Descendants(page).OfType<Button>().ToList();
        foreach (var button in buttons.Where(b => (b.Content?.ToString() ?? string.Empty).StartsWith("Prepara prompt", StringComparison.OrdinalIgnoreCase)))
            button.Click += (_, _) => EnsureBlock();

        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(prompt));
        var marker = new TextBlock
        {
            Name = "DiezImageSpecsPromptMarker",
            Text = "Formato, aspect ratio, pixel, DPI, qualità, margini e bleed vengono inclusi automaticamente nel prompt.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        if (root is not null)
        {
            var index = root.Children.IndexOf(prompt);
            root.Children.Insert(Math.Min(root.Children.Count, index + 1), marker);
        }
    }

    private static ImageSpecs Load(PreviewProject project)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is not null && !string.IsNullOrWhiteSpace(entity.Notes))
        {
            try { return JsonSerializer.Deserialize<ImageSpecs>(entity.Notes, JsonOptions) ?? Default(); }
            catch { }
        }
        return Default();
    }

    private static void Save(PreviewProject project, ImageSpecs settings)
    {
        var entity = project.Entities.FirstOrDefault(e => string.Equals(e.Kind, EntityKind, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            entity = new GraphEntity { Kind = EntityKind, Name = "Specifiche immagini", IsCandidate = false };
            project.Entities.Add(entity);
        }
        entity.IsCandidate = false;
        entity.Notes = JsonSerializer.Serialize(settings, JsonOptions);
    }

    private static ImageSpecs Default() => new();
    private static string PresetLabel(string id) => Presets.FirstOrDefault(p => p.Id == id)?.Label ?? "Personalizzato";

    private static TextBox SmallEditor(string value, double width) => new()
    {
        Text = value, Width = width, Height = 36, IsReadOnly = false, IsEnabled = true, IsUndoEnabled = true,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static StackPanel Labeled(string label, Control control) => new()
    {
        Spacing = 3,
        Children = { new TextBlock { Text = label, FontSize = 13 }, control }
    };

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
                case Border b when b.Child is Control child:
                    stack.Push(child);
                    break;
                case ScrollViewer s when s.Content is Control child:
                    stack.Push(child);
                    break;
                case ContentControl c when c.Content is Control child:
                    stack.Push(child);
                    break;
            }
        }
    }

    private sealed class ImageSpecs
    {
        public string PresetId { get; set; } = "letter";
        public string Width { get; set; } = "8.5";
        public string Height { get; set; } = "11";
        public string Unit { get; set; } = "in";
        public string Orientation { get; set; } = "Verticale";
        public string AspectRatio { get; set; } = "17:22";
        public string PixelWidth { get; set; } = "2550";
        public string PixelHeight { get; set; } = "3300";
        public string Dpi { get; set; } = "300";
        public string Quality { get; set; } = "Alta";
        public string LineDetail { get; set; } = "Dettaglio medio";
        public string SafeMargin { get; set; } = "0.25";
        public bool Bleed { get; set; }
        public string BleedAmount { get; set; } = "0.125";
    }

    private sealed record PagePreset(string Id, string Label, string Width, string Height, string Unit, string Ratio, string PixelWidth, string PixelHeight)
    {
        public override string ToString() => Label;
    }
}
