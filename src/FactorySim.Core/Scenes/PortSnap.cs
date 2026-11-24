namespace FactorySim.Core.Scenes;

/// <summary>
/// Where a dragged belt or curve clicks onto another one (plan 17): its new place and yaw, how far it was turned
/// to get there (a multiple of 90 degrees), the point where the two ends meet, and the item it clicks onto.
/// </summary>
public sealed record PortSnap(double X, double Z, double Yaw, double TurnDegrees, double PortX, double PortY, double PortZ, string OtherId);
