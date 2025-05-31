using System;
using System.ComponentModel;
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
