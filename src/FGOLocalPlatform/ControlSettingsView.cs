using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class ControlSettingsView : UserControl, IComponentConnector
{
	private sealed record CalibrationBinding(string Label, string Key, int Default, Button Button)
	{
		public int Value { get; set; } = Default;
	}

	public sealed record Choice(string Name, int Value)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record Mapping(string Section, string Key, int Default, ComboBox Selector, KeyBindingButton? KeyButton)
	{
		public int Value
		{
			get
			{
				return KeyButton?.Value ?? ((int)(Selector.SelectedValue ?? ((object)0)));
			}
			set
			{
				if (KeyButton != null)
				{
					KeyButton.Value = value;
				}
				else
				{
					Selector.SelectedValue = value;
				}
			}
		}
	}

	private static readonly (string Label, string Key, int Mask)[] PhysicalButtons = new(string, string, int)[19]
	{
		("×", "cross", 4096),
		("○", "circle", 8192),
		("□", "square", 16384),
		("△", "triangle", 32768),
		("L1", "l1", 256),
		("R1", "r1", 512),
		("L2", "l2", 65536),
		("R2", "r2", 131072),
		("L3", "l3", 64),
		("R3", "r3", 128),
		("Options", "options", 16),
		("Create", "create", 32),
		("十字键上", "dpadUp", 1),
		("十字键下", "dpadDown", 2),
		("十字键左", "dpadLeft", 4),
		("十字键右", "dpadRight", 8),
		("PS", "ps", 1024),
		("触摸板按下", "touchpad", 262144),
		("静音键", "mute", 524288)
	};

	private readonly List<CalibrationBinding> dualSenseCalibration = new List<CalibrationBinding>();

	private readonly DispatcherTimer calibrationTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(33.0)
	};

	private CalibrationBinding? pendingCalibration;

	private bool calibrationSawNeutral;

	private DateTime calibrationDeadline;

	private readonly List<Mapping> mappings = new List<Mapping>();

	private readonly List<(string Key, int Default, Slider Slider)> cardRumble = new List<(string, int, Slider)>();

	private string iniPath = Path.Combine(GamePaths.GameRoot, "segatools.ini");

	private bool IsDualSenseMode => InputModeSelector.SelectedIndex == 2;

	public event EventHandler? SaveRequested;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder value, uint size, string file);

	private string ReadIni(string section, string key, string defaultValue = "")
	{
		StringBuilder stringBuilder = new StringBuilder(1024);
		GetPrivateProfileString(section, key, defaultValue, stringBuilder, (uint)stringBuilder.Capacity, iniPath);
		return stringBuilder.ToString();
	}

	private void InitializeDualSense()
	{
		(string, string, int)[] physicalButtons = PhysicalButtons;
		for (int i = 0; i < physicalButtons.Length; i++)
		{
			(string, string, int) tuple = physicalButtons[i];
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(140.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.Children.Add(new TextBlock
			{
				Text = tuple.Item1,
				VerticalAlignment = VerticalAlignment.Center
			});
			Button button = new Button
			{
				MinHeight = 34.0,
				HorizontalContentAlignment = HorizontalAlignment.Center
			};
			CalibrationBinding binding = new CalibrationBinding(tuple.Item1, tuple.Item2, tuple.Item3, button);
			button.Click += delegate
			{
				BeginCalibration(binding);
			};
			Grid.SetColumn(button, 1);
			grid.Children.Add(button);
			DualSenseCalibrationMappings.Items.Add(grid);
			dualSenseCalibration.Add(binding);
		}
		calibrationTimer.Tick += CalibrationTimer_OnTick;
		base.Unloaded += delegate
		{
			CancelCalibration();
		};
		ControllerSelector.SelectionChanged += delegate
		{
			CancelCalibration();
		};
	}

	private static List<Choice> ControllerChoices(bool dualSense)
	{
		List<Choice> list = new List<Choice>
		{
			new Choice("禁用", 0)
		};
		(string, string, int)[] physicalButtons = PhysicalButtons;
		for (int i = 0; i < physicalButtons.Length; i++)
		{
			(string, string, int) tuple = physicalButtons[i];
			bool flag = !dualSense;
			if (flag)
			{
				int item = tuple.Item3;
				bool flag2 = ((item == 1024 || item == 262144 || item == 524288) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				continue;
			}
			string text;
			if (dualSense)
			{
				(text, _, _) = tuple;
			}
			else
			{
				string text2;
				switch (tuple.Item3)
				{
				case 4096:
					text2 = "A";
					break;
				case 8192:
					text2 = "B";
					break;
				case 16384:
					text2 = "X";
					break;
				case 32768:
					text2 = "Y";
					break;
				case 256:
					text2 = "LB";
					break;
				case 512:
					text2 = "RB";
					break;
				case 65536:
					text2 = "LT";
					break;
				case 131072:
					text2 = "RT";
					break;
				case 64:
					text2 = "左摇杆按下";
					break;
				case 128:
					text2 = "右摇杆按下";
					break;
				case 16:
					text2 = "Start";
					break;
				case 32:
					text2 = "Back";
					break;
				default:
					(text2, _, _) = tuple;
					break;
				}
				text = text2;
			}
			string name = text;
			list.Add(new Choice(name, tuple.Item3));
		}
		list.Insert(5, new Choice(dualSense ? "× 或 ○" : "A 或 B", 12288));
		list.Insert(6, new Choice(dualSense ? "□ 或 △" : "X 或 Y", 49152));
		return list;
	}

	private void RefreshControllerPresentation()
	{
		if (DualSenseCalibrationPanel == null)
		{
			return;
		}
		CancelCalibration();
		bool isDualSenseMode = IsDualSenseMode;
		Expander dualSenseCalibrationPanel = DualSenseCalibrationPanel;
		Visibility visibility = (DualSenseHelp.Visibility = ((!isDualSenseMode) ? Visibility.Collapsed : Visibility.Visible));
		dualSenseCalibrationPanel.Visibility = visibility;
		ControllerMappingTitle.Text = (isDualSenseMode ? "PS5 DualSense 手柄映射" : "XInput 手柄映射");
		ControllerDeadzoneHelp.Text = "死区范围 0–32766，默认 7849。" + (isDualSenseMode ? "L2 / R2" : "LT / RT") + " 按下阈值为 64。技能按键模拟原生按下与松开；需要选人时仍使用原游戏触摸界面。冷却与封印规则不变。";
		foreach (Mapping item in mappings.Where((Mapping item) => item.Section == "xinput"))
		{
			int value = item.Value;
			List<Choice> list = ControllerChoices(isDualSenseMode);
			if (!list.Any((Choice item) => item.Value == value))
			{
				list.Add(new Choice($"自定义 0x{value:X}", value));
			}
			item.Selector.ItemsSource = list;
			item.Value = value;
		}
	}

	public void RestorePreferredInputMode(string launcherMode)
	{
		InputModeSelector.SelectedIndex = (string.Equals(launcherMode, "xinput", StringComparison.OrdinalIgnoreCase) ? ((GetPrivateProfileInt("dualsense", "enabled", 0, iniPath) != 1) ? 1 : 2) : 0);
		RefreshControllerPresentation();
	}

	private void LoadDualSenseCalibration()
	{
		foreach (CalibrationBinding item in dualSenseCalibration)
		{
			item.Value = (int)GetPrivateProfileInt("dualsense-calibration", item.Key, item.Default, iniPath);
		}
		RestorePreferredInputMode(ReadIni("io4", "mode", "keyboard"));
		RefreshCalibrationButtons();
	}

	private void SaveDualSenseBindings()
	{
		CancelCalibration();
		foreach (CalibrationBinding item in dualSenseCalibration)
		{
			Write("dualsense-calibration", item.Key, $"0x{item.Value:X}");
		}
		Write("dualsense", "enabled", IsDualSenseMode ? "1" : "0");
		if (IsDualSenseMode)
		{
			Write("fgoio", "path", "fgoio_dualsense.dll");
		}
		else if (Path.GetFileName(ReadIni("fgoio", "path")).Equals("fgoio_dualsense.dll", StringComparison.OrdinalIgnoreCase))
		{
			Write("fgoio", "path", "");
		}
	}

	private void BeginCalibration(CalibrationBinding binding)
	{
		CancelCalibration();
		pendingCalibration = binding;
		calibrationSawNeutral = false;
		calibrationDeadline = DateTime.UtcNow.AddSeconds(10.0);
		RefreshCalibrationButtons();
		StatusText.Text = "正在校正 " + binding.Label + "：请松开所有按键，再按下对应的实体键。";
		calibrationTimer.Start();
	}

	private void CalibrationTimer_OnTick(object? sender, EventArgs e)
	{
		if (pendingCalibration == null)
		{
			calibrationTimer.Stop();
			return;
		}
		if (DateTime.UtcNow >= calibrationDeadline)
		{
			CancelCalibration();
			StatusText.Text = "校正超时，原按键保持不变。请确认已保存 PS5 模式并连接手柄。";
			return;
		}
		try
		{
			if (ControllerInput.GetRawButtons((uint)Math.Clamp(ControllerSelector.SelectedIndex, 0, 3), out var controls) == 0)
			{
				if (controls == 0)
				{
					calibrationSawNeutral = true;
				}
				else if (calibrationSawNeutral && (controls & (controls - 1)) == 0 && PhysicalButtons.Any(((string Label, string Key, int Mask) item) => item.Mask == controls))
				{
					CalibrationBinding calibrationBinding = pendingCalibration;
					calibrationBinding.Value = (int)controls;
					CancelCalibration();
					StatusText.Text = $"已将 {calibrationBinding.Label} 校正为实体键 {PhysicalButtonName(calibrationBinding.Value)}，点击保存后生效。";
				}
			}
		}
		catch (Exception ex) when (ControllerInput.IsLoadError(ex))
		{
			CancelCalibration();
			StatusText.Text = ControllerInput.LoadErrorMessage(ex);
		}
	}

	private static string PhysicalButtonName(int value)
	{
		return PhysicalButtons.FirstOrDefault(((string Label, string Key, int Mask) item) => item.Mask == value).Label ?? $"0x{value:X}";
	}

	private void RefreshCalibrationButtons()
	{
		foreach (CalibrationBinding item in dualSenseCalibration)
		{
			item.Button.Content = ((pendingCalibration == item) ? "松开按键，再按实体键…" : ("当前实体键：" + PhysicalButtonName(item.Value)));
		}
	}

	private void CancelCalibration()
	{
		pendingCalibration = null;
		calibrationTimer.Stop();
		RefreshCalibrationButtons();
	}

	private void ResetDualSenseCalibration()
	{
		CancelCalibration();
		foreach (CalibrationBinding item in dualSenseCalibration)
		{
			item.Value = item.Default;
		}
		RefreshCalibrationButtons();
	}

	private void ResetCalibration_OnClick(object sender, RoutedEventArgs e)
	{
		ResetDualSenseCalibration();
		StatusText.Text = "已复位默认实体按键，点击保存后生效。";
	}

	private static string MissingControllerMessage(bool ds)
	{
		if (!ds)
		{
			return "未找到该序号的 XInput 手柄，请检查连接与手柄序号。";
		}
		return "未找到该序号的 DualSense，请检查 USB/蓝牙连接并先保存 PS5 手柄模式。";
	}

	private void DetectController_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			uint num = (uint)Math.Clamp(ControllerSelector.SelectedIndex, 0, 3);
			uint controls;
			ControllerInput.State state;
			uint num2 = (IsDualSenseMode ? ControllerInput.GetRawButtons(num, out controls) : ControllerInput.GetState(dualSense: false, num, out state));
			StatusText.Text = num2 switch
			{
				1167u => MissingControllerMessage(IsDualSenseMode), 
				0u => $"已检测到第 {num + 1} 个 {(IsDualSenseMode ? "DualSense" : "XInput")} 手柄。", 
				_ => $"检测手柄失败：{num2}。", 
			};
		}
		catch (Exception ex) when (ControllerInput.IsLoadError(ex))
		{
			StatusText.Text = ControllerInput.LoadErrorMessage(ex);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetPrivateProfileInt(string section, string key, int defaultValue, string file);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool WritePrivateProfileString(string section, string key, string value, string file);

	public unsafe ControlSettingsView()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		//IL_02f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fb: Unknown result type (might be due to invalid IL or missing references)
		InitializeComponent();
		(string, string, int)[] array = new(string, string, int)[4]
		{
			("Quick", "rumbleQuick", 35),
			("Arts", "rumbleArts", 55),
			("Buster", "rumbleBuster", 85),
			("Extra", "rumbleExtra", 100)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, int) tuple = array[i];
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(3.0, 3.0, 0.0, 3.0)
			};
			Slider slider = new Slider
			{
				Minimum = 0.0,
				Maximum = 100.0,
				Value = tuple.Item3,
				Width = 220.0,
				IsSnapToTickEnabled = true,
				TickFrequency = 1.0,
				VerticalAlignment = VerticalAlignment.Center
			};
			stackPanel.Children.Add(new TextBlock
			{
				Text = tuple.Item1,
				Width = 85.0,
				VerticalAlignment = VerticalAlignment.Center
			});
			stackPanel.Children.Add(slider);
			TextBlock textBlock = new TextBlock
			{
				Width = 55.0,
				Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			textBlock.SetBinding(TextBlock.TextProperty, new Binding("Value")
			{
				Source = slider,
				StringFormat = "{0:0}%"
			});
			stackPanel.Children.Add(textBlock);
			CardRumbleSettings.Items.Add(stackPanel);
			cardRumble.Add((tuple.Item2, tuple.Item3, slider));
		}
		List<Choice> list = new List<Choice>
		{
			new Choice("禁用", 0),
			new Choice("鼠标左键", 1),
			new Choice("鼠标右键", 2),
			new Choice("鼠标中键", 4),
			new Choice("鼠标侧键 1", 5),
			new Choice("鼠标侧键 2", 6)
		};
		for (int j = 8; j <= 254; j++)
		{
			Key val = KeyInterop.KeyFromVirtualKey(j);
			if ((int)val != 0 && KeyInterop.VirtualKeyFromKey(val) == j)
			{
				list.Add(new Choice(((object)(*(Key*)(&val))/*cast due to constrained. prefix*/).ToString(), j));
			}
		}
		array = new(string, string, int)[12]
		{
			("向上", "up", 87),
			("向下", "down", 83),
			("向左", "left", 65),
			("向右", "right", 68),
			("攻击", "attack", 2),
			("冲刺", "dash", 160),
			("切换目标", "target", 70),
			("宝具", "np", 32),
			("镜头归中", "camera", 67),
			("从者技能 1", "skill1", 49),
			("从者技能 2", "skill2", 50),
			("从者技能 3", "skill3", 51)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, int) tuple2 = array[i];
			AddMapping(KeyboardMappings, tuple2.Item1, "keyboard", tuple2.Item2, tuple2.Item3, list);
		}
		List<Choice> choices = new List<Choice>
		{
			new Choice("禁用", 0),
			new Choice("A", 4096),
			new Choice("B", 8192),
			new Choice("X", 16384),
			new Choice("Y", 32768),
			new Choice("A 或 B", 12288),
			new Choice("X 或 Y", 49152),
			new Choice("LB", 256),
			new Choice("RB", 512),
			new Choice("LT", 65536),
			new Choice("RT", 131072),
			new Choice("左摇杆按下", 64),
			new Choice("右摇杆按下", 128),
			new Choice("Start", 16),
			new Choice("Back", 32),
			new Choice("十字键上", 1),
			new Choice("十字键下", 2),
			new Choice("十字键左", 4),
			new Choice("十字键右", 8)
		};
		array = new(string, string, int)[8]
		{
			("攻击", "attack", 12288),
			("冲刺", "dash", 65536),
			("切换目标", "target", 256),
			("宝具", "np", 49152),
			("镜头归中", "camera", 64),
			("从者技能 1", "skill1", 0),
			("从者技能 2", "skill2", 0),
			("从者技能 3", "skill3", 0)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, int) tuple3 = array[i];
			AddMapping(ControllerMappings, tuple3.Item1, "xinput", tuple3.Item2, tuple3.Item3, choices);
		}
		AddMapping(CommonMappings, "测试菜单", "io4", "test", 112, list);
		AddMapping(CommonMappings, "服务键", "io4", "service", 113, list);
		AddMapping(CommonMappings, "投币", "io4", "coin", 114, list);
		AddMapping(CommonMappings, "读卡", "aime", "scan", 13, list);
		InitializeDualSense();
		LoadBindings(iniPath);
	}

	private void AddMapping(ItemsControl host, string label, string section, string key, int defaultValue, List<Choice> choices)
	{
		ComboBox comboBox = new ComboBox
		{
			ItemsSource = new List<Choice>(choices),
			DisplayMemberPath = "Name",
			SelectedValuePath = "Value",
			SelectedValue = defaultValue,
			MinWidth = 200.0
		};
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(140.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(new TextBlock
		{
			Text = label,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(3.0)
		});
		KeyBindingButton keyBindingButton = ((section == "xinput") ? null : new KeyBindingButton
		{
			Value = defaultValue
		});
		Control element = (Control)(((object)keyBindingButton) ?? ((object)comboBox));
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		host.Items.Add(grid);
		mappings.Add(new Mapping(section, key, defaultValue, comboBox, keyBindingButton));
	}

	public void LoadBindings(string file)
	{
		iniPath = Path.GetFullPath(file);
		foreach (Mapping mapping in mappings)
		{
			int value = (int)GetPrivateProfileInt(mapping.Section, mapping.Key, mapping.Default, iniPath);
			List<Choice> list = (List<Choice>)mapping.Selector.ItemsSource;
			if (!list.Any((Choice choice) => choice.Value == value))
			{
				list.Add(new Choice($"自定义 0x{value:X}", value));
			}
			mapping.Value = value;
		}
		MovementSelector.SelectedIndex = Math.Clamp((int)GetPrivateProfileInt("xinput", "movement", 0, iniPath), 0, 2);
		ControllerSelector.SelectedIndex = Math.Clamp((int)GetPrivateProfileInt("xinput", "controllerIndex", 0, iniPath), 0, 3);
		DeadzoneInput.Text = GetPrivateProfileInt("xinput", "stickDeadzone", 7849, iniPath).ToString();
		RumbleEnabled.IsChecked = GetPrivateProfileInt("xinput", "rumble", 0, iniPath) == 1;
		RumbleStrength.Value = Math.Clamp((int)GetPrivateProfileInt("xinput", "rumbleStrength", 70, iniPath), 0, 100);
		foreach (var item in cardRumble)
		{
			item.Slider.Value = Math.Clamp((int)GetPrivateProfileInt("xinput", item.Key, item.Default, iniPath), 0, 100);
		}
		LoadDualSenseCalibration();
	}

	public bool SaveBindings()
	{
		if (!int.TryParse(DeadzoneInput.Text, out var result) || result < 0 || result > 32766)
		{
			StatusText.Text = "摇杆死区必须为 0–32766 的整数。";
			return false;
		}
		if (mappings.Any((Mapping mapping) => mapping.KeyButton == null && !(mapping.Selector.SelectedValue is int)))
		{
			StatusText.Text = "请选择每个动作的按键。";
			return false;
		}
		if (IsDualSenseMode && !ControllerInput.DualSenseRuntimePresent)
		{
			StatusText.Text = "缺少 DualSense 输入组件，请重新应用完整更新包（App 内的 fgoio_dualsense.dll 和 xinput1_4.dll）。";
			return false;
		}
		try
		{
			foreach (Mapping mapping in mappings)
			{
				Write(mapping.Section, mapping.Key, $"0x{mapping.Value:X}");
			}
			Write("xinput", "stickDeadzone", result.ToString());
			Write("xinput", "movement", MovementSelector.SelectedIndex.ToString());
			Write("xinput", "controllerIndex", ControllerSelector.SelectedIndex.ToString());
			Write("xinput", "rumble", (RumbleEnabled.IsChecked == true) ? "1" : "0");
			Write("xinput", "rumbleStrength", ((int)RumbleStrength.Value).ToString());
			foreach (var item in cardRumble)
			{
				Write("xinput", item.Key, ((int)item.Slider.Value).ToString());
			}
			SaveDualSenseBindings();
			Write("io4", "mode", (InputModeSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "keyboard");
			StatusText.Text = "控制映射已保存，下次启动游戏生效。";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "保存失败：" + ex.Message;
			return false;
		}
	}

	private void Write(string section, string key, string value)
	{
		if (!WritePrivateProfileString(section, key, value, iniPath))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
	}

	private void InputMode_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		if (KeyboardPanel != null && ControllerPanel != null)
		{
			bool flag = InputModeSelector.SelectedIndex > 0;
			KeyboardPanel.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			ControllerPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			RefreshControllerPresentation();
		}
	}

	private void Defaults_OnClick(object sender, RoutedEventArgs e)
	{
		foreach (Mapping mapping in mappings)
		{
			mapping.Value = mapping.Default;
		}
		ResetDualSenseCalibration();
		ComboBox movementSelector = MovementSelector;
		int selectedIndex = (ControllerSelector.SelectedIndex = 0);
		movementSelector.SelectedIndex = selectedIndex;
		RumbleEnabled.IsChecked = false;
		RumbleStrength.Value = 70.0;
		foreach (var item in cardRumble)
		{
			item.Slider.Value = item.Default;
		}
		DeadzoneInput.Text = "7849";
		StatusText.Text = "已恢复默认映射，点击保存后生效。";
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		this.SaveRequested?.Invoke(this, EventArgs.Empty);
	}

	private async void RumbleTest_OnClick(object sender, RoutedEventArgs e)
	{
		bool dualSense = IsDualSenseMode;
		uint index = (uint)Math.Clamp(ControllerSelector.SelectedIndex, 0, 3);
		ControllerInput.Vibration vibration = new ControllerInput.Vibration
		{
			Left = (ushort)(48000.0 * RumbleStrength.Value / 100.0),
			Right = (ushort)(34000.0 * RumbleStrength.Value / 100.0)
		};
		RumbleTestButton.IsEnabled = false;
		try
		{
			uint num = ControllerInput.SetVibration(dualSense, index, ref vibration);
			StatusText.Text = num switch
			{
				1167u => MissingControllerMessage(dualSense), 
				0u => "已发送测试震动。", 
				_ => $"震动发送失败：{num}。", 
			};
			if (num == 0)
			{
				await Task.Delay(250);
			}
		}
		catch (Exception ex) when (ControllerInput.IsLoadError(ex))
		{
			StatusText.Text = ControllerInput.LoadErrorMessage(ex);
		}
		finally
		{
			vibration = default(ControllerInput.Vibration);
			try
			{
				ControllerInput.SetVibration(dualSense, index, ref vibration);
			}
			catch (Exception ex2) when (ControllerInput.IsLoadError(ex2))
			{
			}
			RumbleTestButton.IsEnabled = true;
		}
	}
}
