using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// A knob a mod exposes. The value is always carried as text so one store covers all three
/// shapes; the type says how the Battle Tuner draws it and how the text is validated.
/// </summary>
internal sealed class ModParameter
{
	public string Id = "";

	public string Label = "";

	public string Help = "";

	/// <summary>number, switch or choice.</summary>
	public string Type = "number";

	public string DefaultText = "";

	public string Unit = "";

	public double? Min;

	public double? Max;

	public double? Step;

	public List<ModParameterOption> Options = new List<ModParameterOption>();

	public bool IsNumber
	{
		get
		{
			return Type == "number";
		}
	}

	public bool IsSwitch
	{
		get
		{
			return Type == "switch";
		}
	}

	public bool IsChoice
	{
		get
		{
			return Type == "choice";
		}
	}

	public static ModParameter Parse(JsonNode node)
	{
		ModParameter parameter = new ModParameter
		{
			Id = node["id"]?.GetValue<string>() ?? "",
			Label = node["label"]?.GetValue<string>() ?? "",
			Help = node["help"]?.GetValue<string>() ?? "",
			Type = node["type"]?.GetValue<string>() ?? "number",
			Unit = node["unit"]?.GetValue<string>() ?? "",
			Min = ReadDouble(node["min"]),
			Max = ReadDouble(node["max"]),
			Step = ReadDouble(node["step"])
		};
		parameter.DefaultText = ReadText(node["value"]);
		if (node["options"] is JsonArray options)
		{
			foreach (JsonNode option in options)
			{
				parameter.Options.Add(new ModParameterOption
				{
					Value = option?["value"]?.GetValue<string>() ?? "",
					Label = option?["label"]?.GetValue<string>() ?? option?["value"]?.GetValue<string>() ?? ""
				});
			}
		}
		if (parameter.Type == "switch" && parameter.DefaultText.Length == 0)
		{
			parameter.DefaultText = "false";
		}
		if (parameter.IsChoice && parameter.DefaultText.Length == 0 && parameter.Options.Count > 0)
		{
			parameter.DefaultText = parameter.Options[0].Value;
		}
		return parameter;
	}

	/// <summary>
	/// Brings a stored or typed value into range, so a hand-edited store or a stray keystroke
	/// cannot put a number the rule then multiplies into nonsense.
	/// </summary>
	public string Normalise(string? value)
	{
		if (IsSwitch)
		{
			return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
		}
		if (IsChoice)
		{
			foreach (ModParameterOption option in Options)
			{
				if (string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
				{
					return option.Value;
				}
			}
			return DefaultText;
		}
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
		{
			return DefaultText;
		}
		if (Min.HasValue && parsed < Min.Value)
		{
			parsed = Min.Value;
		}
		if (Max.HasValue && parsed > Max.Value)
		{
			parsed = Max.Value;
		}
		return parsed.ToString("0.####", CultureInfo.InvariantCulture);
	}

	private static string ReadText(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			if (value.TryGetValue<string>(out string? text))
			{
				return text ?? "";
			}
			if (value.TryGetValue<bool>(out bool flag))
			{
				return flag ? "true" : "false";
			}
			return value.ToJsonString();
		}
		return "";
	}

	private static double? ReadDouble(JsonNode? node)
	{
		if (node is JsonValue value && value.TryGetValue<double>(out double result))
		{
			return result;
		}
		return null;
	}
}

internal sealed class ModParameterOption
{
	public string Value = "";

	public string Label = "";
}

/// <summary>
/// Where a mod's chosen knob values live: one small json beside the journal. The defaults come
/// from the mod itself, so the store only ever holds what the player changed.
/// </summary>
internal static class ModParameterStore
{
	public static Dictionary<string, string> Load(string stateRoot, ModDefinition definition)
	{
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ModParameter parameter in definition.Parameters)
		{
			values[parameter.Id] = parameter.Normalise(parameter.DefaultText);
		}
		string path = PathFor(stateRoot, definition.Id);
		if (!File.Exists(path))
		{
			return values;
		}
		try
		{
			if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject stored)
			{
				return values;
			}
			foreach (ModParameter parameter in definition.Parameters)
			{
				if (stored[parameter.Id] is JsonValue value && value.TryGetValue<string>(out string? text))
				{
					values[parameter.Id] = parameter.Normalise(text);
				}
			}
		}
		catch (JsonException)
		{
		}
		catch (IOException)
		{
		}
		return values;
	}

	public static void Save(string stateRoot, ModDefinition definition, IReadOnlyDictionary<string, string> values)
	{
		JsonObject stored = new JsonObject();
		foreach (ModParameter parameter in definition.Parameters)
		{
			values.TryGetValue(parameter.Id, out string? value);
			stored[parameter.Id] = parameter.Normalise(value);
		}
		string path = PathFor(stateRoot, definition.Id);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		AtomicFile.WriteAllText(path, stored.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static string PathFor(string stateRoot, string modId)
	{
		return Path.Combine(stateRoot, "params", modId + ".json");
	}
}