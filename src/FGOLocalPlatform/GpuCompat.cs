using System;
using System.IO;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The OpenGL compatibility layer for AMD and Intel graphics. The game asks for NVIDIA-only
/// extensions; App\fgoglcompat.dll, when present, is loaded ahead of the hook by the author's
/// launch script and translates them. The package ships the file under compat\ next to the
/// launcher, and this class copies it into App or takes it out again.
/// </summary>
internal static class GpuCompat
{
	private const string FileName = "fgoglcompat.dll";

	private const string DisplayClassKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	public static string TargetPath => Path.Combine(GamePaths.GameRoot, FileName);

	public static string SourcePath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "compat", FileName));

	public static bool SourceAvailable => File.Exists(SourcePath);

	public static bool IsInstalled => File.Exists(TargetPath);

	/// <summary>Puts the layer in place or removes it. Throws on an I/O failure so the caller can say so.</summary>
	public static void Apply(bool enabled)
	{
		if (enabled)
		{
			if (!IsInstalled || new FileInfo(TargetPath).Length != new FileInfo(SourcePath).Length)
			{
				File.Copy(SourcePath, TargetPath, overwrite: true);
			}
		}
		else if (IsInstalled)
		{
			File.Delete(TargetPath);
		}
	}

	/// <summary>
	/// True when any display adapter is NVIDIA. A laptop with NVIDIA beside an Intel chip counts as
	/// NVIDIA, since the hook already asks Windows for the NVIDIA card. Unreadable registry counts as
	/// NVIDIA too, so nothing is installed by guesswork.
	/// </summary>
	public static bool HasNvidiaAdapter()
	{
		try
		{
			using RegistryKey? displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
			if (displayClass == null)
			{
				return true;
			}
			bool sawAdapter = false;
			foreach (string name in displayClass.GetSubKeyNames())
			{
				if (name.Length != 4 || !int.TryParse(name, out _))
				{
					continue;
				}
				using RegistryKey? adapter = displayClass.OpenSubKey(name);
				if (adapter == null)
				{
					continue;
				}
				string provider = adapter.GetValue("ProviderName") as string ?? "";
				string description = adapter.GetValue("DriverDesc") as string ?? "";
				if (provider.Length == 0 && description.Length == 0)
				{
					continue;
				}
				sawAdapter = true;
				if (provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return !sawAdapter;
		}
		catch (Exception)
		{
			return true;
		}
	}
}
