using System;
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

    // Default constructor for JSON deserialization
    public TabProfileItem() { }

    // Constructor to create from a TabInfo
    public TabProfileItem(TabInfo tabInfo)
    {
      TabName = tabInfo.TabName;
      RegexPattern = tabInfo.RegexPattern;
      IsEnabled = tabInfo.IsWatchingEnabled;
    }
  }
}
