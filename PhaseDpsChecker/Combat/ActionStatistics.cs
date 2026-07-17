using System;

namespace PhaseDpsChecker.Combat;

public sealed class ActionStatistics
{
	public uint ActionId { get; }

	public string ActionName { get; }

	public ActionKind Kind { get; }

	public int UseCount { get; private set; }

	public int InterruptedCastCount { get; private set; }

	public bool IsHealingAction { get; private set; }

	public long TotalDamage { get; private set; }

	public long TotalHealing { get; private set; }

	public int EffectCount { get; private set; }

	public int CriticalEffects { get; private set; }

	public int DirectHitEffects { get; private set; }

	public int CriticalDirectHitEffects { get; private set; }

	public uint MaximumAmount { get; private set; }

	public uint MinimumAmount { get; private set; } = uint.MaxValue;

	public double CriticalRate => Rate(CriticalEffects);

	public double DirectHitRate => Rate(DirectHitEffects);

	public double CriticalDirectHitRate => Rate(CriticalDirectHitEffects);

	public uint DisplayMinimumAmount
	{
		get
		{
			if (MinimumAmount != uint.MaxValue)
			{
				return MinimumAmount;
			}
			return 0u;
		}
	}

	public ActionStatistics(uint actionId, string actionName, ActionKind kind, bool isHealingAction = false)
	{
		ActionId = actionId;
		ActionName = actionName;
		Kind = kind;
		IsHealingAction = isHealingAction;
	}

	internal void BeginUse()
	{
		UseCount++;
	}

	internal void AddInterruptedCast()
	{
		InterruptedCastCount++;
	}

	internal void MarkAsHealingAction()
	{
		IsHealingAction = true;
	}

	internal void AddDamage(EffectSample effect)
	{
		TotalDamage += effect.Damage;
		AddEffect(effect.Damage, effect.Critical, effect.DirectHit);
	}

	internal void AddHealing(EffectSample effect)
	{
		MarkAsHealingAction();
		TotalHealing += effect.Healing;
		AddEffect(effect.Healing, effect.Critical, effect.DirectHit);
	}

	internal void RestoreState(
		int useCount,
		int interruptedCastCount,
		bool isHealingAction,
		long totalDamage,
		long totalHealing,
		int effectCount,
		int criticalEffects,
		int directHitEffects,
		int criticalDirectHitEffects,
		uint maximumAmount,
		uint minimumAmount)
	{
		UseCount = useCount;
		InterruptedCastCount = interruptedCastCount;
		IsHealingAction = isHealingAction;
		TotalDamage = totalDamage;
		TotalHealing = totalHealing;
		EffectCount = effectCount;
		CriticalEffects = criticalEffects;
		DirectHitEffects = directHitEffects;
		CriticalDirectHitEffects = criticalDirectHitEffects;
		MaximumAmount = maximumAmount;
		MinimumAmount = minimumAmount;
	}

	private void AddEffect(uint amount, bool critical, bool directHit)
	{
		EffectCount++;
		if (critical)
		{
			CriticalEffects++;
		}
		if (directHit)
		{
			DirectHitEffects++;
		}
		if (critical && directHit)
		{
			CriticalDirectHitEffects++;
		}
		MaximumAmount = Math.Max(MaximumAmount, amount);
		MinimumAmount = Math.Min(MinimumAmount, amount);
	}

	private double Rate(int count)
	{
		if (EffectCount != 0)
		{
			return (double)count / (double)EffectCount;
		}
		return 0.0;
	}
}
