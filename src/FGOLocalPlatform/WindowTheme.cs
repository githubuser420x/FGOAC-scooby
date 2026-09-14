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
		window.Background = new SolidColorBrush(Color.FromRgb(12, 10, 14));
		window.Foreground = Brushes.White;
		window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
		window.FontSize = 14.0;
		window.UseLayoutRounding = true;
		TextOptions.SetTextFormattingMode((DependencyObject)(object)window, (TextFormattingMode)1);
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
