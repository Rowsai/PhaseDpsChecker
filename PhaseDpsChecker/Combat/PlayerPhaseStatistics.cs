using System;
using System.Collections.Generic;
using System.Linq;

namespace PhaseDpsChecker.Combat;

public sealed class PlayerPhaseStatistics
{
	internal const uint IinactReconciliationActionId = 0xFFFFFFF0u;
	internal const string IinactReconciliationActionName = "IINACT未帰属（DoT・ペット等）";
	private readonly List<(DateTime Start, DateTime End)> gcdIntervals = new List<(DateTime, DateTime)>();
	private readonly List<(DateTime Start, DateTime End)> damageGcdIntervals = new List<(DateTime, DateTime)>();
	private readonly List<(DateTime Start, DateTime End)> healingGcdIntervals = new List<(DateTime, DateTime)>();
	private long capturedTotalDamage;
	private long capturedTotalHealing;
	private int capturedDamageHitCount;
	private int capturedCriticalDamageHits;
	private int capturedDirectDamageHits;
	private int capturedCriticalDirectDamageHits;
	private long? iinactTotalDamage;
	private long? iinactTotalHealing;
	private int? iinactDamageHitCount;
	private int? iinactCriticalDamageHits;
	private int? iinactDirectDamageHits;
	private int? iinactCriticalDirectDamageHits;

	public uint EntityId { get; }

	public uint JobId { get; private set; }

	public string PlayerName { get; private set; }

	public long TotalDamage => Math.Max(capturedTotalDamage, iinactTotalDamage ?? 0);

	public long TotalHealing => Math.Max(capturedTotalHealing, iinactTotalHealing ?? 0);

	public long ActionDamageTotal => Actions.Values.Sum(action => action.TotalDamage);

	public long ActionHealingTotal => Actions.Values.Sum(action => action.TotalHealing);

	public double ExternalBuffDamageReceived { get; private set; }

	public double RaidBuffDamageGranted { get; private set; }

	public int UnbuffedHitCount { get; private set; }

	public int UnbuffedCriticalHits { get; private set; }

	public int UnbuffedDirectHits { get; private set; }

	public int DamageHitCount => Math.Max(capturedDamageHitCount, iinactDamageHitCount ?? 0);

	public int CriticalDamageHits => Math.Clamp(Math.Max(capturedCriticalDamageHits, iinactCriticalDamageHits ?? 0), 0, DamageHitCount);

	public int DirectDamageHits => Math.Clamp(Math.Max(capturedDirectDamageHits, iinactDirectDamageHits ?? 0), 0, DamageHitCount);

