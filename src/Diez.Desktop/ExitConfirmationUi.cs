using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DiezPublishingStudio;

/// <summary>
/// Intercepts a normal window close (including the X button) and asks the user to confirm.
/// A confirmed close is allowed exactly once so the second Close() call does not reopen the dialog.
/// </summary>
internal static class ExitConfirmationUi
{
    private sealed class State
    {
        public bool AllowClose;
        public bool DialogOpen;
    }

    private static readonly Dictionary<MainWindow, State> States = [];

    public static void Attach(MainWindow window)
    {
        if (States.ContainsKey(window)) return;
        var state = new State();
        States[window] = state;

        window.Closing += async (_, e) =>
        {
            if (state.AllowClose) return;
            e.Cancel = true;
            if (state.DialogOpen) return;

            state.DialogOpen = true;
            try
            {
                var confirmed = await ShowConfirmationAsync(window);
                if (!confirmed) return;
                state.AllowClose = true;
                window.Close();
            }
            catch
            {
                // A failure in the confirmation UI must never force-close the application.
            }
            finally
            {
                state.DialogOpen = false;
            }
        };

        window.Closed += (_, _) => States.Remove(window);
    }

    internal static bool IsAttached(MainWindow window) => States.ContainsKey(window);

    private static async Task<bool> ShowConfirmationAsync(Window owner)
    {
        var dialog = new Window
        {
            Title = "Uscire da Diez?",
            Width = 430,
            Height = 205,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var stay = new Button
        {
            Content = "Resta in Diez",
            Width = 130,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var exit = new Button
        {
            Content = "Esci",
            Width = 110,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        stay.Click += (_, _) => dialog.Close(false);
        exit.Click += (_, _) => dialog.Close(true);

        dialog.Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Vuoi davvero uscire da Diez Publishing Studio?",
                        FontSize = 20,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Scegli “Resta in Diez” per continuare a lavorare.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { stay, exit }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool>(owner);
    }
}
