using System;
using System.Collections.Generic;
using System.Linq;

namespace FGOLocalPlatform.PhotoAssets;

public sealed record PhotoGazeProfile(PhotoGazeChannel Head, PhotoGazeChannel Eyes)
{
	public static PhotoGazeProfile Resolve(IReadOnlyList<PhotoRigBone> bones, IReadOnlyList<PhotoLookDescriptor> head, IReadOnlyList<PhotoLookDescriptor> eyes)
	{
		if (bones.Select((PhotoRigBone b) => b.Index).Distinct().Count() != bones.Count || bones.Select((PhotoRigBone b) => b.Name).Distinct<string>(StringComparer.Ordinal).Count() != bones.Count)
		{
			return new PhotoGazeProfile(Unsupported("骨架节点不唯一"), Unsupported("骨架节点不唯一"));
		}
		Dictionary<string, PhotoRigBone> bones2 = bones.ToDictionary<PhotoRigBone, string>((PhotoRigBone b) => b.Name, StringComparer.Ordinal);
		return new PhotoGazeProfile(ResolveChannel(head, bones2, eyes: false), ResolveChannel(eyes, bones2, eyes: true));
	}

	private static PhotoGazeChannel Unsupported(string reason)
	{
		return new PhotoGazeChannel(Supported: false, Array.Empty<int>(), 0f, reason);
	}

	private static PhotoGazeChannel ResolveChannel(IReadOnlyList<PhotoLookDescriptor> descriptors, IReadOnlyDictionary<string, PhotoRigBone> bones, bool eyes)
	{
		PhotoLookDescriptor[] array = descriptors.Where((PhotoLookDescriptor d) => d.Parameters.Length == 12 && d.Parameters[0] > 0f).ToArray();
		if (array.Length == 0)
		{
			return Unsupported("该骨架没有有效的原生注视节点");
		}
		float num = float.PositiveInfinity;
		List<int> list = new List<int>();
		PhotoLookDescriptor[] array2 = array;
		foreach (PhotoLookDescriptor photoLookDescriptor in array2)
		{
			bool flag = !bones.TryGetValue(photoLookDescriptor.Name, out PhotoRigBone value) || value.Parent < 0;
			if (!flag)
			{
				int forwardAxis = photoLookDescriptor.ForwardAxis;
				bool flag2 = ((forwardAxis < 0 || forwardAxis > 2) ? true : false);
				flag = flag2;
			}
			bool flag3 = flag;
			if (!flag3)
			{
				int forwardAxis = photoLookDescriptor.UpAxis;
				bool flag2 = ((forwardAxis < 0 || forwardAxis > 2) ? true : false);
				flag3 = flag2;
			}
			if (flag3 || photoLookDescriptor.ForwardAxis == photoLookDescriptor.UpAxis || photoLookDescriptor.Parameters.Any((float v) => !float.IsFinite(v)))
			{
				return Unsupported("原生注视配置与当前骨架不匹配");
			}
			if (eyes)
			{
				string name = photoLookDescriptor.Name;
				flag = ((name == "eye_l" || name == "eye_r") ? true : false);
				flag3 = flag;
			}
			else
			{
				string name = photoLookDescriptor.Name;
				flag = ((name == "neck" || name == "head") ? true : false);
				flag3 = flag;
			}
			if (!flag3)
			{
				return Unsupported("尚未确认该骨架的注视节点类型");
			}
			float num3 = photoLookDescriptor.Parameters.Skip(1).Take(7).Select(MathF.Abs)
				.Min();
			if (num3 <= 0f || num3 >= 180f)
			{
				return Unsupported("原生注视角度无效");
			}
			num = MathF.Min(num, num3);
			list.Add(value.Index);
		}
		if (eyes && (array.Length != 2 || !array.Any((PhotoLookDescriptor d) => d.Name == "eye_l") || !array.Any((PhotoLookDescriptor d) => d.Name == "eye_r")))
		{
			return Unsupported("缺少左右独立眼球节点");
		}
		if (!eyes && !array.Any((PhotoLookDescriptor d) => d.Name == "head"))
		{
			return Unsupported("缺少独立头部节点");
		}
		return new PhotoGazeChannel(Supported: true, list.ToArray(), num, "原生节点与限位匹配");
	}
}
