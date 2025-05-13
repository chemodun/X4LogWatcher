using System;
using System.Data.Common;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace X4LogWatcher
{
  /// <summary>
  /// Class used for configuring auto-tabs based on regex pattern matching
  /// </summary>
  public partial class AutoTabConfig
  {
    /// <summary>
    /// Regex pattern to match log lines
    /// </summary>
    [JsonPropertyName("patternRegex")]
    public string PatternRegex { get; set; } = string.Empty;

    /// <summary>
    /// Compiled version of the PatternRegex pattern
    /// </summary>
    [JsonIgnore]
    public Regex? CompiledRegex { get; private set; }

    /// <summary>
    /// Content the unique part of the regex
    /// </summary>
    private string VariablePattern { get; set; } = string.Empty;

    /// <summary>
    /// Whether this auto-tab configuration is enabled
    /// </summary>
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Number of additional lines to show after a match
    /// </summary>
    [JsonPropertyName("afterLines")]
    public int AfterLines { get; set; } = 0;

    /// <summary>
    /// List of tabs IDs (unique parts of patterns) linked to this auto-tab configuration
    /// </summary>
    [JsonIgnore]
    public List<string> LinkedTabsIds { get; set; } = [];

    /// <summary>
    /// Default constructor for JSON deserialization
    /// </summary>
    public AutoTabConfig() { }

    /// <summary>
    /// Constructor with parameters
    /// </summary>
    public AutoTabConfig(string patternRegex, int afterLines = 0, bool isEnabled = true)
    {
      PatternRegex = patternRegex;
      AfterLines = afterLines;
      IsEnabled = isEnabled;
      UpdateRegex();
    }

    /// <summary>
    /// Compiles the regex pattern
    /// </summary>
    /// <returns>True if compilation successful, false otherwise</returns>
    public bool UpdateRegex()
    {
      try
      {
        CompiledRegex = new Regex(PatternRegex);
        return ExtractUniquePattern();
      }
      catch
      {
        CompiledRegex = null;
        return false;
      }
    }

    /// <summary>
    /// Extracts capturing groups from the regex pattern
    /// </summary>
    bool ExtractUniquePattern()
    {
      string groupRegex = $@"\(\?<(unique)>(.*?)\)";
      Match match = Regex.Match(PatternRegex, groupRegex);

      if (match.Success && match.Groups.Count > 2)
      {
        VariablePattern = match.Value;
        return true;
      }

      return false; //
    }

    public void ResetTabs()
    {
      LinkedTabsIds.Clear();
    }

    /// <summary>
    /// Generates a regex pattern for a tab based on a matched value
    /// </summary>
    /// <param name="uniqueValue">The value extracted from the unique group</param>
    /// <returns>A regex pattern string</returns>
    public string GenerateTabRegexPattern(string uniqueValue)
    {
      // Make sure regex special characters in the unique value are escaped
      string escapedUniqueValue = Regex.Escape(uniqueValue);
      // Replace the unique group in the pattern with the escaped value
      return PatternRegex.Replace(VariablePattern, escapedUniqueValue);
    }

    /// <summary>
    /// Validates the regex pattern and named groups
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool Validate()
    {
      try
      {
        // Try to compile the regex
        var regex = new Regex(PatternRegex);

        // Check if the unique named group is valid
        if (!ExtractUniquePattern())
        {
          return false;
        }

        return true;
      }
      catch
      {
        return false;
      }
    }
  }
}