	public int CriticalDirectDamageHits => Math.Clamp(Math.Max(capturedCriticalDirectDamageHits, iinactCriticalDirectDamageHits ?? 0), 0, DamageHitCount);

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
		capturedTotalDamage += effect.Damage;
		capturedDamageHitCount++;
		if (effect.Critical)
		{
			capturedCriticalDamageHits++;
		}
		if (effect.DirectHit)
		{
			capturedDirectDamageHits++;
		}
		if (effect.Critical && effect.DirectHit)
		{
			capturedCriticalDirectDamageHits++;
		}
		if (effect.Damage > MaximumDamage)
		{
			MaximumDamage = effect.Damage;
			MaximumDamageAction = actionName;
		}
		action.AddDamage(effect);
		UpdateIinactReconciliation();
	}

	internal void AddHealing(ActionStatistics action, EffectSample effect)
	{
		capturedTotalHealing += effect.Healing;
		action.AddHealing(effect);
		UpdateIinactReconciliation();
	}

	internal void AddRaidAdjustment(double externalBuffDamageReceived, double raidBuffDamageGranted)
	{
		ExternalBuffDamageReceived += Math.Max(0.0, externalBuffDamageReceived);
		RaidBuffDamageGranted += Math.Max(0.0, raidBuffDamageGranted);
	}

	internal void ApplyIinactTotals(
		long totalDamage,
		long totalHealing,
		int damageHitCount,
		int criticalDamageHits,
		int? directDamageHits,
		int? criticalDirectDamageHits)
	{
		iinactTotalDamage = Math.Max(0, totalDamage);
		iinactTotalHealing = Math.Max(0, totalHealing);
		iinactDamageHitCount = Math.Max(0, damageHitCount);
		iinactCriticalDamageHits = Math.Clamp(criticalDamageHits, 0, iinactDamageHitCount.Value);
		iinactDirectDamageHits = directDamageHits.HasValue
			? Math.Clamp(directDamageHits.Value, 0, iinactDamageHitCount.Value)
			: null;
		iinactCriticalDirectDamageHits = criticalDirectDamageHits.HasValue
			? Math.Clamp(criticalDirectDamageHits.Value, 0, iinactDamageHitCount.Value)
			: null;
		UpdateIinactReconciliation();
	}

	internal void ReconcileRestoredActions(bool trustStoredIinactTotals)
	{
		long restoredDamage = capturedTotalDamage;
		long restoredHealing = capturedTotalHealing;
		int restoredHits = capturedDamageHitCount;
		int restoredCriticalHits = capturedCriticalDamageHits;
		int restoredDirectHits = capturedDirectDamageHits;
		int restoredCriticalDirectHits = capturedCriticalDirectDamageHits;
		Actions.Remove(IinactReconciliationActionId);
		bool hasCapturedActions = Actions.Count > 0;
		capturedTotalDamage = Actions.Values.Sum(action => action.TotalDamage);
		capturedTotalHealing = Actions.Values.Sum(action => action.TotalHealing);
		capturedDamageHitCount = Actions.Values.Where(action => action.TotalDamage > 0).Sum(action => action.EffectCount);
		capturedCriticalDamageHits = Actions.Values.Where(action => action.TotalDamage > 0).Sum(action => action.CriticalEffects);
		capturedDirectDamageHits = Actions.Values.Where(action => action.TotalDamage > 0).Sum(action => action.DirectHitEffects);
		capturedCriticalDirectDamageHits = Actions.Values.Where(action => action.TotalDamage > 0).Sum(action => action.CriticalDirectHitEffects);
		iinactTotalDamage = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredDamage, capturedTotalDamage) : capturedTotalDamage;
		iinactTotalHealing = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredHealing, capturedTotalHealing) : capturedTotalHealing;
		iinactDamageHitCount = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredHits, capturedDamageHitCount) : capturedDamageHitCount;
		iinactCriticalDamageHits = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredCriticalHits, capturedCriticalDamageHits) : capturedCriticalDamageHits;
		iinactDirectDamageHits = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredDirectHits, capturedDirectDamageHits) : capturedDirectDamageHits;
		iinactCriticalDirectDamageHits = trustStoredIinactTotals || !hasCapturedActions ? Math.Max(restoredCriticalDirectHits, capturedCriticalDirectDamageHits) : capturedCriticalDirectDamageHits;
		UpdateIinactReconciliation();
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

	public double Hps(double phaseDurationSeconds)
	{
		return phaseDurationSeconds > 0.0 ? (double)TotalHealing / phaseDurationSeconds : 0.0;
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
		capturedTotalDamage = totalDamage;
		capturedTotalHealing = totalHealing;
		iinactTotalDamage = null;
		iinactTotalHealing = null;
		ExternalBuffDamageReceived = externalBuffDamageReceived;
		RaidBuffDamageGranted = raidBuffDamageGranted;
		UnbuffedHitCount = unbuffedHitCount;
		UnbuffedCriticalHits = unbuffedCriticalHits;
		UnbuffedDirectHits = unbuffedDirectHits;
		capturedDamageHitCount = damageHitCount;
		capturedCriticalDamageHits = criticalDamageHits;
		capturedDirectDamageHits = directDamageHits;
		capturedCriticalDirectDamageHits = criticalDirectDamageHits;
		iinactDamageHitCount = null;
		iinactCriticalDamageHits = null;
		iinactDirectDamageHits = null;
		iinactCriticalDirectDamageHits = null;
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

	private void UpdateIinactReconciliation()
	{
		if (!iinactTotalDamage.HasValue && !iinactTotalHealing.HasValue && !iinactDamageHitCount.HasValue)
		{
			Actions.Remove(IinactReconciliationActionId);
			return;
		}

		long damage = Math.Max(0, TotalDamage - capturedTotalDamage);
		long healing = Math.Max(0, TotalHealing - capturedTotalHealing);
		int hits = Math.Max(0, DamageHitCount - capturedDamageHitCount);
		int criticalHits = Math.Max(0, CriticalDamageHits - capturedCriticalDamageHits);
		int directHits = Math.Max(0, DirectDamageHits - capturedDirectDamageHits);
		int criticalDirectHits = Math.Max(0, CriticalDirectDamageHits - capturedCriticalDirectDamageHits);
		if (damage == 0 && healing == 0 && hits == 0 && criticalHits == 0 && directHits == 0 && criticalDirectHits == 0)
		{
			Actions.Remove(IinactReconciliationActionId);
			return;
		}

		if (!Actions.TryGetValue(IinactReconciliationActionId, out ActionStatistics? reconciliation))
		{
			reconciliation = new ActionStatistics(IinactReconciliationActionId, IinactReconciliationActionName, ActionKind.Other, healing > 0);
			Actions[IinactReconciliationActionId] = reconciliation;
		}
		reconciliation.SetIinactReconciliation(damage, healing, hits, criticalHits, directHits, criticalDirectHits);
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
