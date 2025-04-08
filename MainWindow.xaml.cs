using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;
using Microsoft.Win32;

namespace X4LogWatcher
{
  public partial class MainWindow : MetroWindow, INotifyPropertyChanged
  {
    private string? logFolderPath;
    private string? profileFolderPath;
    private FileSystemWatcher? fileWatcher;
    private FileSystemWatcher? folderWatcher;

    private bool _isWatchingFile = false;
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
      }
    }
    private bool _isWatchingFolder = false;
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
      }
    }

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
    private readonly List<TabInfo> tabs = new List<TabInfo>();

    // Command to close tabs
    private ICommand? _closeTabCommand;
    public ICommand CloseTabCommand => _closeTabCommand ??= new RelayCommand<string>(CloseTabByPattern);

    // Configuration object to store recent profiles
    private Config appConfig;

    public MainWindow()
    {
      InitializeComponent();
      TitleText = titlePrefix;
      // Set DataContext to this to allow bindings to work
      this.DataContext = this;

      // Initialize _titleText with titlePrefix
      _titleText = titlePrefix;

      // Initialize menu items as unchecked
      menuWatchLogFile.IsChecked = false;
      menuWatchLogFolder.IsChecked = false;

      // Load application configuration
      appConfig = Config.LoadConfig();

      // Initialize recent profiles menu
      UpdateRecentProfilesMenu();

      // Automatically load the active profile if one exists
      if (!string.IsNullOrEmpty(appConfig.ActiveProfile) && File.Exists(appConfig.ActiveProfile))
      {
        LoadProfile(appConfig.ActiveProfile);
      }
    }

    private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
    {
      OpenFileDialog openFileDialog = new OpenFileDialog
      {
        Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
        Title = "Select Log File",
      };

      openFileDialog.InitialDirectory = InitialFolderToSelect();
      if (openFileDialog.ShowDialog() == true)
      {
        // Handle file selection
        string selectedFile = openFileDialog.FileName;

        // Save the selected folder path to config
        appConfig.LastLogFolderPath = Path.GetDirectoryName(selectedFile);

        // Start watching the selected file
        StartWatching(selectedFile);
        CurrentLogFolder = null;
        IsWatchingFile = true;
      }
      else
      {
        IsWatchingFile = false;
      }
    }

    private string InitialFolderToSelect()
    {
      if (!string.IsNullOrEmpty(appConfig.LastLogFolderPath) && Directory.Exists(appConfig.LastLogFolderPath))
      {
        return appConfig.LastLogFolderPath;
      }
      return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
    {
      // Create a new OpenFolderDialog (native WPF)
      var dialog = new OpenFolderDialog { Title = "Select Log Folder", Multiselect = false };

      dialog.InitialDirectory = InitialFolderToSelect();

      if (dialog.ShowDialog() == true)
      {
        // Handle folder selection
        logFolderPath = dialog.FolderName;

        // Save the selected folder path to config
        appConfig.LastLogFolderPath = logFolderPath;

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
      folderWatcher = new FileSystemWatcher(folderPath, "*.log")
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
      AddNewTab("", defaultRegex, false);
    }

    private void AddNewTab(string tabName, string regexPattern, bool isEnabled)
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

      // Create the first row panel (checkbox and tab name)
      var firstRowPanel = new DockPanel { LastChildFill = true };
      Grid.SetRow(firstRowPanel, 0);
      mainGrid.Children.Add(firstRowPanel);

      // Add enable/disable checkbox
      var chkEnable = new CheckBox
      {
        Content = "Enable",
        IsChecked = isEnabled,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
      };
      DockPanel.SetDock(chkEnable, Dock.Left);
      firstRowPanel.Children.Add(chkEnable);

      // Add Name label
      var lblName = new Label { Content = "Name:", VerticalAlignment = VerticalAlignment.Center };
      DockPanel.SetDock(lblName, Dock.Left);
      firstRowPanel.Children.Add(lblName);

      // Add the tab name input textbox (fills remaining space)
      var txtName = new TextBox
      {
        Text = tabName,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
      };
      firstRowPanel.Children.Add(txtName);

      // Create the second row panel (regex pattern and Apply button)
      var secondRowPanel = new DockPanel { LastChildFill = true };
      Grid.SetRow(secondRowPanel, 1);
      mainGrid.Children.Add(secondRowPanel);

      // Add RegEx label
      var lblRegEx = new Label { Content = "RegEx:", VerticalAlignment = VerticalAlignment.Center };
      DockPanel.SetDock(lblRegEx, Dock.Left);
      secondRowPanel.Children.Add(lblRegEx);

      // Add Apply button (right side)
      var btnApply = new Button
      {
        Content = "Apply",
        Padding = new Thickness(10, 0, 10, 0),
        Margin = new Thickness(5, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
      };
      DockPanel.SetDock(btnApply, Dock.Right);
      secondRowPanel.Children.Add(btnApply);

      // Add the regex input textbox (fills remaining space)
      var txtRegex = new TextBox
      {
        Text = regexPattern,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0),
      };
      secondRowPanel.Children.Add(txtRegex);

      // Create the content panel for the third row (with the text content)
      var contentPanel = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray };
      Grid.SetRow(contentPanel, 2);
      mainGrid.Children.Add(contentPanel);

      // Add TextBox for content
      var txtContent = new TextBox
      {
        AcceptsReturn = true,
        IsReadOnly = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
      };
      contentPanel.Child = txtContent;

      // Create and store TabInfo object
      var tabInfo = new TabInfo(tabItem, txtRegex, chkEnable, txtContent, regexPattern, isEnabled);
      tabs.Add(tabInfo);

      // Set up Apply button click handler using the TabInfo object
      btnApply.Click += (s, e) =>
      {
        if (tabInfo.ValidateRegex() && tabInfo.IsWatchingEnabled && _currentLogFile != null)
        {
          // Reset file position to process from the beginning with new regex
          tabInfo.FilePosition = 0;
          tabInfo.ClearContent();
          ProcessTabContent(tabInfo);
        }
      };

      // Set up checkbox event to process file when enabled
      chkEnable.Checked += (s, e) =>
      {
        if (_currentLogFile != null)
          ProcessTabContent(tabInfo);
      };

      // Add the tab to the tab control BEFORE the "+" tab
      int addButtonIndex = tabControl.Items.IndexOf(addTabButton);
      tabControl.Items.Insert(addButtonIndex, tabItem);
      tabControl.SelectedItem = tabItem;
    }

    private void CloseTab(TabInfo tabInfo)
    {
      // Remove the tab from the tab control
      tabControl.Items.Remove(tabInfo.TabItem);

      // Remove it from our collection
      tabs.Remove(tabInfo);

      // If there are no more tabs, consider adding a default one
      if (tabs.Count == 0 && _currentLogFile != null)
      {
        AddNewTab(".*", ".*", false);
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
      var profileData = tabs.Select(tab => new TabProfileItem(tab)).ToList();

      File.WriteAllText(profilePath, System.Text.Json.JsonSerializer.Serialize(profileData));

      // Add to recent profiles
      appConfig.AddRecentProfile(profilePath);
      UpdateRecentProfilesMenu();
    }

    private void LoadProfile(string profilePath)
    {
      if (!File.Exists(profilePath))
      {
        MessageBox.Show("Profile file not found.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      var profileData = System.Text.Json.JsonSerializer.Deserialize<List<TabProfileItem>>(File.ReadAllText(profilePath));

      if (profileData == null)
      {
        MessageBox.Show("Failed to load profile data.", "Load Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      // Remove all tabs except the add button
      var itemsToRemove = tabControl.Items.Cast<object>().Where(i => i != addTabButton).ToList();
      foreach (var item in itemsToRemove)
      {
        tabControl.Items.Remove(item);
      }
      tabs.Clear();

      // Add tabs from the profile
      foreach (var item in profileData)
      {
        AddNewTab(item.TabName, item.RegexPattern, item.IsEnabled);
      }

      // Update recent profiles
      appConfig.AddRecentProfile(profilePath);
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
          && !(item is Separator)
        )
        {
          menuProfile.Items.Remove(item);
        }
      }

      // Add recent profiles to the menu
      if (appConfig.RecentProfiles.Count > 0)
      {
        menuRecentProfilesHeader.IsEnabled = false; // Keep it as a header

        foreach (var profilePath in appConfig.RecentProfiles)
        {
          // Create a shorter display name (just the filename)
          string displayName = Path.GetFileName(profilePath);

          // Create menu item for this profile
          var menuItem = new MenuItem
          {
            Header = displayName,
            Tag = profilePath,
            IsCheckable = true,
            IsChecked = profilePath == appConfig.ActiveProfile,
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
        bool isActiveProfile = appConfig.ActiveProfile != null && appConfig.ActiveProfile == profilePath;

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
          appConfig.ActiveProfile = profilePath;
        }
        else
        {
          // If it was checked, keep it unchecked (user wants to deselect)
          menuItem.IsChecked = false;
          appConfig.ActiveProfile = null;
        }
      }
    }

    private void StartWatching(string filePath)
    {
      if (fileWatcher != null)
      {
        fileWatcher.Dispose();
      }

      // Store the previous file path before updating
      string? previousLogFile = _currentLogFile;
      bool isFileChanged = previousLogFile != filePath;

      // Update the current file path
      CurrentLogFile = filePath;

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

      // Handle tab states based on file change
      if (isFileChanged)
      {
        // Inform all tabs about the file change
        foreach (var tab in tabs)
        {
          if (tab.IsWatchingEnabled)
          {
            // If watching is enabled, reset position and clear content immediately
            tab.FilePosition = 0;
            tab.ClearContent();
            ProcessTabContent(tab); // Process the new file content
          }
          else
          {
            // If watching is disabled, just set the change flag
            // Content will be reloaded when watching is enabled again
            tab.FileChangedFlag = true;
          }
        }
      }
      else
      {
        // If the file is the same, just process enabled tabs
        ProcessAllEnabledTabs();
      }
    }

    private void OnSingleFileChanged(object sender, FileSystemEventArgs e)
    {
      if (e.FullPath == _currentLogFile)
      {
        Dispatcher.Invoke(() =>
        {
          // Process each tab based on its watching state
          foreach (var tab in tabs)
          {
            if (tab.IsWatchingEnabled)
            {
              // If watching is enabled, process the content now
              ProcessTabContent(tab);
            }
            else
            {
              // If watching is disabled, just mark the file changed flag
              // This will trigger content reset when watching is enabled later
              tab.FileChangedFlag = true;
            }
          }
        });
      }
    }

    private void ProcessAllEnabledTabs()
    {
      if (_currentLogFile == null || !File.Exists(_currentLogFile))
        return;

      foreach (var tab in tabs.Where(t => t.IsWatchingEnabled))
      {
        // Reset position to read from beginning
        tab.FilePosition = 0;
        ProcessTabContent(tab);
      }
    }

    private string? FindMostRecentLogFile(string folderPath)
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
      if (_currentLogFile == null || !File.Exists(_currentLogFile))
        return;

      try
      {
        using (var stream = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
          // Check if we need to process from the beginning
          bool processFromBeginning = tab.FilePosition == 0;

          // If there's new content since last read
          if (stream.Length > tab.FilePosition || processFromBeginning)
          {
            // Position the stream at the right place
            stream.Seek(tab.FilePosition, SeekOrigin.Begin);

            using (var reader = new StreamReader(stream))
            {
              var sb = new StringBuilder();
              string? line;
              int lineCount = 0;

              // Process file line by line
              while ((line = reader.ReadLine()) != null)
              {
                lineCount++;

                // Check if line matches regex
                if (tab.CompiledRegex != null && tab.CompiledRegex.IsMatch(line))
                {
                  var match = tab.CompiledRegex.Match(line);

                  // Handle line with its matched groups
                  if (match.Groups.Count > 1)
                  {
                    sb.AppendLine(line);
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                      sb.AppendLine($"  Group {i}: {match.Groups[i].Value}");
                    }
                  }
                  else
                  {
                    sb.AppendLine(line);
                  }
                }
              }

              // Append new matches to existing content
              if (sb.Length > 0)
              {
                tab.AppendContent(sb.ToString());
              }

              // Store new position
              tab.FilePosition = stream.Length;
            }
          }
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Error processing tab content: {ex.Message}", "Tab Content Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void MenuSaveProfile_Click(object sender, RoutedEventArgs e)
    {
      SaveFileDialog saveDialog = new SaveFileDialog
      {
        Filter = "Profile files (*.profile)|*.profile|All files (*.*)|*.*",
        Title = "Save Profile",
        DefaultExt = "profile",
      };

      if (saveDialog.ShowDialog() == true)
      {
        SaveProfile(saveDialog.FileName);
        // Update recent profiles list
        appConfig.AddRecentProfile(saveDialog.FileName);
        UpdateRecentProfilesMenu();

        MessageBox.Show("Profile saved successfully.", "Save Profile", MessageBoxButton.OK, MessageBoxImage.Information);
      }
    }

    private void MenuLoadProfile_Click(object sender, RoutedEventArgs e)
    {
      OpenFileDialog openDialog = new OpenFileDialog
      {
        Filter = "Profile files (*.profile)|*.profile|All files (*.*)|*.*",
        Title = "Load Profile",
      };

      if (openDialog.ShowDialog() == true)
      {
        LoadProfile(openDialog.FileName);
        // Update recent profiles list
        appConfig.AddRecentProfile(openDialog.FileName);
        UpdateRecentProfilesMenu();
      }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
      this.Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
  }
}
