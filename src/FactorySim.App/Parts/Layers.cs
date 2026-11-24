namespace FactorySim.App.Parts;

/// <summary>Physics collision layers (bit masks).</summary>
public static class Layers
{
    /// <summary>Floor, conveyors, rails, pusher.</summary>
    public const uint World = 1u << 0;

    /// <summary>Boxes: the only thing sensors, emitters and removers react to.</summary>
    public const uint Boxes = 1u << 1;
}
