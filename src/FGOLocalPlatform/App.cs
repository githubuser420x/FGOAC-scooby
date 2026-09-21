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
			WineLog("Startup check failed: " + ex);
			MessageBox.Show(ex.Message, "FGOAC scooby - Startup Check", MessageBoxButton.OK, MessageBoxImage.Hand);
			Shutdown(4);
			return;
		}
		base.OnStartup(e);
		// The window is opened here rather than through StartupUri, so a failed check above never
		// opens it against an install the launcher cannot run.
		new MainWindow().Show();
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		WineLog("Unhandled UI error: " + e.Exception);
		ThemedMessageBox.Show(StartupDiagnostics.Explain((!(e.Exception is UnauthorizedAccessException)) ? 1 : 4) + "\n\n" + e.Exception, "FGOAC scooby - Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		e.Handled = true;
	}

	/// <summary>
	/// The error dialog is not always visible (remote desktop, Wine), so the message is
	/// also appended to logs\wine-startup.log next to the game folder.
	/// </summary>
	private static void WineLog(string message)
	{
		try
		{
			string directory = GamePaths.LogsRoot;
			Directory.CreateDirectory(directory);
			File.AppendAllText(Path.Combine(directory, "wine-startup.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}
