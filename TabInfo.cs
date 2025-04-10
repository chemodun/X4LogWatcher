using System;
using System.ComponentModel;
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
  public class TabInfo : INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler? PropertyChanged;

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

          // If this tab is watching and the value changed, refresh the content
          if (IsWatchingEnabled)
          {
            FilePosition = 0;
            ClearContent();
          }
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

    // Timer for delayed regex refresh
    private System.Windows.Threading.DispatcherTimer? regexRefreshTimer;

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
      string pattern,
      int afterLines,
      bool enabled
    )
    {
      TabItem = tabItem;
      RegexTextBox = regexTextBox;
      AfterLinesNumericUpDown = afterLinesTextBox;
      WatchingCheckBox = watchingCheckBox;
      ContentTextBox = contentTextBox;
      NameTextBox = nameTextBox;

      RegexPattern = pattern;
      IsWatchingEnabled = enabled;
      FilePosition = 0;
      FileChangedFlag = false;
      HasNewContent = false;
      AfterLines = afterLines;
      AfterLinesCurrent = 0;

      // Initialize timer for delayed refresh
      regexRefreshTimer = new System.Windows.Threading.DispatcherTimer
      {
        Interval = TimeSpan.FromMilliseconds(800), // 800ms delay before refreshing
        IsEnabled = false,
      };
      regexRefreshTimer.Tick += RegexRefreshTimer_Tick;

      // Wire up events
      RegexTextBox.TextChanged += RegexTextBox_TextChanged;

      NameTextBox.TextChanged += NameTextBox_TextChanged;

      AfterLinesNumericUpDown.ValueChanged += AfterLinesNumericUpDown_ValueChanged;

      WatchingCheckBox.Checked += (sender, e) => IsWatchingEnabled = true;
      WatchingCheckBox.Unchecked += (sender, e) => IsWatchingEnabled = false;

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

      // Update the tab header (in case we're displaying the regex)
      UpdateTabHeader();

      // Reset and restart the timer to delay content refresh
      if (regexRefreshTimer != null)
      {
        regexRefreshTimer.Stop();
        regexRefreshTimer.Start();
      }
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

      // Add a prominent indicator for new content
      if (HasNewContent)
      {
        displayText = "🔔 " + displayText; // Warning symbol to indicate attention needed
      }

      TabItem.Header = displayText;
    }

    private void RegexRefreshTimer_Tick(object? sender, EventArgs e)
    {
      // Stop the timer
      if (regexRefreshTimer != null)
        regexRefreshTimer.Stop();

      // Update regex first
      if (UpdateRegex() && IsWatchingEnabled)
      {
        // Signal for content refresh
        WatchingCheckBox.IsChecked = false;
        WatchingCheckBox.IsChecked = true;
      }
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

    /// <summary>
    /// Appends matching lines from a log file to the content text box
    /// </summary>
    public void AppendContent(string text)
    {
      ContentTextBox.AppendText(text);
      ContentTextBox.ScrollToEnd();
    }

    /// <summary>
    /// Clears the content text box
    /// </summary>
    public void ClearContent()
    {
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
  }
}
