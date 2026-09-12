using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Centra.Generators;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    private readonly T[]? _items;

    public EquatableArray(T[] items)
    {
        _items = items;
    }

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? Array.Empty<T>())[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (ReferenceEquals(_items, other._items)) return true;
        if (_items is null || other._items is null) return false;
        if (_items.Length != other._items.Length) return false;

        for (var i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Equals(other._items[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items is null) return 0;
        var hash = 17;
        foreach (var item in _items)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
