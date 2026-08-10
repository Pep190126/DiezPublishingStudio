using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Production controls shared by Coloring, Image Collection and Illustrated Book.
/// Physical size/DPI, aspect ratio and a screen-resolution quality class are deliberately
/// separate: HD/FHD/2K/4K/8K preserve the selected book aspect ratio instead of forcing 16:9.
/// Page presets use Amazon KDP trim sizes. Bleed is intentionally not part of the AI-generation
/// instructions: it belongs to the later layout/print preparation stage.
/// </summary>
internal static class SingleWindowImageSpecsUi
{
    private const string PanelName = "DiezImageSpecsPanel";
    private const string EntityKind = "DiezImageGenerationSpecs";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly PagePreset[] Presets =
    [
        new("kdp_5x8", "KDP — 5 × 8 in", "5", "8", "in", "5:8", "1500", "2400"),
        new("kdp_5_06x7_81", "KDP — 5.06 × 7.81 in", "5.06", "7.81", "in", "5.06:7.81", "1518", "2343"),
        new("kdp_5_25x8", "KDP — 5.25 × 8 in", "5.25", "8", "in", "5.25:8", "1575", "2400"),
        new("kdp_5_5x8_5", "KDP — 5.5 × 8.5 in", "5.5", "8.5", "in", "5.5:8.5", "1650", "2550"),
        new("kdp_6x9", "KDP — 6 × 9 in", "6", "9", "in", "2:3", "1800", "2700"),
        new("kdp_6_14x9_21", "KDP — 6.14 × 9.21 in", "6.14", "9.21", "in", "6.14:9.21", "1842", "2763"),
        new("kdp_6_69x9_61", "KDP — 6.69 × 9.61 in", "6.69", "9.61", "in", "6.69:9.61", "2007", "2883"),
        new("kdp_7x10", "KDP — 7 × 10 in", "7", "10", "in", "7:10", "2100", "3000"),
        new("kdp_7_44x9_69", "KDP — 7.44 × 9.69 in", "7.44", "9.69", "in", "7.44:9.69", "2232", "2907"),
        new("kdp_7_5x9_25", "KDP — 7.5 × 9.25 in", "7.5", "9.25", "in", "7.5:9.25", "2250", "2775"),
        new("kdp_8x10", "KDP — 8 × 10 in", "8", "10", "in", "4:5", "2400", "3000"),
        new("kdp_8_25x6", "KDP — 8.25 × 6 in (orizzontale)", "8.25", "6", "in", "11:8", "2475", "1800"),
        new("kdp_8_25_square", "KDP — 8.25 × 8.25 in", "8.25", "8.25", "in", "1:1", "2475", "2475"),
        new("kdp_8_5_square", "KDP — 8.5 × 8.5 in", "8.5", "8.5", "in", "1:1", "2550", "2550"),
        new("kdp_letter", "KDP — 8.5 × 11 in", "8.5", "11", "in", "17:22", "2550", "3300"),
        new("kdp_a4", "KDP — 8.27 × 11.69 in / A4", "8.27", "11.69", "in", "8.27:11.69", "2481", "3507"),
        new("custom", "Personalizzato", "8.5", "11", "in", "17:22", "2550", "3300")
    ];

    private static readonly ResolutionClass[] ResolutionClasses =
    [
        new("hd", "HD — lato lungo 1280 px", 1280),
        new("fhd", "Full HD — lato lungo 1920 px", 1920),
        new("2k", "2K — lato lungo 2560 px", 2560),
        new("4k", "4K UHD — lato lungo 3840 px", 3840),
        new("8k", "8K UHD — lato lungo 7680 px", 7680),
        new("print300", "Stampa — dimensioni fisiche × DPI", 0),
        new("custom", "Personalizzata — usa i pixel indicati", -1)
    ];

