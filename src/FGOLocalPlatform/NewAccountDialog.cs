using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class NewAccountDialog : Window, IComponentConnector
{
	public string AccountName => MasterNameTextBox.Text.Trim();

	public string AccountMode => (AccountModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "normal";

	public NewAccountDialog()
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		MasterNameTextBox.Focus();
	}

	private void CreateButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (AccountName.Length == 0)
		{
			ThemedMessageBox.Show(this, "Enter a Master name.", "New Local Account", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			MasterNameTextBox.Focus();
		}
		else
		{
			base.DialogResult = true;
		}
	}
}
