namespace FactorySim.Core.Settings;

/// <summary>The app window when it is not maximized: position and size in pixels; plus whether it is maximized.</summary>
public sealed record WindowState(int X, int Y, int Width, int Height, bool Maximized);
