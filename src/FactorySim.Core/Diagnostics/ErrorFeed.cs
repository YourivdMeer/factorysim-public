namespace FactorySim.Core.Diagnostics;

/// <summary>
/// Plan 31: errors from any thread on their way to the status line. Only the newest error is kept; the same error
/// again (an error in a frame callback comes every frame) raises its count instead of queueing. The main thread
/// asks <see cref="TakeChanged"/> once a frame.
/// </summary>
public sealed class ErrorFeed
{
    private const int MaxStatusMessage = 160;

    private readonly object _lock = new();
    private ErrorNotice? _last;
    private bool _changed;
    private int _total;

    /// <summary>Number of errors reported since the start.</summary>
    public int Total
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>Adds an error. Safe from any thread; blank text is ignored.</summary>
    public void Report(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string text = message.Trim();
        lock (_lock)
        {
            _last = _last is not null && _last.Message == text ? _last with { Count = _last.Count + 1 } : new ErrorNotice(text, 1);
            _changed = true;
            _total++;
        }
    }

    /// <summary>The newest error when something came in since the last call; otherwise null.</summary>
    public ErrorNotice? TakeChanged()
    {
        lock (_lock)
        {
            if (!_changed)
            {
                return null;
            }

            _changed = false;
            return _last;
        }
    }

    /// <summary>"Fout: &lt;first line&gt; (×n) — Details opent het log", on one line and cut when long.</summary>
    public static string StatusText(ErrorNotice notice)
    {
        string first = notice.Message.Split('\n')[0].Trim();
        if (first.Length > MaxStatusMessage)
        {
            first = first[..MaxStatusMessage].TrimEnd() + "…";
        }

        string count = notice.Count > 1 ? $" (×{notice.Count})" : string.Empty;
        return $"Fout: {first}{count} — Details opent het log";
    }
}
