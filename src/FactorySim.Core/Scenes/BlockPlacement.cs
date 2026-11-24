namespace FactorySim.Core.Scenes;

/// <summary>What placing a building block did: the new items, and the names of the items whose asset the library does not have.</summary>
public sealed record BlockPlacement(IReadOnlyList<SceneItem> Items, IReadOnlyList<string> Skipped);
