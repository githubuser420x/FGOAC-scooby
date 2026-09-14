using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoMotionChannel
{
	private const float PackedQuaternionCenter = 16384f;

	private const float PackedQuaternionScale = 23169.06f;

	public ushort Type { get; init; }

	public ushort KeyType { get; init; }

	public uint Reserved { get; init; }

	public ushort Encoding { get; init; }

	public byte TimeEncoding { get; init; }

	public ushort RuntimeUnused16 { get; init; }

	public ushort Auxiliary => RuntimeUnused16;

	public int TimeWidth => TimeEncoding switch
	{
		0 => 1, 
		1 => 2, 
		2 => 4, 
		_ => 0, 
	};

	public bool SignedTime => TimeEncoding == 2;

	public int[] Frames { get; init; } = Array.Empty<int>();

	public float[] ScalarValues { get; init; } = Array.Empty<float>();

	public byte[] PackedValues { get; init; } = Array.Empty<byte>();

	public bool IsRotation
	{
		get
		{
			ushort type = Type;
			if (type >= 14)
			{
				return type <= 24;
			}
			return false;
		}
	}

	public bool IsDynamic => Frames.Length > 1;

	public bool HasScalarPayload
	{
		get
		{
			if (ScalarValues.Length == 0)
			{
				if (KeyType == 4)
				{
					return PackedValues.Length >= 12;
				}
				return false;
			}
			return true;
		}
	}

	public float EvaluateScalar(float frame)
	{
		if (KeyType == 4)
		{
			return EvaluateHermite(frame);
		}
		if (ScalarValues.Length == 0)
		{
			ushort type = Type;
			if ((uint)(type - 6) > 2u)
			{
				return 0f;
			}
			return 1f;
		}
		if (ScalarValues.Length == 1 || Frames.Length <= 1)
		{
			return ScalarValues[0];
		}
		int num = (int)frame;
		if (KeyType == 2)
		{
			int num2 = UpperBound(Frames, num);
			int value = ((num2 > 0 && Frames[num2 - 1] == num) ? (num2 - 1) : num2);
			return ScalarValues[Math.Clamp(value, 0, ScalarValues.Length - 1)];
		}
		int num3 = UpperBound(Frames, num);
		if (num3 <= 0)
		{
			return ScalarValues[0];
		}
		if (num3 >= Frames.Length)
		{
			return ScalarValues[^1];
		}
		int num4 = num3 - 1;
		float num5 = Frames[num3] - Frames[num4];
		float num6 = ((num5 > 0f) ? ((frame - (float)Frames[num4]) / num5) : 0f);
		return ScalarValues[num4] + (ScalarValues[num3] - ScalarValues[num4]) * num6;
	}

	public Quaternion DecodeRotation(int keyIndex)
	{
		if (!IsRotation)
		{
			throw new InvalidOperationException("The channel is not a rotation channel.");
		}
		if ((uint)keyIndex >= (uint)Frames.Length)
		{
			throw new ArgumentOutOfRangeException("keyIndex");
		}
		int num;
		switch (Type)
		{
		case 14:
		case 15:
			num = 8;
			break;
		case 16:
		case 17:
		case 18:
			num = 6;
			break;
		case 19:
			num = 4;
			break;
		case 20:
			num = 16;
			break;
		case 21:
			num = 8;
			break;
		case 22:
		case 24:
			num = 12;
			break;
		case 23:
			num = 6;
			break;
		default:
			throw new FgoFormatException($"Unsupported MOT rotation channel 0x{Type:X2}.");
		}
		int num2 = num;
		int num3 = checked(keyIndex * num2);
		if (num3 + num2 > PackedValues.Length)
		{
			throw new FgoFormatException("The packed MOT rotation key is truncated.");
		}
		return Type switch
		{
			14 => DecodeXyzFixed64(num3, signedOmitted: false), 
			15 => DecodeXyzFixed64(num3, signedOmitted: true), 
			16 => DecodeXyzFixed48(num3), 
			17 => DecodeSmallestThree48(num3), 
			18 => DecodeSmallestThree48Signed(num3), 
			19 => DecodeSmallestThree32(num3), 
			20 => DecodeFloatQuaternion(num3), 
			21 => DecodeHalfQuaternion(num3), 
			22 => DecodeLogRotation(num3, half: false), 
			23 => DecodeLogRotation(num3, half: true), 
			24 => DecodeEulerXyz(num3), 
			_ => throw new FgoFormatException($"Unsupported MOT rotation channel 0x{Type:X2}."), 
		};
	}

	private Quaternion DecodeSmallestThree48(int offset)
	{
		ulong num = PackedValues[offset] | ((ulong)PackedValues[offset + 1] << 8) | ((ulong)PackedValues[offset + 2] << 16) | ((ulong)PackedValues[offset + 3] << 24) | ((ulong)PackedValues[offset + 4] << 32) | ((ulong)PackedValues[offset + 5] << 40);
		float num2 = ((float)((num >> 32) & 0x7FFF) - 16384f) / 23169.06f;
		float num3 = ((float)((num >> 17) & 0x7FFF) - 16384f) / 23169.06f;
		float num4 = ((float)((num >> 2) & 0x7FFF) - 16384f) / 23169.06f;
		MathF.Sqrt(MathF.Max(0f, 1f - num2 * num2 - num3 * num3 - num4 * num4));
		return ComposeSmallestThree((int)(num & 3), num2, num3, num4, positiveOmitted: true);
	}

	private Quaternion DecodeSmallestThree48Signed(int offset)
	{
		ulong num = ReadUInt48(offset);
		float first = DecodeCentered((num >> 33) & 0x7FFF, 16384f, 23169.06f);
		float second = DecodeCentered((num >> 18) & 0x7FFF, 16384f, 23169.06f);
		float third = DecodeCentered((num >> 3) & 0x7FFF, 16384f, 23169.06f);
		return ComposeSmallestThree((int)(num & 3), first, second, third, (num & 4) == 0);
	}

	private Quaternion DecodeSmallestThree32(int offset)
	{
		uint num = BinaryPrimitives.ReadUInt32LittleEndian(PackedValues.AsSpan(offset, 4));
		float scale = 512f * MathF.Sqrt(2f);
		float first = DecodeCentered((num >> 22) & 0x3FF, 512f, scale);
		float second = DecodeCentered((num >> 12) & 0x3FF, 512f, scale);
		float third = DecodeCentered((num >> 2) & 0x3FF, 512f, scale);
		return ComposeSmallestThree((int)(num & 3), first, second, third, positiveOmitted: true);
	}

	private Quaternion DecodeXyzFixed64(int offset, bool signedOmitted)
	{
		ulong num = BinaryPrimitives.ReadUInt64LittleEndian(PackedValues.AsSpan(offset, 8));
		float num2 = DecodeCentered((num >> 42) & 0x1FFFFF, 1048576f, 1048576f);
		float num3 = DecodeCentered((num >> 21) & 0x1FFFFF, 1048576f, 1048576f);
		float num4 = DecodeCentered(num & 0x1FFFFF, 1048576f, 1048576f);
		float num5 = MathF.Sqrt(MathF.Max(0f, 1f - num2 * num2 - num3 * num3 - num4 * num4));
		if (signedOmitted && (num & 0x8000000000000000uL) != 0L)
		{
			num5 = 0f - num5;
		}
		return ValidateFinite(new Quaternion(num2, num3, num4, num5));
	}

	private Quaternion DecodeXyzFixed48(int offset)
	{
		float num = ((float)(int)ReadUInt16(offset) - 32768f) / 32768f;
		float num2 = ((float)(int)ReadUInt16(offset + 2) - 32768f) / 32768f;
		float num3 = ((float)(int)ReadUInt16(offset + 4) - 32768f) / 32768f;
		float w = MathF.Sqrt(MathF.Max(0f, 1f - num * num - num2 * num2 - num3 * num3));
		return ValidateFinite(new Quaternion(num, num2, num3, w));
	}

	private static Quaternion ComposeSmallestThree(int omittedIndex, float first, float second, float third, bool positiveOmitted)
	{
		float num = MathF.Sqrt(MathF.Max(0f, 1f - first * first - second * second - third * third));
		if (!positiveOmitted)
		{
			num = 0f - num;
		}
		return ValidateFinite(omittedIndex switch
		{
			0 => new Quaternion(num, first, second, third), 
			1 => new Quaternion(first, num, second, third), 
			2 => new Quaternion(first, second, num, third), 
			_ => new Quaternion(first, second, third, num), 
		});
	}

	private Quaternion DecodeFloatQuaternion(int offset)
	{
		return ValidateFinite(new Quaternion(ReadSingle(offset), ReadSingle(offset + 4), ReadSingle(offset + 8), ReadSingle(offset + 12)));
	}

	private Quaternion DecodeHalfQuaternion(int offset)
	{
		return ValidateFinite(new Quaternion(ReadHalf(offset), ReadHalf(offset + 2), ReadHalf(offset + 4), ReadHalf(offset + 6)));
	}

	private Quaternion DecodeLogRotation(int offset, bool half)
	{
		Vector3 vector = (half ? new Vector3(ReadHalf(offset), ReadHalf(offset + 2), ReadHalf(offset + 4)) : new Vector3(ReadSingle(offset), ReadSingle(offset + 4), ReadSingle(offset + 8)));
		if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
		{
			throw new FgoFormatException("MOT logarithmic rotation is not finite.");
		}
		float num = vector.Length();
		if (num <= 1E-12f)
		{
			return Quaternion.Identity;
		}
		float num2 = MathF.Sin(num) / num;
		return ValidateFinite(new Quaternion(vector * num2, MathF.Cos(num)));
	}

	private Quaternion DecodeEulerXyz(int offset)
	{
		Vector3 vector = new Vector3(ReadSingle(offset), ReadSingle(offset + 4), ReadSingle(offset + 8));
		if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
		{
			throw new FgoFormatException("MOT Euler rotation is not finite.");
		}
		return Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(vector.X) * Matrix4x4.CreateRotationY(vector.Y) * Matrix4x4.CreateRotationZ(vector.Z));
	}

	private float ReadSingle(int offset)
	{
		return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(PackedValues.AsSpan(offset, 4)));
	}

	private float ReadHalf(int offset)
	{
		return (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(PackedValues.AsSpan(offset, 2)));
	}

	private ushort ReadUInt16(int offset)
	{
		return BinaryPrimitives.ReadUInt16LittleEndian(PackedValues.AsSpan(offset, 2));
	}

	private ulong ReadUInt48(int offset)
	{
		return PackedValues[offset] | ((ulong)PackedValues[offset + 1] << 8) | ((ulong)PackedValues[offset + 2] << 16) | ((ulong)PackedValues[offset + 3] << 24) | ((ulong)PackedValues[offset + 4] << 32) | ((ulong)PackedValues[offset + 5] << 40);
	}

	private static float DecodeCentered(ulong value, float center, float scale)
	{
		return ((float)value - center) / scale;
	}

	private static Quaternion ValidateFinite(Quaternion value)
	{
		if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
		{
			throw new FgoFormatException("MOT quaternion is not finite.");
		}
		return value;
	}

	public Quaternion EvaluateRotation(float frame)
	{
		if (Frames.Length == 0)
		{
			return Quaternion.Identity;
		}
		if (Frames.Length == 1)
		{
			return DecodeRotation(0);
		}
		int num = UpperBound(Frames, (int)frame);
		if (num <= 0)
		{
			return DecodeRotation(0);
		}
		if (num >= Frames.Length)
		{
			return DecodeRotation(Frames.Length - 1);
		}
		int num2 = num - 1;
		float num3 = Frames[num] - Frames[num2];
		float amount = ((num3 > 0f) ? ((frame - (float)Frames[num2]) / num3) : 0f);
		Quaternion first = DecodeRotation(num2);
		Quaternion second = DecodeRotation(num);
		if (KeyType != 5)
		{
			return ShortestSlerp(first, second, amount);
		}
		return ShortestLerp(first, second, amount);
	}

	private float EvaluateHermite(float frame)
	{
		if (Frames.Length == 0 || PackedValues.Length == 0)
		{
			ushort type = Type;
			bool flag = (uint)(type - 6) <= 2u;
			return flag ? 1 : 0;
		}
		if (Frames.Length == 1)
		{
			return ReadHermiteComponent(0, 0);
		}
		int num = UpperBound(Frames, (int)frame);
		if (num <= 0)
		{
			return ReadHermiteComponent(0, 0);
		}
		if (num >= Frames.Length)
		{
			return ReadHermiteComponent(Frames.Length - 1, 0);
		}
		int num2 = num - 1;
		float num3 = Frames[num] - Frames[num2];
		if (num3 <= 0f)
		{
			return ReadHermiteComponent(num2, 0);
		}
		float num4 = (frame - (float)Frames[num2]) / num3;
		float num5 = num4 * num4;
		float num6 = num5 * num4;
		float num7 = ReadHermiteComponent(num2, 0);
		float num8 = ReadHermiteComponent(num2, 2);
		float num9 = ReadHermiteComponent(num, 0);
		float num10 = ReadHermiteComponent(num, 1);
		return (2f * num6 - 3f * num5 + 1f) * num7 + (num6 - 2f * num5 + num4) * num3 * num8 + (-2f * num6 + 3f * num5) * num9 + (num6 - num5) * num3 * num10;
	}

	private float ReadHermiteComponent(int key, int component)
	{
		int start = checked((key * 3 + component) * 4);
		return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(PackedValues.AsSpan(start, 4)));
	}

	private static int UpperBound(IReadOnlyList<int> values, int sample)
	{
		int num = 0;
		int num2 = values.Count;
		while (num2 > 0)
		{
			int num3 = num2 / 2;
			int num4 = num + num3;
			if (values[num4] <= sample)
			{
				num = num4 + 1;
				num2 -= num3 + 1;
			}
			else
			{
				num2 = num3;
			}
		}
		return num;
	}

	private static Quaternion ShortestLerp(Quaternion first, Quaternion second, float amount)
	{
		if (Quaternion.Dot(first, second) < 0f)
		{
			second = new Quaternion(0f - second.X, 0f - second.Y, 0f - second.Z, 0f - second.W);
		}
		return NormalizeByLengthSquared(new Quaternion(first.X + (second.X - first.X) * amount, first.Y + (second.Y - first.Y) * amount, first.Z + (second.Z - first.Z) * amount, first.W + (second.W - first.W) * amount));
	}

	private static Quaternion ShortestSlerp(Quaternion first, Quaternion second, float amount)
	{
		float num = Quaternion.Dot(first, second);
		if (num < 0f)
		{
			second = new Quaternion(0f - second.X, 0f - second.Y, 0f - second.Z, 0f - second.W);
			num = 0f - num;
		}
		if (num > 0.9995f)
		{
			return ShortestLerp(first, second, amount);
		}
		float num2 = MathF.Acos(Math.Clamp(num, -1f, 1f));
		float num3 = MathF.Sin(num2);
		if (MathF.Abs(num3) <= 1E-12f)
		{
			return first;
		}
		float num4 = MathF.Sin((1f - amount) * num2) / num3;
		float num5 = MathF.Sin(amount * num2) / num3;
		return new Quaternion(first.X * num4 + second.X * num5, first.Y * num4 + second.Y * num5, first.Z * num4 + second.Z * num5, first.W * num4 + second.W * num5);
	}

	private static Quaternion NormalizeByLengthSquared(Quaternion value)
	{
		float num = value.LengthSquared();
		if (!float.IsFinite(num) || num <= 1E-12f)
		{
			return Quaternion.Identity;
		}
		// A blended rotation is only a rotation again at unit length.
		float length = MathF.Sqrt(num);
		return new Quaternion(value.X / length, value.Y / length, value.Z / length, value.W / length);
	}
}
