using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MahApps.Metro.Controls;

namespace X4LogWatcher
{
  /// <summary>
  /// Class to hold all information related to a tab in the log watcher
  /// </summary>
  public class TabInfo : INotifyPropertyChanged, IDisposable
  {
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _disposed = false;

    private string _regexPattern = string.Empty;
    public string RegexPattern
    {
      get => _regexPattern;
      set
      {
        _regexPattern = value;
        OnPropertyChanged(nameof(RegexPattern));
      }
    }

    private string _tabName = string.Empty;
    public string TabName
    {
      get => _tabName;
      set
      {
        _tabName = value;
        OnPropertyChanged(nameof(TabName));
      }
    }

    // Property to indicate if this tab was auto-created by an auto-tab rule
    private bool _isAutoCreated;
    public bool IsAutoCreated
    {
      get => _isAutoCreated;
      set
      {
        if (_isAutoCreated != value)
        {
          _isAutoCreated = value;
          OnPropertyChanged(nameof(IsAutoCreated));
          UpdateTabHeader();
        }
      }
    }

    private bool _isWatchingEnabled;
    public bool IsWatchingEnabled
    {
      get => _isWatchingEnabled;
      set
      {
        _isWatchingEnabled = value;
        OnPropertyChanged(nameof(IsWatchingEnabled));

        // If watching is enabled and the file changed flag is set, reset file position and reload content
        if (_isWatchingEnabled && FileChangedFlag)
        {
          FilePosition = 0;
          ClearContent();
          FileChangedFlag = false;
          // The actual processing will be done by the MainWindow class
        }
      }
    }

    private int _afterLines;

    /// <summary>
    /// Number of lines to display after a matching line
    /// </summary>
    public int AfterLines
    {
      get => _afterLines;
      set
      {
        if (_afterLines != value)
        {
          _afterLines = value;
          OnPropertyChanged(nameof(AfterLines));
        }
      }
    }

    // Counter for tracking remaining lines to include after a match
    public int AfterLinesCurrent { get; set; }

    public long FilePosition { get; set; }
    public Regex? CompiledRegex { get; private set; }

    // Flag to indicate that the file has changed since last processed
    public bool FileChangedFlag { get; set; }

    // Flag to indicate that new content has arrived since last viewed
    private bool _hasNewContent;
    public bool HasNewContent
    {
      get => _hasNewContent;
      set
      {
        if (_hasNewContent != value)
        {
          _hasNewContent = value;
          OnPropertyChanged(nameof(HasNewContent));

          // Update visual indication when this property changes
          UpdateNewContentIndicator();
        }
      }
    }

    // UI controls
    public MetroTabItem TabItem { get; }
    public TextBox RegexTextBox { get; }
    public NumericUpDown AfterLinesNumericUpDown { get; }
    public TextBox NameTextBox { get; }
    public CheckBox WatchingCheckBox { get; }
    public TextBox ContentTextBox { get; }

    // Validation state
    public bool IsRegexValid { get; private set; }

    // Virtual content management for large files
    private readonly string? _tempFilePath;
    private long _totalContentLength;
    private const long MAX_MEMORY_CONTENT = 25_000_000; // 25MB in memory
    private const long KEEP_RECENT_CONTENT = 5_000_000; // Keep last 5MB in memory
    private bool _isUsingDiskStorage = false;

    /// <summary>
    /// Creates a new TabInfo with references to all controls
    /// </summary>
    public TabInfo(
      MetroTabItem tabItem,
      CheckBox watchingCheckBox,
      TextBox nameTextBox,
      TextBox regexTextBox,
      NumericUpDown afterLinesTextBox,
      TextBox contentTextBox,
      bool isAutoCreated = false
    )
    {
      TabItem = tabItem;
      RegexTextBox = regexTextBox;
      AfterLinesNumericUpDown = afterLinesTextBox;
      WatchingCheckBox = watchingCheckBox;
      ContentTextBox = contentTextBox;
      NameTextBox = nameTextBox;

      TabName = NameTextBox.Text;
      RegexPattern = RegexTextBox.Text;
      IsWatchingEnabled = watchingCheckBox.IsChecked ?? false;
      FilePosition = 0;
      FileChangedFlag = false;
      HasNewContent = false;
      AfterLines = afterLinesTextBox.Value.HasValue ? (int)afterLinesTextBox.Value : 0;
      AfterLinesCurrent = 0;
      IsAutoCreated = isAutoCreated;

      // Initialize virtual content management
      _tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"X4LogWatcher_Tab_{Guid.NewGuid():N}.tmp");
      _totalContentLength = 0;
      _isUsingDiskStorage = false;

