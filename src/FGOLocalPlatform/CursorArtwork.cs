using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FGOLocalPlatform;

internal static class CursorArtwork
{
	public static BitmapSource Load(string path)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(Path.GetFullPath(path));
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private static byte[] Pixels(BitmapSource image, int size, int outline)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.HighQuality);
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			double num = Math.Min((double)(size - 2 * outline) / (double)image.PixelWidth, (double)(size - 2 * outline) / (double)image.PixelHeight);
			double num2 = (double)image.PixelWidth * num;
			double num3 = (double)image.PixelHeight * num;
			drawingContext.DrawImage(image, new Rect(((double)size - num2) / 2.0, ((double)size - num3) / 2.0, num2, num3));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(size, size, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(renderTargetBitmap, PixelFormats.Bgra32, null, 0.0);
		byte[] array = new byte[size * size * 4];
		formatConvertedBitmap.CopyPixels(array, size * 4, 0);
		return array;
	}

	private static byte[] Regions(byte[] pixels, int size, int outline)
	{
		byte[] array = new byte[size * size];
		for (int i = 0; i < array.Length; i++)
		{
			if (pixels[i * 4 + 3] >= 128)
			{
				array[i] = 1;
			}
		}
		for (int j = 0; j < size; j++)
		{
			for (int k = 0; k < size; k++)
			{
				if (array[j * size + k] == 1)
				{
					continue;
				}
				bool flag = false;
				for (int l = -outline; l <= outline; l++)
				{
					if (flag)
					{
						break;
					}
					for (int m = -outline; m <= outline; m++)
					{
						int num = k + m;
						int num2 = j + l;
						if (m * m + l * l <= outline * outline && num >= 0 && num < size && num2 >= 0 && num2 < size && pixels[(num2 * size + num) * 4 + 3] >= 128)
						{
							flag = true;
							break;
						}
					}
				}
				if (flag)
				{
					array[j * size + k] = 2;
				}
			}
		}
		return array;
	}

	public static BitmapSource Preview(BitmapSource image, int size, int outline)
	{
		byte[] array = Pixels(image, size, outline);
		byte[] array2 = Regions(array, size, outline);
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				int num = i * size + j;
				int num2 = num * 4;
				byte b = (byte)(((j / 12 + i / 12) % 2 == 0) ? 32u : 48u);
				if (array2[num] != 1)
				{
					for (int k = 0; k < 3; k++)
					{
						array[num2 + k] = ((array2[num] == 2) ? ((byte)(255 - b)) : b);
					}
				}
				array[num2 + 3] = byte.MaxValue;
			}
		}
		BitmapSource bitmapSource = BitmapSource.Create(size, size, 96.0, 96.0, PixelFormats.Bgra32, null, array, size * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}

	public static byte[] Build(BitmapSource image, int size, int outline, double hotX, double hotY)
	{
		if (size < 16 || size > 128 || outline < 0 || outline > 6 || hotX < 0.0 || hotX > 1.0 || hotY < 0.0 || hotY > 1.0)
		{
			throw new ArgumentOutOfRangeException("size");
		}
		byte[] array = Pixels(image, size, outline);
		byte[] array2 = Regions(array, size, outline);
		int num = size * 4;
		int num2 = (size + 31) / 32 * 4;
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write((ushort)0);
		binaryWriter.Write((ushort)2);
		binaryWriter.Write((ushort)1);
		binaryWriter.Write((byte)size);
		binaryWriter.Write((byte)size);
		binaryWriter.Write((ushort)0);
		binaryWriter.Write((ushort)Math.Round(hotX * (double)(size - 1)));
		binaryWriter.Write((ushort)Math.Round(hotY * (double)(size - 1)));
		binaryWriter.Write(40 + (num + num2) * size);
		binaryWriter.Write(22);
		binaryWriter.Write(40);
		binaryWriter.Write(size);
		binaryWriter.Write(size * 2);
		binaryWriter.Write((ushort)1);
		binaryWriter.Write((ushort)32);
		binaryWriter.Write(0);
		binaryWriter.Write(num * size);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		for (int num3 = size - 1; num3 >= 0; num3--)
		{
			byte[] array3 = new byte[num];
			for (int i = 0; i < size; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					int num4 = num3 * size + i;
					array3[i * 4 + j] = (byte)((array2[num4] == 1) ? array[num4 * 4 + j] : ((array2[num4] == 2) ? byte.MaxValue : 0));
				}
			}
			binaryWriter.Write(array3);
		}
		for (int num5 = size - 1; num5 >= 0; num5--)
		{
			byte[] array4 = new byte[num2];
			for (int k = 0; k < size; k++)
			{
				if (array2[num5 * size + k] != 1)
				{
					array4[k / 8] |= (byte)(128 >> k % 8);
				}
			}
			binaryWriter.Write(array4);
		}
		return memoryStream.ToArray();
	}
}
