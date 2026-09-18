using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

/// <summary>
/// The rewrite rules themselves, kept in one place so text targets, packed-archive members and
/// the mods' own self-check all run the same code. A rule matches every line its pattern matches
/// and rebuilds each one as head + new value + tail, so a byte the rule did not name - a BOM, a
/// trailing tag, CRLF endings - is copied through untouched.
/// </summary>
internal static class ModRuleEngine
{
	/// <summary>
	/// Runs the rules over the text. A numeric field may be a <c>{parameter}</c> placeholder,
	/// which is resolved against the chosen values, and a rule carrying a <c>when</c> map is
	/// skipped unless every one of its pairs matches - which is how one mod offers several
	/// mutually exclusive modes.
	/// </summary>
	public static string Apply(string text, IReadOnlyList<ModRule> rules, IReadOnlyDictionary<string, string>? parameters, out int hits)
	{
		hits = 0;
		string result = text;
		foreach (ModRule rule in rules)
		{
			if (!Matches(rule.When, parameters))
			{
				continue;
			}
			if (string.IsNullOrEmpty(rule.Pattern))
			{
				throw new ModEngineException("A rule is missing its pattern.");
			}
			if (!rule.Pattern.Contains("(?<value>", StringComparison.Ordinal))
			{
				throw new ModEngineException("Rule '" + rule.Pattern + "' has no (?<value>...) capture, so the engine cannot preserve the rest of the line.");
			}
			Regex regex = new Regex(rule.Pattern, RegexOptions.Multiline);
			int localHits = 0;
			result = regex.Replace(result, delegate(Match match)
			{
				string oldText = match.Groups["value"].Value;
				if (oldText.Length == 0)
				{
					return match.Value;
				}
				string newText = Rewrite(rule, oldText, parameters);
				if (!string.Equals(newText, oldText, StringComparison.Ordinal))
				{
					localHits++;
				}
				return match.Groups["head"].Value + newText + match.Groups["tail"].Value;
			});
			hits += localHits;
		}
		return result;
	}

	private static bool Matches(Dictionary<string, string>? when, IReadOnlyDictionary<string, string>? parameters)
	{
		if (when == null || when.Count == 0)
		{
			return true;
		}
		foreach (KeyValuePair<string, string> pair in when)
		{
			if (parameters == null || !parameters.TryGetValue(pair.Key, out string? value))
			{
				return false;
			}
			if (!string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	private static string Rewrite(ModRule rule, string oldText, IReadOnlyDictionary<string, string>? parameters)
	{
		if (rule.Operation == "replace")
		{
			return Resolve(rule.Value, parameters) ?? "";
		}
		if (!long.TryParse(oldText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
		{
			// The capture is not a number, so this line is not one the rule can compute on.
			return oldText;
		}
		long result;
		switch (rule.Operation)
		{
		case "set":
			result = ResolveLong(rule.Value, parameters) ?? 0L;
			break;
		case "scale":
			result = (long)Math.Round((double)value * (ResolveDouble(rule.Factor, parameters) ?? 1.0));
			break;
		case "shift":
			result = value + (ResolveLong(rule.Amount, parameters) ?? 0L);
			break;
		case "clamp":
			result = value;
			break;
		default:
			throw new ModEngineException($"Unknown rule operation '{rule.Operation}'.");
		}
		long? min = ResolveLong(rule.Min, parameters);
		long? max = ResolveLong(rule.Max, parameters);
		if (min.HasValue && result < min.Value)
		{
			result = min.Value;
		}
		if (max.HasValue && result > max.Value)
		{
			result = max.Value;
		}
		return result.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Turns <c>{name}</c> into the chosen value, leaving a literal untouched.</summary>
	private static string? Resolve(string? text, IReadOnlyDictionary<string, string>? parameters)
	{
		if (text == null || text.Length < 3 || text[0] != '{' || text[text.Length - 1] != '}')
		{
			return text;
		}
		string name = text.Substring(1, text.Length - 2);
		if (parameters != null && parameters.TryGetValue(name, out string? value))
		{
			return value;
		}
		return text;
	}

	private static double? ResolveDouble(string? text, IReadOnlyDictionary<string, string>? parameters)
	{
		string? resolved = Resolve(text, parameters);
		if (resolved != null && double.TryParse(resolved, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
		{
			return value;
		}
		return null;
	}

	private static long? ResolveLong(string? text, IReadOnlyDictionary<string, string>? parameters)
	{
		string? resolved = Resolve(text, parameters);
		if (resolved != null)
		{
			if (long.TryParse(resolved, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integral))
			{
				return integral;
			}
			if (double.TryParse(resolved, NumberStyles.Float, CultureInfo.InvariantCulture, out double fractional))
			{
				return (long)Math.Round(fractional);
			}
		}
		return null;
	}

	/// <summary>
	/// Turns a PowerShell <c>-like</c> pattern (the wildcards the mod manifests use for their
	/// file globs) into a regular expression, so a mod's globs can be matched against the set of
	/// files the manager already knows about rather than only against what is on disk.
	/// </summary>
	public static Regex GlobToRegex(string pattern)
	{
		string expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
		return new Regex(expression, RegexOptions.IgnoreCase);
	}
}

/// <summary>A refusal the mods tab shows to the player rather than a crash.</summary>
internal sealed class ModEngineException : Exception
{
	public ModEngineException(string message)
		: base(message)
	{
	}
}