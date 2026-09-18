using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class BannerSettingsView : UserControl
{
	private const string PatchMarker = "fgo_event_toggles_v1";

	private static readonly string[] BannerIds =
	{
		"LTE8008", "LTE8010", "LTE8011", "LTE8012", "LTE8013", "LTE8014",
		"LTE8015", "LTE8016", "LTE8017", "LTE8018", "LTE8019", "LTE8020",
		"LTE8021", "LTE8022", "LTE8023", "LTE8025", "LTE8027", "LTE8028",
		"LTE8029", "LTE8030", "LTE8031", "LTE8032", "LTE8033", "LTE8034",
		"LTE8035", "LTE8036", "LTE8038", "LTE8039", "LTE8040", "LTE8041",
		"LTE8042", "LTE8043", "LTE8044", "LTE8045", "LTE8046", "LTE8047",
		"LTE8048", "LTE8049"
	};

	private readonly Dictionary<string, CheckBox> bannerOptions = new();

	private string ServerRoot => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"));

	private string ServerYamlPath => Path.GetFullPath(Path.Combine(ServerRoot, "artemis", "config", "fgo.yaml"));

	public BannerSettingsView()
	{
		InitializeComponent();
		foreach (string id in BannerIds)
		{
			if (FindName(id) is CheckBox checkBox)
			{
				bannerOptions[id] = checkBox;
			}
		}
	}

	private void Option_OnChanged(object sender, RoutedEventArgs e)
	{
		StatusText.Text = "Banner settings updated. Make sure you press Save.";
	}

	public void Save()
	{
		try
		{
			WriteEnabledSingularityIds();
			StatusText.Text = "Banner settings saved. Restart the local server for the changes to take effect.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save the banner settings: " + ex.Message;
		}
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void DisableAll_OnClick(object sender, RoutedEventArgs e)
	{
		foreach (CheckBox checkBox in bannerOptions.Values)
		{
			if (checkBox != null)
			{
				checkBox.IsChecked = false;
			}
		}
		StatusText.Text = "Banner filter turned off. Restart the game for the changes to take effect.";
	}

	private void ApplyEventPatch_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			PatchStatusText.Text = ApplyEventTogglePatch();
		}
		catch (Exception ex)
		{
			PatchStatusText.Text = ex.Message;
		}
	}

	private string ApplyEventTogglePatch()
	{
		using Stream? patchStream = typeof(BannerSettingsView).Assembly.GetManifestResourceStream("FGOLocalPlatform.EventTogglePatch.json");
		if (patchStream == null)
		{
			return "The embedded event toggle patch could not be found.";
		}
		using StreamReader patchReader = new StreamReader(patchStream);
		string patchText = patchReader.ReadToEnd();
	using JsonDocument document = JsonDocument.Parse(patchText);

	if (!document.RootElement.TryGetProperty("edits", out JsonElement editsElement)
	    || editsElement.ValueKind != JsonValueKind.Array)
	{
	    throw new InvalidOperationException(
	        "The event toggle patch file is missing the edits array.");
	}
		List<string> messages = new List<string>();
		foreach (JsonElement editElement in editsElement.EnumerateArray())
		{
			string fileName = editElement.TryGetProperty("file", out JsonElement fileElement) ? fileElement.GetString() : null;
			string anchorOld = editElement.TryGetProperty("anchor_old", out JsonElement oldElement) ? oldElement.GetString() : null;
			string anchorNew = editElement.TryGetProperty("anchor_new", out JsonElement newElement) ? newElement.GetString() : null;
			if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(anchorOld) || string.IsNullOrEmpty(anchorNew))
			{
				throw new InvalidOperationException("One of the patch entries is missing. Check the file for, anchor_old, or anchor_new.");
			}

			string targetPath = ResolvePatchTarget(fileName);
			if (!File.Exists(targetPath))
			{
				throw new InvalidOperationException(targetPath + " could not be found by this patch. The server files may have been updated since this launcher was built.");
			}

			string currentText = File.ReadAllText(targetPath);
			string fileLineEnding = currentText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
			string normalizedAnchorNew = anchorNew
				.Replace("\r\n", "\n", StringComparison.Ordinal)
				.Replace("\n", fileLineEnding, StringComparison.Ordinal);
			bool configPropertyAlreadyApplied = string.Equals(fileName, "config.py", StringComparison.OrdinalIgnoreCase)
				&& currentText.Contains("def enabled_singularity_ids", StringComparison.Ordinal);
			if (ContainsLineEndingInsensitive(currentText, anchorNew)
				|| configPropertyAlreadyApplied
				|| currentText.Contains(PatchMarker, StringComparison.OrdinalIgnoreCase))
			{
				messages.Add(Path.GetFileName(targetPath) + " already has the patch applied.");
				continue;
			}

			MatchCollection oldMatches = FindLineEndingInsensitiveMatches(currentText, anchorOld);
			if (oldMatches.Count != 1)
			{
				throw new InvalidOperationException(targetPath + " does not match what this patch expects. The server files may have been updated since this launcher was built.");
			}

			string backupPath = targetPath + ".bak";
			if (!File.Exists(backupPath))
			{
				File.Copy(targetPath, backupPath);
			}

			Match oldMatch = oldMatches[0];
			string updatedText = currentText.Substring(0, oldMatch.Index)
				+ normalizedAnchorNew
				+ currentText.Substring(oldMatch.Index + oldMatch.Length);
			AtomicFile.WriteAllText(targetPath, updatedText);
			ValidatePythonFile(targetPath, backupPath);
			messages.Add("Applied to " + Path.GetFileName(targetPath));
		}

		return string.Join(Environment.NewLine, messages.Count > 0 ? messages : new[] { "No event-toggle changes were needed." });
	}

	private static bool ContainsLineEndingInsensitive(string text, string value)
	{
		return FindLineEndingInsensitiveMatches(text, value).Count > 0;
	}

	private static MatchCollection FindLineEndingInsensitiveMatches(string text, string value)
	{
		string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		string pattern = string.Join("\\r?\\n", lines.Select(Regex.Escape));
		return Regex.Matches(text, pattern, RegexOptions.CultureInvariant);
	}

	private void WriteEnabledSingularityIds()
	{
		string path = ServerYamlPath;
		if (!File.Exists(path))
		{
			throw new IOException("The server config file was not found: " + path);
		}

		List<int> selectedIds = bannerOptions
			.Where(pair => pair.Value != null && pair.Value.IsChecked == true && pair.Key.StartsWith("LTE", StringComparison.Ordinal))
			.Select(pair => int.Parse(pair.Key.Substring(3), System.Globalization.CultureInfo.InvariantCulture))
			.OrderBy(id => id)
			.ToList();

		string valueText = selectedIds.Count == 0 ? "null" : "[" + string.Join(", ", selectedIds) + "]";
		string[] lines = File.ReadAllLines(path);
		bool found = false;
		int serverStart = -1;
		int serverEnd = lines.Length;
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed == "server:")
			{
				serverStart = i;
				break;
			}
		}
		if (serverStart < 0)
		{
			throw new InvalidOperationException("The server: block could not be found in fgo.yaml. Has there been other changes made?");
		}
		for (int i = serverStart + 1; i < lines.Length; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed.Length == 0 && !trimmed.StartsWith("#") && !lines[i].StartsWith(" ") && !lines[i].StartsWith("\t"))
			{
				serverEnd = i;
				break;
			}
		}
		for (int i = serverStart + 1; i < serverEnd; i++)
		{
			if (lines[i].TrimStart().StartsWith("enabled_singularity_ids:", StringComparison.Ordinal))
			{
				lines[i] = "  enabled_singularity_ids: " + valueText;
				found = true;
				break;
			}
		}
		if (!found)
		{
			List<string> expanded = new List<string>(lines.Length + 2);
			expanded.AddRange(lines.Take(serverEnd));
			expanded.Add("  enabled_singularity_ids: " + valueText);
			expanded.AddRange(lines.Skip(serverEnd));
			lines = expanded.ToArray();
		}
		AtomicFile.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
	}

	private string ResolvePatchTarget(string fileName)
	{
		return Path.GetFullPath(Path.Combine(ServerRoot, "artemis", "titles", "fgo", fileName));
	}

	private static void ValidatePythonFile(string path, string backupPath)
	{
		string python = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "python", "python.exe"));
		if (!File.Exists(python))
		{
			return;
		}

		ProcessStartInfo startInfo = new ProcessStartInfo(python)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
		};
		startInfo.ArgumentList.Add("-m");
		startInfo.ArgumentList.Add("py_compile");
		startInfo.ArgumentList.Add(path);

		using Process process = Process.Start(startInfo) ?? throw new IOException("Could not run Python validation for the patched file.");
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			if (File.Exists(backupPath))
			{
				File.Copy(backupPath, path, overwrite: true);
			}
			throw new InvalidOperationException("The patched Python file failed validation and has been restored from backup. Error: " + error.Trim());
		}
	}
}
