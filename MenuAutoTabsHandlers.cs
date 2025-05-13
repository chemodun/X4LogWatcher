using System;
using System.Collections.Generic;
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

      // Add all existing configs as menu items
      if (autoTabConfigs.Count > 0)
      {
        // Add the configs to the menu
        foreach (var config in autoTabConfigs)
        {
          // Create a display name that includes regex pattern details
          string configName = config.PatternRegex;

          if (configName.Length > 80)
          {
            configName = configName.Substring(0, 77) + "...";
          }

          var configMenuItem = new MenuItem { Header = configName, Tag = config };
          configMenuItem.IsChecked = config.IsEnabled;

          // Add edit and remove as sub-items
          var editItem = new MenuItem { Header = "Edit", Tag = config };
          editItem.Click += (s, args) =>
          {
            if (s is MenuItem item && item.Tag is AutoTabConfig selectedConfig)
            {
              var dialog = new AutoTabConfigDialog(selectedConfig) { Owner = this };
              if (dialog.ShowDialog() == true)
              {
                // Re-populate the menu to reflect any changes
                MenuAutoTabs_Click(this, new RoutedEventArgs());
              }
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

          configMenuItem.Items.Add(toggleItem);
          configMenuItem.Items.Add(editItem);
          configMenuItem.Items.Add(removeItem);

          menuAutoTabs.Items.Add(configMenuItem);
        }

        menuAutoTabs.Items.Add(new Separator());
      }

      // Add "Add New Auto Tab" item at the bottom
      var addNewItem = new MenuItem { Header = "Add New Auto Tab..." };
      addNewItem.Click += (s, args) =>
      {
        var dialog = new AutoTabConfigDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
          autoTabConfigs.Add(dialog.AutoTabConfig);
          // Re-populate the menu to show the new config
          MenuAutoTabs_Click(this, new RoutedEventArgs());
        }
      };
      menuAutoTabs.Items.Add(addNewItem);
    }
  }
}
