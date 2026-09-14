using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class DiagnosticsView : UserControl, IComponentConnector
{
	public DiagnosticsView()
	{
		InitializeComponent();
		ErrorGuideText.Text = StartupDiagnostics.GameErrors + "\n\n" + string.Join("\n\n", new int[11]
		{
			2, 3, 4, 5, 10, 11, 12, 13, 14, 15,
			22
		}.Select((int code) => $"Launcher exit code {code} - {StartupDiagnostics.Explain(code)}"));
	}

	private async void EnvironmentCheck_OnClick(object sender, RoutedEventArgs e)
	{
		EnvironmentCheckButton.IsEnabled = false;
		EnvironmentCheckText.Text = "Checking the runtime environment...";
		try
		{
			TextBlock environmentCheckText = EnvironmentCheckText;
			environmentCheckText.Text = await RuntimeDiagnostics.CheckAsync();
		}
		catch (Exception ex)
		{
			EnvironmentCheckText.Text = ex.Message;
		}
		finally
		{
			EnvironmentCheckButton.IsEnabled = true;
		}
	}
}
