using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;

namespace FGOLocalPlatform.PhotoAssets;

public static class PhotoWeaponMotion
{
	public const int MappingSize = 273576;

	public const int Start = 77896;

	public const int Stride = 19568;

	public static PhotoWeaponRig[] Read(MemoryMappedViewAccessor ipc)
	{
		int num = ipc.ReadInt32(8L);
		int num2 = ipc.ReadInt32(77888L);
		if ((num & 1) != 0 || num2 < 0 || num2 > 10)
		{
			throw new IOException("武器快照正在更新，请重新选择动作");
		}
		List<PhotoWeaponRig> list = new List<PhotoWeaponRig>();
		for (int i = 0; i < num2; i++)
		{
			int num3 = 77896 + i * 19568;
			int num4 = ipc.ReadInt32(num3 + 24);
			if (num4 > 0 && num4 <= 128)
			{
				PhotoRigBone[] array = new PhotoRigBone[num4];
				float[] array2 = new float[num4 * 10];
				for (int j = 0; j < num4; j++)
				{
					int num5 = num3 + 96 + j * 72;
					array[j] = new PhotoRigBone(ipc.ReadInt32(num5), ipc.ReadInt32(num5 + 4), ReadName(ipc, num5 + 8));
				}
				ipc.ReadArray(num3 + 9312, array2, 0, array2.Length);
				list.Add(new PhotoWeaponRig(i, ipc.ReadUInt64(num3), ipc.ReadUInt64(num3 + 8), ipc.ReadUInt64(num3 + 16), ReadName(ipc, num3 + 32), ipc.ReadInt32(num3 + 28), array, array2));
			}
		}
		if (ipc.ReadInt32(8L) != num)
		{
			throw new IOException("武器快照正在更新，请重新选择动作");
		}
		return list.ToArray();
	}

	private static string ReadName(MemoryMappedViewAccessor ipc, int offset)
	{
		byte[] array = new byte[64];
		ipc.ReadArray(offset, array, 0, 64);
		int num = Array.IndexOf(array, (byte)0);
		if (num < 0)
		{
			throw new IOException("武器骨架名称无效");
		}
		return Encoding.UTF8.GetString(array, 0, num);
	}

	public static PhotoWeaponClip[] Load(PhotoBodyMotionEntry body, PhotoWeaponRig[] rigs)
	{
		FarcArchive farcArchive = new FarcArchive(body.ArchivePath);
		List<PhotoWeaponClip> list = new List<PhotoWeaponClip>();
		string entryName = body.EntryName;
		string prefix = entryName.Substring(0, entryName.Length - 4) + "_";
		foreach (PhotoWeaponRig rig in rigs)
		{
			FarcEntry farcEntry = farcArchive.Entries.SingleOrDefault((FarcEntry e) => string.Equals(e.Name, prefix + rig.Name + ".mot", StringComparison.OrdinalIgnoreCase));
			if (!(farcEntry == null))
			{
				FgoMotion motion = new FgoMotionParser().Parse(farcArchive.Read(farcEntry), farcEntry.Name);
				if (PhotoBodyMotionCatalog.IsCompatible(motion, rig.JointType, rig.Bones))
				{
					list.Add(new PhotoWeaponClip(rig, motion));
				}
			}
		}
		return list.ToArray();
	}

	public static void Write(MemoryMappedViewAccessor ipc, PhotoWeaponClip[] clips, float frame)
	{
		for (int i = 0; i < 10; i++)
		{
			ipc.Write(77896 + i * 19568 + 19552, 0uL);
			ipc.Write(77896 + i * 19568 + 19560, 0);
		}
		foreach (PhotoWeaponClip photoWeaponClip in clips)
		{
			PhotoWeaponRig rig = photoWeaponClip.Rig;
			int num = 77896 + rig.Slot * 19568;
			if (rig.Slot < ipc.ReadInt32(77888L) && ipc.ReadUInt64(num) == rig.Rig && ipc.ReadUInt64(num + 8) == rig.Mesh && ipc.ReadUInt64(num + 16) == rig.Pose && ipc.ReadInt32(num + 24) == rig.Bones.Length)
			{
				float[] array = PhotoBodyMotionCatalog.Sample(photoWeaponClip.Motion, frame, rig.Baseline);
				ipc.WriteArray(num + 14432, array, 0, array.Length);
				ipc.Write(num + 19552, rig.Rig);
				ipc.Write(num + 19560, rig.Bones.Length);
			}
		}
	}
}
