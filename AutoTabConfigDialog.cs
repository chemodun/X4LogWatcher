using System;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;

namespace X4LogWatcher
{
  /// <summary>
  /// Interaction logic for AutoTabConfigDialog.xaml
  /// </summary>
  public partial class AutoTabConfigDialog : Window
  {
    public AutoTabConfig AutoTabConfig { get; private set; }

    public AutoTabConfigDialog()
    {
      InitializeComponent();
      AutoTabConfig = new AutoTabConfig();
      this.DataContext = AutoTabConfig;
    }

    public AutoTabConfigDialog(AutoTabConfig config)
    {
      InitializeComponent();
      AutoTabConfig = config;
      this.DataContext = AutoTabConfig;
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
      // Validate inputs
      if (string.IsNullOrWhiteSpace(txtPattern.Text))
      {
        MessageBox.Show("Pattern cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      } // Update the regex
      AutoTabConfig.PatternRegex = txtPattern.Text;

      if (!int.TryParse(txtAfterLines.Text, out int afterLines) || afterLines < 0)
      {
        MessageBox.Show("After lines must be a non-negative integer.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      AutoTabConfig.AfterLines = afterLines;

      AutoTabConfig.IsEnabled = chkEnabled.IsChecked ?? true;

      // Validate the regex pattern
      if (!AutoTabConfig.UpdateRegex())
      {
        MessageBox.Show(
          "Invalid regex pattern. Pattern must include a named group '?<unique>'.",
          "Validation Error",
          MessageBoxButton.OK,
          MessageBoxImage.Warning
        );
        return;
      } // Validate the configuration
      if (!AutoTabConfig.Validate())
      {
        MessageBox.Show(
          "Invalid auto tab configuration. Make sure your pattern contains the named capturing group '?<unique>'.",
          "Validation Error",
          MessageBoxButton.OK,
          MessageBoxImage.Warning
        );
        return;
      }

      DialogResult = true;
      Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
      // Show confirmation dialog
      var result = MessageBox.Show(
        "Are you sure you want to delete this auto tab configuration?",
        "Confirm Deletion",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question
      );

      if (result == MessageBoxResult.Yes)
      {
        // Return with a special result to indicate deletion
        Tag = "DELETE"; // We'll use the Tag property to communicate deletion intent
        DialogResult = true;
        Close();
      }
    }
  }
}
