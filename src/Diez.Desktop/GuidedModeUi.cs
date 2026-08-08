using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace DiezPublishingStudio;

internal static class GuidedModeUi
{
    public static void Attach(MainWindow window)
    {
        if (window.Content is not Border border || border.Child is not Control advancedView) return;

        border.Child = null;
        var host = new Border { Child = advancedView };
        var guide = new PublisherGuideView(window, host, advancedView);

        var guidedButton = new Button { Content = "Guida passo passo", Width = 170 };
        var advancedButton = new Button { Content = "Modalità esperto", Width = 160 };
        var modeHelp = new TextBlock
        {
            Text = "Guida passo passo: Diez ti fa una domanda alla volta.",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        void ShowGuided()
        {
            host.Child = null;
            host.Child = guide;
            modeHelp.Text = "Guida passo passo: scegli cosa vuoi ottenere e Diez ti accompagna senza gergo tecnico.";
        }

        void ShowAdvanced()
        {
            host.Child = null;
            host.Child = advancedView;
            modeHelp.Text = "Modalità esperto: mostra direttamente tutti gli strumenti e gli stati interni del progetto.";
        }

        guidedButton.Click += (_, _) => ShowGuided();
        advancedButton.Click += (_, _) => ShowAdvanced();
        ToolTip.SetTip(guidedButton, "Consigliata: una domanda alla volta, con Avanti e Indietro.");
        ToolTip.SetTip(advancedButton, "Per chi conosce già Diez e vuole accedere direttamente a tutti gli strumenti.");

        border.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { guidedButton, advancedButton, modeHelp }
                },
                host.WithGridRow(1)
            }
        };

        ShowGuided();
    }
}

internal sealed class PublisherGuideView : Grid
{
    private readonly MainWindow _window;
    private readonly Border _host;
    private readonly Control _advancedView;
    private readonly TextBlock _stepTitle;
    private readonly TextBlock _stepInfo;
    private readonly Border _body;
    private readonly Button _back;
    private readonly Button _next;
    private int _step;
    private string _projectType = "Puzzle / giochi di parole";
    private string _startingPoint = "Da zero";

    public PublisherGuideView(MainWindow window, Border host, Control advancedView)
    {
        _window = window;
        _host = host;
        _advancedView = advancedView;

        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
        RowSpacing = 10;
        Margin = new Thickness(12, 8);

        _stepTitle = new TextBlock { FontSize = 24, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _stepInfo = new TextBlock { FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _body = new Border { Padding = new Thickness(12) };
        _back = new Button { Content = "← Indietro", Width = 120 };
        _next = new Button { Content = "Avanti →", Width = 120 };

        _back.Click += (_, _) => { if (_step > 0) { _step--; Render(); } };
        _next.Click += (_, _) => { if (_step < 4) { _step++; Render(); } };

        Children.Add(_stepTitle);
        Children.Add(_stepInfo.WithGridRow(1));
        Children.Add(_body.WithGridRow(2));
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _back, _next }
        }.WithGridRow(3));

