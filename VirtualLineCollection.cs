using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace X4LogWatcher
{
  /// <summary>
  /// A single line exposed by <see cref="VirtualLineCollection"/>.  Its identity is the
  /// absolute index in the <see cref="DiskBackedLineStore"/>, NOT the text, so that WPF's
  /// Selector can track selected rows in O(1) and duplicate log lines stay distinct.
  /// The text is fetched lazily from the store (and is rarely needed except for the row
  /// currently realized on screen or for a copy operation).
  /// </summary>
  public sealed class LineItem
  {
    private readonly DiskBackedLineStore _store;

    public int AbsoluteIndex { get; }

    public LineItem(DiskBackedLineStore store, int absoluteIndex)
    {
      _store = store;
      AbsoluteIndex = absoluteIndex;
    }

    /// <summary>The line text, read on demand from the disk-backed store.</summary>
    public string Text => _store.GetLine(AbsoluteIndex);

    public override string ToString() => Text;

    // Value equality on (store, absolute index) lets WPF match a freshly-created LineItem
    // (e.g. from SelectedItems.Add) against the instance held by a realized container.
    public override bool Equals(object? obj) =>
      obj is LineItem other && other.AbsoluteIndex == AbsoluteIndex && ReferenceEquals(other._store, _store);

    public override int GetHashCode() => AbsoluteIndex;
  }

  /// <summary>
  /// A non-generic IList backed by a DiskBackedLineStore. WPF's VirtualizingStackPanel
  /// calls the indexer only for visible rows, so main memory usage stays proportional
  /// to the number of visible lines rather than the total number of stored lines.
  /// Lines before <see cref="StartIndex"/> are hidden from the view but remain on disk.
  /// Items are <see cref="LineItem"/> wrappers so multi-row selection works correctly.
  /// </summary>
  public class VirtualLineCollection : IList, INotifyCollectionChanged
  {
    private readonly DiskBackedLineStore _store;
    private int _startIndex;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public VirtualLineCollection(DiskBackedLineStore store)
    {
      _store = store;
    }

    /// <summary>
    /// Absolute index of the first visible line. All lines before this index are hidden.
    /// </summary>
    public int StartIndex
    {
      get => _startIndex;
      set => _startIndex = Math.Max(0, value);
    }

    public int Count => Math.Max(0, _store.LineCount - _startIndex);

    public object? this[int index]
    {
      get => new LineItem(_store, index + _startIndex);
      set => throw new NotSupportedException();
    }

    /// <summary>
    /// Tell the ListView that the collection has changed so it re-queries visible items.
    /// Using Reset is simpler than tracking individual additions; with virtualization the
    /// panel only re-reads the small visible window.
    /// </summary>
    public void NotifyReset()
    {
      CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    // IList boilerplate — write operations not supported
    public bool IsReadOnly => true;
    public bool IsFixedSize => false;
    public object SyncRoot => this;
    public bool IsSynchronized => false;

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index) => throw new NotSupportedException();

    // Contains/IndexOf must work for WPF's Selector to track row selection.  Identity is the
    // absolute store index carried by the LineItem, so both are O(1) and duplicate-safe.
    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
      if (value is LineItem li)
      {
        int visible = li.AbsoluteIndex - _startIndex;
        if (visible >= 0 && visible < Count)
          return visible;
      }
      return -1;
    }

    public IEnumerator GetEnumerator()
    {
      int start = _startIndex;
      int count = Math.Max(0, _store.LineCount - start);
      for (int i = 0; i < count; i++)
        yield return new LineItem(_store, i + start);
    }
  }
}
