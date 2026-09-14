using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

/// <summary>
/// Drag and drop for the deck editor. A card dragged from the library into the deck adds one copy
/// of it; a deck card dragged along the deck moves its stack to that place, and dragged back into
/// the library it leaves the deck. Double-click still opens the type-and-quantity dialog.
/// </summary>
public partial class MainWindow
{
	private const string CardDragFormat = "FGOAC.CardStack";

	private sealed record CardDrag(CardStack Stack, bool FromDeck);

	private Point dragStart;

	private CardDrag? dragCandidate;

	private void CardDrag_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		CardStack? stack = CardStackAt(e.OriginalSource as DependencyObject, (ItemsControl)sender);
		dragCandidate = ((stack == null) ? null : new CardDrag(stack, ReferenceEquals(sender, SelectedCardList)));
		dragStart = e.GetPosition(this);
	}

	private void CardDrag_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (dragCandidate == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Vector moved = e.GetPosition(this) - dragStart;
		if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		CardDrag drag = dragCandidate;
		dragCandidate = null;
		DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(CardDragFormat, drag), DragDropEffects.Move);
	}

	private void CardDrag_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		dragCandidate = null;
	}

	private void Deck_OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = (e.Data.GetDataPresent(CardDragFormat) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private void Deck_OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		if (!(e.Data.GetData(CardDragFormat) is CardDrag drag))
		{
			return;
		}
		if (drag.FromDeck)
		{
			MoveStackInDeck(drag.Stack, CardStackAt(e.OriginalSource as DependencyObject, SelectedCardList));
		}
		else if (CardStack.Move(cardCollection, drag.Stack.TrcId, 1, add: true) == 0)
		{
			RuntimeStatusText.Text = ((cardCollection.SelectedCards.Count >= 30) ? "The deck is full: it holds 30 cards." : "No more copies of that card are available to add.");
			return;
		}
		ClearIconSelection();
		ApplyOwnedCardFilter();
		PublishDeck();
	}

	private void Library_OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = ((e.Data.GetData(CardDragFormat) is CardDrag { FromDeck: true }) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private void Library_OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		if (!(e.Data.GetData(CardDragFormat) is CardDrag { FromDeck: true } drag) || CardStack.Move(cardCollection, drag.Stack.TrcId, drag.Stack.Count, add: false) == 0)
		{
			return;
		}
		ApplyOwnedCardFilter();
		PublishDeck();
	}

	/// <summary>Puts every copy of a stack in front of the target stack, or at the end when dropped on empty space.</summary>
	private void MoveStackInDeck(CardStack stack, CardStack? target)
	{
		if (target != null && target.TrcId == stack.TrcId)
		{
			return;
		}
		List<Card> deck = cardCollection.SelectedCards;
		List<Card> moving = deck.Where((Card card) => card.TrcId == stack.TrcId).ToList();
		deck.RemoveAll((Card card) => card.TrcId == stack.TrcId);
		int index = ((target == null) ? (-1) : deck.FindIndex((Card card) => card.TrcId == target.TrcId));
		deck.InsertRange((index < 0) ? deck.Count : index, moving);
	}

	/// <summary>The card stack under a point in one of the two lists: the tile button of the icon grid, or the row.</summary>
	private static CardStack? CardStackAt(DependencyObject? source, ItemsControl list)
	{
		for (DependencyObject? node = source; node != null && node != list; node = ((node is Visual || node is Visual3D) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)))
		{
			if (node is Button { Tag: CardStack tile })
			{
				return tile;
			}
			if (node is ListViewItem { DataContext: CardStack row })
			{
				return row;
			}
		}
		return null;
	}
}
