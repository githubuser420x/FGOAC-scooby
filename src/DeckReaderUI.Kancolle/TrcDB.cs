using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DeckReaderUI.Kancolle;

public class TrcDB
{
	private readonly Dictionary<ushort, TrcMetadata> cards;

	public string Path { get; }

	public TrcMetadata this[ushort key] => cards[key];

	public TrcDB(string path)
	{
		Path = path;
		string json = File.ReadAllText(path);
		cards = JsonSerializer.Deserialize<Dictionary<ushort, TrcMetadata>>(json) ?? throw new InvalidDataException("Trading-card database is invalid: " + path + ".");
	}

	public TrcMetadata? GetById(ushort id)
	{
		cards.TryGetValue(id, out TrcMetadata value);
		return value;
	}
}
