using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform.PhotoAssets;

public static class FaceMotionCatalog
{
	private static readonly Regex TokenRegex = new Regex("(?:SVTGO|SVT|EMI|NPC|CDE|CD)_[0-9]{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	public static IReadOnlyList<FaceMotionEntry> ListForModel(string gameRoot, string modelPath)
	{
		string path = ResolveRobDirectory(gameRoot);
		Match match = FaceTokenRegex().Match(Path.GetFileNameWithoutExtension(modelPath));
		List<(string, string)> list = new List<(string, string)>();
		if (match.Success)
		{
			list.Add((Path.Combine(path, "FACE_" + match.Value.ToUpperInvariant() + ".farc"), "Character-specific"));
		}
		list.Add((Path.Combine(path, "FACE_CMN.farc"), "Shared expressions"));
		List<FaceMotionEntry> list2 = new List<FaceMotionEntry>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		FgoFaceMotionParser fgoFaceMotionParser = new FgoFaceMotionParser();
		foreach (var (text, source) in list)
		{
			if (!File.Exists(text))
			{
				continue;
			}
			try
			{
				FarcArchive farcArchive = new FarcArchive(text);
				foreach (FarcEntry item in farcArchive.Matching(".json"))
				{
					string fileName = Path.GetFileName(item.Name);
					if (hashSet.Add(fileName))
					{
						try
						{
							FgoFaceMotion motion = fgoFaceMotionParser.Parse(farcArchive.Read(item), item.Name);
							list2.Add(new FaceMotionEntry(text, item.Name, source, motion));
						}
						catch (Exception ex) when (((ex is IOException || ex is FgoFormatException || ex is JsonException) ? 1 : 0) != 0)
						{
							hashSet.Remove(fileName);
						}
					}
				}
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is FgoFormatException) ? 1 : 0) != 0)
			{
			}
		}
		return list2;
	}

	public static FgoFaceMotion? Load(string gameRoot, string faceName)
	{
		string path = ResolveRobDirectory(gameRoot);
		Match match = FaceTokenRegex().Match(faceName);
		string[] source = ((!match.Success) ? new string[1] { Path.Combine(path, "FACE_CMN.farc") } : new string[2]
		{
			Path.Combine(path, "FACE_" + match.Value.ToUpperInvariant() + ".farc"),
			Path.Combine(path, "FACE_CMN.farc")
		});
		string entryName = (faceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? faceName : (faceName + ".json"));
		foreach (string item in source.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (File.Exists(item))
			{
				FarcArchive farcArchive = new FarcArchive(item);
				FarcEntry farcEntry = farcArchive.Entries.FirstOrDefault((FarcEntry item) => Path.GetFileName(item.Name).Equals(entryName, StringComparison.OrdinalIgnoreCase));
				if ((object)farcEntry != null)
				{
					return new FgoFaceMotionParser().Parse(farcArchive.Read(farcEntry), farcEntry.Name);
				}
			}
		}
		return null;
	}

	private static string ResolveRobDirectory(string gameRoot)
	{
		return Path.Combine(Directory.Exists(Path.Combine(gameRoot, "rom")) ? Path.Combine(gameRoot, "rom") : gameRoot, "rob");
	}

	private static Regex FaceTokenRegex()
	{
		return TokenRegex;
	}
}
