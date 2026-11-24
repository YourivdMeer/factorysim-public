namespace FactorySim.Core.Scenes;

/// <summary>
/// One box-shaped collider of a passive part, as the catalogue lists it. Everything is in the part's
/// own frame (x along the travel, y up, z across the line) with the origin where the model has it,
/// in metres and degrees. The generator writes these next to the geometry they belong to, so the
/// model and its collider cannot drift apart.
/// </summary>
/// <param name="Size">Size along x, y and z.</param>
/// <param name="Position">Centre of the box.</param>
/// <param name="Yaw">Turn about the vertical axis; positive turns +X towards -Z, like the yaw of an item in a scene.</param>
/// <param name="Pitch">Tilt about the cross axis; positive lowers the +X end, as a slope that runs downhill along +X.</param>
/// <param name="Friction">Friction of the surface, 0 to 1. An item has 0.7.</param>
public sealed record ColliderSpec(double[] Size, double[] Position, double Yaw, double Pitch, double Friction);
