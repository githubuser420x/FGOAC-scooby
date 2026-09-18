using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// One mod as its <c>mod.json</c> describes it. The schema is the one the PowerShell mod
/// manager uses, so a mod written for either tool works in both: a mod is a list of
/// <c>targets</c> (file globs plus the rules that rewrite them) and an optional <c>overlay</c>
/// (whole files the mod ships under its own <c>files\</c> folder).
/// <para>
/// Two extensions this launcher adds are optional, and a mod that uses neither stays portable:
/// <c>parameters</c> declares the knobs the Battle Tuner shows, and a rule may name one as
/// <c>{id}</c> instead of a literal, or carry a <c>when</c> map so it only runs for a chosen mode.
/// </para>
/// </summary>
internal sealed class ModDefinition
{
	public string Id = "";

	public string Name = "";

	public string Version = "";

	public string Description = "";

	/// <summary>The folder name under the mods root, kept for the journal and for a rename-safe lookup.</summary>
	public string Folder = "";

	public List<ModTarget> Targets = new List<ModTarget>();

	public List<string> Overlay = new List<string>();

	public List<ModParameter> Parameters = new List<ModParameter>();

	public static ModDefinition Parse(string folder, string json)
	{
		JsonNode root = JsonNode.Parse(json) ?? throw new InvalidDataException("the mod manifest is empty");
		ModDefinition definition = new ModDefinition
		{
			Folder = folder,
			Id = root["id"]?.GetValue<string>() ?? folder,
			Name = root["name"]?.GetValue<string>() ?? folder,
			Version = root["version"]?.GetValue<string>() ?? "",
			Description = root["description"]?.GetValue<string>() ?? ""
		};
		if (root["parameters"] is JsonArray parameters)
		{
			foreach (JsonNode parameter in parameters)
			{
				definition.Parameters.Add(ModParameter.Parse(parameter));
			}
		}
		if (root["targets"] is JsonArray targets)
		{
			foreach (JsonNode target in targets)
			{
				definition.Targets.Add(ModTarget.Parse(target));
			}
		}
		if (root["overlay"] is JsonArray overlay)
		{
			foreach (JsonNode entry in overlay)
			{
				string? relative = entry?.GetValue<string>();
				if (!string.IsNullOrEmpty(relative))
				{
					definition.Overlay.Add(relative.Replace('/', '\\'));
				}
			}
		}
		return definition;
	}
}

/// <summary>One entry of a mod's <c>targets</c>: the files to read and the rules that rewrite them.</summary>
internal sealed class ModTarget
{
	public List<string> Files = new List<string>();

	/// <summary>When set, the rules edit this member inside a packed .farc archive rather than the file itself.</summary>
	public string? FarcMember;

	public List<ModRule> Rules = new List<ModRule>();

	/// <summary>
	/// Raw byte patches. A whole-file overlay can only ship bytes the mod carries, and a game
	/// file may not be carried by this project; a patch names the offset and the bytes instead,
	/// so a mod that changes a couple of bytes in a packed archive stays a small text file.
	/// </summary>
	public List<ModBytePatch> Patches = new List<ModBytePatch>();

	public bool IsFarc
	{
		get
		{
			return !string.IsNullOrEmpty(FarcMember);
		}
	}

	public bool IsBytePatch
	{
		get
		{
			return Patches.Count > 0;
		}
	}

	public static ModTarget Parse(JsonNode node)
	{
		ModTarget target = new ModTarget();
		if (node["files"] is JsonArray files)
		{
			foreach (JsonNode file in files)
			{
				string? pattern = file?.GetValue<string>();
				if (!string.IsNullOrEmpty(pattern))
				{
					target.Files.Add(pattern);
				}
			}
		}
		target.FarcMember = node["farc"]?["member"]?.GetValue<string>();
		if (node["rules"] is JsonArray rules)
		{
			foreach (JsonNode rule in rules)
			{
				target.Rules.Add(ModRule.Parse(rule));
			}
		}
		if (node["patches"] is JsonArray patches)
		{
			foreach (JsonNode patch in patches)
			{
				target.Patches.Add(ModBytePatch.Parse(patch));
			}
		}
		return target;
	}
}

/// <summary>
/// One rewrite rule. The pattern must capture <c>value</c>, and may capture <c>head</c> and
/// <c>tail</c>; the line is rebuilt as head + new value + tail so every byte the rule did not
/// touch survives. The numeric fields are kept as text because they may be a <c>{parameter}</c>
/// placeholder, which is resolved against the mod's chosen values when the rule runs.
/// </summary>
internal sealed class ModRule
{
	public string Id = "";

	public string Pattern = "";

	public string Operation = "";

	public string? Value;

	public string? Factor;

	public string? Amount;

	public string? Min;

	public string? Max;

	/// <summary>Parameter id to expected value. Every pair must match for the rule to run at all.</summary>
	public Dictionary<string, string>? When;

	public static ModRule Parse(JsonNode node)
	{
		ModRule rule = new ModRule
		{
			Id = node["id"]?.GetValue<string>() ?? "",
			Pattern = node["pattern"]?.GetValue<string>() ?? "",
			Operation = node["operation"]?.GetValue<string>() ?? "",
			Value = ReadText(node["value"]),
			Factor = ReadText(node["factor"]),
			Amount = ReadText(node["amount"]),
			Min = ReadText(node["min"]),
			Max = ReadText(node["max"])
		};
		if (node["when"] is JsonObject when)
		{
			rule.When = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, JsonNode?> pair in when)
			{
				rule.When[pair.Key] = ReadText(pair.Value) ?? "";
			}
		}
		return rule;
	}

	/// <summary>
	/// A textual rule's value is the literal replacement, so it must be read as the string it is;
	/// ToJsonString would hand back a quoted literal. Numbers keep the token text so a placeholder
	/// can still be recognised later.
	/// </summary>
	private static string? ReadText(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			if (value.TryGetValue<string>(out string? text))
			{
				return text;
			}
			if (value.TryGetValue<bool>(out bool flag))
			{
				return flag ? "true" : "false";
			}
			return value.ToJsonString();
		}
		return null;
	}
}

internal sealed class ModBytePatch
{
	public long Offset;

	public byte[] Bytes = Array.Empty<byte>();

	/// <summary>When set, the patch only applies to a file of exactly this length, so a file that
	/// is not the build the offsets were measured against is left alone.</summary>
	public long? ExpectSize;

	public static ModBytePatch Parse(JsonNode node)
	{
		ModBytePatch patch = new ModBytePatch
		{
			Offset = node["offset"]?.GetValue<long>() ?? 0L,
			ExpectSize = node["size"]?.GetValue<long>()
		};
		string hex = node["bytes"]?.GetValue<string>() ?? "";
		List<byte> bytes = new List<byte>();
		for (int i = 0; i + 1 < hex.Length; i += 2)
		{
			bytes.Add(byte.Parse(hex.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
		}
		patch.Bytes = bytes.ToArray();
		return patch;
	}
}