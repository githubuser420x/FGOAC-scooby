using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class App : Application
{
	[CompilerGenerated]
	private static class _003C_003EO
	{
		public static DispatcherUnhandledExceptionEventHandler _003C0_003E__OnDispatcherUnhandledException;
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		object obj = _003C_003EO._003C0_003E__OnDispatcherUnhandledException;
		if (obj == null)
		{
			DispatcherUnhandledExceptionEventHandler val = OnDispatcherUnhandledException;
			_003C_003EO._003C0_003E__OnDispatcherUnhandledException = val;
			obj = (object)val;
		}
		base.DispatcherUnhandledException += (DispatcherUnhandledExceptionEventHandler)obj;
		try
		{
			Directory.SetCurrentDirectory(GamePaths.GameRoot);
			StartupDiagnostics.CheckLayout();
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "FGOAC scooby - Startup Check", MessageBoxButton.OK, MessageBoxImage.Hand);
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
