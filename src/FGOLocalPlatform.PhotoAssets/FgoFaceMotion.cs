using System;
using System.Collections.Generic;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoFaceMotion
{
	public string Name { get; init; } = string.Empty;

	public IReadOnlyList<FgoFaceCurve> Curves { get; init; } = Array.Empty<FgoFaceCurve>();

	public float DurationSeconds { get; init; }

	public IReadOnlyDictionary<string, float> Evaluate(float timeSeconds)
	{
		Dictionary<string, float> dictionary = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		foreach (FgoFaceCurve curf in Curves)
		{
			dictionary[curf.Name] = Math.Clamp(curf.Evaluate(timeSeconds), 0f, 1f);
		}
		return dictionary;
	}
}
