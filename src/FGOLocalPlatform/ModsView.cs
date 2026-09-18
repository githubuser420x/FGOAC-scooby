using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The Mods page. It drives <see cref="ModEngine"/> and lists one row per mod. The Battle Tuner
/// tab edits the knobs the bundle mod declares, so the numbers behind a battle are set in one
/// place instead of in a stack of separate mods.
/// </summary>
public partial class ModsView : UserControl, IComponentConnector
{
	/// <summary>The one mod whose parameters the Battle Tuner tab edits.</summary>
	private const string TunerModId = "battle-tuner";

	private readonly ModEngine engine = new ModEngine();

	private readonly List<TunerControl> tunerControls = new List<TunerControl>();

	private List<ModRow> rows = new List<ModRow>();

	public ModsView()
	{
		InitializeComponent();
		Reload(reportPruned: true);
	}

	private void Reload(bool reportPruned)
	{
		try
		{
			List<string> pruned = engine.PruneMissingMods();
			List<ModStateRow> status = engine.Status();
			rows = status.Select((ModStateRow row) => new ModRow
			{
				Definition = row.Definition,
				Applied = row.Applied,
				FileCount = row.FileCount,
				StateText = row.Applied ? "Applied" : "Available"
			}).ToList();
			ModList.ItemsSource = rows;
			BuildTuner();
			int applied = rows.Count((ModRow row) => row.Applied);
			ModSummaryText.Text = rows.Count + (rows.Count == 1 ? " mod" : " mods") + " · " + applied + " applied · " + (rows.Count - applied) + " available";
			if (reportPruned && pruned.Count > 0)
			{
				SetStatus("These mods were applied but their folder is gone, so their edits were put back: " + string.Join(", ", pruned) + ".");
			}
			else if (!engine.ModsFolderExists)
			{
				SetStatus("No Mods folder was found next to App and Server. Run the English patch once, or create the folder by hand.");
			}
			else
			{
				SetStatus("Tick the mods to change, then press Apply Selected or Remove Selected.");
			}
		}
		catch (ModEngineException ex)
		{
			SetStatus(ex.Message);
		}
	}

	/// <summary>The two tabs share one engine, so they share one status line as well.</summary>
	private void SetStatus(string message)
	{
		StatusText.Text = message;
		BattleStatusText.Text = message;
	}

	// ---- Battle Tuner ------------------------------------------------------

	/// <summary>
	/// Builds one control per declared knob. The tab is read-only until the bundle is applied,
	/// because a number written while the mod is off would have nothing to attach to.
	/// </summary>
	private void BuildTuner()
	{
		tunerControls.Clear();
		TunerPanel.Children.Clear();
		ModDefinition? mod = engine.AvailableMods().FirstOrDefault((ModDefinition candidate) => string.Equals(candidate.Id, TunerModId, StringComparison.OrdinalIgnoreCase));
		if (mod == null)
		{
			SetTunerEnabled(false);
			TunerStateText.Text = "The Battle Tuner mod is not in the Mods folder, so there is nothing to tune.";
			return;
		}
		Dictionary<string, string> values = engine.ParametersFor(mod);
		foreach (ModParameter parameter in mod.Parameters)
		{
			tunerControls.Add(BuildTunerRow(parameter, values));
		}
		SetTunerEnabled(engine.IsApplied(mod.Id));
	}

	private void SetTunerEnabled(bool enabled)
	{
		TunerPanel.IsEnabled = enabled;
		TunerApplyButton.IsEnabled = enabled;
		TunerResetButton.IsEnabled = enabled;
		if (enabled)
		{
			TunerStateText.Text = "Battle Tuner is on. Change any number, then press Save & Apply to write it into the game files.";
		}
		else
		{
			TunerStateText.Text = "Battle Tuner is off. Tick Battle Tuner on the Mods tab, press Apply Selected there, then come back.";
		}
	}

