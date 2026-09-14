using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FGOLocalPlatform;

internal static class PowerShellHost
{
	private static readonly Lazy<string> selected = new Lazy<string>(Find);

	public static string Executable => selected.Value;

	private static string Find()
	{
		List<string> list = new List<string>();
		string[] array = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				list.Add(Path.Combine(text.Trim('"'), "pwsh.exe"));
			}
		}
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"));
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
		foreach (string item2 in list.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!File.Exists(item2))
			{
				continue;
			}
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(item2)
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				array = new string[5] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "if ([IntPtr]::Size -eq 8 -and ($PSVersionTable.PSVersion -ge [version]'5.1') -and ($PSVersionTable.PSVersion.Major -ne 6)) { exit 0 } else { exit 6 }" };
				foreach (string item in array)
				{
					processStartInfo.ArgumentList.Add(item);
				}
				using Process process = Process.Start(processStartInfo);
				if (process != null)
				{
					process.StandardOutput.ReadToEndAsync();
					process.StandardError.ReadToEndAsync();
					if (!process.WaitForExit(10000))
					{
						process.Kill(entireProcessTree: true);
					}
					else if (process.ExitCode == 0)
					{
						return item2;
					}
				}
			}
			catch (Exception ex) when (((ex is Win32Exception || ex is IOException || ex is InvalidOperationException) ? 1 : 0) != 0)
			{
			}
		}
		throw new IOException("No 64-bit PowerShell was found - enable the built-in Windows PowerShell 5.1, install PowerShell 7, or add the folder holding a portable pwsh.exe to PATH. Any 7.x version will do.");
	}
}