    public static void Attach(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost is null) return;
        pageHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) EnsureCurrentPage(window);
        };
        EnsureCurrentPage(window);
    }

    internal static void EnsureCurrentPage(MainWindow window)
    {
        var host = SingleWindowEntryPointUi.GetHost(window);
        var pageHost = host.GetType().GetField("_pageHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host) as ContentControl;
        if (pageHost?.Content is not Control page || !TrySession(window, out var project, out var path)) return;
        if (!BookTypeProfileService.IsImageCollection(project)) return;

        var texts = Descendants(page).OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        if (texts.Any(t => t.Contains("Quante immagini vuoi creare?", StringComparison.Ordinal)))
            EnsureQuantitySpecs(page, project, path);
        else if (texts.Any(t => string.Equals(t, "PROMPT — modificabile", StringComparison.Ordinal)))
            EnsurePromptInjection(page, project);
    }

    internal static string BuildPromptBlock(PreviewProject project)
    {
        var s = Load(project);
        var type = BookTypeProfileService.Get(project);
        var coloring = string.Equals(type, BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var resolutionLabel = ResolutionClasses.FirstOrDefault(x => x.Id == s.ResolutionClassId)?.Label ?? "Personalizzata";

        var sb = new StringBuilder();
        sb.AppendLine("SPECIFICHE TECNICHE:");
        sb.AppendLine($"- Tipo libro / uso immagini: {type}.");
        sb.AppendLine($"- Formato pagina / trim finale: {PresetLabel(s.PresetId)}.");
        sb.AppendLine($"- Dimensioni finali: {s.Width} × {s.Height} {s.Unit}.");
        sb.AppendLine($"- Orientamento: {s.Orientation}.");
        sb.AppendLine($"- Aspect ratio: {s.AspectRatio}.");
        sb.AppendLine($"- Classe risoluzione / qualità immagine: {resolutionLabel}.");
        sb.AppendLine($"- Risoluzione target effettiva: {s.PixelWidth} × {s.PixelHeight} px; preserva questo aspect ratio e non deformare il soggetto.");
        sb.AppendLine($"- DPI di destinazione per stampa: {s.Dpi} DPI. I DPI sono separati dalla classe HD/FHD/2K/4K/8K.");
        sb.AppendLine($"- Qualità rendering: {s.Quality}.");
        sb.AppendLine($"- Livello tecnico di dettaglio: {s.LineDetail}.");
        sb.AppendLine($"- Margine di sicurezza creativo: {s.SafeMargin} {s.Unit}.");

        if (coloring)
        {
            sb.AppendLine("- Output Coloring Book: line art binaria con ESATTAMENTE due colori: nero puro #000000 su fondo bianco puro #FFFFFF.");
            sb.AppendLine("- Vietati senza eccezioni: grigi, mezzetinte, colori, ombre, sfumature, gradienti e qualunque valore cromatico intermedio.");
        }
        else
        {
            sb.AppendLine("- Resa cromatica: applica la modalità scelta nel profilo illustrazioni (colore, scala di grigi, B/N puro, monocromatico o automatico). Non sovrascriverla con regole del Coloring Book.");
        }

        sb.AppendLine("- Evita testo tecnico, watermark, ID e nomi file dentro l'immagine salvo richiesta editoriale esplicita.");
        sb.AppendLine("- Mantieni gli elementi importanti entro il margine di sicurezza; risoluzione e DPI sono requisiti di output, non testo da disegnare nell'immagine.");
        return sb.ToString().Trim();
    }

    private static void EnsureQuantitySpecs(Control page, PreviewProject project, string path)
    {
        if (Descendants(page).Any(c => string.Equals(c.Name, PanelName, StringComparison.Ordinal))) return;
        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p =>
            p.Children.OfType<TextBlock>().Any(t => (t.Text ?? string.Empty).Contains("quantità", StringComparison.OrdinalIgnoreCase)));
        if (root is null) return;

        var coloring = string.Equals(BookTypeProfileService.Get(project), BookTypeProfileService.ColoringBook, StringComparison.OrdinalIgnoreCase);
        var s = Load(project);
        NormalizeResolutionClass(s);
        NormalizeLegacyPreset(s);

        var preset = new ComboBox { Name = "ImageSpecPreset", ItemsSource = Presets, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        preset.SelectedItem = Presets.FirstOrDefault(p => p.Id == s.PresetId) ?? Presets.First(p => p.Id == "kdp_letter");
        var width = SmallEditor(s.Width, 90); width.Name = "ImageSpecWidth";
        var height = SmallEditor(s.Height, 90); height.Name = "ImageSpecHeight";
        var unit = new ComboBox { Name = "ImageSpecUnit", ItemsSource = new[] { "in", "mm" }, SelectedItem = s.Unit, Width = 80 };
        var orientation = new ComboBox { Name = "ImageSpecOrientation", ItemsSource = new[] { "Verticale", "Orizzontale", "Quadrata" }, SelectedItem = s.Orientation, Width = 160 };
        var ratio = SmallEditor(s.AspectRatio, 110); ratio.Name = "ImageSpecAspectRatio";

        var resolutionClass = new ComboBox
        {
            Name = "ImageSpecResolutionClass",
            ItemsSource = ResolutionClasses,
            Width = 330,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        resolutionClass.SelectedItem = ResolutionClasses.FirstOrDefault(x => x.Id == s.ResolutionClassId) ?? ResolutionClasses[5];

        var pxW = SmallEditor(s.PixelWidth, 105); pxW.Name = "ImageSpecPixelWidth";
        var pxH = SmallEditor(s.PixelHeight, 105); pxH.Name = "ImageSpecPixelHeight";
        var dpi = SmallEditor(s.Dpi, 90); dpi.Name = "ImageSpecDpi";
        var quality = new ComboBox { Name = "ImageSpecQuality", ItemsSource = new[] { "Standard", "Alta", "Massima / stampa" }, SelectedItem = s.Quality, Width = 190 };
        var detailChoices = coloring
            ? new[] { "Linee semplici e pulite", "Dettaglio medio", "Dettaglio alto ma colorabile" }
            : new[] { "Molto schematico", "Dettaglio basso", "Dettaglio medio", "Dettaglio alto", "Dettaglio massimo" };
        if (!detailChoices.Contains(s.LineDetail, StringComparer.Ordinal)) s.LineDetail = "Dettaglio medio";
        var line = new ComboBox { Name = "ImageSpecLineDetail", ItemsSource = detailChoices, SelectedItem = s.LineDetail, Width = 260 };
        var safe = SmallEditor(s.SafeMargin, 90); safe.Name = "ImageSpecSafeMargin";
        var applyingResolution = false;

        void FromControls()
        {
            s.PresetId = (preset.SelectedItem as PagePreset)?.Id ?? "custom";
            s.Width = width.Text?.Trim() ?? s.Width;
            s.Height = height.Text?.Trim() ?? s.Height;
            s.Unit = unit.SelectedItem?.ToString() ?? s.Unit;
            s.Orientation = orientation.SelectedItem?.ToString() ?? s.Orientation;
            s.AspectRatio = ratio.Text?.Trim() ?? s.AspectRatio;
            s.ResolutionClassId = (resolutionClass.SelectedItem as ResolutionClass)?.Id ?? "custom";
            s.PixelWidth = pxW.Text?.Trim() ?? s.PixelWidth;
            s.PixelHeight = pxH.Text?.Trim() ?? s.PixelHeight;
            s.Dpi = dpi.Text?.Trim() ?? s.Dpi;
            s.Quality = quality.SelectedItem?.ToString() ?? s.Quality;
            s.LineDetail = line.SelectedItem?.ToString() ?? s.LineDetail;
            s.SafeMargin = safe.Text?.Trim() ?? s.SafeMargin;
            // Bleed remains readable from old project JSON for compatibility, but it is no longer
            // edited here and never enters AI-generation prompts.
            Save(project, s);
        }

        void ApplyResolutionClass()
        {
            if (applyingResolution || resolutionClass.SelectedItem is not ResolutionClass selected || selected.LongSidePixels < 0) return;
            applyingResolution = true;
            try
            {
                var (pixelWidth, pixelHeight) = selected.LongSidePixels == 0
                    ? PixelsFromPhysicalSize(width.Text, height.Text, unit.SelectedItem?.ToString(), dpi.Text)
                    : PixelsFromLongSide(width.Text, height.Text, ratio.Text, selected.LongSidePixels);
                pxW.Text = pixelWidth.ToString(CultureInfo.InvariantCulture);
                pxH.Text = pixelHeight.ToString(CultureInfo.InvariantCulture);
                FromControls();
            }
            finally { applyingResolution = false; }
        }

        void ApplyPreset(PagePreset p)
        {
            if (p.Id == "custom") return;
            width.Text = p.Width;
            height.Text = p.Height;
            unit.SelectedItem = p.Unit;
            ratio.Text = p.Ratio;
            orientation.SelectedItem = p.Width == p.Height ? "Quadrata" : ParsePositive(p.Width, 0) > ParsePositive(p.Height, 0) ? "Orizzontale" : "Verticale";
            if (resolutionClass.SelectedItem is ResolutionClass selected && selected.LongSidePixels >= 0)
                ApplyResolutionClass();
            else
            {
                pxW.Text = p.PixelWidth;
                pxH.Text = p.PixelHeight;
                FromControls();
            }
        }

        preset.SelectionChanged += (_, _) => { if (preset.SelectedItem is PagePreset p) ApplyPreset(p); };
        resolutionClass.SelectionChanged += (_, _) => ApplyResolutionClass();
        width.TextChanged += (_, _) => { if (!applyingResolution) { FromControls(); ApplyResolutionClass(); } };
        height.TextChanged += (_, _) => { if (!applyingResolution) { FromControls(); ApplyResolutionClass(); } };
        unit.SelectionChanged += (_, _) => { FromControls(); ApplyResolutionClass(); };
        orientation.SelectionChanged += (_, _) => FromControls();
        ratio.TextChanged += (_, _) => { if (!applyingResolution) { FromControls(); ApplyResolutionClass(); } };
        pxW.TextChanged += (_, _) =>
        {
            if (applyingResolution) return;
            if ((resolutionClass.SelectedItem as ResolutionClass)?.Id != "custom") resolutionClass.SelectedItem = ResolutionClasses[^1];
            FromControls();
        };
        pxH.TextChanged += (_, _) =>
        {
            if (applyingResolution) return;
            if ((resolutionClass.SelectedItem as ResolutionClass)?.Id != "custom") resolutionClass.SelectedItem = ResolutionClasses[^1];
            FromControls();
        };
        dpi.TextChanged += (_, _) => { FromControls(); if ((resolutionClass.SelectedItem as ResolutionClass)?.Id == "print300") ApplyResolutionClass(); };
        quality.SelectionChanged += (_, _) => FromControls();
        line.SelectionChanged += (_, _) => FromControls();
        safe.TextChanged += (_, _) => FromControls();

        var panel = new StackPanel
        {
            Name = PanelName,
            Spacing = 8,
            Children =
            {
                new Separator(),
                new TextBlock { Text = "Specifiche immagine / stampa", FontSize = 19 },
                new TextBlock
                {
                    Text = "I preset pagina seguono i trim Amazon KDP. Formato, aspect ratio, classe HD/FHD/2K/4K/8K, pixel, DPI, qualità e margine di sicurezza entrano automaticamente nel prompt. Il bleed verrà gestito più avanti nella preparazione di stampa, non dall’AI che crea l’immagine.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                Labeled("Formato pagina / trim KDP", preset),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Larghezza", width), Labeled("Altezza", height), Labeled("Unità", unit) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Orientamento", orientation), Labeled("Aspect ratio", ratio) } },
                Labeled("Qualità / classe risoluzione", resolutionClass),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Larghezza px", pxW), Labeled("Altezza px", pxH), Labeled("DPI", dpi) } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Labeled("Qualità rendering", quality), Labeled("Dettaglio tecnico", line) } },
                Labeled("Margine di sicurezza", safe)
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

    private static (int Width, int Height) PixelsFromLongSide(string? widthText, string? heightText, string? ratioText, int longSide)
    {
        var (w, h) = EffectiveRatio(widthText, heightText, ratioText);
        if (w >= h)
            return (longSide, Math.Max(1, (int)Math.Round(longSide * h / w)));
        return (Math.Max(1, (int)Math.Round(longSide * w / h)), longSide);
    }

    private static (int Width, int Height) PixelsFromPhysicalSize(string? widthText, string? heightText, string? unit, string? dpiText)
    {
        var width = ParsePositive(widthText, 8.5);
        var height = ParsePositive(heightText, 11);
        var dpi = ParsePositive(dpiText, 300);
        var inchesFactor = string.Equals(unit, "mm", StringComparison.OrdinalIgnoreCase) ? 1.0 / 25.4 : 1.0;
        return (
            Math.Max(1, (int)Math.Round(width * inchesFactor * dpi)),
            Math.Max(1, (int)Math.Round(height * inchesFactor * dpi)));
    }

    private static (double Width, double Height) EffectiveRatio(string? widthText, string? heightText, string? ratioText)
    {
        var width = ParsePositive(widthText, 0);
        var height = ParsePositive(heightText, 0);
        if (width > 0 && height > 0) return (width, height);

        var parts = (ratioText ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var rw = ParsePositive(parts[0], 0);
            var rh = ParsePositive(parts[1], 0);
            if (rw > 0 && rh > 0) return (rw, rh);
        }
        return (17, 22);
    }

    private static double ParsePositive(string? text, double fallback)
    {
        var normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
    }

    private static void NormalizeResolutionClass(ImageSpecs s)
    {
        if (ResolutionClasses.Any(x => x.Id == s.ResolutionClassId)) return;
        s.ResolutionClassId = "print300";
    }

    private static void NormalizeLegacyPreset(ImageSpecs s)
    {
        s.PresetId = s.PresetId switch
        {
            "letter" => "kdp_letter",
            "a4" => "kdp_a4",
            "square" => "kdp_8_5_square",
            _ => s.PresetId
        };
        if (!Presets.Any(p => p.Id == s.PresetId)) s.PresetId = "custom";
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
            internalChange = true;
            prompt.Text = text.TrimEnd() + Environment.NewLine + Environment.NewLine + BuildPromptBlock(project);
            internalChange = false;
        }

        prompt.TextChanged += (_, _) => EnsureBlock();
        foreach (var button in Descendants(page).OfType<Button>().Where(b =>
                     (b.Content?.ToString() ?? string.Empty).StartsWith("Prepara prompt", StringComparison.OrdinalIgnoreCase)))
            button.Click += (_, _) => EnsureBlock();

        var root = Descendants(page).OfType<StackPanel>().FirstOrDefault(p => p.Children.Contains(prompt));
        var marker = new TextBlock
        {
            Name = "DiezImageSpecsPromptMarker",
            Text = "Formato/trim KDP, aspect ratio, HD/FHD/2K/4K/8K, pixel, DPI, qualità e margine di sicurezza vengono inclusi automaticamente nel prompt. Il bleed resta fuori dal flusso creativo AI.",
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
            try
            {
                var result = JsonSerializer.Deserialize<ImageSpecs>(entity.Notes, JsonOptions) ?? Default();
                NormalizeResolutionClass(result);
                NormalizeLegacyPreset(result);
                return result;
            }
            catch { }
        }
        return Default();
    }

    private static void Save(PreviewProject project, ImageSpecs settings)
    {
        NormalizeResolutionClass(settings);
        NormalizeLegacyPreset(settings);
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
        Text = value,
        Width = width,
        Height = 36,
        IsReadOnly = false,
        IsEnabled = true,
        IsUndoEnabled = true,
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
                case Border b when b.Child is Control child: stack.Push(child); break;
                case ScrollViewer s when s.Content is Control child: stack.Push(child); break;
                case ContentControl c when c.Content is Control child: stack.Push(child); break;
            }
        }
    }

    private sealed class ImageSpecs
    {
        public string PresetId { get; set; } = "kdp_letter";
        public string Width { get; set; } = "8.5";
        public string Height { get; set; } = "11";
        public string Unit { get; set; } = "in";
        public string Orientation { get; set; } = "Verticale";
        public string AspectRatio { get; set; } = "17:22";
        public string ResolutionClassId { get; set; } = "print300";
        public string PixelWidth { get; set; } = "2550";
        public string PixelHeight { get; set; } = "3300";
        public string Dpi { get; set; } = "300";
        public string Quality { get; set; } = "Alta";
        public string LineDetail { get; set; } = "Dettaglio medio";
        public string SafeMargin { get; set; } = "0.25";
        // Retained only to deserialize older .diez projects. Not exposed to AI creation UI.
        public bool Bleed { get; set; }
        public string BleedAmount { get; set; } = "0.125";
    }

    private sealed record PagePreset(string Id, string Label, string Width, string Height, string Unit, string Ratio, string PixelWidth, string PixelHeight)
    {
        public override string ToString() => Label;
    }

    private sealed record ResolutionClass(string Id, string Label, int LongSidePixels)
    {
        public override string ToString() => Label;
    }
}