	private TunerControl BuildTunerRow(ModParameter parameter, Dictionary<string, string> values)
	{
		values.TryGetValue(parameter.Id, out string? current);
		current = parameter.Normalise(current);

		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(216.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		TextBlock label = new TextBlock
		{
			Text = parameter.Label,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = Brush("TextBrush")
		};
		Grid.SetRow(label, 0);
		Grid.SetColumn(label, 0);
		grid.Children.Add(label);

		StackPanel control = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetRow(control, 0);
		Grid.SetColumn(control, 1);
		grid.Children.Add(control);

		TunerControl row;
		if (parameter.IsNumber)
		{
			row = BuildNumber(parameter, current, control);
		}
		else if (parameter.IsChoice)
		{
			row = BuildChoice(parameter, current, control);
		}
		else
		{
			row = BuildSwitch(parameter, current, control);
		}

		if (parameter.Help.Length > 0)
		{
			TextBlock help = new TextBlock
			{
				Text = parameter.Help,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 5.0, 0.0, 0.0),
				FontSize = 12.0,
				Foreground = Brush("TextSoftBrush")
			};
			Grid.SetRow(help, 1);
			Grid.SetColumn(help, 1);
			grid.Children.Add(help);
		}
		TunerPanel.Children.Add(grid);
		return row;
	}

