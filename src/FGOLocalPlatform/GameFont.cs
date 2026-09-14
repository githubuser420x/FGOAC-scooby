using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// The display face is the game's own: App\rom\font\SEGA_Skip-B.ttf, which is already on every
/// player's disk. It is loaded from the install at startup and put in the application resources
/// under DisplayFont, so the XAML does not have to know where it came from. If the file is not
/// there the resource keeps the Segoe UI fallback the dictionary declares.
/// </summary>
internal static class GameFont
{
	private const string FamilyName = "SEGA-Skip B";

	private const string FileName = "SEGA_Skip-B.ttf";

	public static void Install(ResourceDictionary resources)
	{
		try
		{
			string folder = Path.Combine(GamePaths.GameRoot, "rom", "font");
			if (!File.Exists(Path.Combine(folder, FileName)))
			{
				return;
			}
			FontFamily family = new FontFamily(new Uri(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar), "./#" + FamilyName);
			if (family.GetTypefaces().Count == 0)
			{
				return;
			}
			resources["DisplayFont"] = family;
		}
		catch (Exception)
		{
		}
	}
}
