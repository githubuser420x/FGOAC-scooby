using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FGOLocalPlatform;

/// <summary>
/// Applies and reverts mods against a game install. The model is the one the PowerShell mod
/// manager proved out, kept deliberately:
/// <list type="bullet">
/// <item>a file is written only after its pristine original is stored in a baseline folder;</item>
/// <item>the journal records what is applied, and the intended state is always recomputed from
/// the baseline plus the applied mods - never read off the current disk - so an outside change is
/// both detectable and undoable;</item>
/// <item>a mod whose folder has gone missing simply stops being replayed, so the part it edited
/// goes back to the original on the next write.</item>
/// </list>
/// </summary>
internal sealed class ModEngine
{
	private readonly string installRoot;

	private readonly string modsRoot;

	private readonly string stateRoot;

	private readonly string baselineRoot;

	private readonly string stateFile;

	private readonly FarcTool farc;

	public ModEngine()
	{
		installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		modsRoot = Path.Combine(installRoot, "Mods");
		stateRoot = Path.Combine(modsRoot, ".state");
		baselineRoot = Path.Combine(stateRoot, "baseline");
		stateFile = Path.Combine(stateRoot, "state.json");
		farc = new FarcTool(installRoot);
	}

	public string ModsRoot
	{
		get
		{
			return modsRoot;
		}
	}

	public string InstallRoot
	{
		get
		{
			return installRoot;
		}
	}

	public bool ModsFolderExists
	{
		get
		{
			return Directory.Exists(modsRoot);
		}
	}

	/// <summary>The four services the local platform binds. A mod must not be applied while one answers.</summary>
	public int? RunningPort()
	{
		int[] ports = new int[4] { 777, 9999, 7777, 8888 };
		foreach (int port in ports)
		{
			using TcpClient client = new TcpClient();
			using CancellationTokenSource timeout = new CancellationTokenSource(150);
			try
			{
				client.ConnectAsync("127.0.0.1", port, timeout.Token).GetAwaiter().GetResult();
				return port;
			}
			catch
			{
			}
		}
		return null;
	}

