using System;
using System.Collections.Generic;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoMotionBoneTrack
{
	public int BoneIndex { get; init; }

	public int ParentIndex { get; init; }

	public ushort Flags { get; init; }

	public string Name { get; init; } = "";

	public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

	public IReadOnlyList<FgoMotionAlias> AliasRecords { get; init; } = Array.Empty<FgoMotionAlias>();

	public IReadOnlyList<FgoMotionChannel> Channels { get; init; } = Array.Empty<FgoMotionChannel>();

	// Index loops rather than LINQ: this runs for every bone of every frame while a motion plays.
	public FgoMotionChannel? Find(ushort type)
	{
		for (int i = 0; i < Channels.Count; i++)
		{
			if (Channels[i].Type == type)
			{
				return Channels[i];
			}
		}
		return null;
	}

	public FgoMotionChannel? FindRotation()
	{
		for (int i = 0; i < Channels.Count; i++)
		{
			if (Channels[i].IsRotation)
			{
				return Channels[i];
			}
		}
		return null;
	}
}
