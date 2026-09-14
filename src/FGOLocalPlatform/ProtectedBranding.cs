using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FGOLocalPlatform;

internal static class ProtectedBranding
{
	private static readonly byte[] KeyPartA = Convert.FromBase64String("9rJkft5Zit0FeASyx4vP6DCP5+6ahDvsk3r+4o+l3Tg=");

	private static readonly byte[] KeyPartB = Convert.FromBase64String("xixtwBqIG1e+CvL+dO3kHv5NvF1lspJ1lPQosRiliMs=");

	private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("FGOLocalPlatform.Branding.v1");

	public static string WindowTitle { get; } = Decrypt("oIXhrQEzw/Ig6+EG", "+mHsIDowtPh8+qRYsMWLr30M8ctKx/tekwks+ZOrAgXSWjFJcTO9svO4lA==", "o39eiCWmXa8qQXeMaY8zNQ==");

	public static string HeaderText { get; } = Decrypt("8QipDpQ7gwFmfbR1", "RuY/cxf95y7zo8+2aA474LmSE/FS4KhAsNAVRmwrmVj28M91tLfUGECYGS6YDheyghY=", "DgTBHVbn2pE3V+YY67zPMg==");

	public static string FooterNotice { get; } = Decrypt("1HOnmYKLkP+KznKk", "IGahHf9RCXWGpnJSTMPCcss8Ptxu3bt2R3t2Tlsx0nzIjQXTCbPq/muIDjsPD1821WXpOKeglsk0NVrzx4nqS5kZ", "QN0IVbXUoJO8GAnMnPONvA==");

	public static string AuthorSuffix
	{
		get
		{
			string windowTitle = WindowTitle;
			return windowTitle.Substring(windowTitle.Length - 13);
		}
	}

	private static string Decrypt(string nonceText, string cipherText, string tagText)
	{
		byte[] array = new byte[KeyPartA.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = (byte)(KeyPartA[i] ^ KeyPartB[i]);
		}
		byte[] nonce = Convert.FromBase64String(nonceText);
		byte[] array2 = Convert.FromBase64String(cipherText);
		byte[] tag = Convert.FromBase64String(tagText);
		byte[] array3 = new byte[array2.Length];
		try
		{
			using AesGcm aesGcm = new AesGcm(array);
			aesGcm.Decrypt(nonce, array2, tag, array3, AssociatedData);
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array3);
		}
		catch (Exception ex) when (((ex is CryptographicException || ex is DecoderFallbackException) ? 1 : 0) != 0)
		{
			throw new InvalidDataException("Protected application branding failed its integrity check.", ex);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
			CryptographicOperations.ZeroMemory(array3);
		}
	}
}
