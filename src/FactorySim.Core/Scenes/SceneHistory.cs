namespace FactorySim.Core.Scenes;

/// <summary>
/// Undo and redo for the scene editor. Call <see cref="Record"/> just before a change; the
/// scene is kept as a snapshot of its file text, so every kind of change is covered.
/// </summary>
public sealed class SceneHistory
{
    private readonly int _capacity;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    public SceneHistory(int capacity = 100)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Remembers the scene as it is now, before it changes. Drops the redo steps.</summary>
    public void Record(Scene scene)
    {
        Push(_undo, scene.ToJson());
        _redo.Clear();
    }

    /// <summary>Forgets the step recorded last, for a change that did not go through.</summary>
    public void DropLast()
    {
        if (_undo.Count > 0)
        {
            _undo.RemoveAt(_undo.Count - 1);
        }
    }

    /// <summary>The scene before the last change, or null when there is nothing to undo.</summary>
    public Scene? Undo(Scene current) => Step(_undo, _redo, current);

    /// <summary>The scene after the change that was undone last, or null.</summary>
    public Scene? Redo(Scene current) => Step(_redo, _undo, current);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private Scene? Step(List<string> from, List<string> to, Scene current)
    {
        if (from.Count == 0)
        {
            return null;
        }

        string snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        Push(to, current.ToJson());
        return Scene.FromJson(snapshot);
    }

    private void Push(List<string> stack, string snapshot)
    {
        stack.Add(snapshot);
        if (stack.Count > _capacity)
        {
            stack.RemoveAt(0);
        }
    }
}
