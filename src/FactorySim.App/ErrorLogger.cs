using FactorySim.Core.Diagnostics;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 31: receives every error Godot logs, also an exception that Godot caught in a C# callback, and passes it to
/// an <see cref="ErrorFeed"/>. Godot calls this from any thread; the feed is thread-safe and nothing here logs itself.
/// Warnings stay in the log only: Godot and the drivers give harmless ones at every start.
/// </summary>
public partial class ErrorLogger : Logger
{
    private readonly ErrorFeed _feed;

    public ErrorLogger(ErrorFeed feed)
    {
        _feed = feed;
    }

    /// <summary>Needed by Godot to create the object; not used.</summary>
    public ErrorLogger()
        : this(new ErrorFeed())
    {
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify,
        int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType is (int)ErrorType.Error or (int)ErrorType.Script)
        {
            _feed.Report(string.IsNullOrWhiteSpace(rationale) ? code : rationale);
        }
    }

    public override void _LogMessage(string message, bool error)
    {
    }
}
