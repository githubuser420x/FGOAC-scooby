using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public sealed class CardStack
{
	public Card Card { get; }

	public int Count { get; }

	public IReadOnlyList<Card> Variants { get; private init; } = Array.Empty<Card>();

	public string JapaneseName => CardNames.Get(Card).Japanese;

	public string ChineseName => CardNames.Get(Card).Chinese;

	public string EntityLabel => "Internal ID " + CardFormState.EntityKey(Card);

	public string DisplayName => Card.DisplayName;

	public string FileName => Card.FileName;

	public ushort TrcId => Card.TrcId;

	public BitmapImage Thumbnail => Card.Thumbnail;

	public string StackLabel => $"{Card.CardTypeLabel} x{Count}";

	private CardStack(Card card, int count)
	{
		Card = card;
		Count = count;
	}

	public static List<CardStack> Build(IEnumerable<Card> cards)
	{
		return (from card in cards
			group card by card.TrcId into @group
			select new CardStack(@group.First(), @group.Count())).ToList();
	}

	public static List<CardStack> BuildEntities(IEnumerable<Card> cards)
	{
		return (from @group in cards.GroupBy(CardFormState.EntityKey)
			select new CardStack(@group.OrderBy((Card c) => c.TrcId).First(), @group.Count())
			{
				Variants = (from c in @group
					group c by c.TrcId into g
					select g.First()).ToArray()
			}).ToList();
	}

	public static int Move(CardCollection collection, ushort cardId, int count, bool add)
	{
		List<Card> list = (add ? collection.Cards : collection.SelectedCards).Where((Card card) => card.TrcId == cardId).Take(count).ToList();
		if (count < 1 || list.Count != count || (add && collection.SelectedCards.Count + count > 30))
		{
			return 0;
		}
		foreach (Card item in list)
		{
			if (add)
			{
				collection.Select(item);
			}
			else
			{
				collection.Deselect(item);
			}
		}
		return count;
	}
}
