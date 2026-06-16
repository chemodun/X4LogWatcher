using System;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace X4LogWatcher
{
  /// <summary>
  /// Class to hold all information related to a tab in the log watcher
  /// </summary>
  public class TabInfo : INotifyPropertyChanged, IDisposable
  {
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _disposed;
    private bool _scrollDetectionSetUp;
    private bool _shouldFollowBottom = true; // tracks whether the user was at the bottom last time the tab was visible

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
          IsInMultilineSequence = false;
          ClearContent();
          FileChangedFlag = false;
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

    // Multiline log support - tracks if this tab is currently in a multiline log sequence
    public bool IsInMultilineSequence { get; set; }

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
          UpdateNewContentIndicator();
        }
      }
    }

    // Callbacks for driving the per-tab manual horizontal scrollbar.
    // Wired by MainWindow.AddNewTab after the tab UI is fully constructed.
    public Action<double>? SetHorizontalOffset { get; set; }
    public Func<double>? GetHorizontalOffset { get; set; }

    // UI controls
    public MetroTabItem TabItem { get; }
    public TextBox RegexTextBox { get; }
    public NumericUpDown AfterLinesNumericUpDown { get; }
    public TextBox NameTextBox { get; }
    public CheckBox WatchingCheckBox { get; }

    // Disk-backed content storage and its virtual collection for the ListBox
    public DiskBackedLineStore LineStore { get; }
    public VirtualLineCollection LineCollection { get; }
    public ListBox ContentListBox { get; }

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
      ListBox contentListBox,
      bool isAutoCreated = false
    )
    {
      TabItem = tabItem;
      RegexTextBox = regexTextBox;
      AfterLinesNumericUpDown = afterLinesTextBox;
      WatchingCheckBox = watchingCheckBox;
      ContentListBox = contentListBox;
      NameTextBox = nameTextBox;

      // Set up disk-backed storage and wire it to the ListBox
      LineStore = new DiskBackedLineStore();
      LineCollection = new VirtualLineCollection(LineStore);
      ContentListBox.ItemsSource = LineCollection;

      TabName = NameTextBox.Text;
      RegexPattern = RegexTextBox.Text;
      IsWatchingEnabled = watchingCheckBox.IsChecked ?? false;
      FilePosition = 0;
      FileChangedFlag = false;
      HasNewContent = false;
      AfterLines = afterLinesTextBox.Value.HasValue ? (int)afterLinesTextBox.Value : 0;
      AfterLinesCurrent = 0;
      IsAutoCreated = isAutoCreated;
      IsInMultilineSequence = false;

      // Wire up events
      RegexTextBox.TextChanged += RegexTextBox_TextChanged;
      NameTextBox.TextChanged += NameTextBox_TextChanged;
      AfterLinesNumericUpDown.ValueChanged += AfterLinesNumericUpDown_ValueChanged;
      WatchingCheckBox.Unchecked += WatchingCheckBox_Unchecked;

      UpdateRegex();
      UpdateTabHeader();
      // SetupScrollDetection is called from outside via the ListBox.Loaded event
    }

    protected void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void NameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
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
          AfterLines = (int)(afterLinesNumericUpDown.Value ?? AfterLines);
        else
          afterLinesNumericUpDown.Value = AfterLines;
      }
    }

    private void RegexTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
      RegexPattern = RegexTextBox.Text;
    }

    /// <summary>
    /// Updates the tab header to display name if available, otherwise regex pattern
    /// </summary>
    public void UpdateTabHeader()
    {
      string displayText;
      if (!string.IsNullOrWhiteSpace(TabName))
        displayText = TabName;
      else
        displayText = RegexPattern;

      if (IsAutoCreated)
      {
        displayText = "🔄 " + displayText;
        TabItem.Foreground = Brushes.Navy;
      }
      else
      {
        TabItem.Foreground = Brushes.Black;
      }

      if (HasNewContent)
      {
        displayText = "🔔 " + displayText;
        HeaderedControlHelper.SetHeaderFontWeight(TabItem, FontWeights.Bold);
      }
      else
      {
        HeaderedControlHelper.SetHeaderFontWeight(TabItem, FontWeights.Normal);
      }

      TabItem.Header = displayText;
    }

    /// <summary>
    /// Updates the compiled regex from the TextBox. Pass showError=true to show a
    /// MessageBox when the pattern is invalid (e.g. on explicit Apply/Enable).
    /// </summary>
    public bool UpdateRegex(bool showError = false)
    {
      RegexPattern = RegexTextBox.Text;
      try
      {
        CompiledRegex = new Regex(RegexPattern);
        IsRegexValid = true;
        return true;
      }
      catch (Exception ex)
      {
        if (showError)
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
    /// Appends matching lines from a log file to the disk store and refreshes the ListBox.
    /// </summary>
    public void AppendContent(string text)
    {
      if (_disposed)
        return;

      bool wasAtBottom = IsUserAtBottom();

      LineStore.AppendLines(SplitIntoLines(text));
      LineCollection.NotifyReset();

      if (wasAtBottom)
        ScrollToEnd();
      else if (!HasNewContent)
        SetHasNewContent(true);
    }

    /// <summary>
    /// Splits a multi-line string (built with AppendLine) into individual lines,
    /// discarding the trailing empty element produced by the final newline.
    /// </summary>
    private static IEnumerable<string> SplitIntoLines(string text)
    {
      var parts = text.Split('\n');
      for (int i = 0; i < parts.Length; i++)
      {
        var line = parts[i].TrimEnd('\r');
        if (i < parts.Length - 1 || line.Length > 0)
          yield return line;
      }
    }

    /// <summary>
    /// Clears the disk store and resets the ListBox.
    /// </summary>
    public void ClearContent()
    {
      if (_disposed)
        return;
      LineStore.Clear();
      LineCollection.StartIndex = 0;
      LineCollection.NotifyReset();
    }

    /// <summary>
    /// Hides all lines before <paramref name="absoluteLineIndex"/>. That line becomes
    /// visible index 0.
    /// </summary>
    public void HideLinesBefore(int absoluteLineIndex)
    {
      LineCollection.StartIndex = absoluteLineIndex;
      LineCollection.NotifyReset();
    }

    /// <summary>
    /// Restores all previously hidden lines.
    /// </summary>
    public void ShowAllLines()
    {
      LineCollection.StartIndex = 0;
      LineCollection.NotifyReset();
    }

    /// <summary>
    /// Scrolls the ListBox to the last line.
    /// </summary>
    public void ScrollToEnd()
    {
      if (_disposed || ContentListBox == null)
        return;
      var scrollViewer = FindScrollViewer(ContentListBox);
      scrollViewer?.ScrollToEnd();
    }

    /// <summary>
    /// Selects and scrolls to a specific line index (used by search navigation).
    /// When <paramref name="searchTerm"/> is provided, also scrolls horizontally so the
    /// match is visible with a small amount of leading context.
    /// </summary>
    /// <param name="lineIndex">Absolute index into the disk store (as returned by Search).</param>
    public void ScrollToLine(int lineIndex, string searchTerm = "", bool matchCase = false, FontFamily? fontFamily = null)
    {
      if (_disposed || ContentListBox == null || lineIndex < 0 || lineIndex >= LineStore.LineCount)
        return;
      int visibleIndex = lineIndex - LineCollection.StartIndex;
      if (visibleIndex < 0 || visibleIndex >= LineCollection.Count)
        return;

      ContentListBox.SelectedIndex = visibleIndex;
      // With CanContentScroll=true and VirtualizingStackPanel, VerticalOffset is item-based
      var scrollViewer = FindScrollViewer(ContentListBox);
      if (scrollViewer == null)
        return;
      scrollViewer.ScrollToVerticalOffset(visibleIndex);

      if (!string.IsNullOrEmpty(searchTerm))
      {
        // Force the panel to measure the items now at visibleIndex so inner ScrollViewers
        // have their ExtentWidth updated before the horizontal match offset is computed.
        ContentListBox.UpdateLayout();
        // Pass absolute lineIndex so ScrollToHorizontalMatch reads the correct line text.
        ScrollToHorizontalMatch(scrollViewer, lineIndex, searchTerm, matchCase, fontFamily);
      }
      else
      {
        SetHorizontalOffset?.Invoke(0);
      }

      ContentListBox.Focus();
    }

    private void ScrollToHorizontalMatch(
      ScrollViewer scrollViewer,
      int lineIndex,
      string searchTerm,
      bool matchCase,
      FontFamily? fontFamily
    )
    {
      string lineText = LineStore.GetLine(lineIndex);
      var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      int matchIndex = lineText.IndexOf(searchTerm, cmp);
      if (matchIndex < 0)
        return;

      // Match is at the very start — only scroll if we're already scrolled right past it
      if (matchIndex == 0)
      {
        if ((GetHorizontalOffset?.Invoke() ?? 0) > 0)
          SetHorizontalOffset?.Invoke(0);
        return;
      }

      try
      {
        string textBefore = lineText[..matchIndex];
        var typeface = new Typeface(fontFamily ?? ContentListBox.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double fontSize = ContentListBox.FontSize;
        if (double.IsNaN(fontSize) || fontSize <= 0)
          fontSize = SystemFonts.MessageFontSize;
        double dpi = VisualTreeHelper.GetDpi(ContentListBox).PixelsPerDip;
        var formatted = new FormattedText(
          textBefore,
          CultureInfo.CurrentCulture,
          FlowDirection.LeftToRight,
          typeface,
          fontSize,
          Brushes.Black,
          dpi
        );

        // +4 px for the combined ListBoxItem + TextBlock padding
        double matchPixelX = formatted.Width + 4.0;
        double viewLeft = GetHorizontalOffset?.Invoke() ?? 0;
        double viewRight = viewLeft + scrollViewer.ViewportWidth;

        // Don't scroll if the match start is already visible with at least 50 px of right margin
        if (matchPixelX >= viewLeft && matchPixelX <= viewRight - 50)
          return;

        // Scroll just enough to bring the match into view with ~50 px of leading context
        SetHorizontalOffset?.Invoke(Math.Max(0, matchPixelX - 50));
      }
      catch { }
    }

    /// <summary>
    /// Get estimated memory usage of this tab's content (page cache only; bulk is on disk).
    /// </summary>
    public long GetEstimatedMemoryUsage()
    {
      if (_disposed)
        return 0;
      // Only the page cache lives in memory; rough estimate: 80 bytes per cached line
      return Math.Min(LineStore.LineCount, 15 * 200) * 80L;
    }

    /// <summary>
    /// Makes the tab name bold to indicate new content
    /// </summary>
    public void SetHasNewContent(bool hasNew)
    {
      HasNewContent = hasNew;
    }

    private void UpdateNewContentIndicator()
    {
      UpdateTabHeader();
    }

    /// <summary>
    /// Checks if the user is currently scrolled to the bottom of the ListBox
    /// </summary>
    private bool IsUserAtBottom()
    {
      if (_disposed || ContentListBox == null)
        return _shouldFollowBottom;
      try
      {
        var scrollViewer = FindScrollViewer(ContentListBox);
        if (scrollViewer != null)
        {
          const double tolerance = 5.0;
          return Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) <= tolerance;
        }
        return _shouldFollowBottom;
      }
      catch
      {
        return _shouldFollowBottom;
      }
    }

    /// <summary>
    /// Finds the ScrollViewer inside a control using visual tree walking
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
      if (parent == null)
        return null;
      try
      {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
          var child = VisualTreeHelper.GetChild(parent, i);
          if (child is ScrollViewer sv)
            return sv;
          var result = FindScrollViewer(child);
          if (result != null)
            return result;
        }
      }
      catch { }
      return null;
    }

    /// <summary>
    /// Subscribe to ListBox scroll events to detect when user scrolls back to bottom.
    /// Must be called after the ListBox has been loaded into the visual tree.
    /// </summary>
    public void SetupScrollDetection()
    {
      if (_disposed || ContentListBox == null || _scrollDetectionSetUp)
        return;
      try
      {
        var scrollViewer = FindScrollViewer(ContentListBox);
        if (scrollViewer != null)
        {
          scrollViewer.ScrollChanged += OnScrollChanged;
          _scrollDetectionSetUp = true;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error setting up scroll detection for tab '{TabName}': {ex.Message}");
      }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
      if (_disposed)
        return;
      try
      {
        _shouldFollowBottom = IsUserAtBottom();
        if (HasNewContent && _shouldFollowBottom)
          SetHasNewContent(false);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error in scroll change handler for tab '{TabName}': {ex.Message}");
      }
    }

    /// <summary>
    /// Called when a tab becomes visible again. Scrolls to end if the user was at the
    /// bottom when the tab was last active, preserving position otherwise.
    /// </summary>
    public void RestoreScrollIfFollowing()
    {
      if (_shouldFollowBottom)
        ScrollToEnd();
    }

    #region IDisposable Implementation

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!_disposed && disposing)
      {
        try
        {
          if (RegexTextBox != null)
            RegexTextBox.TextChanged -= RegexTextBox_TextChanged;
          if (NameTextBox != null)
            NameTextBox.TextChanged -= NameTextBox_TextChanged;
          if (AfterLinesNumericUpDown != null)
            AfterLinesNumericUpDown.ValueChanged -= AfterLinesNumericUpDown_ValueChanged;
          if (WatchingCheckBox != null)
            WatchingCheckBox.Unchecked -= WatchingCheckBox_Unchecked;

          PropertyChanged = null;

          try
          {
            var scrollViewer = FindScrollViewer(ContentListBox);
            if (scrollViewer != null)
              scrollViewer.ScrollChanged -= OnScrollChanged;
          }
          catch { }

          LineStore.Dispose();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Error during TabInfo disposal: {ex.Message}");
        }

        _disposed = true;
      }
    }

    ~TabInfo()
    {
      Dispose(false);
    }

    #endregion
  }
}
