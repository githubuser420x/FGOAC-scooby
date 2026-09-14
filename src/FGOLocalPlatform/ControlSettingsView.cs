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
using System.Windows.Media;
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
		("D-Pad Up", "dpadUp", 1),
		("D-Pad Down", "dpadDown", 2),
		("D-Pad Left", "dpadLeft", 4),
		("D-Pad Right", "dpadRight", 8),
		("PS", "ps", 1024),
		("Touchpad Click", "touchpad", 262144),
		("Mute", "mute", 524288)
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
				Height = 34.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(200.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.Children.Add(new TextBlock
			{
				Text = tuple.Item1,
				Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
				VerticalAlignment = VerticalAlignment.Center
			});
			Button button = new Button
			{
				Width = 360.0,
				Height = 34.0,
				MinHeight = 34.0,
				Margin = new Thickness(0.0),
				HorizontalAlignment = HorizontalAlignment.Left,
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
			new Choice("Disabled", 0)
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
					text2 = "Left Stick Click";
					break;
				case 128:
					text2 = "Right Stick Click";
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
		list.Insert(5, new Choice(dualSense ? "× or ○" : "A or B", 12288));
		list.Insert(6, new Choice(dualSense ? "□ or △" : "X or Y", 49152));
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
		ControllerMappingTitle.Text = (isDualSenseMode ? "PS5 DualSense Controller Mapping" : "XInput Controller Mapping");
		ControllerDeadzoneHelp.Text = "Deadzone range is 0-32766, default 7849. " + (isDualSenseMode ? "L2 / R2" : "LT / RT") + " press threshold is 64. Skill buttons simulate a real press and release; use the game's own touch interface when you need to pick a target. Cooldown and seal rules are unchanged.";
		foreach (Mapping item in mappings.Where((Mapping item) => item.Section == "xinput"))
		{
			int value = item.Value;
			List<Choice> list = ControllerChoices(isDualSenseMode);
			if (!list.Any((Choice item) => item.Value == value))
			{
				list.Add(new Choice($"Custom 0x{value:X}", value));
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
		StatusText.Text = "Calibrating " + binding.Label + " - release every button, then press the physical button you want.";
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
			StatusText.Text = "Calibration timed out and nothing changed - save PS5 mode and connect the controller, then try again.";
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
					StatusText.Text = $"{calibrationBinding.Label} now uses the physical button {PhysicalButtonName(calibrationBinding.Value)} - click Save to apply.";
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
			item.Button.Content = ((pendingCalibration == item) ? "Release, then press the button..." : ("Current button: " + PhysicalButtonName(item.Value)));
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
		StatusText.Text = "Physical buttons reset to defaults - click Save to apply.";
	}

	private static string MissingControllerMessage(bool ds)
	{
		if (!ds)
		{
			return "No XInput controller found on that number - check the connection and the controller number.";
		}
		return "No DualSense found on that number - check the USB or Bluetooth connection and save PS5 mode first.";
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
				0u => $"Found {(IsDualSenseMode ? "DualSense" : "XInput")} controller {num + 1}.", 
				_ => $"Controller check failed with error {num2}.", 
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
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
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
				Width = 200.0,
				Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
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
			new Choice("Disabled", 0),
			new Choice("Left Mouse Button", 1),
			new Choice("Right Mouse Button", 2),
			new Choice("Middle Mouse Button", 4),
			new Choice("Mouse Button 4", 5),
			new Choice("Mouse Button 5", 6)
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
			("Move Up", "up", 87),
			("Move Down", "down", 83),
			("Move Left", "left", 65),
			("Move Right", "right", 68),
			("Attack", "attack", 2),
			("Dash", "dash", 160),
			("Switch Lock-on", "target", 70),
			("Noble Phantasm", "np", 32),
			("Center Camera", "camera", 67),
			("Servant Skill 1", "skill1", 49),
			("Servant Skill 2", "skill2", 50),
			("Servant Skill 3", "skill3", 51)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, int) tuple2 = array[i];
			AddMapping(KeyboardMappings, tuple2.Item1, "keyboard", tuple2.Item2, tuple2.Item3, list);
		}
		List<Choice> choices = new List<Choice>
		{
			new Choice("Disabled", 0),
			new Choice("A", 4096),
			new Choice("B", 8192),
			new Choice("X", 16384),
			new Choice("Y", 32768),
			new Choice("A or B", 12288),
			new Choice("X or Y", 49152),
			new Choice("LB", 256),
			new Choice("RB", 512),
			new Choice("LT", 65536),
			new Choice("RT", 131072),
			new Choice("Left Stick Click", 64),
			new Choice("Right Stick Click", 128),
			new Choice("Start", 16),
			new Choice("Back", 32),
			new Choice("D-Pad Up", 1),
			new Choice("D-Pad Down", 2),
			new Choice("D-Pad Left", 4),
			new Choice("D-Pad Right", 8)
		};
		array = new(string, string, int)[8]
		{
			("Attack", "attack", 12288),
			("Dash", "dash", 65536),
			("Switch Lock-on", "target", 256),
			("Noble Phantasm", "np", 49152),
			("Center Camera", "camera", 64),
			("Servant Skill 1", "skill1", 0),
			("Servant Skill 2", "skill2", 0),
			("Servant Skill 3", "skill3", 0)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, int) tuple3 = array[i];
			AddMapping(ControllerMappings, tuple3.Item1, "xinput", tuple3.Item2, tuple3.Item3, choices);
		}
		AddMapping(CommonMappings, "Test Menu", "io4", "test", 112, list);
		AddMapping(CommonMappings, "Service", "io4", "service", 113, list);
		AddMapping(CommonMappings, "Insert Coin", "io4", "coin", 114, list);
		AddMapping(CommonMappings, "Card Read", "aime", "scan", 13, list);
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
			Height = 34.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(200.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
			VerticalAlignment = VerticalAlignment.Center
		});
		KeyBindingButton keyBindingButton = ((section == "xinput") ? null : new KeyBindingButton
		{
			Value = defaultValue
		});
		Control element = (Control)(((object)keyBindingButton) ?? ((object)comboBox));
		element.Width = 360.0;
		element.Height = 34.0;
		element.MinHeight = 34.0;
		element.Margin = new Thickness(0.0);
		element.HorizontalAlignment = HorizontalAlignment.Left;
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
				list.Add(new Choice($"Custom 0x{value:X}", value));
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
			StatusText.Text = "Stick deadzone must be a whole number from 0 to 32766.";
			return false;
		}
		if (mappings.Any((Mapping mapping) => mapping.KeyButton == null && !(mapping.Selector.SelectedValue is int)))
		{
			StatusText.Text = "Choose a button for every action.";
			return false;
		}
		if (IsDualSenseMode && !ControllerInput.DualSenseRuntimePresent)
		{
			StatusText.Text = "The DualSense input files are missing - reinstall the full update package (fgoio_dualsense.dll and xinput1_4.dll in the App folder).";
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
			StatusText.Text = "Controls saved - they take effect the next time the game starts.";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save: " + ex.Message;
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
		StatusText.Text = "Default mapping restored - click Save to apply.";
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
				0u => "Test rumble sent.", 
				_ => $"Rumble failed with error {num}.", 
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
