using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class StartupDiagnostics
{
	public const string PlatformUpdateNeeded = "This game folder has not had Cloud23333's V1.01 update yet: App\\FGO_Runtime.dll is missing, and the English launch and server scripts need it. Apply his V1.01 or V1.02 update to the game folder first, then start FGOAC scooby again.";

	public const string GameErrors = "ERROR 4102 - The game could not reach the local server, because the server is not running, its ports are already in use, or the server address in Settings is wrong. Start the server from the launcher, then click Run Environment Check on this page. On Cloud23333's V1.01 the same error also appears when the computer name is the same as the user name; his V1.02 update fixes that, so update to V1.02.\n\nERROR 8404 - The game's own Startup Mode was saved as Satellite (Sub Unit), so it waits for a main unit that does not exist. On the error screen press F1 to open the Game Test Menu; F2 moves the arrow and F1 confirms. Open Game Settings, set Startup Mode to Main Unit, then choose Exit. The next boot goes to the title screen.\n\nCannot use Aime card, at the title screen - the game's first message to the local server timed out on that boot. Close the game, check that the server shows ready on the Play page, and press Play again.\n\n0x80131515 at Play, or the server failing with a message about FGO_Runtime.dll - Windows marked App\\FGO_Runtime.dll as downloaded from the internet, and PowerShell refuses to load a file with that mark. The launcher clears the mark itself when it starts; if it comes back, right-click the file, open Properties and tick Unblock.\n\nERROR 4104 - The game is installed on drive E: or Y:, and the game's own file hook redirects every E: path to its data mount, so it cannot open its resource files. Move the whole install folder to another drive, such as D:, and start it from there.\n\nERROR 4105 - The game was not started as administrator, so it could not create the registry keys it needs and the server rejected its first play record about 90 seconds after boot. Close the game and start the launcher as administrator.\n\n0xC0000005, the game closing a few seconds after launch - Windows Defender Controlled Folder Access is almost always blocking the game from writing its files. Open Windows Security > Virus & threat protection > Ransomware protection and allow the game's executable, or turn Controlled Folder Access off.\n\nNo sound, or 0x88890004 / 0x88890026 - Windows dropped the game's audio stream, usually because the default output device changed or is muted. Set the headphones or speakers you want as the Windows default output and unmute them in the volume mixer, and if it keeps happening turn the audio hook off on the Settings > Audio page so the game uses shared-mode WASAPI.\n\n0x88890010 - The Windows Audio service is not running. Start the Windows Audio service, then launch the game again.\n\nA black screen or a display driver error - the game is built for NVIDIA cards and needs a current driver. Update your graphics driver, then launch the game again.";

	public static string Explain(int code)
	{
		return code switch
		{
			2 => "Some of the files the game needs are missing. Unzip the FGOAC scooby package into the game folder itself, the one that holds App and Server, so that FGOAC scooby.exe sits beside them; make sure Cloud23333's V1.01 or V1.02 update has been applied; and check whether your antivirus quarantined anything.",
			3 => "The launcher could not find a network adapter for the cabinet network. Set the server address to auto in Settings and re-install the App folder from the package, so that FGO_Launcher.ps1 and fgohook.dll match; auto mode needs no internet connection.", 
			4 => "A folder the game must write to is locked. Start the launcher as administrator, then check the path shown below for free space, a read-only flag, or a folder block from your antivirus.", 
			5 => "Windows has no audio output device turned on. Enable your speakers or headphones in Windows and set them as the default output, then start the game again.", 
			10 => "The local server could not start, usually because a port is already in use, the database failed, or an older server is still running. Read the error below, then check logs/server-control.log, artemis-stderr.log and mariadb.log.", 
			11 => "The server ports could not be reached. On a single PC set the server address to auto and start the local server; for a remote server check the address and ports you entered.", 
			12 => "The optional FGOAudio.dll audio hook is missing. Turn the experimental audio hook off on the Settings > Audio page - version 11.00 has its own shared-mode audio and does not need that file.", 
			13 => "A settings file is not valid JSON, or the game version in it is wrong. Check fgo-launcher.json and config.json for typos, and make sure the game version reads 11.00.", 
			14 => "The monitor or the resolution setting is not valid. Choose your monitor and a valid width and height again on the Display page, then save.", 
			15 => "The environment check did not pass. Fix the items listed below, then click Run Environment Check on the Diagnostics and Help page for the full report.", 
			22 => "The game crashed or closed on its own soon after starting. Check the logs folder for the last failure, the exit code and any crash dump. If the game window opened and closed again with 0xC0000005 although the environment check passes, try windowed 1280x720 on the primary monitor, and when reporting it attach logs\\ago-crash-*.dmp with your graphics card and driver version.",
			_ => "The launch script did not finish. Read the error below and logs/fgo-last-launch.log; the exit number on its own does not tell you the cause.", 
		};
	}

	public static void CheckLayout()
	{
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		string runtime = Path.Combine(GamePaths.GameRoot, "FGO_Runtime.dll");
		if (!File.Exists(runtime))
		{
			throw new FileNotFoundException(PlatformUpdateNeeded, runtime);
		}
		string text = Path.Combine(GamePaths.GameRoot, "FGO_StartupChecks.ps1");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException(Explain(2), text);
		}
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(fullPath, redirectOutput: true, new string[2] { "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; . $env:FGO_CHECK_SCRIPT; Test-FgoWritableLayout -InstallRoot $env:FGO_CHECK_ROOT" });
		processStartInfo.Environment["FGO_CHECK_SCRIPT"] = text;
		processStartInfo.Environment["FGO_CHECK_ROOT"] = fullPath;
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not start the folder check.");
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(20.0)).GetAwaiter().GetResult();
		Task.WhenAll<string>(task, task2).WaitAsync(TimeSpan.FromSeconds(2.0)).GetAwaiter()
			.GetResult();
		if (process.ExitCode != 0)
		{
			throw new IOException(Explain(4) + "\n\n" + task2.GetAwaiter().GetResult() + task.GetAwaiter().GetResult());
		}
	}
}
