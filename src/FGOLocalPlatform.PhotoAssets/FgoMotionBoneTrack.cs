using System;
using System.Collections.Generic;
using System.Linq;

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

	public FgoMotionChannel? Find(ushort type)
	{
		return Channels.FirstOrDefault((FgoMotionChannel channel) => channel.Type == type);
	}
}
