using System;
using System.IO;
using System.Text;

namespace FGOLocalPlatform;

/// <summary>
/// Writes a file so that a crash or a power cut mid-write leaves the previous contents in place:
/// the data goes to a temporary file beside the target, is flushed to disk, and is then swapped in.
/// An existing target is kept as "name.bak".
/// </summary>
internal static class AtomicFile
{
	public static void WriteAllText(string path, string contents)
	{
		Write(path, delegate(Stream stream)
		{
			byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
			stream.Write(bytes, 0, bytes.Length);
		});
	}

	public static void WriteAllBytes(string path, byte[] bytes)
	{
		Write(path, (Stream stream) => stream.Write(bytes, 0, bytes.Length));
	}

	public static void Write(string path, Action<Stream> write)
	{
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
		try
		{
			using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				write(stream);
				stream.Flush(flushToDisk: true);
			}
			if (File.Exists(path))
			{
				File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(temporary, path);
			}
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}
}
