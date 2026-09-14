using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			Directory.SetCurrentDirectory(GamePaths.GameRoot);
			StartupDiagnostics.CheckLayout();
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "FGOAC scooby - Startup Check", MessageBoxButton.OK, MessageBoxImage.Hand);
			// The main window would still be created after this method returns; clearing the
			// start-up page keeps it from opening against an install that just failed the check.
			StartupUri = null;
			Shutdown(4);
			return;
		}
		base.OnStartup(e);
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		ThemedMessageBox.Show(StartupDiagnostics.Explain((!(e.Exception is UnauthorizedAccessException)) ? 1 : 4) + "\n\n" + e.Exception, "FGOAC scooby - Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		e.Handled = true;
	}
}
