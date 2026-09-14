using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FGOLocalPlatform;

internal static class DisplayMonitor
{
	internal sealed record Entry(string Device, string Label);

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfo
	{
		public int Size;

		public Rect Monitor;

		public Rect Work;

		public uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string Device;
	}

	private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

	internal static IReadOnlyList<Entry> GetConnected()
	{
		List<Entry> entries = new List<Entry>();
		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr _, IntPtr _, IntPtr _)
		{
			MonitorInfo info = new MonitorInfo
			{
				Size = Marshal.SizeOf<MonitorInfo>(),
				Device = ""
			};
			if (GetMonitorInfo(monitor, ref info))
			{
				string value = info.Device.Replace("\\\\.\\DISPLAY", "Display ");
				entries.Add(new Entry(info.Device, $"{value}{(((info.Flags & 1) != 0) ? " (primary)" : "")} - {info.Monitor.Right - info.Monitor.Left}x{info.Monitor.Bottom - info.Monitor.Top}"));
			}
			return true;
		}, IntPtr.Zero);
		return entries;
	}
}
