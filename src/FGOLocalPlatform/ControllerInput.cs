using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FGOLocalPlatform;

internal static class ControllerInput
{
	internal struct Gamepad
	{
		public ushort Buttons;

		public byte LeftTrigger;

		public byte RightTrigger;

		public short LeftX;

		public short LeftY;

		public short RightX;

		public short RightY;
	}

	internal struct State
	{
		public uint Packet;

		public Gamepad Gamepad;
	}

	internal struct Vibration
	{
		public ushort Left;

		public ushort Right;
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate uint ReadState(uint index, out State state);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate uint ReadRaw(uint index, out uint controls);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate uint Vibrate(uint index, ref Vibration vibration);

	private static IntPtr dualSenseModule;

	private static IntPtr systemModule;

	internal static bool DualSenseRuntimePresent
	{
		get
		{
			if (File.Exists(Path.Combine(GamePaths.GameRoot, "fgoio_dualsense.dll")))
			{
				return File.Exists(Path.Combine(GamePaths.GameRoot, "xinput1_4.dll"));
			}
			return false;
		}
	}

	private static IntPtr Module(bool dualSense)
	{
		ref IntPtr reference = ref dualSense ? ref dualSenseModule : ref systemModule;
		if (reference == IntPtr.Zero)
		{
			reference = NativeLibrary.Load(Path.Combine(dualSense ? GamePaths.GameRoot : Environment.SystemDirectory, "xinput1_4.dll"));
		}
		return reference;
	}

	internal static uint GetState(bool dualSense, uint index, out State state)
	{
		return Marshal.GetDelegateForFunctionPointer<ReadState>(NativeLibrary.GetExport(Module(dualSense), "XInputGetState"))(index, out state);
	}

	internal static uint GetRawButtons(uint index, out uint controls)
	{
		return Marshal.GetDelegateForFunctionPointer<ReadRaw>(NativeLibrary.GetExport(Module(dualSense: true), "DualSenseGetRawButtons"))(index, out controls);
	}

	internal static uint SetVibration(bool dualSense, uint index, ref Vibration vibration)
	{
		return Marshal.GetDelegateForFunctionPointer<Vibrate>(NativeLibrary.GetExport(Module(dualSense), "XInputSetState"))(index, ref vibration);
	}

	internal static bool IsLoadError(Exception ex)
	{
		if (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException || ex is FileNotFoundException)
		{
			return true;
		}
		return false;
	}

	internal static string LoadErrorMessage(Exception ex)
	{
		return "The controller input component could not be loaded - reinstall the full update package. Details: " + ex.Message;
	}
}
