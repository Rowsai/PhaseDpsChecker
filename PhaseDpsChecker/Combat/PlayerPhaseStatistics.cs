using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed class PlayerPhaseStatistics
{
	private readonly List<(DateTime Start, DateTime End)> gcdIntervals = new List<(DateTime, DateTime)>();

	public uint EntityId { get; }

	public string PlayerName { get; private set; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public int DamageHitCount { get; private set; }

	public int CriticalDamageHits { get; private set; }

	public int DirectDamageHits { get; private set; }

	public int CriticalDirectDamageHits { get; private set; }

	public uint MaximumDamage { get; private set; }

	public string MaximumDamageAction { get; private set; } = "-";

	public Dictionary<uint, ActionStatistics> Actions { get; } = new Dictionary<uint, ActionStatistics>();

	public double CriticalRate => DamageRate(CriticalDamageHits);

	public double DirectHitRate => DamageRate(DirectDamageHits);

	public double CriticalDirectHitRate => DamageRate(CriticalDirectDamageHits);

	public PlayerPhaseStatistics(uint entityId, string playerName)
	{
		EntityId = entityId;
		PlayerName = playerName;
	}

	internal void UpdateName(string playerName)
	{
		if (!string.IsNullOrWhiteSpace(playerName))
		{
			PlayerName = playerName;
		}
	}

	internal ActionStatistics GetAction(uint actionId, string actionName, ActionKind kind, bool countsAsUse)
	{
		if (!Actions.TryGetValue(actionId, out ActionStatistics value))
		{
			value = new ActionStatistics(actionId, actionName, kind);
			Actions.Add(actionId, value);
		}
		if (countsAsUse)
		{
			value.BeginUse();
		}
		return value;
	}

	internal void AddDamage(string actionName, ActionStatistics action, EffectSample effect)
	{
		TotalDamage += effect.Damage;
		DamageHitCount++;
		if (effect.Critical)
		{
			CriticalDamageHits++;
		}
		if (effect.DirectHit)
		{
			DirectDamageHits++;
		}
		if (effect.Critical && effect.DirectHit)
		{
			CriticalDirectDamageHits++;
		}
		if (effect.Damage > MaximumDamage)
		{
			MaximumDamage = effect.Damage;
			MaximumDamageAction = actionName;
		}
		action.AddDamage(effect);
	}

	internal void AddHealing(ActionStatistics action, EffectSample effect)
	{
		TotalHealing += effect.Healing;
		action.AddHealing(effect);
	}

	internal void AddGcdInterval(DateTime timestamp, double durationSeconds)
	{
		double value = Math.Clamp(durationSeconds, 0.1, 10.0);
		DateTime dateTime = timestamp.AddSeconds(value);
		if (gcdIntervals.Count != 0)
		{
			DateTime dateTime2 = timestamp;
			List<(DateTime Start, DateTime End)> list = gcdIntervals;
			if (!(dateTime2 > list[list.Count - 1].End))
			{
				List<(DateTime Start, DateTime End)> list2 = gcdIntervals;
				(DateTime, DateTime) tuple = list2[list2.Count - 1];
				if (dateTime > tuple.Item2)
				{
					List<(DateTime Start, DateTime End)> list3 = gcdIntervals;
					list3[list3.Count - 1] = (tuple.Item1, dateTime);
				}
				return;
			}
		}
		gcdIntervals.Add((timestamp, dateTime));
	}

	public double Dps(double phaseDurationSeconds)
	{
		if (!(phaseDurationSeconds <= 0.0))
		{
			return (double)TotalDamage / phaseDurationSeconds;
		}
		return 0.0;
	}

	public double ActiveRate(DateTime phaseStart, DateTime phaseEnd)
	{
		double totalSeconds = (phaseEnd - phaseStart).TotalSeconds;
		if (totalSeconds <= 0.0)
		{
			return 0.0;
		}
		double num = 0.0;
		foreach (var gcdInterval in gcdIntervals)
		{
			DateTime dateTime = ((gcdInterval.Start < phaseStart) ? phaseStart : gcdInterval.Start);
			DateTime dateTime2 = ((gcdInterval.End > phaseEnd) ? phaseEnd : gcdInterval.End);
			if (dateTime2 > dateTime)
			{
				num += (dateTime2 - dateTime).TotalSeconds;
			}
		}
		return Math.Clamp(num / totalSeconds, 0.0, 1.0);
	}

	private double DamageRate(int count)
	{
		if (DamageHitCount != 0)
		{
			return (double)count / (double)DamageHitCount;
		}
		return 0.0;
	}
}
