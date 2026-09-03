using Microsoft.Extensions.Logging;
using Cello.Audio;
using Cello.Hybrid.Shared.Services;
using Cello.Hybrid.Services;
#if WINDOWS
using Cello.Audio.Windows;
#endif

namespace Cello.Hybrid;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Add device-specific services used by the Cello.Hybrid.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddScoped<DashboardLayoutService>();

        builder.Services.AddMauiBlazorWebView();

    #if WINDOWS
        builder.Services.AddSingleton<IMicrophoneCapture, WindowsMicrophoneCapture>();
        builder.Services.AddSingleton<IMidiPlayback, WindowsMidiPlayback>();
    #else
            builder.Services.AddSingleton<IMicrophoneCapture, UnsupportedMicrophoneCapture>();
            builder.Services.AddSingleton<IMidiPlayback, UnsupportedMidiPlayback>();
    #endif
            builder.Services.AddSingleton<IAudioAnalysisStream>(services =>
                services.GetRequiredService<IMicrophoneCapture>());
            builder.Services.AddScoped<AudioDashboardService>();
            builder.Services.AddScoped<ScorePlaybackService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
