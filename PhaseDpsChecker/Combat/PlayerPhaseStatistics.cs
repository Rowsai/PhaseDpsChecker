using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed class PlayerPhaseStatistics
{
	private readonly List<(DateTime Start, DateTime End)> gcdIntervals = new List<(DateTime, DateTime)>();
	private readonly List<(DateTime Start, DateTime End)> damageGcdIntervals = new List<(DateTime, DateTime)>();
	private readonly List<(DateTime Start, DateTime End)> healingGcdIntervals = new List<(DateTime, DateTime)>();

	public uint EntityId { get; }

	public uint JobId { get; private set; }

	public string PlayerName { get; private set; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public double ExternalBuffDamageReceived { get; private set; }

	public double RaidBuffDamageGranted { get; private set; }

	public int UnbuffedHitCount { get; private set; }

	public int UnbuffedCriticalHits { get; private set; }

	public int UnbuffedDirectHits { get; private set; }

	public int DamageHitCount { get; private set; }

	public int CriticalDamageHits { get; private set; }

	public int DirectDamageHits { get; private set; }

	public int CriticalDirectDamageHits { get; private set; }

	public uint MaximumDamage { get; private set; }

	public string MaximumDamageAction { get; private set; } = "-";

	public Dictionary<uint, ActionStatistics> Actions { get; } = new Dictionary<uint, ActionStatistics>();

	internal IReadOnlyList<(DateTime Start, DateTime End)> GcdIntervals => gcdIntervals;

	internal IReadOnlyList<(DateTime Start, DateTime End)> DamageGcdIntervals => damageGcdIntervals;

	internal IReadOnlyList<(DateTime Start, DateTime End)> HealingGcdIntervals => healingGcdIntervals;

	public double CriticalRate => DamageRate(CriticalDamageHits);

	public double DirectHitRate => DamageRate(DirectDamageHits);

	public double CriticalDirectHitRate => DamageRate(CriticalDirectDamageHits);

	public double RaidAdjustedDamage => TotalDamage - ExternalBuffDamageReceived + RaidBuffDamageGranted;

	internal double EstimatedUnbuffedCriticalChance => Math.Clamp((UnbuffedCriticalHits + 5.0) / (UnbuffedHitCount + 20.0), 0.05, 0.95);

	internal double EstimatedUnbuffedDirectHitChance => Math.Clamp((UnbuffedDirectHits + 4.0) / (UnbuffedHitCount + 20.0), 0.05, 0.95);

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

	internal void SetJobId(uint jobId)
	{
		if (jobId != 0)
		{
			JobId = jobId;
		}
	}

	internal ActionStatistics GetAction(uint actionId, string actionName, ActionKind kind, bool countsAsUse, bool isHealingAction = false)
	{
		if (!Actions.TryGetValue(actionId, out ActionStatistics value))
		{
			value = new ActionStatistics(actionId, actionName, kind, isHealingAction);
			Actions.Add(actionId, value);
		}
		else if (isHealingAction)
		{
			value.MarkAsHealingAction();
		}
		if (countsAsUse)
		{
			value.BeginUse();
		}
		return value;
	}

	internal void AddInterruptedCast(uint actionId, string actionName, ActionKind kind, bool isHealingAction)
	{
		GetAction(actionId, actionName, kind, countsAsUse: false, isHealingAction).AddInterruptedCast();
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

	internal void AddRaidAdjustment(double externalBuffDamageReceived, double raidBuffDamageGranted)
	{
		ExternalBuffDamageReceived += Math.Max(0.0, externalBuffDamageReceived);
		RaidBuffDamageGranted += Math.Max(0.0, raidBuffDamageGranted);
	}

	internal void AddUnbuffedObservation(EffectSample effect, bool hasExternalCriticalBuff, bool hasExternalDirectHitBuff)
	{
		if (!hasExternalCriticalBuff && !hasExternalDirectHitBuff)
		{
			UnbuffedHitCount++;
			if (effect.Critical)
			{
				UnbuffedCriticalHits++;
			}
			if (effect.DirectHit)
			{
				UnbuffedDirectHits++;
			}
		}
	}

	internal void AddGcdInterval(DateTime timestamp, double durationSeconds, bool countsAsDamage, bool countsAsHealing)
	{
		double value = Math.Clamp(durationSeconds, 0.1, 10.0);
		DateTime dateTime = timestamp.AddSeconds(value);
		AddInterval(gcdIntervals, timestamp, dateTime);
		if (countsAsDamage)
		{
			AddInterval(damageGcdIntervals, timestamp, dateTime);
		}
		if (countsAsHealing)
		{
			AddInterval(healingGcdIntervals, timestamp, dateTime);
		}
	}

	public double Dps(double phaseDurationSeconds)
	{
		if (!(phaseDurationSeconds <= 0.0))
		{
			return (double)TotalDamage / phaseDurationSeconds;
		}
		return 0.0;
	}

	public double Rdps(double phaseDurationSeconds)
	{
		return phaseDurationSeconds > 0.0 ? RaidAdjustedDamage / phaseDurationSeconds : 0.0;
	}

	public double ActiveRate(DateTime phaseStart, DateTime phaseEnd)
	{
		return ActiveRate(gcdIntervals, phaseStart, phaseEnd);
	}

	internal void RestoreState(
		long totalDamage,
		long totalHealing,
		double externalBuffDamageReceived,
		double raidBuffDamageGranted,
		int unbuffedHitCount,
		int unbuffedCriticalHits,
		int unbuffedDirectHits,
		int damageHitCount,
		int criticalDamageHits,
		int directDamageHits,
		int criticalDirectDamageHits,
		uint maximumDamage,
		string maximumDamageAction,
		IEnumerable<(DateTime Start, DateTime End)> restoredGcdIntervals,
		IEnumerable<(DateTime Start, DateTime End)> restoredDamageGcdIntervals,
		IEnumerable<(DateTime Start, DateTime End)> restoredHealingGcdIntervals)
	{
		TotalDamage = totalDamage;
		TotalHealing = totalHealing;
		ExternalBuffDamageReceived = externalBuffDamageReceived;
		RaidBuffDamageGranted = raidBuffDamageGranted;
		UnbuffedHitCount = unbuffedHitCount;
		UnbuffedCriticalHits = unbuffedCriticalHits;
		UnbuffedDirectHits = unbuffedDirectHits;
		DamageHitCount = damageHitCount;
		CriticalDamageHits = criticalDamageHits;
		DirectDamageHits = directDamageHits;
		CriticalDirectDamageHits = criticalDirectDamageHits;
		MaximumDamage = maximumDamage;
		MaximumDamageAction = maximumDamageAction;
		gcdIntervals.Clear();
		gcdIntervals.AddRange(restoredGcdIntervals);
		damageGcdIntervals.Clear();
		damageGcdIntervals.AddRange(restoredDamageGcdIntervals);
		healingGcdIntervals.Clear();
		healingGcdIntervals.AddRange(restoredHealingGcdIntervals);
		Actions.Clear();
	}

	public double DamageActiveRate(DateTime phaseStart, DateTime phaseEnd)
	{
		return ActiveRate(damageGcdIntervals, phaseStart, phaseEnd);
	}

	public double HealingActiveRate(DateTime phaseStart, DateTime phaseEnd)
	{
		return ActiveRate(healingGcdIntervals, phaseStart, phaseEnd);
	}

	private static void AddInterval(List<(DateTime Start, DateTime End)> intervals, DateTime start, DateTime end)
	{
		if (intervals.Count != 0 && start <= intervals[^1].End)
		{
			(DateTime existingStart, DateTime existingEnd) = intervals[^1];
			if (end > existingEnd)
			{
				intervals[^1] = (existingStart, end);
			}
			return;
		}
		intervals.Add((start, end));
	}

	private static double ActiveRate(IReadOnlyList<(DateTime Start, DateTime End)> intervals, DateTime phaseStart, DateTime phaseEnd)
	{
		double totalSeconds = (phaseEnd - phaseStart).TotalSeconds;
		if (totalSeconds <= 0.0)
		{
			return 0.0;
		}
		double num = 0.0;
		foreach (var gcdInterval in intervals)
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
