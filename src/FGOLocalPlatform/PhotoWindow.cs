using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public sealed class PhotoWindow : UserControl
{
	private struct MonitorInfo
	{
		public int Size;

		public NativeRect Monitor;

		public NativeRect Work;

		public uint Flags;
	}

	private struct NativeRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private readonly Window owner;

	private readonly PhotoFaceView faceView;

	private readonly PhotoBodyView bodyView;

	private readonly PhotoModelView modelView;

	private StackPanel faceHome;

	private ContentControl? faceHost;

	private ContentControl? bodyHost;

	private ContentControl? modelHost;

	private readonly string settingsPath;

	private readonly int[] keys = new int[14]
	{
		120, 87, 83, 65, 68, 81, 69, 37, 39, 38,
		40, 90, 67, 82
	};

	private readonly string[] labels = new string[14]
	{
		"Enter / Exit Photo Mode", "Move Forward", "Move Back", "Move Left", "Move Right", "Move Down", "Move Up", "Turn Left", "Turn Right", "Look Up",
		"Look Down", "Roll Left", "Roll Right", "Reset Camera"
	};

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? view;

	private readonly DispatcherTimer timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(200.0)
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private readonly Slider fov = new Slider
	{
		Minimum = 5.0,
		Maximum = 150.0,
		Value = 45.0,
		Width = 250.0
	};

	private readonly Slider speed = new Slider
	{
		Minimum = 0.05,
		Maximum = 10.0,
		Value = 1.0,
		Width = 250.0
	};

	private readonly TextBlock fovLabel = new TextBlock
	{
		Width = 65.0
	};

	private readonly TextBlock speedLabel = new TextBlock
	{
		Width = 65.0
	};

	private bool syncing;

	private readonly WrapPanel actions = new WrapPanel();

	private int? connectedPid;

	private readonly CheckBox dof = new CheckBox
	{
		Content = "Custom Depth of Field (Photo Mode Only)",
		Foreground = Brushes.White,
		Margin = new Thickness(0.0, 12.0, 0.0, 8.0)
	};

	private readonly Slider focus = new Slider
	{
		Minimum = 0.1,
		Maximum = 100.0,
		Value = 3.0,
		Width = 250.0
	};

	private readonly Slider focusRange = new Slider
	{
		Minimum = 0.0,
		Maximum = 20.0,
		Value = 0.3,
		Width = 250.0
	};

	private readonly Slider falloff = new Slider
	{
		Minimum = 0.01,
		Maximum = 50.0,
		Value = 1.0,
		Width = 250.0
	};

	private readonly Slider blur = new Slider
	{
		Minimum = 0.0,
		Maximum = 8.0,
		Value = 4.0,
		Width = 250.0
	};

	private Window? overlay;

	private IntPtr overlayOwner;

	private bool overlayDismissed;

	private bool wasActive;

	private bool shuttingDown;

	private readonly KeyBindingButton panelKey = new KeyBindingButton
	{
		Value = 119,
		MinWidth = 180.0,
		Height = 32.0
	};

	private const int PanelHotkeyId = 20552;

	private HwndSource? hotkeySource;

	private void DockFace(bool floating)
	{
		if (faceHost == null || bodyHost == null || modelHost == null)
		{
			return;
		}
		(ContentControl, FrameworkElement)[] array = new(ContentControl, FrameworkElement)[3]
		{
			(faceHost, faceView),
			(bodyHost, bodyView),
			(modelHost, modelView)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(ContentControl, FrameworkElement) tuple = array[i];
			if (floating)
			{
				if (tuple.Item1.Content != tuple.Item2)
				{
					faceHome.Children.Remove(tuple.Item2);
					tuple.Item1.Content = tuple.Item2;
				}
			}
			else
			{
				tuple.Item1.Content = null;
				if (!faceHome.Children.Contains(tuple.Item2))
				{
					faceHome.Children.Add(tuple.Item2);
				}
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	public PhotoWindow(Window owner, string configPath, int? gamePid)
	{
		this.owner = owner;
		faceView = new PhotoFaceView(Path.GetDirectoryName(configPath));
		bodyView = new PhotoBodyView(Path.GetDirectoryName(configPath), faceView);
		modelView = new PhotoModelView(faceView);
		base.Background = new SolidColorBrush(Color.FromRgb(13, 10, 16));
		base.Foreground = Brushes.White;
		Type[] array = new Type[8]
		{
			typeof(TextBox),
			typeof(Button),
			typeof(Slider),
			typeof(ScrollViewer),
			typeof(ComboBox),
			typeof(TabControl),
			typeof(TabItem),
			typeof(CheckBox)
		};
		foreach (Type type in array)
		{
			if (owner.TryFindResource(type) is Style value)
			{
				base.Resources[type] = value;
			}
		}
		ResourceDictionary resourceDictionary = new ResourceDictionary
		{
			Source = new Uri("/FGOLocalPlatform;component/PhotoControls.xaml", UriKind.Relative)
		};
		base.Resources[typeof(Slider)] = resourceDictionary[typeof(Slider)];
		settingsPath = configPath;
		(string, Slider)[] array2;
		try
		{
			JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(settingsPath))?["graphics"]?["photo"];
			if (jsonNode?["panelKey"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value2) && value2 >= 8 && value2 <= 254)
			{
				panelKey.Value = value2;
			}
			if (jsonNode?["keys"] is JsonArray jsonArray)
			{
				for (int j = 0; j < Math.Min(keys.Length, jsonArray.Count); j++)
				{
					int? num = ((jsonArray[j] is JsonValue keyValue && keyValue.TryGetValue(out int parsed)) ? parsed : null);
					if (num.HasValue)
					{
						int valueOrDefault = num.GetValueOrDefault();
						if (valueOrDefault >= 8 && valueOrDefault <= 254)
						{
							keys[j] = valueOrDefault;
						}
					}
				}
			}
			array2 = new(string, Slider)[5]
			{
				("focus", focus),
				("focusRange", focusRange),
				("falloff", falloff),
				("blur", blur),
				("speed", speed)
			};
			for (int i = 0; i < array2.Length; i++)
			{
				(string, Slider) tuple = array2[i];
				if (jsonNode?[tuple.Item1] is JsonValue jsonValue2 && jsonValue2.TryGetValue<double>(out var value3) && double.IsFinite(value3))
				{
					tuple.Item2.Value = Math.Clamp(value3, tuple.Item2.Minimum, tuple.Item2.Maximum);
				}
			}
			if (jsonNode?["dof"] is JsonValue jsonValue3 && jsonValue3.TryGetValue<bool>(out var value4))
			{
				dof.IsChecked = value4;
			}
		}
		catch (IOException)
		{
		}
		catch (JsonException)
		{
		}
		connectedPid = gamePid;
		if (gamePid.HasValue)
		{
			try
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhoto_{gamePid.Value}");
				view = mapping.CreateViewAccessor(0L, 80L);
				if (view.ReadInt32(0L) != 1347372870 || view.ReadInt32(4L) != 2)
				{
					view.Dispose();
					mapping.Dispose();
					view = null;
					mapping = null;
				}
			}
			catch (FileNotFoundException)
			{
			}
		}
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0)
		};
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Photo Mode",
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		stackPanel.Children.Add(status);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Press it once to enter photo mode and again to resume the game. Drag with the right button to pan, the left button to rotate, and scroll to zoom; hold Shift to move faster or Ctrl to move finely.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
		});
		(string, int)[] array3 = new(string, int)[3]
		{
			("Enter / Exit", 1),
			("Reset Camera", 2),
			("Resume Game", 4)
		};
		for (int i = 0; i < array3.Length; i++)
		{
			(string, int) tuple2 = array3[i];
			Button button = new Button
			{
				Content = tuple2.Item1,
				Width = 160.0,
				Height = 36.0,
				Margin = new Thickness(0.0, 0.0, 8.0, 8.0),
				IsEnabled = (view != null)
			};
			int command = tuple2.Item2;
			button.Click += delegate
			{
				Send(command);
			};
			actions.Children.Add(button);
		}
		stackPanel.Children.Add(actions);
		Button button2 = new Button
		{
			Content = "Show Photo Panel",
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		button2.Click += delegate
		{
			overlayDismissed = false;
			UpdateOverlay(view != null && view.ReadInt32(12L) != 0, explicitShow: true);
		};
		stackPanel.Children.Add(button2);
		AddSlider(stackPanel, "FOV", fov, fovLabel);
		AddSlider(stackPanel, "Move Speed", speed, speedLabel);
		fov.ValueChanged += delegate
		{
			fovLabel.Text = $"{fov.Value:F1}°";
			if (!syncing && view != null)
			{
				view.Write(48L, (float)fov.Value);
				Send(8);
			}
		};
		speed.ValueChanged += delegate
		{
			speedLabel.Text = $"{speed.Value:F2}";
			view?.Write(44L, (float)speed.Value);
		};
		stackPanel.Children.Add(dof);
		stackPanel.Children.Add(faceView);
		stackPanel.Children.Add(bodyView);
		stackPanel.Children.Add(modelView);
		faceHome = stackPanel;
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Set the focus point and the blur range; with this off the game's own depth of field is used. Leaving photo mode restores the original settings.",
			TextWrapping = TextWrapping.Wrap
		});
		array2 = new(string, Slider)[4]
		{
			("Focus Distance", focus),
			("Focus Range", focusRange),
			("Blur Falloff", falloff),
			("Blur Radius", blur)
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, Slider) tuple3 = array2[i];
			TextBlock value5 = new TextBlock
			{
				Width = 65.0,
				Text = $"{tuple3.Item2.Value:F2}"
			};
			AddSlider(stackPanel, tuple3.Item1, tuple3.Item2, value5);
			Slider slider = tuple3.Item2;
			slider.ValueChanged += delegate
			{
				value5.Text = $"{slider.Value:F2}";
				WriteDof();
			};
		}
		dof.Checked += delegate
		{
			WriteDof();
		};
		dof.Unchecked += delegate
		{
			WriteDof();
		};
		stackPanel.Children.Remove(status);
		stackPanel = new StackPanel
		{
			Margin = new Thickness(28.0),
			MaxWidth = 900.0,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Photo Mode Hotkeys",
			Style = (Style)Application.Current.Resources["SectionTitleStyle"],
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		DockPanel dockPanel = new DockPanel
		{
			Height = 34.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		dockPanel.Children.Add(new TextBlock
		{
			Text = "Show / Hide Photo Panel",
			Width = 200.0,
			Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
			VerticalAlignment = VerticalAlignment.Center
		});
		panelKey.Width = 360.0;
		panelKey.Height = 34.0;
		panelKey.Margin = new Thickness(0.0);
		panelKey.HorizontalAlignment = HorizontalAlignment.Left;
		dockPanel.Children.Add(panelKey);
		stackPanel.Children.Add(dockPanel);
		for (int num2 = 0; num2 < keys.Length; num2++)
		{
			int index = num2;
			DockPanel dockPanel2 = new DockPanel
			{
				Height = 34.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			dockPanel2.Children.Add(new TextBlock
			{
				Text = labels[num2],
				Width = 200.0,
				Foreground = (Brush)Application.Current.Resources["TextSoftBrush"],
				VerticalAlignment = VerticalAlignment.Center
			});
			Button button3 = new Button
			{
				Content = KeyLabel(keys[num2]),
				Width = 360.0,
				Height = 34.0,
				Margin = new Thickness(0.0),
				HorizontalAlignment = HorizontalAlignment.Left
			};
			bool binding = false;
			button3.Click += delegate
			{
				binding = true;
				button3.Content = "Press a key (Esc to cancel)";
				button3.Focus();
			};
			button3.LostKeyboardFocus += delegate
			{
				binding = false;
				button3.Content = KeyLabel(keys[index]);
			};
			button3.PreviewKeyDown += delegate(object _, KeyEventArgs e)
			{
				if (binding)
				{
					e.Handled = true;
					Key val = ((e.Key == Key.System) ? e.SystemKey : e.Key);
					if ((uint)(val - 70) > 1u && (uint)(val - 116) > 5u)
					{
						if ((int)val != 13)
						{
							keys[index] = KeyInterop.VirtualKeyFromKey(val);
						}
						binding = false;
						button3.Content = KeyLabel(keys[index]);
					}
				}
			};
			dockPanel2.Children.Add(button3);
			stackPanel.Children.Add(dockPanel2);
		}
		Button button4 = new Button
		{
			Content = "Save Hotkeys",
			HorizontalAlignment = HorizontalAlignment.Left,
			Padding = new Thickness(20.0, 8.0, 20.0, 8.0),
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		button4.Click += delegate
		{
			Save();
		};
		stackPanel.Children.Add(button4);
		stackPanel.Children.Add(status);
		WriteDof();
		timer.Tick += delegate
		{
			Refresh();
		};
		timer.Start();
		Refresh();
		hotkeySource = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
		hotkeySource?.AddHook(PanelHotkey);
		RegisterPanelHotkey();
		owner.Closed += delegate
		{
			if (hotkeySource != null)
			{
				UnregisterHotKey(hotkeySource.Handle, 20552);
				hotkeySource.RemoveHook(PanelHotkey);
			}
			shuttingDown = true;
			timer.Stop();
			overlay?.Close();
			Send(4);
			faceView.Dispose();
			bodyView.Dispose();
			modelView.Dispose();
			view?.Dispose();
			mapping?.Dispose();
		};
	}

	private bool RegisterPanelHotkey()
	{
		if (hotkeySource == null)
		{
			return false;
		}
		UnregisterHotKey(hotkeySource.Handle, 20552);
		bool flag = panelKey.Value >= 8 && RegisterHotKey(hotkeySource.Handle, 20552, 16384u, (uint)panelKey.Value);
		panelKey.ToolTip = (flag ? "Shows or hides the panel during photo mode; it works as soon as you save." : "This key is unavailable or already in use - pick another one and save. You can also click Show Photo Panel.");
		return flag;
	}

	private IntPtr PanelHotkey(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != 786 || wParam.ToInt32() != 20552)
		{
			return IntPtr.Zero;
		}
		GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
		if (view == null || view.ReadInt32(12L) == 0 || (pid != (uint)connectedPid.GetValueOrDefault() && pid != (uint)Environment.ProcessId))
		{
			return IntPtr.Zero;
		}
		overlayDismissed = !overlayDismissed;
		if (overlayDismissed)
		{
			DockFace(floating: false);
			overlay?.Hide();
		}
		else
		{
			UpdateOverlay(active: true, explicitShow: true);
		}
		handled = true;
		return IntPtr.Zero;
	}

	private static string KeyLabel(int key)
	{
		return key switch
		{
			32 => "Space", 
			13 => "Enter", 
			_ => ((object)KeyInterop.KeyFromVirtualKey(key)/*cast due to constrained. prefix*/).ToString(), 
		};
	}

	private static void AddSlider(Panel panel, string label, Slider slider, TextBlock value)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = label,
			Width = 130.0
		});
		stackPanel.Children.Add(slider);
		stackPanel.Children.Add(PhotoNumberInput.Create(slider));
		panel.Children.Add(stackPanel);
	}

	private void Send(int command)
	{
		view?.Write(8L, command);
	}

	private void Refresh()
	{
		if (connectedPid.HasValue)
		{
			try
			{
				using Process process = Process.GetProcessById(connectedPid.Value);
				if (process.HasExited)
				{
					Disconnect();
				}
			}
			catch (ArgumentException)
			{
				Disconnect();
			}
		}
		if (view == null)
		{
			Process[] processesByName = Process.GetProcessesByName("ago");
			foreach (Process process2 in processesByName)
			{
				using (process2)
				{
					try
					{
						if (string.Equals(process2.MainModule?.FileName, Path.Combine(Path.GetDirectoryName(settingsPath), "ago.exe"), StringComparison.OrdinalIgnoreCase))
						{
							mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhoto_{process2.Id}");
							view = mapping.CreateViewAccessor(0L, 80L);
							if (view.ReadInt32(0L) == 1347372870 && view.ReadInt32(4L) == 2)
							{
								connectedPid = process2.Id;
								view.Write(44L, (float)speed.Value);
								WriteDof();
								break;
							}
							Disconnect();
						}
					}
					catch (FileNotFoundException)
					{
						Disconnect();
					}
					catch (Win32Exception)
					{
					}
					catch (InvalidOperationException)
					{
					}
				}
			}
		}
		foreach (Button child in actions.Children)
		{
			child.IsEnabled = view != null;
		}
		if (view == null)
		{
			faceView.Refresh(null, active: false);
			bodyView.Refresh(null, active: false);
			modelView.Refresh(null, active: false);
			UpdateOverlay(active: false);
			status.Text = "The game is not running - you can still set the keys, then save and start the game.";
			return;
		}
		bool flag = view.ReadInt32(12L) != 0;
		faceView.Refresh(connectedPid, flag);
		bodyView.Refresh(connectedPid, flag);
		modelView.Refresh(connectedPid, flag);
		UpdateOverlay(flag);
		status.Text = (flag ? $"In photo mode - whether the battle timer and Noble Phantasms pause is still unconfirmed\nPosition {view.ReadSingle(32L):F2}, {view.ReadSingle(36L):F2}, {view.ReadSingle(40L):F2}" : "Connected to the game - photo mode off");
		if (flag && !fov.IsMouseCaptureWithin && (view.ReadInt32(8L) & 8) == 0)
		{
			syncing = true;
			fov.Value = view.ReadSingle(16L);
			syncing = false;
		}
		fovLabel.Text = $"{fov.Value:F1}°";
		speedLabel.Text = $"{speed.Value:F2}";
	}

	private void UpdateOverlay(bool active, bool explicitShow = false)
	{
		if (!active)
		{
			overlay?.Hide();
			DockFace(floating: false);
			overlayDismissed = false;
			wasActive = false;
			return;
		}
		if (!wasActive)
		{
			overlayDismissed = false;
		}
		wasActive = true;
		if (overlayDismissed || !connectedPid.HasValue)
		{
			return;
		}
		try
		{
			using Process process = Process.GetProcessById(connectedPid.Value);
			IntPtr mainWindowHandle = process.MainWindowHandle;
			if (mainWindowHandle == IntPtr.Zero)
			{
				return;
			}
			IntPtr foregroundWindow = GetForegroundWindow();
			GetWindowThreadProcessId(foregroundWindow, out var pid);
			if (!explicitShow && pid != (uint)Environment.ProcessId && pid != (uint)connectedPid.Value && (overlay == null || foregroundWindow != new WindowInteropHelper(overlay).Handle))
			{
				overlay?.Hide();
				DockFace(floating: false);
				return;
			}
			if (overlay != null && overlayOwner != mainWindowHandle)
			{
				CloseOverlay();
			}
			if (overlay == null)
			{
				CreateOverlay(mainWindowHandle);
			}
			DockFace(floating: true);
			bool flag = !overlay.IsVisible;
			if (flag)
			{
				overlay.Show();
			}
			FitOverlay(mainWindowHandle, flag);
		}
		catch (ArgumentException)
		{
			overlay?.Hide();
		}
		catch (InvalidOperationException)
		{
			overlay?.Hide();
		}
		catch (Win32Exception)
		{
			overlay?.Hide();
		}
	}

	private void FitOverlay(IntPtr gameWindow, bool opening)
	{
		if (overlay == null)
		{
			return;
		}
		MonitorInfo info = new MonitorInfo
		{
			Size = Marshal.SizeOf<MonitorInfo>()
		};
		if (GetMonitorInfo(MonitorFromWindow(gameWindow, 2u), ref info) && GetWindowRect(gameWindow, out var rect))
		{
			IntPtr handle = new WindowInteropHelper(overlay).Handle;
			double num = (double)Math.Max(96u, GetDpiForWindow(handle)) / 96.0;
			NativeRect work = info.Work;
			int num2 = Math.Max(work.Left, rect.Left) + 8;
			int num3 = Math.Max(work.Top, rect.Top) + 8;
			int num4 = Math.Min(work.Right, rect.Right) - 8;
			int num5 = Math.Min(work.Bottom, rect.Bottom) - 8;
			if (num4 <= num2 || num5 <= num3)
			{
				num2 = work.Left;
				num3 = work.Top;
				num4 = work.Right;
				num5 = work.Bottom;
			}
			overlay.Width = Math.Min(540.0, (double)(num4 - num2) / num);
			overlay.Height = Math.Min(720.0, (double)(num5 - num3) / num);
			overlay.UpdateLayout();
			if (GetWindowRect(handle, out var rect2))
			{
				int num6 = rect2.Right - rect2.Left;
				int num7 = rect2.Bottom - rect2.Top;
				int value = (opening ? (num4 - num6) : rect2.Left);
				int value2 = (opening ? num3 : rect2.Top);
				value = Math.Clamp(value, num2, Math.Max(num2, num4 - num6));
				value2 = Math.Clamp(value2, num3, Math.Max(num3, num5 - num7));
				SetWindowPos(handle, IntPtr.Zero, value, value2, 0, 0, 21u);
			}
		}
	}

	private void CreateOverlay(IntPtr gameWindow)
	{
		overlayOwner = gameWindow;
		overlay = new Window
		{
			Title = "Photo Mode - Depth of Field and Character Controls",
			Width = 540.0,
			Height = 720.0,
			Icon = new BitmapImage(new Uri("pack://application:,,,/FGOLocalPlatform;component/Platform.ico")),
			ResizeMode = ResizeMode.NoResize,
			WindowStyle = WindowStyle.None,
			ShowInTaskbar = false,
			ShowActivated = false,
			Topmost = true,
			Background = base.Background,
			Foreground = base.Foreground,
			FontFamily = owner.FontFamily,
			FontSize = 14.0,
			UseLayoutRounding = true,
			SnapsToDevicePixels = true
		};
		new WindowInteropHelper(overlay).Owner = gameWindow;
		Type[] array = new Type[8]
		{
			typeof(TextBox),
			typeof(Button),
			typeof(Slider),
			typeof(ComboBox),
			typeof(TabControl),
			typeof(TabItem),
			typeof(CheckBox),
			typeof(ScrollViewer)
		};
		foreach (Type type in array)
		{
			if (TryFindResource(type) is Style value)
			{
				overlay.Resources[type] = value;
			}
		}
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(14.0)
		};
		DockPanel dockPanel = new DockPanel();
		overlay.Content = new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(90, 51, 83)),
			BorderThickness = new Thickness(1.0),
			Child = dockPanel
		};
		DockPanel dockPanel2 = new DockPanel
		{
			Background = new SolidColorBrush(Color.FromRgb(29, 20, 31)),
			LastChildFill = true
		};
		DockPanel.SetDock(dockPanel2, Dock.Top);
		dockPanel.Children.Add(dockPanel2);
		Button button = new Button
		{
			Content = "Hide",
			Width = 64.0,
			Height = 32.0,
			Margin = new Thickness(4.0)
		};
		DockPanel.SetDock(button, Dock.Right);
		dockPanel2.Children.Add(button);
		button.Click += delegate
		{
			overlayDismissed = true;
			DockFace(floating: false);
			overlay?.Hide();
		};
		TextBlock textBlock = new TextBlock
		{
			Text = "Photo Controls",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Padding = new Thickness(14.0, 10.0, 8.0, 10.0)
		};
		textBlock.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.ButtonState == MouseButtonState.Pressed)
			{
				overlay?.DragMove();
			}
		};
		dockPanel2.Children.Add(textBlock);
		TabControl tabControl = new TabControl
		{
			Background = base.Background,
			BorderBrush = new SolidColorBrush(Color.FromRgb(64, 53, 69)),
			Margin = new Thickness(10.0)
		};
		dockPanel.Children.Add(tabControl);
		tabControl.Items.Add(new TabItem
		{
			Header = "Camera & Depth of Field",
			Content = Scroller(stackPanel)
		});
		StackPanel characterPanel = new StackPanel
		{
			Margin = new Thickness(10.0)
		};
		faceHost = AddSection();
		bodyHost = AddSection();
		modelHost = AddSection();
		tabControl.Items.Add(new TabItem
		{
			Header = "Character Controls",
			Content = Scroller(characterPanel)
		});
		DockFace(floating: true);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Camera & Depth of Field",
			FontSize = 20.0,
			FontWeight = FontWeights.Bold
		});
		CheckBox checkBox = new CheckBox
		{
			Content = "Custom Depth of Field",
			Foreground = Brushes.White,
			Margin = new Thickness(0.0, 12.0, 0.0, 8.0)
		};
		checkBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsChecked")
		{
			Source = dof,
			Mode = BindingMode.TwoWay
		});
		stackPanel.Children.Add(checkBox);
		(string, Slider)[] array2 = new(string, Slider)[6]
		{
			("FOV", fov),
			("Move Speed", speed),
			("Focus Distance", focus),
			("Focus Range", focusRange),
			("Blur Falloff", falloff),
			("Blur Radius", blur)
		};
		for (int i = 0; i < array2.Length; i++)
		{
			(string, Slider) tuple = array2[i];
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 7.0, 0.0, 7.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(110.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = tuple.Item1,
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider = new Slider
			{
				Minimum = tuple.Item2.Minimum,
				Maximum = tuple.Item2.Maximum,
				Margin = new Thickness(4.0, 0.0, 8.0, 0.0)
			};
			slider.SetBinding(RangeBase.ValueProperty, new Binding("Value")
			{
				Source = tuple.Item2,
				Mode = BindingMode.TwoWay
			});
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			stackPanel.Children.Add(grid);
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Left button rotates - right button pans - scroll wheel zooms",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.LightSteelBlue,
			Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
		});
		WrapPanel wrapPanel = new WrapPanel();
		stackPanel.Children.Add(wrapPanel);
		(string, int)[] array3 = new(string, int)[2]
		{
			("Save Settings", 0),
			("Resume Game", 4)
		};
		for (int i = 0; i < array3.Length; i++)
		{
			(string, int) tuple2 = array3[i];
			Button button2 = new Button
			{
				Content = tuple2.Item1,
				Width = 160.0,
				Height = 34.0,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			int command = tuple2.Item2;
			button2.Click += delegate
			{
				if (command == 0)
				{
					Save();
				}
				else
				{
					Send(command);
				}
			};
			wrapPanel.Children.Add(button2);
		}
		overlay.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (KeyInterop.VirtualKeyFromKey((e.Key == Key.System) ? e.SystemKey : e.Key) == keys[0])
			{
				Send(1);
				e.Handled = true;
			}
		};
		Window created = overlay;
		created.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (!shuttingDown && overlay == created)
			{
				e.Cancel = true;
				overlayDismissed = true;
				DockFace(floating: false);
				created.Hide();
			}
		};
		ContentControl AddSection()
		{
			ContentControl contentControl = new ContentControl
			{
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			characterPanel.Children.Add(new Border
			{
				Child = contentControl,
				Padding = new Thickness(12.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
				CornerRadius = new CornerRadius(6.0),
				Background = new SolidColorBrush(Color.FromRgb(23, 18, 27)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(64, 53, 69)),
				BorderThickness = new Thickness(1.0)
			});
			return contentControl;
		}
		static ScrollViewer Scroller(UIElement content)
		{
			return new ScrollViewer
			{
				Content = content,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
			};
		}
	}

	private void CloseOverlay()
	{
		DockFace(floating: false);
		faceHost = null;
		bodyHost = null;
		modelHost = null;
		Window? window = overlay;
		overlay = null;
		overlayOwner = IntPtr.Zero;
		window?.Close();
	}

	private void Disconnect()
	{
		CloseOverlay();
		wasActive = false;
		overlayDismissed = false;
		view?.Dispose();
		mapping?.Dispose();
		view = null;
		mapping = null;
		connectedPid = null;
	}

	private void WriteDof()
	{
		if (view != null)
		{
			view.Write(56L, (float)focus.Value);
			view.Write(60L, (float)focusRange.Value);
			view.Write(64L, (float)falloff.Value);
			view.Write(68L, (float)blur.Value);
			view.Write(52L, (dof.IsChecked == true) ? 1 : 0);
		}
	}

	private void Save()
	{
		if (keys.Distinct().Count() != keys.Length || keys.Contains(panelKey.Value) || panelKey.Value < 8)
		{
			ThemedMessageBox.Show(owner, "Two photo keys are the same, or the panel hotkey is not valid - change them and save again.");
			return;
		}
		try
		{
			JsonObject jsonObject = (JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject) ?? new JsonObject();
			JsonObject jsonObject2 = jsonObject["graphics"] as JsonObject;
			if (jsonObject2 == null)
			{
				jsonObject2 = (JsonObject)(jsonObject["graphics"] = new JsonObject());
			}
			JsonObject jsonObject3 = jsonObject2["photo"] as JsonObject;
			if (jsonObject3 == null)
			{
				jsonObject3 = (JsonObject)(jsonObject2["photo"] = new JsonObject());
			}
			JsonObject jsonObject4 = jsonObject3;
			JsonNode[] items = keys.Select((int v) => JsonValue.Create(v)).ToArray();
			jsonObject4["keys"] = new JsonArray(items);
			jsonObject3["panelKey"] = panelKey.Value;
			jsonObject3["dof"] = dof.IsChecked == true;
			jsonObject3["focus"] = focus.Value;
			jsonObject3["focusRange"] = focusRange.Value;
			jsonObject3["falloff"] = falloff.Value;
			jsonObject3["blur"] = blur.Value;
			jsonObject3["speed"] = speed.Value;
			AtomicFile.WriteAllText(settingsPath, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
			status.Text = "Photo settings saved - the panel hotkey and the depth of field work now, and the in-game camera keys work after you restart the game.";
			if (!RegisterPanelHotkey())
			{
				ThemedMessageBox.Show(owner, "The panel hotkey is unavailable or already in use - pick another one and save. You can still open the panel with the Show Photo Panel button.");
			}
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(owner, "Could not save the photo settings: " + ex.Message);
		}
	}
}