	private TunerControl BuildNumber(ModParameter parameter, string current, StackPanel host)
	{
		double min = parameter.Min ?? 0.0;
		double max = parameter.Max ?? 1.0;
		Slider slider = new Slider
		{
			Minimum = min,
			Maximum = max,
			SmallChange = parameter.Step ?? 1.0,
			LargeChange = (parameter.Step ?? 1.0) * 5.0,
			IsSnapToTickEnabled = false,
			Width = 300.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBox box = new TextBox
		{
			Width = 96.0,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed);
		slider.Value = Math.Min(Math.Max(parsed, min), max);
		box.Text = current;
		bool syncing = false;
		slider.ValueChanged += delegate
		{
			if (!syncing)
			{
				syncing = true;
				box.Text = slider.Value.ToString("0.####", CultureInfo.InvariantCulture);
				syncing = false;
			}
		};
		box.TextChanged += delegate
		{
			if (!syncing && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed))
			{
				syncing = true;
				slider.Value = Math.Min(Math.Max(typed, min), max);
				syncing = false;
			}
		};
		host.Children.Add(slider);
		host.Children.Add(box);
		if (parameter.Unit.Length > 0)
		{
			host.Children.Add(new TextBlock
			{
				Text = parameter.Unit,
				Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = Brush("TextSoftBrush")
			});
		}
		return new TunerControl
		{
			Parameter = parameter,
			Read = () => box.Text,
			Write = delegate(string value)
			{
				string normalised = parameter.Normalise(value);
				box.Text = normalised;
				if (double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out double shown))
				{
					slider.Value = Math.Min(Math.Max(shown, min), max);
				}
			}
		};
	}

	private TunerControl BuildChoice(ModParameter parameter, string current, StackPanel host)
	{
		ComboBox combo = new ComboBox
		{
			Width = 380.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		foreach (ModParameterOption option in parameter.Options)
		{
			combo.Items.Add(new ComboBoxItem
			{
				Content = option.Label,
				Tag = option.Value
			});
		}
		combo.SelectedIndex = Math.Max(0, parameter.Options.FindIndex((ModParameterOption option) => string.Equals(option.Value, current, StringComparison.OrdinalIgnoreCase)));
		host.Children.Add(combo);
		return new TunerControl
		{
			Parameter = parameter,
			Read = delegate
			{
				return (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? parameter.DefaultText;
			},
			Write = delegate(string value)
			{
				combo.SelectedIndex = Math.Max(0, parameter.Options.FindIndex((ModParameterOption option) => string.Equals(option.Value, parameter.Normalise(value), StringComparison.OrdinalIgnoreCase)));
			}
		};
	}

	private TunerControl BuildSwitch(ModParameter parameter, string current, StackPanel host)
	{
		CheckBox toggle = new CheckBox
		{
			IsChecked = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase),
			VerticalAlignment = VerticalAlignment.Center
		};
		host.Children.Add(toggle);
		return new TunerControl
		{
			Parameter = parameter,
			Read = () => (toggle.IsChecked == true) ? "true" : "false",
			Write = delegate(string value)
			{
				toggle.IsChecked = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
			}
		};
	}

	private Dictionary<string, string> CollectTunerValues()
	{
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (TunerControl control in tunerControls)
		{
			values[control.Parameter.Id] = control.Read();
		}
		return values;
	}

	private void TunerApply_OnClick(object sender, RoutedEventArgs e)
	{
		Guard(delegate
		{
			engine.SaveParameters(TunerModId, CollectTunerValues());
			ModPlanSummary summary = engine.Apply(TunerModId);
			Reload(reportPruned: false);
			if (summary.Written == 0)
			{
				SetStatus("Those numbers are already in the game files, so nothing was rewritten.");
			}
			else
			{
				SetStatus("Battle Tuner written to " + summary.Written + " file(s).");
			}
		});
	}

	private void TunerReset_OnClick(object sender, RoutedEventArgs e)
	{
		foreach (TunerControl control in tunerControls)
		{
			control.Write(control.Parameter.DefaultText);
		}
		SetStatus("Defaults loaded into the fields. Press Save & Apply to write them.");
	}

	private Brush Brush(string key)
	{
		return (Brush)FindResource(key);
	}

	// ---- The list ----------------------------------------------------------

	private void ModCheck_OnChanged(object sender, RoutedEventArgs e)
	{
		SetStatus("Press Apply Selected to write the change.");
	}

	private void ModList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ModList.SelectedItem is ModRow row)
		{
			DetailText.Text = row.Description;
		}
	}

	private void Apply_OnClick(object sender, RoutedEventArgs e)
	{
		Change(ticked: true, apply: true);
	}

	private void Remove_OnClick(object sender, RoutedEventArgs e)
	{
		Change(ticked: true, apply: false);
	}

	private void ApplyAll_OnClick(object sender, RoutedEventArgs e)
	{
		Change(ticked: false, apply: true);
	}

	private void RemoveAll_OnClick(object sender, RoutedEventArgs e)
	{
		Change(ticked: false, apply: false);
	}

	private void Change(bool ticked, bool apply)
	{
		List<ModRow> targets = rows.Where((ModRow row) => !ticked || row.Selected).ToList();
		List<string> failures = new List<string>();
		int changed = 0;
		foreach (ModRow row in targets)
		{
			try
			{
				if (apply)
				{
					if (row.Applied)
					{
						continue;
					}
					engine.Apply(row.Definition.Id);
				}
				else
				{
					if (!row.Applied)
					{
						continue;
					}
					engine.Remove(row.Definition.Id);
				}
				changed++;
			}
			catch (ModEngineException ex)
			{
				failures.Add(row.Definition.Name + ": " + ex.Message);
				if (failures.Count >= 4)
				{
					break;
				}
			}
		}
		Reload(reportPruned: false);
		string verb = apply ? "applied" : "removed";
		SetStatus(failures.Count > 0 ? "Stopped: " + string.Join("  |  ", failures) : (changed == 0 ? "Nothing to do." : changed + " mod(s) " + verb + "."));
	}

	private void Restore_OnClick(object sender, RoutedEventArgs e)
	{
		if (ThemedMessageBox.Show(Window.GetWindow(this), "Put every managed file back to its original and clear the journal?\n\nThe baseline copies are kept, so this stays repeatable.", "Restore everything", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		Guard(delegate
		{
			int restored = engine.RestoreAll();
			Reload(reportPruned: false);
			SetStatus(restored == 0 ? "Nothing to restore: no file has been touched." : restored + " file(s) put back to the original.");
		});
	}

	private void Verify_OnClick(object sender, RoutedEventArgs e)
	{
		Guard(delegate
		{
			ModVerifyResult result = engine.Verify();
			if (result.Managed == 0)
			{
				SetStatus("Nothing is managed yet, so there is nothing to verify.");
				return;
			}
			if (result.Drifted.Count == 0 && result.Missing.Count == 0)
			{
				SetStatus("Every managed file matches the journal (" + result.Managed + " file(s)).");
				return;
			}
			SetStatus("Managed " + result.Managed + ", intact " + result.Intact + ", changed " + result.Drifted.Count + " (of which " + result.Reverted.Count + " is back to the original), missing " + result.Missing.Count + ". Press Repair to put them back.");
		});
	}

	private void Repair_OnClick(object sender, RoutedEventArgs e)
	{
		Guard(delegate
		{
			int repaired = engine.Repair();
			Reload(reportPruned: false);
			SetStatus(repaired == 0 ? "No drift: every managed file is in the state the journal describes." : "Re-applied the intended state to " + repaired + " file(s).");
		});
	}

	private void Refresh_OnClick(object sender, RoutedEventArgs e)
	{
		Reload(reportPruned: true);
	}

	private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(engine.ModsRoot);
			Process.Start(new ProcessStartInfo(engine.ModsRoot)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex) when (ex is IOException || ex is Win32Exception)
		{
			SetStatus("Could not open the folder: " + ex.Message);
		}
	}

	private void Import_OnClick(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Import a mod",
			Filter = "Mod archive (*.zip)|*.zip|All files (*.*)|*.*"
		};
		if (dialog.ShowDialog(Window.GetWindow(this)) != true)
		{
			return;
		}
		Guard(delegate
		{
			string temp = Path.Combine(Path.GetTempPath(), "fgoac-mod-" + Guid.NewGuid().ToString("N"));
			try
			{
				ZipFile.ExtractToDirectory(dialog.FileName, temp, overwriteFiles: true);
				string? source = FindModFolder(temp);
				if (source == null)
				{
					throw new ModEngineException("That archive does not hold a folder with a mod.json in it.");
				}
				string name = Path.GetFileName(source);
				CopyTree(source, Path.Combine(engine.ModsRoot, name));
				Reload(reportPruned: false);
				SetStatus("Imported " + name + ". Tick it and press Apply Selected.");
			}
			finally
			{
				TryDeleteTree(temp);
			}
		});
	}

	private static string? FindModFolder(string root)
	{
		if (File.Exists(Path.Combine(root, "mod.json")))
		{
			return root;
		}
		foreach (string directory in Directory.EnumerateDirectories(root))
		{
			if (File.Exists(Path.Combine(directory, "mod.json")))
			{
				return directory;
			}
		}
		foreach (string directory in Directory.EnumerateDirectories(root))
		{
			string? nested = FindModFolder(directory);
			if (nested != null)
			{
				return nested;
			}
		}
		return null;
	}

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			string target = Path.Combine(destination, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, overwrite: true);
		}
	}

	private static void TryDeleteTree(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private void Guard(Action action)
	{
		try
		{
			action();
		}
		catch (ModEngineException ex)
		{
			SetStatus(ex.Message);
		}
	}

	/// <summary>One knob's control, with a way to read it back and to put a value in it.</summary>
	private sealed class TunerControl
	{
		public ModParameter Parameter = null!;

		public Func<string> Read = null!;

		public Action<string> Write = null!;
	}
}

/// <summary>
/// One mod as the page lists it. Public on purpose: a WPF binding does not resolve against a
/// non-public type, and the row would then show as the class name and refuse to tick.
/// </summary>
public sealed class ModRow : INotifyPropertyChanged
{
	private bool selected;

	internal ModDefinition Definition = null!;

	public bool Applied { get; set; }

	public long FileCount { get; set; }

	public string FilesLabel
	{
		get
		{
			return FileCount == 1 ? "1 file" : FileCount + " files";
		}
	}

	public string StateText { get; set; } = "";

	public bool Selected
	{
		get
		{
			return selected;
		}
		set
		{
			if (selected != value)
			{
				selected = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Selected"));
			}
		}
	}

	public string Name
	{
		get
		{
			return Definition.Name;
		}
	}

	public string Folder
	{
		get
		{
			return Definition.Folder;
		}
	}

	public string Description
	{
		get
		{
			return Definition.Description;
		}
	}

	/// <summary>The first sentence of the description, for the row's one-line summary.</summary>
	public string Summary
	{
		get
		{
			string text = Definition.Description;
			int stop = text.IndexOf(". ", StringComparison.Ordinal);
			return stop > 40 ? text.Substring(0, stop + 1) : text;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}