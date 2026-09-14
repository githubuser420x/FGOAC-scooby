using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class CardVariantsWindow : Window, IComponentConnector
{
	public sealed class Choice
	{
		public Card Card { get; init; }

		public string Label => CardFormState.FormLabel(Card);

		public int Owned { get; init; }

		public int Selected { get; init; }

		public int Maximum { get; init; }

		public string Quantity { get; set; } = "0";
	}

	private readonly int slots;

	public List<Choice> Choices { get; }

	public Dictionary<ushort, int> Quantities { get; private set; } = new Dictionary<ushort, int>();

	public CardVariantsWindow(Card card, IEnumerable<Card> available, IEnumerable<Card> selected, IReadOnlyDictionary<int, int> owned, bool ownedOnly)
	{
		CardVariantsWindow cardVariantsWindow = this;
		InitializeComponent();
		WindowTheme.Apply(this);
		List<Card> library = available.ToList();
		List<Card> list = selected.ToList();
		slots = 30 - list.Count;
		base.DataContext = CardStack.BuildEntities(new Card[1] { card }).Single();
		Choices = (from f in CardFormState.Build(card, library, list, owned)
			select new Choice
			{
				Card = f.Card,
				Owned = f.Owned,
				Selected = f.Selected,
				Maximum = Math.Max(0, Math.Min(cardVariantsWindow.slots, Math.Min(library.Count((Card c) => c.TrcId == f.Card.TrcId), ownedOnly ? f.Available : 30)))
			}).ToList();
		Forms.ItemsSource = Choices;
		if (card.CardTypeId == 2)
		{
			base.Height = 820.0;
			base.MinHeight = 650.0;
			CraftPreview.Visibility = Visibility.Visible;
			CraftImage.Source = card.Bitmap;
			CraftEffects.Effect effect = CraftEffects.Get(CardFormState.EntityKey(card));
			NormalEffect.Text = effect?.Normal ?? "No normal effect text found.";
			MaximumEffect.Text = effect?.Maximum ?? "No Max Limit Break effect text found.";
			NormalEffect.ToolTip = effect?.NormalJapanese;
			MaximumEffect.ToolTip = effect?.MaximumJapanese;
		}
		Hint.Text = $"{slots} slots left in the deck. Double-click a row to pick a quantity and add it, or fill in several rows and add them all at once." + (ownedOnly ? " Only cards this account owns can be added." : " Browsing the full card library.");
	}

	private void Forms_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		DataGrid forms = Forms;
		object originalSource = e.OriginalSource;
		if (!(ItemsControl.ContainerFromElement(forms, (DependencyObject)((originalSource is DependencyObject) ? originalSource : null)) is DataGridRow { Item: Choice item }))
		{
			return;
		}
		e.Handled = true;
		if (item.Maximum < 1)
		{
			Error.Text = "No card of this type can be added, or the deck is full.";
			return;
		}
		CardQuantityWindow cardQuantityWindow = new CardQuantityWindow(item.Label, item.Maximum, add: true)
		{
			Owner = this
		};
		if (cardQuantityWindow.ShowDialog() == true)
		{
			Quantities = new Dictionary<ushort, int> { [item.Card.TrcId] = cardQuantityWindow.Quantity };
			base.DialogResult = true;
		}
	}

	private void Forms_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CraftPreview.Visibility == Visibility.Visible && Forms.SelectedItem is Choice choice)
		{
			CraftImage.Source = choice.Card.Bitmap;
		}
	}

	private void Add_OnClick(object sender, RoutedEventArgs e)
	{
		Forms.CommitEdit();
		Forms.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
		Dictionary<ushort, int> dictionary = new Dictionary<ushort, int>();
		foreach (Choice choice in Choices)
		{
			if (!int.TryParse(choice.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0 || result > choice.Maximum)
			{
				Error.Text = $"{choice.Label}: enter a whole number from 0 to {choice.Maximum}.";
				return;
			}
			if (result > 0)
			{
				dictionary[choice.Card.TrcId] = result;
			}
		}
		if (dictionary.Values.Sum() < 1 || dictionary.Values.Sum() > slots)
		{
			Error.Text = $"Choose 1 to {slots} cards.";
		}
		else
		{
			Quantities = dictionary;
			base.DialogResult = true;
		}
	}
}
