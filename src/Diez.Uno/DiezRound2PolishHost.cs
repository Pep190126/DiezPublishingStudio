using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// Second physical-review presentation pass.
/// Keeps layout/branding concerns outside the canonical project model while
/// the larger navigation and workflow redesign is still being specified.
/// </summary>
internal sealed class DiezRound2PolishHost : ContentControl
{
    private static readonly SolidColorBrush Napoli = Brush("#007FFF");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private bool _applying;

    public DiezRound2PolishHost(UIElement content)
    {
        Content = content;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) => ApplyNow();
        LayoutUpdated += (_, _) => ApplyNow();
    }

    private void ApplyNow()
    {
        if (_applying || Content is not DependencyObject root) return;
        _applying = true;
        try
        {
            ApplyTree(root);
            ReplaceLegacyBrand(root);
        }
        finally
        {
            _applying = false;
        }
    }

    private static void ApplyTree(DependencyObject node)
    {
        switch (node)
        {
            case ScrollViewer scroll when Grid.GetColumn(scroll) == 0:
                // Sidebar uses the same Bourbon/Naples blue as the main Diez identity.
                scroll.Background = Napoli;
                break;

            case StackPanel panel when IsWorkspaceRoot(panel):
                // The old 1050px ceiling left unused space on maximized windows.
                panel.MaxWidth = double.PositiveInfinity;
                panel.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;

            case TextBox box:
                // Selection is deliberately brand-blue; focus border is also reinforced
                // by the global TextControl resources in App.xaml.
                box.SelectionHighlightColor = Napoli;
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            ApplyTree(VisualTreeHelper.GetChild(node, i));
    }

    private static bool IsWorkspaceRoot(StackPanel panel) =>
        panel.MaxWidth >= 1049 &&
        panel.MaxWidth <= 1051 &&
        panel.Margin.Left >= 27 &&
        panel.Margin.Right >= 27;

    private static void ReplaceLegacyBrand(DependencyObject root)
    {
        if (root is StackPanel panel)
        {
            for (var i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not TextBlock text) continue;
                if (!string.Equals(text.Text, "Diez ∞ Publishing Studio", StringComparison.Ordinal) &&
                    !string.Equals(text.Text, "Diez Publishing Studio", StringComparison.Ordinal))
                    continue;

                panel.Children.RemoveAt(i);
                panel.Children.Insert(i, BuildBrand());
                return;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            ReplaceLegacyBrand(child);
        }
    }

    private static StackPanel BuildBrand() => new()
    {
        Spacing = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Children =
        {
            BrandText("Diez", 29, Microsoft.UI.Text.FontWeights.SemiBold),
            BrandText("∞", 36, Microsoft.UI.Text.FontWeights.SemiBold),
            BrandText("Publishing Studio", 15, Microsoft.UI.Text.FontWeights.Normal)
        }
    };

    private static TextBlock BrandText(string value, double size, Windows.UI.Text.FontWeight weight) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        Foreground = White,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        TextWrapping = TextWrapping.NoWrap
    };

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        return new SolidColorBrush(Color.FromArgb(
            255,
            Convert.ToByte(value[0..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16)));
    }
}
