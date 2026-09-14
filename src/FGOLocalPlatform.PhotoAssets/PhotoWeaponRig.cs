namespace FGOLocalPlatform.PhotoAssets;

public sealed record PhotoWeaponRig(int Slot, ulong Rig, ulong Mesh, ulong Pose, string Name, int JointType, PhotoRigBone[] Bones, float[] Baseline);
