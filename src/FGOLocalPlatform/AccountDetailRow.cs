using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

public static class AccountDetailRow
{
	public static Border Create(string name, string value)
	{
		Grid grid = new Grid
		{
			MinHeight = 28.0,
			Margin = new Thickness(12.0, 1.0, 12.0, 1.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(160.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		TextBlock element = new TextBlock
		{
			Text = name,
			Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
			FontSize = 13.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(element);
		TextBlock element2 = new TextBlock
		{
			Text = value,
			Foreground = (Brush)Application.Current.Resources["TextBrush"],
			FontSize = 14.0,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element2, 1);
		grid.Children.Add(element2);
		return new Border
		{
			Child = grid,
			Background = (Brush)Application.Current.Resources["PlateBrush"],
			BorderBrush = (Brush)Application.Current.Resources["LineSoftBrush"],
			BorderThickness = new Thickness(1.0, 0.0, 1.0, 1.0)
		};
	}
}
