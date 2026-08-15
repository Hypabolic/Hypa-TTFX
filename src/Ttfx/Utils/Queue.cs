using System.Collections.Generic;
using Ttfx.Engine;

namespace Ttfx.Utils;

/// <summary>
/// FIFO queue mirroring Rust <c>VecDeque</c> push_back / pop_front sites.
/// </summary>
public sealed class Queue<T>
{
    private readonly List<T> _items = new List<T>();

    public int Count => _items.Count;

    public bool IsEmpty => _items.Count == 0;

    public void PushBack(T item) => _items.Add(item);

    public T PopFront()
    {
        if (_items.Count == 0)
        {
            throw new EngineInvariantException("queue is empty");
        }

        T item = _items[0];
        _items.RemoveAt(0);
        return item;
    }
}
