namespace Cello.Hybrid.Shared.Models;

public sealed record DashboardPanelState(
    string Id,
    string Title,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsEnabled,
    int Order)
{
    public const int MinimumWidth = 260;
    public const int MinimumHeight = 190;

    public DashboardPanelState MoveBy(double deltaX, double deltaY) => this with
    {
        X = Math.Max(0, X + (int)Math.Round(deltaX)),
        Y = Math.Max(0, Y + (int)Math.Round(deltaY))
    };

    public DashboardPanelState ResizeBy(double deltaX, double deltaY) => this with
    {
        Width = Math.Max(MinimumWidth, Width + (int)Math.Round(deltaX)),
        Height = Math.Max(MinimumHeight, Height + (int)Math.Round(deltaY))
    };
}
