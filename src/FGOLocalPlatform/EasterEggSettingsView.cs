using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class EasterEggSettingsView : StackPanel
{
	private readonly string settingsPath;

	public ToggleButton EnabledCheckBox { get; } = new ToggleButton
	{
		Content = "Easter Egg",
		FontSize = 15.0,
		Foreground = Brushes.White,
		MinWidth = 110.0,
		Height = 40.0,
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
		EnabledCheckBox.Style = (Style)XamlReader.Parse("\r\n<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToggleButton'>\r\n <Setter Property='Background' Value='#17131B'/><Setter Property='BorderBrush' Value='#46364D'/>\r\n <Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,6'/>\r\n <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>\r\n  <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>\r\n   <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>\r\n  </Border>\r\n </ControlTemplate></Setter.Value></Setter>\r\n <Style.Triggers>\r\n  <Trigger Property='IsChecked' Value='True'><Setter Property='Background' Value='#5A1D50'/><Setter Property='BorderBrush' Value='#A85B9B'/></Trigger>\r\n  <Trigger Property='IsMouseOver' Value='True'><Setter Property='BorderBrush' Value='#ED9BDC'/></Trigger>\r\n  <Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='#ED9BDC'/></Trigger>\r\n </Style.Triggers>\r\n</Style>");
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
			Foreground = Brushes.LightGray,
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
			Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
			string text = settingsPath + $".{Environment.ProcessId}.tmp";
			File.WriteAllText(text, "[easter_bgm]\nenabled=" + ((EnabledCheckBox.IsChecked == true) ? "1" : "0") + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text, settingsPath, overwrite: true);
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
