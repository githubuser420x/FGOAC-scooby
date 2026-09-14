using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FGOLocalPlatform;

namespace DeckReaderUI;

public class Config
{
	public List<string> SelectedCards { get; set; }

	public List<int> SelectedCardCopies { get; set; } = new List<int>();

	public string CardsPath { get; set; }

	public Config(List<string> selectedCards, string cardsPath)
	{
		SelectedCards = selectedCards;
		CardsPath = cardsPath;
	}

	public static Config Load(string path = "deck.json")
	{
		path = ResolvePath(path);
		if (!File.Exists(path))
		{
			return new Config(new List<string>(), "../DEVICE/print");
		}
		return JsonSerializer.Deserialize<Config>(File.ReadAllText(path)) ?? new Config(new List<string>(), "../DEVICE/print");
	}

	public void Save(string path = "deck.json")
	{
		path = ResolvePath(path);
		AtomicFile.Write(path, (Stream stream) => JsonSerializer.Serialize(stream, this));
	}

	private static string ResolvePath(string path)
	{
		if (!Path.IsPathRooted(path))
		{
			return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
		}
		return Path.GetFullPath(path);
	}
}
