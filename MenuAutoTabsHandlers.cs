using System;
using System.Windows;
using System.Windows.Controls;

namespace X4LogWatcher
{
  /// <summary>
  /// Interaction logic for MenuAutoTabs_Click handler
  /// </summary>
  public partial class MainWindow
  {
    private void MenuAutoTabs_Click(object sender, RoutedEventArgs e)
    {
      // Clear existing items except for the header
      while (menuAutoTabs.Items.Count > 0)
      {
        menuAutoTabs.Items.RemoveAt(0);
      }

      // Add "Add New Auto Tab" item at the top
      var addNewItem = new MenuItem { Header = "Add New Auto Tab..." };
      addNewItem.Click += (s, args) =>
      {
        var dialog = new AutoTabConfigDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
          autoTabConfigs.Add(dialog.AutoTabConfig);
        }
      };
      menuAutoTabs.Items.Add(addNewItem);

      // Add separator if we have any existing configs
      if (autoTabConfigs.Count > 0)
      {
        menuAutoTabs.Items.Add(new Separator());

        // Add all existing configs as menu items
        foreach (var config in autoTabConfigs)
        {
          // Create a display name from the pattern and group info
          string displayName = $"{config.PatternRegex} [{config.ConstantGroupNumber}, {config.VariableGroupNumber}]";

          if (displayName.Length > 40)
          {
            displayName = displayName.Substring(0, 37) + "...";
          }

          if (!config.IsEnabled)
          {
            displayName = "(Disabled) " + displayName;
          }

          var menuItem = new MenuItem { Header = displayName, Tag = config };

          // Add edit and remove as sub-items
          var editItem = new MenuItem { Header = "Edit", Tag = config };
          editItem.Click += (s, args) =>
          {
            if (s is MenuItem item && item.Tag is AutoTabConfig selectedConfig)
            {
              var dialog = new AutoTabConfigDialog(selectedConfig) { Owner = this };
              dialog.ShowDialog();
              // Re-populate the menu to reflect any changes
              MenuAutoTabs_Click(this, new RoutedEventArgs());
            }
          };

          var removeItem = new MenuItem { Header = "Remove", Tag = config };
          removeItem.Click += (s, args) =>
          {
            if (s is MenuItem item && item.Tag is AutoTabConfig selectedConfig)
            {
              var result = MessageBox.Show(
                "Are you sure you want to remove this auto tab configuration?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
              );

              if (result == MessageBoxResult.Yes)
              {
                autoTabConfigs.Remove(selectedConfig);
                // Re-populate the menu to reflect the removal
                MenuAutoTabs_Click(this, new RoutedEventArgs());
              }
            }
          };

          var toggleItem = new MenuItem { Header = config.IsEnabled ? "Disable" : "Enable", Tag = config };

          toggleItem.Click += (s, args) =>
          {
            if (s is MenuItem item && item.Tag is AutoTabConfig selectedConfig)
            {
              selectedConfig.IsEnabled = !selectedConfig.IsEnabled;
              // Re-populate the menu to reflect the change
              MenuAutoTabs_Click(this, new RoutedEventArgs());
            }
          };

          menuItem.Items.Add(editItem);
          menuItem.Items.Add(toggleItem);
          menuItem.Items.Add(removeItem);

          menuAutoTabs.Items.Add(menuItem);
        }
      }
    }
  }
}
