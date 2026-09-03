using Cello.Audio;

namespace Cello.Hybrid;

public partial class App : Application
{
    private readonly IMicrophoneCapture _microphone;
    private bool _resumeMicrophone;

    public App(IMicrophoneCapture microphone)
    {
        InitializeComponent();
        _microphone = microphone;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "Cello.Hybrid" };
    }

    protected override void OnSleep()
    {
        _resumeMicrophone = _microphone.IsActive;
        if (_resumeMicrophone)
        {
            _microphone.Stop();
        }

        base.OnSleep();
    }

    protected override async void OnResume()
    {
        base.OnResume();
        if (!_resumeMicrophone)
        {
            return;
        }

        _resumeMicrophone = false;
        try
        {
            await _microphone.StartAsync();
        }
        catch
        {
            // The dashboard reports a start error if the user retries manually.
        }
    }
}
