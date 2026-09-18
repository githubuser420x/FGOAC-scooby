using System.Collections.Generic;

namespace FGOLocalPlatform;

/// <summary>
/// The manager's own record: which files have a pristine original stored, and which mods are
/// applied, in the order they were applied. It lives beside the baseline store and is never
/// derived from what is on disk, which is what lets the manager both spot an outside change and
/// undo it.
/// </summary>
internal sealed class ModJournal
{
	public string ManagerVersion { get; set; } = "1.0.0";

	public string? GameRoot { get; set; }

	public List<ModBaselineEntry> Baseline { get; set; } = new List<ModBaselineEntry>();

	public List<ModAppliedEntry> Applied { get; set; } = new List<ModAppliedEntry>();
}

internal sealed class ModBaselineEntry
{
	public string Path { get; set; } = "";

	public string Sha256 { get; set; } = "";

	public long Size { get; set; }

	public string CapturedUtc { get; set; } = "";
}

internal sealed class ModAppliedEntry
{
	public string Id { get; set; } = "";

	public string Folder { get; set; } = "";

	public string Version { get; set; } = "";

	public string AppliedUtc { get; set; } = "";

	public List<string> Files { get; set; } = new List<string>();
}

/// <summary>One file the intended state says should exist, with the mods that write it.</summary>
internal sealed class ModExpectedFile
{
	public string Path = "";

	public byte[] Bytes = System.Array.Empty<byte>();

	public List<string> Owners = new List<string>();

	public string Sha256 = "";
}

internal sealed class ModPlanItem
{
	public string Relative = "";

	public string Action = "";

	public long Changes;

	public byte[] Before = System.Array.Empty<byte>();

	public byte[] After = System.Array.Empty<byte>();
}

internal sealed class ModVerifyResult
{
	public int Managed;

	public int Intact;

	public List<string> Drifted = new List<string>();

	public List<string> Missing = new List<string>();

	public List<string> Reverted = new List<string>();
}

internal sealed class ModStateRow
{
	public ModDefinition Definition = null!;

	public bool Applied;

	public long FileCount;

	public string Availability = "available";
}