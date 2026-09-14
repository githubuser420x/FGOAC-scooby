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
			ToolTip = "输入数值后按 Enter 或点击别处应用；Esc 撤销输入。超出范围时取最近有效值。"
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
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Invalid comparison between Unknown and I4
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Invalid comparison between Unknown and I4
			if ((int)e.Key == 6)
			{
				Commit();
				e.Handled = true;
			}
			else if ((int)e.Key == 13)
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
				((DependencyObject)slider).SetCurrentValue(RangeBase.ValueProperty, (object)Math.Clamp(result, slider.Minimum, slider.Maximum));
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
					Text = "时间（秒）",
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				},
				(UIElement)Create(slider, 1.0 / 60.0, stop)
			}
		};
	}
}
