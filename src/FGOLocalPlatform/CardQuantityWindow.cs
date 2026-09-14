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
		base.Title = (add ? "选择加入数量" : "选择移出数量");
		WindowTheme.Apply(this);
		ConfirmButton.Content = (add ? "加入卡组" : "移出卡组");
		LimitText.Text = (add ? $"本次可加入 1–{maximum} 张（受剩余数量及卡组 30 张上限限制）" : $"已选 {maximum} 张，请选择本次移出的数量。");
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
		ValidationText.Text = (flag ? "" : $"请输入 1–{Maximum} 之间的整数。");
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
