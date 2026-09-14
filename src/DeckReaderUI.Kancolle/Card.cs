using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeckReaderUI.Kancolle;

public class Card
{
	private static readonly byte[] ENCRYPTION_KEY = new byte[16]
	{
		75, 27, 120, 20, 141, 5, 127, 212, 221, 69,
		192, 167, 220, 195, 226, 88
	};

	private static readonly byte[] LEGACY_ENCRYPTION_KEY = new byte[16]
	{
		75, 27, 123, 20, 141, 5, 127, 212, 221, 69,
		192, 167, 220, 195, 226, 88
	};

	private static readonly byte[] FGO11_IV_MASK = new byte[16]
	{
		119, 100, 206, 83, 38, 148, 64, 44, 72, 142,
		13, 12, 152, 111, 169, 72
	};

	private static readonly object ThumbnailWriteLock = new object();

	public string Path { get; }

	public BitmapImage Bitmap => LoadBitmap(Path);

	public BitmapImage Thumbnail => LoadThumbnail(Path);

	public byte[] FullMetadata { get; }

	public byte[] DecryptedCardData { get; }

	public byte[] RfidId { get; }

	public ushort TrcId { get; }

	public int CardTypeId
	{
		get
		{
			if (!System.IO.Path.GetFileName(Path).Contains("_CE", StringComparison.OrdinalIgnoreCase))
			{
				return 1;
			}
			return 2;
		}
	}

	public string CardTypeLabel
	{
		get
		{
			if (CardTypeId != 2)
			{
				return "Servant";
			}
			return "Craft Essence";
		}
	}

	public string DisplayName => Trc?.NameAlt ?? Trc?.Name ?? $"TC {TrcId}";

	public string FileName => System.IO.Path.GetFileName(Path);

	public int CopyNumber { get; } = 1;

	public string CopyLabel => $"{CardTypeLabel} - Copy {CopyNumber}";

	public TrcMetadata? Trc { get; }

