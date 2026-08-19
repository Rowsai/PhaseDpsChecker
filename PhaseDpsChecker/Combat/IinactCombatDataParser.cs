using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace PhaseDpsChecker.Combat;

internal static class IinactCombatDataParser
{
	public static IinactCombatSnapshot Parse(JObject message, DateTime receivedAt, long sequence)
	{
		JObject encounter = message["Encounter"] as JObject ?? new JObject();
		string encounterId = Value(encounter, "encid", "EncId", "title", "TITLE");
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
					NullableInt(values, "CritDirectHitCount", "critdirecthitcount", "critdirecthits"));
			}
		}
		return new IinactCombatSnapshot(sequence, receivedAt, encounterId, isActive, combatants);
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

	private static int? NullableInt(JObject values, params string[] keys)
	{
		string raw = Value(values, keys);
		return string.IsNullOrWhiteSpace(raw)
			? null
			: int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int value) ? value : null;
	}
}
