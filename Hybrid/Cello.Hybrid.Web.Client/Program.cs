using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Cello.Audio;
using Cello.Hybrid.Shared.Services;
using Cello.Hybrid.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Add device-specific services used by the Cello.Hybrid.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<DashboardLayoutService>();
builder.Services.AddSingleton<IMicrophoneCapture, BrowserMicrophoneCapture>();
builder.Services.AddSingleton<IMidiPlayback, BrowserMidiPlayback>();
builder.Services.AddSingleton<IAudioAnalysisStream>(services => services.GetRequiredService<IMicrophoneCapture>());
builder.Services.AddScoped<AudioDashboardService>();
builder.Services.AddScoped<ScorePlaybackService>();

await builder.Build().RunAsync();
