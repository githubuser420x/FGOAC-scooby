using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

/// <summary>
/// Checks GitHub Releases for a newer build and installs it the way a player would by hand: the
/// release zip is unpacked into the game folder and the Apply-EN-Patch.ps1 that came with it is
/// run. Every failure is silent on screen during the startup check and written to
/// logs\update.log; the manual check on the About page reports what happened in one sentence.
/// </summary>
internal static class Updater
{
	internal sealed record Release(string Version, string Tag, string ZipUrl, string ZipName, string HashUrl);

	private const string ZipPrefix = "FGOAC-scooby-v";

	private static readonly HttpClient Client = CreateClient();

	private static HttpClient CreateClient()
	{
		// No client-wide timeout: it would also cut off the download of a release zip that takes
		// minutes. The small API calls carry their own ten-second token.
		HttpClient client = new HttpClient
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
		// GitHub rejects an API request that does not name the program making it.
		client.DefaultRequestHeaders.Add("User-Agent", "FGOAC-scooby/" + UpdateSettings.Version);
		client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
		return client;
	}

	/// <summary>
	/// The newest release when it is newer than the running build, otherwise null. Throws only for
	/// the manual check to report; the startup check swallows it.
	/// </summary>
	public static async Task<Release> CheckAsync()
	{
		Log("Checking " + UpdateSettings.LatestReleaseApiUrl);
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
		string json = await Client.GetStringAsync(UpdateSettings.LatestReleaseApiUrl, timeout.Token);
		JsonNode node = JsonNode.Parse(json);
		string tag = (string)node?["tag_name"] ?? "";
		Version latest = ParseVersion(tag);
		if (latest == null)
		{
			Log("The latest release is tagged " + tag + ", which is not a version number.");
			return null;
		}
		Version running = ParseVersion(UpdateSettings.Version);
		if (latest <= running)
		{
			Log($"The latest release is {tag} and this build is {UpdateSettings.Version}, so there is nothing to install.");
			return null;
		}
		JsonArray assets = node["assets"] as JsonArray;
		string zipName = null;
		string zipUrl = null;
		string hashUrl = null;
		foreach (JsonNode asset in assets ?? new JsonArray())
		{
			string name = (string)asset?["name"] ?? "";
			string url = (string)asset?["browser_download_url"] ?? "";
			if (name.StartsWith(ZipPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			{
				zipName = name;
				zipUrl = url;
			}
			else if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase))
			{
				hashUrl = url;
			}
		}
		if (zipUrl == null || hashUrl == null)
		{
			Log("Release " + tag + " has no " + ZipPrefix + "*.zip and .zip.sha256 pair, so it cannot be installed from here.");
			return null;
		}
		Log("Release " + tag + " is newer than " + UpdateSettings.Version + ": " + zipName);
		return new Release(FormatVersion(latest), tag, zipUrl, zipName, hashUrl);
	}

	/// <summary>
	/// Downloads the release, checks it against its published SHA-256, unzips it into the game
	/// folder and runs the Apply-EN-Patch.ps1 it brought. Returns true when the patch finished;
	/// <paramref name="report" /> gets one sentence per step.
	/// </summary>
	public static async Task<bool> InstallAsync(Release release, string installRoot, Action<string> report, CancellationToken cancellation)
	{
		string staging = Path.Combine(Path.GetTempPath(), "FGOAC-scooby-update");
		try
		{
			if (Directory.Exists(staging))
			{
				Directory.Delete(staging, recursive: true);
			}
			Directory.CreateDirectory(staging);

			report("Downloading " + release.ZipName + "...");
			Log("Downloading " + release.ZipUrl);
			string archive = Path.Combine(staging, release.ZipName);
			using (HttpResponseMessage response = await Client.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellation))
			{
				response.EnsureSuccessStatusCode();
				using FileStream file = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None);
				await response.Content.CopyToAsync(file, cancellation);
			}

			report("Checking the download...");
			string published = ReadHash(await Client.GetStringAsync(release.HashUrl, cancellation));
			string actual = HashFile(archive);
			if (published == null || !string.Equals(published, actual, StringComparison.OrdinalIgnoreCase))
			{
				Log($"Checksum mismatch: published {published}, downloaded {actual}");
				report("The download did not match its published checksum, so nothing was installed. Try again, or download the release from GitHub yourself.");
				return false;
			}

			report("Unpacking the update into the game folder...");
			ExtractIntoGameFolder(archive, installRoot);
			string script = Path.Combine(installRoot, "Apply-EN-Patch.ps1");
			if (!File.Exists(script))
			{
				Log("No Apply-EN-Patch.ps1 in " + release.ZipName);
				report("The update package has no installer in it, so nothing was installed. Download the release from GitHub and unzip it into the game folder yourself.");
				return false;
			}

