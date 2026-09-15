using System;
using System.IO;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The OpenGL compatibility layer for AMD and Intel graphics. The game asks for NVIDIA-only
/// extensions, and a layer in App\ translates them. Two layers exist: fluphus's shim, which the
/// package ships under compat\amd-shim as App\opengl32.dll beside a copy of the system DLL and a
/// driver profile, and the older fgoglcompat.dll that installs from before 1.1.1 still carry
/// under compat\. An install keeps whichever layer it has; nothing here changes a layer except
/// the switch on the Display page, which is the player's to use, and the first run of a fresh
/// install without an NVIDIA card.
/// </summary>
internal static class GpuCompat
{
	internal enum Layer
	{
		None,
		Legacy,
		Shim,
		/// <summary>An App\opengl32.dll the launcher did not put there: the shim's own installer, or a newer build of it.</summary>
		Foreign
	}

	private const string LegacyFileName = "fgoglcompat.dll";

	/// <summary>compat\amd-shim\opengl32.dll: fluphus/fgo-arcade-amd-shim at commit 1fbf3e4.</summary>
	private const string ShimSha256 = "a85042d91ea60a3108f28dfad4673265f2e1f2a402b252436e5d5757c368a0f0";

	private const string DisplayClassKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	private static string CompatRoot => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "compat"));

	public static string LegacySourcePath => Path.Combine(CompatRoot, LegacyFileName);

	public static string ShimSourcePath => Path.Combine(CompatRoot, "amd-shim", "opengl32.dll");

	private static string ShimConfigSourcePath => Path.Combine(CompatRoot, "amd-shim", "amdcfg", "amdOglpSettings.cfg");

	private static string LegacyTargetPath => Path.Combine(GamePaths.GameRoot, LegacyFileName);

	private static string ShimTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32.dll");

	private static string ShimRealTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32real.dll");

	private static string ShimConfigTargetPath => Path.Combine(GamePaths.GameRoot, "amdcfg", "amdOglpSettings.cfg");

	public static bool LegacySourceAvailable => File.Exists(LegacySourcePath);

	public static bool ShimSourceAvailable => File.Exists(ShimSourcePath) && File.Exists(ShimConfigSourcePath);

	public static bool SourceAvailable => ShimSourceAvailable || LegacySourceAvailable;

	/// <summary>The layer in App\ right now.</summary>
	public static Layer Installed
	{
		get
		{
			if (File.Exists(ShimTargetPath))
			{
				return string.Equals(Updater.HashFile(ShimTargetPath), ShimSha256, StringComparison.OrdinalIgnoreCase) ? Layer.Shim : Layer.Foreign;
			}
			return File.Exists(LegacyTargetPath) ? Layer.Legacy : Layer.None;
		}
	}

	public static bool IsInstalled => Installed != Layer.None;

	/// <summary>
	/// Turns the layer on or off. On keeps the layer the install already has and gives a fresh
	/// install the shim; off removes what is there, a foreign opengl32.dll included, since the
	/// switch is only ever moved by the player. Throws on an I/O failure so the caller can say so.
	/// </summary>
	public static void Apply(bool enabled)
	{
		switch (Installed)
		{
		case Layer.Shim:
		case Layer.Foreign:
			if (!enabled)
			{
				RemoveShim();
			}
			return;
		case Layer.Legacy:
			if (!enabled)
			{
				File.Delete(LegacyTargetPath);
			}
			else if (new FileInfo(LegacyTargetPath).Length != new FileInfo(LegacySourcePath).Length)
			{
				File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
			}
			return;
		default:
			if (!enabled)
			{
				return;
			}
			if (ShimSourceAvailable)
			{
				InstallShim();
			}
			else
			{
				File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
			}
			return;
		}
	}

	/// <summary>Replaces the older layer with the shim. The older layer's file stays under compat\ for the way back.</summary>
	public static void SwitchToShim()
	{
		if (File.Exists(LegacyTargetPath))
		{
			File.Delete(LegacyTargetPath);
		}
		InstallShim();
	}

	/// <summary>Replaces the shim with the older layer this install had before.</summary>
	public static void SwitchToLegacy()
	{
		RemoveShim();
		File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
	}

	/// <summary>
	/// The three files fluphus's installer writes: the system OpenGL DLL under the name the shim
	/// forwards to, the shim itself, and the driver profile.
	/// </summary>
	private static void InstallShim()
	{
		File.Copy(Path.Combine(Environment.SystemDirectory, "opengl32.dll"), ShimRealTargetPath, overwrite: true);
		File.Copy(ShimSourcePath, ShimTargetPath, overwrite: true);
		Directory.CreateDirectory(Path.GetDirectoryName(ShimConfigTargetPath));
		File.Copy(ShimConfigSourcePath, ShimConfigTargetPath, overwrite: true);
	}

	private static void RemoveShim()
	{
		foreach (string path in new string[3] { ShimTargetPath, ShimRealTargetPath, ShimConfigTargetPath })
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		string configFolder = Path.GetDirectoryName(ShimConfigTargetPath);
		if (Directory.Exists(configFolder) && Directory.GetFileSystemEntries(configFolder).Length == 0)
		{
			Directory.Delete(configFolder);
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
