using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace DiezPublishingStudio;

internal static class ResponsiveLayoutUi
{
    public static void Attach(MainWindow window)
    {
        // 1920x1080 at 125% scaling exposes roughly 1536x864 logical pixels.
        // Keep the startup window comfortably inside that work area and make
        // the complete editorial surface reachable at smaller heights.
        window.Width = 1200;
        window.Height = 760;
        window.MinWidth = 900;
        window.MinHeight = 560;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (window.Content is not Border border || border.Child is not StackPanel root)
            return;

        // The top project row grew over successive previews. Keep every action
        // visible without horizontal clipping on common scaled Full-HD screens.
        var projectButtons = root.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Button>().Any(button =>
                                         string.Equals(button.Content?.ToString(), "Nuovo progetto", StringComparison.Ordinal)));
        if (projectButtons is not null)
        {
            foreach (var button in projectButtons.Children.OfType<Button>())
                button.Width = Math.Min(button.Width, 145);
        }

        // A Control cannot be parented to the ScrollViewer while it is still the
        // Border child. Detach it first; doing this in the opposite order can make
        // Avalonia abort during startup before the main window becomes visible.
        border.Child = null;
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        scroll.Content = root;
        border.Child = scroll;
    }
}
