using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed class PhaseRecord
{
	public int Number { get; }

	public DateTime StartedAt { get; }

	public DateTime? EndedAt { get; internal set; }

	public uint AnchorTargetId { get; }

	public Dictionary<uint, PlayerPhaseStatistics> Players { get; } = new Dictionary<uint, PlayerPhaseStatistics>();

	public bool IsActive => !EndedAt.HasValue;

	public PhaseRecord(int number, DateTime startedAt, uint anchorTargetId)
	{
		Number = number;
		StartedAt = startedAt;
		AnchorTargetId = anchorTargetId;
	}

	public DateTime EffectiveEnd(DateTime now)
	{
		return EndedAt ?? now;
	}

	public double DurationSeconds(DateTime now)
	{
		return Math.Max(0.0, (EffectiveEnd(now) - StartedAt).TotalSeconds);
	}

	internal PlayerPhaseStatistics EnsurePlayer(uint entityId, string playerName)
	{
		if (!Players.TryGetValue(entityId, out PlayerPhaseStatistics value))
		{
			value = new PlayerPhaseStatistics(entityId, playerName);
			Players.Add(entityId, value);
		}
		else
		{
			value.UpdateName(playerName);
		}
		return value;
	}
}
