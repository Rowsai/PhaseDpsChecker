namespace PhaseDpsChecker.Combat;

internal static class ActionGcdClassifier
{
	private const uint GlobalCooldownGroup = 58;

	public static (bool IsGcd, double DurationSeconds) Resolve(
		ActionKind kind,
		uint cooldownGroup,
		uint additionalCooldownGroup,
		int recast100ms)
	{
		bool supportsGcd = kind is ActionKind.Magic or ActionKind.WeaponSkill;
		bool usesPrimaryGcd = cooldownGroup == GlobalCooldownGroup;
		bool usesAdditionalGcd = additionalCooldownGroup == GlobalCooldownGroup;
		bool isGcd = supportsGcd && (usesPrimaryGcd || usesAdditionalGcd);
		if (!isGcd)
		{
			return (false, 0.0);
		}

		// Drill and Chainsaw keep their own recast in the primary group and the
		// shared GCD in the additional group. Do not count that long recast as uptime.
		double duration = usesPrimaryGcd && recast100ms > 0
			? recast100ms / 10.0
			: 2.5;
		return (true, duration);
	}
}
