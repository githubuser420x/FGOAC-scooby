using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FGOLocalPlatform;

internal static class CraftEffects
{
	internal sealed record Effect(string Normal, string Maximum, string NormalJapanese, string MaximumJapanese);

	private static readonly Dictionary<string, Effect> Entries = Load();

	private static Dictionary<string, Effect> Load()
	{
		using Stream utf8Json = typeof(CraftEffects).Assembly.GetManifestResourceStream("FGOLocalPlatform.CraftEffects.json");
		return JsonSerializer.Deserialize<Dictionary<string, Effect>>(utf8Json);
	}

	public static Effect? Get(string key)
	{
		return Entries.GetValueOrDefault(key);
	}
}
