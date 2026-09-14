using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

internal static class CardNames
{
	internal sealed record Names(string Japanese, string Chinese);

	private static readonly Dictionary<string, Names> Entries = Load();

	private static Dictionary<string, Names> Load()
	{
		using Stream utf8Json = typeof(CardNames).Assembly.GetManifestResourceStream("FGOLocalPlatform.CardNames.json");
		return JsonSerializer.Deserialize<Dictionary<string, Names>>(utf8Json);
	}

	public static Names Get(Card card)
	{
		return Entries.GetValueOrDefault(CardFormState.EntityKey(card)) ?? new Names(card.Trc?.Name ?? card.DisplayName, "Name not available");
	}

	public static string Chinese(int kind, int id)
	{
		return Entries.GetValueOrDefault($"{((kind == 1) ? "SVT" : "CE")}{id:D5}")?.Chinese ?? "Name not available";
	}
}