	public string CardInfoText
	{
		get
		{
			StringBuilder stringBuilder2;
			StringBuilder stringBuilder = (stringBuilder2 = new StringBuilder());
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
			handler.AppendLiteral("Filename: ");
			handler.AppendFormatted(System.IO.Path.GetFileName(Path));
			handler.AppendLiteral("\n");
			StringBuilder stringBuilder3 = stringBuilder2.Append(ref handler);
			StringBuilder.AppendInterpolatedStringHandler handler2 = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder3);
			handler2.AppendLiteral("RFID: ");
			handler2.AppendFormatted(BytesToHex(RfidId));
			handler2.AppendLiteral("\n");
			StringBuilder stringBuilder4 = stringBuilder3.Append(ref handler2);
			StringBuilder.AppendInterpolatedStringHandler handler3 = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder4);
			handler3.AppendLiteral("Trc ID: ");
			handler3.AppendFormatted(TrcId);
			handler3.AppendLiteral("\n");
			StringBuilder stringBuilder5 = stringBuilder4.Append(ref handler3);
			StringBuilder.AppendInterpolatedStringHandler handler4 = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder5);
			handler4.AppendLiteral("Name: ");
			handler4.AppendFormatted(Trc?.Name);
			handler4.AppendLiteral("\n");
			StringBuilder stringBuilder6 = stringBuilder5.Append(ref handler4);
			StringBuilder.AppendInterpolatedStringHandler handler5 = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder6);
			handler5.AppendLiteral("NameAlt: ");
			handler5.AppendFormatted(Trc?.NameAlt);
			stringBuilder6.Append(ref handler5);
			return stringBuilder.ToString();
		}
	}

	public Card(Card source, int copyNumber)
	{
		if (copyNumber < 1 || copyNumber > 30)
		{
			throw new ArgumentOutOfRangeException("copyNumber");
		}
		Path = source.Path;
		TrcId = source.TrcId;
		Trc = source.Trc;
		CopyNumber = copyNumber;
		RfidId = (byte[])source.RfidId.Clone();
		if (copyNumber != 1)
		{
			byte[] array = new byte[16];
			Array.Copy(source.RfidId, array, 12);
			BinaryPrimitives.WriteInt32BigEndian(array.AsSpan(12), copyNumber);
			Array.Copy(SHA256.HashData(array), 0, RfidId, 0, 12);
		}
		FullMetadata = BuildFgo11Metadata(RfidId, TrcId, out byte[] decrypted);
		DecryptedCardData = decrypted;
	}

	public Card(TrcDB trcDb, string path)
	{
		FullMetadata = new byte[44];
		DecryptedCardData = new byte[16];
		RfidId = new byte[12];
		Path = path;
		byte[] array = new byte[44];
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			if (fileStream.Length < array.Length)
			{
				throw new InvalidDataException("Card bitmap is too small: " + path + ".");
			}
			fileStream.Seek(-array.Length, SeekOrigin.End);
			int num;
			for (int i = 0; i < array.Length; i += num)
			{
				num = fileStream.Read(array, i, array.Length - i);
				if (num == 0)
				{
					throw new EndOfStreamException("Card metadata is truncated: " + path + ".");
				}
			}
		}
		Array.Copy(array, RfidId, 12);
		byte[] array2 = new byte[16];
		Array.Copy(array, 22, array2, 0, 16);
		byte[] array3 = DecryptCardData(RfidId, array2, ENCRYPTION_KEY, useFgo11Iv: true);
		if (!LooksLikeSupportedCard(array, array3))
		{
			byte[] array4 = DecryptCardData(RfidId, array2, ENCRYPTION_KEY, useFgo11Iv: false);
			if (!LooksLikeLegacyCard(array4))
			{
				array4 = DecryptCardData(RfidId, array2, LEGACY_ENCRYPTION_KEY, useFgo11Iv: false);
			}
			if (LooksLikeLegacyCard(array4))
			{
				array3 = array4;
			}
		}
		ushort result = BinaryPrimitives.ReadUInt16BigEndian(array3.AsSpan(7, 2));
		if (result == 0 || result > 16383)
		{
			string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(path);
			int num2 = fileNameWithoutExtension.IndexOf('_');
			if (!ushort.TryParse((num2 >= 0) ? fileNameWithoutExtension.Substring(0, num2) : fileNameWithoutExtension, out result) || result > 16383)
			{
				throw new InvalidDataException("Cannot determine a valid FGO card ID from " + path + ".");
			}
		}
		TrcId = result;
		FullMetadata = BuildFgo11Metadata(RfidId, TrcId, out byte[] decrypted);
		DecryptedCardData = decrypted;
		Trc = trcDb.GetById(TrcId);
	}

	private static BitmapImage LoadBitmap(string path)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(System.IO.Path.GetFullPath(path));
		bitmapImage.EndInit();
		((Freezable)bitmapImage).Freeze();
		return bitmapImage;
	}

	private static BitmapImage LoadThumbnail(string sourcePath)
	{
		string uriString = EnsureThumbnailFile(sourcePath);
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		// The tiles are 144 px wide, so there is no reason to keep more pixels than that in memory.
		bitmapImage.DecodePixelWidth = 144;
		bitmapImage.UriSource = new Uri(uriString, UriKind.Absolute);
		bitmapImage.EndInit();
		((Freezable)bitmapImage).Freeze();
		return bitmapImage;
	}

	private static string ThumbnailPathFor(string sourcePath)
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(System.IO.Path.GetDirectoryName(sourcePath));
		DirectoryInfo directoryInfo2 = directoryInfo.Parent?.Parent;
		return System.IO.Path.Combine((directoryInfo2 == null) ? System.IO.Path.Combine(directoryInfo.FullName, ".thumbnails") : System.IO.Path.Combine(directoryInfo2.FullName, "cache", "card-thumbnails"), System.IO.Path.GetFileNameWithoutExtension(sourcePath) + ".jpg");
	}

	private static string EnsureThumbnailFile(string sourcePath)
	{
		string text = ThumbnailPathFor(sourcePath);
		DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
		if (File.Exists(text) && File.GetLastWriteTimeUtc(text) >= lastWriteTimeUtc)
		{
			return text;
		}
		lock (ThumbnailWriteLock)
		{
			if (File.Exists(text) && File.GetLastWriteTimeUtc(text) >= lastWriteTimeUtc)
			{
				return text;
			}
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(text));
			using FileStream bitmapStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			BitmapSource bitmapSource = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
			double num = Math.Min(160.0 / (double)bitmapSource.PixelWidth, 224.0 / (double)bitmapSource.PixelHeight);
			BitmapSource bitmapSource2 = ((num < 1.0) ? new TransformedBitmap(bitmapSource, new ScaleTransform(num, num)) : bitmapSource);
			((Freezable)bitmapSource2).Freeze();
			JpegBitmapEncoder jpegBitmapEncoder = new JpegBitmapEncoder
			{
				QualityLevel = 84
			};
			jpegBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource2));
			string text2 = text + ".tmp";
			using (FileStream stream = new FileStream(text2, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				jpegBitmapEncoder.Save(stream);
			}
			File.Move(text2, text, overwrite: true);
			File.SetLastWriteTimeUtc(text, lastWriteTimeUtc);
			return text;
		}
	}

	public static (int Created, int Failed) WarmThumbnailCache(IReadOnlyCollection<Card> cards, Action<int, int>? progress = null)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (Card card in cards)
		{
			try
			{
				string path = ThumbnailPathFor(card.Path);
				bool num4 = File.Exists(path) && File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(card.Path);
				EnsureThumbnailFile(card.Path);
				if (!num4)
				{
					num2++;
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException) ? 1 : 0) != 0)
			{
				num3++;
			}
			num++;
			if (num == cards.Count || num % 50 == 0)
			{
				progress?.Invoke(num, cards.Count);
			}
		}
		return (Created: num2, Failed: num3);
	}

	private static string BytesToHex(byte[] bytes)
	{
		return BitConverter.ToString(bytes).Replace('-', ' ');
	}

	private static bool LooksLikeSupportedCard(byte[] metadata, byte[] decrypted)
	{
		if (metadata[12] == 0 && metadata[13] == 2 && metadata[14] == 83 && metadata[15] == 68 && metadata[16] == 69 && metadata[17] == 74)
		{
			return BinaryPrimitives.ReadUInt16BigEndian(decrypted.AsSpan(0, 2)) == ComputeCardCrc(decrypted.AsSpan(2, 14));
		}
		return LooksLikeLegacyCard(decrypted);
	}

	private static bool LooksLikeLegacyCard(byte[] decrypted)
	{
		for (int i = 0; i < 7; i++)
		{
			if (decrypted[i] != 0)
			{
				return false;
			}
		}
		ushort num = BinaryPrimitives.ReadUInt16BigEndian(decrypted.AsSpan(7, 2));
		if (num == 0 || num > 16383)
		{
			return false;
		}
		for (int j = 9; j < 16; j++)
		{
			if (decrypted[j] != 7)
			{
				return false;
			}
		}
		return true;
	}

	private static byte[] BuildFgo11Metadata(byte[] cardRfid, ushort tcId, out byte[] decrypted)
	{
		byte[] array = new byte[44];
		decrypted = new byte[16];
		Array.Copy(cardRfid, array, 12);
		array[12] = 0;
		array[13] = 2;
		array[14] = 83;
		array[15] = 68;
		array[16] = 69;
		array[17] = 74;
		decrypted[2] = 6;
		BinaryPrimitives.WriteUInt16BigEndian(decrypted.AsSpan(7, 2), tcId);
		BinaryPrimitives.WriteUInt16BigEndian(decrypted.AsSpan(0, 2), ComputeCardCrc(decrypted.AsSpan(2, 14)));
		byte[] array2 = EncryptCardData(cardRfid, decrypted, ENCRYPTION_KEY);
		Array.Copy(array2, 0, array, 22, array2.Length);
		return array;
	}

	private static ushort ComputeCardCrc(ReadOnlySpan<byte> data)
	{
		ushort num = ushort.MaxValue;
		ReadOnlySpan<byte> readOnlySpan = data;
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			byte b = readOnlySpan[i];
			num ^= (ushort)(b << 8);
			for (int j = 0; j < 8; j++)
			{
				num = (ushort)(((num & 0x8000) != 0) ? ((num << 1) ^ 0x1021) : (num << 1));
			}
		}
		return num;
	}

	private static byte[] DecryptCardData(byte[] cardRfid, byte[] encryptedData, byte[] encryptionKey, bool useFgo11Iv)
	{
		using Aes aes = Aes.Create();
		byte[] iv = CreateCardIv(cardRfid, useFgo11Iv);
		aes.Key = encryptionKey;
		return aes.DecryptCbc(encryptedData, iv, PaddingMode.None);
	}

	private static byte[] EncryptCardData(byte[] cardRfid, byte[] decryptedData, byte[] encryptionKey)
	{
		using Aes aes = Aes.Create();
		byte[] iv = CreateCardIv(cardRfid, useFgo11Mask: true);
		aes.Key = encryptionKey;
		return aes.EncryptCbc(decryptedData, iv, PaddingMode.None);
	}

	private static byte[] CreateCardIv(byte[] cardRfid, bool useFgo11Mask)
	{
		byte[] array = new byte[16];
		Array.Copy(cardRfid, array, Math.Min(cardRfid.Length, 12));
		if (useFgo11Mask)
		{
			for (int i = 0; i < array.Length; i++)
			{
				array[i] ^= FGO11_IV_MASK[i];
			}
		}
		return array;
	}
}
