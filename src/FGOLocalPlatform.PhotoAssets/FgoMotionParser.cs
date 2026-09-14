using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoMotionParser
{
	private const int HeaderNameOffset = 32;

	private const uint SupportedVersion = 386007831u;

	public FgoMotion Parse(ReadOnlySpan<byte> data, string name)
	{
		ValidateHeader(data);
		int frameCount;
		int num;
		int num2;
		string skeletonName;
		int[] array;
		FgoMotionBoneTrack[] array2;
		checked
		{
			frameCount = (int)ReadUInt32(data, 20);
			num = ReadUInt16(data, 24);
			num2 = data[26];
			int num3 = data[27];
			if ((num <= 0 || num > 100000) ? true : false)
			{
				throw new FgoFormatException($"#MOT element count is invalid: {num}.");
			}
			if (num2 > 11)
			{
				throw new FgoFormatException($"#MOT joint type is invalid: {num2}.");
			}
			CheckRange(data, 32, num3 + 1, "#MOT skeleton name");
			if (data[unchecked(32 + num3)] != 0)
			{
				throw new FgoFormatException("#MOT skeleton name is not NUL terminated.");
			}
			skeletonName = Encoding.ASCII.GetString(data.Slice(32, num3));
			int table = Align4(32 + num3 + 1);
			array = ReadRelativeOffsets(data, table, num, "#MOT element");
			array2 = new FgoMotionBoneTrack[num];
		}
		for (int i = 0; i < num; i++)
		{
			int start = array[i];
			int end = ((i + 1 < num) ? array[i + 1] : data.Length);
			FgoMotionBoneTrack fgoMotionBoneTrack = ParseBoneTrack(data, start, end, frameCount, i);
			if ((uint)fgoMotionBoneTrack.BoneIndex >= (uint)num)
			{
				throw new FgoFormatException($"#MOT record {i} has out-of-range bone id {fgoMotionBoneTrack.BoneIndex}.");
			}
			if (array2[fgoMotionBoneTrack.BoneIndex] != null)
			{
				throw new FgoFormatException($"#MOT contains duplicate bone id {fgoMotionBoneTrack.BoneIndex}.");
			}
			array2[fgoMotionBoneTrack.BoneIndex] = fgoMotionBoneTrack;
		}
		FgoMotionBoneTrack[] array3 = new FgoMotionBoneTrack[num];
		for (int j = 0; j < num; j++)
		{
			FgoMotionBoneTrack fgoMotionBoneTrack2 = array2[j] ?? throw new FgoFormatException($"#MOT is missing bone id {j}.");
			if (fgoMotionBoneTrack2.ParentIndex < -1 || fgoMotionBoneTrack2.ParentIndex >= num || fgoMotionBoneTrack2.ParentIndex == j)
			{
				throw new FgoFormatException($"#MOT bone {j} has invalid parent {fgoMotionBoneTrack2.ParentIndex}.");
			}
			array3[j] = fgoMotionBoneTrack2;
		}
		ValidateHierarchy(array3);
		return new FgoMotion
		{
			Name = name,
			SkeletonName = skeletonName,
			FrameCount = frameCount,
			JointType = num2,
			Tracks = array3,
			RotationDecoderVerified = true
		};
	}

	private static void ValidateHeader(ReadOnlySpan<byte> data)
	{
		if (data.Length < 32 || !data.Slice(0, 4).SequenceEqual("#MOT"u8))
		{
			throw new FgoFormatException("Data is not a valid #MOT stream.");
		}
		if (data[15] != 10)
		{
			throw new FgoFormatException("#MOT uses an unsupported container header.");
		}
		uint num = ReadUInt32(data, 16);
		if (num != 386007831)
		{
			throw new FgoFormatException($"Unsupported #MOT version 0x{num:X8}.");
		}
	}

	private static FgoMotionBoneTrack ParseBoneTrack(ReadOnlySpan<byte> data, int start, int end, int frameCount, int recordIndex)
	{
		if (start < 0 || end <= start || end > data.Length)
		{
			throw new FgoFormatException($"#MOT record {recordIndex} has an invalid range.");
		}
		CheckRange(data, start, 12, $"#MOT record {recordIndex}");
		int num = ReadUInt16(data, start);
		int parentIndex = ReadInt16(data, start + 2);
		ushort flags = ReadUInt16(data, start + 4);
		int num2 = ReadUInt16(data, start + 6);
		int num3 = data[start + 8];
		int num4 = data[start + 9];
		if (num2 > 4096)
		{
			throw new FgoFormatException($"#MOT bone {num} channel count is invalid: {num2}.");
		}
		if (data[start + 10] != 0 || data[start + 11] != 0)
		{
			throw new FgoFormatException($"#MOT bone {num} has unsupported record flags.");
		}
		int num5 = start + 12;
		CheckRange(data, num5, checked(num3 + 1), $"#MOT bone {num} name");
		if (data[num5 + num3] != 0)
		{
			throw new FgoFormatException($"#MOT bone {num} name is not NUL terminated.");
		}
		string text = Encoding.UTF8.GetString(data.Slice(num5, num3));
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new FgoFormatException($"#MOT bone {num} has no name.");
		}
		int num6 = checked(num5 + num3 + 1);
		List<FgoMotionAlias> list = new List<FgoMotionAlias>(num4);
		for (int i = 0; i < num4; i++)
		{
			CheckRange(data, num6, 2, $"#MOT bone {num} alias {i}");
			byte kind = data[num6];
			int num7 = data[num6 + 1];
			int num8 = num6 + 2;
			CheckRange(data, num8, checked(num7 + 1), $"#MOT bone {num} alias {i}");
			if (data[num8 + num7] != 0)
			{
				throw new FgoFormatException($"#MOT bone {num} alias {i} is not " + "NUL terminated.");
			}
			string text2 = Encoding.UTF8.GetString(data.Slice(num8, num7));
			if (string.IsNullOrWhiteSpace(text2))
			{
				throw new FgoFormatException($"#MOT bone {num} alias {i} is empty.");
			}
			list.Add(new FgoMotionAlias(kind, text2));
			num6 = checked(num8 + num7 + 1);
		}
		int table = Align4(num6);
		IReadOnlyList<FgoMotionChannel> channels = ParseChannelTable(data, table, num2, end, frameCount, num);
		return new FgoMotionBoneTrack
		{
			BoneIndex = num,
			ParentIndex = parentIndex,
			Flags = flags,
			Name = text,
			Aliases = list.Select((FgoMotionAlias alias) => alias.Name).ToArray(),
			AliasRecords = list,
			Channels = channels
		};
	}

	private static IReadOnlyList<FgoMotionChannel> ParseChannelTable(ReadOnlySpan<byte> data, int table, int channelCount, int elementEnd, int frameCount, int boneIndex)
	{
		int num = checked(channelCount * 4);
		if (table < 0 || table + num > elementEnd || elementEnd > data.Length)
		{
			throw new FgoFormatException($"#MOT bone {boneIndex} channel table is truncated.");
		}
		int[] array = new int[channelCount];
		int num2 = checked(table + num);
		for (int i = 0; i < channelCount; i++)
		{
			uint num3 = ReadUInt32(data, table + i * 4);
			if (num3 > int.MaxValue)
			{
				throw new FgoFormatException($"#MOT bone {boneIndex} channel {i} offset is too " + "large.");
			}
			int num4 = checked(table + (int)num3);
			if (num4 < num2 || num4 + 8 > elementEnd)
			{
				throw new FgoFormatException($"#MOT bone {boneIndex} channel {i} offset is " + "invalid.");
			}
			array[i] = num4;
			num2 = num4;
		}
		List<FgoMotionChannel> list = new List<FgoMotionChannel>(channelCount);
		for (int j = 0; j < channelCount; j++)
		{
			int num5 = array[j];
			int num6 = ((j + 1 < channelCount) ? array[j + 1] : elementEnd);
			try
			{
				list.Add(ParseChannel(data, num5, num6, frameCount));
			}
			catch (FgoFormatException ex)
			{
				ushort value = ReadUInt16(data, num5);
				ushort value2 = ReadUInt16(data, num5 + 2);
				throw new FgoFormatException($"#MOT bone {boneIndex} channel {j} (type=0x{value:X2}, key={value2}, range=0x{num5:X}-0x{num6:X}) could not be parsed: " + ex.Message, ex);
			}
		}
		return list;
	}

	private static FgoMotionChannel ParseChannel(ReadOnlySpan<byte> data, int start, int end, int frameCount)
	{
		if (start < 0 || end < start + 8 || end > data.Length)
		{
			throw new FgoFormatException("#MOT channel range is invalid.");
		}
		ushort type = ReadUInt16(data, start);
		ushort num = ReadUInt16(data, start + 2);
		uint reserved = ReadUInt32(data, start + 4);
		bool flag = IsRotationType(type);
		switch (num)
		{
		case 0:
			return NewChannel(type, num, reserved, 0, Array.Empty<int>(), Array.Empty<float>(), Array.Empty<byte>());
		case 1:
		{
			if (flag)
			{
				int rotationValueStride = GetRotationValueStride(type);
				CheckRange(data, start + 8, rotationValueStride, "#MOT static rotation");
				return NewChannel(type, num, reserved, 0, new int[1], Array.Empty<float>(), data.Slice(start + 8, rotationValueStride).ToArray());
			}
			CheckRange(data, start + 8, 4, "#MOT static value");
			float num2 = ReadSingle(data, start + 8);
			if (!float.IsFinite(num2))
			{
				throw new FgoFormatException("#MOT static scalar is not finite.");
			}
			return NewChannel(type, num, reserved, 0, new int[1], new float[1] { num2 }, Array.Empty<byte>());
		}
		case 2:
		case 3:
		case 4:
		case 5:
		case 6:
			return ParseDynamicChannel(data, start, end, frameCount, type, num, reserved);
		default:
		{
			int length = Math.Max(0, end - (start + 8));
			return NewChannel(type, num, reserved, 0, Array.Empty<int>(), Array.Empty<float>(), data.Slice(start + 8, length).ToArray());
		}
		}
	}

	private static FgoMotionChannel ParseDynamicChannel(ReadOnlySpan<byte> data, int start, int end, int frameCount, ushort type, ushort keyType, uint reserved)
	{
		CheckRange(data, start + 8, 4, "#MOT dynamic channel header");
		int num = ReadUInt16(data, start + 8);
		ushort auxiliary = ReadUInt16(data, start + 10);
		if (num <= 0)
		{
			throw new FgoFormatException("#MOT dynamic channel has no keys.");
		}
		int valueStride = GetValueStride(type, keyType);
		int valueAlignment = GetValueAlignment(type, keyType);
		int num3;
		int[] frames;
		float[] array;
		checked
		{
			byte b = (byte)(reserved & 0xFF);
			int num2 = b switch
			{
				0 => 1, 
				1 => 2, 
				2 => 4, 
				_ => throw new FgoFormatException($"Unsupported #MOT time encoding {b}."), 
			};
			num3 = Align(start + 12 + num * num2, valueAlignment);
			frames = ReadFrames(data, unchecked(start + 12), num, num2, b == 2);
			if (unchecked((uint)(keyType - 4)) <= 2u)
			{
				byte[] packed = CopyDynamicPayload(data, num3, num * valueStride, end, type == 23);
				return NewChannel(type, keyType, reserved, auxiliary, frames, Array.Empty<float>(), packed);
			}
			CheckChannelPayloadRange(num3, num * 4, end, "#MOT scalar keys");
			array = new float[num];
		}
		for (int i = 0; i < num; i++)
		{
			array[i] = ReadSingle(data, num3 + i * 4);
			if (!float.IsFinite(array[i]))
			{
				throw new FgoFormatException($"#MOT scalar key {i} is not finite.");
			}
		}
		return NewChannel(type, keyType, reserved, auxiliary, frames, array, Array.Empty<byte>());
	}

	private static int GetValueStride(ushort type, ushort keyType)
	{
		switch (keyType)
		{
		case 2:
		case 3:
			return 4;
		case 4:
			return 12;
		case 5:
		case 6:
			if (IsRotationType(type))
			{
				return GetRotationValueStride(type);
			}
			break;
		}
		throw new FgoFormatException($"Unsupported #MOT dynamic key type {keyType}.");
	}

	private static bool IsRotationType(ushort type)
	{
		if (type >= 14)
		{
			return type <= 24;
		}
		return false;
	}

	private static int GetRotationValueStride(ushort type)
	{
		switch (type)
		{
		case 14:
		case 15:
			return 8;
		case 16:
		case 17:
		case 18:
			return 6;
		case 19:
			return 4;
		case 20:
			return 16;
		case 21:
			return 8;
		case 22:
		case 24:
			return 12;
		case 23:
			return 6;
		default:
			throw new FgoFormatException($"Unsupported #MOT rotation channel type 0x{type:X2}.");
		}
	}

	private static int GetValueAlignment(ushort type, ushort keyType)
	{
		bool flag = (uint)(keyType - 2) <= 2u;
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3;
			switch (type)
			{
			case 20:
			case 22:
			case 24:
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			flag2 = flag3;
		}
		if (!flag2)
		{
			return 2;
		}
		return 4;
	}

	private static byte[] CopyDynamicPayload(ReadOnlySpan<byte> data, int offset, int length, int channelEnd, bool allowRuntimeTwoBytePadding)
	{
		if (offset < 0 || length < 0 || offset > channelEnd)
		{
			throw new FgoFormatException("#MOT dynamic value range is invalid.");
		}
		int num = Math.Min(length, channelEnd - offset);
		int num2 = length - num;
		if (num2 > 0 && (!allowRuntimeTwoBytePadding || num2 > 2))
		{
			throw new FgoFormatException("#MOT dynamic value payload is truncated.");
		}
		CheckRange(data, offset, num, "#MOT dynamic values");
		byte[] array = new byte[length];
		data.Slice(offset, num).CopyTo(array);
		return array;
	}

	private static void CheckChannelPayloadRange(int offset, int length, int channelEnd, string label)
	{
		if (offset < 0 || length < 0 || (long)offset + (long)length > channelEnd)
		{
			throw new FgoFormatException(label + " exceeds its channel.");
		}
	}

	private static int[] ReadFrames(ReadOnlySpan<byte> data, int offset, int count, int width, bool signed)
	{
		CheckRange(data, offset, checked(count * width), "#MOT timetable");
		int[] array = new int[count];
		for (int i = 0; i < count; i++)
		{
			int[] array2 = array;
			int num = i;
			array2[num] = width switch
			{
				1 => data[offset + i], 
				2 => (!signed) ? ((int)ReadUInt16(data, offset + i * 2)) : ((int)ReadInt16(data, offset + i * 2)), 
				4 => ReadInt32(data, offset + i * 4), 
				_ => throw new FgoFormatException($"Unsupported #MOT timetable width {width}."), 
			};
		}
		return array;
	}

	private static void ValidateHierarchy(IReadOnlyList<FgoMotionBoneTrack> tracks)
	{
		byte[] state = new byte[tracks.Count];
		for (int i = 0; i < tracks.Count; i++)
		{
			Visit(i);
		}
		void Visit(int id)
		{
			if (state[id] != 2)
			{
				if (state[id] == 1)
				{
					throw new FgoFormatException($"#MOT hierarchy contains a cycle at bone {id}.");
				}
				state[id] = 1;
				int parentIndex = tracks[id].ParentIndex;
				if (parentIndex >= 0)
				{
					Visit(parentIndex);
				}
				state[id] = 2;
			}
		}
	}

	private static int[] ReadRelativeOffsets(ReadOnlySpan<byte> data, int table, int count, string label)
	{
		int[] array;
		int num;
		checked
		{
			CheckRange(data, table, count * 4, label + " offset table");
			array = new int[count];
			num = table + count * 4;
		}
		for (int i = 0; i < count; i++)
		{
			uint num2 = ReadUInt32(data, table + i * 4);
			if (num2 > int.MaxValue)
			{
				throw new FgoFormatException($"{label} {i} offset is too large.");
			}
			int num3 = checked(table + (int)num2);
			if (num3 < num || num3 >= data.Length)
			{
				throw new FgoFormatException($"{label} {i} offset is invalid.");
			}
			array[i] = num3;
			num = num3;
		}
		return array;
	}

	private static FgoMotionChannel NewChannel(ushort type, ushort keyType, uint reserved, ushort auxiliary, int[] frames, float[] scalars, byte[] packed)
	{
		return new FgoMotionChannel
		{
			Type = type,
			KeyType = keyType,
			Reserved = reserved,
			Encoding = auxiliary,
			Frames = frames,
			ScalarValues = scalars,
			PackedValues = packed,
			TimeEncoding = checked((byte)(reserved & 0xFF)),
			RuntimeUnused16 = auxiliary
		};
	}

	private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
	{
		return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
	}

	private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
	{
		return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
	}

	private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
	{
		return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
	}

	private static int ReadInt32(ReadOnlySpan<byte> data, int offset)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
	}

	private static float ReadSingle(ReadOnlySpan<byte> data, int offset)
	{
		return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));
	}

	private static int Align4(int value)
	{
		return checked(value + 3) & -4;
	}

	private static int Align(int value, int alignment)
	{
		return checked((value + alignment - 1) & ~(alignment - 1));
	}

	private static void CheckRange(ReadOnlySpan<byte> data, int offset, int length, string label)
	{
		if (offset < 0 || length < 0 || (long)offset + (long)length > data.Length)
		{
			throw new FgoFormatException(label + " exceeds the stream.");
		}
	}
}
