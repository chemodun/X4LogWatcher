using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;
using Microsoft.Win32;

namespace X4LogWatcher
{
  public partial class MainWindow : MetroWindow, INotifyPropertyChanged
  {
    private string? logFolderPath;
    private ResilientFileWatcher? watcher;

    private bool _isWatchingFile;
    public bool IsWatchingFile
    {
      get => _isWatchingFile;
      set
      {
        _isWatchingFile = value;
        OnPropertyChanged(nameof(IsWatchingFile));
        if (value)
        {
          // If watching file, stop watching folder
          IsWatchingFolder = false;
        }

        // Update the forced refresh enabled state
        UpdateForcedRefreshEnabledState();
      }
    }
    private bool _isWatchingFolder;
    public bool IsWatchingFolder
    {
      get => _isWatchingFolder;
      set
      {
        _isWatchingFolder = value;
        OnPropertyChanged(nameof(IsWatchingFolder));
        if (value)
        {
          // If watching folder, stop watching file
          IsWatchingFile = false;
        }

        // Update the forced refresh enabled state
        UpdateForcedRefreshEnabledState();
      }
    }

    private bool _isForcedRefreshEnabled;
    public bool IsForcedRefreshEnabled
    {
      get => _isForcedRefreshEnabled;
      set
      {
        if (value == _isForcedRefreshEnabled)
          return;
        _isForcedRefreshEnabled = value;
        OnPropertyChanged(nameof(IsForcedRefreshEnabled));

        if (value)
        {
          // Start the forced refresh timer
          StartForcedRefresh();
        }
        else
        {
          // Stop the forced refresh timer
          StopForcedRefresh();
        }
      }
    }

    private string _statusLineFileInfo = "No file is watched.";
    public string StatusLineFileInfo
    {
      get => _statusLineFileInfo;
      set
      {
        _statusLineFileInfo = value;
        OnPropertyChanged(nameof(StatusLineFileInfo));
      }
    }

    // Timer for forced refresh
    private System.Windows.Threading.DispatcherTimer? forcedRefreshTimer;

    // File information for forced refresh
    private DateTime lastModifiedTime;
    private long lastFileSize;

    private string? _currentLogFile;
    private string? CurrentLogFile
    {
      get => _currentLogFile;
      set
      {
        _currentLogFile = value;
        OnPropertyChanged(nameof(CurrentLogFile));
        if (_currentLogFolder != null)
        {
          TitleText = $"{titlePrefix} - {_currentLogFolder}";
        }
        else
        {
          TitleText = titlePrefix;
        }
        if (value != null)
        {
          TitleText += $" - {(_currentLogFolder != null ? Path.GetFileName(value) : value)}";
        }
        UpdateFileStatus();
      }
    }
    private string? _currentLogFolder;
    private string? CurrentLogFolder
    {
      get => _currentLogFolder;
      set
      {
        _currentLogFolder = value;
        OnPropertyChanged(nameof(CurrentLogFolder));
        if (value != null)
        {
          TitleText = $"{titlePrefix} - {value}";
        }
        else
        {
          TitleText = titlePrefix;
        }
        UpdateFileStatus();
      }
    }

    private readonly string titlePrefix = "X4 Log Watcher";
    private string? _titleText;
    public string? TitleText
    {
      get => _titleText;
      set
      {
        _titleText = value;
        OnPropertyChanged(nameof(TitleText));
      }
    }

    // Replace dictionary with TabInfo collection
    private readonly List<TabInfo> tabs = [];

    // Collection of auto tab configurations
    private readonly List<AutoTabConfig> autoTabConfigs = [];

    // Command to close tabs
    private ICommand? _closeTabCommand;
    public ICommand CloseTabCommand => _closeTabCommand ??= new RelayCommand<string>(CloseTabByPattern);

    // Configuration object to store recent profiles
    private Config _appConfig;
    public Config AppConfig
    {
      get => _appConfig;
      set
      {
        if (_appConfig != value)
        {
          _appConfig = value;
          OnPropertyChanged(nameof(AppConfig));
        }
      }
    }

    // Search functionality
    private int currentSearchPosition = -1;
    private readonly List<int> searchResultPositions = [];
    private TabInfo? currentSearchTab;

    // Row-selection anchor for Shift+click range selection (set on a plain click).
    private ListBox? _rowAnchorListBox;
    private int _rowAnchorIndex = -1;

    private bool _isProcessing;

    public MainWindow()
    {
      InitializeComponent();
      _appConfig = Config.LoadConfig();
      TitleText = titlePrefix;
      // Set DataContext to this to allow bindings to work
      this.DataContext = this;

      // Initialize _titleText with titlePrefix
      _titleText = titlePrefix;

      // Initialize menu items as unchecked
      menuWatchLogFile.IsChecked = false;
      menuWatchLogFolder.IsChecked = false;
      menuForcedRefresh.IsChecked = false;
      menuForcedRefresh.IsEnabled = false; // Initially disabled until watching starts

      // Initialize the status bar
      UpdateFileStatus();

      // Load application configuration
      AppConfig = Config.LoadConfig();
      AppConfig.PropertyChanged += AppConfig_PropertyChanged;

      // Initialize recent profiles menu
      UpdateRecentProfilesMenu();

      // Add handler for tab selection changed to clear new content indicator
      tabControl.SelectionChanged += TabControl_SelectionChanged;

      // Add handler for window closing to clean up resources
      this.Closing += MainWindow_Closing;

      // Automatically load the active profile if one exists
      if (!string.IsNullOrEmpty(AppConfig.ActiveProfile) && File.Exists(AppConfig.ActiveProfile))
      {
        LoadProfile(AppConfig.ActiveProfile);
      }
    }

    // Handle tab selection changes to clear new content indicators
    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (e.AddedItems.Count > 0 && e.AddedItems[0] is MetroTabItem selectedItem && selectedItem != addTabButton)
      {
        // Find the TabInfo corresponding to the selected tab
        var selectedTabInfo = tabs.FirstOrDefault(t => t.TabItem == selectedItem);
        // Reset the new content indicator when a tab is selected
        selectedTabInfo?.SetHasNewContent(false);
      }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
      base.OnPreviewKeyDown(e);

      if (e.Key == Key.PageUp || e.Key == Key.PageDown)
      {
        var activeTab = tabs.FirstOrDefault(t => t.TabItem == tabControl.SelectedItem);
        var sv = activeTab?.ContentListBox != null ? FindVisualDescendant<ScrollViewer>(activeTab.ContentListBox) : null;
        if (sv != null)
        {
          if (Keyboard.Modifiers == ModifierKeys.None)
          {
            if (e.Key == Key.PageDown)
              sv.PageDown();
            else
              sv.PageUp();
            e.Handled = true;
          }
          else if (Keyboard.Modifiers == ModifierKeys.Control)
          {
            if (e.Key == Key.PageDown)
              sv.ScrollToBottom();
            else
              sv.ScrollToTop();
            e.Handled = true;
          }
        }
      }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
      base.OnKeyDown(e);

      // Handle Ctrl+F to open search, pre-filled with any partial text selection in a row.
      if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
      {
        string? selectedText = null;
        if (Keyboard.FocusedElement is RichTextBox focRtb && !focRtb.Selection.IsEmpty)
          selectedText = focRtb.Selection.Text;
        ShowFindPanel(selectedText);
        e.Handled = true;
      }

      // Handle F3 to find next
      if (e.Key == Key.F3)
      {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
          FindPrevious();
        }
        else
        {
          FindNext();
        }
        e.Handled = true;
      }

