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
    private FileSystemWatcher? fileWatcher;
    private FileSystemWatcher? folderWatcher;

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
      base.OnKeyDown(e);

      // Handle Ctrl+F to open search
      if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
      {
        ShowFindPanel();
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
      if (folderWatcher != null)
      {
        folderWatcher.Dispose();
        folderWatcher = null;
      }

      if (!Directory.Exists(folderPath))
      {
        MessageBox.Show("Selected folder does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      CurrentLogFolder = folderPath;

      // Set up a watcher for the folder to detect all log files
      folderWatcher = new FileSystemWatcher(folderPath, $"*{AppConfig.LogFileExtension}")
      {
        NotifyFilter =
          NotifyFilters.Attributes
          | NotifyFilters.CreationTime
          | NotifyFilters.DirectoryName
          | NotifyFilters.FileName
          | NotifyFilters.LastWrite
          | NotifyFilters.Security
          | NotifyFilters.Size,
        InternalBufferSize = 64 * 1024 * 1024, // Increase buffer size
        EnableRaisingEvents = true,
      };

      // Subscribe to events
      folderWatcher.Changed += OnFolderFileChanged;
      folderWatcher.Created += OnFolderFileCreated;
      folderWatcher.Error += OnFolderWatcherError;
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
        if (Path.GetExtension(e.FullPath).Equals(".log", StringComparison.OrdinalIgnoreCase))
        {
          // Wait a moment for the file to be accessible
          System.Threading.Thread.Sleep(500);

          // Switch to monitoring the new file since it's just been created
          Dispatcher.Invoke(() =>
          {
            StartWatching(e.FullPath);
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
        if (Path.GetExtension(e.FullPath).Equals(".log", StringComparison.OrdinalIgnoreCase))
        {
          Dispatcher.Invoke(() =>
          {
            StartWatching(e.FullPath);
            if (folderWatcher != null)
            {
              folderWatcher.Changed -= OnFolderFileChanged;
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

      // Add TextBox for content
      var txtContent = new TextBox
      {
        AcceptsReturn = true,
        IsReadOnly = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
      };

      // Add Enter key handling for search navigation when content box has focus
      txtContent.KeyDown += ContentBox_KeyDown;

      contentPanel.Child = txtContent;

      // Create and store TabInfo object
      var tabInfo = new TabInfo(tabItem, chkEnable, txtName, txtRegex, numAfterLines, txtContent, isAutoCreated);
      tabs.Add(tabInfo);

      // Set up Apply button click handler using the TabInfo object
      btnApply.Click += (s, e) =>
      {
        if (tabInfo.ValidateRegex() && tabInfo.IsWatchingEnabled && _currentLogFile != null)
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
          if (tabInfo.ValidateRegex())
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

    private void ContentBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter && findPanel.Visibility == Visibility.Visible)
      {
        // When Enter is pressed in content box and search panel is visible,
        // use the same behavior as F3 to find the next occurrence
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

    private void StartWatching(string filePath)
    {
      fileWatcher?.Dispose();

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

      fileWatcher = new FileSystemWatcher(directoryPath, Path.GetFileName(filePath))
      {
        NotifyFilter =
          NotifyFilters.Attributes
          | NotifyFilters.CreationTime
          | NotifyFilters.DirectoryName
          | NotifyFilters.FileName
          | NotifyFilters.LastWrite
          | NotifyFilters.Security
          | NotifyFilters.Size,
        InternalBufferSize = 64 * 1024 * 1024, // Increase buffer size
        EnableRaisingEvents = true,
      };

      fileWatcher.Changed += OnSingleFileChanged;
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

    private static string? FindMostRecentLogFile(string folderPath)
    {
      try
      {
        var logFiles = Directory.GetFiles(folderPath, "*.log").OrderByDescending(f => new FileInfo(f).LastWriteTime).ToList();

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
    private void ShowFindPanel()
    {
      // Get active tab
      if (tabControl.SelectedItem is MetroTabItem selectedTabItem && selectedTabItem != addTabButton)
      {
        // Show the find panel
        findPanel.Visibility = Visibility.Visible;

        // Clear previous search results
        searchResultPositions.Clear();
        currentSearchPosition = -1;

        // Focus the search text box
        txtFindText.Focus();
        txtFindText.SelectAll();
      }
    }

    private void CloseFindPanel()
    {
      findPanel.Visibility = Visibility.Collapsed;
      ClearSearchHighlights();
      ClearSearchStatus();
    }

    private void TxtFindText_TextChanged(object sender, TextChangedEventArgs e)
    {
      // No longer perform immediate search on text change
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

          // Get the text and search term
          string content = tabInfo.ContentTextBox.Text;
          string searchTerm = txtFindText.Text;

          // Get comparison type based on match case checkbox
          StringComparison comparison = chkMatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

          // Find all occurrences
          int index = 0;
          while ((index = content.IndexOf(searchTerm, index, comparison)) >= 0)
          {
            // Add position to results
            searchResultPositions.Add(index);

            // Move index forward
            index += searchTerm.Length;
          }

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

      // Get position and length
      int startIndex = searchResultPositions[currentSearchPosition];
      int length = txtFindText.Text.Length;

      // Select the text in the TextBox to highlight it
      TextBox contentBox = currentSearchTab.ContentTextBox;
      contentBox.Focus();
      contentBox.Select(startIndex, length);

      // Ensure the highlighted text is visible by scrolling to its line
      contentBox.ScrollToLine(GetLineIndexFromPosition(contentBox.Text, startIndex));

      // Update status information in the status bar
      UpdateSearchStatus();
    }

    private void ClearSearchHighlights()
    {
      if (currentSearchTab != null)
      {
        currentSearchTab.ContentTextBox.SelectionLength = 0;
      }
    }

    // Helper method to get line index from character position
    private static int GetLineIndexFromPosition(string text, int position)
    {
      int lineIndex = 0;
      int currentPos = 0;

      foreach (char c in text.Take(position))
      {
        if (c == '\n')
          lineIndex++;

        currentPos++;
      }

      return lineIndex;
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

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
      // Clean up file and folder watchers
      if (fileWatcher != null)
      {
        try
        {
          fileWatcher.Changed -= OnSingleFileChanged;
          fileWatcher.Dispose();
          fileWatcher = null;
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Error disposing fileWatcher: {ex.Message}");
        }
      }

      if (folderWatcher != null)
      {
        try
        {
          folderWatcher.Changed -= OnFolderFileChanged;
          folderWatcher.Created -= OnFolderFileCreated;
          folderWatcher.Error -= OnFolderWatcherError;
          folderWatcher.Dispose();
          folderWatcher = null;
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
