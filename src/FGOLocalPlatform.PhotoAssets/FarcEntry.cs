namespace FGOLocalPlatform.PhotoAssets;

public sealed record FarcEntry(string Name, int Offset, int CompressedSize, int UncompressedSize, int Flags)
{
	public bool IsCompressed => (Flags & 0x22) != 0;
}
