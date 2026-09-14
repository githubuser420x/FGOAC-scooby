using System.IO;

namespace FGOLocalPlatform.PhotoAssets;

public sealed record PhotoBodyMotionEntry(string ArchivePath, string EntryName, string SkeletonName, int Frames, bool Compatible, string Reason)
{
	public string DisplayName => Path.GetFileNameWithoutExtension(EntryName.Replace('\\', '/'));

	public override string ToString()
	{
		return DisplayName;
	}
}
