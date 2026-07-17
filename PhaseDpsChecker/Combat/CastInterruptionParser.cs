using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

internal readonly record struct CastInterruption(string PlayerName, string ActionName);

internal static class CastInterruptionParser
{
	private const string PlayerActionSeparator = "は「";
	private const string InterruptedSuffix = "」の詠唱を中断した";

	public static bool TryParse(string message, out CastInterruption interruption)
	{
		interruption = default;
		if (string.IsNullOrWhiteSpace(message))
		{
			return false;
		}

		int suffixIndex = message.IndexOf(InterruptedSuffix, StringComparison.Ordinal);
		if (suffixIndex < 0)
		{
			return false;
		}
		int separatorIndex = message.LastIndexOf(PlayerActionSeparator, suffixIndex, StringComparison.Ordinal);
		if (separatorIndex < 0)
		{
			return false;
		}

		string playerName = StripLogPrefix(message[..separatorIndex]).Trim();
		int actionStart = separatorIndex + PlayerActionSeparator.Length;
		string actionName = message[actionStart..suffixIndex].Trim();
		if (playerName.Length == 0 || actionName.Length == 0)
		{
			return false;
		}

		interruption = new CastInterruption(playerName, actionName);
		return true;
	}

	public static bool TryResolvePartyMember(
		string parsedPlayerName,
		IReadOnlyDictionary<uint, string> partyMembers,
		out uint entityId,
		out string playerName)
	{
		string parsed = parsedPlayerName.Trim();
		foreach (KeyValuePair<uint, string> member in partyMembers)
		{
			string candidate = member.Value.Trim();
			if (string.Equals(parsed, candidate, StringComparison.Ordinal) ||
				parsed.StartsWith(candidate + "@", StringComparison.Ordinal))
			{
				entityId = member.Key;
				playerName = candidate;
				return true;
			}
		}

		entityId = 0;
		playerName = string.Empty;
		return false;
	}

	public static uint CreateSyntheticActionId(string actionName)
	{
		uint hash = 2166136261;
		foreach (char character in actionName)
		{
			hash ^= character;
			hash *= 16777619;
		}
		return 0xE0000000u | (hash & 0x0FFFFFFFu);
	}

	private static string StripLogPrefix(string text)
	{
		int fullWidthColon = text.LastIndexOf('：');
		int colon = text.LastIndexOf(':');
		int prefixEnd = Math.Max(fullWidthColon, colon);
		return prefixEnd >= 0 ? text[(prefixEnd + 1)..] : text;
	}
}
