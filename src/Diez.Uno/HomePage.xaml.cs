using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DiezPublishingStudio.UnoSpike;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ProjectNameText.Text = SpikeState.ProjectName;
        MaterialText.Text = SpikeState.ImportedMaterial;
    }

    private void OpenBookType_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(BookTypePage));
    }
}
