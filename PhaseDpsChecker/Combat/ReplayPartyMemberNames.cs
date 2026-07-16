using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public static class ReplayPartyMemberNames
{
	private static readonly IReadOnlyDictionary<string, uint> JobIds =
		new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
		{
			["Gladiator"] = 1,
			["Pugilist"] = 2,
			["Marauder"] = 3,
			["Lancer"] = 4,
			["Archer"] = 5,
			["Conjurer"] = 6,
			["Thaumaturge"] = 7,
			["Paladin"] = 19,
			["Monk"] = 20,
			["Warrior"] = 21,
			["Dragoon"] = 22,
			["Bard"] = 23,
			["White Mage"] = 24,
			["Black Mage"] = 25,
			["Arcanist"] = 26,
			["Summoner"] = 27,
			["Scholar"] = 28,
			["Rogue"] = 29,
			["Ninja"] = 30,
			["Machinist"] = 31,
			["Dark Knight"] = 32,
			["Astrologian"] = 33,
			["Samurai"] = 34,
			["Red Mage"] = 35,
			["Blue Mage"] = 36,
			["Gunbreaker"] = 37,
			["Dancer"] = 38,
			["Reaper"] = 39,
			["Sage"] = 40,
			["Viper"] = 41,
			["Pictomancer"] = 42,
		};

	public static bool TryResolve(string? displayName, uint classJobId, out uint resolvedJobId)
	{
		resolvedJobId = 0;
		if (string.IsNullOrWhiteSpace(displayName) ||
			!JobIds.TryGetValue(displayName.Trim(), out uint nameJobId) ||
			(classJobId != 0 && classJobId != nameJobId))
		{
			return false;
		}

		resolvedJobId = classJobId != 0 ? classJobId : nameJobId;
		return true;
	}
}