			report("Installing " + release.Version + "...");
			int exitCode = await RunPatchAsync(script, installRoot, installRoot, report, cancellation);
			if (exitCode != 0)
			{
				Log("Apply-EN-Patch.ps1 exited with " + exitCode);
				report("The update did not install (the installer stopped with code " + exitCode + "). The log panel and logs\\update.log say what happened.");
				return false;
			}
			Log("Version " + release.Version + " installed.");
			return true;
		}
		catch (Exception ex)
		{
			Log("Install failed: " + ex);
			report("The update could not be installed: " + ex.Message + ". Try again, or download the release from GitHub yourself.");
			return false;
		}
	}

	/// <summary>
	/// Unzips the release into the game folder, the layout a player gets by unzipping it by hand,
	/// so the folder keeps the installer, the manifest and the payload of the version it is on
	/// and the next start finds nothing to redo. The running launcher cannot be overwritten, so
	/// its new build lands beside it as .new and is swapped in when the launcher closes.
	/// manifest.json goes last: until it is there the folder still describes the previous
	/// package, so an extraction that stops half way is not taken for a complete one.
	/// </summary>
	private static void ExtractIntoGameFolder(string archive, string installRoot)
	{
		string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		using ZipArchive zip = ZipFile.OpenRead(archive);
		foreach (ZipArchiveEntry entry in zip.Entries.OrderBy((ZipArchiveEntry e) => string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase)))
		{
			if (entry.Name.Length == 0)
			{
				continue;
			}
			string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
			if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("The update lists a path outside the game folder: " + entry.FullName);
			}
			if (string.Equals(entry.FullName, "FGOAC scooby.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
			{
				target += ".new";
			}
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			entry.ExtractToFile(target, overwrite: true);
		}
	}

	private static async Task<int> RunPatchAsync(string script, string packageRoot, string installRoot, Action<string> report, CancellationToken cancellation)
	{
		ProcessStartInfo start = PowerShellHost.CreateStartInfo(installRoot, redirectOutput: true, new string[]
		{
			"-File", script, "-InstallRoot", installRoot, "-PackageRoot", packageRoot, "-NonInteractive",
			"-IgnoreProcessId", Environment.ProcessId.ToString()
		});

		using Process process = Process.Start(start) ?? throw new IOException("PowerShell could not be started.");
		process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				Log("  " + e.Data);
				report(e.Data.Trim());
			}
		};
		process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				Log("  ! " + e.Data);
			}
		};
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		await process.WaitForExitAsync(cancellation);
		return process.ExitCode;
	}

	/// <summary>
	/// True when the installer left the new launcher beside the running one because a program
	/// cannot overwrite its own file.
	/// </summary>
	public static bool HasStagedLauncher(out string stagedPath)
	{
		stagedPath = Environment.ProcessPath + ".new";
		return File.Exists(stagedPath);
	}

	/// <summary>
	/// Writes and starts the one-line script that waits for this process to close, puts the staged
	/// launcher in its place and starts it. The caller closes the window straight afterwards.
	/// </summary>
	public static void SwapAndRestart(string stagedPath)
	{
		string launcher = Environment.ProcessPath;
		string swap = Path.Combine(Path.GetTempPath(), "update-swap.cmd");
		// The move is retried for a while: the virus scanner can still be reading the staged file, or
		// the closing process may not have released its own file yet.
		string[] lines = new string[13]
		{
			"@echo off",
			"setlocal",
			"set tries=0",
			":wait",
			"tasklist /fi \"PID eq " + Environment.ProcessId + "\" | find \"" + Environment.ProcessId + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)",
			":move",
			"move /y \"" + stagedPath + "\" \"" + launcher + "\" >nul 2>&1 && goto run",
			"set /a tries+=1",
			"if %tries% lss 60 (timeout /t 1 /nobreak >nul & goto move)",
			":run",
			"start \"\" \"" + launcher + "\"",
			"del \"%~f0\"",
			""
		};
		File.WriteAllLines(swap, lines, Encoding.ASCII);
		Log("Swapping in " + stagedPath + " through " + swap);
		Process.Start(new ProcessStartInfo(swap)
		{
			UseShellExecute = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		});
	}

	public static void Log(string message)
	{
		try
		{
			Directory.CreateDirectory(GamePaths.LogsRoot);
			File.AppendAllText(Path.Combine(GamePaths.LogsRoot, "update.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
		}
		catch (Exception)
		{
		}
	}

	private static string ReadHash(string text)
	{
		foreach (string token in (text ?? "").Split(new char[4] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string candidate = token.TrimStart('*');
			if (candidate.Length == 64 && candidate.All((char c) => Uri.IsHexDigit(c)))
			{
				return candidate;
			}
		}
		return null;
	}

	internal static string HashFile(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
	}

	private static Version ParseVersion(string text)
	{
		string trimmed = (text ?? "").Trim();
		if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed.Substring(1);
		}
		List<int> parts = new List<int>();
		foreach (string part in trimmed.Split('.'))
		{
			if (!int.TryParse(part, out var value) || value < 0)
			{
				break;
			}
			parts.Add(value);
		}
		if (parts.Count == 0)
		{
			return null;
		}
		while (parts.Count < 3)
		{
			parts.Add(0);
		}
		return new Version(parts[0], parts[1], parts[2]);
	}

	private static string FormatVersion(Version version)
	{
		return $"{version.Major}.{version.Minor}.{version.Build}";
	}
}
