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
		ProcessStartInfo processStartInfo = new ProcessStartInfo(PowerShellHost.Executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = GamePaths.GameRoot,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		string[] array = new string[7]
		{
			"-NoLogo",
			"-NoProfile",
			"-NonInteractive",
			"-ExecutionPolicy",
			"Bypass",
			"-File",
			Path.Combine(GamePaths.GameRoot, "FGO_EnvironmentCheck.ps1")
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("无法运行环境检查。");
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
			throw new IOException("环境检查超过 45 秒，请查看安全软件是否阻止脚本或内置 Python。");
		}
		string text = $"检查时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n脚本程序：{PowerShellHost.Executable}\n\n";
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
