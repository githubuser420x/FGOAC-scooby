using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FGOLocalPlatform.PhotoAssets;

public sealed class FgoFaceMotionParser
{
	public FgoFaceMotion Parse(ReadOnlySpan<byte> json, string name)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(json.ToArray());
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
		{
			throw new FgoFormatException("FACE JSON root is not an object.");
		}
		List<FgoFaceCurve> list = new List<FgoFaceCurve>();
		float num = 0f;
		foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
		{
			if (item.Value.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			List<FgoFaceKey> list2 = new List<FgoFaceKey>();
			foreach (JsonElement item2 in item.Value.EnumerateArray())
			{
				if (item2.ValueKind == JsonValueKind.Object && TryGetSingle(item2, "time", out var value) && TryGetSingle(item2, "value", out var value2))
				{
					TryGetSingle(item2, "lslope", out var value3);
					TryGetSingle(item2, "rslope", out var value4);
					JsonElement value5;
					int value6;
					bool step = item2.TryGetProperty("step", out value5) && value5.ValueKind == JsonValueKind.Number && value5.TryGetInt32(out value6) && value6 != 0;
					list2.Add(new FgoFaceKey(value, value2, value3, value4, step));
					if (value >= 0f)
					{
						num = Math.Max(num, value);
					}
				}
			}
			list2.Sort((FgoFaceKey left, FgoFaceKey right) => left.Time.CompareTo(right.Time));
			if (list2.Count > 0)
			{
				list.Add(new FgoFaceCurve
				{
					Name = item.Name,
					Keys = list2
				});
			}
		}
		return new FgoFaceMotion
		{
			Name = Path.GetFileNameWithoutExtension(name),
			Curves = list,
			DurationSeconds = num
		};
	}

	private static bool TryGetSingle(JsonElement source, string property, out float value)
	{
		value = 0f;
		if (source.TryGetProperty(property, out var value2) && value2.ValueKind == JsonValueKind.Number)
		{
			return value2.TryGetSingle(out value);
		}
		return false;
	}
}
