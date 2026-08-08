using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DiezPublishingStudio;

internal static class PlainLanguageUi
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["Modalità esperto"] = "Controllo completo",
        ["Produzione AI"] = "Contenuti con AI",
        ["Salva brief"] = "Salva regole",
        ["Nuovo job"] = "Nuovo contenuto",
        ["Prompt CSV"] = "Istruzioni CSV",
        ["Prompt XLSX"] = "Istruzioni XLSX",
        ["Coda di produzione"] = "Contenuti preparati",
        ["Nessun job selezionato"] = "Nessun contenuto selezionato",
        ["Richiesta specifica"] = "Cosa deve fare l'AI",
        ["Prompt da usare con l'AI"] = "Istruzioni da dare all'AI",
        ["Ricrea prompt"] = "Aggiorna istruzioni",
        ["Copia prompt"] = "Copia per l'AI",
        ["Collega file risultato"] = "Scegli file ottenuto",
        ["Brief comune del progetto — scrivilo una volta, poi ogni job eredita queste regole"] = "Regole comuni del progetto — scrivile una volta e Diez le riusa nei contenuti che prepari",
        ["Nuovo lavoro AI"] = "Nuovo contenuto con AI",
        ["Crea job e prompt"] = "Prepara le istruzioni",
        ["Il prompt verrà costruito unendo questa richiesta al brief generale del progetto."] = "Diez unirà questa richiesta alle regole comuni del progetto e preparerà le istruzioni per l'AI.",
        ["Crea un job per ogni immagine, testo o tabella da produrre. Diez conserva il collegamento fra prompt e risultato."] = "Crea un elemento per ogni immagine, testo o tabella. Diez ricorda quali istruzioni hanno prodotto ogni risultato."
    };

    public static void Attach(MainWindow window)
    {
        Apply(window);

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            foreach (var openWindow in desktop.Windows)
                Apply(openWindow);
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    internal static void Apply(Control root) => Visit(root);

    private static void Visit(Control control)
    {
        if (control is Window window)
        {
            if (window.Title.Contains("Produzione AI", StringComparison.Ordinal))
                window.Title = window.Title.Replace("Produzione AI", "Contenuti con AI", StringComparison.Ordinal);
            if (string.Equals(window.Title, "Nuovo lavoro AI", StringComparison.Ordinal))
                window.Title = "Nuovo contenuto con AI";
        }

        if (control is Button button && button.Content is string buttonText && Exact.TryGetValue(buttonText, out var newButtonText))
            button.Content = newButtonText;
        else if (control is RadioButton radio && radio.Content is string radioText && Exact.TryGetValue(radioText, out var newRadioText))
            radio.Content = newRadioText;
        else if (control is TextBlock text)
        {
            if (Exact.TryGetValue(text.Text ?? string.Empty, out var newText))
                text.Text = newText;
            else if (!string.IsNullOrWhiteSpace(text.Text))
                text.Text = Simplify(text.Text);
        }

        var tip = ToolTip.GetTip(control);
        if (tip is string tipText)
            ToolTip.SetTip(control, Simplify(tipText));

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>()) Visit(child);
        }
        else if (control is Border border && border.Child is Control child)
        {
            Visit(child);
        }
        else if (control is ScrollViewer scroll && scroll.Content is Control scrollChild)
        {
            Visit(scrollChild);
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control contentChild)
        {
            Visit(contentChild);
        }
    }

    private static string Simplify(string value) => value
        .Replace("job", "contenuto", StringComparison.OrdinalIgnoreCase)
        .Replace("prompt", "istruzioni", StringComparison.OrdinalIgnoreCase)
        .Replace("brief", "regole comuni", StringComparison.OrdinalIgnoreCase);
}
