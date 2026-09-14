using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

/// <summary>
/// Everything that has to happen once before a fresh install can be played: the English patch,
/// the folder and environment checks, a first account with a full roster, and the launch
/// defaults. Each step is skipped when it has already been done, so later starts do nothing.
/// </summary>
internal sealed class FirstRun
{
	internal sealed record ToolResult(bool Ok, string Message, JsonObject? Root);

	private static readonly string[] UpgradeActions = new string[7] { "servants", "craft-essences", "materials", "levels", "bond", "costumes", "quests" };

	/// <summary>
	/// The three programs that listen or connect on the cabinet network. Without a rule for each,
	/// Windows asks about them the first time the game runs, and the question can open behind the
	/// game window.
	/// </summary>
	private static readonly (string Name, string Program, string Description)[] FirewallPrograms = new (string, string, string)[3]
	{
		("FGOAC scooby server", "Server\\python\\python.exe", "the local server"),
		("FGOAC scooby game", "App\\ago.exe", "the game"),
		("FGOAC scooby service", "App\\am\\amdaemon.exe", "the cabinet service")
	};

	/// <summary>
	/// Rule names used before the launcher was renamed. Deleting them keeps an install that ran
	/// 1.0.1 from carrying two rules per program.
	/// </summary>
	private static readonly string[] RetiredFirewallRules = new string[3] { "FGOA scooby server", "FGOA scooby game", "FGOA scooby service" };

	private readonly Window owner;

	private readonly Func<string[], Task<ToolResult>> accountTool;

	private readonly Action<string> report;

	private readonly Action<string> log;

	private readonly List<string> summary = new List<string>();

