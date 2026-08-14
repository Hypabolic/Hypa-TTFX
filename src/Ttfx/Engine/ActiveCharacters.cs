using System;
using System.Collections.Generic;

namespace Ttfx.Engine;

/// <summary>
/// Ordered set for active characters. Iteration is ascending
/// <c>CharacterId</c> (the Python-compatible field), never arena index
/// (plan.md §4.3 / §5.8). Bitmap promotion is dropped.
/// Transcribed from <c>engine/active_characters.rs</c>.
/// </summary>
public sealed class ActiveCharacters
{
    private readonly SortedSet<ActiveEntry> _set = new SortedSet<ActiveEntry>();
    private readonly Dictionary<uint, uint> _characterIdByArena = new Dictionary<uint, uint>();

    public int Count => _set.Count;

    public bool IsEmpty => _set.Count == 0;

    public void Clear()
    {
        _set.Clear();
        _characterIdByArena.Clear();
    }

    public bool Contains(CharId id) => _characterIdByArena.ContainsKey(id.Value);

    public bool Insert(CharId id, uint characterId)
    {
        if (_characterIdByArena.ContainsKey(id.Value))
        {
            return false;
        }

        _set.Add(new ActiveEntry(characterId, id.Value));
        _characterIdByArena[id.Value] = characterId;
        return true;
    }

    public bool Remove(CharId id)
    {
        if (!_characterIdByArena.TryGetValue(id.Value, out uint characterId))
        {
            return false;
        }

        _set.Remove(new ActiveEntry(characterId, id.Value));
        _characterIdByArena.Remove(id.Value);
        return true;
    }

    /// <summary>
    /// Snapshot taken before the walk (<c>ctx.rs:682-687</c>): ascending
    /// CharacterId order, then tick the copy so emissions can mutate membership.
    /// </summary>
    public CharId[] Snapshot()
    {
        var snapshot = new CharId[_set.Count];
        int i = 0;
        foreach (ActiveEntry entry in _set)
        {
            snapshot[i++] = new CharId(entry.ArenaId);
        }

        return snapshot;
    }

    /// <summary>
    /// Retains elements in the same ascending CharacterId order in which
    /// <c>BTreeSet</c> invokes its predicate.
    /// </summary>
    public void Retain(Func<CharId, bool> keep)
    {
        var remove = new List<ActiveEntry>();
        foreach (ActiveEntry entry in _set)
        {
            if (!keep(new CharId(entry.ArenaId)))
            {
                remove.Add(entry);
            }
        }

        // Length captured once: retain's removal pass does not emit (active_characters.rs:141).
        int count = remove.Count;
        for (int i = 0; i < count; i++)
        {
            _set.Remove(remove[i]);
            _characterIdByArena.Remove(remove[i].ArenaId);
        }
    }

    private readonly record struct ActiveEntry(uint CharacterId, uint ArenaId) : IComparable<ActiveEntry>
    {
        public int CompareTo(ActiveEntry other)
        {
            int cmp = CharacterId.CompareTo(other.CharacterId);
            return cmp != 0 ? cmp : ArenaId.CompareTo(other.ArenaId);
        }
    }
}
