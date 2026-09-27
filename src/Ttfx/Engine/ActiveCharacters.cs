using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ttfx.Engine;

/// <summary>
/// Ordered set for active characters. Iteration is ascending
/// <c>CharacterId</c> (the Python-compatible field), never arena index.
///
/// A packed bitmap over <c>CharacterId</c>: set bits iterate in ascending id
/// order with no tree nodes to allocate or chase, and membership by arena
/// slot is an array read.
/// Transcribed from <c>engine/active_characters.rs</c>.
/// </summary>
public sealed class ActiveCharacters
{
    private const uint Absent = uint.MaxValue;

    /// <summary>Bit per CharacterId.</summary>
    private ulong[] _words = [];
    /// <summary>Arena slot by CharacterId (meaningful where the bit is set).</summary>
    private uint[] _arenaByCharacterId = [];
    /// <summary>CharacterId by arena slot, <see cref="Absent"/> when not a member.</summary>
    private uint[] _characterIdByArena = [];
    private int _count;

    public int Count => _count;

    public bool IsEmpty => _count == 0;

    public void Clear()
    {
        Array.Clear(_words);
        Array.Fill(_characterIdByArena, Absent);
        _count = 0;
    }

    public bool Contains(CharId id) => MemberCharacterId(id.Value) != Absent;

    public bool Insert(CharId id, uint characterId)
    {
        if (MemberCharacterId(id.Value) != Absent)
        {
            return false;
        }

        int word = (int)(characterId / 64);
        if (word >= _words.Length)
        {
            Array.Resize(ref _words, Math.Max(word + 1, _words.Length * 2));
        }

        if (characterId >= _arenaByCharacterId.Length)
        {
            Array.Resize(ref _arenaByCharacterId, (int)Math.Max(characterId + 1, (uint)_arenaByCharacterId.Length * 2));
        }

        if (id.Value >= _characterIdByArena.Length)
        {
            int oldLength = _characterIdByArena.Length;
            Array.Resize(ref _characterIdByArena, (int)Math.Max(id.Value + 1, (uint)oldLength * 2));
            _characterIdByArena.AsSpan(oldLength).Fill(Absent);
        }

        _words[word] |= 1UL << (int)(characterId % 64);
        _arenaByCharacterId[characterId] = id.Value;
        _characterIdByArena[id.Value] = characterId;
        _count += 1;
        return true;
    }

    public bool Remove(CharId id)
    {
        uint characterId = MemberCharacterId(id.Value);
        if (characterId == Absent)
        {
            return false;
        }

        _words[characterId / 64] &= ~(1UL << (int)(characterId % 64));
        _characterIdByArena[id.Value] = Absent;
        _count -= 1;
        return true;
    }

    /// <summary>
    /// Snapshot taken before the walk (<c>ctx.rs:682-687</c>): ascending
    /// CharacterId order, then tick the copy so emissions can mutate membership.
    /// </summary>
    public CharId[] Snapshot()
    {
        var snapshot = new CharId[_count];
        int i = 0;
        for (int word = 0; word < _words.Length; word++)
        {
            ulong bits = _words[word];
            while (bits != 0)
            {
                uint characterId = (uint)(word * 64 + BitOperations.TrailingZeroCount(bits));
                snapshot[i++] = new CharId(_arenaByCharacterId[characterId]);
                bits &= bits - 1;
            }
        }

        return snapshot;
    }

    /// <summary>
    /// <see cref="Snapshot"/> into a caller-owned buffer, reused across frames.
    /// </summary>
    internal void SnapshotInto(List<CharId> snapshot)
    {
        snapshot.Clear();
        for (int word = 0; word < _words.Length; word++)
        {
            ulong bits = _words[word];
            while (bits != 0)
            {
                uint characterId = (uint)(word * 64 + BitOperations.TrailingZeroCount(bits));
                snapshot.Add(new CharId(_arenaByCharacterId[characterId]));
                bits &= bits - 1;
            }
        }
    }

    /// <summary>
    /// Retains elements in the same ascending CharacterId order in which
    /// <c>BTreeSet</c> invokes its predicate.
    /// </summary>
    public void Retain(Func<CharId, bool> keep)
    {
        for (int word = 0; word < _words.Length; word++)
        {
            ulong bits = _words[word];
            while (bits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                uint arena = _arenaByCharacterId[word * 64 + bit];
                if (!keep(new CharId(arena)))
                {
                    _words[word] &= ~(1UL << bit);
                    _characterIdByArena[arena] = Absent;
                    _count -= 1;
                }
            }
        }
    }

    /// <summary>
    /// <see cref="Retain"/> with <see cref="EffectCharacter.IsActive"/> as the
    /// predicate, without a delegate per frame.
    /// </summary>
    internal void RetainActive(List<EffectCharacter> arena)
    {
        for (int word = 0; word < _words.Length; word++)
        {
            ulong bits = _words[word];
            while (bits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                uint slot = _arenaByCharacterId[word * 64 + bit];
                if (!arena[(int)slot].IsActive())
                {
                    _words[word] &= ~(1UL << bit);
                    _characterIdByArena[slot] = Absent;
                    _count -= 1;
                }
            }
        }
    }

    private uint MemberCharacterId(uint arena) =>
        arena < _characterIdByArena.Length ? _characterIdByArena[arena] : Absent;
}
