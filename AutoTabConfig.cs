using System;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace X4LogWatcher
{
  /// <summary>
  /// Class used for configuring auto-tabs based on regex pattern matching
  /// </summary>
  public class AutoTabConfig
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
    /// Group number for the constant part of the pattern
    /// </summary>
    [JsonPropertyName("constantGroupNumber")]
    public int ConstantGroupNumber { get; set; }

    /// <summary>
    /// Group number for the variable part of the pattern
    /// (this will be used to generate unique tab names)
    /// </summary>
    [JsonPropertyName("variableGroupNumber")]
    public int VariableGroupNumber { get; set; }

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

    [JsonIgnore]
    public List<string> LinkedTabs { get; set; } = [];

    /// <summary>
    /// Default constructor for JSON deserialization
    /// </summary>
    public AutoTabConfig() { }

    /// <summary>
    /// Constructor with parameters
    /// </summary>
    public AutoTabConfig(string patternRegex, int constantGroupNumber, int variableGroupNumber, int afterLines = 0, bool isEnabled = true)
    {
      PatternRegex = patternRegex;
      ConstantGroupNumber = constantGroupNumber;
      VariableGroupNumber = variableGroupNumber;
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
        return true;
      }
      catch
      {
        CompiledRegex = null;
        return false;
      }
    }

    /// <summary>
    /// Generates a regex pattern for a tab based on a matched value
    /// </summary>
    /// <param name="variableValue">The value extracted from the variable group</param>
    /// <returns>A regex pattern string</returns>
    public string GenerateTabRegexPattern(string variableValue)
    {
      // Make sure regex special characters in the variable value are escaped
      string escapedVariableValue = Regex.Escape(variableValue);
      string variableGroupPattern = GetCaptureGroup(PatternRegex, VariableGroupNumber);
      return PatternRegex.Replace(variableGroupPattern, escapedVariableValue);
    }

    /// <summary>
    /// Counts the number of capture groups in a regex pattern substring
    /// </summary>
    private static int CountCaptureGroups(string pattern)
    {
      // Count the number of opening parentheses that aren't escaped or part of a non-capturing group
      int count = 0;
      bool escaped = false;

      for (int i = 0; i < pattern.Length; i++)
      {
        if (pattern[i] == '\\' && !escaped)
        {
          escaped = true;
        }
        else if (pattern[i] == '(' && !escaped)
        {
          // Check if it's not a non-capturing group (?:...)
          if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == ':')
          {
            // This is a non-capturing group
          }
          else
          {
            count++;
          }
          escaped = false;
        }
        else
        {
          escaped = false;
        }
      }

      return count;
    }

    /// <summary>
    /// Extracts the nth capture group from a regex pattern
    /// </summary>
    private static string GetCaptureGroup(string pattern, int groupIndex)
    {
      // Find the nth capture group in the regex pattern
      int count = 0;
      bool escaped = false;

      for (int i = 0; i < pattern.Length; i++)
      {
        if (pattern[i] == '\\' && !escaped)
        {
          escaped = true;
        }
        else if (pattern[i] == '(' && !escaped)
        {
          // Check if it's not a non-capturing group (?:...)
          if (i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == ':')
          {
            // This is a non-capturing group
          }
          else
          {
            count++;
            if (count == groupIndex)
            {
              return pattern.Substring(i);
            }
          }
          escaped = false;
        }
        else
        {
          escaped = false;
        }
      }

      return string.Empty; // No such group found
    }

    /// <summary>
    /// Validates the regex pattern and group numbers
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool Validate()
    {
      try
      {
        // Try to compile the regex
        var regex = new Regex(PatternRegex);

        // Count the number of capture groups
        int groupCount = CountCaptureGroups(PatternRegex);

        // Check if the group numbers are valid
        if (ConstantGroupNumber < 1 || ConstantGroupNumber > groupCount || VariableGroupNumber < 1 || VariableGroupNumber > groupCount)
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
