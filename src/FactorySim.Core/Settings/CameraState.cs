namespace FactorySim.Core.Settings;

/// <summary>Where the orbit camera looks: pivot in metres, distance in metres, yaw and pitch in degrees.</summary>
public sealed record CameraState(double PivotX, double PivotY, double PivotZ, double Distance, double Yaw, double Pitch);
