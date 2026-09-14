using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FGOLocalPlatform;

internal static class WindowTheme
{
	private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

	private const int DwmUseImmersiveDarkMode = 20;

	public static void Apply(Window window)
	{
		window.Background = (Brush)Application.Current.Resources["GroundBrush"];
		window.Foreground = (Brush)Application.Current.Resources["TextBrush"];
		window.FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
		window.FontSize = 14.0;
		window.UseLayoutRounding = true;
		window.SnapsToDevicePixels = true;
		TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
		TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
		window.SourceInitialized += delegate
		{
			EnableDarkTitleBar(window);
		};
	}

	private static void EnableDarkTitleBar(Window window)
	{
		IntPtr handle = new WindowInteropHelper(window).Handle;
		int attributeValue = 1;
		if (DwmSetWindowAttribute(handle, 20, ref attributeValue, Marshal.SizeOf<int>()) != 0)
		{
			DwmSetWindowAttribute(handle, 19, ref attributeValue, Marshal.SizeOf<int>());
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);
}
