using System;
using System.Collections.Generic;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoMotion
{
	public string Name { get; init; } = "";

	public string SkeletonName { get; init; } = "";

	public int FrameCount { get; init; }

	public int JointType { get; init; }

	public IReadOnlyList<FgoMotionBoneTrack> Tracks { get; init; } = Array.Empty<FgoMotionBoneTrack>();

	public bool RotationDecoderVerified { get; init; }

	public bool IsPlayable
	{
		get
		{
			if (RotationDecoderVerified)
			{
				return Tracks.Count > 0;
			}
			return false;
		}
	}
}
