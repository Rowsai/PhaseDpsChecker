using System;

namespace PhaseDpsChecker.Combat;

public sealed class ActionStatistics
{
	public uint ActionId { get; }

	public string ActionName { get; }

	public ActionKind Kind { get; }

	public int UseCount { get; private set; }

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

	public ActionStatistics(uint actionId, string actionName, ActionKind kind)
	{
		ActionId = actionId;
		ActionName = actionName;
		Kind = kind;
	}

	internal void BeginUse()
	{
		UseCount++;
	}

	internal void AddDamage(EffectSample effect)
	{
		TotalDamage += effect.Damage;
		AddEffect(effect.Damage, effect.Critical, effect.DirectHit);
	}

	internal void AddHealing(EffectSample effect)
	{
		TotalHealing += effect.Healing;
		AddEffect(effect.Healing, effect.Critical, effect.DirectHit);
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
