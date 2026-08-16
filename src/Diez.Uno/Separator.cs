using Microsoft.UI.Xaml.Controls;

namespace DiezPublishingStudio.UnoSpike;

internal sealed class Separator : TextBlock
{
    public Separator()
    {
        Text = "────────────────────────────────";
        Opacity = 0.24;
        IsHitTestVisible = false;
    }
}
