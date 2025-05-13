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
      }

      // Update the regex
      AutoTabConfig.PatternRegex = txtPattern.Text;

      if (!int.TryParse(txtConstantGroup.Text, out int constantGroup) || constantGroup < 1)
      {
        MessageBox.Show(
          "Constant group number must be a positive integer.",
          "Validation Error",
          MessageBoxButton.OK,
          MessageBoxImage.Warning
        );
        return;
      }
      AutoTabConfig.ConstantGroupNumber = constantGroup;

      if (!int.TryParse(txtVariableGroup.Text, out int variableGroup) || variableGroup < 1)
      {
        MessageBox.Show(
          "Variable group number must be a positive integer.",
          "Validation Error",
          MessageBoxButton.OK,
          MessageBoxImage.Warning
        );
        return;
      }
      AutoTabConfig.VariableGroupNumber = variableGroup;

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
        MessageBox.Show("Invalid regex pattern.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      // Validate the configuration
      if (!AutoTabConfig.Validate())
      {
        MessageBox.Show(
          "Invalid auto tab configuration. Make sure the group numbers exist in the pattern.",
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
  }
}
