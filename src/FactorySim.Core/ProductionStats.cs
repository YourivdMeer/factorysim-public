using System.Globalization;
using System.Text;

namespace FactorySim.Core;

/// <summary>
/// Production figures of a line (plan 11): throughput per remover, lead time from emitter to remover,
/// on-time per actuator tag and the items that fell. It gets events with the simulation time and keeps
/// no reference to the scene. Run time and on-time grow only by the <c>dt</c> of <see cref="Tick"/>,
/// so a paused simulation (dt = 0) adds nothing.
/// </summary>
public sealed class ProductionStats
{
    /// <summary>Length of the sliding window for <see cref="RemoverStats.PerMinuteWindow"/>, in seconds.</summary>
    public const double WindowSeconds = 60.0;

    private readonly List<RemoverCounter> _removers = new();
    private readonly Dictionary<string, RemoverCounter> _removerByName = new();
    private readonly List<ActuatorCounter> _actuators = new();
    private readonly Dictionary<string, ActuatorCounter> _actuatorByTag = new();

    /// <summary>Simulation seconds the line ran: the sum of the dt of every tick.</summary>
    public double RunTime { get; private set; }

    /// <summary>Simulation time of the latest tick or event.</summary>
    public double Now { get; private set; }

    public int EmittedCount { get; private set; }

    public int RemovedCount => _removers.Sum(r => r.Count);

    /// <summary>Items that left the line, each counted once.</summary>
    public int FallenCount { get; private set; }

    /// <summary>All removed items per minute over the whole run time.</summary>
    public double PerMinute => PerMinuteOver(RemovedCount, RunTime);

    /// <summary>All removed items per minute over the last 60 s.</summary>
    public double PerMinuteWindow => PerMinuteOver(_removers.Sum(r => r.InWindow(Now)), Math.Min(RunTime, WindowSeconds));

    public IReadOnlyList<RemoverStats> Removers => _removers.Select(Snapshot).ToArray();

    public IReadOnlyList<ActuatorStats> Actuators =>
        _actuators.Select(a => new ActuatorStats(a.Tag, a.OnSeconds, RunTime > 0.0 ? a.OnSeconds / RunTime : 0.0)).ToArray();

    /// <summary>All removers together, as one row named "total".</summary>
    public RemoverStats Total
    {
        get
        {
            List<double> all = _removers.SelectMany(r => r.LeadTimes).ToList();
            return new RemoverStats("total", RemovedCount, PerMinute, PerMinuteWindow, all.Count,
                all.Count > 0 ? all.Average() : null,
                all.Count > 0 ? all.Min() : null,
                all.Count > 0 ? all.Max() : null);
        }
    }

    /// <summary>Makes a remover known, so it shows with zeros before its first item. Does nothing if it is known.</summary>
    public void AddRemover(string name) => RemoverNamed(name);

    public RemoverStats? Remover(string name) =>
        _removerByName.TryGetValue(name, out RemoverCounter? counter) ? Snapshot(counter) : null;

    public void Emitted(string emitter, double time)
    {
        EmittedCount++;
        See(time);
    }

    /// <summary>
    /// An item reached a remover. A spawn time that is NaN, negative or after <paramref name="time"/> is
    /// unknown: the item counts, but has no lead time.
    /// </summary>
    public void Removed(string remover, double spawnTime, double time)
    {
        RemoverCounter counter = RemoverNamed(remover);
        counter.Count++;
        counter.Times.Enqueue(time);
        if (!double.IsNaN(spawnTime) && spawnTime >= 0.0 && spawnTime <= time)
        {
            counter.LeadTimes.Add(time - spawnTime);
        }

        See(time);
    }

    public void Fallen(double time)
    {
        FallenCount++;
        See(time);
    }

