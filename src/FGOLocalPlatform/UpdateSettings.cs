using System.Reflection;

namespace FGOLocalPlatform;

/// <summary>
/// Where the launcher looks for a newer build, and which version it is running. The version comes
/// from the assembly, which takes it from &lt;Version&gt; in FGOLocalPlatform.csproj.
/// </summary>
internal static class UpdateSettings
{
	public const string Owner = "githubuser420x";

	public const string Repo = "FGOAC-scooby";

	public const string ProjectUrl = "https://github.com/" + Owner + "/" + Repo;

	public const string ReleasesUrl = ProjectUrl + "/releases";

	public const string LatestReleaseApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

	/// <summary>The running version as three numbers, for example 1.1.0.</summary>
	public static string Version { get; } = Read();

	private static string Read()
	{
		System.Version version = Assembly.GetExecutingAssembly().GetName().Version;
		if (version == null)
		{
			return "0.0.0";
		}
		return $"{version.Major}.{version.Minor}.{version.Build}";
	}
}
