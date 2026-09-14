using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FGOLocalPlatform;

internal static class ThemedMessageBox
{
	private static T Resource<T>(string key)
	{
		return (T)Application.Current.Resources[key];
	}

	public static MessageBoxResult Show(string message, string caption = "FGOAC scooby", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None, bool foreground = false)
	{
		return Show(null, message, caption, buttons, image, defaultResult, foreground);
	}

	/// <summary>
	/// Set <paramref name="foreground" /> for a question asked while the game is on screen: the
	/// dialog then stays above other windows, appears in the task bar, takes focus, and answers
	/// nothing at all if it is closed without a button being pressed.
	/// </summary>
	public static MessageBoxResult Show(Window? owner, string message, string caption = "FGOAC scooby", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None, bool foreground = false)
	{
		//IL_01c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		if (owner == null)
		{
			owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault((Window w) => w.IsActive);
		}
		if (owner == null)
		{
			owner = Application.Current?.MainWindow;
		}
		Window dialog = new Window
		{
			Title = caption,
			Width = 500.0,
			SizeToContent = SizeToContent.Height,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = (foreground || owner == null || !owner.IsVisible),
			Topmost = foreground,
			WindowStartupLocation = ((owner == null || !owner.IsVisible) ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner)
		};
		if (owner != null && owner.IsVisible)
		{
			dialog.Owner = owner;
		}
		WindowTheme.Apply(dialog);
		StackPanel stackPanel = new StackPanel();
		string text = image switch
		{
			MessageBoxImage.Hand => "Error", 
			MessageBoxImage.Exclamation => "Warning", 
			MessageBoxImage.Question => "Confirm", 
			_ => "Notice", 
		};
		Brush titleBrush = image switch
		{
			MessageBoxImage.Hand => Resource<Brush>("DangerBrush"), 
			MessageBoxImage.Exclamation => Resource<Brush>("EmberBrush"), 
			_ => Resource<Brush>("ParchmentBrush"), 
		};
		StackPanel titleRow = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		titleRow.Children.Add(new Border
		{
			Width = 2.0,
			Background = titleBrush,
			Margin = new Thickness(0.0, 1.0, 12.0, 1.0)
		});
		titleRow.Children.Add(new TextBlock
		{
			Text = text,
			FontFamily = Resource<FontFamily>("DisplayFont"),
			FontSize = 21.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = titleBrush,
			VerticalAlignment = VerticalAlignment.Center
		});
		stackPanel.Children.Add(titleRow);
		stackPanel.Children.Add(new Border
		{
			Height = 1.0,
			Background = Resource<Brush>("RuleBrush"),
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		UIElementCollection children = stackPanel.Children;
		ScrollViewer scrollViewer = new ScrollViewer();
		Rect workArea = SystemParameters.WorkArea;
		scrollViewer.MaxHeight = Math.Max(120.0, workArea.Height - 240.0);
		scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		scrollViewer.Background = Brushes.Transparent;
		scrollViewer.Content = new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			LineHeight = 23.0
		};
		children.Add(scrollViewer);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 22.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(stackPanel2);
		dialog.Content = new ChamferPanel
		{
			Fill = Resource<Brush>("PlateBrush"),
			Stroke = Resource<Brush>("GoldSoftBrush"),
			StrokeThickness = 1.0,
			Cut = 16.0,
			OrnamentBrush = Resource<Brush>("GoldBrush"),
			Padding = new Thickness(22.0, 20.0, 22.0, 20.0),
			Child = stackPanel,
			Margin = new Thickness(10.0)
		};
		MessageBoxResult[] choices = buttons switch
		{
			MessageBoxButton.YesNo => new MessageBoxResult[2]
			{
				MessageBoxResult.Yes,
				MessageBoxResult.No
			}, 
			MessageBoxButton.YesNoCancel => new MessageBoxResult[3]
			{
				MessageBoxResult.Yes,
				MessageBoxResult.No,
				MessageBoxResult.Cancel
			}, 
			MessageBoxButton.OKCancel => new MessageBoxResult[2]
			{
				MessageBoxResult.OK,
				MessageBoxResult.Cancel
			}, 
			_ => new MessageBoxResult[1] { MessageBoxResult.OK }, 
		};
		if (!Enumerable.Contains(choices, defaultResult))
		{
			defaultResult = choices[0];
		}
		MessageBoxResult result = MessageBoxResult.None;
		bool selected = false;
		Button initialFocus = null;
		MessageBoxResult[] array = choices;
		foreach (MessageBoxResult choice in array)
		{
			Button button = new Button();
			Button button2 = button;
			button2.Content = choice switch
			{
				MessageBoxResult.Yes => "Yes (Y)", 
				MessageBoxResult.No => "No (N)", 
				MessageBoxResult.Cancel => "Cancel", 
				_ => "OK", 
			};
			button.MinWidth = 104.0;
			button.MinHeight = 40.0;
			if (choice == defaultResult)
			{
				button.Style = Resource<Style>("PrimaryButtonStyle");
			}
			button.IsDefault = choice == defaultResult;
			button.IsCancel = choice == MessageBoxResult.Cancel || buttons == MessageBoxButton.OK;
			Button button3 = button;
			button3.Click += delegate
			{
				result = choice;
				selected = true;
				dialog.Close();
			};
			stackPanel2.Children.Add(button3);
			if (choice == defaultResult)
			{
				initialFocus = button3;
			}
		}
		dialog.Closing += delegate
		{
			if (!selected && !foreground)
			{
				if (buttons == MessageBoxButton.YesNo)
				{
					result = MessageBoxResult.No;
				}
				else
				{
					result = ((buttons == MessageBoxButton.OK) ? MessageBoxResult.OK : MessageBoxResult.Cancel);
				}
			}
		};
		dialog.Loaded += delegate
		{
			initialFocus?.Focus();
			if (foreground)
			{
				dialog.Activate();
			}
		};
		dialog.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Invalid comparison between Unknown and I4
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Invalid comparison between Unknown and I4
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Invalid comparison between Unknown and I4
			MessageBoxResult messageBoxResult = (((int)e.Key == 68) ? MessageBoxResult.Yes : (((int)e.Key == 57 || ((int)e.Key == 13 && buttons == MessageBoxButton.YesNo)) ? MessageBoxResult.No : MessageBoxResult.None));
			if (messageBoxResult != MessageBoxResult.None && Enumerable.Contains(choices, messageBoxResult))
			{
				result = messageBoxResult;
				selected = true;
				e.Handled = true;
				dialog.Close();
			}
		};
		dialog.ShowDialog();
		return result;
	}
}
