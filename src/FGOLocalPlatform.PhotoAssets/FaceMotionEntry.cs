using System.IO;

namespace FGOLocalPlatform.PhotoAssets;

public sealed record FaceMotionEntry(string ArchivePath, string EntryName, string Source, FgoFaceMotion Motion)
{
	public string DisplayName => Path.GetFileNameWithoutExtension(EntryName.Replace('\\', '/'));

	public override string ToString()
	{
		return DisplayName;
	}
}