	private readonly string installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));

	internal FirstRun(Window owner, Func<string[], Task<ToolResult>> accountTool, Action<string> report, Action<string> log)
	{
		this.owner = owner;
		this.accountTool = accountTool;
		this.report = report;
		this.log = log;
	}

	private string MarkerPath => Path.Combine(GamePaths.GameRoot, "zh", "en-patch.json");

	private string ScriptPath => Path.Combine(installRoot, "Apply-EN-Patch.ps1");

	private string ManifestPath => Path.Combine(installRoot, "manifest.json");

	private string LauncherSettingsPath => Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");

	/// <summary>
	/// Returns true when something was changed, so the caller can reload what it shows.
	/// </summary>
	internal async Task<bool> RunAsync()
	{
		bool patched = await ApplyPatchAsync();
		report("Checking your account...");
		ToolResult accounts = await accountTool(new string[2] { "list", "--json" });
		bool needsAccount = NeedsFirstAccount(accounts);
		bool firstTime = patched || needsAccount;
		if (firstTime)
		{
			await CheckEnvironmentAsync();
		}
		await EnsureFirewallRulesAsync(firstTime);
		bool accountCreated = needsAccount && await CreateFirstAccountAsync(accounts);
		bool defaultsWritten = ApplyMissingDefaults();
		if (summary.Count == 0)
		{
			return patched || accountCreated || defaultsWritten;
		}
		report("Everything is ready - press Play when you are.");
		// The setup can take a few minutes, so the player may well be looking at something else:
		// this one has to come to the front and be findable in the task bar.
		ThemedMessageBox.Show(owner, "FGOAC scooby is ready." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, summary) + Environment.NewLine + Environment.NewLine + "Press Play to start the game. Windows asks for permission once, because the game needs administrator rights to run.", "FGOAC scooby - First Run", MessageBoxButton.OK, MessageBoxImage.Asterisk, MessageBoxResult.OK, foreground: true);
		return true;
	}

	private async Task<bool> ApplyPatchAsync()
	{
		if (!File.Exists(ScriptPath) || !File.Exists(ManifestPath))
		{
			return false;
		}
		string version = "";
		string manifestHash = "";
		try
		{
			if (JsonNode.Parse(File.ReadAllText(ManifestPath)) is JsonObject manifest)
			{
				version = manifest["version"]?.GetValue<string>() ?? "";
				manifestHash = manifest["manifestHash"]?.GetValue<string>() ?? "";
			}
		}
		catch (Exception ex)
		{
			log("The English patch manifest could not be read: " + ex.Message);
			return false;
		}
		if (version.Length == 0 || manifestHash.Length == 0)
		{
			log("The English patch manifest has no version, so the patch was skipped.");
			return false;
		}
		if (IsPatchInstalled(version, manifestHash))
		{
			return false;
		}
		report("Installing the English patch - this takes a minute the first time.");
		log("Applying the English patch version " + version);
		StringBuilder transcript = new StringBuilder();
		int exitCode;
		try
		{
			exitCode = await RunPatchScriptAsync(transcript);
		}
		catch (Exception ex2)
		{
			log("The English patch could not be started: " + ex2.Message);
			summary.Add("- The English patch could not be started: " + ex2.Message);
			return false;
		}
		log(transcript.ToString());
		string lastLine = LastLine(transcript.ToString());
		if (exitCode != 0)
		{
			report("The English patch was not installed.");
			summary.Add("- The English patch was not installed. " + lastLine);
			return false;
		}
		summary.Add("- Installed the English patch version " + version + ".");
		return true;
	}

	private bool IsPatchInstalled(string version, string manifestHash)
	{
		try
		{
			if (File.Exists(MarkerPath) && JsonNode.Parse(File.ReadAllText(MarkerPath)) is JsonObject marker)
			{
				return marker["version"]?.GetValue<string>() == version && marker["manifestHash"]?.GetValue<string>() == manifestHash;
			}
		}
		catch (Exception ex)
		{
			log("The patch marker could not be read, so the patch is applied again: " + ex.Message);
		}
		return false;
	}

	private async Task<int> RunPatchScriptAsync(StringBuilder transcript)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(PowerShellHost.Executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = installRoot,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		string[] array = new string[]
		{
			"-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath,
			"-InstallRoot", installRoot, "-PackageRoot", installRoot, "-NonInteractive",
			"-IgnoreProcessId", Environment.ProcessId.ToString()
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("The patch installer did not start.");
		Task<string> errorTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30.0));
		// The output loop below cannot take the token, so the deadline ends the process instead,
		// which closes the pipe and lets the loop finish.
		using CancellationTokenRegistration killOnTimeout = timeout.Token.Register(delegate
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
		});
		string? line;
		while ((line = await process.StandardOutput.ReadLineAsync()) != null)
		{
			transcript.AppendLine(line);
			if (!string.IsNullOrWhiteSpace(line))
			{
				report(line.Trim());
			}
		}
		string text = await errorTask;
		if (!string.IsNullOrWhiteSpace(text))
		{
			transcript.AppendLine(text);
		}
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw new IOException("The patch installer took longer than 30 minutes and was stopped.");
		}
		return process.ExitCode;
	}

	private async Task CheckEnvironmentAsync()
	{
		report("Checking that the game can write to its folders...");
		try
		{
			await Task.Run(StartupDiagnostics.CheckLayout);
		}
		catch (Exception ex)
		{
			log("The folder check failed: " + ex.Message);
			summary.Add("- A folder the game writes to is still locked. " + StartupDiagnostics.Explain(4));
			return;
		}
		report("Running the environment check...");
		string text;
		try
		{
			text = await RuntimeDiagnostics.CheckAsync();
		}
		catch (Exception ex2)
		{
			log("The environment check could not run: " + ex2.Message);
			summary.Add("- The environment check could not run: " + ex2.Message);
			return;
		}
		log(text);
		string[] array = text.Split('\n').Select((string value) => value.Trim()).Where((string value) => value.StartsWith("[ACTION NEEDED]", StringComparison.Ordinal)).ToArray();
		if (array.Length == 0)
		{
			summary.Add("- Environment check: everything passed.");
			return;
		}
		string text2 = array[0].Substring("[ACTION NEEDED]".Length).Trim();
		int num = text2.IndexOf(':');
		if (num > 0)
		{
			text2 = text2.Substring(0, num);
		}
		summary.Add($"- Environment check: {array.Length} item(s) need attention, starting with {text2}. The full report is on the Advanced > Diagnostics and Help page.");
	}

	/// <summary>
	/// Adds one inbound allow rule per program, so Windows never has to ask. Runs on every start,
	/// because a rule can be removed later; a rule that is already there is left alone, and one
	/// that points somewhere else is replaced, which is what happens when the game folder moves.
	/// </summary>
	private async Task EnsureFirewallRulesAsync(bool announce)
	{
		List<string> created = new List<string>();
		List<string> failed = new List<string>();
		foreach (string retired in RetiredFirewallRules)
		{
			try
			{
				if ((await RunNetshAsync("advfirewall", "firewall", "show", "rule", "name=" + retired)).ExitCode == 0)
				{
					await RunNetshAsync("advfirewall", "firewall", "delete", "rule", "name=" + retired);
					log("Removed the Windows Firewall rule " + retired + ", left over from an earlier version.");
				}
			}
			catch (Exception ex)
			{
				log("The Windows Firewall rule " + retired + " could not be removed: " + ex.Message);
			}
		}
		foreach ((string Name, string Program, string Description) firewallProgram in FirewallPrograms)
		{
			string path = Path.Combine(installRoot, firewallProgram.Program);
			if (!File.Exists(path))
			{
				continue;
			}
			try
			{
				(int ExitCode, string Output) tuple = await RunNetshAsync("advfirewall", "firewall", "show", "rule", "name=" + firewallProgram.Name, "verbose");
				if (tuple.ExitCode == 0 && tuple.Output.Contains(path, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				report("Allowing " + firewallProgram.Description + " through Windows Firewall...");
				if (tuple.ExitCode == 0)
				{
					await RunNetshAsync("advfirewall", "firewall", "delete", "rule", "name=" + firewallProgram.Name);
				}
				(int ExitCode, string Output) tuple2 = await RunNetshAsync("advfirewall", "firewall", "add", "rule", "name=" + firewallProgram.Name, "dir=in", "action=allow", "program=" + path, "enable=yes", "profile=any");
				if (tuple2.ExitCode == 0)
				{
					created.Add(firewallProgram.Description);
					log("Added the Windows Firewall rule " + firewallProgram.Name + " for " + path);
				}
				else
				{
					failed.Add(firewallProgram.Description);
					log("The Windows Firewall rule " + firewallProgram.Name + " could not be added: " + tuple2.Output.Trim());
				}
			}
			catch (Exception ex)
			{
				failed.Add(firewallProgram.Description);
				log("The Windows Firewall rule " + firewallProgram.Name + " could not be added: " + ex.Message);
			}
		}
		if (!announce)
		{
			return;
		}
		if (failed.Count > 0)
		{
			summary.Add("- Windows Firewall could not be set up for " + string.Join(" and ", failed) + ". The first time you press Play, Windows asks whether to allow it through - say yes. That question can open behind the game window, so look for it in the task bar.");
		}
		else if (created.Count > 0)
		{
			summary.Add("- Allowed " + string.Join(", ", created) + " through Windows Firewall, so Windows does not interrupt you when you press Play.");
		}
	}

	private static async Task<(int ExitCode, string Output)> RunNetshAsync(params string[] arguments)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (string item in arguments)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Windows Firewall could not be reached.");
		Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
		Task<string> errorTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw new IOException("Windows Firewall did not answer within 20 seconds.");
		}
		return (process.ExitCode, await outputTask + await errorTask);
	}

	private bool NeedsFirstAccount(ToolResult accounts)
	{
		if (!accounts.Ok || accounts.Root == null)
		{
			log("The account list could not be read during the first run: " + accounts.Message);
			return false;
		}
		return !(accounts.Root["accounts"] is JsonArray existing) || existing.Count == 0;
	}

	private async Task<bool> CreateFirstAccountAsync(ToolResult accounts)
	{
		if (accounts.Root == null)
		{
			return false;
		}
		if (accounts.Root["server_running"]?.GetValue<bool>() == true)
		{
			summary.Add("- No account was created, because the local server is running. Stop the server on the Play page, then use New Account on the Account page.");
			return false;
		}
		report("Creating the account Master...");
		ToolResult toolResult2 = await accountTool(new string[6] { "create", "--name", "Master", "--mode", "normal", "--json" });
		if (!toolResult2.Ok || toolResult2.Root == null)
		{
			log("The first account could not be created: " + toolResult2.Message);
			summary.Add("- No account was created: " + toolResult2.Message + " Use New Account on the Account page.");
			return false;
		}
		int aimeId = (toolResult2.Root["aime_id"]?.GetValue<int>()).GetValueOrDefault();
		string text = toolResult2.Root["access_code"]?.GetValue<string>() ?? "";
		log($"Created the first account Master (ID {aimeId}), access code {text}");
		List<string> list = new List<string>();
		foreach (string action in UpgradeActions)
		{
			report("Filling the account: " + action.Replace('-', ' ') + "...");
			ToolResult toolResult3 = await accountTool(new string[6] { "upgrade", "--aime-id", aimeId.ToString(), "--action", action, "--json" });
			if (!toolResult3.Ok)
			{
				list.Add(action);
				log($"The {action} step of the one-click inventory failed: {toolResult3.Message}");
			}
		}
		report("Selecting the account...");
		ToolResult toolResult4 = await accountTool(new string[4] { "use", "--aime-id", aimeId.ToString(), "--json" });
		if (!toolResult4.Ok)
		{
			log("The first account could not be selected: " + toolResult4.Message);
			summary.Add($"- The account Master (ID {aimeId}) was created but not selected. Pick it on the Account page and click Use Selected Account.");
			return true;
		}
		string text2 = ((list.Count == 0) ? "" : $" The {string.Join(", ", list)} part(s) of the one-click inventory did not finish - you can run them again on the Account page.");
		summary.Add($"- Created the account Master (ID {aimeId}) with the full Servant and Craft Essence roster, and selected it. Its access code is {text}.{text2}");
		return true;
	}

	private bool ApplyMissingDefaults()
	{
		try
		{
			JsonObject jsonObject = ((File.Exists(LauncherSettingsPath) && JsonNode.Parse(File.ReadAllText(LauncherSettingsPath)) is JsonObject settings) ? settings : new JsonObject());
			List<string> list = new List<string>();
			if (jsonObject["displayMode"] == null && jsonObject["windowed"] == null)
			{
				jsonObject["displayMode"] = "windowed";
				jsonObject["windowed"] = true;
				list.Add("windowed");
			}
			if (jsonObject["resolutionWidth"] == null && jsonObject["resolutionHeight"] == null)
			{
				jsonObject["resolutionWidth"] = 1280;
				jsonObject["resolutionHeight"] = 720;
				list.Add("1280x720");
			}
			if (jsonObject["inputMode"] == null)
			{
				jsonObject["inputMode"] = "keyboard";
				list.Add("keyboard controls");
			}
			if (string.IsNullOrWhiteSpace(jsonObject["monitorDevice"]?.GetValue<string>()))
			{
				string primaryMonitorDevice = GetPrimaryMonitorDevice();
				if (primaryMonitorDevice.Length > 0)
				{
					jsonObject["monitorDevice"] = primaryMonitorDevice;
					list.Add("your main monitor");
				}
			}
			if (jsonObject["gpuCompat"] == null && GpuCompat.SourceAvailable && !GpuCompat.HasNvidiaAdapter())
			{
				GpuCompat.Apply(enabled: true);
				jsonObject["gpuCompat"] = true;
				list.Add("the AMD and Intel graphics compatibility layer");
			}
			if (list.Count == 0)
			{
				return false;
			}
			AtomicFile.WriteAllText(LauncherSettingsPath, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
			log("First-run defaults written: " + string.Join(", ", list));
			summary.Add("- Set the settings you had not chosen yet: " + string.Join(", ", list) + ". Change them any time on the Settings page.");
			return true;
		}
		catch (Exception ex)
		{
			log("The first-run defaults could not be written: " + ex.Message);
			return false;
		}
	}

	private static string GetPrimaryMonitorDevice()
	{
		IReadOnlyList<DisplayMonitor.Entry> connected = DisplayMonitor.GetConnected();
		DisplayMonitor.Entry entry = connected.FirstOrDefault((DisplayMonitor.Entry candidate) => candidate.Label.Contains("(primary)", StringComparison.OrdinalIgnoreCase));
		if (entry != null)
		{
			return entry.Device;
		}
		if (connected.Count <= 0)
		{
			return "";
		}
		return connected[0].Device;
	}

	private static string LastLine(string text)
	{
		string[] source = text.Split('\n').Select((string value) => value.Trim()).Where((string value) => value.Length > 0).ToArray();
		if (source.Length == 0)
		{
			return "";
		}
		return source[^1];
	}
}
