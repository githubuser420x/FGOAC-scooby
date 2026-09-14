using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class SummonSettingsWindow : UserControl, IComponentConnector
{
	private readonly string settingsPath;

	private readonly List<SummonCardOption> cards = new List<SummonCardOption>();

	private ICollectionView? view;

	private bool batching;

	private bool dirty;

	private string? loadedText;

	public SummonSettingsWindow(string appRoot, string? configurationPath = null)
	{
		InitializeComponent();
		string fullPath = Path.GetFullPath(Path.Combine(appRoot, "..", "Server", "artemis"));
		settingsPath = configurationPath ?? Path.Combine(fullPath, "config", "fgo_summon_weights.json");
		JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(Path.Combine(appRoot, "deck.json"))).AsObject();
		string fullPath2 = Path.GetFullPath(Path.Combine(appRoot, jsonObject["CardsPath"].GetValue<string>()));
		foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(Path.Combine(fullPath, "titles", "fgo", "data", "summon_candidates.json")))["cards"].AsArray())
		{
			SummonCardOption summonCardOption = new SummonCardOption
			{
				TcId = item["tc_id"].GetValue<int>(),
				Kind = item["type"].GetValue<int>(),
				EntityId = item["entity_id"].GetValue<int>(),
				Name = item["name"].GetValue<string>(),
				Rarity = item["rarity"].GetValue<int>(),
				Category = (item["category"]?.GetValue<string>() ?? "regular"),
				HoloType = (item["holo_type"]?.GetValue<int>() ?? 0),
				AcquisitionNote = string.Join("\n", (from n in item["acquisition_notes"]?.AsArray()
					select n.GetValue<string>()) ?? Enumerable.Empty<string>()),
				ImagePath = Path.Combine(fullPath2, Path.GetFileName(item["file_name"].GetValue<string>()))
			};
			cards.Add(summonCardOption);
			summonCardOption.PropertyChanged += delegate(object? _, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == "WeightText" && !batching)
				{
					dirty = true;
					Recalculate();
				}
			};
		}
		view = CollectionViewSource.GetDefaultView(cards);
		view.Filter = Filter;
		CardsGrid.ItemsSource = (IEnumerable)view;
		LoadSettings();
	}

	private bool Filter(object value)
	{
		SummonCardOption summonCardOption = (SummonCardOption)value;
		string text = SearchBox.Text.Trim();
		if (CategoryFilter.SelectedIndex switch
		{
			1 => summonCardOption.Category == "regular", 
			2 => summonCardOption.Category == "supplemental", 
			3 => summonCardOption.IsStory, 
			_ => !summonCardOption.IsStory, 
		} && (KindFilter.SelectedIndex == 0 || summonCardOption.Kind == KindFilter.SelectedIndex))
		{
			if (text.Length != 0 && !summonCardOption.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.EnglishName.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileNumber.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				return summonCardOption.TcId.ToString().Contains(text);
			}
			return true;
		}
		return false;
	}

	private void Search_OnChanged(object sender, TextChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
	}

	private void Filter_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		ICollectionView? obj = view;
		if (obj != null)
		{
			obj.Refresh();
		}
		if (EditButtons != null && CategoryFilter != null)
		{
			EditButtons.IsEnabled = CategoryFilter.SelectedIndex != 3;
		}
	}

	private void Grid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
	{
		((DispatcherObject)this).Dispatcher.BeginInvoke((DispatcherPriority)4, (Delegate)new Action(Recalculate));
	}

	private void Grid_OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
	{
		if (e.Row.Item is SummonCardOption { IsStory: not false })
		{
			e.Cancel = true;
			StatusText.Text = "Story-fixed cards are read-only and cannot be added to the random pool.";
		}
	}

	private void Grid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (StatusText != null && CardsGrid.SelectedItem is SummonCardOption { IsStory: not false } summonCardOption)
		{
			StatusText.Text = summonCardOption.AcquisitionNote.Split('\n')[0];
		}
	}

	private void LoadSettings()
	{
		try
		{
			string text = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			JsonObject jsonObject = ((text == null) ? null : JsonNode.Parse(text).AsObject());
			JsonObject jsonObject2 = null;
			if (jsonObject != null)
			{
				JsonNode? jsonNode = jsonObject["version"];
				if (jsonNode == null || jsonNode.GetValue<int>() != 1 || !(jsonObject["weights"] is JsonObject jsonObject3))
				{
					throw new InvalidDataException("the config version or format is wrong");
				}
				jsonObject2 = jsonObject3;
				HashSet<string> hashSet = (from c in cards
					where !c.IsStory
					select c.TcId.ToString(CultureInfo.InvariantCulture)).ToHashSet();
				foreach (KeyValuePair<string, JsonNode> item in jsonObject2)
				{
					if (!hashSet.Contains(item.Key) || !(item.Value is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value) || value < 0 || value > 1000000)
					{
						throw new InvalidDataException("Card " + item.Key + " cannot be drawn, or its weight is invalid");
					}
				}
				if (!jsonObject2.Any<KeyValuePair<string, JsonNode>>((KeyValuePair<string, JsonNode> pair) => pair.Value.GetValue<int>() > 0))
				{
					throw new InvalidDataException("at least one card needs a weight above 0");
				}
			}
			batching = true;
			foreach (SummonCardOption card in cards)
			{
				card.WeightText = ((!card.IsStory) ? ((jsonObject2 == null) ? 1 : (jsonObject2[card.TcId.ToString()]?.GetValue<int>() ?? 0)) : 0).ToString(CultureInfo.InvariantCulture);
			}
			batching = false;
			loadedText = text;
			dirty = false;
			Recalculate();
			StatusText.Text = ((text == null) ? "No custom rates saved yet - every drawable card has the same chance." : "Saved rates loaded. A draw already under way, and its retries, keep their original result.");
		}
		catch (Exception ex)
		{
			batching = false;
			loadedText = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			dirty = true;
			Recalculate();
			StatusText.Text = "Could not read the rates: " + ex.Message + ". The server rejects an invalid file - fix the values here and save.";
		}
	}

	private void Recalculate()
	{
		bool flag = cards.Count > 0 && cards.All((SummonCardOption c) => c.Valid);
		decimal num = ((IEnumerable<SummonCardOption>)cards).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		foreach (SummonCardOption card in cards)
		{
			card.SetTotal(flag ? num : 0m);
		}
		SaveButton.IsEnabled = flag && num > 0m;
		decimal num2 = cards.Where((SummonCardOption c) => c.Kind == 1).Sum((Func<SummonCardOption, decimal>)((SummonCardOption c) => c.Weight));
		SummaryText.Text = ((!flag) ? "Enter each weight as a whole number from 0 to 1,000,000 - invalid input cannot be saved." : ((num <= 0m) ? "Every card is excluded - enable at least one." : $"{cards.Count((SummonCardOption c) => !c.IsStory)} drawable · {cards.Count((SummonCardOption c) => c.IsStory)} story-fixed · {cards.Count((SummonCardOption c) => c.Weight > 0)} enabled  |  Servant {num2 * 100m / num:0.####}% · Craft Essence {(num - num2) * 100m / num:0.####}%  |  total 100%"));
		if (dirty)
		{
			StatusText.Text = "You have unsaved changes. Once saved, the server uses the new rates on its next draw. This page only sets draw rates. It does not draw or grant any cards.";
		}
	}

	private void SetWeights(Func<SummonCardOption, int> selector)
	{
		CardsGrid.CancelEdit();
		CardsGrid.CancelEdit(DataGridEditingUnit.Row);
		batching = true;
		foreach (SummonCardOption card in cards)
		{
			card.WeightText = ((!card.IsStory) ? selector(card) : 0).ToString(CultureInfo.InvariantCulture);
		}
		batching = false;
		dirty = true;
		Recalculate();
	}

	private void EqualAll_OnClick(object sender, RoutedEventArgs e)
	{
		SetWeights((SummonCardOption _) => 1);
	}

	private void OnlyOne_OnClick(object sender, RoutedEventArgs e)
	{
		if (CardsGrid.SelectedItems.Count != 1)
		{
			StatusText.Text = "Select one card first, then set it to 100%.";
			return;
		}
		SummonCardOption selected = (SummonCardOption)CardsGrid.SelectedItem;
		if (selected.IsStory)
		{
			StatusText.Text = "A story-fixed card cannot be set to draw randomly.";
			return;
		}
		SetWeights((SummonCardOption c) => (c == selected) ? 1 : 0);
	}

	private void EqualSelected_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = (from SummonCardOption c in CardsGrid.SelectedItems
			where !c.IsStory
			select c).ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "Select the cards that should share the chance first.";
			return;
		}
		SetWeights((SummonCardOption c) => selected.Contains(c) ? 1 : 0);
	}

	private void Exclude_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = CardsGrid.SelectedItems.Cast<SummonCardOption>().ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "Select the cards to exclude first.";
			return;
		}
		if (cards.Any((SummonCardOption c) => !c.Valid))
		{
			StatusText.Text = "Fix the invalid weights first.";
			return;
		}
		SetWeights((SummonCardOption c) => (!selected.Contains(c)) ? c.Weight : 0);
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		if (!CardsGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true) || !CardsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
		{
			return;
		}
		Recalculate();
		if (!SaveButton.IsEnabled)
		{
			return;
		}
		try
		{
			if ((File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null) != loadedText)
			{
				throw new IOException("Another window changed the rates file - click Reload before editing.");
			}
			JsonObject jsonObject = new JsonObject();
			foreach (SummonCardOption item in cards.Where((SummonCardOption c) => !c.IsStory))
			{
				jsonObject[item.TcId.ToString(CultureInfo.InvariantCulture)] = item.Weight;
			}
			string contents = new JsonObject
			{
				["version"] = 1,
				["weights"] = jsonObject
			}.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			});
			Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
			string text = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				File.WriteAllText(text, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				if (File.Exists(settingsPath))
				{
					File.Replace(text, settingsPath, settingsPath + ".bak");
				}
				else
				{
					File.Move(text, settingsPath);
				}
			}
			finally
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			loadedText = contents;
			dirty = false;
			StatusText.Text = "Saved to the server config. A server that supports this picks the rates up on its next draw, with no need to restart the game; if you have just replaced older server code, restart the server once.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save - the existing file was left unchanged: " + ex.Message;
		}
	}

	public bool DiscardConfirmed()
	{
		if (dirty)
		{
			return ThemedMessageBox.Show("Discard the unsaved draw-rate changes?", "Draw Rates", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
		}
		return true;
	}

	private void Reload_OnClick(object sender, RoutedEventArgs e)
	{
		if (DiscardConfirmed())
		{
			LoadSettings();
		}
	}
}
