using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PhaseDpsChecker.Combat;

internal static class IinactCombatDataParser
{
	public static bool TryExtract(JObject message, out JObject combatData)
	{
		if (string.Equals(message.Value<string>("type"), "CombatData", StringComparison.Ordinal))
		{
			combatData = message;
			return true;
		}
		if (string.Equals(message.Value<string>("type"), "broadcast", StringComparison.Ordinal)
			&& string.Equals(message.Value<string>("msgtype"), "CombatData", StringComparison.Ordinal)
			&& message["msg"] is JObject legacyCombatData)
		{
			combatData = legacyCombatData;
			return true;
		}
		combatData = null!;
		return false;
	}

	public static IinactCombatSnapshot Parse(JObject message, DateTime receivedAt, long sequence)
	{
		JObject encounter = message["Encounter"] as JObject ?? new JObject();
		string encounterId = Value(encounter, "encid", "EncId", "title", "TITLE");
		double durationSeconds = DurationSeconds(encounter);
		bool isActive = string.Equals(Value(message, "isActive"), "true", StringComparison.OrdinalIgnoreCase);
		Dictionary<string, IinactCombatantSnapshot> combatants = new(StringComparer.OrdinalIgnoreCase);
		if (message["Combatant"] is JObject combatantObject)
		{
			foreach (JProperty property in combatantObject.Properties())
			{
				if (property.Value is not JObject values)
				{
					continue;
				}
				string name = Value(values, "name", "Name", "NAME");
				if (string.IsNullOrWhiteSpace(name))
				{
					name = property.Name;
				}
				combatants[name] = new IinactCombatantSnapshot(
					name,
					Long(values, "damage", "Damage"),
					Long(values, "healed", "Healed"),
					Long(values, "damagetaken", "DamageTaken"),
					Int(values, "hits", "Hits"),
					Int(values, "crithits", "CritHits"),
					NullableInt(values, "DirectHitCount", "directhitcount", "directhits"),
					NullableInt(values, "CritDirectHitCount", "critdirecthitcount", "critdirecthits"),
					Double(values, "encdps", "dps"),
					Double(values, "enchps", "hps"));
			}
		}
		return new IinactCombatSnapshot(sequence, receivedAt, encounterId, isActive, combatants, durationSeconds);
	}

	private static string Value(JObject values, params string[] keys)
	{
		foreach (string key in keys)
		{
			JProperty? property = values.Property(key, StringComparison.OrdinalIgnoreCase);
			if (property != null && property.Value.Type is not JTokenType.Null and not JTokenType.Undefined)
			{
				return property.Value.ToString();
			}
		}
		return string.Empty;
	}

	private static long Long(JObject values, params string[] keys) =>
		long.TryParse(Value(values, keys), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out long value) ? value : 0;

	private static int Int(JObject values, params string[] keys) =>
		int.TryParse(Value(values, keys), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int value) ? value : 0;

	private static double Double(JObject values, params string[] keys) =>
		double.TryParse(Value(values, keys), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
			? Math.Max(0, value)
			: 0;

	private static int? NullableInt(JObject values, params string[] keys)
	{
		string raw = Value(values, keys);
		return string.IsNullOrWhiteSpace(raw)
			? null
			: int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int value) ? value : null;
	}

	private static double DurationSeconds(JObject encounter)
	{
		JProperty? wholeSeconds = encounter.Property("DURATION", StringComparison.Ordinal);
		if (wholeSeconds != null && double.TryParse(wholeSeconds.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
		{
			return Math.Max(0, seconds);
		}

		string[] parts = Value(encounter, "duration").Split(':');
		if (parts.Length is 2 or 3 && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
		{
			double total = 0;
			foreach (string part in parts)
			{
				total = total * 60 + double.Parse(part, CultureInfo.InvariantCulture);
			}
			return Math.Max(0, total);
		}
		return 0;
	}
}