    /// <summary>One tick of <paramref name="dt"/> simulation seconds that ended at <paramref name="time"/>, with the state of the actuators.</summary>
    public void Tick(double time, double dt, IEnumerable<(string Tag, bool On)> actuators)
    {
        See(time);
        if (dt <= 0.0)
        {
            return;
        }

        RunTime += dt;
        foreach ((string tag, bool on) in actuators)
        {
            if (!_actuatorByTag.TryGetValue(tag, out ActuatorCounter? counter))
            {
                counter = new ActuatorCounter(tag);
                _actuatorByTag[tag] = counter;
                _actuators.Add(counter);
            }

            if (on)
            {
                counter.OnSeconds += dt;
            }
        }
    }

    /// <summary>Forgets everything: run time, counts, removers and actuators.</summary>
    public void Reset()
    {
        _removers.Clear();
        _removerByName.Clear();
        _actuators.Clear();
        _actuatorByTag.Clear();
        RunTime = 0.0;
        Now = 0.0;
        EmittedCount = 0;
        FallenCount = 0;
    }

    /// <summary>
    /// One row for the total, one per remover and one per actuator tag; separator ';', decimal point,
    /// times in seconds. Empty cells where a figure does not apply.
    /// </summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("Kind;Name;Count;PerMinute;PerMinuteLast60s;LeadTimeAvg;LeadTimeMin;LeadTimeMax;OnSeconds;OnPercent\n");
        AppendRemover(sb, "total", $"Run time {Number(RunTime)} s, emitted {EmittedCount}, fallen {FallenCount}", Total);
        foreach (RemoverStats remover in Removers)
        {
            AppendRemover(sb, "remover", remover.Name, remover);
        }

        foreach (ActuatorStats actuator in Actuators)
        {
            sb.Append("actuator;").Append(actuator.Tag).Append(";;;;;;;")
                .Append(Number(actuator.OnSeconds)).Append(';')
                .Append((actuator.OnFraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendRemover(StringBuilder sb, string kind, string name, RemoverStats stats)
    {
        sb.Append(kind).Append(';').Append(name).Append(';')
            .Append(stats.Count.ToString(CultureInfo.InvariantCulture)).Append(';')
            .Append(Number(stats.PerMinute)).Append(';')
            .Append(Number(stats.PerMinuteWindow)).Append(';')
            .Append(Number(stats.LeadTimeAverage)).Append(';')
            .Append(Number(stats.LeadTimeMin)).Append(';')
            .Append(Number(stats.LeadTimeMax)).Append(";;\n");
    }

    private static string Number(double? value) =>
        value is { } v ? v.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty;

    private static double PerMinuteOver(int count, double seconds) => seconds > 0.0 ? count * 60.0 / seconds : 0.0;

    private void See(double time) => Now = Math.Max(Now, time);

    private RemoverCounter RemoverNamed(string name)
    {
        if (!_removerByName.TryGetValue(name, out RemoverCounter? counter))
        {
            counter = new RemoverCounter(name);
            _removerByName[name] = counter;
            _removers.Add(counter);
        }

        return counter;
    }

    private RemoverStats Snapshot(RemoverCounter counter)
    {
        List<double> leads = counter.LeadTimes;
        return new RemoverStats(counter.Name, counter.Count, PerMinuteOver(counter.Count, RunTime),
            PerMinuteOver(counter.InWindow(Now), Math.Min(RunTime, WindowSeconds)), leads.Count,
            leads.Count > 0 ? leads.Average() : null,
            leads.Count > 0 ? leads.Min() : null,
            leads.Count > 0 ? leads.Max() : null);
    }

    /// <summary>Running counts of one remover.</summary>
    private sealed class RemoverCounter
    {
        public RemoverCounter(string name) => Name = name;

        public string Name { get; }

        public int Count { get; set; }

        /// <summary>Removal times, oldest first; pruned to the window when asked.</summary>
        public Queue<double> Times { get; } = new();

        public List<double> LeadTimes { get; } = new();

        public int InWindow(double now)
        {
            while (Times.Count > 0 && Times.Peek() <= now - WindowSeconds)
            {
                Times.Dequeue();
            }

            return Times.Count;
        }
    }

    /// <summary>Running on-time of one actuator tag.</summary>
    private sealed class ActuatorCounter
    {
        public ActuatorCounter(string tag) => Tag = tag;

        public string Tag { get; }

        public double OnSeconds { get; set; }
    }
}
