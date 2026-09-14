using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class DebugSettingsView : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private bool ready;

	private Dictionary<string, CheckBox> options = new Dictionary<string, CheckBox>();

	public DebugSettingsView()
		: this(Path.Combine(GamePaths.GameRoot, "battle-debug.ini"))
	{
	}

	public DebugSettingsView(string path)
	{
		settingsPath = path;
		InitializeComponent();
		options = new Dictionary<string, CheckBox>
		{
			["attack_stun"] = AttackStun,
			["one_hit"] = OneHit,
			["full_np"] = FullNp,
			["instant_noble"] = InstantNoble,
			["invincible"] = Invincible
		};
		try
		{
			if (File.Exists(path))
			{
				foreach (string item in File.ReadLines(path))
				{
					string[] array = item.Split('=', 2);
					if (array.Length == 2 && options.TryGetValue(array[0].Trim(), out CheckBox value))
					{
						value.IsChecked = array[1].Trim() == "1";
					}
				}
			}
			StatusText.Text = "设置已读取；新增昏倒选项需加载新版游戏钩子，实际覆盖范围待实战验证。";
		}
		catch (Exception ex)
		{
			StatusText.Text = "读取预设失败：" + ex.Message;
		}
		ready = true;
	}

	private void Option_OnChanged(object sender, RoutedEventArgs e)
	{
		if (ready)
		{
			Save();
		}
	}

	public bool Save()
	{
		string text = settingsPath + ".tmp";
		try
		{
			StringBuilder stringBuilder = new StringBuilder("[battle_debug]\n");
			foreach (KeyValuePair<string, CheckBox> option in options)
			{
				stringBuilder.Append(option.Key).Append('=').Append((option.Value.IsChecked == true) ? '1' : '0')
					.Append('\n');
			}
			stringBuilder.Append("freeze_enemy=0\nenemy_evade=0\nenemy_flee=0\nenemy_guard=0\n");
			File.WriteAllText(text, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text, settingsPath, overwrite: true);
			StatusText.Text = "设置已保存；已加载新版游戏钩子时，战斗开关约一秒后生效。";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "保存预设失败：" + ex.Message;
			return false;
		}
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void DisableAll_OnClick(object sender, RoutedEventArgs e)
	{
		ready = false;
		foreach (CheckBox value in options.Values)
		{
			value.IsChecked = false;
		}
		ready = true;
		Save();
	}
}
