using System;
using System.Collections.Generic;

namespace Ttfx.Utils;

/// <summary>
/// Insertion-ordered string-keyed map with Python-dict iteration semantics.
/// Used everywhere upstream iterates dict values: motion.paths,
/// animation.scenes, effect-level dicts.
/// Representation half of the Rust index-threshold / pointer-equality cache is
/// dropped; insert/overwrite/remove order is the semantic half.
/// Transcribed from <c>utils/ordered_map.rs</c>.
/// </summary>
public sealed class OrderedMap<T>
{
    private readonly List<(string Key, T Value)> _entries = new List<(string, T)>();
    private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public bool IsEmpty => _entries.Count == 0;

    public bool ContainsKey(string key) => _index.ContainsKey(key);

    /// <summary>Python dict semantics: overwriting an existing key keeps its position.</summary>
    public void Insert(string key, T value)
    {
        if (_index.TryGetValue(key, out int position))
        {
            _entries[position] = (key, value);
            return;
        }

        _index[key] = _entries.Count;
        _entries.Add((key, value));
    }

    public T? Get(string key)
    {
        return _index.TryGetValue(key, out int position) ? _entries[position].Value : default;
    }

    /// <summary>
    /// The map's own handle for <paramref name="key"/>, for callers that want
    /// later lookups to use the stored allocation.
    /// </summary>
    public string? SharedKey(string key)
    {
        return _index.TryGetValue(key, out int position) ? _entries[position].Key : null;
    }

    /// <summary>
    /// Entry slot for <paramref name="key"/>. Slots stay valid until an entry is removed.
    /// </summary>
    public int? Slot(string key)
    {
        return _index.TryGetValue(key, out int position) ? position : null;
    }

    public T At(int slot) => _entries[slot].Value;

    public string KeyAt(int slot) => _entries[slot].Key;

    public IEnumerable<string> Keys
    {
        get
        {
            // Length captured once: values() iteration does not emit events.
            int count = _entries.Count;
            for (int i = 0; i < count; i++)
            {
                yield return _entries[i].Key;
            }
        }
    }

    public IEnumerable<T> Values
    {
        get
        {
            int count = _entries.Count;
            for (int i = 0; i < count; i++)
            {
                yield return _entries[i].Value;
            }
        }
    }

    /// <summary>
    /// Python dict.pop(key, None): removes the entry, preserving the order of
    /// the remaining entries.
    /// </summary>
    public T? Remove(string key)
    {
        if (!_index.TryGetValue(key, out int pos))
        {
            return default;
        }

        T value = _entries[pos].Value;
        _entries.RemoveAt(pos);
        _index.Remove(key);
        for (int i = pos; i < _entries.Count; i++)
        {
            _index[_entries[i].Key] = i;
        }

        return value;
    }

    public void Clear()
    {
        _entries.Clear();
        _index.Clear();
    }
}
