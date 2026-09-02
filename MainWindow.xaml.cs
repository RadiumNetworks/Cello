using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Cello.Tuning;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Cello;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _isChangingMicrophoneState;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 900));

        AppNavigation.SelectedItem = AnalysisNavigationItem;
        if (RootFrame.CurrentSourcePageType is null)
        {
            RootFrame.Navigate(typeof(MainPage));
        }

        Closed += (_, _) => App.Microphone.Dispose();
    }

    private async void GlobalMicrophoneToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isChangingMicrophoneState)
        {
            return;
        }

        _isChangingMicrophoneState = true;
        GlobalMicrophoneToggle.IsEnabled = false;

        try
        {
            if (GlobalMicrophoneToggle.IsOn)
            {
                await App.Microphone.StartAsync();
                GlobalMicrophoneStatus.Text = "Mikrofon ist aktiv – gilt für alle Seiten";
            }
            else
            {
                App.Microphone.Stop();
                GlobalMicrophoneStatus.Text = "Mikrofon ist ausgeschaltet";
            }
        }
        catch (Exception ex)
        {
            App.Microphone.Stop();
            GlobalMicrophoneToggle.IsOn = false;
            GlobalMicrophoneStatus.Text = $"Mikrofonfehler: {ex.Message}";
        }
        finally
        {
            GlobalMicrophoneToggle.IsEnabled = true;
            _isChangingMicrophoneState = false;
        }
    }

    private void AppNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem { Tag: string destination })
        {
            return;
        }

        Type pageType = destination switch
        {
            "notation-recorder" => typeof(NotationRecorderPage),
            "musicxml-viewer" => typeof(MusicXmlViewerPage),
            "tuner" => typeof(TunerPage),
            "guitar-tuner" => typeof(GuitarTunerPage),
            _ => typeof(MainPage)
        };
        if (RootFrame.CurrentSourcePageType != pageType)
        {
            RootFrame.Navigate(pageType);
        }
    }
}
