using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ZstdSharp;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FarcArchive
{
	private const uint ZstdMagicLittleEndian = 4247762216u;

	private const uint GzipMagicLittleEndian = 134777631u;

	public string Path { get; }

	public string Magic { get; private set; } = string.Empty;

	public int HeaderSize { get; private set; }

	public int ArchiveFlags { get; private set; }

	public int Alignment { get; private set; }

	public int Format { get; private set; }

	public IReadOnlyList<FarcEntry> Entries { get; private set; } = Array.Empty<FarcEntry>();

	public FarcArchive(string path)
	{
		Path = System.IO.Path.GetFullPath(path);
		using FileStream stream = File.OpenRead(Path);
		ReadDirectory(stream);
	}

	public FarcEntry? Find(string suffix)
	{
		return Entries.FirstOrDefault((FarcEntry entry) => entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
	}

	public IReadOnlyList<FarcEntry> Matching(params string[] suffixes)
	{
		return Entries.Where((FarcEntry entry) => suffixes.Any((string suffix) => entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))).ToArray();
	}

	public byte[] Read(FarcEntry entry)
	{
		using FileStream fileStream = File.OpenRead(Path);
		fileStream.Position = entry.Offset;
		if (entry.Flags == 0)
		{
			return ReadExactly(fileStream, entry.CompressedSize, entry.Name);
		}
		List<byte[]> list = new List<byte[]>();
		if ((entry.Flags & 0x10) != 0)
		{
			ReadUInt32LittleEndian(fileStream);
			List<int> list2 = new List<int>();
			while (true)
			{
				uint num = ReadUInt32LittleEndian(fileStream);
				if ((num == 134777631 || num == 4247762216u) ? true : false)
				{
					break;
				}
				if (num == 0 || num > entry.CompressedSize)
				{
					throw new FgoFormatException($"{entry.Name} gives an invalid compressed chunk length: {num}");
				}
				list2.Add(checked((int)num));
				if (list2.Count > 1000000)
				{
					throw new FgoFormatException(entry.Name + " is split into far too many compressed chunks.");
				}
			}
			fileStream.Position -= 4L;
			foreach (int item in list2)
			{
				list.Add(ReadExactly(fileStream, item, entry.Name));
			}
		}
		else
		{
			list.Add(ReadExactly(fileStream, entry.CompressedSize, entry.Name));
		}
		try
		{
			if ((entry.Flags & 2) != 0)
			{
				list = list.Select(DecompressGzip).ToList();
			}
			else if ((entry.Flags & 0x20) != 0)
			{
				Decompressor decompressor = new Decompressor();
				try
				{
					list = list.Select((byte[] chunk) => decompressor.Unwrap(chunk).ToArray()).ToList();
				}
				finally
				{
					if (decompressor != null)
					{
						((IDisposable)decompressor).Dispose();
					}
				}
			}
		}
		catch (Exception ex) when (!(ex is FgoFormatException) && !(ex is OutOfMemoryException))
		{
			throw new FgoFormatException("Could not decompress " + entry.Name + ": " + ex.Message, ex);
		}
		byte[] array = new byte[list.Sum((byte[] chunk) => chunk.Length)];
		int num2 = 0;
		foreach (byte[] item2 in list)
		{
			Buffer.BlockCopy(item2, 0, array, num2, item2.Length);
			num2 += item2.Length;
		}
		if (entry.UncompressedSize > 0 && array.Length != entry.UncompressedSize)
		{
			throw new FgoFormatException($"{entry.Name} unpacked to the wrong size: got {array.Length:N0} bytes, expected {entry.UncompressedSize:N0}");
		}
		return array;
	}

	public byte[] Read(string entryName)
	{
		FarcEntry entry = Entries.FirstOrDefault((FarcEntry item) => item.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException(entryName);
		return Read(entry);
	}

	private void ReadDirectory(Stream stream)
	{
		byte[] bytes = ReadExactly(stream, 4, "FARC");
		Magic = Encoding.ASCII.GetString(bytes);
		string magic = Magic;
		if ((!(magic == "FARc") && !(magic == "FARC")) || 1 == 0)
		{
			throw new FgoFormatException("This is not a FARC file: " + Magic);
		}
		checked
		{
			HeaderSize = (int)ReadUInt32BigEndian(stream);
			ArchiveFlags = (int)ReadUInt32BigEndian(stream);
			ReadUInt32BigEndian(stream);
			Alignment = (int)ReadUInt32BigEndian(stream);
			Format = (int)ReadUInt32BigEndian(stream);
			int num = (int)ReadUInt32BigEndian(stream);
			ReadUInt32BigEndian(stream);
			if ((num < 0 || num > 1000000) ? true : false)
			{
				throw new FgoFormatException($"The FARC file count is out of range: {num}");
			}
			List<FarcEntry> list = new List<FarcEntry>(num);
			for (int i = 0; i < num; i = unchecked(i + 1))
			{
				string name = ReadCString(stream);
				int offset = (int)ReadUInt32BigEndian(stream);
				int compressedSize = (int)ReadUInt32BigEndian(stream);
				int uncompressedSize = (int)ReadUInt32BigEndian(stream);
				int flags = (int)ReadUInt32BigEndian(stream);
				list.Add(new FarcEntry(name, offset, compressedSize, uncompressedSize, flags));
			}
			Entries = list;
		}
	}

	private static byte[] DecompressGzip(byte[] chunk)
	{
		using MemoryStream stream = new MemoryStream(chunk, writable: false);
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private static string ReadCString(Stream stream)
	{
		using MemoryStream memoryStream = new MemoryStream();
		while (true)
		{
			int num = stream.ReadByte();
			if (num < 0)
			{
				throw new FgoFormatException("A FARC entry name runs off the end of the file.");
			}
			if (num == 0)
			{
				break;
			}
			memoryStream.WriteByte((byte)num);
		}
		return Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, checked((int)memoryStream.Length));
	}

	private static uint ReadUInt32BigEndian(Stream stream)
	{
		return BinaryPrimitives.ReadUInt32BigEndian(ReadExactly(stream, 4, "FARC header"));
	}

	private static uint ReadUInt32LittleEndian(Stream stream)
	{
		return BinaryPrimitives.ReadUInt32LittleEndian(ReadExactly(stream, 4, "FARC entry"));
	}

	private static byte[] ReadExactly(Stream stream, int length, string label)
	{
		byte[] array = new byte[length];
		int num;
		for (int i = 0; i < array.Length; i += num)
		{
			num = stream.Read(array, i, array.Length - i);
			if (num == 0)
			{
				throw new EndOfStreamException(label);
			}
		}
		return array;
	}
}
