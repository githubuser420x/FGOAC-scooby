using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

public sealed class PhotoModelView : StackPanel, IDisposable
{
	private sealed record Saved(double[] Offset, double[] Rotation, bool Weapons, bool Head, bool ManualHead, double[] HeadAngles, int[] Modes, int[] States);

	private readonly PhotoFaceView selection;

	private readonly Slider[] offsets = new Slider[3];

	private readonly Slider[] rotations = new Slider[3];

	private readonly Slider[] headAngles = new Slider[3];

	private readonly CheckBox manualHead = new CheckBox
	{
		Content = "Custom Head Rotation",
		Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
	};

	private readonly CheckBox weapons = new CheckBox
	{
		Content = "Show Weapons",
		Foreground = Brushes.White,
		IsChecked = true,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly CheckBox head = new CheckBox
	{
		Content = "Head Follows Camera",
		Foreground = Brushes.White,
		Margin = new Thickness(0.0, 10.0, 0.0, 10.0)
	};

	private readonly ComboBox weaponChoice = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponPosition = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0),
		MaxDropDownHeight = 220.0
	};

	private readonly ComboBox weaponMode = new ComboBox
	{
		Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
	};

	private readonly int[] weaponModes = new int[10];

	private readonly int[] weaponStates = new int[10];

	private bool syncingWeapons;

	private bool loadingActor;

	private readonly Dictionary<ulong, Saved> saved = new Dictionary<ulong, Saved>();

	private string weaponSignature = "";

	private int weaponMask = -1;

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private ulong actor;

	private bool dirty = true;

	private static int Choice(ComboBox box)
	{
		if (box.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int)
		{
			return (int)tag;
		}
		return 0;
	}

	public PhotoModelView(PhotoFaceView selection)
	{
		this.selection = selection;
		base.Children.Add(new TextBlock
		{
			Text = "Model and Weapons",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Moves the character picked in Face Animation above along the scene axes. The camera does not move.",
			TextWrapping = TextWrapping.Wrap
		});
		for (int i = 0; i < 3; i++)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X Side", "Y Height", "Z Depth" })[i]
			});
			Slider slider = new Slider
			{
				Minimum = -20.0,
				Maximum = 20.0,
				SmallChange = 0.01,
				LargeChange = 0.1
			};
			offsets[i] = slider;
			Grid.SetColumn(slider, 1);
			grid.Children.Add(slider);
			TextBox element = PhotoNumberInput.Create(slider);
			Grid.SetColumn(element, 2);
			grid.Children.Add(element);
			slider.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid);
		}
		Button button = new Button
		{
			Content = "Reset Position",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			Slider[] array2 = offsets;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button);
		base.Children.Add(new TextBlock
		{
			Text = "Model Rotation (degrees)",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Turns the model about its own position on the scene X, Y and Z axes; 0° is the original facing.",
			TextWrapping = TextWrapping.Wrap
		});
		for (int num = 0; num < 3; num++)
		{
			Grid grid2 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid2.ColumnDefinitions.Add(new ColumnDefinition());
			grid2.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid2.Children.Add(new TextBlock
			{
				Text = (new string[3] { "X Pitch", "Y Yaw", "Z Roll" })[num],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider2 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			rotations[num] = slider2;
			Grid.SetColumn(slider2, 1);
			grid2.Children.Add(slider2);
			TextBox element2 = PhotoNumberInput.Create(slider2);
			Grid.SetColumn(element2, 2);
			grid2.Children.Add(element2);
			slider2.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid2);
		}
		Button button2 = new Button
		{
			Content = "Reset Rotation",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button2.Click += delegate
		{
			Slider[] array2 = rotations;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button2);
		base.Children.Add(weapons);
		base.Children.Add(new TextBlock
		{
			Text = "Shows the weapons set below; turn this off to hide them all.",
			TextWrapping = TextWrapping.Wrap
		});
		base.Children.Add(status);
		weapons.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		weapons.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(head);
		base.Children.Add(new TextBlock
		{
			Text = "Turns the head on top of the current motion and eases it back to center once the camera passes the character's limit. The eyes are not affected.",
			TextWrapping = TextWrapping.Wrap
		});
		head.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		head.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		base.Children.Add(manualHead);
		base.Children.Add(new TextBlock
		{
			Text = "Uses the same limits and smoothing as Head Follows Camera. With follow on these fine-tune the local X, Y and Z axes; with follow off they aim the head on their own. Values are degrees, and 0° adds no offset.",
			TextWrapping = TextWrapping.Wrap
		});
		manualHead.Checked += delegate
		{
			dirty = true;
			Publish();
		};
		manualHead.Unchecked += delegate
		{
			dirty = true;
			Publish();
		};
		for (int num2 = 0; num2 < 3; num2++)
		{
			Grid grid3 = new Grid
			{
				Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
			};
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(65.0)
			});
			grid3.ColumnDefinitions.Add(new ColumnDefinition());
			grid3.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(90.0)
			});
			grid3.Children.Add(new TextBlock
			{
				Text = (new string[3] { "Head X", "Head Y", "Head Z" })[num2],
				VerticalAlignment = VerticalAlignment.Center
			});
			Slider slider3 = new Slider
			{
				Minimum = -180.0,
				Maximum = 180.0,
				SmallChange = 0.1,
				LargeChange = 5.0
			};
			headAngles[num2] = slider3;
			Grid.SetColumn(slider3, 1);
			grid3.Children.Add(slider3);
			TextBox element3 = PhotoNumberInput.Create(slider3);
			Grid.SetColumn(element3, 2);
			grid3.Children.Add(element3);
			slider3.ValueChanged += delegate
			{
				dirty = true;
				Publish();
			};
			base.Children.Add(grid3);
		}
		Button button3 = new Button
		{
			Content = "Reset Head Rotation",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button3.Click += delegate
		{
			Slider[] array2 = headAngles;
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j].Value = 0.0;
			}
			dirty = true;
			Publish();
		};
		base.Children.Add(button3);
		base.Children.Add(new TextBlock
		{
			Text = "Weapon Switching",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		});
		weaponChoice.Items.Add(new ComboBoxItem
		{
			Content = "Select a weapon to adjust",
			Tag = 0
		});
		weaponChoice.SelectedIndex = 0;
		weaponPosition.Items.Add(new ComboBoxItem
		{
			Content = "Original attach point",
			Tag = 0
		});
		weaponPosition.SelectedIndex = 0;
		string[] array = new string[3] { "Leave as is", "Show", "Hide" };
		foreach (string newItem in array)
		{
			weaponMode.Items.Add(newItem);
		}
		weaponMode.SelectedIndex = 0;
		weaponMode.IsEnabled = false;
		weaponPosition.IsEnabled = false;
		base.Children.Add(weaponChoice);
		base.Children.Add(weaponMode);
		base.Children.Add(weaponPosition);
		Button button4 = new Button
		{
			Content = "Restore Original Weapons",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button4.Click += delegate
		{
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			weapons.IsChecked = true;
			LoadWeaponEditor();
			dirty = true;
			Publish();
		};
		base.Children.Add(button4);
		base.Children.Add(new TextBlock
		{
			Text = "Each weapon is set on its own and several can show at once; leaving photo mode restores them.",
			TextWrapping = TextWrapping.Wrap
		});
		weaponChoice.SelectionChanged += delegate
		{
			if (!syncingWeapons)
			{
				LoadWeaponEditor();
				dirty = true;
				Publish();
			}
		};
		weaponMode.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponModes[num4 - 1] = weaponMode.SelectedIndex;
				if (weaponMode.SelectedIndex == 1)
				{
					weapons.IsChecked = true;
				}
				dirty = true;
				Publish();
			}
		};
		weaponPosition.SelectionChanged += delegate
		{
			int num4 = Choice(weaponChoice);
			if (!syncingWeapons && num4 > 0)
			{
				weaponStates[num4 - 1] = Choice(weaponPosition);
				weaponModes[num4 - 1] = 1;
				weaponMode.SelectedIndex = 1;
				weapons.IsChecked = true;
				dirty = true;
				Publish();
			}
		};
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		base.IsEnabled = active && selection.SelectedActorId != 0;
		if (!active)
		{
			saved.Clear();
			actor = 0uL;
			Array.Clear(weaponModes);
			Array.Clear(weaponStates);
			Slider[] array = offsets;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			array = rotations;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			weapons.IsChecked = true;
			head.IsChecked = false;
			manualHead.IsChecked = false;
			array = headAngles;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Value = 0.0;
			}
			dirty = true;
			status.Text = "Enter photo mode, then pick a character.";
			return;
		}
		try
		{
			if (ipc == null && gamePid.HasValue)
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhotoModel_{gamePid}");
				ipc = mapping.CreateViewAccessor(0L, 856L);
				if (ipc.ReadInt32(0L) != 1297106758 || ipc.ReadInt32(4L) != 6)
				{
					Dispose();
					status.Text = "Restart the game to load the newer photo hook.";
					return;
				}
			}
			if (actor != selection.SelectedActorId)
			{
				if (ipc != null && ipc.ReadInt32(8L) != 0)
				{
					return;
				}
				if (actor != 0L)
				{
					saved[actor] = new Saved(offsets.Select((Slider s) => s.Value).ToArray(), rotations.Select((Slider s) => s.Value).ToArray(), weapons.IsChecked == true, head.IsChecked == true, manualHead.IsChecked == true, headAngles.Select((Slider s) => s.Value).ToArray(), (int[])weaponModes.Clone(), (int[])weaponStates.Clone());
				}
				loadingActor = true;
				try
				{
					actor = selection.SelectedActorId;
					saved.TryGetValue(actor, out Saved value);
					for (int num = 0; num < 3; num++)
					{
						offsets[num].Value = (((object)value != null) ? value.Offset[num] : 0.0);
						rotations[num].Value = (((object)value != null) ? value.Rotation[num] : 0.0);
					}
					for (int num2 = 0; num2 < 10; num2++)
					{
						weaponModes[num2] = (((object)value != null) ? value.Modes[num2] : 0);
						weaponStates[num2] = (((object)value != null) ? value.States[num2] : 0);
					}
					weapons.IsChecked = value?.Weapons ?? true;
					head.IsChecked = value?.Head ?? false;
					manualHead.IsChecked = value?.ManualHead ?? false;
					for (int num3 = 0; num3 < 3; num3++)
					{
						headAngles[num3].Value = (((object)value != null) ? value.HeadAngles[num3] : 0.0);
					}
					weaponChoice.SelectedIndex = 0;
					LoadWeaponEditor();
					weaponSignature = "";
					dirty = true;
				}
				finally
				{
					loadingActor = false;
				}
			}
			Publish();
			RefreshWeapons();
			TextBlock textBlock = status;
			textBlock.Text = ipc?.ReadInt32(12L) switch
			{
				1 => (weapons.IsChecked == true) ? "Weapons are shown" : "Weapons are hidden - tick Show Weapons above", 
				-1 => "That character has left the scene.", 
				-2 => "Those values are not valid.", 
				-3 => "This model cannot be edited right now.", 
				-5 => "This weapon has no original attach point - pick one of the listed positions.", 
				_ => "Pick a character.", 
			};
			if (head.IsChecked == true || manualHead.IsChecked == true)
			{
				MemoryMappedViewAccessor? memoryMappedViewAccessor = ipc;
				if (memoryMappedViewAccessor != null && memoryMappedViewAccessor.ReadInt32(44L) == -4)
				{
					status.Text = "No usable head bone or follow limit was found on this character.";
				}
			}
		}
		catch (FileNotFoundException)
		{
			status.Text = "This needs a newer game hook - restart the game.";
		}
		catch (IOException)
		{
			Dispose();
			status.Text = "The connection to the game was lost.";
		}
	}

	private void Publish()
	{
		if (!loadingActor && base.IsEnabled && dirty && ipc != null && ipc.ReadInt32(8L) == 0)
		{
			ipc.Write(16L, actor);
			for (int i = 0; i < 3; i++)
			{
				ipc.Write(24 + i * 4, (float)offsets[i].Value);
			}
			for (int j = 0; j < 3; j++)
			{
				ipc.Write(824 + j * 4, (float)rotations[j].Value);
			}
			ipc.Write(36L, (weapons.IsChecked != true) ? 1 : 0);
			ipc.Write(40L, (head.IsChecked == true) ? 1 : 0);
			ipc.Write(836L, (manualHead.IsChecked == true) ? 1 : 0);
			for (int k = 0; k < 3; k++)
			{
				ipc.Write(840 + k * 4, (float)headAngles[k].Value);
			}
			ipc.Write(48L, Choice(weaponChoice));
			ipc.Write(52L, 0);
			ipc.WriteArray(704L, weaponModes, 0, 10);
			ipc.WriteArray(744L, weaponStates, 0, 10);
			ipc.Write(8L, 1);
			dirty = false;
		}
	}

	private void LoadWeaponEditor()
	{
		syncingWeapons = true;
		try
		{
			int num = Choice(weaponChoice);
			ComboBox comboBox = weaponMode;
			bool isEnabled = (weaponPosition.IsEnabled = num > 0);
			comboBox.IsEnabled = isEnabled;
			weaponMode.SelectedIndex = ((num > 0) ? weaponModes[num - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "Original attach point",
				Tag = 0
			});
			weaponPosition.SelectedIndex = 0;
			weaponMask = -1;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	private void RefreshWeapons()
	{
		if (ipc == null || ipc.ReadInt32(8L) != 0)
		{
			return;
		}
		int num = ipc.ReadInt32(60L);
		if (num < 0 || num > 10)
		{
			return;
		}
		List<string> list = new List<string>();
		for (int i = 0; i < num; i++)
		{
			byte[] array = new byte[64];
			ipc.ReadArray(64 + i * 64, array, 0, 64);
			int num2 = Array.IndexOf(array, (byte)0);
			list.Add(Encoding.UTF8.GetString(array, 0, (num2 < 0) ? 64 : num2));
		}
		string text = string.Join("\n", list);
		int num3 = ipc.ReadInt32(56L);
		syncingWeapons = true;
		try
		{
			if (text != weaponSignature)
			{
				int num4 = Choice(weaponChoice);
				weaponChoice.Items.Clear();
				weaponChoice.Items.Add(new ComboBoxItem
				{
					Content = "Select a weapon to adjust",
					Tag = 0
				});
				for (int j = 0; j < list.Count; j++)
				{
					weaponChoice.Items.Add(new ComboBoxItem
					{
						Content = $"{j + 1} - {list[j]}",
						Tag = j + 1
					});
				}
				weaponChoice.SelectedIndex = ((num4 <= num) ? num4 : 0);
				weaponSignature = text;
				if (num4 > num)
				{
					dirty = true;
					weaponMask = -1;
				}
			}
			if (num3 == weaponMask)
			{
				return;
			}
			int num5 = Choice(weaponChoice);
			int num6 = ((num5 > 0) ? weaponStates[num5 - 1] : 0);
			weaponPosition.Items.Clear();
			weaponPosition.Items.Add(new ComboBoxItem
			{
				Content = "Original attach point",
				Tag = 0
			});
			string[] array2 = new string[14]
			{
				"", "Right Hand", "Left Hand", "", "Upper Body", "Chest", "Left Thigh", "Lower Body", "Head", "Waist",
				"Right Forearm", "Left Forearm", "Root", "Base"
			};
			for (int k = 1; k < array2.Length; k++)
			{
				if ((num3 & (1 << k)) != 0)
				{
					weaponPosition.Items.Add(new ComboBoxItem
					{
						Content = array2[k],
						Tag = k
					});
				}
			}
			weaponPosition.SelectedIndex = 0;
			foreach (ComboBoxItem item in (IEnumerable)weaponPosition.Items)
			{
				if ((int)item.Tag == num6)
				{
					weaponPosition.SelectedItem = item;
				}
			}
			if (Choice(weaponPosition) != num6 && num5 > 0)
			{
				weaponStates[num5 - 1] = 0;
				dirty = true;
			}
			weaponMask = num3;
		}
		finally
		{
			syncingWeapons = false;
		}
	}

	public void Dispose()
	{
		saved.Clear();
		ipc?.Dispose();
		mapping?.Dispose();
		ipc = null;
		mapping = null;
		actor = 0uL;
		dirty = true;
	}
}