        Render();
    }

    private void Render()
    {
        _back.IsEnabled = _step > 0;
        _next.IsVisible = _step < 4;
        _stepTitle.Text = $"Passo {_step + 1} di 5 — {StepName(_step)}";
        _stepInfo.Text = StepExplanation(_step);
        _body.Child = _step switch
        {
            0 => BuildProjectTypeStep(),
            1 => BuildStartingPointStep(),
            2 => BuildProjectStep(),
            3 => BuildContentStep(),
            _ => BuildFinishStep()
        };
    }

    private Control BuildProjectTypeStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Cosa vuoi realizzare?", FontSize = 20 });
        foreach (var value in new[]
                 {
                     "Puzzle / giochi di parole", "Quiz / trivia", "Coloring book", "Romanzo / racconto",
                     "Libro illustrato", "Catalogo / raccolta dati", "Altro"
                 })
        {
            var radio = new RadioButton { Content = value, GroupName = "project-type", IsChecked = value == _projectType };
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) _projectType = value; };
            panel.Children.Add(radio);
        }
        return panel;
    }

    private Control BuildStartingPointStep()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Hai già qualcosa da cui partire?", FontSize = 20 });
        foreach (var item in new[]
                 {
                     ("Da zero", "No. Voglio che Diez mi aiuti a creare tutto da capo."),
                     ("Ho file", "Sì. Ho già testi, immagini, tabelle o altri file."),
                     ("Un po' e un po'", "Ho già qualcosa, ma devo anche creare nuovo materiale.")
                 })
        {
            var radio = new RadioButton { Content = item.Item2, GroupName = "starting-point", IsChecked = item.Item1 == _startingPoint };
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) _startingPoint = item.Item1; };
            panel.Children.Add(radio);
        }
        return panel;
    }

    private Control BuildProjectStep()
    {
        var panel = new StackPanel { Spacing = 10 };
        if (TryGetProject(out var project, out _))
        {
            panel.Children.Add(new TextBlock { Text = $"Progetto aperto: {project.Name}", FontSize = 20 });
            panel.Children.Add(new TextBlock { Text = "Perfetto. Non devi creare niente di nuovo: premi Avanti." });
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = "Dove salviamo il lavoro?", FontSize = 20 });
        panel.Children.Add(new TextBlock
        {
            Text = "Diez salva tutto in un file .diez. È la cartella di lavoro del progetto, non il libro finale.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var create = new Button { Content = "Crea un nuovo progetto", Width = 210 };
        var open = new Button { Content = "Apri un progetto che ho già", Width = 230 };
        create.Click += async (_, _) => { await InvokeWindowTaskAsync("CreateProjectAsync"); Render(); };
        open.Click += async (_, _) => { await InvokeWindowTaskAsync("OpenProjectAsync"); Render(); };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { create, open } });
        return panel;
    }

    private Control BuildContentStep()
    {
        var panel = new StackPanel { Spacing = 10 };
        if (!TryGetProject(out _, out _))
        {
            panel.Children.Add(new TextBlock { Text = "Prima crea o apri il progetto nel passo precedente." });
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = "Adesso mettiamo dentro ciò che serve", FontSize = 20 });
        panel.Children.Add(new TextBlock
        {
            Text = _startingPoint switch
            {
                "Ho file" => "Hai già il materiale: scegli i file e Diez li conserverà nel progetto.",
                "Un po' e un po'" => "Puoi aggiungere ciò che hai già e creare con l'AI quello che manca.",
                _ => "Partiamo da zero: descrivi cosa vuoi ottenere e Diez prepara le istruzioni da dare all'AI."
            },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (_startingPoint != "Da zero")
        {
            var add = new Button { Content = "Aggiungi i miei file", Width = 180 };
            add.Click += async (_, _) => await InvokeWindowTaskAsync("ImportMaterialsAsync");
            panel.Children.Add(add);
        }
        if (_startingPoint != "Ho file")
        {
            var create = new Button { Content = "Crea un nuovo contenuto", Width = 200 };
            create.Click += async (_, _) => await OpenSimpleAiAsync();
            panel.Children.Add(create);
        }

        return panel;
    }

    private Control BuildFinishStep()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Quando i contenuti sono pronti", FontSize = 20 });
        panel.Children.Add(new TextBlock
        {
            Text = "Puoi continuare a creare o aggiungere materiale, controllare il progetto, prepararlo per la consegna oppure esportare i file modificabili.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var create = new Button { Content = "Crea / aggiungi ancora", Width = 190 };
        var check = new Button { Content = "Controlla il progetto", Width = 180 };
        var prepare = new Button { Content = "Prepara la consegna", Width = 180 };
        var export = new Button { Content = "Esporta / consegna", Width = 180 };

        create.Click += (_, _) => { _step = 3; Render(); };
        check.Click += (_, _) => ShowAdvanced("Qui trovi i controlli del progetto. Nella modalità guidata li renderemo progressivamente più automatici.");
        prepare.Click += (_, _) => ClickAdvancedButton("Prepara consegna");
        export.Click += (_, _) => ClickAdvancedButton("Esporta / Consegna");

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { create, check, prepare, export }
        });
        return panel;
    }

    private async Task OpenSimpleAiAsync()
    {
        if (!TryGetProject(out var project, out var path)) return;
        var suggested = _projectType switch
        {
            "Coloring book" or "Libro illustrato" => AiProductionService.TypeImage,
            "Romanzo / racconto" => AiProductionService.TypeText,
            _ => AiProductionService.TypeData
        };
        var window = new SimpleAiCreationWindow(project, path, _projectType, suggested);
        await window.ShowDialog(_window);
    }

    private void ClickAdvancedButton(string text)
    {
        var button = Descendants(_advancedView).OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Content?.ToString(), text, StringComparison.Ordinal));
        if (button is null)
        {
            ShowAdvanced($"Non trovo il comando '{text}'. Apri la modalità esperto per continuare.");
            return;
        }
        ShowAdvanced(string.Empty);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void ShowAdvanced(string message)
    {
        _host.Child = null;
        _host.Child = _advancedView;
        if (!string.IsNullOrWhiteSpace(message)) SetMainStatus(message);
    }

    private async Task InvokeWindowTaskAsync(string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.Invoke(_window, null) is Task task) await task;
    }

    private bool TryGetProject(out PreviewProject project, out string path)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        project = typeof(MainWindow).GetField("_project", flags)?.GetValue(_window) as PreviewProject ?? null!;
        path = typeof(MainWindow).GetField("_currentProjectPath", flags)?.GetValue(_window) as string ?? string.Empty;
        return project is not null && !string.IsNullOrWhiteSpace(path);
    }

    private void SetMainStatus(string message)
    {
        var status = typeof(MainWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window) as TextBlock;
        if (status is not null) status.Text = message;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
        {
            foreach (var child in panel.Children.SelectMany(Descendants)) yield return child;
        }
        else if (root is Border border && border.Child is Control child)
        {
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static string StepName(int step) => step switch
    {
        0 => "Cosa vuoi fare",
        1 => "Da dove partiamo",
        2 => "Il tuo progetto",
        3 => "Contenuti",
        _ => "Controllo e consegna"
    };

    private static string StepExplanation(int step) => step switch
    {
        0 => "Scegli il tipo di pubblicazione. Non serve conoscere termini tecnici.",
        1 => "Dì a Diez se hai già dei file oppure se vuoi creare tutto da zero.",
        2 => "Salviamo il lavoro in un progetto Diez oppure apriamo quello che hai già.",
        3 => "Aggiungiamo file esistenti oppure creiamo nuovi contenuti con l'AI.",
        _ => "Quando sei soddisfatto, controlli il progetto e prepari i file da consegnare."
    };
}

internal sealed class SimpleAiCreationWindow : Window
{
    private readonly PreviewProject _project;
    private readonly string _projectPath;
    private readonly string _projectType;
    private readonly RadioButton _data;
    private readonly RadioButton _text;
    private readonly RadioButton _image;
    private readonly TextBox _request;
    private readonly TextBox _instructions;
    private readonly TextBox _answer;
    private readonly TextBlock _status;
    private AiProductionJob? _job;

    public SimpleAiCreationWindow(PreviewProject project, string projectPath, string projectType, string suggestedType)
    {
        _project = project;
        _projectPath = projectPath;
        _projectType = projectType;
        Title = "Crea un nuovo contenuto";
        Width = 900;
        Height = 700;
        MinWidth = 760;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _data = Choice("Una lista o una tabella", suggestedType == AiProductionService.TypeData);
        _text = Choice("Un testo", suggestedType == AiProductionService.TypeText);
        _image = Choice("Un'immagine", suggestedType == AiProductionService.TypeImage);
        _request = new TextBox
        {
            AcceptsReturn = true,
            Height = 110,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = SuggestedRequest(projectType)
        };
        _instructions = new TextBox
        {
            AcceptsReturn = true,
            Height = 180,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Qui compariranno le istruzioni pronte da copiare nella tua AI."
        };
        _answer = new TextBox
        {
            AcceptsReturn = true,
            Height = 120,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "Se l'AI risponde con testo o dati, incolla qui la risposta."
        };
        _status = new TextBlock
        {
            Text = "1. Scegli cosa creare. 2. Scrivi cosa vuoi. 3. Prepara le istruzioni. 4. Copiale nella tua AI.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var prepare = Button("Prepara le istruzioni", 190);
        var copy = Button("Copia per l'AI", 150);
        var saveAnswer = Button("Salva la risposta", 160);
        var file = Button("Scegli il file creato", 180);
        var approve = Button("Va bene, approva", 160);

        prepare.Click += async (_, _) => await PrepareAsync();
        copy.Click += async (_, _) => await CopyAsync();
        saveAnswer.Click += async (_, _) => await SaveAnswerAsync();
        file.Click += async (_, _) => await AttachFileAsync();
        approve.Click += async (_, _) => await ApproveAsync();

        Help(prepare, "Diez trasforma quello che hai scritto in istruzioni complete da dare all'AI.");
        Help(copy, "Copia le istruzioni. Poi incollale nella chat o nel generatore AI che preferisci.");
        Help(saveAnswer, "Per testo e tabelle: incolla qui quello che ha risposto l'AI e salvalo nel progetto.");
        Help(file, "Per immagini o file: scegli dal PC il risultato creato dall'AI. Diez lo conserverà nel progetto.");
        Help(approve, "Usalo solo dopo aver controllato il risultato e deciso che va bene.");

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = "Cosa vuoi creare adesso?", FontSize = 23 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15, Children = { _data, _text, _image } },
                    new TextBlock { Text = "Spiegalo con parole tue" },
                    _request,
                    prepare,
                    new TextBlock { Text = "Istruzioni pronte da dare all'AI" },
                    _instructions,
                    copy,
                    new TextBlock { Text = "Riporta qui quello che hai ottenuto" },
                    _answer,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { saveAnswer, file, approve } },
                    _status
                }
            }
        };
    }

    private async Task PrepareAsync()
    {
        var request = (_request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(request))
        {
            _status.Text = "Scrivi prima cosa vuoi ottenere. Puoi usare parole normali: Diez prepara il resto.";
            return;
        }
        var type = _image.IsChecked == true ? AiProductionService.TypeImage : _text.IsChecked == true ? AiProductionService.TypeText : AiProductionService.TypeData;
        _job = AiProductionService.CreateJob(_project, type, _projectType, request);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        _instructions.Text = _job.Prompt;
        _status.Text = "Pronto. Copia queste istruzioni nella tua AI. Quando hai il risultato, torna qui.";
    }

    private async Task CopyAsync()
    {
        if (_job is null) { _status.Text = "Prima prepara le istruzioni."; return; }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) { _status.Text = "Non riesco ad accedere agli appunti di Windows."; return; }
        await clipboard.SetTextAsync(_job.Prompt);
        _status.Text = "Copiato. Ora incolla nella tua AI.";
    }

    private async Task SaveAnswerAsync()
    {
        if (_job is null) { _status.Text = "Prima prepara le istruzioni."; return; }
        AiProductionService.SetTextResult(_job, _answer.Text);
        await ProjectFileStore.SaveAsync(_projectPath, _project);
        _status.Text = string.IsNullOrWhiteSpace(_job.ResultText)
            ? "Non c'è ancora una risposta da salvare."
            : "Risposta salvata nel progetto. Controllala e, se va bene, approvala.";
    }

    private async Task AttachFileAsync()
    {
        if (_job is null) { _status.Text = "Prima prepara le istruzioni."; return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scegli il risultato creato dall'AI",
            AllowMultiple = false
        });
        var selected = files.FirstOrDefault();
        if (selected is null) return;
        var result = await AiProductionService.AttachResultFileAsync(_project, _projectPath, _job, selected.Path.LocalPath);
        _status.Text = result.Message;
    }

    private async Task ApproveAsync()
    {
        if (_job is null) { _status.Text = "Prima crea e riporta un risultato."; return; }
        var result = AiProductionService.Approve(_project, _job);
        if (result.Success) await ProjectFileStore.SaveAsync(_projectPath, _project);
        _status.Text = result.Success ? "Approvato. Diez ricorderà che questo è il risultato scelto." : result.Message;
    }

    private void Help(Control control, string text)
    {
        ToolTip.SetTip(control, text);
        control.GotFocus += (_, _) => _status.Text = text;
        control.PointerEntered += (_, _) => _status.Text = text;
    }

    private static RadioButton Choice(string text, bool selected) => new()
    {
        Content = text,
        GroupName = "simple-ai-output",
        IsChecked = selected
    };

    private static Button Button(string text, double width) => new()
    {
        Content = text,
        Width = width,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static string SuggestedRequest(string projectType) => projectType switch
    {
        "Puzzle / giochi di parole" => "Es. Crea da zero una raccolta di temi e parole per un libro di word search nostalgico, senza duplicati.",
        "Quiz / trivia" => "Es. Crea da zero domande e risposte su un tema preciso, evitando doppioni.",
        "Coloring book" => "Es. Crea un'immagine da colorare semplice, con linee pulite e senza testo.",
        "Romanzo / racconto" => "Es. Proponi una scena o un testo seguendo il tono che ti descrivo.",
        "Libro illustrato" => "Es. Crea un'illustrazione coerente con la scena che ti descrivo.",
        "Catalogo / raccolta dati" => "Es. Crea da zero una tabella completa e ordinata sul tema che ti indico.",
        _ => "Descrivi semplicemente ciò che vuoi ottenere."
    };
}
