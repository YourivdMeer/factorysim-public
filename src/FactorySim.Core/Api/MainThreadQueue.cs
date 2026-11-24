using System.Collections.Concurrent;

namespace FactorySim.Core.Api;

/// <summary>
/// Hands work from the API thread to the main thread and waits for the answer (plan 21).
/// The main thread calls <see cref="Pump"/> every frame. Work that is not picked up within
/// <see cref="Timeout"/> is cancelled and never runs later.
/// </summary>
public sealed class MainThreadQueue
{
    private readonly ConcurrentQueue<Job> _jobs = new();

    public MainThreadQueue(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The time-out must be above zero.");
        }

        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    /// <summary>Jobs waiting for the main thread; cancelled ones are dropped at the next pump.</summary>
    public int Pending => _jobs.Count;

    /// <summary>
    /// Runs <paramref name="work"/> on the main thread and returns its result. Returns false when the
    /// main thread did not start it within the time-out; it then never runs. Exceptions of the work
    /// are thrown here, on the calling thread.
    /// </summary>
    public bool TryInvoke<T>(Func<T> work, out T result)
    {
        T value = default!;
        var job = new Job(() => value = work());
        _jobs.Enqueue(job);
        if (!job.Done.Wait(Timeout) && job.TryCancel())
        {
            result = default!;
            return false;
        }

        // Started in time: wait for the end, however long the work takes.
        job.Done.Wait();
        job.Done.Dispose();
        if (job.Error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(job.Error).Throw();
        }

        result = value;
        return true;
    }

    /// <summary>Main thread: runs every job that is waiting. Returns how many ran.</summary>
    public int Pump()
    {
        int ran = 0;
        while (_jobs.TryDequeue(out Job? job))
        {
            if (job.TryStart())
            {
                job.Run();
                ran++;
            }
        }

        return ran;
    }

    private sealed class Job
    {
        private const int Waiting = 0;
        private const int Started = 1;
        private const int Cancelled = 2;

        private readonly Action _work;
        private int _state = Waiting;

        public Job(Action work) => _work = work;

        public ManualResetEventSlim Done { get; } = new(false);

        public Exception? Error { get; private set; }

        public bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Waiting) == Waiting;

        public bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Waiting) == Waiting;

        public void Run()
        {
            try
            {
                _work();
            }
            catch (Exception e)
            {
                Error = e;
            }
            finally
            {
                Done.Set();
            }
        }
    }
}
