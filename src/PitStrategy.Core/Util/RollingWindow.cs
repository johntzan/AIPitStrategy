using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PitStrategy.Core.Util
{
    /// <summary>
    /// Fixed-capacity FIFO that drops the oldest sample when the capacity is exceeded.
    /// Used by FuelTracker to bound memory and keep recent-window stats cheap.
    /// </summary>
    public sealed class RollingWindow<T> : IEnumerable<T>
    {
        private readonly Queue<T> _items;

        public RollingWindow(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be >= 1");
            Capacity = capacity;
            _items = new Queue<T>(capacity);
        }

        public int Capacity { get; }
        public int Count => _items.Count;
        public bool IsFull => _items.Count >= Capacity;

        public void Add(T item)
        {
            if (_items.Count >= Capacity)
            {
                _items.Dequeue();
            }
            _items.Enqueue(item);
        }

        public void Clear() => _items.Clear();

        public IReadOnlyList<T> Snapshot() => _items.ToArray();

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
