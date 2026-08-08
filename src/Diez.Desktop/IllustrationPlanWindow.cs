using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal sealed class IllustrationPlanWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly ComboBox _image;
    private readonly ComboBox _content;
    private readonly ComboBox _position;
    private readonly ComboBox _width;
    private readonly TextBox _caption;
    private readonly ListBox _placements;
    private readonly TextBlock _status;
    private readonly List<ImageChoice> _images;
    private readonly List<ContentChoice> _contents;
    private readonly List<PositionChoice> _positions;
    private readonly List<WidthChoice> _widths;

    public IllustrationPlanWindow(PreviewProject project, string projectPath)
    {
        _project = project;
        _projectPath = projectPath;

        Title = "Piano illustrazioni DOCX";
        Width = 850;
        Height = 690;
        MinWidth = 760;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _images = project.Materials
            .Where(IllustrationPlanService.CanEmbedInDocx)
            .Select(m => new ImageChoice(m.MaterialId, m.FileName, m.Summary))
            .ToList();
        _contents = project.ContentNodes
            .Where(n => EditableMasterService.CanEdit(project, n))
            .OrderBy(n => MaterialOrder(n.MaterialId))
            .ThenBy(n => n.Ordinal)
            .Select(n => new ContentChoice(n.ContentId, n.Title, n.SourceLocator))
            .ToList();
        _positions =
        [
            new(IllustrationPlanService.BeforeHeading, "Prima del titolo"),
            new(IllustrationPlanService.AfterHeading, "Dopo il titolo"),
            new(IllustrationPlanService.AfterContent, "Dopo il testo"),
            new(IllustrationPlanService.FullPageAfter, "Pagina dedicata dopo il testo")
        ];
        _widths = [new(25), new(50), new(75), new(100)];

        var heading = new TextBlock
        {
            Text = "Collocazione delle immagini nel DOCX modificabile",
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var explanation = new TextBlock
        {
            Text = "Il piano indica dove Diez inserirà le immagini nel DOCX. Gli originali restano comunque separati nel .diez e nello ZIP immagini. PNG/JPEG/GIF/BMP sono incorporabili; altri formati restano disponibili come asset originali.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _image = new ComboBox { Width = 355, ItemsSource = _images, PlaceholderText = "Immagine" };
        _content = new ComboBox { Width = 355, ItemsSource = _contents, PlaceholderText = "Capitolo / sezione" };
        _position = new ComboBox { Width = 230, ItemsSource = _positions };
        _width = new ComboBox { Width = 120, ItemsSource = _widths };
        _caption = new TextBox { Width = 710, Watermark = "Didascalia opzionale", MaxLength = 500 };

        if (_images.Count > 0) _image.SelectedIndex = 0;
        if (_contents.Count > 0) _content.SelectedIndex = 0;
        _position.SelectedIndex = 1;
        _width.SelectedIndex = 2;

        _placements = new ListBox { Width = 710, Height = 190 };
        _placements.SelectionChanged += (_, _) => LoadSelectedPlacement();

        var save = MakeButton("Aggiungi / aggiorna", 180);
        save.Click += async (_, _) => await SavePlacementAsync();
        var remove = MakeButton("Rimuovi collocazione", 180);
        remove.Click += async (_, _) => await RemovePlacementAsync();
        var newPlacement = MakeButton("Nuova", 110);
        newPlacement.Click += (_, _) => ClearSelection();

        _status = new TextBlock
        {
            Text = BuildInitialStatus(),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 710,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var close = MakeButton("Chiudi", 120);
        close.HorizontalAlignment = HorizontalAlignment.Center;
        close.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    heading,
                    explanation,
                    Label("Immagine e destinazione"),
                    Row(_image, _content),
                    Label("Posizione e larghezza indicativa"),
                    Row(_position, _width),
                    _caption,
                    Row(save, remove, newPlacement),
                    Label("Collocazioni registrate"),
                    _placements,
                    _status,
                    close
                }
            }
        };

        RefreshPlacements();
    }

    private async Task SavePlacementAsync()
    {
        if (_image.SelectedItem is not ImageChoice image ||
            _content.SelectedItem is not ContentChoice content ||
            _position.SelectedItem is not PositionChoice position ||
            _width.SelectedItem is not WidthChoice width)
        {
            _status.Text = "Seleziona immagine, destinazione, posizione e larghezza.";
            return;
        }

        var selectedId = (_placements.SelectedItem as PlacementChoice)?.PlacementId;
        var result = IllustrationPlanService.Upsert(
            _project,
            selectedId,
            image.MaterialId,
            content.ContentId,
            position.Code,
            width.Value,
            _caption.Text);
        _status.Text = result.Message;
        if (!result.Changed || result.Placement is null) return;

        try
        {
            await ProjectFileStore.SaveAsync(_projectPath, _project);
            RefreshPlacements(result.Placement.PlacementId);
            var freeze = EditionFreezeService.GetLatestFreeze(_project);
            if (freeze is not null && !EditionFreezeService.IsLatestFreezeCurrent(_project))
                _status.Text = result.Message + " L'Edition Freeze precedente è ora superato: ricrealo prima del prossimo Publication Candidate.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Piano aggiornato in memoria ma salvataggio fallito: {ex.Message}";
        }
    }

    private async Task RemovePlacementAsync()
    {
        if (_placements.SelectedItem is not PlacementChoice selected)
        {
            _status.Text = "Seleziona prima una collocazione da rimuovere.";
            return;
        }
        if (!IllustrationPlanService.Remove(_project, selected.PlacementId)) return;

        try
        {
            await ProjectFileStore.SaveAsync(_projectPath, _project);
            ClearSelection();
            RefreshPlacements();
            _status.Text = "Collocazione rimossa. Se esisteva un Edition Freeze, verifica lo stato prima del prossimo handoff.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Collocazione rimossa in memoria ma salvataggio fallito: {ex.Message}";
        }
    }

    private void RefreshPlacements(Guid? selectId = null)
    {
        var items = _project.IllustrationPlacements
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.PlacementId)
            .Select(p =>
            {
                var image = _project.Materials.FirstOrDefault(m => m.MaterialId == p.MaterialId)?.FileName ?? "immagine mancante";
                var content = _project.ContentNodes.FirstOrDefault(n => n.ContentId == p.ContentId)?.Title ?? "contenuto mancante";
                return new PlacementChoice(p.PlacementId, $"{image} → {content} · {IllustrationPlanService.PositionLabel(p.Position)} · {p.WidthPercent}%{(string.IsNullOrWhiteSpace(p.Caption) ? string.Empty : " · “" + p.Caption + "”")}");
            })
            .ToList();
        _placements.ItemsSource = items;
        if (selectId.HasValue)
        {
            var index = items.FindIndex(i => i.PlacementId == selectId.Value);
            if (index >= 0) _placements.SelectedIndex = index;
        }
    }

    private void LoadSelectedPlacement()
    {
        if (_placements.SelectedItem is not PlacementChoice selected) return;
        var placement = _project.IllustrationPlacements.FirstOrDefault(p => p.PlacementId == selected.PlacementId);
        if (placement is null) return;

        _image.SelectedIndex = _images.FindIndex(i => i.MaterialId == placement.MaterialId);
        _content.SelectedIndex = _contents.FindIndex(c => c.ContentId == placement.ContentId);
        _position.SelectedIndex = _positions.FindIndex(p => p.Code == placement.Position);
        _width.SelectedIndex = _widths.FindIndex(w => w.Value == placement.WidthPercent);
        _caption.Text = placement.Caption ?? string.Empty;
    }

    private void ClearSelection()
    {
        _placements.SelectedIndex = -1;
        if (_images.Count > 0) _image.SelectedIndex = 0;
        if (_contents.Count > 0) _content.SelectedIndex = 0;
        _position.SelectedIndex = 1;
        _width.SelectedIndex = 2;
        _caption.Text = string.Empty;
    }

    private string BuildInitialStatus()
    {
        var allImages = _project.Materials.Count(IllustrationPlanService.IsImage);
        var supported = _images.Count;
        return $"{_project.IllustrationPlacements.Count} collocazioni registrate · {supported}/{allImages} immagini incorporabili nel DOCX.";
    }

    private int MaterialOrder(Guid materialId)
    {
        var index = _project.Materials.FindIndex(m => m.MaterialId == materialId);
        return index < 0 ? int.MaxValue : index;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 15,
        Width = 710,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var control in controls) row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private sealed record ImageChoice(Guid MaterialId, string FileName, string Summary)
    {
        public override string ToString() => $"{FileName} · {Summary}";
    }

    private sealed record ContentChoice(Guid ContentId, string Title, string SourceLocator)
    {
        public override string ToString() => $"{Title} · {SourceLocator}";
    }

    private sealed record PositionChoice(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record WidthChoice(int Value)
    {
        public override string ToString() => Value + "%";
    }

    private sealed record PlacementChoice(Guid PlacementId, string Label)
    {
        public override string ToString() => Label;
    }
}