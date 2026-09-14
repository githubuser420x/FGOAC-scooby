using System;
using System.Collections.Generic;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoFaceCurve
{
	public string Name { get; init; } = string.Empty;

	public IReadOnlyList<FgoFaceKey> Keys { get; init; } = Array.Empty<FgoFaceKey>();

	public float Evaluate(float time)
	{
		if (Keys.Count == 0)
		{
			return 0f;
		}
		int i;
		for (i = 0; i < Keys.Count && Keys[i].Time <= time; i++)
		{
		}
		if (i <= 0)
		{
			return Keys[0].Value;
		}
		if (i >= Keys.Count)
		{
			IReadOnlyList<FgoFaceKey> keys = Keys;
			return keys[keys.Count - 1].Value;
		}
		FgoFaceKey fgoFaceKey = Keys[i - 1];
		FgoFaceKey fgoFaceKey2 = Keys[i];
		if (fgoFaceKey.Step)
		{
			return fgoFaceKey.Value;
		}
		float num = fgoFaceKey2.Time - fgoFaceKey.Time;
		if (num <= 1E-07f)
		{
			return fgoFaceKey2.Value;
		}
		float num2 = Math.Clamp((time - fgoFaceKey.Time) / num, 0f, 1f);
		float num3 = num2 * num2;
		float num4 = num3 * num2;
		float num5 = 2f * num4 - 3f * num3 + 1f;
		float num6 = num4 - 2f * num3 + num2;
		float num7 = -2f * num4 + 3f * num3;
		float num8 = num4 - num3;
		return num5 * fgoFaceKey.Value + num6 * num * fgoFaceKey.RightSlope + num7 * fgoFaceKey2.Value + num8 * num * fgoFaceKey2.LeftSlope;
	}
}
