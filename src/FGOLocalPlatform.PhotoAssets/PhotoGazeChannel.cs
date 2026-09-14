namespace FGOLocalPlatform.PhotoAssets;

public sealed record PhotoGazeChannel(bool Supported, int[] BoneIds, float AngularLimit, string Reason);
