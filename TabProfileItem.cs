using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace X4LogWatcher
{
  /// <summary>
  /// Class used for serializing and deserializing tab information to/from profiles
  /// </summary>
  public class TabProfileItem
  {
    [JsonPropertyName("tabName")]
    public string TabName { get; set; } = string.Empty;

    [JsonPropertyName("regexPattern")]
    public string RegexPattern { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("afterLines")]
    public int AfterLines { get; set; }

    // Default constructor for JSON deserialization
    public TabProfileItem() { } // Constructor to create from a TabInfo

    public TabProfileItem(TabInfo tabInfo)
    {
      TabName = tabInfo.TabName;
      RegexPattern = tabInfo.RegexPattern;
      IsEnabled = tabInfo.IsWatchingEnabled;
      AfterLines = tabInfo.AfterLines;
    }
  }

  /// <summary>
  /// Class used for serializing and deserializing a complete tab profile including auto tab configurations
  /// </summary>
  public class TabProfile
  {
    [JsonPropertyName("tabs")]
    public List<TabProfileItem> Tabs { get; set; } = new List<TabProfileItem>();

    [JsonPropertyName("autoTabConfigs")]
    public List<AutoTabConfig> AutoTabConfigs { get; set; } = new List<AutoTabConfig>();
  }
}
