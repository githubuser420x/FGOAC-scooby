using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
		string? directoryName = Path.GetDirectoryName(path);
		Directory.CreateDirectory(directoryName);
		string text = Path.Combine(directoryName, $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				JsonSerializer.Serialize(fileStream, this);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(path))
			{
				File.Replace(text, path, path + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(text, path);
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
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
