using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

/// <summary>
/// Reading and writing a member inside a packed .farc archive. The container has a zstd-chunked
/// layout, so the work is done by the game's own Python - which already ships the zstandard
/// module - through <c>Mods\tools\farc.py</c>. The rules themselves run in this process; Python
/// only unpacks and repacks, which keeps one implementation of what a rule means.
/// </summary>
internal sealed class FarcTool
{
	private readonly string pythonPath;

	private readonly string scriptPath;

	public FarcTool(string installRoot)
	{
		pythonPath = Path.Combine(installRoot, "Server", "python", "python.exe");
		scriptPath = Path.Combine(installRoot, "Mods", "tools", "farc.py");
	}

	public bool IsAvailable
	{
		get
		{
			if (File.Exists(pythonPath))
			{
				return File.Exists(scriptPath);
			}
			return false;
		}
	}

	public byte[] ExtractMember(byte[] archive, string member, string label)
	{
		string work = NewWorkArea();
		try
		{
			string source = Path.Combine(work, "source.farc");
			string memberOut = Path.Combine(work, "member.bin");
			File.WriteAllBytes(source, archive);
			Run("extract", source, member, memberOut);
			if (!File.Exists(memberOut))
			{
				throw new ModEngineException("The FARc tool did not produce member '" + member + "' of " + label + ".");
			}
			return File.ReadAllBytes(memberOut);
		}
		finally
		{
			DeleteWorkArea(work);
		}
	}

	public byte[] RepackMember(byte[] archive, string member, byte[] payload, string label)
	{
		string work = NewWorkArea();
		try
		{
			string source = Path.Combine(work, "source.farc");
			string memberIn = Path.Combine(work, "member.bin");
			string packed = Path.Combine(work, "packed.farc");
			File.WriteAllBytes(source, archive);
			File.WriteAllBytes(memberIn, payload);
			Run("repack", source, packed, member + "=" + memberIn);
			if (!File.Exists(packed))
			{
				throw new ModEngineException("The FARc tool did not repack " + label + ".");
			}
			return File.ReadAllBytes(packed);
		}
		finally
		{
			DeleteWorkArea(work);
		}
	}

	private string NewWorkArea()
	{
		if (!IsAvailable)
		{
			throw new ModEngineException("This mod edits a packed archive, so it needs Server\\python\\python.exe and Mods\\tools\\farc.py. One of them is missing.");
		}
		string root = Path.Combine(Path.GetDirectoryName(scriptPath)!, "work");
		Directory.CreateDirectory(root);
		string work = Path.Combine(root, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(work);
		return work;
	}

	private static void DeleteWorkArea(string work)
	{
		try
		{
			Directory.Delete(work, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private void Run(params string[] arguments)
	{
		ProcessStartInfo start = new ProcessStartInfo(pythonPath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		start.ArgumentList.Add(scriptPath);
		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(start) ?? throw new ModEngineException("Could not start the FARc tool.");
		List<string> output = new List<string>();
		Task<string> stdout = process.StandardOutput.ReadToEndAsync();
		Task<string> stderr = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(60000))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException)
			{
			}
			throw new ModEngineException("The FARc tool timed out on '" + string.Join(" ", arguments) + "'.");
		}
		output.Add(stdout.GetAwaiter().GetResult());
		output.Add(stderr.GetAwaiter().GetResult());
		if (process.ExitCode != 0)
		{
			throw new ModEngineException("The FARc tool failed on '" + string.Join(" ", arguments) + "' (exit " + process.ExitCode + "): " + string.Join(" ", output).Trim());
		}
	}
}