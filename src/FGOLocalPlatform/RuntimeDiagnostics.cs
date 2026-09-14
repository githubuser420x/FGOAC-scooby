using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class RuntimeDiagnostics
{
	public static async Task<string> CheckAsync()
	{
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(GamePaths.GameRoot, redirectOutput: true, new string[2] { "-File", Path.Combine(GamePaths.GameRoot, "FGO_EnvironmentCheck.ps1") });
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not start the environment check.");
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			throw new IOException("The environment check took longer than 45 seconds. Check whether your antivirus is blocking PowerShell or the bundled Python, then run it again.");
		}
		string text = $"Checked at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScript host: {PowerShellHost.Executable}\n\n";
		string result = text + await output;
		string text2 = await error;
		if (!string.IsNullOrWhiteSpace(text2))
		{
			result = result + "\n" + text2;
		}
		Directory.CreateDirectory(GamePaths.LogsRoot);
		File.WriteAllText(Path.Combine(GamePaths.LogsRoot, "environment-check.txt"), result, Encoding.UTF8);
		return result;
	}
}
