using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace X4LogWatcher
{
  public class Config : INotifyPropertyChanged
  {
    private static readonly int MaxRecentProfiles = 5;
    private bool _isSaving; // Flag to prevent recursive saves

    // Backing fields for properties
    private ObservableCollection<string> _recentProfiles = new ObservableCollection<string>();
    private string? _activeProfile;
    private string? _lastLogFolderPath;
    private string _logFileExtension = ".log";
    private bool _skipSignatureErrors = true;
    private bool _realTimeStamping;
    private JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };

    // Properties with notification
    public ObservableCollection<string> RecentProfiles
    {
      get => _recentProfiles;
      set => SetProperty(ref _recentProfiles, value);
    }

    public string? ActiveProfile
    {
      get => _activeProfile;
      set => SetProperty(ref _activeProfile, value);
    }

    public string? LastLogFolderPath
    {
      get => _lastLogFolderPath;
      set => SetProperty(ref _lastLogFolderPath, value);
    }

    public string LogFileExtension
    {
      get => _logFileExtension;
      set => SetProperty(ref _logFileExtension, value);
    }

    public bool SkipSignatureErrors
    {
      get => _skipSignatureErrors;
      set => SetProperty(ref _skipSignatureErrors, value);
    }

    public bool RealTimeStamping
    {
      get => _realTimeStamping;
      set => SetProperty(ref _realTimeStamping, value);
    }

    // Constructor to initialize the ObservableCollection
    public Config()
    {
      _recentProfiles = new ObservableCollection<string>();
      // Subscribe to collection changes to auto-save when the collection is modified
      _recentProfiles.CollectionChanged += (s, e) => AutoSave();
    }

    // Save configuration to a file
    public static void SaveConfig(Config config)
    {
      try
      {
        string executablePath = AppDomain.CurrentDomain.BaseDirectory;
        string executableName = Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName);
        string configPath = Path.Combine(executablePath, $"{executableName}.cfg");

        // Convert ObservableCollection to List for serialization
        var configToSave = new
        {
          RecentProfiles = new List<string>(config.RecentProfiles),
          config.ActiveProfile,
          config.LastLogFolderPath,
          config.LogFileExtension,
          config.SkipSignatureErrors,
          config.RealTimeStamping,
        };

        // Serialize the config to JSON and write to file
        string jsonConfig = JsonSerializer.Serialize(configToSave, config._jsonSerializerOptions);
        File.WriteAllText(configPath, jsonConfig);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error saving configuration: {ex.Message}");
      }
    }

    // Load configuration from file
    public static Config LoadConfig()
    {
      try
      {
        string executablePath = AppDomain.CurrentDomain.BaseDirectory;
        string executableName = Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName);
        string configPath = Path.Combine(executablePath, $"{executableName}.cfg");

        if (File.Exists(configPath))
        {
          string jsonConfig = File.ReadAllText(configPath);
          var tempConfig = JsonSerializer.Deserialize<Config>(jsonConfig);

          if (tempConfig != null)
          {
            var config = new Config();

            // Copy values from deserialized config
            if (tempConfig.RecentProfiles != null)
            {
              foreach (var profile in tempConfig.RecentProfiles)
              {
                config.RecentProfiles.Add(profile);
              }
            }

            config.ActiveProfile = tempConfig.ActiveProfile;
            config.LastLogFolderPath = tempConfig.LastLogFolderPath;
            config.LogFileExtension = tempConfig.LogFileExtension ?? ".log";
            if (tempConfig.GetType().GetProperty(nameof(SkipSignatureErrors)) != null)
            {
              config.SkipSignatureErrors = tempConfig.SkipSignatureErrors;
            }
            if (tempConfig.GetType().GetProperty(nameof(RealTimeStamping)) != null)
            {
              config.RealTimeStamping = tempConfig.RealTimeStamping;
            }
            return config;
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error loading configuration: {ex.Message}");
      }

      // Return default config if loading fails
      return new Config();
    }

    // Add a profile to the recent list
    public void AddRecentProfile(string profilePath)
    {
      // Set flag to prevent auto-save during this batch operation
      _isSaving = true;

      try
      {
        // Remove the profile if it already exists (Remove returns true if found and removed)
        RecentProfiles.Remove(profilePath);

        // Add the profile to the beginning of the list
        RecentProfiles.Insert(0, profilePath);

        // Trim the list if it exceeds the maximum number of recent profiles
        while (RecentProfiles.Count > MaxRecentProfiles)
        {
          RecentProfiles.RemoveAt(RecentProfiles.Count - 1);
        }

        // Set this as the active profile
        ActiveProfile = profilePath;
      }
      finally
      {
        // Reset flag and manually save at the end
        _isSaving = false;
        SaveConfig(this);
      }
    }

    // Auto-save when any property changes
    private void AutoSave()
    {
      if (!_isSaving)
      {
        SaveConfig(this);
      }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Improved property changed notification method with caller member name
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      // Auto-save the configuration when a property changes
      AutoSave();
    }

    // Helper method to set property values and raise change notification
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
      if (EqualityComparer<T>.Default.Equals(field, value))
      {
        return false;
      }

      field = value;
      OnPropertyChanged(propertyName);
      return true;
    }
  }
}