      // Handle Escape to close find panel
      if (e.Key == Key.Escape && findPanel.Visibility == Visibility.Visible)
      {
        CloseFindPanel();
        e.Handled = true;
      }
    }

    private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
    {
      OpenFileDialog openFileDialog = new()
      {
        Filter = $"Log files (*{AppConfig.LogFileExtension})|*{AppConfig.LogFileExtension}|All files (*.*)|*.*",
        Title = "Select Log File",
        InitialDirectory = InitialFolderToSelect(),
      };
      if (openFileDialog.ShowDialog() == true)
      {
        // Handle file selection
        string selectedFile = openFileDialog.FileName;

        // Save the selected folder path to config
        AppConfig.LastLogFolderPath = Path.GetDirectoryName(selectedFile);

        CurrentLogFolder = null;
        IsWatchingFile = true;

        // Start watching the selected file
        Dispatcher.Invoke(() =>
        {
          StartWatching(selectedFile);
        });
      }
      else
      {
        IsWatchingFile = false;
      }
    }

    private string InitialFolderToSelect()
    {
      if (!string.IsNullOrEmpty(AppConfig.LastLogFolderPath) && Directory.Exists(AppConfig.LastLogFolderPath))
      {
        return AppConfig.LastLogFolderPath;
      }
      return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
    {
      // Create a new OpenFolderDialog (native WPF)
      var dialog = new OpenFolderDialog
      {
        Title = "Select Log Folder",
        Multiselect = false,
        InitialDirectory = InitialFolderToSelect(),
      };

      if (dialog.ShowDialog() == true)
      {
        // Handle folder selection
        logFolderPath = dialog.FolderName;

        // Save the selected folder path to config
        AppConfig.LastLogFolderPath = logFolderPath;

        // Update folder info and status line immediately
        _currentLogFolder = dialog.FolderName;
        UpdateFileStatus();

        // Start watching the folder for all log files and new log files
        IsWatchingFolder = true;
        CurrentLogFile = null;
        StartWatchingFolder(logFolderPath);
      }
      else
      {
        IsWatchingFolder = false;
      }
    }

    private void StartWatchingFolder(string folderPath)
    {
      if (watcher != null)
      {
        watcher.Dispose();
      }

      if (!Directory.Exists(folderPath))
      {
        MessageBox.Show("Selected folder does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      CurrentLogFolder = folderPath;

      // Set up a watcher for the folder to detect all log files
      watcher = new ResilientFileWatcher(folderPath, $"*{AppConfig.LogFileExtension}", false);

      // Subscribe to events
      watcher.Changed += OnFolderFileChanged;
      watcher.Created += OnFolderFileCreated;
      watcher.Start();
    }

    private void OnFolderWatcherError(object sender, ErrorEventArgs e)
    {
      MessageBox.Show(
        $"An error occurred while watching the folder: {e.GetException().Message}",
        "Folder Watcher Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error
      );
    }

    private void OnFolderFileCreated(object sender, FileSystemEventArgs e)
    {
      try
      {
        Console.WriteLine($"New file created: {e.FullPath}");

        // Check if the new file is a log file
        if (Path.GetExtension(e.FullPath).Equals($"{AppConfig.LogFileExtension}", StringComparison.OrdinalIgnoreCase))
        {
          // Wait a moment for the file to be accessible
          System.Threading.Thread.Sleep(500);

          // Switch to monitoring the new file since it's just been created
          Dispatcher.Invoke(() =>
          {
            StartWatching(e.FullPath, true);
          });
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error handling new file: {ex.Message}");
      }
    }

    private void OnFolderFileChanged(object sender, FileSystemEventArgs e)
    {
      try
      {
        // Check if the new file is a log file
        if (Path.GetExtension(e.FullPath).Equals($"{AppConfig.LogFileExtension}", StringComparison.OrdinalIgnoreCase))
        {
          Dispatcher.Invoke(() =>
          {
            if (CurrentLogFile == e.FullPath)
            {
              // Already watching this file, no action needed
              OnSingleFileChanged(sender, e);
            }
            else
            {
              StartWatching(e.FullPath, true);
            }
          });
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error handling file change: {ex.Message}");
      }
    }

    private void AddTabButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
      // Create a new tab with default settings
      string defaultRegex = ".*";
      AddNewTab("", defaultRegex, 0, false, false);
    }

    private void AddNewTab(string tabName, string regexPattern, int afterLines, bool isEnabled, bool isAutoCreated = false)
    {
      // Create a MetroTabItem with close button
      var tabItem = new MetroTabItem
      {
        Header = tabName,
        CloseButtonEnabled = true,
        CloseTabCommand = this.CloseTabCommand,
        CloseTabCommandParameter = regexPattern,
      };

      // Create root Grid for tab content
      var mainGrid = new Grid();
      tabItem.Content = mainGrid;

      // Create row definitions for the grid (two control rows and the content row)
      mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); // First control row
      mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); // Second control row
      mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content row
      mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Horizontal scrollbar row

      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // Content column
      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // Content column
      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // Content column
      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // Content column
      mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }); // Content column

      // Add enable/disable checkbox
      var chkEnable = new CheckBox
      {
        Content = "Enabled",
        IsChecked = isEnabled,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
      };
      Grid.SetColumn(chkEnable, 0);
      Grid.SetRow(chkEnable, 0);
      mainGrid.Children.Add(chkEnable);

      // Add Name label
      var lblName = new Label
      {
        Content = "Name:",
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
      };
      Grid.SetColumn(lblName, 1);
      Grid.SetRow(lblName, 0);
      mainGrid.Children.Add(lblName);

      // Add the tab name input textbox (fills remaining space)
      var txtName = new TextBox
      {
        Text = tabName,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
        IsReadOnly = isAutoCreated, // Make it read-only if auto-created
      };
      Grid.SetColumn(txtName, 2);
      Grid.SetColumnSpan(txtName, 4);
      Grid.SetRow(txtName, 0);
      mainGrid.Children.Add(txtName);

      // Add RegEx label
      var lblRegEx = new Label
      {
        Content = "RegEx:",
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
      };
      Grid.SetColumn(lblRegEx, 0);
      Grid.SetRow(lblRegEx, 1);
      mainGrid.Children.Add(lblRegEx);

      // Add the regex input textbox (fills remaining space in two columns)
      var txtRegex = new TextBox
      {
        Text = regexPattern,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
      };
      Grid.SetColumn(txtRegex, 1);
      Grid.SetColumnSpan(txtRegex, 2);
      Grid.SetRow(txtRegex, 1);
      mainGrid.Children.Add(txtRegex);

      // Add After Lines label and input field
      var lblAfterLines = new Label { Content = "After Lines:", VerticalAlignment = VerticalAlignment.Center };
      Grid.SetColumn(lblAfterLines, 3);
      Grid.SetRow(lblAfterLines, 1);
      mainGrid.Children.Add(lblAfterLines);

      // Add NumericUpDown for After Lines (from MahApps.Metro)
      var numAfterLines = new NumericUpDown
      {
        Value = afterLines,
        Width = 80,
        Minimum = 0,
        Maximum = 10,
        HideUpDownButtons = false,
        NumericInputMode = MahApps.Metro.Controls.NumericInput.Numbers,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
      };
      Grid.SetColumn(numAfterLines, 4);
      Grid.SetRow(numAfterLines, 1);
      mainGrid.Children.Add(numAfterLines);

      // Add Apply button (right side)
      var btnApply = new Button
      {
        Content = "Apply",
        Padding = new Thickness(10, 0, 10, 0),
        Margin = new Thickness(5, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
      };
      Grid.SetColumn(btnApply, 5);
      Grid.SetRow(btnApply, 1);
      mainGrid.Children.Add(btnApply);

      // Create the content panel for the third row (with the text content)
      var contentPanel = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray };
      Grid.SetRow(contentPanel, 2);
      Grid.SetColumnSpan(contentPanel, 6);
      mainGrid.Children.Add(contentPanel);

      // Add virtualized ListBox for content
      var contentListBox = new ListBox
      {
        SelectionMode = SelectionMode.Extended,
        ItemTemplate = CreateLineDataTemplate(),
        BorderThickness = new Thickness(0),
      };
      VirtualizingPanel.SetIsVirtualizing(contentListBox, true);
      VirtualizingPanel.SetVirtualizationMode(contentListBox, VirtualizationMode.Recycling);
      // ScrollUnit=Pixel is required for horizontal scrolling: in item-based mode (the default)
      // the VirtualizingStackPanel always reports ExtentWidth == ViewportWidth, making horizontal
      // scrolling impossible regardless of content width.  Pixel mode correctly tracks the
      // widest visible child and reports it as ExtentWidth.
      VirtualizingPanel.SetScrollUnit(contentListBox, ScrollUnit.Pixel);
      ScrollViewer.SetHorizontalScrollBarVisibility(contentListBox, ScrollBarVisibility.Disabled);
      ScrollViewer.SetVerticalScrollBarVisibility(contentListBox, ScrollBarVisibility.Auto);
      ScrollViewer.SetCanContentScroll(contentListBox, true);

      // Build an explicit ListBoxItem ControlTemplate: a zero-MinHeight Border wrapping
      // a ContentPresenter, with an IsSelected trigger for the highlight background.
      // OverridesDefaultStyle=true bypasses MahApps's implicit style, but then WPF cannot
      // find a template from the theme dictionary, so we must supply one ourselves.
      var liCpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
      liCpFactory.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

      var liBdFactory = new FrameworkElementFactory(typeof(Border));
      liBdFactory.Name = "Bd";
      liBdFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
      liBdFactory.AppendChild(liCpFactory);

      var liTemplate = new ControlTemplate(typeof(ListBoxItem));
      liTemplate.VisualTree = liBdFactory;

      // Hover: subtle tint (hover trigger before selected so selected wins when both active)
      var liHoverBrush = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));
      liHoverBrush.Freeze();
      var liHoverTrig = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
      liHoverTrig.Setters.Add(new Setter(Border.BackgroundProperty, liHoverBrush) { TargetName = "Bd" });
      liTemplate.Triggers.Add(liHoverTrig);

      // Selected: highlight background + foreground so RichTextBox text inherits white
      var liSelTrig = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
      liSelTrig.Setters.Add(new Setter(Border.BackgroundProperty, SystemColors.HighlightBrush) { TargetName = "Bd" });
      liSelTrig.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
      liTemplate.Triggers.Add(liSelTrig);

      var itemStyle = new Style(typeof(ListBoxItem));
      itemStyle.Setters.Add(new Setter(FrameworkElement.OverridesDefaultStyleProperty, true));
      itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
      itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
      itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, liTemplate));
      contentListBox.ItemContainerStyle = itemStyle;

      contentListBox.PreviewKeyDown += ContentBox_PreviewKeyDown;

      contentPanel.Child = contentListBox;

      // Manual horizontal scrollbar: the outer ListBox ScrollViewer never sees a true
      // horizontal extent from VirtualizingStackPanel (ExtentWidth == ViewportWidth always).
      // Each row's inner RichTextBox PART_ContentHost ScrollViewer has ExtentWidth equal to
      // the FormattedText-measured PageWidth set in HighlightRichTextBox.Rebuild.
      // This ScrollBar drives all visible inner ScrollViewers directly.
      var hScrollBar = new System.Windows.Controls.Primitives.ScrollBar
      {
        Orientation = Orientation.Horizontal,
        SmallChange = 20,
        LargeChange = 200,
        Minimum = 0,
        Maximum = 0,
        ViewportSize = 0,
        IsEnabled = false,
      };
      Grid.SetRow(hScrollBar, 3);
      Grid.SetColumnSpan(hScrollBar, 6);
      mainGrid.Children.Add(hScrollBar);

      // Create and store TabInfo object
      var tabInfo = new TabInfo(tabItem, chkEnable, txtName, txtRegex, numAfterLines, contentListBox, isAutoCreated);

      // Max inner-SV ExtentWidth ever seen in this tab (monotonically increasing so the scrollbar
      // Maximum never shrinks while the user is scrolled right — virtualization hides older rows).
      double[] maxExtentHolder = { 0.0 };

      void UpdateHScrollBar()
      {
        double viewportW = 0;
        for (int i = 0; i < contentListBox.Items.Count; i++)
        {
          if (contentListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem lbi)
            continue;
          var innerSv = FindVisualDescendant<ScrollViewer>(lbi);
          var rtb = FindVisualDescendant<RichTextBox>(lbi);
          if (innerSv == null || rtb == null)
            continue;
          // ContentWidth is the FormattedText-measured line width stored by HighlightRichTextBox.
          // PageWidth stays at 50000 to prevent wrapping, so innerSv.ExtentWidth would always be
          // 50000 and useless as a scrollbar extent.
          double cw = HighlightRichTextBox.GetContentWidth(rtb);
          if (cw > maxExtentHolder[0])
            maxExtentHolder[0] = cw;
          viewportW = innerSv.ViewportWidth;
        }
        if (viewportW <= 0)
          return;
        double scrollable = Math.Max(0, maxExtentHolder[0] - viewportW);
        hScrollBar.ViewportSize = viewportW;
        hScrollBar.LargeChange = viewportW;
        hScrollBar.Maximum = scrollable;
        hScrollBar.IsEnabled = scrollable > 0;
        if (hScrollBar.Value > scrollable)
          hScrollBar.Value = scrollable;
      }

      void SyncInnerViewers(double offset)
      {
        for (int i = 0; i < contentListBox.Items.Count; i++)
        {
          if (contentListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem lbi)
            continue;
          var innerSv = FindVisualDescendant<ScrollViewer>(lbi);
          innerSv?.ScrollToHorizontalOffset(offset);
        }
      }

      hScrollBar.ValueChanged += (s, e) => SyncInnerViewers(hScrollBar.Value);

      // SetupScrollDetection needs the visual tree to be ready
      contentListBox.Loaded += (_, _) =>
      {
        tabInfo.SetupScrollDetection();
        var outerSv = FindVisualDescendant<ScrollViewer>(contentListBox);
        if (outerSv != null)
          outerSv.ScrollChanged += (s, e) =>
          {
            UpdateHScrollBar();
            SyncInnerViewers(hScrollBar.Value);
          };
        tabInfo.RestoreScrollIfFollowing();
      };
      tabs.Add(tabInfo);

      tabInfo.SetHorizontalOffset = v =>
      {
        double clamped = Math.Max(0, Math.Min(v, hScrollBar.Maximum));
        hScrollBar.Value = clamped;
        SyncInnerViewers(clamped);
      };
      tabInfo.GetHorizontalOffset = () => hScrollBar.Value;

      // Row context menu: "Hide all before this line" / "Show all lines"
      var menuHideBefore = new MenuItem { Header = "Hide all before this line" };
      var menuShowAll = new MenuItem { Header = "Show all lines", IsEnabled = false };
      var rowContextMenu = new ContextMenu();
      rowContextMenu.Items.Add(menuHideBefore);
      rowContextMenu.Items.Add(new Separator());
      rowContextMenu.Items.Add(menuShowAll);
      // Row selection: a plain click + drag does partial text selection inside one line
      // (native RichTextBox), while Shift+click / Ctrl+click select whole rows.  The latter is
      // driven by RowRichTextBox_PreviewMouseLeftButtonDown (attached in CreateLineDataTemplate)
      // which manipulates this ListBox's SelectedItems directly.

      // RichTextBox has a built-in Cut/Copy/Paste context menu that intercepts a normal
      // right-click before the ListBox ContextMenu can open.  PreviewMouseRightButtonUp is
      // a tunneling event that reaches the ListBox before the RichTextBox sees MouseRightButtonUp,
      // so we open our custom menu here and mark the event handled to suppress the built-in one.
      int rightClickedVisibleIndex = -1;
      contentListBox.PreviewMouseRightButtonUp += (s, e) =>
      {
        var pos = e.GetPosition(contentListBox);
        var hit = VisualTreeHelper.HitTest(contentListBox, pos);
        var lbi = FindVisualAncestor<ListBoxItem>(hit?.VisualHit);
        rightClickedVisibleIndex = lbi != null ? contentListBox.ItemContainerGenerator.IndexFromContainer(lbi) : -1;

        int hiddenCount = tabInfo.LineCollection.StartIndex;
        menuHideBefore.IsEnabled = rightClickedVisibleIndex > 0;
        menuShowAll.IsEnabled = hiddenCount > 0;
        menuShowAll.Header = hiddenCount > 0 ? $"Show all lines ({hiddenCount} hidden)" : "Show all lines";

        rowContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        rowContextMenu.IsOpen = true;
        e.Handled = true;
      };

      menuHideBefore.Click += (s, e) =>
      {
        if (rightClickedVisibleIndex > 0)
        {
          tabInfo.HideLinesBefore(rightClickedVisibleIndex + tabInfo.LineCollection.StartIndex);
          if (currentSearchTab == tabInfo && !string.IsNullOrEmpty(ActiveSearchTerm))
            PerformSearch();
        }
      };

      menuShowAll.Click += (s, e) =>
      {
        tabInfo.ShowAllLines();
        if (currentSearchTab == tabInfo && !string.IsNullOrEmpty(ActiveSearchTerm))
          PerformSearch();
      };

      // Set up Apply button click handler using the TabInfo object
      btnApply.Click += (s, e) =>
      {
        if (tabInfo.UpdateRegex(showError: true) && tabInfo.IsWatchingEnabled && _currentLogFile != null)
        {
          // Reset file position to process from the beginning with new regex
          tabInfo.UpdateTabHeader();
          tabInfo.FilePosition = 0;
          tabInfo.IsInMultilineSequence = false; // Reset multiline state
          tabInfo.ClearContent();
          ProcessTabContent(tabInfo);
        }
      };

      // Set up checkbox event to process file when enabled
      chkEnable.Checked += (s, e) =>
      {
        if (s is CheckBox checkBox)
        {
          if (tabInfo.UpdateRegex(showError: true))
          {
            tabInfo.IsWatchingEnabled = true;
            tabInfo.UpdateTabHeader();
          }
          else
          {
            tabInfo.IsWatchingEnabled = false;
            checkBox.IsChecked = false;
          }
          if (_currentLogFile != null)
          {
            if (tabInfo.FileChangedFlag && tabInfo.IsWatchingEnabled)
            {
              // Reset file position to process from the beginning when enabling a tab
              tabInfo.FilePosition = 0;
              tabInfo.IsInMultilineSequence = false; // Reset multiline state
              tabInfo.ClearContent();
              tabInfo.FileChangedFlag = false;
            }
            // Process this specific tab's content individually when it's enabled
            ProcessTabContent(tabInfo);
          }
        }
        else
        {
          tabInfo.IsWatchingEnabled = false;
        }
      };

      // Add the tab to the tab control BEFORE the "+" tab
      int addButtonIndex = tabControl.Items.IndexOf(addTabButton);
      tabControl.Items.Insert(addButtonIndex, tabItem);

      // Only select the new tab if it's not auto-created to avoid shifting focus
      if (!isAutoCreated)
      {
        tabControl.SelectedItem = tabItem;
      }
    }

    private void ContentBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      // Enter: navigate search results when the find panel is visible
      if (e.Key == Key.Enter && findPanel.Visibility == Visibility.Visible)
      {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
          FindPrevious();
        else
          FindNext();
        e.Handled = true;
        return;
      }
      if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
      {
        var focused = Keyboard.FocusedElement;
        if (sender is ListBox lb)
        {
          if (lb.SelectedItems.Count > 1)
          {
            // Multi-row: copy all selected lines as full lines in display order.
            // LineItem identity is its absolute index, so ordering by it gives display order
            // and duplicate lines are handled naturally.
            var lines = lb.SelectedItems.Cast<LineItem>().OrderBy(li => li.AbsoluteIndex).Select(li => li.Text);
            Clipboard.SetDataObject(string.Join(Environment.NewLine, lines), true);
            e.Handled = true;
            return;
          }

          // Partial text selection inside a single row: copy it explicitly.
          var selRtb = (focused as RichTextBox) ?? FindRichTextBoxWithSelection(lb);
          if (selRtb != null && !selRtb.Selection.IsEmpty)
          {
            Clipboard.SetDataObject(selRtb.Selection.Text, true);
            e.Handled = true;
            return;
          }

          // Single row, no partial selection: copy the full line.
          if (lb.SelectedItem is LineItem line)
          {
            Clipboard.SetDataObject(line.Text, true);
            e.Handled = true;
          }
        }
      }
    }

    // Attached to every row's RichTextBox.  A plain click just records the anchor row and lets
    // the RichTextBox select text within the line.  Shift+click selects the whole-row range from
    // the anchor; Ctrl+click toggles a single row.  For modifier clicks we mark the event handled
    // so the RichTextBox's TextEditor (which acts on the bubbling MouseDown) never starts a
    // single-line text selection that would fight the multi-row selection.
    private void RowRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (sender is not RichTextBox rtb)
        return;
      var lb = FindVisualAncestor<ListBox>(rtb);
      var lbi = FindVisualAncestor<ListBoxItem>(rtb);
      if (lb == null || lbi == null)
        return;
      int index = lb.ItemContainerGenerator.IndexFromContainer(lbi);
      if (index < 0)
        return;

      bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
      bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

      if (!shift && !ctrl)
      {
        if (e.ClickCount >= 3)
        {
          // Triple-click (a "second" double-click in quick succession): select the WHOLE line
          // as a row and make it the anchor, so Ctrl+C copies the full line.  Suppress the
          // RichTextBox's native paragraph selection and drop every partial text selection.
          e.Handled = true;
          ClearRowTextSelections(lb, null);
          lb.SelectedIndex = index;
          _rowAnchorListBox = lb;
          _rowAnchorIndex = index;
          lb.Focus();
          return;
        }

        // Single click: clear any existing row selection (so a previous multi-line selection is
        // removed) AND clear the anchor, but do NOT record a new anchor or highlight the row.
        // Also drop partial text selections in OTHER rows so only the line being clicked can hold
        // a selection.  The RichTextBox keeps the click so it can place the caret and a drag
        // selects partial text.  Double click (ClickCount==2) is left untouched so the
        // RichTextBox selects the word.
        if (e.ClickCount == 1)
        {
          lb.SelectedIndex = -1;
          _rowAnchorListBox = null;
          _rowAnchorIndex = -1;
          ClearRowTextSelections(lb, rtb);
        }
        return;
      }

      // Modifier click: drive row selection ourselves and suppress text selection.
      e.Handled = true;
      // Drop every partial text selection — a row selection replaces them.
      ClearRowTextSelections(lb, null);

      if (ctrl && !shift)
      {
        var item = lb.Items[index];
        if (lb.SelectedItems.Contains(item))
          lb.SelectedItems.Remove(item);
        else
          lb.SelectedItems.Add(item);
        _rowAnchorListBox = lb;
        _rowAnchorIndex = index;
      }
      else // Shift (optionally with Ctrl): select the range from the anchor to here.
      {
        bool hasAnchor = _rowAnchorListBox == lb && _rowAnchorIndex >= 0;
        int anchor = hasAnchor ? _rowAnchorIndex : index;
        if (!hasAnchor)
        {
          // No anchor yet — this Shift+click establishes one at the clicked line so a
          // following Shift+click extends the range from here.
          _rowAnchorListBox = lb;
          _rowAnchorIndex = index;
        }
        lb.SelectedItems.Clear();
        int lo = Math.Min(anchor, index);
        int hi = Math.Max(anchor, index);
        for (int i = lo; i <= hi; i++)
          lb.SelectedItems.Add(lb.Items[i]);
      }

      lb.Focus(); // ensure Ctrl+C reaches the ListBox's PreviewKeyDown handler
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element)
      where T : DependencyObject
    {
      var node = element;
      while (node != null)
      {
        if (node is T result)
          return result;
        node = VisualTreeHelper.GetParent(node);
      }
      return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
      where T : DependencyObject
    {
      int count = VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++)
      {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T result)
          return result;
        var found = FindVisualDescendant<T>(child);
        if (found != null)
          return found;
      }
      return null;
    }

    // Scans visible ListBoxItems for the first RichTextBox with a non-empty text selection.
    private static RichTextBox? FindRichTextBoxWithSelection(ListBox? listBox)
    {
      if (listBox == null)
        return null;
      for (int i = 0; i < listBox.Items.Count; i++)
      {
        if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
          continue;
        var rtb = FindVisualDescendant<RichTextBox>(item);
        if (rtb != null && !rtb.Selection.IsEmpty)
          return rtb;
      }
      return null;
    }

    // Collapses the partial text selection in every realized row RichTextBox except <paramref
    // name="keep"/>.  Only visible rows are realized, and off-screen rows can't hold a selection
    // (their RichTextBox is recycled), so this clears all stray partial selections.
    private static void ClearRowTextSelections(ListBox listBox, RichTextBox? keep)
    {
      for (int i = 0; i < listBox.Items.Count; i++)
      {
        if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
          continue;
        var rtb = FindVisualDescendant<RichTextBox>(item);
        if (rtb != null && rtb != keep && !rtb.Selection.IsEmpty)
          rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentStart);
      }
    }

    private void CloseTab(TabInfo tabInfo)
    {
      // Remove the tab from the tab control
      tabControl.Items.Remove(tabInfo.TabItem);

      // Remove it from our collection
      tabs.Remove(tabInfo);

      // Properly dispose of the TabInfo to clean up memory and event handlers
      tabInfo.Dispose();

      // If there are no more tabs, consider adding a default one
      if (tabs.Count == 0 && _currentLogFile != null)
      {
        AddNewTab(".*", ".*", 0, false);
      }
    }

    private void CloseTabByPattern(string pattern)
    {
      if (string.IsNullOrEmpty(pattern))
        return;

      var tabToClose = tabs.FirstOrDefault(t => t.RegexPattern == pattern);
      if (tabToClose != null)
      {
        CloseTab(tabToClose);
      }
    }

    private void SaveProfile(string profilePath)
    {
      var profileData = tabs.Where(tab => !tab.IsAutoCreated).Select(tab => new TabProfileItem(tab)).ToList();

      var profile = new TabProfile { Tabs = profileData, AutoTabConfigs = [.. autoTabConfigs] };

      File.WriteAllText(profilePath, System.Text.Json.JsonSerializer.Serialize(profile));

      // Add to recent profiles
      AppConfig.AddRecentProfile(profilePath);
      UpdateRecentProfilesMenu();
    }

    private void LoadProfile(string profilePath)
    {
      if (!File.Exists(profilePath))
      {
        MessageBox.Show("Profile file not found.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      try
      {
        // Try to deserialize as the new format first
        var profile = System.Text.Json.JsonSerializer.Deserialize<TabProfile>(File.ReadAllText(profilePath));

        if (profile != null)
        {
          // Remove all tabs except the add button
          var itemsToRemove = tabControl.Items.Cast<object>().Where(i => i != addTabButton).ToList();
          foreach (var item in itemsToRemove)
          {
            tabControl.Items.Remove(item);
          }

          // Dispose all existing tabs before clearing
          foreach (var tab in tabs)
          {
            tab.Dispose();
          }
          tabs.Clear();

          // Clear and load auto tab configurations
          autoTabConfigs.Clear();
          autoTabConfigs.AddRange(profile.AutoTabConfigs);
          foreach (var config in autoTabConfigs)
          {
            config.UpdateRegex();
          }
          MenuAutoTabs_Click(this, new RoutedEventArgs());

          // Add tabs from the profile
          foreach (var item in profile.Tabs)
          {
            AddNewTab(item.TabName, item.RegexPattern, item.AfterLines, item.IsEnabled, false);
          }
        }
      }
      catch (System.Text.Json.JsonException)
      {
        // If deserializing as TabProfile fails, try the old format
        try
        {
          var oldFormatData = System.Text.Json.JsonSerializer.Deserialize<List<TabProfileItem>>(File.ReadAllText(profilePath));

          if (oldFormatData != null)
          {
            // Remove all tabs except the add button
            var itemsToRemove = tabControl.Items.Cast<object>().Where(i => i != addTabButton).ToList();
            foreach (var item in itemsToRemove)
            {
              tabControl.Items.Remove(item);
            }

            // Dispose all existing tabs before clearing
            foreach (var tab in tabs)
            {
              tab.Dispose();
            }
            tabs.Clear();
            autoTabConfigs.Clear();
            MenuAutoTabs_Click(this, new RoutedEventArgs());

            // Add tabs from the old format profile
            foreach (var item in oldFormatData)
            {
              AddNewTab(item.TabName, item.RegexPattern, item.AfterLines, item.IsEnabled);
            }
          }
          else
          {
            MessageBox.Show("Failed to load profile data.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
          }
        }
        catch (Exception ex)
        {
          MessageBox.Show($"Error loading profile: {ex.Message}", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
      }

      // Update recent profiles
      AppConfig.AddRecentProfile(profilePath);
      UpdateRecentProfilesMenu();
    }

    // Method to update the Recent Profiles menu items
    private void UpdateRecentProfilesMenu()
    {
      // Remove existing recent profile items (keep only the header)
      for (int i = menuProfile.Items.Count - 1; i >= 0; i--)
      {
        var item = menuProfile.Items[i];
        if (
          item is MenuItem menuItem
          && menuItem != menuSaveProfile
          && menuItem != menuLoadProfile
          && menuItem != menuRecentProfilesHeader
          && item is not Separator
        )
        {
          menuProfile.Items.Remove(item);
        }
      }

      // Add recent profiles to the menu
      if (AppConfig.RecentProfiles.Count > 0)
      {
        menuRecentProfilesHeader.IsEnabled = false; // Keep it as a header

        foreach (var profilePath in AppConfig.RecentProfiles)
        {
          // Create a shorter display name (just the filename)
          string displayName = Path.GetFileName(profilePath);

          // Create menu item for this profile
          var menuItem = new MenuItem
          {
            Header = displayName,
            Tag = profilePath,
            IsCheckable = true,
            IsChecked = profilePath == AppConfig.ActiveProfile,
          };

          // Add handler to load this profile when clicked
          menuItem.Click += RecentProfile_Click;

          // Add after the Recent Profiles header
          int headerIndex = menuProfile.Items.IndexOf(menuRecentProfilesHeader);
          menuProfile.Items.Insert(headerIndex + 1, menuItem);
        }
      }
      else
      {
        // No recent profiles
        menuRecentProfilesHeader.IsEnabled = false;
      }
    }

    // Event handler for recent profile menu items
    private void RecentProfile_Click(object sender, RoutedEventArgs e)
    {
      if (sender is MenuItem menuItem && menuItem.Tag is string profilePath)
      {
        // Get the current checked state before we make changes
        bool isActiveProfile = AppConfig.ActiveProfile != null && AppConfig.ActiveProfile == profilePath;

        // Uncheck all profile items
        foreach (var item in menuProfile.Items)
        {
          if (item is MenuItem mi && mi.IsCheckable)
          {
            mi.IsChecked = false;
          }
        }

        if (!isActiveProfile)
        {
          // If it wasn't checked before, check it and load the profile
          menuItem.IsChecked = true;
          LoadProfile(profilePath);
          AppConfig.ActiveProfile = profilePath;
        }
        else
        {
          // If it was checked, keep it unchecked (user wants to deselect)
          menuItem.IsChecked = false;
          AppConfig.ActiveProfile = null;
        }
      }
    }

    private void StartWatching(string filePath, bool isAlreadyWatched = false)
    {
      // Store the previous file path before updating
      string? previousLogFile = _currentLogFile;

      // Update the current file path
      CurrentLogFile = filePath;

      UpdateFileStatus();

      // Set up new file watcher
      string? directoryPath = Path.GetDirectoryName(filePath);
      if (directoryPath == null)
      {
        MessageBox.Show("Invalid file path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      List<TabInfo> tabsToClose = [];
      // Inform all tabs about the file change
      foreach (var tab in tabs)
      {
        if (tab.IsAutoCreated)
        {
          tabsToClose.Add(tab);
        }
        else
        {
          tab.FileChangedFlag = true;
          if (tab.IsWatchingEnabled)
          {
            // Reset new content flag
            tab.HasNewContent = false;
            // Reset position to read from beginning
            tab.FilePosition = 0;
            tab.IsInMultilineSequence = false; // Reset multiline state
            // Clear content for the tab
            tab.ClearContent();
          }
        }
      }
      // Remove auto-created tabs
      foreach (var tab in tabsToClose)
      {
        CloseTab(tab);
      }

      foreach (var config in autoTabConfigs)
      {
        config.ResetTabs();
      }

      ProcessAllEnabledTabsParallel();
      if (!isAlreadyWatched)
      {
        if (watcher != null)
        {
          watcher.Dispose();
        }
        watcher = new ResilientFileWatcher(directoryPath, Path.GetFileName(filePath), false);

        watcher.Changed += OnSingleFileChanged;
        watcher.Start();
      }
    }

    private void OnSingleFileChanged(object sender, FileSystemEventArgs e)
    {
      if (e.FullPath == _currentLogFile)
      {
        Dispatcher.Invoke(() =>
        {
          // Update the file status in the status bar
          UpdateFileStatus();

          // Process changes in the file
          ProcessChanges();
        });
      }
    }

    private void ProcessChanges()
    {
      // Process all enabled tabs in parallel for better performance
      ProcessAllEnabledTabsParallel();

      // Mark files as changed for disabled tabs
      foreach (var tab in tabs.Where(t => !t.IsWatchingEnabled))
      {
        if (tab.TabItem.IsSelected == false)
        {
          // Set the file changed flag for disabled non active tabs
          tab.FileChangedFlag = true;
        }
      }
    }

    /// <summary>
    /// Processes file updates in parallel for all enabled tabs by reading each source line once
    /// and applying it to all tabs that match the line.
    /// </summary>
    private void ProcessAllEnabledTabsParallel()
    {
      // If processing is already in progress, exit early
      if (_isProcessing || _currentLogFile == null || !File.Exists(_currentLogFile))
        return;

      try
      {
        _isProcessing = true;
        List<TabInfo> enabledTabs = [];

        Dispatcher.Invoke(() => enabledTabs = tabs.Where(t => t.IsWatchingEnabled).ToList());

        // If no enabled tabs, exit early
        if (enabledTabs.Count == 0)
        {
          _isProcessing = false;
          return;
        }

        Stopwatch sw = new();
        sw.Start();

        // Get file content once for all tabs
        using var stream = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long fileSize = stream.Length;

        // Check all tabs to see if we need to re-read the entire file
        bool needFullRead = false;
        foreach (var tab in enabledTabs)
        {
          if (tab.FilePosition > fileSize)
          {
            tab.FilePosition = 0;
            tab.AfterLinesCurrent = 0; // Reset after lines counter when file is reset
            tab.IsInMultilineSequence = false; // Reset multiline state
            needFullRead = true;
          }
          tab.FileChangedFlag = false; // Reset file changed flag
        }

        // If file got truncated/replaced, we need to reset positions and re-read
        if (needFullRead)
        {
          foreach (var tab in enabledTabs)
          {
            tab.FilePosition = 0;
            tab.AfterLinesCurrent = 0; // Reset after lines counter when file is reset
            tab.IsInMultilineSequence = false; // Reset multiline state
          }
        }

        // Check if there's new content for any tab
        bool hasNewContent = enabledTabs.Any(t => t.FilePosition < fileSize);
        if (!hasNewContent)
        {
          _isProcessing = false;
          return;
        }

        // Read the file line by line just once for all tabs
        using var reader = new StreamReader(stream);
        var fileLines = new List<(long position, string text, DateTime timestamp)>();
        string? line;

        // Read the whole file for full update, or just new portion for incremental update
        if (needFullRead)
        {
          stream.Seek(0, SeekOrigin.Begin);
        }
        else
        {
          // Find the earliest position among tabs
          long minPosition = enabledTabs.Min(t => t.FilePosition);
          stream.Seek(minPosition, SeekOrigin.Begin);
        }

        // Read all lines and their positions
        while ((line = reader.ReadLine()) != null)
        {
          if (
            AppConfig.SkipSignatureErrors
            && (line.Contains("Failed to verify the file signature for file") || line.Contains("Could not find signature file"))
          )
            continue;

          fileLines.Add((stream.Position, line, DateTime.Now));
        }

        // Check for auto tabs first, creating new ones as needed
        var additionalTabs = new ConcurrentBag<TabInfo>();

        // Process each line to check for auto tab matches
        // This needs to be done sequentially since it might create new tabs
        foreach (var (position, logLine, timestamp) in fileLines)
        {
          if (IsFirstLineOfMultilineLog(logLine))
          {
            var autoTab = CheckAndCreateAutoTab(logLine);
            if (autoTab != null && !enabledTabs.Contains(autoTab) && !additionalTabs.Contains(autoTab))
            {
              additionalTabs.Add(autoTab);
            }
          }
        }

        // Add any newly created tabs to our processing list
        var allTabsToProcess = new List<TabInfo>(enabledTabs);
        allTabsToProcess.AddRange(additionalTabs);

        // Process each tab in parallel
        var contentByTab = new ConcurrentDictionary<TabInfo, StringBuilder>();

        Parallel.ForEach(
          allTabsToProcess,
          tab =>
          {
            var contentBuilder = new StringBuilder();
            bool processFromBeginning = tab.FilePosition == 0;

            // Reset after lines counter and multiline state when starting from beginning
            if (processFromBeginning)
            {
              tab.AfterLinesCurrent = 0;
              tab.IsInMultilineSequence = false;
            }

            // Get only lines after tab's last position
            var linesToProcess = fileLines.Where(l =>
            {
              var position = l.position;
              if (position <= tab.FilePosition)
                return false;
              return true;
            });

            foreach (var currentLine in linesToProcess)
            {
              if (tab.CompiledRegex != null)
              {
                bool isFirstLine = IsFirstLineOfMultilineLog(currentLine.text);
                bool isMatch = false;
                string lineText = currentLine.text;
                if (AppConfig.RealTimeStamping && isFirstLine)
                {
                  lineText = currentLine.timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " | " + lineText;
                }
                // Only check regex pattern against first lines (lines starting with '[')
                if (isFirstLine)
                {
                  isMatch = tab.CompiledRegex.IsMatch(currentLine.text);
                  if (isMatch)
                  {
                    // Start a new multiline sequence
                    tab.IsInMultilineSequence = true;

                    contentBuilder.AppendLine(lineText);

                    // If this is a match, reset the after lines counter to the configured value
                    if (tab.AfterLines > 0)
                    {
                      tab.AfterLinesCurrent = tab.AfterLines;
                    }
                  }
                  else
                  {
                    // This first line doesn't match, end any current multiline sequence
                    tab.IsInMultilineSequence = false;
                  }
                }
                else
                {
                  // This is a continuation line (not starting with '[')
                  if (tab.IsInMultilineSequence)
                  {
                    // We're in a multiline sequence, include this line
                    contentBuilder.AppendLine(lineText);
                  }
                  else if (tab.AfterLinesCurrent > 0)
                  {
                    // We're in the "after lines" period, include this line too
                    contentBuilder.AppendLine(lineText);
                    // Decrement the counter
                    tab.AfterLinesCurrent--;
                  }
                }

                // Handle "after lines" logic for first lines that didn't match
                if (isFirstLine && !isMatch && tab.AfterLinesCurrent > 0)
                {
                  contentBuilder.AppendLine(lineText);
                  tab.AfterLinesCurrent--;
                }
              }
            }

            contentByTab.TryAdd(tab, contentBuilder);
          }
        );

        // Update UI on main thread
        Dispatcher.Invoke(() =>
        {
          // Process results for each tab
          foreach (var tab in allTabsToProcess)
          {
            if (contentByTab.TryGetValue(tab, out var contentBuilder))
            {
              var content = contentBuilder.ToString();
              if (!string.IsNullOrEmpty(content))
              {
                tab.AppendContent(content);

                // If this tab isn't currently selected, mark it as having new content
                if (tabControl.SelectedItem != tab.TabItem)
                {
                  tab.SetHasNewContent(true);
                }
              }
            }

            // Update tab's position to end of file
            tab.FilePosition = fileSize;
          }
        });

        sw.Stop();
        // Log processing time for performance monitoring
        // Debug.WriteLine($"ProcessAllEnabledTabsParallel finished in {sw.ElapsedMilliseconds}ms");
      }
      catch (Exception ex)
      {
        Dispatcher.Invoke(() =>
        {
          MessageBox.Show($"Error processing tabs: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
        });
      }
      finally
      {
        _isProcessing = false;
      }
    }

    private string? FindMostRecentLogFile(string folderPath)
    {
      try
      {
        var logFiles = Directory
          .GetFiles(folderPath, $"*{AppConfig.LogFileExtension}")
          .OrderByDescending(f => new FileInfo(f).LastWriteTime)
          .ToList();

        return logFiles.Count > 0 ? logFiles[0] : null;
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Error accessing folder: {ex.Message}", "Folder Access Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return null;
      }
    }

    private void ProcessTabContent(TabInfo tab)
    {
      tab.FileChangedFlag = false;
      if (_currentLogFile == null || !File.Exists(_currentLogFile) || tab.CompiledRegex == null)
        return;

      try
      {
        using var stream = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Check if there's new content
        if (stream.Length <= tab.FilePosition)
          return;

        // Get total file size for progress reporting
        long totalSize = stream.Length;

        // Position the stream at tab's last read position
        stream.Seek(tab.FilePosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var contentBuilder = new StringBuilder();
        string? line;
        int lineCount = 0;
        long lastReportedProgress = 0;
        bool processFromBeginning = tab.FilePosition == 0;

        // Reset after lines counter and multiline state when starting from beginning
        if (processFromBeginning)
        {
          tab.AfterLinesCurrent = 0;
          tab.IsInMultilineSequence = false;
        }

        // Process file line by line
        while ((line = reader.ReadLine()) != null)
        {
          lineCount++;
          long currentPosition = stream.Position;
          if (
            AppConfig.SkipSignatureErrors
            && (line.Contains("Failed to verify the file signature for file") || line.Contains("Could not find signature file"))
          )
            continue;

          // Implement multiline logic
          bool isFirstLine = IsFirstLineOfMultilineLog(line);
          bool isMatch = false;

          if (AppConfig.RealTimeStamping && isFirstLine)
          {
            line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + line;
          }

          // Only check regex pattern against first lines (lines starting with '[')
          if (isFirstLine)
          {
            isMatch = tab.CompiledRegex.IsMatch(line);
            if (isMatch)
            {
              // Start a new multiline sequence
              tab.IsInMultilineSequence = true;
              contentBuilder.AppendLine(line);

              // If this is a match, reset the after lines counter to the configured value
              if (tab.AfterLines > 0)
              {
                tab.AfterLinesCurrent = tab.AfterLines;
              }
            }
            else
            {
              // This first line doesn't match, end any current multiline sequence
              tab.IsInMultilineSequence = false;
            }
          }
          else
          {
            // This is a continuation line (not starting with '[')
            if (tab.IsInMultilineSequence)
            {
              // We're in a multiline sequence, include this line
              contentBuilder.AppendLine(line);
            }
            else if (tab.AfterLinesCurrent > 0)
            {
              // We're in the "after lines" period, include this line too
              contentBuilder.AppendLine(line);
              // Decrement the counter
              tab.AfterLinesCurrent--;
            }
          }

          // Handle "after lines" logic for first lines that didn't match
          if (isFirstLine && !isMatch && tab.AfterLinesCurrent > 0)
          {
            contentBuilder.AppendLine(line);
            tab.AfterLinesCurrent--;
          }

          // Report loading progress if processing from beginning
          if (processFromBeginning && totalSize > 0)
          {
            // Calculate current progress percentage
            int progressPercentage = (int)((currentPosition * 100) / totalSize);

            // Update status bar every 5% to avoid too frequent updates
            if (progressPercentage - lastReportedProgress >= 5)
            {
              lastReportedProgress = progressPercentage;
              Dispatcher.Invoke(
                () =>
                {
                  StatusLineFileInfo = $"Loading {Path.GetFileName(_currentLogFile)} - {progressPercentage}% complete...";
                  // Force UI update
                  System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render,
                    new Action(() => { })
                  );
                },
                System.Windows.Threading.DispatcherPriority.Normal
              );
            }
          }
        }

        // Append new matches to existing content
        var content = contentBuilder.ToString();
        if (!string.IsNullOrEmpty(content))
        {
          Dispatcher.Invoke(() =>
          {
            tab.AppendContent(content);

            // If this tab isn't currently selected, mark it as having new content
            if (tabControl.SelectedItem != tab.TabItem)
            {
              tab.SetHasNewContent(true);
            }
          });
        }

        // Store new position
        tab.FilePosition = stream.Length;

        // Restore normal status bar display after loading is complete
        if (processFromBeginning)
        {
          Dispatcher.Invoke(() => UpdateFileStatus());
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Error processing tab content: {ex.Message}", "Tab Content Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void MenuSaveProfile_Click(object sender, RoutedEventArgs e)
    {
      SaveFileDialog saveDialog = new()
      {
        Filter = "Profile files (*.profile)|*.profile|All files (*.*)|*.*",
        Title = "Save Profile",
        DefaultExt = "profile",
      };

      if (saveDialog.ShowDialog() == true)
      {
        SaveProfile(saveDialog.FileName);
        // Update recent profiles list
        AppConfig.AddRecentProfile(saveDialog.FileName);
        UpdateRecentProfilesMenu();

        MessageBox.Show("Profile saved successfully.", "Save Profile", MessageBoxButton.OK, MessageBoxImage.Information);
      }
    }

    private void MenuLoadProfile_Click(object sender, RoutedEventArgs e)
    {
      OpenFileDialog openDialog = new() { Filter = "Profile files (*.profile)|*.profile|All files (*.*)|*.*", Title = "Load Profile" };

      if (openDialog.ShowDialog() == true)
      {
        LoadProfile(openDialog.FileName);
        // Update recent profiles list
        AppConfig.AddRecentProfile(openDialog.FileName);
        UpdateRecentProfilesMenu();
      }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
      this.Close();
    }

    // Event handler for the Forced Refresh menu item click
    private void MenuForcedRefresh_Click(object sender, RoutedEventArgs e)
    {
      // Toggle the forced refresh state
      IsForcedRefreshEnabled = menuForcedRefresh.IsChecked;
    }

    // Update the enabled state of the Forced Refresh menu item based on current watch state
    private void UpdateForcedRefreshEnabledState()
    {
      // Enable the menu item only if file or folder watching is active
      menuForcedRefresh.IsEnabled = IsWatchingFile || IsWatchingFolder;

      // If watching is disabled, also disable forced refresh
      if (!IsWatchingFile && !IsWatchingFolder)
      {
        IsForcedRefreshEnabled = false;
        menuForcedRefresh.IsChecked = false;
      }
    }

    // Start the forced refresh timer
    private void StartForcedRefresh()
    {
      // Stop any existing timer
      StopForcedRefresh();

      // If watching is not enabled, don't start the timer
      if (!IsWatchingFile && !IsWatchingFolder)
      {
        return;
      }

      // If no file is currently selected, try to find the most recent log file in the watched folder
      if (_currentLogFile == null && _currentLogFolder != null)
      {
        string? mostRecentLogFile = FindMostRecentLogFile(_currentLogFolder);
        if (mostRecentLogFile != null)
        {
          _currentLogFile = mostRecentLogFile;
          // Don't call StartWatching here as it would set up file watchers
          // Just update the UI
          CurrentLogFile = mostRecentLogFile;
        }
        else
        {
          // No log file found, can't start forced refresh
          MessageBox.Show("No log files found in the watched folder.", "Forced Refresh", MessageBoxButton.OK, MessageBoxImage.Information);
          IsForcedRefreshEnabled = false;
          return;
        }
      }

      if (_currentLogFile == null || !File.Exists(_currentLogFile))
      {
        // Can't start forced refresh without a valid file
        MessageBox.Show(
          "No valid log file available for forced refresh.",
          "Forced Refresh",
          MessageBoxButton.OK,
          MessageBoxImage.Information
        );
        IsForcedRefreshEnabled = false;
        return;
      }

      // Store the current file information
      var fileInfo = new FileInfo(_currentLogFile);
      lastModifiedTime = fileInfo.LastWriteTime;
      lastFileSize = fileInfo.Length;

      // Process all enabled tabs once to ensure we have the latest content
      ProcessAllEnabledTabsParallel();

      // Create and start the timer for periodic checks
      forcedRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
      forcedRefreshTimer.Tick += OnForcedRefreshTimerTick;
      forcedRefreshTimer.Start();
    }

    // Stop the forced refresh timer
    private void StopForcedRefresh()
    {
      if (forcedRefreshTimer != null)
      {
        forcedRefreshTimer.Stop();
        forcedRefreshTimer.Tick -= OnForcedRefreshTimerTick;
        forcedRefreshTimer = null;
      }
    }

    // Timer tick handler for forced refresh
    private void OnForcedRefreshTimerTick(object? sender, EventArgs e)
    {
      if (_currentLogFile == null || !File.Exists(_currentLogFile))
      {
        // File no longer exists, stop the timer
        StopForcedRefresh();
        IsForcedRefreshEnabled = false;
        menuForcedRefresh.IsChecked = false;
        return;
      }

      try
      {
        // Check if the file has changed
        var fileInfo = new FileInfo(_currentLogFile);
        bool hasChanged = fileInfo.LastWriteTime != lastModifiedTime || fileInfo.Length != lastFileSize;

        if (hasChanged)
        {
          // Update stored file information
          lastModifiedTime = fileInfo.LastWriteTime;
          lastFileSize = fileInfo.Length;

          // Simulate the file change event
          Dispatcher.Invoke(() =>
          {
            // Process changes as if the file was modified
            ProcessChanges();
          });
        }
      }
      catch (Exception ex)
      {
        // Handle any exceptions during the check
        Console.WriteLine($"Error during forced refresh: {ex.Message}");
      }
    }

    // Find functionality methods
    private void ShowFindPanel(string? populateWith = null)
    {
      if (tabControl.SelectedItem is MetroTabItem selectedTabItem && selectedTabItem != addTabButton)
      {
        findPanel.Visibility = Visibility.Visible;
        searchResultPositions.Clear();
        currentSearchPosition = -1;

        if (!string.IsNullOrEmpty(populateWith))
          txtFindText.Text = populateWith;

        txtFindText.Focus();
        FindVisualDescendant<TextBox>(txtFindText)?.SelectAll();
      }
    }

    private void CloseFindPanel()
    {
      findPanel.Visibility = Visibility.Collapsed;
      ClearSearchHighlights();
      ClearSearchStatus();
    }

    private void TxtFindText_Loaded(object sender, RoutedEventArgs e)
    {
      txtFindText.ItemsSource = AppConfig.SearchHistory;
      // Wire TextChanged from the inner editable TextBox (ComboBox has no TextChanged of its own).
      var innerTb = FindVisualDescendant<TextBox>(txtFindText);
      if (innerTb != null)
        innerTb.TextChanged += TxtFindText_TextChanged;
      // Auto-search when the user picks an item from the history dropdown.
      txtFindText.SelectionChanged += (s, ev) =>
      {
        if (txtFindText.SelectedItem is string)
          PerformSearch();
      };
    }

    private void TxtFindText_TextChanged(object sender, TextChangedEventArgs e)
    {
      if (string.IsNullOrEmpty(txtFindText.Text))
      {
        ClearSearchHighlights();
        ClearSearchStatus();
      }
    }

    private void TxtFindText_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        // Perform search when Enter is pressed
        PerformSearch();

        if (searchResultPositions.Count > 0)
        {
          // Then perform navigation based on Shift modifier
          if (Keyboard.Modifiers == ModifierKeys.Shift)
          {
            FindPrevious();
          }
          else
          {
            FindNext();
          }
        }
        e.Handled = true;
      }
      else if (e.Key == Key.Escape)
      {
        CloseFindPanel();
        e.Handled = true;
      }
    }

    private void BtnCloseFindPanel_Click(object sender, RoutedEventArgs e)
    {
      CloseFindPanel();
    }

    private void BtnFindNext_Click(object sender, RoutedEventArgs e)
    {
      FindNext();
    }

    private void BtnFindPrevious_Click(object sender, RoutedEventArgs e)
    {
      FindPrevious();
    }

    private void FindOptions_Changed(object sender, RoutedEventArgs e)
    {
      PerformSearch();
    }

    private void PerformSearch()
    {
      // Get the active tab's content TextBox
      if (tabControl.SelectedItem is MetroTabItem selectedTabItem && selectedTabItem != addTabButton)
      {
        var tabInfo = tabs.FirstOrDefault(t => t.TabItem == selectedTabItem);
        if (tabInfo != null && !string.IsNullOrEmpty(txtFindText.Text))
        {
          // Update current search tab
          currentSearchTab = tabInfo;

          // Clear previous search results
          searchResultPositions.Clear();
          currentSearchPosition = -1;

          string searchTerm = txtFindText.Text;
          bool matchCase = chkMatchCase.IsChecked == true;

          AppConfig.AddSearchTerm(searchTerm);
          txtFindText.ItemsSource = null;
          txtFindText.ItemsSource = AppConfig.SearchHistory;
          txtFindText.Text = searchTerm;

          // Search across disk-backed line store; keep only lines that are currently visible
          // (i.e. at or after StartIndex). Results are stored as absolute line indices so
          // ScrollToLine can convert them to visible indices itself.
          int visStart = tabInfo.LineCollection.StartIndex;
          searchResultPositions.AddRange(tabInfo.LineStore.Search(searchTerm, matchCase).Where(i => i >= visStart));

          // Update highlight properties so every visible TextBlock marks its matches
          SearchMatchCase = matchCase;
          ActiveSearchTerm = searchTerm;

          // Set initial position and highlight first result
          if (searchResultPositions.Count > 0)
          {
            currentSearchPosition = 0;
            HighlightCurrentResult();
          }
          UpdateSearchStatus();
        }
      }
    }

    private void FindNext()
    {
      if (currentSearchTab == null || searchResultPositions.Count == 0)
      {
        PerformSearch();
        return;
      }

      // Move to next result
      currentSearchPosition = (currentSearchPosition + 1) % searchResultPositions.Count;
      HighlightCurrentResult();
      UpdateSearchStatus();
    }

    private void FindPrevious()
    {
      if (currentSearchTab == null || searchResultPositions.Count == 0)
      {
        PerformSearch();
        return;
      }

      // Move to previous result
      currentSearchPosition = (currentSearchPosition - 1 + searchResultPositions.Count) % searchResultPositions.Count;
      HighlightCurrentResult();
      UpdateSearchStatus();
    }

    private void HighlightCurrentResult()
    {
      if (
        currentSearchTab == null
        || searchResultPositions.Count == 0
        || currentSearchPosition < 0
        || currentSearchPosition >= searchResultPositions.Count
      )
        return;

      // searchResultPositions holds line indices into the disk store
      int lineIndex = searchResultPositions[currentSearchPosition];
      currentSearchTab.ScrollToLine(lineIndex, txtFindText.Text, chkMatchCase.IsChecked == true, ContentFontFamily);
      UpdateSearchStatus();
    }

    private void ClearSearchHighlights()
    {
      if (currentSearchTab != null)
        currentSearchTab.ContentListBox.SelectedIndex = -1;
      ActiveSearchTerm = string.Empty;
    }

    // Status bar methods
    private void UpdateFileStatus()
    {
      try
      {
        if (_currentLogFile != null && File.Exists(_currentLogFile))
        {
          var fileInfo = new FileInfo(_currentLogFile);
          string fileName = Path.GetFileName(_currentLogFile);
          string lastUpdate = $"Last updated: {fileInfo.LastWriteTime:HH:mm:ss}";
          string fileSize = $"Size: {(fileInfo.Length / 1024.0):N1} KB";

          // Ensure status updates happen on the UI thread and are processed immediately
          if (System.Windows.Threading.Dispatcher.CurrentDispatcher.CheckAccess())
          {
            StatusLineFileInfo = $"{fileName} - {lastUpdate} - {fileSize}";
            // Force UI update
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
              System.Windows.Threading.DispatcherPriority.Render,
              new Action(() => { })
            );
          }
          else
          {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
              System.Windows.Threading.DispatcherPriority.Normal,
              new Action(() =>
              {
                StatusLineFileInfo = $"{fileName} - {lastUpdate} - {fileSize}";
                // Force UI update
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                  System.Windows.Threading.DispatcherPriority.Render,
                  new Action(() => { })
                );
              })
            );
          }
        }
        else if (_currentLogFile != null)
        {
          StatusLineFileInfo = $"File not found: {Path.GetFileName(_currentLogFile)}";
        }
        else if (_currentLogFolder != null)
        {
          StatusLineFileInfo = $"Watching folder: {_currentLogFolder}";
        }
        else
        {
          StatusLineFileInfo = "No file is watched.";
        }
      }
      catch (Exception ex)
      {
        StatusLineFileInfo = $"Error: {ex.Message}";
      }
    }

    private void UpdateSearchStatus()
    {
      if (currentSearchPosition >= 0 && searchResultPositions.Count > 0)
      {
        txtSearchStatus.Text = $"Match {currentSearchPosition + 1} of {searchResultPositions.Count}";
      }
      else if (!string.IsNullOrEmpty(txtFindText?.Text) && searchResultPositions.Count == 0)
      {
        txtSearchStatus.Text = "No matches found";
      }
      else
      {
        txtSearchStatus.Text = string.Empty;
      }
    }

    private void ClearSearchStatus()
    {
      txtSearchStatus.Text = string.Empty;
    }

    /// <summary>
    /// Checks if a line matches any auto tab configuration, and creates a new tab if needed.
    /// Only checks first lines (lines starting with '[') for auto tab patterns.
    /// Returns the tab that matches, or null if no auto tabs match.
    /// </summary>
    private TabInfo? CheckAndCreateAutoTab(string line)
    {
      if (autoTabConfigs.Count == 0)
        return null;

      // Only check auto tab patterns against first lines (lines starting with '[')
      if (!IsFirstLineOfMultilineLog(line))
        return null;

      foreach (var config in autoTabConfigs)
      {
        if (!config.IsEnabled || config.CompiledRegex == null)
          continue;

        var match = config.CompiledRegex.Match(line);
        if (match.Success && match.Groups["unique"].Value != null)
        {
          // We have a match - extract the unique value
          string uniqueValue = match.Groups["unique"].Value;

          if (config.LinkedTabsIds.IndexOf(uniqueValue) >= 0)
          {
            // We found an existing tab for this auto tab pattern and unique value
            return null;
          }
          config.LinkedTabsIds.Add(uniqueValue);

          // We need to create a new tab
          string tabName = match.Value;
          string tabRegex = config.GenerateTabRegexPattern(uniqueValue);

          // Create the new tab
          TabInfo? newTab = null;

          Dispatcher.Invoke(() =>
          {
            AddNewTab(tabName, tabRegex, config.AfterLines, true, true);
            newTab = tabs.Last(); // Get the tab we just added
          });

          return newTab;
        }
      }

      // No auto tab match found
      return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public FontFamily ContentFontFamily => AppConfig.UseMonospaceFont ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily;

    private string _activeSearchTerm = string.Empty;
    public string ActiveSearchTerm
    {
      get => _activeSearchTerm;
      private set
      {
        _activeSearchTerm = value;
        OnPropertyChanged(nameof(ActiveSearchTerm));
      }
    }

    private bool _searchMatchCase;
    public bool SearchMatchCase
    {
      get => _searchMatchCase;
      private set
      {
        _searchMatchCase = value;
        OnPropertyChanged(nameof(SearchMatchCase));
      }
    }

    private void AppConfig_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(Config.UseMonospaceFont))
        OnPropertyChanged(nameof(ContentFontFamily));
    }

    private DataTemplate CreateLineDataTemplate()
    {
      var ancestor = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(MainWindow), 1);

      // Single RichTextBox replaces the old TextBox+TextBlock overlay pair.
      // It provides native text selection with no dual-rendering artefacts, and the
      // FlowDocument populated by HighlightRichTextBox carries the search highlights.
      // PART_ContentHost must be a ScrollViewer; OverridesDefaultStyle on it prevents
      // MahApps's implicit ScrollViewer style from imposing a MinHeight.
      var rtbScrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
      rtbScrollFactory.Name = "PART_ContentHost";
      rtbScrollFactory.SetValue(FrameworkElement.OverridesDefaultStyleProperty, true);
      rtbScrollFactory.SetValue(Control.FocusableProperty, false);
      rtbScrollFactory.SetValue(Control.PaddingProperty, new Thickness(0));
      rtbScrollFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
      rtbScrollFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

      var rtbTemplate = new ControlTemplate(typeof(RichTextBox));
      rtbTemplate.VisualTree = rtbScrollFactory;

      var rtbFactory = new FrameworkElementFactory(typeof(RichTextBox));
      rtbFactory.SetValue(FrameworkElement.OverridesDefaultStyleProperty, true);
      rtbFactory.SetValue(FrameworkElement.MinHeightProperty, 0.0);
      rtbFactory.SetValue(Control.TemplateProperty, rtbTemplate);
      rtbFactory.SetValue(RichTextBox.IsReadOnlyProperty, true);
      rtbFactory.SetValue(RichTextBox.BackgroundProperty, Brushes.Transparent);
      rtbFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0));
      rtbFactory.SetValue(Control.PaddingProperty, new Thickness(0));
      rtbFactory.SetValue(Control.FocusVisualStyleProperty, null);
      rtbFactory.SetValue(SpellCheck.IsEnabledProperty, false);
      // Keep the manually-driven text selection visible even when the RichTextBox is not focused.
      rtbFactory.SetValue(RichTextBox.IsInactiveSelectionHighlightEnabledProperty, true);
      // Plain click + drag selects partial text within this one line (native RichTextBox
      // behaviour).  Shift+click / Ctrl+click instead select whole rows in the ListBox — that
      // is handled in RowRichTextBox_PreviewMouseLeftButtonDown, which suppresses the
      // RichTextBox's own text-selection for those modifier clicks.
      rtbFactory.AddHandler(
        UIElement.PreviewMouseLeftButtonDownEvent,
        new MouseButtonEventHandler(RowRichTextBox_PreviewMouseLeftButtonDown)
      );
      rtbFactory.SetBinding(HighlightRichTextBox.TextProperty, new System.Windows.Data.Binding(nameof(LineItem.Text)));
      rtbFactory.SetBinding(
        HighlightRichTextBox.SearchTermProperty,
        new System.Windows.Data.Binding(nameof(ActiveSearchTerm)) { RelativeSource = ancestor }
      );
      rtbFactory.SetBinding(
        HighlightRichTextBox.MatchCaseProperty,
        new System.Windows.Data.Binding(nameof(SearchMatchCase)) { RelativeSource = ancestor }
      );
      rtbFactory.SetBinding(
        RichTextBox.FontFamilyProperty,
        new System.Windows.Data.Binding(nameof(ContentFontFamily)) { RelativeSource = ancestor }
      );

      return new DataTemplate { VisualTree = rtbFactory };
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
      if (watcher != null)
      {
        try
        {
          watcher.Changed -= OnFolderFileChanged;
          watcher.Created -= OnFolderFileCreated;
          watcher.Dispose();
          watcher = null;
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Error disposing folderWatcher: {ex.Message}");
        }
      }

      // Stop forced refresh timer if it's running
      StopForcedRefresh();

      // Dispose all tabs to clean up memory and event handlers
      foreach (var tab in tabs)
      {
        try
        {
          tab.Dispose();
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Error disposing tab: {ex.Message}");
        }
      }
      tabs.Clear();

      // Save current application configuration
      try
      {
        Config.SaveConfig(AppConfig);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Error saving configuration: {ex.Message}");
      }
    }

    /// <summary>
    /// Helper method to determine if a line is a "first line" in a multiline log entry.
    /// First lines are defined as lines that start with '[' character.
    /// </summary>
    /// <param name="line">The line to check</param>
    /// <returns>True if this is a first line (starts with '['), false otherwise</returns>
    private static bool IsFirstLineOfMultilineLog(string line)
    {
      return !string.IsNullOrEmpty(line) && line.TrimStart().StartsWith('[');
    }
  }
}
