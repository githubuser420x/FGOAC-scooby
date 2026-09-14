using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class AudioSettingsView : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private readonly DispatcherTimer saveTimer;

	private bool ready;

	public AudioSettingsView()
		: this(Path.Combine(GamePaths.GameRoot, "audio-volume.ini"))
	{
	}

	public AudioSettingsView(string path)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		settingsPath = path;
		InitializeComponent();
		saveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(120.0)
		};
		saveTimer.Tick += delegate
		{
			Save();
		};
		Dictionary<string, int> dictionary = new Dictionary<string, int>
		{
			{ "voice", 100 },
			{ "bgm", 100 },
			{ "effects", 100 }
		};
		try
		{
			if (File.Exists(path))
			{
				foreach (string item in File.ReadLines(path))
				{
					string[] array = item.Split('=', 2);
					if (array.Length == 2 && dictionary.ContainsKey(array[0]) && int.TryParse(array[1], out var result))
					{
						dictionary[array[0]] = Math.Clamp(result, 0, 100);
					}
				}
			}
			StatusText.Text = "拖动滑块调整音量，设置会自动保留。";
		}
		catch (Exception ex)
		{
			StatusText.Text = "读取音量设置失败：" + ex.Message;
		}
		VoiceSlider.Value = dictionary["voice"];
		BgmSlider.Value = dictionary["bgm"];
		EffectsSlider.Value = dictionary["effects"];
		ready = true;
		base.Unloaded += delegate
		{
			if (saveTimer.IsEnabled)
			{
				Save();
			}
		};
	}

	private void Volume_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (ready && !saveTimer.IsEnabled)
		{
			saveTimer.Start();
		}
	}

	public bool Save()
	{
		saveTimer.Stop();
		string text = settingsPath + ".tmp";
		try
		{
			string contents = $"[audio]\nbgm={(int)BgmSlider.Value}\nvoice={(int)VoiceSlider.Value}\neffects={(int)EffectsSlider.Value}\n";
			File.WriteAllText(text, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text, settingsPath, overwrite: true);
			StatusText.Text = "已保存 · 游戏运行时实时应用，下次启动也会保留。";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "保存音量设置失败：" + ex.Message;
			return false;
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private void Apply_OnClick(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void SetAll(int value)
	{
		VoiceSlider.Value = value;
		BgmSlider.Value = value;
		EffectsSlider.Value = value;
		Save();
	}

	private void Mute_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(0);
	}

	private void Defaults_OnClick(object sender, RoutedEventArgs e)
	{
		SetAll(100);
	}
}
