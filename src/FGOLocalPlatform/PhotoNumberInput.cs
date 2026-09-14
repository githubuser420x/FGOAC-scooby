using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FGOLocalPlatform;

public static class PhotoNumberInput
{
	public static TextBox Create(Slider slider, double multiplier = 1.0, Action? beforeCommit = null)
	{
		TextBox box = new TextBox
		{
			Width = 84.0,
			MinWidth = 0.0,
			Height = 30.0,
			Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
			FontSize = 14.0,
			TextAlignment = TextAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(23, 18, 27)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(86, 65, 93)),
			BorderThickness = new Thickness(1.0),
			ToolTip = "Type a value and press Enter, or click elsewhere, to apply it; Esc undoes what you typed. A value outside the range snaps to the nearest one allowed."
		};
		slider.ValueChanged += delegate
		{
			if (!box.IsKeyboardFocusWithin)
			{
				Refresh();
			}
		};
		box.LostKeyboardFocus += delegate
		{
			Commit();
		};
		box.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				Commit();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape)
			{
				Refresh();
				e.Handled = true;
			}
		};
		Refresh();
		return box;
		void Commit()
		{
			if ((double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) || double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) && double.IsFinite(result))
			{
				result = Math.Clamp(result / multiplier, slider.Minimum, slider.Maximum);
				if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0.0)
				{
					result = slider.Minimum + Math.Round((result - slider.Minimum) / slider.TickFrequency) * slider.TickFrequency;
				}
				beforeCommit?.Invoke();
				slider.SetCurrentValue(RangeBase.ValueProperty, (object)Math.Clamp(result, slider.Minimum, slider.Maximum));
			}
			Refresh();
		}
		void Refresh()
		{
			box.Text = (slider.Value * multiplier).ToString("0.###", CultureInfo.CurrentCulture);
		}
	}

	public static FrameworkElement TimeRow(Slider slider, Action stop)
	{
		return new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = "Time (s)",
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				},
				(UIElement)Create(slider, 1.0 / 60.0, stop)
			}
		};
	}
}