      // Wire up events
      RegexTextBox.TextChanged += RegexTextBox_TextChanged;

      NameTextBox.TextChanged += NameTextBox_TextChanged;

      AfterLinesNumericUpDown.ValueChanged += AfterLinesNumericUpDown_ValueChanged;

      WatchingCheckBox.Unchecked += WatchingCheckBox_Unchecked;

      // Initialize regex
      UpdateRegex();

      // Initialize tab header
      UpdateTabHeader();
    }

    protected void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NameTextBox_TextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      if (sender is TextBox nameTextBox)
      {
        TabName = nameTextBox.Text;
        UpdateTabHeader();
      }
    }

    private void AfterLinesNumericUpDown_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double?> e)
    {
      if (sender is NumericUpDown afterLinesNumericUpDown)
      {
        if (afterLinesNumericUpDown.Value.HasValue)
        {
          AfterLines = (int)(afterLinesNumericUpDown.Value ?? AfterLines);
        }
        else
        {
          afterLinesNumericUpDown.Value = AfterLines;
        }
      }
    }

    private void RegexTextBox_TextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      // Update the regex pattern
      RegexPattern = RegexTextBox.Text;
    }

    /// <summary>
    /// Updates the tab header to display name if available, otherwise regex pattern
    /// </summary>
    public void UpdateTabHeader()
    {
      string displayText;
      if (!string.IsNullOrWhiteSpace(TabName))
      {
        displayText = TabName;
      }
      else
      {
        displayText = RegexPattern;
      }

      // Add an indicator for auto-created tabs
      if (IsAutoCreated)
      {
        displayText = "🔄 " + displayText; // Recycling symbol to indicate auto-created tab
        TabItem.Foreground = Brushes.Navy; // Different color for auto tabs
      }
      else
      {
        TabItem.Foreground = Brushes.Black; // Default color for normal tabs
      }

      // Add a prominent indicator for new content
      if (HasNewContent)
      {
        displayText = "🔔 " + displayText; // Warning symbol to indicate attention needed
        HeaderedControlHelper.SetHeaderFontWeight(TabItem, FontWeights.Bold);
      }
      else
      {
        HeaderedControlHelper.SetHeaderFontWeight(TabItem, FontWeights.Normal);
      }

      TabItem.Header = displayText;
    }

    /// <summary>
    /// Updates the compiled regex when pattern changes
    /// </summary>
    public bool UpdateRegex()
    {
      RegexPattern = RegexTextBox.Text;

      try
      {
        CompiledRegex = new Regex(RegexPattern);
        IsRegexValid = true;
        return true;
      }
      catch
      {
        IsRegexValid = false;
        return false;
      }
    }

    /// <summary>
    /// Validates the current regex pattern
    /// </summary>
    public bool ValidateRegex()
    {
      try
      {
        CompiledRegex = new Regex(RegexPattern);
        IsRegexValid = true;
        return true;
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Invalid Regex: {ex.Message}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        IsRegexValid = false;
        return false;
      }
    }

    private void WatchingCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
      IsWatchingEnabled = false;
    }

    /// <summary>
    /// Appends matching lines from a log file to the content text box
    /// </summary>
    public void AppendContent(string text)
    {
      if (_disposed)
        return;

      ContentTextBox.AppendText(text);
      ContentTextBox.ScrollToEnd();

      // Check if we need to optimize memory after adding content
      OptimizeMemoryIfNeeded();
    }

    /// <summary>
    /// Clears the content text box
    /// </summary>
    public void ClearContent()
    {
      if (_disposed)
        return;

      ContentTextBox.Clear();
    }

    /// <summary>
    /// Makes the tab name bold to indicate new content
    /// </summary>
    public void SetHasNewContent(bool hasNew)
    {
      HasNewContent = hasNew;
      // UpdateTabHeader is called by HasNewContent property setter via UpdateNewContentIndicator
    }

    /// <summary>
    /// Updates the visual indicator for new content
    /// </summary>
    private void UpdateNewContentIndicator()
    {
      // Just update the tab header text which includes the indicator when HasNewContent is true
      UpdateTabHeader();
    }

    /// <summary>
    /// Get estimated memory usage of this tab's content
    /// </summary>
    /// <returns>Estimated memory usage in bytes</returns>
    public long GetEstimatedMemoryUsage()
    {
      if (_disposed)
        return 0;

      try
      {
        // Estimate memory usage: string content + UI overhead
        var textLength = ContentTextBox?.Text?.Length ?? 0;
        return textLength * 2; // Unicode characters are 2 bytes each
      }
      catch
      {
        return 0;
      }
    }

    /// <summary>
    /// Optimize memory usage by moving old content to disk if needed
    /// </summary>
    public void OptimizeMemoryIfNeeded()
    {
      if (_disposed || ContentTextBox == null)
        return;

      try
      {
        var currentLength = ContentTextBox.Text.Length;

        if (currentLength > MAX_MEMORY_CONTENT)
        {
          // Keep only the most recent content in memory
          var text = ContentTextBox.Text;
          var keepFromIndex = Math.Max(0, currentLength - (int)KEEP_RECENT_CONTENT);

          // Find a good line boundary to cut at
          var cutIndex = text.LastIndexOf('\n', keepFromIndex);
          if (cutIndex < 0)
            cutIndex = keepFromIndex;

          var oldContent = text.Substring(0, cutIndex);
          var recentContent = text.Substring(cutIndex);

          // Save old content to disk if we have a temp file path
          if (!string.IsNullOrEmpty(_tempFilePath))
          {
            File.AppendAllText(_tempFilePath, oldContent);
            _isUsingDiskStorage = true;
          }

          // Keep only recent content in memory
          ContentTextBox.Text = recentContent;
          _totalContentLength += oldContent.Length;

          System.Diagnostics.Debug.WriteLine(
            $"Tab '{TabName}' optimized: moved {oldContent.Length} chars to disk, kept {recentContent.Length} in memory"
          );
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error optimizing memory for tab '{TabName}': {ex.Message}");
      }
    }

    /// <summary>
    /// Get the full content including disk-stored content (expensive operation)
    /// </summary>
    public string GetFullContent()
    {
      if (_disposed)
        return string.Empty;

      try
      {
        var memoryContent = ContentTextBox?.Text ?? string.Empty;

        if (_isUsingDiskStorage && !string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
        {
          var diskContent = File.ReadAllText(_tempFilePath);
          return diskContent + memoryContent;
        }

        return memoryContent;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error reading full content for tab '{TabName}': {ex.Message}");
        return ContentTextBox?.Text ?? string.Empty;
      }
    }

    #region IDisposable Implementation

    /// <summary>
    /// Dispose of resources and unsubscribe from event handlers
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method
    /// </summary>
    /// <param name="disposing">True if disposing from Dispose() method, false if from finalizer</param>
    protected virtual void Dispose(bool disposing)
    {
      if (!_disposed && disposing)
      {
        try
        {
          // Unsubscribe from all event handlers to prevent memory leaks
          if (RegexTextBox != null)
          {
            RegexTextBox.TextChanged -= RegexTextBox_TextChanged;
          }

          if (NameTextBox != null)
          {
            NameTextBox.TextChanged -= NameTextBox_TextChanged;
          }

          if (AfterLinesNumericUpDown != null)
          {
            AfterLinesNumericUpDown.ValueChanged -= AfterLinesNumericUpDown_ValueChanged;
          }

          if (WatchingCheckBox != null)
          {
            WatchingCheckBox.Unchecked -= WatchingCheckBox_Unchecked;
          }

          // Clear property change event handlers
          PropertyChanged = null;

          // Force garbage collection of large content if present
          if (ContentTextBox != null && ContentTextBox.Text.Length > 10_000_000) // 10MB threshold
          {
            ContentTextBox.Clear();
          }

          // Clean up temporary file if it exists
          if (_isUsingDiskStorage && !string.IsNullOrEmpty(_tempFilePath))
          {
            try
            {
              if (File.Exists(_tempFilePath))
              {
                File.Delete(_tempFilePath);
              }
            }
            catch (Exception deleteEx)
            {
              System.Diagnostics.Debug.WriteLine($"Error deleting temp file {_tempFilePath}: {deleteEx.Message}");
            }
          }
        }
        catch (Exception ex)
        {
          // Log but don't throw during disposal
          System.Diagnostics.Debug.WriteLine($"Error during TabInfo disposal: {ex.Message}");
        }

        _disposed = true;
      }
    }

    /// <summary>
    /// Finalizer to ensure disposal if Dispose() wasn't called
    /// </summary>
    ~TabInfo()
    {
      Dispose(false);
    }

    #endregion
  }
}
