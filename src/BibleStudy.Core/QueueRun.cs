namespace BibleStudy.Core;

/// <summary>One walk through the reading list. Nothing leaves the list during the walk, so one can go back; the visited entries are taken off when it ends.</summary>
public sealed class QueueRun
{
    List<long> _ids;

    public QueueRun(IEnumerable<long> ids)
    {
        _ids = [.. ids];
        if (_ids.Count == 0) throw new ArgumentException("The list is empty", nameof(ids));
        Visited.Add(_ids[0]);
    }

    public HashSet<long> Visited { get; } = [];
    public int Position { get; private set; }
    public int Count => _ids.Count;
    public long Current => _ids[Position];
    public bool IsFirst => Position == 0;
    public bool IsLast => Position == _ids.Count - 1;

    /// <summary>Moves on by the given steps and counts the entry as visited; false when there is nothing in that direction.</summary>
    public bool Move(int delta)
    {
        var target = Position + delta;
        if (target < 0 || target >= _ids.Count) return false;
        Position = target;
        Visited.Add(Current);
        return true;
    }

    public bool MoveTo(long id)
    {
        var index = _ids.IndexOf(id);
        if (index < 0) return false;
        Position = index;
        Visited.Add(id);
        return true;
    }

    /// <summary>The list changed during the walk (entries added or removed): stay on the current entry, or at its place when it is gone.</summary>
    public void Sync(IEnumerable<long> ids)
    {
        var current = _ids.Count > 0 ? Current : (long?)null;
        _ids = [.. ids];
        Visited.IntersectWith(_ids);
        var index = current is long c ? _ids.IndexOf(c) : -1;
        Position = index >= 0 ? index : Math.Max(0, Math.Min(Position, _ids.Count - 1));
    }
}

/// <summary>Digits typed one after the other make one number: 2 is verse 2, 8 right after it verse 28. After a pause the next digit starts a new number.</summary>
public sealed class TypedNumber
{
    public const int ResetAfterMs = 1000;
    string _digits = "";
    long _lastAt;

    /// <summary>The number the digit leads to, or null if there is none. A digit that would make a number that does not exist starts a new one.</summary>
    public int? Type(int digit, long nowMs, Func<int, bool> exists)
    {
        var text = nowMs - _lastAt > ResetAfterMs ? "" : _digits;
        if (!int.TryParse(text + digit, out var number) || !exists(number)) { text = ""; number = digit; }
        if (!exists(number)) { _digits = ""; return null; }
        _digits = text + digit;
        _lastAt = nowMs;
        return number;
    }

    /// <summary>The pause counts from the moment the jump is done: a slow jump must not eat the time for the next digit.</summary>
    public void Finished(long nowMs) => _lastAt = nowMs;

    public void Reset() => _digits = "";
}
