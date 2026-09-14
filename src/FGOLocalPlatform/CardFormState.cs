using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public sealed record CardFormState(Card Card, int Owned, int Selected)
{
	public int Available => Math.Max(0, Owned - Selected);

	public string Label => FormLabel(Card);

	public string Status
	{
		get
		{
			if (Owned <= 0)
			{
				return "Not printed yet";
			}
			return $"{Owned} owned - {Selected} in deck - {Available} available";
		}
	}

	private static readonly Regex FilePattern = new Regex("_(SVT|CE)(\\d+)_A(\\d+)_(NORMAL|HOLO)\\.", RegexOptions.IgnoreCase);

	public static string EntityKey(Card card)
	{
		Match match = FilePattern.Match(card.FileName);
		if (!match.Success)
		{
			return $"TC{card.TrcId}";
		}
		return match.Groups[1].Value.ToUpperInvariant() + match.Groups[2].Value;
	}

	public static string FormLabel(Card card)
	{
		Match match = FilePattern.Match(card.FileName);
		if (match.Success)
		{
			int num = int.Parse(match.Groups[3].Value);
			string text2;
			if (match.Groups[1].Value.Equals("SVT", StringComparison.OrdinalIgnoreCase))
			{
				string text;
				switch (num)
				{
				case 0:
					text = "Base Art";
					break;
				case 1:
				case 2:
				case 3:
					text = $"Ascension {num}";
					break;
				case 4:
					text = "Final Ascension";
					break;
				default:
					text = $"Special Art {num:D2}";
					break;
				}
				text2 = text;
			}
			else
			{
				text2 = $"Art {num:D2}";
			}
			string value = text2;
			return $"{value} - {(match.Groups[4].Value.Equals("HOLO", StringComparison.OrdinalIgnoreCase) ? "Fatal Foil" : "Normal")} - TC {card.TrcId}";
		}
		return $"Art - TC {card.TrcId}";
	}

	public static List<CardFormState> Build(Card focus, IEnumerable<Card> library, IEnumerable<Card> selected, IReadOnlyDictionary<int, int> owned)
	{
		List<Card> selection = selected.ToList();
		string entity = EntityKey(focus);
		return (from card in library.Concat(selection)
			where EntityKey(card) == entity
			group card by card.TrcId into @group
			select new CardFormState(@group.First(), owned.TryGetValue(@group.Key, out var value) ? Math.Max(0, value) : 0, selection.Count((Card card) => card.TrcId == @group.Key))).OrderBy<CardFormState, string>((CardFormState row) => row.Card.FileName, StringComparer.OrdinalIgnoreCase).ToList();
	}
}