	public List<ModDefinition> AvailableMods()
	{
		List<ModDefinition> mods = new List<ModDefinition>();
		if (!Directory.Exists(modsRoot))
		{
			return mods;
		}
		foreach (string directory in Directory.EnumerateDirectories(modsRoot).OrderBy((string path) => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
		{
			string manifest = Path.Combine(directory, "mod.json");
			if (!File.Exists(manifest))
			{
				continue;
			}
			try
			{
				mods.Add(ModDefinition.Parse(Path.GetFileName(directory), File.ReadAllText(manifest)));
			}
			catch (JsonException)
			{
			}
			catch (InvalidDataException)
			{
			}
		}
		return mods;
	}

	public List<ModStateRow> Status()
	{
		List<ModDefinition> mods = AvailableMods();
		ModJournal journal = LoadJournal();
		List<ModStateRow> rows = new List<ModStateRow>();
		foreach (ModDefinition mod in mods)
		{
			rows.Add(new ModStateRow
			{
				Definition = mod,
				Applied = journal.Applied.Any((ModAppliedEntry entry) => entry.Id == mod.Id),
				FileCount = TouchFiles(mod).Count
			});
		}
		return rows;
	}

	public IReadOnlyList<string> AppliedIds()
	{
		return LoadJournal().Applied.Select((ModAppliedEntry entry) => entry.Id).ToList();
	}

	/// <summary>
	/// The files a mod changes, without computing any transform. Byte-patch and rule targets alike
	/// name their files, so this is what decides which originals to capture before the first write.
	/// </summary>
	public List<string> TouchFiles(ModDefinition mod)
	{
		List<string> files = new List<string>();
		foreach (ModTarget target in mod.Targets)
		{
			files.AddRange(ResolveTargets(target));
		}
		files.AddRange(mod.Overlay);
		return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string path) => path, StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>The chosen knob values for a mod: its own defaults, with anything stored over them.</summary>
	public Dictionary<string, string> ParametersFor(ModDefinition mod)
	{
		return ModParameterStore.Load(stateRoot, mod);
	}

	public Dictionary<string, string> ParametersFor(string id)
	{
		return ParametersFor(Resolve(AvailableMods(), id));
	}

	/// <summary>
	/// Stores the knob values, brought into range first. The files are not touched here: applying
	/// the mod is what writes them, so the tuner can collect several changes and write once.
	/// </summary>
	public void SaveParameters(string id, IReadOnlyDictionary<string, string> values)
	{
		ModDefinition mod = Resolve(AvailableMods(), id);
		if (mod.Parameters.Count == 0)
		{
			throw new ModEngineException("Mod '" + mod.Id + "' has no settings to save.");
		}
		ModParameterStore.Save(stateRoot, mod, values);
	}

	public bool IsApplied(string id)
	{
		ModDefinition mod = Resolve(AvailableMods(), id);
		return LoadJournal().Applied.Any((ModAppliedEntry entry) => entry.Id == mod.Id);
	}

	public ModPlanSummary Apply(string id)
	{
		AssertGameStopped();
		List<ModDefinition> mods = AvailableMods();
		ModDefinition mod = Resolve(mods, id);
		ModJournal journal = LoadJournal();
		ModPlanSummary summary = new ModPlanSummary
		{
			Mod = mod.Id
		};
		if (journal.Applied.Any((ModAppliedEntry entry) => entry.Id == mod.Id))
		{
			// Drift aware: a platform update or a re-run of the English patch can put a file
			// back, so compare against the intended state and re-apply instead of reporting
			// "nothing to do" over a reverted mod.
			ExpectedResult check = WriteExpectedState(journal, mods, planOnly: true);
			if (check.Pending.Count == 0)
			{
				summary.AlreadyApplied = true;
				summary.Intact = check.Intact.Count;
				return summary;
			}
			ExpectedResult fixedState = WriteExpectedState(journal, mods, planOnly: false);
			summary.AlreadyApplied = true;
			summary.ReApplied = true;
			summary.Written = fixedState.Pending.Count;
			summary.Intact = fixedState.Intact.Count;
			return summary;
		}
		List<string> touch = TouchFiles(mod);
		List<string> captured = new List<string>();
		foreach (string relative in touch)
		{
			string diskPath = Path.Combine(installRoot, relative);
			if (!File.Exists(diskPath))
			{
				continue;
			}
			if (SaveBaselineIfNew(journal, relative, File.ReadAllBytes(diskPath)))
			{
				captured.Add(relative);
			}
		}
		journal.GameRoot = installRoot;
		journal.Applied.Add(new ModAppliedEntry
		{
			Id = mod.Id,
			Folder = mod.Folder,
			Version = mod.Version,
			AppliedUtc = DateTime.UtcNow.ToString("o"),
			Files = touch
		});
		SaveJournal(journal);
		ExpectedResult result = WriteExpectedState(journal, mods, planOnly: false);
		summary.Written = result.Pending.Count;
		summary.Intact = result.Intact.Count;
		summary.BaselinesCaptured = captured.Count;
		return summary;
	}

	public void Remove(string id)
	{
		AssertGameStopped();
		List<ModDefinition> mods = AvailableMods();
		ModDefinition mod = Resolve(mods, id);
		ModJournal journal = LoadJournal();
		int removed = journal.Applied.RemoveAll((ModAppliedEntry entry) => entry.Id == mod.Id);
		if (removed == 0)
		{
			throw new ModEngineException("Mod '" + mod.Id + "' is not applied.");
		}
		WriteExpectedState(journal, mods, planOnly: false);
	}

	public int RestoreAll()
	{
		AssertGameStopped();
		ModJournal journal = LoadJournal();
		if (journal.Baseline.Count == 0)
		{
			return 0;
		}
		journal.Applied.Clear();
		List<ModDefinition> mods = AvailableMods();
		return WriteExpectedState(journal, mods, planOnly: false).Pending.Count;
	}

	public ModVerifyResult Verify()
	{
		ModJournal journal = LoadJournal();
		List<ModDefinition> mods = AvailableMods();
		ModVerifyResult result = new ModVerifyResult();
		if (journal.Baseline.Count == 0)
		{
			return result;
		}
		ExpectedResult check = WriteExpectedState(journal, mods, planOnly: true);
		result.Managed = check.Total;
		result.Intact = check.Intact.Count;
		foreach (PendingFile item in check.Pending)
		{
			string diskPath = Path.Combine(installRoot, item.Path);
			if (!File.Exists(diskPath))
			{
				result.Missing.Add(item.Path);
				continue;
			}
			result.Drifted.Add(item.Path);
			ModBaselineEntry? entry = journal.Baseline.FirstOrDefault((ModBaselineEntry candidate) => string.Equals(candidate.Path, item.Path, StringComparison.OrdinalIgnoreCase));
			if (entry != null && string.Equals(Hash(File.ReadAllBytes(diskPath)), entry.Sha256, StringComparison.Ordinal))
			{
				result.Reverted.Add(item.Path);
			}
		}
		return result;
	}

	public int Repair()
	{
		AssertGameStopped();
		ModJournal journal = LoadJournal();
		List<ModDefinition> mods = AvailableMods();
		ExpectedResult check = WriteExpectedState(journal, mods, planOnly: true);
		if (check.Pending.Count == 0)
		{
			return 0;
		}
		return WriteExpectedState(journal, mods, planOnly: false).Pending.Count;
	}

	public List<ModPlanItem> Plan(string id)
	{
		List<ModDefinition> mods = AvailableMods();
		ModDefinition mod = Resolve(mods, id);
		Dictionary<string, string> parameters = ParametersFor(mod);
		List<ModPlanItem> plan = new List<ModPlanItem>();
		foreach (ModTarget target in mod.Targets)
		{
			foreach (string relative in ResolveTargets(target))
			{
				byte[] before = File.ReadAllBytes(Path.Combine(installRoot, relative));
				byte[] after = Transform(before, target, relative, parameters, out int hits);
				plan.Add(new ModPlanItem
				{
					Relative = relative,
					Action = target.IsBytePatch ? "patch" : (target.IsFarc ? "farc" : "edit"),
					Before = before,
					After = after,
					Changes = hits
				});
			}
		}
		foreach (string entry in mod.Overlay)
		{
			string source = Path.Combine(modsRoot, mod.Folder, "files", entry);
			if (!File.Exists(source))
			{
				throw new ModEngineException("Mod " + mod.Id + " ships an overlay file that is missing: " + entry);
			}
			string destination = Path.Combine(installRoot, entry);
			plan.Add(new ModPlanItem
			{
				Relative = entry,
				Action = "overlay",
				Before = File.Exists(destination) ? File.ReadAllBytes(destination) : Array.Empty<byte>(),
				After = File.ReadAllBytes(source),
				Changes = 1
			});
		}
		return plan;
	}

	/// <summary>
	/// Prunes applied mods whose folder has gone, which is what reverts the part they edited: the
	/// journal no longer replays them, so the next write takes their files back to the original.
	/// Returns the ids that were dropped.
	/// </summary>
	public List<string> PruneMissingMods()
	{
		ModJournal journal = LoadJournal();
		List<ModDefinition> mods = AvailableMods();
		HashSet<string> known = mods.Select((ModDefinition mod) => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<string> missing = journal.Applied.Where((ModAppliedEntry entry) => !known.Contains(entry.Id)).Select((ModAppliedEntry entry) => entry.Id).ToList();
		if (missing.Count == 0)
		{
			return missing;
		}
		journal.Applied.RemoveAll((ModAppliedEntry entry) => !known.Contains(entry.Id));
		SaveJournal(journal);
		// Forgetting the mod is not enough: the files it wrote are still on disk, so the intended
		// state is written back now. A running game is left alone, and the revert then lands on
		// the next apply, remove or repair.
		if (RunningPort() == null)
		{
			WriteExpectedState(journal, mods, planOnly: false);
		}
		return missing;
	}

	private ModDefinition Resolve(List<ModDefinition> mods, string identifier)
	{
		ModDefinition? mod = mods.FirstOrDefault((ModDefinition candidate) => string.Equals(candidate.Id, identifier, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.Folder, identifier, StringComparison.OrdinalIgnoreCase));
		if (mod == null)
		{
			throw new ModEngineException("No such mod: " + identifier + ".");
		}
		return mod;
	}

	private void AssertGameStopped()
	{
		int? port = RunningPort();
		if (port.HasValue)
		{
			throw new ModEngineException("Port " + port.Value + " is accepting connections, so the game or the local server is running. Close both, then try again.");
		}
	}

	// ---- The intended state -------------------------------------------------

	private ExpectedResult WriteExpectedState(ModJournal journal, List<ModDefinition> mods, bool planOnly)
	{
		List<ModExpectedFile> expected = ExpectedState(journal, mods);
		ExpectedResult result = new ExpectedResult();
		result.Total = expected.Count;
		foreach (ModExpectedFile file in expected)
		{
			string diskPath = Path.Combine(installRoot, file.Path);
			if (File.Exists(diskPath) && string.Equals(Hash(File.ReadAllBytes(diskPath)), file.Sha256, StringComparison.Ordinal))
			{
				result.Intact.Add(file.Path);
				continue;
			}
			result.Pending.Add(new PendingFile
			{
				Path = file.Path,
				Owners = file.Owners
			});
			if (!planOnly)
			{
				WriteBytesAtomic(AssertInsideRoot(file.Path), file.Bytes);
			}
		}
		if (!planOnly)
		{
			SaveJournal(journal);
		}
		return result;
	}

	private List<ModExpectedFile> ExpectedState(ModJournal journal, List<ModDefinition> mods)
	{
		List<ModExpectedFile> map = new List<ModExpectedFile>();
		Dictionary<string, ModExpectedFile> index = new Dictionary<string, ModExpectedFile>(StringComparer.OrdinalIgnoreCase);
		foreach (ModBaselineEntry entry in journal.Baseline)
		{
			string storePath = Path.Combine(baselineRoot, entry.Path);
			if (!File.Exists(storePath))
			{
				throw new ModEngineException("The baseline for " + entry.Path + " is missing, so the intended state cannot be computed.");
			}
			ModExpectedFile file = new ModExpectedFile
			{
				Path = entry.Path,
				Bytes = File.ReadAllBytes(storePath)
			};
			map.Add(file);
			index[entry.Path] = file;
		}
		List<string> managedPaths = map.Select((ModExpectedFile file) => file.Path).ToList();

		foreach (ModAppliedEntry record in journal.Applied)
		{
			ModDefinition? mod = mods.FirstOrDefault((ModDefinition candidate) => candidate.Id == record.Id);
			if (mod == null)
			{
				// The mod folder is gone. Dropping it here is exactly what reverts the edit.
				continue;
			}
			// Resolved once per mod: the chosen knobs decide which rules run and what they write,
			// so a tuner change is reflected the next time the intended state is computed.
			Dictionary<string, string> parameters = ParametersFor(mod);
			foreach (ModTarget target in mod.Targets)
			{
				foreach (string relative in MatchManaged(target.Files, managedPaths))
				{
					ModExpectedFile file = index[relative];
					file.Bytes = Transform(file.Bytes, target, relative, parameters, out int _);
					file.Owners.Add(record.Id);
				}
			}
			foreach (string entry in mod.Overlay)
			{
				if (!index.TryGetValue(entry, out ModExpectedFile file))
				{
					throw new ModEngineException("Mod " + record.Id + " overlays " + entry + ", which was not present when the mod was installed.");
				}
				string source = Path.Combine(modsRoot, mod.Folder, "files", entry);
				if (!File.Exists(source))
				{
					throw new ModEngineException("Mod " + record.Id + " ships an overlay file that is missing: " + entry);
				}
				file.Bytes = File.ReadAllBytes(source);
				file.Owners.Add(record.Id);
			}
		}

		foreach (ModExpectedFile file in map)
		{
			file.Sha256 = Hash(file.Bytes);
		}
		return map;
	}

	private static List<string> MatchManaged(List<string> patterns, List<string> managedPaths)
	{
		List<string> matched = new List<string>();
		foreach (string pattern in patterns)
		{
			if (string.IsNullOrEmpty(pattern))
			{
				continue;
			}
			System.Text.RegularExpressions.Regex regex = ModRuleEngine.GlobToRegex(pattern.Replace('/', '\\'));
			foreach (string path in managedPaths)
			{
				if (regex.IsMatch(path))
				{
					matched.Add(path);
				}
			}
		}
		return matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	// ---- Targets ------------------------------------------------------------

	private List<string> ResolveTargets(ModTarget target)
	{
		List<string> found = new List<string>();
		foreach (string pattern in target.Files)
		{
			string normalised = pattern.Replace('/', '\\');
			string search = Path.Combine(installRoot, normalised);
			string? directory = Path.GetDirectoryName(search);
			string filePattern = Path.GetFileName(search);
			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				continue;
			}
			foreach (string file in Directory.EnumerateFiles(directory, filePattern))
			{
				found.Add(Path.GetRelativePath(installRoot, file));
			}
		}
		return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private byte[] Transform(byte[] before, ModTarget target, string label, IReadOnlyDictionary<string, string> parameters, out int hits)
	{
		if (target.IsBytePatch)
		{
			return ApplyPatches(before, target, out hits);
		}
		string text = EditableText(before, target, label);
		string rewritten = ModRuleEngine.Apply(text, target.Rules, parameters, out hits);
		return RestoredBytes(before, rewritten, target, label);
	}

	private static byte[] ApplyPatches(byte[] before, ModTarget target, out int hits)
	{
		hits = 0;
		byte[] after = before;
		foreach (ModBytePatch patch in target.Patches)
		{
			if (patch.ExpectSize.HasValue && before.LongLength != patch.ExpectSize.Value)
			{
				continue;
			}
			if (patch.Offset < 0 || patch.Offset + patch.Bytes.Length > before.LongLength)
			{
				continue;
			}
			bool changed = false;
			for (int i = 0; i < patch.Bytes.Length; i++)
			{
				if (after[patch.Offset + i] != patch.Bytes[i])
				{
					changed = true;
					break;
				}
			}
			if (!changed)
			{
				continue;
			}
			if (ReferenceEquals(after, before))
			{
				after = (byte[])before.Clone();
			}
			Array.Copy(patch.Bytes, 0L, after, patch.Offset, patch.Bytes.LongLength);
			hits++;
		}
		return after;
	}

	private string EditableText(byte[] bytes, ModTarget target, string label)
	{
		if (!target.IsFarc)
		{
			return Encoding.UTF8.GetString(bytes);
		}
		byte[] member = farc.ExtractMember(bytes, target.FarcMember!, label);
		return Encoding.UTF8.GetString(member);
	}

	private byte[] RestoredBytes(byte[] bytes, string text, ModTarget target, string label)
	{
		if (!target.IsFarc)
		{
			if (string.Equals(text, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal))
			{
				return bytes;
			}
			return Encoding.UTF8.GetBytes(text);
		}
		if (string.Equals(text, EditableText(bytes, target, label), StringComparison.Ordinal))
		{
			return bytes;
		}
		return farc.RepackMember(bytes, target.FarcMember!, Encoding.UTF8.GetBytes(text), label);
	}

	// ---- Baseline and journal ----------------------------------------------

	private bool SaveBaselineIfNew(ModJournal journal, string relative, byte[] original)
	{
		if (journal.Baseline.Any((ModBaselineEntry entry) => string.Equals(entry.Path, relative, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		string storePath = Path.Combine(baselineRoot, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
		File.WriteAllBytes(storePath, original);
		journal.Baseline.Add(new ModBaselineEntry
		{
			Path = relative,
			Sha256 = Hash(original),
			Size = original.LongLength,
			CapturedUtc = DateTime.UtcNow.ToString("o")
		});
		return true;
	}

	private ModJournal LoadJournal()
	{
		if (!File.Exists(stateFile))
		{
			return new ModJournal();
		}
		try
		{
			ModJournal? journal = JsonSerializer.Deserialize<ModJournal>(File.ReadAllText(stateFile));
			if (journal == null)
			{
				return new ModJournal();
			}
			journal.Baseline ??= new List<ModBaselineEntry>();
			journal.Applied ??= new List<ModAppliedEntry>();
			return journal;
		}
		catch (JsonException)
		{
			return new ModJournal();
		}
	}

	private void SaveJournal(ModJournal journal)
	{
		Directory.CreateDirectory(stateRoot);
		AtomicFile.WriteAllText(stateFile, JsonSerializer.Serialize(journal, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private string AssertInsideRoot(string relative)
	{
		string full = Path.GetFullPath(Path.Combine(installRoot, relative));
		string prefix = installRoot.TrimEnd('\\') + "\\";
		if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new ModEngineException("Refusing to touch a path outside the game folder: " + full);
		}
		string statePrefix = stateRoot.TrimEnd('\\') + "\\";
		if (full.StartsWith(statePrefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new ModEngineException("Refusing to touch a path inside the manager's own store: " + relative);
		}
		return full;
	}

	private static void WriteBytesAtomic(string path, byte[] bytes)
	{
		AtomicFile.WriteAllBytes(path, bytes);
	}

	private static string Hash(byte[] bytes)
	{
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
	}
}

internal sealed class ExpectedResult
{
	public List<string> Intact = new List<string>();

	public List<PendingFile> Pending = new List<PendingFile>();

	public int Total;
}

internal sealed class PendingFile
{
	public string Path = "";

	public List<string> Owners = new List<string>();
}

internal sealed class ModPlanSummary
{
	public string Mod = "";

	public bool AlreadyApplied;

	public bool ReApplied;

	public int Written;

	public int Intact;

	public int BaselinesCaptured;
}