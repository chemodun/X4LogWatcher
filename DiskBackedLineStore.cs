using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X4LogWatcher
{
  /// <summary>
  /// Stores matched log lines in a temporary file on disk, exposing random-access reads
  /// via a page cache so the UI only keeps a small working set in memory.
  /// </summary>
  public class DiskBackedLineStore : IDisposable
  {
    private readonly string _filePath;
    private readonly List<long> _lineOffsets = new();
    private FileStream? _writeStream;
    private readonly object _lock = new();
    private bool _disposed;

    private const int PageSize = 200;
    private const int MaxCachedPages = 15;
    private readonly Dictionary<int, string[]> _pageCache = new();

    public int LineCount
    {
      get
      {
        lock (_lock)
        {
          return _lineOffsets.Count;
        }
      }
    }

    public string FilePath => _filePath;

    public long FileSize
    {
      get
      {
        lock (_lock)
        {
          return _writeStream?.Position ?? 0;
        }
      }
    }

    public DiskBackedLineStore()
    {
      _filePath = Path.GetTempFileName();
      _writeStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    public void AppendLines(IEnumerable<string> lines)
    {
      lock (_lock)
      {
        if (_writeStream == null || _disposed)
          return;

        // Track the page that is currently being written to so we can invalidate it
        int dirtyPage = _lineOffsets.Count > 0 ? (_lineOffsets.Count - 1) / PageSize : 0;

        foreach (var line in lines)
        {
          _lineOffsets.Add(_writeStream.Position);
          var bytes = Encoding.UTF8.GetBytes(line + "\n");
          _writeStream.Write(bytes);
        }
        _writeStream.Flush();

        // Invalidate any cached pages that were modified
        _pageCache.Remove(dirtyPage);
        if (_lineOffsets.Count > 0)
          _pageCache.Remove((_lineOffsets.Count - 1) / PageSize);
      }
    }

    public string GetLine(int index)
    {
      lock (_lock)
      {
        if (index < 0 || index >= _lineOffsets.Count)
          return string.Empty;
        int pageIndex = index / PageSize;
        int pageOffset = index % PageSize;
        var page = GetOrLoadPage(pageIndex);
        return pageOffset < page.Length ? page[pageOffset] : string.Empty;
      }
    }

    private string[] GetOrLoadPage(int pageIndex)
    {
      if (_pageCache.TryGetValue(pageIndex, out var cached))
        return cached;

      // Evict the lowest-indexed (oldest) page when cache is full
      if (_pageCache.Count >= MaxCachedPages)
      {
        int minKey = _pageCache.Keys.Min();
        _pageCache.Remove(minKey);
      }

      var page = LoadPageFromDisk(pageIndex);
      _pageCache[pageIndex] = page;
      return page;
    }

    private string[] LoadPageFromDisk(int pageIndex)
    {
      int startLine = pageIndex * PageSize;
      int count = Math.Min(PageSize, _lineOffsets.Count - startLine);
      if (count <= 0)
        return Array.Empty<string>();

      long startOffset = _lineOffsets[startLine];
      int endLine = startLine + count;
      long endOffset = endLine < _lineOffsets.Count ? _lineOffsets[endLine] : _writeStream!.Position;
      int byteCount = (int)(endOffset - startOffset);
      if (byteCount <= 0)
        return Array.Empty<string>();

      var buffer = new byte[byteCount];
      try
      {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(startOffset, SeekOrigin.Begin);
        _ = fs.Read(buffer, 0, byteCount);
      }
      catch
      {
        return Array.Empty<string>();
      }

      return Encoding.UTF8.GetString(buffer).Split('\n').Take(count).Select(l => l.TrimEnd('\r')).ToArray();
    }

    public void Clear()
    {
      lock (_lock)
      {
        _lineOffsets.Clear();
        _pageCache.Clear();
        _writeStream?.SetLength(0);
        _writeStream?.Seek(0, SeekOrigin.Begin);
      }
    }

    /// <summary>
    /// Yields the 0-based index of every line that contains <paramref name="term"/>.
    /// </summary>
    public IEnumerable<int> Search(string term, bool matchCase)
    {
      StringComparison cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      int count;
      lock (_lock)
      {
        count = _lineOffsets.Count;
      }
      for (int i = 0; i < count; i++)
        if (GetLine(i).Contains(term, cmp))
          yield return i;
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!_disposed)
      {
        if (disposing)
        {
          _writeStream?.Dispose();
          _writeStream = null;
        }
        try
        {
          File.Delete(_filePath);
        }
        catch { }
        _disposed = true;
      }
    }

    ~DiskBackedLineStore() => Dispose(false);
  }
}
