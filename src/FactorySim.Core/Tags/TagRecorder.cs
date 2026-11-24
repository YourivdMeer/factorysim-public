using System.Globalization;
using System.Text;

namespace FactorySim.Core.Tags;

/// <summary>
/// Records the tags tick by tick, like a logic analyzer, and keeps only the changes.
///
/// The first tick (and the first one after <see cref="Clear"/>) stores every tag once, so each
/// tag has a value from the start. Changes older than the window, or beyond the maximum count,
/// drop off; the state they leave behind is kept as the value on the border (<see cref="Origin"/>).
/// Runs on the main thread only.
/// </summary>
public sealed class TagRecorder
{
    private readonly double _window;
    private readonly int _maxChanges;

    private string[] _tags = Array.Empty<string>();
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);
    private ChangeList[] _changes = Array.Empty<ChangeList>();

    /// <summary>Per tag the state on the border, once older changes have dropped off.</summary>
    private Entry?[] _border = Array.Empty<Entry?>();

    /// <summary>Tag index of every stored change, oldest first: which tag to trim next.</summary>
    private readonly Queue<int> _order = new();

    private bool _started;

    /// <param name="window">Seconds of simulation time to keep; older changes drop off.</param>
    /// <param name="maxChanges">Hard limit on the number of stored changes.</param>
    public TagRecorder(double window = 600.0, int maxChanges = 1_000_000)
    {
        if (window <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        if (maxChanges < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChanges));
        }

        _window = window;
        _maxChanges = maxChanges;
    }

    /// <summary>While true, <see cref="Record(double, TagStore)"/> stores nothing.</summary>
    public bool Paused { get; set; }

    /// <summary>Tag names in the order of the store.</summary>
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>Number of stored changes, not counting the states on the border.</summary>
    public int Count => _order.Count;

    /// <summary>Earliest time with a known value for every tag; NaN when nothing is recorded.</summary>
    public double Origin { get; private set; } = double.NaN;

    /// <summary>Time of the last recorded tick; NaN when nothing is recorded.</summary>
    public double End { get; private set; } = double.NaN;

    /// <summary>Records the tick that the store has just ended.</summary>
    public void Record(double time, TagStore store) => Record(time, store.Snapshot());

    /// <summary>Records one tick of tag states.</summary>
    public void Record(double time, IReadOnlyList<TagState> tags)
    {
        if (Paused)
        {
            return;
        }

        if (!SameTags(tags))
        {
            Start(tags);
        }

        for (int i = 0; i < tags.Count; i++)
        {
            TagState state = tags[i];
            int value = state.Kind == TagKind.Int ? state.EffectiveNumber : state.EffectiveValue ? 1 : 0;
            Entry? last = Last(i);
            if (_started && last is { } previous && previous.Value == value && previous.Forced == state.Forced)
            {
                continue;
            }

            _changes[i].Add(new Entry(time, value, state.Forced));
            _order.Enqueue(i);
        }

        if (!_started)
        {
            Origin = time;
            _started = true;
        }

        End = time;
        Trim(time - _window);
    }

    /// <summary>Forgets everything; the next tick stores every tag again.</summary>
    public void Clear()
    {
        _tags = Array.Empty<string>();
        _indexByName.Clear();
        _changes = Array.Empty<ChangeList>();
        _border = Array.Empty<Entry?>();
        _order.Clear();
        _started = false;
        Origin = double.NaN;
        End = double.NaN;
    }

    /// <summary>The value of a tag at a time; null before <see cref="Origin"/> or for an unknown tag.</summary>
    public int? ValueAt(string tag, double time) => StateAt(tag, time)?.Value;

    /// <summary>Whether the tag was forced at a time.</summary>
    public bool ForcedAt(string tag, double time) => StateAt(tag, time)?.Forced ?? false;

    /// <summary>Changes of a tag with from ≤ time ≤ to, oldest first; the state on the border counts as a change at <see cref="Origin"/>.</summary>
    public IReadOnlyList<TagChange> Changes(string tag, double from, double to)
    {
        var result = new List<TagChange>();
        if (!_indexByName.TryGetValue(tag, out int index))
        {
            return result;
        }

        if (_border[index] is { } border && from <= Origin && Origin <= to)
        {
            result.Add(new TagChange(Origin, tag, border.Value, border.Forced));
        }

        ChangeList list = _changes[index];
        for (int i = list.FirstAtOrAfter(from); i < list.Count; i++)
        {
            Entry entry = list[i];
            if (entry.Time > to)
            {
                break;
            }

            result.Add(new TagChange(entry.Time, tag, entry.Value, entry.Forced));
        }

        return result;
    }

    /// <summary>Times at which a tag goes from 0 to non-zero (rising) or back (falling).</summary>
    public IReadOnlyList<double> Edges(string tag, bool rising)
    {
        var result = new List<double>();
        if (!_indexByName.TryGetValue(tag, out int index))
        {
            return result;
        }

        bool? previous = _border[index] is { } border ? border.Value != 0 : null;
        ChangeList list = _changes[index];
        for (int i = 0; i < list.Count; i++)
        {
            Entry entry = list[i];
            bool high = entry.Value != 0;
            if (previous is { } before && before != high && high == rising)
            {
                result.Add(entry.Time);
            }

            previous = high;
        }

        return result;
    }

    /// <summary>
    /// The changes of the given tags in [from, to] as CSV: <c>Time;Tag;Value;Forced</c>, one line per
    /// change in rising time, time in seconds with a point and three decimals.
    /// </summary>
    public string ToCsv(IEnumerable<string> tags, double from, double to)
    {
        var rows = new List<TagChange>();
        foreach (string tag in tags)
        {
            rows.AddRange(Changes(tag, from, to));
        }

        var sb = new StringBuilder("Time;Tag;Value;Forced\n");
        foreach (TagChange change in rows.OrderBy(c => c.Time))
        {
            sb.Append(change.Time.ToString("0.000", CultureInfo.InvariantCulture)).Append(';')
                .Append(change.Tag).Append(';')
                .Append(change.Value.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(change.Forced ? '1' : '0').Append('\n');
        }

        return sb.ToString();
    }

    private Entry? StateAt(string tag, double time)
    {
        if (!_indexByName.TryGetValue(tag, out int index) || double.IsNaN(Origin) || time < Origin)
        {
            return null;
        }

        ChangeList list = _changes[index];
        int at = list.FirstAfter(time) - 1;
        return at >= 0 ? list[at] : _border[index];
    }

    private Entry? Last(int index)
    {
        ChangeList list = _changes[index];
        return list.Count > 0 ? list[list.Count - 1] : _border[index];
    }

    private bool SameTags(IReadOnlyList<TagState> tags)
    {
        if (tags.Count != _tags.Length)
        {
            return false;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            if (!string.Equals(tags[i].Name, _tags[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Start(IReadOnlyList<TagState> tags)
    {
        Clear();
        _tags = tags.Select(t => t.Name).ToArray();
        _changes = new ChangeList[_tags.Length];
        _border = new Entry?[_tags.Length];
        for (int i = 0; i < _tags.Length; i++)
        {
            _indexByName[_tags[i]] = i;
            _changes[i] = new ChangeList();
        }
    }

    /// <summary>Drops changes before the cutoff and beyond the maximum count, oldest first.</summary>
    private void Trim(double cutoff)
    {
        while (_order.Count > 0)
        {
            int index = _order.Peek();
            Entry oldest = _changes[index][0];
            if (_order.Count <= _maxChanges && oldest.Time >= cutoff)
            {
                return;
            }

            _order.Dequeue();
            _changes[index].RemoveFirst();
            _border[index] = oldest;
            Origin = Math.Max(Origin, oldest.Time);
        }
    }

    private readonly record struct Entry(double Time, int Value, bool Forced);

    /// <summary>A list in time order that drops from the front cheaply.</summary>
    private sealed class ChangeList
    {
        private readonly List<Entry> _items = new();
        private int _head;

        public int Count => _items.Count - _head;

        public Entry this[int index] => _items[_head + index];

        public void Add(Entry entry) => _items.Add(entry);

        public void RemoveFirst()
        {
            _head++;
            if (_head >= 1024 && _head * 2 >= _items.Count)
            {
                _items.RemoveRange(0, _head);
                _head = 0;
            }
        }

        /// <summary>Index of the first entry with time ≥ t (Count if none).</summary>
        public int FirstAtOrAfter(double t) => Search(t, inclusive: true);

        /// <summary>Index of the first entry with time &gt; t (Count if none).</summary>
        public int FirstAfter(double t) => Search(t, inclusive: false);

        private int Search(double t, bool inclusive)
        {
            int lo = 0;
            int hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                double time = this[mid].Time;
                bool before = inclusive ? time < t : time <= t;
                if (before)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }
}
