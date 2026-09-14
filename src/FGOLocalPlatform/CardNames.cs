using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

internal static class CardNames
{
	/// <summary>
	/// One row of FGOLocalPlatform.CardNames.json. The English name is stored in the field the
	/// author called "Chinese", which is why the JSON name and the property name differ.
	/// </summary>
	internal sealed class Names
	{
		[JsonPropertyName("Japanese")]
		public string Japanese { get; init; } = "";

		[JsonPropertyName("Chinese")]
		public string English { get; init; } = "";
	}

	private static readonly Dictionary<string, Names> Entries = Load();

	private static Dictionary<string, Names> Load()
	{
		using Stream utf8Json = typeof(CardNames).Assembly.GetManifestResourceStream("FGOLocalPlatform.CardNames.json");
		return JsonSerializer.Deserialize<Dictionary<string, Names>>(utf8Json);
	}

	public static Names Get(Card card)
	{
		return Entries.GetValueOrDefault(CardFormState.EntityKey(card)) ?? new Names { Japanese = card.Trc?.Name ?? card.DisplayName };
	}

	public static string English(int kind, int id)
	{
		return Entries.GetValueOrDefault($"{((kind == 1) ? "SVT" : "CE")}{id:D5}")?.English ?? "";
	}
}
