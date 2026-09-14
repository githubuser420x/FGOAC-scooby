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
			if (text.Length != 0 && !summonCardOption.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.ChineseName.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileNumber.Contains(text, StringComparison.OrdinalIgnoreCase) && !summonCardOption.FileName.Contains(text, StringComparison.OrdinalIgnoreCase))
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
			StatusText.Text = "剧情固定卡只读，不可加入随机池。";
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
					throw new InvalidDataException("配置版本或格式不正确");
				}
				jsonObject2 = jsonObject3;
				HashSet<string> hashSet = (from c in cards
					where !c.IsStory
					select c.TcId.ToString(CultureInfo.InvariantCulture)).ToHashSet();
				foreach (KeyValuePair<string, JsonNode> item in jsonObject2)
				{
					if (!hashSet.Contains(item.Key) || !(item.Value is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value) || value < 0 || value > 1000000)
					{
						throw new InvalidDataException("卡牌 " + item.Key + " 不可抽取或权重无效");
					}
				}
				if (!jsonObject2.Any<KeyValuePair<string, JsonNode>>((KeyValuePair<string, JsonNode> pair) => pair.Value.GetValue<int>() > 0))
				{
					throw new InvalidDataException("至少一张卡的权重需要大于 0");
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
			StatusText.Text = ((text == null) ? "尚未保存自定义配置：默认所有候选卡平均抽取。" : "已读取保存配置。正在进行中的抽卡及其重试保持原结果。");
		}
		catch (Exception ex)
		{
			batching = false;
			loadedText = (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
			dirty = true;
			Recalculate();
			StatusText.Text = "读取失败：" + ex.Message + "。服务器会拒绝无效配置；可编辑后保存修复。";
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
		SummaryText.Text = ((!flag) ? "权重请输入 0～1,000,000 的整数；不能保存无效输入。" : ((num <= 0m) ? "所有卡均被排除，请至少启用一张。" : $"可抽取 {cards.Count((SummonCardOption c) => !c.IsStory)} 张 · 剧情隔离 {cards.Count((SummonCardOption c) => c.IsStory)} 张 · 启用 {cards.Count((SummonCardOption c) => c.Weight > 0)} 张  |  从者 {num2 * 100m / num:0.####}% · 礼装 {(num - num2) * 100m / num:0.####}%  |  总概率 100%"));
		if (dirty)
		{
			StatusText.Text = "有未保存修改。保存后，服务器下一笔新抽卡读取新配置；当前配置页不会实际抽卡。";
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
			StatusText.Text = "请先选中一张卡，再设置 100%。";
			return;
		}
		SummonCardOption selected = (SummonCardOption)CardsGrid.SelectedItem;
		if (selected.IsStory)
		{
			StatusText.Text = "剧情固定卡不能设置为随机抽取。";
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
			StatusText.Text = "请先选择要平均抽取的卡。";
			return;
		}
		SetWeights((SummonCardOption c) => selected.Contains(c) ? 1 : 0);
	}

	private void Exclude_OnClick(object sender, RoutedEventArgs e)
	{
		HashSet<SummonCardOption> selected = CardsGrid.SelectedItems.Cast<SummonCardOption>().ToHashSet();
		if (selected.Count == 0)
		{
			StatusText.Text = "请先选择要排除的卡。";
			return;
		}
		if (cards.Any((SummonCardOption c) => !c.Valid))
		{
			StatusText.Text = "请先修正无效权重。";
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
				throw new IOException("配置已被其他窗口修改，请重新读取后再编辑。");
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
			StatusText.Text = "已保存到服务器配置。支持此功能的服务器下一笔新抽卡直接生效，无须重启游戏；替换旧服务器代码后需先重启服务器一次。";
		}
		catch (Exception ex)
		{
			StatusText.Text = "保存失败，原配置未替换：" + ex.Message;
		}
	}

	public bool DiscardConfirmed()
	{
		if (dirty)
		{
			return ThemedMessageBox.Show("放弃尚未保存的概率修改？", "统一抽卡概率", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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
