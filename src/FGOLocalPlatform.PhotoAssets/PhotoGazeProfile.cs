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
			return new PhotoGazeProfile(Unsupported("The skeleton lists the same bone twice."), Unsupported("The skeleton lists the same bone twice."));
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
			return Unsupported("This skeleton has no usable built-in look-at bones.");
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
				return Unsupported("The built-in look-at setup does not match this skeleton.");
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
				return Unsupported("The look-at bones on this skeleton are not ones we recognize.");
			}
			float num3 = photoLookDescriptor.Parameters.Skip(1).Take(7).Select(MathF.Abs)
				.Min();
			if (num3 <= 0f || num3 >= 180f)
			{
				return Unsupported("The built-in look-at angle limits are not valid.");
			}
			num = MathF.Min(num, num3);
			list.Add(value.Index);
		}
		if (eyes && (array.Length != 2 || !array.Any((PhotoLookDescriptor d) => d.Name == "eye_l") || !array.Any((PhotoLookDescriptor d) => d.Name == "eye_r")))
		{
			return Unsupported("This skeleton has no separate left and right eye bones.");
		}
		if (!eyes && !array.Any((PhotoLookDescriptor d) => d.Name == "head"))
		{
			return Unsupported("This skeleton has no separate head bone.");
		}
		return new PhotoGazeChannel(Supported: true, list.ToArray(), num, "Built-in bones and angle limits match.");
	}
}
