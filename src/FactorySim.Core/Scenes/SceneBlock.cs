namespace FactorySim.Core.Scenes;

/// <summary>
/// A building block (plan 18): a group of items saved to place again. The positions of the items are relative to
/// the middle of the group on the floor; heights stay as they were. Tags and addresses follow from the items when
/// the block is placed.
/// </summary>
public sealed record SceneBlock(string Name, string Description, IReadOnlyList<SceneItem> Items);
