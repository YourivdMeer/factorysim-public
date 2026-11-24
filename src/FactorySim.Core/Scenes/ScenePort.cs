namespace FactorySim.Core.Scenes;

/// <summary>
/// One end of a belt or curve, in world coordinates (plan 17). Items travel along (DirX, DirZ) through it:
/// into the part when <paramref name="IsIn"/>, out of it otherwise. Y is the height of the belt surface there.
/// </summary>
public sealed record ScenePort(string ItemId, double X, double Y, double Z, double DirX, double DirZ, bool IsIn);
