using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FGOLocalPlatform;

public sealed class SummonCardOption : INotifyPropertyChanged
{
	private string weightText = "0";

	private decimal total;

	private BitmapImage? icon;

	public int TcId { get; init; }

	public int Kind { get; init; }

	public int EntityId { get; init; }

	public string FileNumber => $"{((Kind == 1) ? "Servant" : "Craft Essence")} {EntityId:D5}";

	public string FileName => Path.GetFileName(ImagePath);

	public string Name { get; init; } = "";

	public string ChineseName => CardNames.Chinese(Kind, EntityId);

	public int Rarity { get; init; }

	public string ImagePath { get; init; } = "";

	public string Category { get; init; } = "regular";

	public int HoloType { get; init; }

	public string AcquisitionNote { get; init; } = "";

	public bool IsStory => Category == "story";

	public string CategoryName
	{
		get
		{
			if (!(Category == "story"))
			{
				if (!(Category == "supplemental"))
				{
					return "普通召唤";
				}
				return "补充获取";
			}
			return "剧情固定";
		}
	}

	public string KindName => ((Kind == 1) ? "从者" : "概念礼装") + ((HoloType == 1) ? " · Fatal" : "");

	public string Stars => $"★ {Rarity}";

	public string WeightText
	{
		get
		{
			return weightText;
		}
		set
		{
			if ((!IsStory || !(value != "0")) && weightText != value)
			{
				weightText = value;
				Changed("WeightText");
			}
		}
	}

	public bool Valid
	{
		get
		{
			if (int.TryParse(WeightText, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0)
			{
				return result <= 1000000;
			}
			return false;
		}
	}

	public int Weight
	{
		get
		{
			if (!Valid)
			{
				return 0;
			}
			return int.Parse(WeightText, CultureInfo.InvariantCulture);
		}
	}

	public string Probability
	{
		get
		{
			if (!IsStory)
			{
				if (Valid && !(total <= 0m))
				{
					return ((decimal)Weight * 100m / total).ToString("0.######", CultureInfo.InvariantCulture) + "%";
				}
				return "—";
			}
			return "剧情发放";
		}
	}

	public BitmapImage? Icon
	{
		get
		{
			if (icon != null || !File.Exists(ImagePath))
			{
				return icon;
			}
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.DecodePixelWidth = 100;
				bitmapImage.UriSource = new Uri(ImagePath);
				bitmapImage.EndInit();
				((Freezable)bitmapImage).Freeze();
				icon = bitmapImage;
			}
			catch (Exception ex) when (((ex is IOException || ex is NotSupportedException) ? 1 : 0) != 0)
			{
				return null;
			}
			return icon;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void SetTotal(decimal value)
	{
		total = value;
		Changed("Probability");
	}

	private void Changed(string property)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
	}
}
