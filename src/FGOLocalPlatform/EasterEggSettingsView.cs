using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class EasterEggSettingsView : StackPanel
{
	private readonly string settingsPath;

	public ToggleButton EnabledCheckBox { get; } = new ToggleButton
	{
		Content = "Easter Egg",
		MinWidth = 110.0,
		Height = 34.0,
		HorizontalAlignment = HorizontalAlignment.Left
	};

	public TextBlock StatusText { get; } = new TextBlock
	{
		Visibility = Visibility.Collapsed,
		FontSize = 14.0,
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
	};

	public EasterEggSettingsView()
		: this(Path.Combine(GamePaths.GameRoot, "BGM", "settings.ini"))
	{
	}

	public EasterEggSettingsView(string path)
	{
		settingsPath = path;
		EnabledCheckBox.Style = (Style)Application.Current.Resources["SwitchToggleStyle"];
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(EnabledCheckBox);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Turn it on, then take a look in game.",
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
			FontSize = 14.0
		});
		base.Children.Add(stackPanel);
		base.Children.Add(StatusText);
		try
		{
			if (File.Exists(path))
			{
				string[] array = File.ReadAllLines(path);
				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].Trim().Equals("enabled=1", StringComparison.OrdinalIgnoreCase))
					{
						EnabledCheckBox.IsChecked = true;
					}
				}
			}
		}
		catch (IOException ex)
		{
			StatusText.Text = "Could not read the Easter egg setting: " + ex.Message;
			StatusText.Visibility = Visibility.Visible;
		}
		EnabledCheckBox.Click += delegate
		{
			Save();
		};
	}

	public bool Save()
	{
		try
		{
			AtomicFile.WriteAllText(settingsPath, "[easter_bgm]\nenabled=" + ((EnabledCheckBox.IsChecked == true) ? "1" : "0") + "\n");
			StatusText.Text = "";
			StatusText.Visibility = Visibility.Collapsed;
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save the Easter egg setting: " + ex.Message;
			StatusText.Visibility = Visibility.Visible;
			return false;
		}
	}
}
