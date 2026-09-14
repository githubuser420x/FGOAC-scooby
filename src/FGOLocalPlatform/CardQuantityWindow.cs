using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class CardQuantityWindow : Window, IComponentConnector
{
	public int Maximum { get; }

	public int Quantity { get; private set; }

	public CardQuantityWindow(string cardName, int maximum, bool add)
	{
		if (maximum < 1 || maximum > 30)
		{
			throw new ArgumentOutOfRangeException("maximum");
		}
		Maximum = maximum;
		InitializeComponent();
		CardNameText.Text = cardName;
		base.Title = (add ? "Choose Quantity to Add" : "Choose Quantity to Remove");
		WindowTheme.Apply(this);
		ConfirmButton.Content = (add ? "Add to Deck" : "Remove from Deck");
		LimitText.Text = (add ? $"You can add 1 to {maximum} now, limited by what is left and the 30-card deck cap." : $"{maximum} in the deck - choose how many to remove.");
		ValidateQuantity();
		base.Loaded += delegate
		{
			QuantityInput.Focus();
			QuantityInput.SelectAll();
		};
	}

	private bool ValidateQuantity()
	{
		if (ConfirmButton == null || ValidationText == null)
		{
			return false;
		}
		int result;
		bool flag = int.TryParse(QuantityInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 1 && result <= Maximum;
		ConfirmButton.IsEnabled = flag;
		ValidationText.Text = (flag ? "" : $"Enter a whole number from 1 to {Maximum}.");
		Quantity = (flag ? result : 0);
		return flag;
	}

	private void QuantityInput_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		ValidateQuantity();
	}

	private void SetQuantity(int quantity)
	{
		QuantityInput.Text = Math.Clamp(quantity, 1, Maximum).ToString(CultureInfo.InvariantCulture);
	}

	private void Decrease_OnClick(object sender, RoutedEventArgs e)
	{
		SetQuantity(Quantity - 1);
	}

	private void Increase_OnClick(object sender, RoutedEventArgs e)
	{
		SetQuantity(Quantity + 1);
	}

	private void All_OnClick(object sender, RoutedEventArgs e)
	{
		SetQuantity(Maximum);
	}

	private void Confirm_OnClick(object sender, RoutedEventArgs e)
	{
		if (ValidateQuantity())
		{
			base.DialogResult = true;
		}
	}
}
