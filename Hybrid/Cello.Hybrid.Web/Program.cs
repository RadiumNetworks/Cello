using Cello.Audio;
using Cello.Hybrid.Web.Components;
using Cello.Hybrid.Shared.Services;
using Cello.Hybrid.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Add device-specific services used by the Cello.Hybrid.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<DashboardLayoutService>();
builder.Services.AddSingleton<IMicrophoneCapture, UnsupportedMicrophoneCapture>();
builder.Services.AddSingleton<IMidiPlayback, UnsupportedMidiPlayback>();
builder.Services.AddSingleton<IAudioAnalysisStream>(services => services.GetRequiredService<IMicrophoneCapture>());
builder.Services.AddScoped<AudioDashboardService>();
builder.Services.AddScoped<ScorePlaybackService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(Cello.Hybrid.Shared._Imports).Assembly,
        typeof(Cello.Hybrid.Web.Client._Imports).Assembly);

app.Run();
