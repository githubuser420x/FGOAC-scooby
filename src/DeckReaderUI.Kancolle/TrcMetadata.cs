namespace DeckReaderUI.Kancolle;

public class TrcMetadata
{
	public ushort TrcId { get; }

	public string Name { get; }

	public string NameAlt { get; }

	public TrcMetadata(ushort trcId, string name, string nameAlt)
	{
		TrcId = trcId;
		Name = name;
		NameAlt = nameAlt;
	}
}
