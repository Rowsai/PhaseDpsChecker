namespace PhaseDpsChecker.Combat;

public readonly record struct EffectSample(
	uint TargetEntityId,
	uint Damage,
	uint Healing,
	bool Critical,
	bool DirectHit,
	bool IsDamageEffect = false);
