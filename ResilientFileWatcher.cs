using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace X4LogWatcher
{
  public sealed class ResilientFileWatcher : IDisposable
  {
    private FileSystemWatcher? _watcher;
    private readonly string _path;
    private readonly string _filter;
    private readonly bool _includeSubdirs;
    private readonly int _debounceMs;
    private readonly SynchronizationContext? _syncContext;

    // Separate per-event-type debounce stores so one type doesn't suppress another
    private readonly ConcurrentDictionary<string, DateTime> _lastChangedTimes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastCreatedTimes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastDeletedTimes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRenamedTimes = new();

    private readonly NotifyFilters _notifyFilters =
      NotifyFilters.Attributes
      | NotifyFilters.CreationTime
      | NotifyFilters.DirectoryName
      | NotifyFilters.FileName
      | NotifyFilters.LastWrite
      | NotifyFilters.Security
      | NotifyFilters.Size;

    public event FileSystemEventHandler? Changed;
    public event FileSystemEventHandler? Created;
    public event FileSystemEventHandler? Deleted;
    public event RenamedEventHandler? Renamed;

    public ResilientFileWatcher(
      string path,
      string filter = "*.*",
      bool includeSubdirectories = false,
      int debounceMilliseconds = 200,
      SynchronizationContext? syncContext = null
    )
    {
      _path = path ?? throw new ArgumentNullException(nameof(path));
      _filter = filter ?? "*.*";
      _includeSubdirs = includeSubdirectories;
      _debounceMs = debounceMilliseconds;
      _syncContext = syncContext ?? SynchronizationContext.Current;
    }

    public void Start()
    {
      Stop();

      if (!Directory.Exists(_path))
        throw new DirectoryNotFoundException($"Path not found: {_path}");

      _watcher = new FileSystemWatcher(_path, _filter)
      {
        NotifyFilter = _notifyFilters,
        InternalBufferSize = 64 * 1024, // Max allowed
        IncludeSubdirectories = _includeSubdirs,
        EnableRaisingEvents = true,
      };

      _watcher.Changed += OnChangedInternal;
      _watcher.Created += OnCreatedInternal;
      _watcher.Deleted += OnDeletedInternal;
      _watcher.Renamed += OnRenamedInternal;
      _watcher.Error += OnErrorInternal;
    }

    public void Stop()
    {
      if (_watcher != null)
      {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChangedInternal;
        _watcher.Created -= OnCreatedInternal;
        _watcher.Deleted -= OnDeletedInternal;
        _watcher.Renamed -= OnRenamedInternal;
        _watcher.Error -= OnErrorInternal;
        _watcher.Dispose();
        _watcher = null;
      }
    }

    private void OnChangedInternal(object sender, FileSystemEventArgs e)
    {
      if (IsDuplicate(_lastChangedTimes, e.FullPath))
        return;
      PostToContext(() => Changed?.Invoke(this, e));
    }

    private void OnCreatedInternal(object sender, FileSystemEventArgs e)
    {
      if (IsDuplicate(_lastCreatedTimes, e.FullPath))
        return;
      PostToContext(() => Created?.Invoke(this, e));
    }

    private void OnDeletedInternal(object sender, FileSystemEventArgs e)
    {
      if (IsDuplicate(_lastDeletedTimes, e.FullPath))
        return;
      PostToContext(() => Deleted?.Invoke(this, e));
    }

    private void OnRenamedInternal(object sender, RenamedEventArgs e)
    {
      // Use the new full path for deduplication (old name bursts still treated separately)
      if (IsDuplicate(_lastRenamedTimes, e.FullPath))
        return;
      PostToContext(() => Renamed?.Invoke(this, e));
    }

    private void OnErrorInternal(object sender, ErrorEventArgs e)
    {
      // Log or handle error
      RestartAsync();
    }

    private bool IsDuplicate(ConcurrentDictionary<string, DateTime> dict, string path)
    {
      var now = DateTime.UtcNow;
      if (dict.TryGetValue(path, out var last) && (now - last).TotalMilliseconds < _debounceMs)
        return true;

      dict[path] = now;
      return false;
    }

    private void PostToContext(Action action)
    {
      if (_syncContext != null)
        _syncContext.Post(_ => action(), null);
      else
        action();
    }

    private async void RestartAsync()
    {
      Stop();
      await Task.Delay(500); // small backoff
      try
      {
        Start();
      }
      catch
      { /* swallow or log */
      }
    }

    public void Dispose() => Stop();
  }
}
