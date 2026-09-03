using System.Text.Json;
using Cello.Hybrid.Shared.Models;
using Microsoft.JSInterop;

namespace Cello.Hybrid.Shared.Services;

public sealed class DashboardLayoutService(IJSRuntime jsRuntime)
{
    private const string StorageKey = "cello.tuner.layout.v2";

    public static IReadOnlyList<DashboardPanelState> CreateDefaultLayout() =>
    [
        new("cello-image", "Cello", 0, 0, 300, 590, true, 0),
        new("pitch", "Tonerkennung", 320, 0, 340, 270, true, 1),
        new("tuner", "Stimmgerät", 680, 0, 430, 270, true, 2),
        new("notation", "Aktuelle Note", 1130, 0, 300, 270, true, 3),
        new("level", "Eingangspegel", 320, 290, 260, 300, true, 4),
        new("spectrum", "Klangspektrum", 600, 290, 830, 300, true, 5)
    ];

    public async ValueTask<IReadOnlyList<DashboardPanelState>> LoadAsync()
    {
        try
        {
            string? json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            DashboardPanelState[]? panels = json is null
                ? null
                : JsonSerializer.Deserialize<DashboardPanelState[]>(json);
            if (panels is not { Length: > 0 })
            {
                return CreateDefaultLayout();
            }

            Dictionary<string, DashboardPanelState> saved = panels.ToDictionary(panel => panel.Id);
            return CreateDefaultLayout()
                .Select(defaultPanel => saved.GetValueOrDefault(defaultPanel.Id, defaultPanel))
                .OrderBy(panel => panel.Order)
                .ToArray();
        }
        catch (JSException)
        {
            return CreateDefaultLayout();
        }
    }

    public async ValueTask SaveAsync(IEnumerable<DashboardPanelState> panels)
    {
        string json = JsonSerializer.Serialize(panels.OrderBy(panel => panel.Order));
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public async ValueTask ResetAsync()
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }
}
